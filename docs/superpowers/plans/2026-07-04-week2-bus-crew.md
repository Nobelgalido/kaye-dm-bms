# Week 2 — Bus & Crew Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement Week 2 scope from `docs/kaye-dm-agent-prompts-weeks-1-5.md` (WEEK 2 PROMPT): `BusCompany`/`BusTrip`/`CrewMealCredit` entities, three migrations (`AddBusCompanyAndTrip`, `AddCrewMealCredit`, `LinkOrderToBusTrip`), `IBusService`, crew-meal order creation on `IOrderService`, `/buses/companies`, `/buses/arrivals`, `/buses/report` pages, POS "assign to bus" + crew-meal mode, and 7-8 new xUnit tests covering the crew-meal allowance rule, ₱0 crew orders, credit/order atomicity, and the 45-minute arrival window query.

**Architecture:** Same 5-project layering as Week 1 (`KayeDM.Domain` ← `KayeDM.Application` ← `KayeDM.Infrastructure`, `KayeDM.Web` → Application + Infrastructure, `KayeDM.Tests` → all three). `IBusService`/`BusService` follow the exact same `IDbContextFactory<AppDbContext>` pattern already used by `MenuItemService`/`OrderService` (Blazor Server safety, per commit `bfa2c9b`). Crew-meal orders are created directly by `OrderService` (not delegated to `BusService`) so the `Order` + `OrderLine`s + `CrewMealCredit` are written in a single `SaveChangesAsync` call — true atomicity via one shared `DbContext`/transaction, achieved by setting `CrewMealCredit.Order` as a tracked navigation property (EF fixes up the FK after the Order's generated `Id` becomes available) rather than assigning `OrderId` manually after a separate save.

**Tech Stack:** Same as Week 1 — ASP.NET Core 8 Blazor Server, EF Core 8.0.11, SQL Server (LocalDB), xUnit + FluentAssertions + EF Core Sqlite (in-memory) for tests.

## Global Constraints

- Packages pinned to **8.0.11** for every `Microsoft.EntityFrameworkCore.*` / `Microsoft.AspNetCore.Identity.EntityFrameworkCore` package. Never upgrade to EF 9/10. `TargetFramework` stays `net8.0`.
- Migrations are sacred: one per schema change, descriptive names, never delete/regenerate/squash/edit an existing migration. A wrong migration gets a corrective migration, not an edit. This week's migration sequence is fixed: `AddBusCompanyAndTrip` → `AddCrewMealCredit` → `LinkOrderToBusTrip`, in that order, never combined.
- `dotnet ef` CLI calls always use `--project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj`.
- Layering: no EF Core types in `KayeDM.Domain`; `KayeDM.Web` talks to `KayeDM.Application` interfaces only, never to `AppDbContext` directly.
- No MediatR, AutoMapper, or repository wrappers. Plain services + constructor injection.
- Services take `IDbContextFactory<AppDbContext>` (not `AppDbContext` directly) and call `await using var db = await _dbContextFactory.CreateDbContextAsync();` per call — this is now the established pattern (see `MenuItemService`, `OrderService`), required for Blazor Server DbContext-per-operation safety.
- Prefer pure Blazor over JS interop. Currency format is always `"₱{0:N2}"`. File-scoped namespaces. Nullable reference types enabled everywhere.
- All commands below assume the working directory is `C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS` unless a step explicitly `cd`s elsewhere.
- Out of scope this week (do not build): inventory, waste, expenses, dashboard, closing, auth, seeding, charts, Docker, deployment, report export/print.

---

### Task 0: Domain layer — `CrewRole` enum, `BusCompany`/`BusTrip`/`CrewMealCredit` entities, wire `Order.BusTrip`

**Files:**
- Create: `src/KayeDM.Domain/Enums/CrewRole.cs`
- Create: `src/KayeDM.Domain/Entities/BusCompany.cs`
- Create: `src/KayeDM.Domain/Entities/BusTrip.cs`
- Create: `src/KayeDM.Domain/Entities/CrewMealCredit.cs`
- Modify: `src/KayeDM.Domain/Entities/Order.cs`

**Interfaces:**
- Produces: `KayeDM.Domain.Enums.CrewRole { Driver, Conductor, Assistant }`, `KayeDM.Domain.Entities.BusCompany`, `KayeDM.Domain.Entities.BusTrip`, `KayeDM.Domain.Entities.CrewMealCredit` — consumed by every later task. `Order.BusTrip` (nav property) — consumed by Task 3's Fluent config.

Plain data/enum types with no behavior to unit-test — write them directly.

- [ ] **Step 1: `CrewRole` enum**

`src/KayeDM.Domain/Enums/CrewRole.cs`:

```csharp
namespace KayeDM.Domain.Enums;

public enum CrewRole
{
    Driver,
    Conductor,
    Assistant
}
```

- [ ] **Step 2: `BusCompany` entity**

`src/KayeDM.Domain/Entities/BusCompany.cs`:

```csharp
namespace KayeDM.Domain.Entities;

public class BusCompany
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }

    // Free crew meals allowed per trip stop — the accounting obligation that
    // makes the bus company stop here.
    public int CrewMealAllowancePerTrip { get; set; }

    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 3: `BusTrip` entity**

`src/KayeDM.Domain/Entities/BusTrip.cs`:

```csharp
namespace KayeDM.Domain.Entities;

public class BusTrip
{
    public int Id { get; set; }
    public int BusCompanyId { get; set; }
    public BusCompany BusCompany { get; set; } = null!;
    public string BusNumber { get; set; } = string.Empty;
    public DateTime ArrivedAt { get; set; }
    public DateTime? DepartedAt { get; set; }
    public string Route { get; set; } = string.Empty;
    public int? EstimatedPassengers { get; set; }
}
```

- [ ] **Step 4: `CrewMealCredit` entity**

`src/KayeDM.Domain/Entities/CrewMealCredit.cs`:

```csharp
using KayeDM.Domain.Enums;

namespace KayeDM.Domain.Entities;

public class CrewMealCredit
{
    public int Id { get; set; }
    public int BusTripId { get; set; }
    public BusTrip BusTrip { get; set; } = null!;
    public CrewRole CrewRole { get; set; }
    public int OrderId { get; set; }

    // Tracked as a navigation (not just OrderId) so OrderService can add the
    // Order and this credit to the same DbContext and save once — EF fixes up
    // OrderId from the Order's generated Id in that single SaveChangesAsync,
    // making order + credit creation atomic.
    public Order Order { get; set; } = null!;

    public DateTime LoggedAt { get; set; }
}
```

- [ ] **Step 5: Wire `Order.BusTripId` to a real navigation**

In `src/KayeDM.Domain/Entities/Order.cs`, replace the `BusTripId` block:

```csharp
    // TODO Week 2: FK to BusTrip
    public int? BusTripId { get; set; }
```

with:

```csharp
    public int? BusTripId { get; set; }
    public BusTrip? BusTrip { get; set; }
```

- [ ] **Step 6: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(domain): add BusCompany, BusTrip, CrewMealCredit entities and CrewRole enum"
```

---

### Task 1: `AppDbContext` — BusCompany/BusTrip + `AddBusCompanyAndTrip` migration

**Files:**
- Modify: `src/KayeDM.Infrastructure/Data/AppDbContext.cs`
- Create (generated): `src/KayeDM.Infrastructure/Data/Migrations/*_AddBusCompanyAndTrip.cs`

**Interfaces:**
- Consumes: `BusCompany`, `BusTrip` (Task 0).
- Produces: `AppDbContext.BusCompanies` (`DbSet<BusCompany>`), `AppDbContext.BusTrips` (`DbSet<BusTrip>`) — consumed by `BusService` (Task 6) and `OrderService` (Task 7).

- [ ] **Step 1: Add the DbSets and Fluent config**

In `src/KayeDM.Infrastructure/Data/AppDbContext.cs`, add two DbSets after the existing ones:

```csharp
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<BusCompany> BusCompanies => Set<BusCompany>();
    public DbSet<BusTrip> BusTrips => Set<BusTrip>();
```

and add Fluent config inside `OnModelCreating`, after the `OrderLine` block:

```csharp
        builder.Entity<BusCompany>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(150).IsRequired();
            entity.Property(c => c.ContactPerson).HasMaxLength(150);
        });

        builder.Entity<BusTrip>(entity =>
        {
            entity.Property(t => t.BusNumber).HasMaxLength(50).IsRequired();
            entity.Property(t => t.Route).HasMaxLength(200).IsRequired();

            entity.HasOne(t => t.BusCompany)
                .WithMany()
                .HasForeignKey(t => t.BusCompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 3: Generate the `AddBusCompanyAndTrip` migration**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations add AddBusCompanyAndTrip --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj --output-dir Data/Migrations
```

Expected: `Done.` and a new migration file alongside the existing ones.

- [ ] **Step 4: Apply it**

```bash
dotnet ef database update --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected: `Done.` Verify with:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT name FROM sys.tables WHERE name IN ('BusCompanies','BusTrips');"
```

Expected: both table names returned.

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): add BusCompany/BusTrip to AppDbContext and AddBusCompanyAndTrip migration"
```

---

### Task 2: `AppDbContext` — CrewMealCredit + `AddCrewMealCredit` migration

**Files:**
- Modify: `src/KayeDM.Infrastructure/Data/AppDbContext.cs`
- Create (generated): `src/KayeDM.Infrastructure/Data/Migrations/*_AddCrewMealCredit.cs`

**Interfaces:**
- Consumes: `CrewMealCredit` (Task 0); `BusTrips`, `Orders` (Tasks 1, existing).
- Produces: `AppDbContext.CrewMealCredits` (`DbSet<CrewMealCredit>`) — consumed by `BusService` (Task 6) and `OrderService` (Task 7).

- [ ] **Step 1: Add the DbSet and Fluent config**

In `src/KayeDM.Infrastructure/Data/AppDbContext.cs`, add the DbSet:

```csharp
    public DbSet<CrewMealCredit> CrewMealCredits => Set<CrewMealCredit>();
```

and Fluent config inside `OnModelCreating`, after the `BusTrip` block:

```csharp
        builder.Entity<CrewMealCredit>(entity =>
        {
            entity.HasOne(c => c.BusTrip)
                .WithMany()
                .HasForeignKey(c => c.BusTripId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Order)
                .WithMany()
                .HasForeignKey(c => c.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // One credit per ₱0 crew-meal order — enforced at the DB level too.
            entity.HasIndex(c => c.OrderId).IsUnique();
        });
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 3: Generate the `AddCrewMealCredit` migration**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations add AddCrewMealCredit --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj --output-dir Data/Migrations
```

Expected: `Done.`

- [ ] **Step 4: Apply it**

```bash
dotnet ef database update --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected: `Done.` Verify with:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT name FROM sys.tables WHERE name = 'CrewMealCredits';"
```

Expected: `CrewMealCredits` returned.

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): add CrewMealCredit to AppDbContext and AddCrewMealCredit migration"
```

---

### Task 3: `AppDbContext` — real FK from `Order.BusTripId` + `LinkOrderToBusTrip` migration

**Files:**
- Modify: `src/KayeDM.Infrastructure/Data/AppDbContext.cs`
- Create (generated): `src/KayeDM.Infrastructure/Data/Migrations/*_LinkOrderToBusTrip.cs`

**Interfaces:**
- Consumes: `Order.BusTrip` (Task 0), `BusTrips` (Task 1).
- Produces: FK constraint `Orders.BusTripId → BusTrips.Id` — no new C# surface, but later tasks (7, 10) can now rely on `Order.BusTripId` being referentially valid.

- [ ] **Step 1: Add the Fluent config**

In `src/KayeDM.Infrastructure/Data/AppDbContext.cs`, inside the existing `builder.Entity<Order>(entity => { ... })` block, add after the `HasMany(o => o.Lines)...` call:

```csharp
            entity.HasOne(o => o.BusTrip)
                .WithMany()
                .HasForeignKey(o => o.BusTripId)
                .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 3: Generate the `LinkOrderToBusTrip` migration**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations add LinkOrderToBusTrip --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj --output-dir Data/Migrations
```

Expected: `Done.` The generated migration should contain an `AddForeignKey` (and likely `CreateIndex`) on `Orders.BusTripId` referencing `BusTrips` — no new/dropped columns, since `BusTripId` already exists from `AddOrderTables`. If the diff shows anything else (e.g. a dropped/recreated column), stop and inspect before applying — that would mean the model changed in an unexpected way.

- [ ] **Step 4: Apply it**

```bash
dotnet ef database update --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected: `Done.` Verify with:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT name FROM sys.foreign_keys WHERE name LIKE 'FK_Orders_BusTrips%';"
```

Expected: one FK name returned.

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): convert Order.BusTripId into a real FK via LinkOrderToBusTrip migration"
```

---

### Task 4: Application layer — Bus DTOs and `IBusService`

**Files:**
- Create: `src/KayeDM.Application/Buses/BusModels.cs`
- Create: `src/KayeDM.Application/Buses/IBusService.cs`

**Interfaces:**
- Consumes: nothing from Domain directly (plain contracts).
- Produces: `BusCompanyDto(int Id, string Name, string? ContactPerson, int CrewMealAllowancePerTrip, bool IsActive)`, `BusCompanyUpsertDto(string Name, string? ContactPerson, int CrewMealAllowancePerTrip)`, `LogArrivalRequest(int BusCompanyId, string BusNumber, string Route, int? EstimatedPassengers)`, `BusTripDto(int Id, int BusCompanyId, string BusCompanyName, string BusNumber, DateTime ArrivedAt, DateTime? DepartedAt, string Route, int? EstimatedPassengers)`, `BusTripBoardRow(BusTripDto Trip, int MealsUsed, int AllowanceRemaining)`, `CrewMealReportRow(...)`, `CrewMealReportResult(...)`, `IBusService` — all consumed by Tasks 6, 8, 9, 10, 11.

Plain contracts, no behavior to test.

- [ ] **Step 1: DTOs**

`src/KayeDM.Application/Buses/BusModels.cs`:

```csharp
namespace KayeDM.Application.Buses;

public record BusCompanyDto(int Id, string Name, string? ContactPerson, int CrewMealAllowancePerTrip, bool IsActive);

public record BusCompanyUpsertDto(string Name, string? ContactPerson, int CrewMealAllowancePerTrip);

public record LogArrivalRequest(int BusCompanyId, string BusNumber, string Route, int? EstimatedPassengers);

public record BusTripDto(
    int Id,
    int BusCompanyId,
    string BusCompanyName,
    string BusNumber,
    DateTime ArrivedAt,
    DateTime? DepartedAt,
    string Route,
    int? EstimatedPassengers);

public record BusTripBoardRow(BusTripDto Trip, int MealsUsed, int AllowanceRemaining);

public record CrewMealReportRow(
    DateTime ArrivedAt,
    string BusNumber,
    string Route,
    int DriverCredits,
    int ConductorCredits,
    int AssistantCredits,
    int TotalCredits);

public record CrewMealReportResult(
    int CompanyId,
    string CompanyName,
    int Year,
    int Month,
    IReadOnlyList<CrewMealReportRow> Trips,
    int TotalTrips,
    int TotalMeals,
    int DriverMealsTotal,
    int ConductorMealsTotal,
    int AssistantMealsTotal);
```

- [ ] **Step 2: `IBusService`**

`src/KayeDM.Application/Buses/IBusService.cs`:

```csharp
namespace KayeDM.Application.Buses;

public interface IBusService
{
    Task<List<BusCompanyDto>> GetCompaniesAsync(bool includeInactive = false);
    Task<BusCompanyDto> CreateCompanyAsync(BusCompanyUpsertDto dto);
    Task<BusCompanyDto> UpdateCompanyAsync(int id, BusCompanyUpsertDto dto);
    Task SetCompanyActiveAsync(int id, bool isActive);

    Task<BusTripDto> LogArrivalAsync(LogArrivalRequest request);
    Task DepartAsync(int tripId);
    Task<List<BusTripBoardRow>> GetTodaysTripBoardAsync();

    // Trips arrived within the given window and not yet departed — feeds the
    // POS "assign to bus" dropdown (blueprint Module A: last 45 minutes).
    Task<List<BusTripDto>> GetRecentArrivalsAsync(TimeSpan window);

    Task<int> GetAllowanceRemainingAsync(int tripId);
    Task<CrewMealReportResult> GetMonthlyReportAsync(int companyId, int year, int month);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(application): add Bus DTOs and IBusService interface"
```

---

### Task 5: Application layer — crew-meal order request + `IOrderService` extension

**Files:**
- Modify: `src/KayeDM.Application/Orders/OrderModels.cs`
- Modify: `src/KayeDM.Application/Orders/IOrderService.cs`

**Interfaces:**
- Consumes: `CrewRole` (Task 0); `OrderLineRequest`, `OrderResult` (existing).
- Produces: `CreateOrderRequest` gains an optional trailing `int? BusTripId = null` parameter (existing call sites unaffected — positional records allow omitting trailing optional parameters). New `CreateCrewMealOrderRequest(IReadOnlyList<OrderLineRequest> Lines, int BusTripId, CrewRole CrewRole, string? CashierId)`. `IOrderService.CreateCrewMealOrderAsync(CreateCrewMealOrderRequest request)` — consumed by Task 7 (implementation) and Task 10 (POS crew-meal mode).

- [ ] **Step 1: Extend `CreateOrderRequest`, add `CreateCrewMealOrderRequest`**

In `src/KayeDM.Application/Orders/OrderModels.cs`, add the `using` and change/add these records:

```csharp
using KayeDM.Domain.Enums;

namespace KayeDM.Application.Orders;

public record OrderLineRequest(int MenuItemId, int Quantity);

public record CreateOrderRequest(
    IReadOnlyList<OrderLineRequest> Lines,
    PaymentMethod PaymentMethod,
    decimal AmountTendered,
    string? CashierId,
    int? BusTripId = null);

public record CreateCrewMealOrderRequest(
    IReadOnlyList<OrderLineRequest> Lines,
    int BusTripId,
    CrewRole CrewRole,
    string? CashierId);

public record OrderLineResult(int MenuItemId, string MenuItemName, int Quantity, decimal UnitPriceAtSale, decimal LineTotal);

public record OrderResult(
    int Id,
    string OrderNumber,
    DateTime CreatedAt,
    decimal Total,
    decimal AmountTendered,
    decimal ChangeGiven,
    IReadOnlyList<OrderLineResult> Lines);
```

- [ ] **Step 2: Extend `IOrderService`**

`src/KayeDM.Application/Orders/IOrderService.cs`:

```csharp
namespace KayeDM.Application.Orders;

public interface IOrderService
{
    Task<OrderResult> CreateOrderAsync(CreateOrderRequest request);
    Task<OrderResult> CreateCrewMealOrderAsync(CreateCrewMealOrderRequest request);
    Task VoidOrderAsync(int orderId, string reason);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: Build FAILS — `KayeDM.Infrastructure.Orders.OrderService` no longer implements `IOrderService` (missing `CreateCrewMealOrderAsync`). This is expected; Task 7 fixes it. Confirm the error mentions `OrderService` before moving on.

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(application): extend CreateOrderRequest with BusTripId and add crew-meal order contract"
```

---

### Task 6: `BusService` implementation (TDD on the two query methods) + DI registration

**Files:**
- Create: `src/KayeDM.Infrastructure/Buses/BusService.cs`
- Create: `tests/KayeDM.Tests/Buses/BusServiceTests.cs`
- Modify: `src/KayeDM.Web/Program.cs`

**Interfaces:**
- Consumes: `IBusService` and all Bus DTOs (Task 4); `AppDbContext`, `BusCompany`, `BusTrip`, `CrewMealCredit` (Tasks 0–2); `DomainException` (existing).
- Produces: `KayeDM.Infrastructure.Buses.BusService : IBusService` — consumed by the `/buses/*` pages (Tasks 8, 9, 11) via DI.

Company CRUD and arrival logging are plain CRUD (same shape as `MenuItemService` — no dedicated tests, per Week 1 precedent). `GetRecentArrivalsAsync` (45-minute window) and `GetAllowanceRemainingAsync` carry real query logic the Week 2 prompt calls out explicitly, so they're built test-first.

- [ ] **Step 1: Write the full service, with the two tested methods stubbed**

`src/KayeDM.Infrastructure/Buses/BusService.cs`:

```csharp
using KayeDM.Application.Buses;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Buses;

public class BusService : IBusService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public BusService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<BusCompanyDto>> GetCompaniesAsync(bool includeInactive = false)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var query = db.BusCompanies.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new BusCompanyDto(c.Id, c.Name, c.ContactPerson, c.CrewMealAllowancePerTrip, c.IsActive))
            .ToListAsync();
    }

    public async Task<BusCompanyDto> CreateCompanyAsync(BusCompanyUpsertDto dto)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = new BusCompany
        {
            Name = dto.Name,
            ContactPerson = dto.ContactPerson,
            CrewMealAllowancePerTrip = dto.CrewMealAllowancePerTrip,
            IsActive = true
        };

        db.BusCompanies.Add(entity);
        await db.SaveChangesAsync();

        return new BusCompanyDto(entity.Id, entity.Name, entity.ContactPerson, entity.CrewMealAllowancePerTrip, entity.IsActive);
    }

    public async Task<BusCompanyDto> UpdateCompanyAsync(int id, BusCompanyUpsertDto dto)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.BusCompanies.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new DomainException($"Bus company {id} not found.");

        entity.Name = dto.Name;
        entity.ContactPerson = dto.ContactPerson;
        entity.CrewMealAllowancePerTrip = dto.CrewMealAllowancePerTrip;

        await db.SaveChangesAsync();

        return new BusCompanyDto(entity.Id, entity.Name, entity.ContactPerson, entity.CrewMealAllowancePerTrip, entity.IsActive);
    }

    public async Task SetCompanyActiveAsync(int id, bool isActive)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.BusCompanies.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new DomainException($"Bus company {id} not found.");

        entity.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    public async Task<BusTripDto> LogArrivalAsync(LogArrivalRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var company = await db.BusCompanies.FirstOrDefaultAsync(c => c.Id == request.BusCompanyId)
            ?? throw new DomainException($"Bus company {request.BusCompanyId} not found.");

        if (string.IsNullOrWhiteSpace(request.BusNumber))
        {
            throw new DomainException("Bus number is required.");
        }

        var trip = new BusTrip
        {
            BusCompanyId = request.BusCompanyId,
            BusNumber = request.BusNumber,
            Route = request.Route,
            EstimatedPassengers = request.EstimatedPassengers,
            ArrivedAt = DateTime.Now
        };

        db.BusTrips.Add(trip);
        await db.SaveChangesAsync();

        return new BusTripDto(trip.Id, company.Id, company.Name, trip.BusNumber, trip.ArrivedAt, trip.DepartedAt, trip.Route, trip.EstimatedPassengers);
    }

    public async Task DepartAsync(int tripId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var trip = await db.BusTrips.FirstOrDefaultAsync(t => t.Id == tripId)
            ?? throw new DomainException($"Bus trip {tripId} not found.");

        trip.DepartedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }

    public async Task<List<BusTripBoardRow>> GetTodaysTripBoardAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var today = DateTime.Now.Date;
        var trips = await db.BusTrips
            .AsNoTracking()
            .Include(t => t.BusCompany)
            .Where(t => t.ArrivedAt >= today && t.ArrivedAt < today.AddDays(1))
            .OrderByDescending(t => t.ArrivedAt)
            .ToListAsync();

        var board = new List<BusTripBoardRow>();
        foreach (var trip in trips)
        {
            var mealsUsed = await db.CrewMealCredits.CountAsync(c => c.BusTripId == trip.Id);
            var dto = new BusTripDto(trip.Id, trip.BusCompanyId, trip.BusCompany.Name, trip.BusNumber, trip.ArrivedAt, trip.DepartedAt, trip.Route, trip.EstimatedPassengers);
            board.Add(new BusTripBoardRow(dto, mealsUsed, trip.BusCompany.CrewMealAllowancePerTrip - mealsUsed));
        }

        return board;
    }

    public Task<List<BusTripDto>> GetRecentArrivalsAsync(TimeSpan window) => throw new NotImplementedException();

    public Task<int> GetAllowanceRemainingAsync(int tripId) => throw new NotImplementedException();

    public async Task<CrewMealReportResult> GetMonthlyReportAsync(int companyId, int year, int month)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var company = await db.BusCompanies.FirstOrDefaultAsync(c => c.Id == companyId)
            ?? throw new DomainException($"Bus company {companyId} not found.");

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var trips = await db.BusTrips
            .AsNoTracking()
            .Where(t => t.BusCompanyId == companyId && t.ArrivedAt >= monthStart && t.ArrivedAt < monthEnd)
            .OrderBy(t => t.ArrivedAt)
            .ToListAsync();

        var rows = new List<CrewMealReportRow>();
        foreach (var trip in trips)
        {
            var credits = await db.CrewMealCredits.Where(c => c.BusTripId == trip.Id).ToListAsync();
            var driverCount = credits.Count(c => c.CrewRole == CrewRole.Driver);
            var conductorCount = credits.Count(c => c.CrewRole == CrewRole.Conductor);
            var assistantCount = credits.Count(c => c.CrewRole == CrewRole.Assistant);
            rows.Add(new CrewMealReportRow(trip.ArrivedAt, trip.BusNumber, trip.Route, driverCount, conductorCount, assistantCount, driverCount + conductorCount + assistantCount));
        }

        return new CrewMealReportResult(
            company.Id,
            company.Name,
            year,
            month,
            rows,
            TotalTrips: rows.Count,
            TotalMeals: rows.Sum(r => r.TotalCredits),
            DriverMealsTotal: rows.Sum(r => r.DriverCredits),
            ConductorMealsTotal: rows.Sum(r => r.ConductorCredits),
            AssistantMealsTotal: rows.Sum(r => r.AssistantCredits));
    }
}
```

- [ ] **Step 2: Write the failing tests for the two stubbed methods**

`tests/KayeDM.Tests/Buses/BusServiceTests.cs`:

```csharp
using FluentAssertions;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Buses;
using KayeDM.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Buses;

public class BusServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly BusService _sut;

    public BusServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new BusService(new TestDbContextFactory(options));

        _db.BusCompanies.Add(new BusCompany { Id = 1, Name = "DLTB", ContactPerson = "Juan", CrewMealAllowancePerTrip = 3, IsActive = true });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options)
        {
            _options = options;
        }

        public AppDbContext CreateDbContext() => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AppDbContext(_options));
    }

    [Fact]
    public async Task GetRecentArrivalsAsync_ExcludesTripsOutsideWindow()
    {
        _db.BusTrips.AddRange(
            new BusTrip { Id = 1, BusCompanyId = 1, BusNumber = "8112", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now.AddMinutes(-10) },
            new BusTrip { Id = 2, BusCompanyId = 1, BusNumber = "9001", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now.AddMinutes(-60) });
        _db.SaveChanges();

        var result = await _sut.GetRecentArrivalsAsync(TimeSpan.FromMinutes(45));

        result.Should().ContainSingle(t => t.Id == 1);
    }

    [Fact]
    public async Task GetRecentArrivalsAsync_ExcludesDepartedTrips()
    {
        _db.BusTrips.Add(new BusTrip
        {
            Id = 1,
            BusCompanyId = 1,
            BusNumber = "8112",
            Route = "Manila-Sorsogon",
            ArrivedAt = DateTime.Now.AddMinutes(-10),
            DepartedAt = DateTime.Now.AddMinutes(-2)
        });
        _db.SaveChanges();

        var result = await _sut.GetRecentArrivalsAsync(TimeSpan.FromMinutes(45));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllowanceRemainingAsync_SubtractsCreditsUsed_FromAllowance()
    {
        _db.BusTrips.Add(new BusTrip { Id = 1, BusCompanyId = 1, BusNumber = "8112", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now });
        _db.SaveChanges();

        var order = new Order
        {
            OrderNumber = "20260704-001",
            CreatedAt = DateTime.Now,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            IsCrewMeal = true,
            BusTripId = 1
        };
        _db.Orders.Add(order);
        _db.SaveChanges();

        _db.CrewMealCredits.Add(new CrewMealCredit { BusTripId = 1, CrewRole = CrewRole.Driver, Order = order, LoggedAt = DateTime.Now });
        _db.SaveChanges();

        var remaining = await _sut.GetAllowanceRemainingAsync(1);

        remaining.Should().Be(2);
    }
}
```

- [ ] **Step 3: Run the tests to confirm they fail**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~BusServiceTests"`
Expected: 3 tests run, all FAIL with `System.NotImplementedException`.

- [ ] **Step 4: Implement the two methods for real**

Replace the two stub lines in `src/KayeDM.Infrastructure/Buses/BusService.cs`:

```csharp
    public Task<List<BusTripDto>> GetRecentArrivalsAsync(TimeSpan window) => throw new NotImplementedException();

    public Task<int> GetAllowanceRemainingAsync(int tripId) => throw new NotImplementedException();
```

with:

```csharp
    public async Task<List<BusTripDto>> GetRecentArrivalsAsync(TimeSpan window)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var cutoff = DateTime.Now - window;

        return await db.BusTrips
            .AsNoTracking()
            .Include(t => t.BusCompany)
            .Where(t => t.ArrivedAt >= cutoff && t.DepartedAt == null)
            .OrderByDescending(t => t.ArrivedAt)
            .Select(t => new BusTripDto(t.Id, t.BusCompanyId, t.BusCompany.Name, t.BusNumber, t.ArrivedAt, t.DepartedAt, t.Route, t.EstimatedPassengers))
            .ToListAsync();
    }

    public async Task<int> GetAllowanceRemainingAsync(int tripId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var trip = await db.BusTrips.Include(t => t.BusCompany).FirstOrDefaultAsync(t => t.Id == tripId)
            ?? throw new DomainException($"Bus trip {tripId} not found.");

        var used = await db.CrewMealCredits.CountAsync(c => c.BusTripId == tripId);
        return trip.BusCompany.CrewMealAllowancePerTrip - used;
    }
```

- [ ] **Step 5: Run the tests again to confirm they pass**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~BusServiceTests"`
Expected: 3 tests run, all PASS.

- [ ] **Step 6: Register `BusService` in DI**

In `src/KayeDM.Web/Program.cs`, add the using and registration:

```csharp
using KayeDM.Application.Buses;
using KayeDM.Application.Menu;
using KayeDM.Application.Orders;
using KayeDM.Infrastructure.Buses;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Menu;
using KayeDM.Infrastructure.Orders;
using KayeDM.Web.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("KayeDmBms")));

builder.Services.AddScoped<IMenuItemService, MenuItemService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IBusService, BusService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
```

(Leave the rest of `Program.cs` unchanged — the build still fails here because `OrderService` doesn't implement `IOrderService.CreateCrewMealOrderAsync` yet; that's fixed in Task 7.)

- [ ] **Step 7: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): implement BusService with tests for arrival window and allowance remaining"
```

---

### Task 7: `OrderService` extension — bus-tagged orders + crew-meal orders (TDD)

**Files:**
- Modify: `src/KayeDM.Infrastructure/Orders/OrderService.cs`
- Modify: `tests/KayeDM.Tests/Orders/OrderServiceTests.cs`
- Create: `tests/KayeDM.Tests/Orders/CrewMealOrderServiceTests.cs`

**Interfaces:**
- Consumes: `CreateCrewMealOrderRequest`, extended `CreateOrderRequest` (Task 5); `BusTrip`, `BusCompany`, `CrewMealCredit` (Tasks 0–2); `CrewRole` (Task 0).
- Produces: `OrderService.CreateCrewMealOrderAsync` — consumed by the `/pos` page (Task 10). `OrderService.CreateOrderAsync` now also persists `BusTripId` when supplied.

This is where the crew-meal allowance rule and atomicity live — build the crew-meal path test-first.

- [ ] **Step 1: Update `CreateOrderAsync` to persist `BusTripId`, stub `CreateCrewMealOrderAsync`**

In `src/KayeDM.Infrastructure/Orders/OrderService.cs`, change the `order` construction inside `CreateOrderAsync`:

```csharp
        var order = new Order
        {
            OrderNumber = await NextOrderNumberAsync(db),
            CreatedAt = DateTime.Now,
            CashierId = request.CashierId,
            Status = OrderStatus.Completed,
            PaymentMethod = request.PaymentMethod,
            BusTripId = request.BusTripId
        };
```

Then add the stub method to the class (anywhere after `CreateOrderAsync`):

```csharp
    public Task<OrderResult> CreateCrewMealOrderAsync(CreateCrewMealOrderRequest request) => throw new NotImplementedException();
```

- [ ] **Step 2: Add a `using` for `KayeDM.Domain.Enums`**

Confirm `src/KayeDM.Infrastructure/Orders/OrderService.cs` already has `using KayeDM.Domain.Enums;` at the top (it does, from Week 1 — `OrderStatus`/`PaymentMethod` live there, and `CrewRole` joins them with no new using needed).

- [ ] **Step 3: Add the bus-tag test to the existing `OrderServiceTests.cs`**

In `tests/KayeDM.Tests/Orders/OrderServiceTests.cs`, add this test at the end of the class, before the final closing brace:

```csharp
    [Fact]
    public async Task CreateOrderAsync_SetsBusTripId_WhenProvided()
    {
        _db.BusCompanies.Add(new BusCompany { Id = 1, Name = "DLTB", CrewMealAllowancePerTrip = 2, IsActive = true });
        _db.BusTrips.Add(new BusTrip { Id = 1, BusCompanyId = 1, BusNumber = "8112", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now });
        _db.SaveChanges();

        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 1) }, PaymentMethod.Cash, 100m, null, BusTripId: 1);

        var result = await _sut.CreateOrderAsync(request);

        var order = await _db.Orders.FindAsync(result.Id);
        order!.BusTripId.Should().Be(1);
    }
```

- [ ] **Step 4: Write the failing crew-meal tests**

`tests/KayeDM.Tests/Orders/CrewMealOrderServiceTests.cs`:

```csharp
using FluentAssertions;
using KayeDM.Application.Orders;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Orders;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Orders;

public class CrewMealOrderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly OrderService _sut;

    public CrewMealOrderServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new OrderService(new TestDbContextFactory(options));

        _db.MenuItems.Add(new MenuItem { Id = 1, Name = "Adobo", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 1 });
        _db.BusCompanies.Add(new BusCompany { Id = 1, Name = "DLTB", CrewMealAllowancePerTrip = 2, IsActive = true });
        _db.BusTrips.Add(new BusTrip { Id = 1, BusCompanyId = 1, BusNumber = "8112", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options)
        {
            _options = options;
        }

        public AppDbContext CreateDbContext() => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AppDbContext(_options));
    }

    [Fact]
    public async Task CreateCrewMealOrderAsync_ForcesTotalAndTenderedToZero()
    {
        var request = new CreateCrewMealOrderRequest(new[] { new OrderLineRequest(1, 2) }, BusTripId: 1, CrewRole: CrewRole.Driver, CashierId: null);

        var result = await _sut.CreateCrewMealOrderAsync(request);

        result.Total.Should().Be(0m);
        result.AmountTendered.Should().Be(0m);
        result.ChangeGiven.Should().Be(0m);
    }

    [Fact]
    public async Task CreateCrewMealOrderAsync_CreatesExactlyOneCrewMealCreditLinkedToOrder()
    {
        var request = new CreateCrewMealOrderRequest(new[] { new OrderLineRequest(1, 1) }, BusTripId: 1, CrewRole: CrewRole.Conductor, CashierId: null);

        var result = await _sut.CreateCrewMealOrderAsync(request);

        var credits = await _db.CrewMealCredits.Where(c => c.OrderId == result.Id).ToListAsync();
        credits.Should().ContainSingle();
        credits[0].CrewRole.Should().Be(CrewRole.Conductor);
        credits[0].BusTripId.Should().Be(1);

        var order = await _db.Orders.FindAsync(result.Id);
        order!.IsCrewMeal.Should().BeTrue();
    }

    [Fact]
    public async Task CreateCrewMealOrderAsync_Succeeds_AtExactAllowanceLimit()
    {
        var request = new CreateCrewMealOrderRequest(new[] { new OrderLineRequest(1, 1) }, BusTripId: 1, CrewRole: CrewRole.Driver, CashierId: null);

        // Allowance is 2 — both of the first two crew meal orders for this trip must succeed.
        await _sut.CreateCrewMealOrderAsync(request);
        var act = async () => await _sut.CreateCrewMealOrderAsync(request);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateCrewMealOrderAsync_Throws_WhenAllowanceExceeded()
    {
        var request = new CreateCrewMealOrderRequest(new[] { new OrderLineRequest(1, 1) }, BusTripId: 1, CrewRole: CrewRole.Driver, CashierId: null);

        // Allowance is 2 — the third crew meal order for this trip must be rejected.
        await _sut.CreateCrewMealOrderAsync(request);
        await _sut.CreateCrewMealOrderAsync(request);
        var act = async () => await _sut.CreateCrewMealOrderAsync(request);

        await act.Should().ThrowAsync<DomainException>();
    }
}
```

- [ ] **Step 5: Run the new tests to confirm they fail**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~CrewMealOrderServiceTests|FullyQualifiedName~CreateOrderAsync_SetsBusTripId_WhenProvided"`
Expected: `CreateOrderAsync_SetsBusTripId_WhenProvided` PASSES already (the field is just being set); all 4 `CrewMealOrderServiceTests` FAIL with `System.NotImplementedException`.

- [ ] **Step 6: Implement `CreateCrewMealOrderAsync` for real**

Replace the stub line in `src/KayeDM.Infrastructure/Orders/OrderService.cs`:

```csharp
    public Task<OrderResult> CreateCrewMealOrderAsync(CreateCrewMealOrderRequest request) => throw new NotImplementedException();
```

with:

```csharp
    public async Task<OrderResult> CreateCrewMealOrderAsync(CreateCrewMealOrderRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new DomainException("A crew meal order must have at least one line.");
        }

        var trip = await db.BusTrips.Include(t => t.BusCompany).FirstOrDefaultAsync(t => t.Id == request.BusTripId)
            ?? throw new DomainException($"Bus trip {request.BusTripId} not found.");

        var creditsUsed = await db.CrewMealCredits.CountAsync(c => c.BusTripId == request.BusTripId);
        if (creditsUsed >= trip.BusCompany.CrewMealAllowancePerTrip)
        {
            throw new DomainException(
                $"Crew meal allowance exceeded for this trip ({creditsUsed} of {trip.BusCompany.CrewMealAllowancePerTrip} used).");
        }

        var menuItemIds = request.Lines.Select(l => l.MenuItemId).Distinct().ToList();
        var menuItems = await db.MenuItems
            .Where(m => menuItemIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id);

        var order = new Order
        {
            OrderNumber = await NextOrderNumberAsync(db),
            CreatedAt = DateTime.Now,
            CashierId = request.CashierId,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            BusTripId = request.BusTripId,
            IsCrewMeal = true,
            AmountTendered = 0m,
            ChangeGiven = 0m
        };

        var lineResults = new List<OrderLineResult>();

        foreach (var lineRequest in request.Lines)
        {
            if (!menuItems.TryGetValue(lineRequest.MenuItemId, out var menuItem))
            {
                throw new DomainException($"Menu item {lineRequest.MenuItemId} not found.");
            }

            if (lineRequest.Quantity <= 0)
            {
                throw new DomainException("Line quantity must be positive.");
            }

            // Crew meals are free — the line is recorded for stock/traceability but
            // priced at zero so the order total is always ₱0.
            order.Lines.Add(new OrderLine
            {
                MenuItemId = menuItem.Id,
                Quantity = lineRequest.Quantity,
                UnitPriceAtSale = 0m
            });

            lineResults.Add(new OrderLineResult(menuItem.Id, menuItem.Name, lineRequest.Quantity, 0m, 0m));
        }

        // Order and credit are added to the same context and saved once: EF fixes up
        // CrewMealCredit.OrderId from Order.Id during this single SaveChangesAsync,
        // making the two inserts atomic.
        db.Orders.Add(order);
        db.CrewMealCredits.Add(new CrewMealCredit
        {
            BusTripId = request.BusTripId,
            CrewRole = request.CrewRole,
            Order = order,
            LoggedAt = DateTime.Now
        });

        await db.SaveChangesAsync();

        return new OrderResult(order.Id, order.OrderNumber, order.CreatedAt, 0m, 0m, 0m, lineResults);
    }
```

- [ ] **Step 7: Run the full test suite to confirm everything passes**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"`
Expected: `Passed! - Failed: 0`, total test count = 7 (Week 1) + 3 (`BusServiceTests`) + 1 (`CreateOrderAsync_SetsBusTripId_WhenProvided`) + 4 (`CrewMealOrderServiceTests`) = 15.

- [ ] **Step 8: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 9: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): implement crew-meal order creation with allowance enforcement and atomic credit"
```

---

### Task 8: `/buses/companies` page

**Files:**
- Create: `src/KayeDM.Web/Components/Pages/BusCompanies.razor`
- Modify: `src/KayeDM.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `IBusService`, `BusCompanyDto`, `BusCompanyUpsertDto` (Task 4, registered in DI by Task 6).

- [ ] **Step 1: Write the page**

`src/KayeDM.Web/Components/Pages/BusCompanies.razor`:

```razor
@page "/buses/companies"
@using System.ComponentModel.DataAnnotations
@using KayeDM.Application.Buses
@inject IBusService BusService

<PageTitle>Bus Companies</PageTitle>

<h1>Bus Companies</h1>

<button class="btn btn-primary mb-3" @onclick="StartCreate">+ Add Company</button>

@if (_companies is null)
{
    <p>Loading…</p>
}
else if (_companies.Count == 0)
{
    <p>No bus companies yet.</p>
}
else
{
    <table class="table">
        <thead>
            <tr>
                <th>Name</th>
                <th>Contact Person</th>
                <th>Meal Allowance / Trip</th>
                <th>Status</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var company in _companies)
            {
                <tr>
                    <td>@company.Name</td>
                    <td>@company.ContactPerson</td>
                    <td>@company.CrewMealAllowancePerTrip</td>
                    <td>@(company.IsActive ? "Active" : "Inactive")</td>
                    <td>
                        <button class="btn btn-sm btn-secondary" @onclick="() => StartEdit(company)">Edit</button>
                        <button class="btn btn-sm btn-outline-danger" @onclick="() => ToggleActiveAsync(company)">
                            @(company.IsActive ? "Deactivate" : "Activate")
                        </button>
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

@if (_editing)
{
    <EditForm Model="_form" OnValidSubmit="SaveAsync">
        <DataAnnotationsValidator />
        <ValidationSummary />
        <div class="mb-2">
            <label>Name</label>
            <InputText class="form-control" @bind-Value="_form.Name" />
        </div>
        <div class="mb-2">
            <label>Contact Person</label>
            <InputText class="form-control" @bind-Value="_form.ContactPerson" />
        </div>
        <div class="mb-2">
            <label>Crew Meal Allowance per Trip</label>
            <InputNumber class="form-control" @bind-Value="_form.CrewMealAllowancePerTrip" />
        </div>
        <button type="submit" class="btn btn-success">Save</button>
        <button type="button" class="btn btn-link" @onclick="CancelEdit">Cancel</button>
    </EditForm>
}

@code {
    private List<BusCompanyDto>? _companies;
    private bool _editing;
    private int _editingId;
    private EditModel _form = new();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync() => _companies = await BusService.GetCompaniesAsync(includeInactive: true);

    private void StartCreate()
    {
        _editingId = 0;
        _form = new EditModel();
        _editing = true;
    }

    private void StartEdit(BusCompanyDto company)
    {
        _editingId = company.Id;
        _form = new EditModel
        {
            Name = company.Name,
            ContactPerson = company.ContactPerson,
            CrewMealAllowancePerTrip = company.CrewMealAllowancePerTrip
        };
        _editing = true;
    }

    private void CancelEdit() => _editing = false;

    private async Task SaveAsync()
    {
        var dto = new BusCompanyUpsertDto(_form.Name, _form.ContactPerson, _form.CrewMealAllowancePerTrip);
        if (_editingId > 0)
        {
            await BusService.UpdateCompanyAsync(_editingId, dto);
        }
        else
        {
            await BusService.CreateCompanyAsync(dto);
        }

        _editing = false;
        await LoadAsync();
    }

    private async Task ToggleActiveAsync(BusCompanyDto company)
    {
        await BusService.SetCompanyActiveAsync(company.Id, !company.IsActive);
        await LoadAsync();
    }

    private class EditModel
    {
        [Required]
        public string Name { get; set; } = "";

        public string? ContactPerson { get; set; }

        [Range(0, 20)]
        public int CrewMealAllowancePerTrip { get; set; } = 2;
    }
}
```

- [ ] **Step 2: Add the nav link**

In `src/KayeDM.Web/Components/Layout/NavMenu.razor`, add after the `menu` nav item:

```razor
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="buses/companies">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Bus Companies
            </NavLink>
        </div>
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add /buses/companies CRUD page"
```

---

### Task 9: `/buses/arrivals` page

**Files:**
- Create: `src/KayeDM.Web/Components/Pages/BusArrivals.razor`
- Modify: `src/KayeDM.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `IBusService`, `BusCompanyDto`, `LogArrivalRequest`, `BusTripBoardRow` (Task 4); `DomainException` (existing).

- [ ] **Step 1: Write the page**

`src/KayeDM.Web/Components/Pages/BusArrivals.razor`:

```razor
@page "/buses/arrivals"
@using System.ComponentModel.DataAnnotations
@using KayeDM.Application.Buses
@using KayeDM.Domain.Exceptions
@inject IBusService BusService

<PageTitle>Bus Arrivals</PageTitle>

<h1>Bus Arrivals</h1>

<EditForm Model="_form" OnValidSubmit="LogArrivalAsync">
    <DataAnnotationsValidator />
    <ValidationSummary />
    <div class="mb-2">
        <label>Company</label>
        <InputSelect class="form-control" @bind-Value="_form.BusCompanyId">
            <option value="0">-- select --</option>
            @foreach (var company in _companies)
            {
                <option value="@company.Id">@company.Name</option>
            }
        </InputSelect>
    </div>
    <div class="mb-2">
        <label>Bus Number</label>
        <InputText class="form-control" @bind-Value="_form.BusNumber" />
    </div>
    <div class="mb-2">
        <label>Route</label>
        <InputText class="form-control" @bind-Value="_form.Route" />
    </div>
    <button type="submit" class="btn btn-success">Bus Arrived</button>
</EditForm>

@if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}

<h2 class="mt-4">Today's Trip Board</h2>

@if (_board is null)
{
    <p>Loading…</p>
}
else if (_board.Count == 0)
{
    <p>No trips logged today.</p>
}
else
{
    <table class="table">
        <thead>
            <tr>
                <th>Company</th>
                <th>Bus No.</th>
                <th>Arrived</th>
                <th>Meals Used / Allowance</th>
                <th>Departed</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var row in _board)
            {
                <tr>
                    <td>@row.Trip.BusCompanyName</td>
                    <td>@row.Trip.BusNumber</td>
                    <td>@row.Trip.ArrivedAt.ToString("h:mm tt")</td>
                    <td>@row.MealsUsed / @(row.MealsUsed + row.AllowanceRemaining) (@row.AllowanceRemaining left)</td>
                    <td>@(row.Trip.DepartedAt is null ? "—" : row.Trip.DepartedAt.Value.ToString("h:mm tt"))</td>
                    <td>
                        @if (row.Trip.DepartedAt is null)
                        {
                            <button class="btn btn-sm btn-outline-secondary" @onclick="() => DepartAsync(row.Trip.Id)">Depart</button>
                        }
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    private List<BusCompanyDto> _companies = new();
    private List<BusTripBoardRow>? _board;
    private ArrivalForm _form = new();
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        _companies = await BusService.GetCompaniesAsync();
        await LoadBoardAsync();
    }

    private async Task LoadBoardAsync() => _board = await BusService.GetTodaysTripBoardAsync();

    private async Task LogArrivalAsync()
    {
        _error = null;
        if (_form.BusCompanyId <= 0)
        {
            _error = "Select a bus company.";
            return;
        }

        try
        {
            await BusService.LogArrivalAsync(new LogArrivalRequest(_form.BusCompanyId, _form.BusNumber, _form.Route, null));
            _form = new ArrivalForm();
            await LoadBoardAsync();
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }

    private async Task DepartAsync(int tripId)
    {
        await BusService.DepartAsync(tripId);
        await LoadBoardAsync();
    }

    private class ArrivalForm
    {
        public int BusCompanyId { get; set; }

        [Required]
        public string BusNumber { get; set; } = "";

        [Required]
        public string Route { get; set; } = "";
    }
}
```

- [ ] **Step 2: Add the nav link**

In `src/KayeDM.Web/Components/Layout/NavMenu.razor`, add after the `buses/companies` nav item added in Task 8:

```razor
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="buses/arrivals">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Bus Arrivals
            </NavLink>
        </div>
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add /buses/arrivals quick-log and trip board page"
```

---

### Task 10: POS updates — assign to bus + crew-meal mode

**Files:**
- Modify: `src/KayeDM.Web/Components/Pages/Pos.razor`

**Interfaces:**
- Consumes: `IBusService`, `BusTripDto` (Task 4); `IOrderService.CreateCrewMealOrderAsync`, `CreateCrewMealOrderRequest` (Tasks 5, 7); `CrewRole` (Task 0).

- [ ] **Step 1: Add the bus service injection and new state**

In `src/KayeDM.Web/Components/Pages/Pos.razor`, change the top `@using`/`@inject` block:

```razor
@page "/pos"
@using KayeDM.Application.Buses
@using KayeDM.Application.Menu
@using KayeDM.Application.Orders
@using KayeDM.Domain.Enums
@using KayeDM.Domain.Exceptions
@inject IMenuItemService MenuItemService
@inject IOrderService OrderService
@inject IBusService BusService
```

- [ ] **Step 2: Add the assign-to-bus + crew-meal-mode UI**

In `src/KayeDM.Web/Components/Pages/Pos.razor`, insert this block right before the closing `</div>` of `pos-payment` (i.e. immediately after the `else` block that renders "Total to pay via GCash"/`Complete`/`Clear` buttons and before the order-number/error alerts — insert directly above the existing `<button class="btn btn-success btn-lg" ...>Complete</button>` line):

```razor
        <div class="pos-bus-assignment">
            <label>Assign to bus (optional)</label>
            <select class="form-control" @bind="_selectedBusTripId">
                <option value="">-- none --</option>
                @foreach (var trip in _recentTrips)
                {
                    <option value="@trip.Id">@trip.BusCompanyName · @trip.BusNumber</option>
                }
            </select>
            <button type="button" class="btn btn-sm btn-outline-secondary" @onclick="RefreshTripsAsync">Refresh</button>

            <div class="form-check mt-2">
                <input class="form-check-input" type="checkbox" id="crewMealMode" @bind="_isCrewMealMode" />
                <label class="form-check-label" for="crewMealMode">Crew Meal Mode (free meal for bus crew)</label>
            </div>

            @if (_isCrewMealMode)
            {
                <div class="crew-role-toggle">
                    <label>Crew Role</label>
                    @foreach (var role in Enum.GetValues<CrewRole>())
                    {
                        <button type="button" class="btn @(role == _crewRole ? "btn-primary" : "btn-outline-primary")"
                                @onclick="() => _crewRole = role">@role</button>
                    }
                </div>
            }
        </div>

```

- [ ] **Step 3: Hide the payment strip in crew-meal mode**

Wrap the existing cash/GCash toggle + quick-amounts + tendered/total block in a `@if (!_isCrewMealMode)`. Replace:

```razor
        <div>
            <button class="btn @(_paymentMethod == PaymentMethod.Cash ? "btn-primary" : "btn-outline-primary")"
                    @onclick="() => _paymentMethod = PaymentMethod.Cash">Cash</button>
            <button class="btn @(_paymentMethod == PaymentMethod.GCash ? "btn-primary" : "btn-outline-primary")"
                    @onclick="() => _paymentMethod = PaymentMethod.GCash">GCash</button>
        </div>
        @if (_paymentMethod == PaymentMethod.Cash)
        {
            <div class="quick-amounts">
                @foreach (var amount in new decimal[] { 100, 200, 500, 1000 })
                {
                    <button class="btn btn-outline-secondary" @onclick="() => Tendered = amount">@FormatPeso(amount)</button>
                }
            </div>
            <div>
                <label>Tendered</label>
                <input type="number" class="form-control" @bind="Tendered" step="0.01" />
            </div>
            <div>Change: @FormatPeso(Change)</div>
        }
        else
        {
            <div>Total to pay via GCash: @FormatPeso(Total)</div>
        }
```

with:

```razor
        @if (_isCrewMealMode)
        {
            <div>Crew meal — total: @FormatPeso(0m) (free)</div>
        }
        else
        {
            <div>
                <button class="btn @(_paymentMethod == PaymentMethod.Cash ? "btn-primary" : "btn-outline-primary")"
                        @onclick="() => _paymentMethod = PaymentMethod.Cash">Cash</button>
                <button class="btn @(_paymentMethod == PaymentMethod.GCash ? "btn-primary" : "btn-outline-primary")"
                        @onclick="() => _paymentMethod = PaymentMethod.GCash">GCash</button>
            </div>
            @if (_paymentMethod == PaymentMethod.Cash)
            {
                <div class="quick-amounts">
                    @foreach (var amount in new decimal[] { 100, 200, 500, 1000 })
                    {
                        <button class="btn btn-outline-secondary" @onclick="() => Tendered = amount">@FormatPeso(amount)</button>
                    }
                </div>
                <div>
                    <label>Tendered</label>
                    <input type="number" class="form-control" @bind="Tendered" step="0.01" />
                </div>
                <div>Change: @FormatPeso(Change)</div>
            }
            else
            {
                <div>Total to pay via GCash: @FormatPeso(Total)</div>
            }
        }
```

- [ ] **Step 4: Add the new state fields and update `CanComplete`/`OnInitializedAsync`/`ClearTicket`**

In the `@code` block, replace:

```csharp
    private List<MenuItemDto> _menuItems = new();
    private MenuCategory _activeCategory = MenuCategory.Ulam;
    private List<TicketLine> _ticket = new();
    private PaymentMethod _paymentMethod = PaymentMethod.Cash;
    private decimal Tendered { get; set; }
    private string? _lastOrderNumber;
    private decimal _lastChange;
    private string? _error;

    private decimal Total => _ticket.Sum(l => l.MenuItem.Price * l.Quantity);
    private decimal Change => Tendered - Total;
    private bool CanComplete => _ticket.Count > 0 && (_paymentMethod == PaymentMethod.GCash || Tendered >= Total);

    protected override async Task OnInitializedAsync()
    {
        _menuItems = await MenuItemService.GetAllAsync();
    }
```

with:

```csharp
    private List<MenuItemDto> _menuItems = new();
    private MenuCategory _activeCategory = MenuCategory.Ulam;
    private List<TicketLine> _ticket = new();
    private PaymentMethod _paymentMethod = PaymentMethod.Cash;
    private decimal Tendered { get; set; }
    private string? _lastOrderNumber;
    private decimal _lastChange;
    private string? _error;

    private List<BusTripDto> _recentTrips = new();
    private int? _selectedBusTripId;
    private bool _isCrewMealMode;
    private CrewRole _crewRole = CrewRole.Driver;

    private decimal Total => _ticket.Sum(l => l.MenuItem.Price * l.Quantity);
    private decimal Change => Tendered - Total;

    private bool CanComplete => _ticket.Count > 0 &&
        (_isCrewMealMode
            ? _selectedBusTripId is not null
            : (_paymentMethod == PaymentMethod.GCash || Tendered >= Total));

    protected override async Task OnInitializedAsync()
    {
        _menuItems = await MenuItemService.GetAllAsync();
        await RefreshTripsAsync();
    }

    private async Task RefreshTripsAsync()
    {
        _recentTrips = await BusService.GetRecentArrivalsAsync(TimeSpan.FromMinutes(45));
    }
```

- [ ] **Step 5: Update `ClearTicket` to reset the new state**

Replace:

```csharp
    private void ClearTicket()
    {
        _ticket.Clear();
        Tendered = 0;
        _lastOrderNumber = null;
        _error = null;
    }
```

with:

```csharp
    private void ClearTicket()
    {
        _ticket.Clear();
        Tendered = 0;
        _lastOrderNumber = null;
        _error = null;
        _isCrewMealMode = false;
        _selectedBusTripId = null;
    }
```

- [ ] **Step 6: Update `CompleteAsync` to branch on crew-meal mode**

Replace:

```csharp
    private async Task CompleteAsync()
    {
        _error = null;
        try
        {
            var request = new CreateOrderRequest(
                _ticket.Select(l => new OrderLineRequest(l.MenuItem.Id, l.Quantity)).ToList(),
                _paymentMethod,
                Tendered,
                CashierId: null);

            var result = await OrderService.CreateOrderAsync(request);
            _lastOrderNumber = result.OrderNumber;
            _lastChange = result.ChangeGiven;
            _ticket.Clear();
            Tendered = 0;
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
        catch (Exception)
        {
            _error = "Something went wrong completing this order. Please try again.";
        }
    }
```

with:

```csharp
    private async Task CompleteAsync()
    {
        _error = null;
        try
        {
            OrderResult result;
            if (_isCrewMealMode)
            {
                if (_selectedBusTripId is null)
                {
                    _error = "Select a bus trip for the crew meal.";
                    return;
                }

                var crewRequest = new CreateCrewMealOrderRequest(
                    _ticket.Select(l => new OrderLineRequest(l.MenuItem.Id, l.Quantity)).ToList(),
                    _selectedBusTripId.Value,
                    _crewRole,
                    CashierId: null);

                result = await OrderService.CreateCrewMealOrderAsync(crewRequest);
            }
            else
            {
                var request = new CreateOrderRequest(
                    _ticket.Select(l => new OrderLineRequest(l.MenuItem.Id, l.Quantity)).ToList(),
                    _paymentMethod,
                    Tendered,
                    CashierId: null,
                    BusTripId: _selectedBusTripId);

                result = await OrderService.CreateOrderAsync(request);
            }

            _lastOrderNumber = result.OrderNumber;
            _lastChange = result.ChangeGiven;
            _ticket.Clear();
            Tendered = 0;
            _isCrewMealMode = false;
            _selectedBusTripId = null;
            await RefreshTripsAsync();
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
        catch (Exception)
        {
            _error = "Something went wrong completing this order. Please try again.";
        }
    }
```

- [ ] **Step 7: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add assign-to-bus and crew-meal mode to /pos"
```

---

### Task 11: `/buses/report` page

**Files:**
- Create: `src/KayeDM.Web/Components/Pages/BusReport.razor`
- Modify: `src/KayeDM.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `IBusService`, `BusCompanyDto`, `CrewMealReportResult`, `CrewMealReportRow` (Task 4).

- [ ] **Step 1: Write the page**

`src/KayeDM.Web/Components/Pages/BusReport.razor`:

```razor
@page "/buses/report"
@using KayeDM.Application.Buses
@using KayeDM.Domain.Exceptions
@inject IBusService BusService

<PageTitle>Crew Meal Report</PageTitle>

<h1>Monthly Crew Meal Report</h1>

<div class="mb-3">
    <label>Company</label>
    <select class="form-control" @bind="_selectedCompanyId">
        <option value="0">-- select --</option>
        @foreach (var company in _companies)
        {
            <option value="@company.Id">@company.Name</option>
        }
    </select>

    <label>Month</label>
    <input type="month" class="form-control" @bind="_monthInput" />

    <button class="btn btn-primary mt-2" @onclick="LoadReportAsync">Run Report</button>
</div>

@if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}

@if (_report is not null)
{
    <h2>@_report.CompanyName — @_report.Year:@_report.Month.ToString("D2")</h2>

    @if (_report.Trips.Count == 0)
    {
        <p>No trips recorded for this company in this month.</p>
    }
    else
    {
        <table class="table">
            <thead>
                <tr>
                    <th>Arrived</th>
                    <th>Bus No.</th>
                    <th>Route</th>
                    <th>Driver</th>
                    <th>Conductor</th>
                    <th>Assistant</th>
                    <th>Total</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var row in _report.Trips)
                {
                    <tr>
                        <td>@row.ArrivedAt.ToString("MMM d, h:mm tt")</td>
                        <td>@row.BusNumber</td>
                        <td>@row.Route</td>
                        <td>@row.DriverCredits</td>
                        <td>@row.ConductorCredits</td>
                        <td>@row.AssistantCredits</td>
                        <td>@row.TotalCredits</td>
                    </tr>
                }
            </tbody>
            <tfoot>
                <tr>
                    <th colspan="3">Totals (@_report.TotalTrips trips)</th>
                    <th>@_report.DriverMealsTotal</th>
                    <th>@_report.ConductorMealsTotal</th>
                    <th>@_report.AssistantMealsTotal</th>
                    <th>@_report.TotalMeals</th>
                </tr>
            </tfoot>
        </table>
    }
}

@code {
    private List<BusCompanyDto> _companies = new();
    private int _selectedCompanyId;
    private string _monthInput = DateTime.Now.ToString("yyyy-MM");
    private CrewMealReportResult? _report;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        _companies = await BusService.GetCompaniesAsync(includeInactive: true);
    }

    private async Task LoadReportAsync()
    {
        _error = null;
        _report = null;

        if (_selectedCompanyId <= 0)
        {
            _error = "Select a bus company.";
            return;
        }

        if (!DateTime.TryParse(_monthInput + "-01", out var parsed))
        {
            _error = "Select a valid month.";
            return;
        }

        try
        {
            _report = await BusService.GetMonthlyReportAsync(_selectedCompanyId, parsed.Year, parsed.Month);
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }
}
```

- [ ] **Step 2: Add the nav link**

In `src/KayeDM.Web/Components/Layout/NavMenu.razor`, add after the `buses/arrivals` nav item added in Task 9:

```razor
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="buses/report">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Crew Meal Report
            </NavLink>
        </div>
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add /buses/report monthly crew meal report page"
```

---

### Task 12: Final verification

**Files:** None — verification only.

**Interfaces:** None — exercises everything from Tasks 0–11 end to end.

- [ ] **Step 1: Full build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"`
Expected: `Passed! - Failed: 0, Passed: 15, Skipped: 0` (7 from Week 1 + 8 new this week).

- [ ] **Step 3: Confirm the migration history is exactly as planned**

Run: `dotnet ef migrations list --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj`
Expected order: `InitialCreate`, `AddOrderTables`, `AddBusCompanyAndTrip`, `AddCrewMealCredit`, `LinkOrderToBusTrip` — five migrations, no gaps, no edits to the first two.

- [ ] **Step 4: Manual walkthrough — bus company + arrival**

Run the app (`dotnet run --project src/KayeDM.Web`), then:
1. Go to `/buses/companies`, add a company (e.g. "DLTB", allowance 2).
2. Go to `/buses/arrivals`, log an arrival for that company with a bus number and route.
3. Confirm it appears in "Today's Trip Board" with "0 / 2 (2 left)".

- [ ] **Step 5: Manual walkthrough — POS assign-to-bus and crew meal mode**

1. Go to `/pos`. Confirm the trip logged in Step 4 appears in the "Assign to bus" dropdown.
2. Add a menu item to the ticket, assign it to the bus trip, complete a normal Cash payment. Confirm the order saves.
3. Add another item, check "Crew Meal Mode", pick the same trip and a crew role, complete. Confirm it saves with change ₱0.00.
4. Repeat crew-meal completion a 3rd time for the same trip (allowance is 2) — confirm it shows the "Crew meal allowance exceeded" error and does not save.

- [ ] **Step 6: Manual walkthrough — report**

Go to `/buses/report`, select the company and current month, run the report. Confirm the two completed crew-meal orders show up with the correct role columns and totals.

- [ ] **Step 7: Record completion**

Update `.superpowers/sdd/progress.md` (or equivalent) noting Week 2 complete, matching the Week 1 entry style.
