# Week 3 — Tray Inventory + Expense Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement Week 3 scope from `docs/kaye-dm-agent-prompts-weeks-1-5.md` (WEEK 3 PROMPT): tray inventory (`DishBatch`/`WasteLog`, availability math, oversell-override flag) and the expense module (`ExpenseCategory`/`Expense`), four migrations in a fixed order (`AddDishBatch` → `AddWasteLog` → `AddOversoldFlag` → `AddExpenseTables`), `/inventory/production`, POS availability strip, `/inventory/waste`, `/inventory/variance`, `/expenses`, `/expenses/categories`, `/expenses/report`, and 6–10 new xUnit tests covering availability math (including waste and the oversell flag), variance calculation, and expense monthly summary aggregation.

**Architecture:** Same 5-project layering as Weeks 1–2 (`KayeDM.Domain` ← `KayeDM.Application` ← `KayeDM.Infrastructure`, `KayeDM.Web` → Application + Infrastructure, `KayeDM.Tests` → all three). `InventoryService`/`ExpenseService` follow the exact same `IDbContextFactory<AppDbContext>` pattern as `MenuItemService`/`OrderService`/`BusService`. The availability formula (`produced − sold − wasted`) is implemented once as a static helper, `KayeDM.Infrastructure.Inventory.AvailabilityCalculator`, that takes an already-open `AppDbContext` — this lets both `InventoryService` (for the POS strip / variance report, its own short-lived context) and `OrderService` (for the oversell check, sharing the context it's about to insert the order into) reuse identical math without a second service-to-service call. **Domain decision, confirmed upfront (not left for the agent to guess):** "sold" for the availability formula counts every `OrderLine` on an `Order` with `Status == Completed` for that `MenuItemId` on that date — **voided orders are excluded, crew-meal orders are included** (the driver still ate the food; the order is just priced at ₱0, it is not voided). This is why `AvailabilityCalculator.GetSoldServingsAsync` filters only on `Status`, never on `IsCrewMeal`.

**Tech Stack:** Same as Weeks 1–2 — ASP.NET Core 8 Blazor Server, EF Core 8.0.11, SQL Server (LocalDB), xUnit + FluentAssertions + EF Core Sqlite (in-memory) for tests.

## Global Constraints

- Packages pinned to **8.0.11** for every `Microsoft.EntityFrameworkCore.*` / `Microsoft.AspNetCore.Identity.EntityFrameworkCore` package. Never upgrade to EF 9/10. `TargetFramework` stays `net8.0`.
- Migrations are sacred: one per schema change, descriptive names, never delete/regenerate/squash/edit an existing migration. A wrong migration gets a corrective migration, not an edit. This week's migration sequence is fixed: `AddDishBatch` → `AddWasteLog` → `AddOversoldFlag` → `AddExpenseTables`, in that order, never combined.
- `dotnet ef` CLI calls always use `--project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj`.
- Layering: no EF Core types in `KayeDM.Domain`; `KayeDM.Web` talks to `KayeDM.Application` interfaces only, never to `AppDbContext` directly.
- No MediatR, AutoMapper, or repository wrappers. Plain services + constructor injection.
- Services take `IDbContextFactory<AppDbContext>` (not `AppDbContext` directly) and call `await using var db = await _dbContextFactory.CreateDbContextAsync();` per call.
- Prefer pure Blazor over JS interop — **never use browser `confirm()`/`alert()`**; the oversell confirmation is an in-page button, not a JS dialog. Currency format is always `"₱{0:N2}"`. File-scoped namespaces. Nullable reference types enabled everywhere.
- Date pickers: bind `<input type="date">` to a `string` field (`yyyy-MM-dd`) and parse with `DateOnly.TryParseExact`/`TryParse`, per the established workaround for the `<input type="month">`/`DateOnly` Blazor binding CS0029 issue found in Week 2's `/buses/report` page — do not attempt `@bind` directly to a `DateOnly`/`TimeOnly` field.
- All commands below assume the working directory is `C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS` unless a step explicitly `cd`s elsewhere.
- `LoggedById` on `WasteLog` and `Expense` stays a plain string set to `"system"` by the service (no auth until Week 4) — do not build a user-picker UI for it.
- Out of scope this week (do not build): dashboard/charts, daily closing, auth/roles, seed data generator, Docker, deployment, exports.

---

### Task 0: Domain layer — inventory enums/entities + `Order.OversoldOverride`

**Files:**
- Create: `src/KayeDM.Domain/Enums/WasteReason.cs`
- Create: `src/KayeDM.Domain/Entities/DishBatch.cs`
- Create: `src/KayeDM.Domain/Entities/WasteLog.cs`
- Modify: `src/KayeDM.Domain/Entities/Order.cs`

**Interfaces:**
- Produces: `KayeDM.Domain.Enums.WasteReason { EndOfDay, Spoiled, Dropped }`, `KayeDM.Domain.Entities.DishBatch`, `KayeDM.Domain.Entities.WasteLog`, `Order.OversoldOverride` (bool) — consumed by every later task.

Plain data/enum types with no behavior to unit-test — write them directly.

- [ ] **Step 1: `WasteReason` enum**

`src/KayeDM.Domain/Enums/WasteReason.cs`:

```csharp
namespace KayeDM.Domain.Enums;

public enum WasteReason
{
    EndOfDay,
    Spoiled,
    Dropped
}
```

- [ ] **Step 2: `DishBatch` entity**

`src/KayeDM.Domain/Entities/DishBatch.cs`:

```csharp
namespace KayeDM.Domain.Entities;

public class DishBatch
{
    public int Id { get; set; }
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;

    // Date-only in practice — always stored/queried at midnight so a batch
    // belongs to exactly one calendar day.
    public DateTime Date { get; set; }

    // Half trays allowed (e.g. 2.5 trays of adobo).
    public decimal TraysProduced { get; set; }
    public int ServingsPerTray { get; set; }
    public DateTime ProducedAt { get; set; }
}
```

- [ ] **Step 3: `WasteLog` entity**

`src/KayeDM.Domain/Entities/WasteLog.cs`:

```csharp
using KayeDM.Domain.Enums;

namespace KayeDM.Domain.Entities;

public class WasteLog
{
    public int Id { get; set; }
    public int DishBatchId { get; set; }
    public DishBatch DishBatch { get; set; } = null!;
    public decimal TraysWasted { get; set; }
    public WasteReason Reason { get; set; }
    public DateTime LoggedAt { get; set; }

    // Plain string until auth in Week 4.
    // TODO Week 4: replace with real Identity user id.
    public string LoggedById { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Add `Order.OversoldOverride`**

In `src/KayeDM.Domain/Entities/Order.cs`, after the `IsCrewMeal` line:

```csharp
    public bool IsCrewMeal { get; set; } = false;

    // Set true only when the cashier explicitly confirmed selling past the
    // day's computed availability (see AvailabilityCalculator). Never set by
    // request input directly — OrderService derives it from the actual check.
    public bool OversoldOverride { get; set; } = false;
```

- [ ] **Step 5: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(domain): add DishBatch, WasteLog entities, WasteReason enum, and Order.OversoldOverride"
```

---

### Task 1: `AppDbContext` — `DishBatch` + `AddDishBatch` migration

**Files:**
- Modify: `src/KayeDM.Infrastructure/Data/AppDbContext.cs`
- Create (generated): `src/KayeDM.Infrastructure/Data/Migrations/*_AddDishBatch.cs`

**Interfaces:**
- Consumes: `DishBatch`, `MenuItem` (Task 0, existing).
- Produces: `AppDbContext.DishBatches` (`DbSet<DishBatch>`) — consumed by `AvailabilityCalculator` (Task 4), `InventoryService` (Task 6).

- [ ] **Step 1: Add the DbSet and Fluent config**

In `src/KayeDM.Infrastructure/Data/AppDbContext.cs`, add the DbSet after `CrewMealCredits`:

```csharp
    public DbSet<CrewMealCredit> CrewMealCredits => Set<CrewMealCredit>();
    public DbSet<DishBatch> DishBatches => Set<DishBatch>();
```

and Fluent config inside `OnModelCreating`, after the `CrewMealCredit` block:

```csharp
        builder.Entity<DishBatch>(entity =>
        {
            entity.Property(b => b.TraysProduced).HasPrecision(10, 2);

            entity.HasOne(b => b.MenuItem)
                .WithMany()
                .HasForeignKey(b => b.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 3: Generate the `AddDishBatch` migration**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations add AddDishBatch --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj --output-dir Data/Migrations
```

Expected: `Done.`

- [ ] **Step 4: Apply it**

```bash
dotnet ef database update --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected: `Done.` Verify with:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT name FROM sys.tables WHERE name = 'DishBatches';"
```

Expected: `DishBatches` returned.

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): add DishBatch to AppDbContext and AddDishBatch migration"
```

---

### Task 2: `AppDbContext` — `WasteLog` + `AddWasteLog` migration

**Files:**
- Modify: `src/KayeDM.Infrastructure/Data/AppDbContext.cs`
- Create (generated): `src/KayeDM.Infrastructure/Data/Migrations/*_AddWasteLog.cs`

**Interfaces:**
- Consumes: `WasteLog`, `DishBatches` (Tasks 0, 1).
- Produces: `AppDbContext.WasteLogs` (`DbSet<WasteLog>`) — consumed by `AvailabilityCalculator` (Task 4), `InventoryService` (Task 6).

- [ ] **Step 1: Add the DbSet and Fluent config**

In `src/KayeDM.Infrastructure/Data/AppDbContext.cs`, add the DbSet:

```csharp
    public DbSet<WasteLog> WasteLogs => Set<WasteLog>();
```

and Fluent config inside `OnModelCreating`, after the `DishBatch` block:

```csharp
        builder.Entity<WasteLog>(entity =>
        {
            entity.Property(w => w.TraysWasted).HasPrecision(10, 2);
            entity.Property(w => w.LoggedById).HasMaxLength(450);

            entity.HasOne(w => w.DishBatch)
                .WithMany()
                .HasForeignKey(w => w.DishBatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 3: Generate the `AddWasteLog` migration**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations add AddWasteLog --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj --output-dir Data/Migrations
```

Expected: `Done.`

- [ ] **Step 4: Apply it**

```bash
dotnet ef database update --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected: `Done.` Verify with:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT name FROM sys.tables WHERE name = 'WasteLogs';"
```

Expected: `WasteLogs` returned.

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): add WasteLog to AppDbContext and AddWasteLog migration"
```

---

### Task 3: `AppDbContext` — `Order.OversoldOverride` column + `AddOversoldFlag` migration

**Files:**
- Modify: `src/KayeDM.Infrastructure/Data/AppDbContext.cs`
- Create (generated): `src/KayeDM.Infrastructure/Data/Migrations/*_AddOversoldFlag.cs`

**Interfaces:**
- Consumes: `Order.OversoldOverride` (Task 0).
- Produces: `Orders.OversoldOverride` column, `NOT NULL DEFAULT 0` — no new C# surface, but Task 7 relies on it being persisted.

- [ ] **Step 1: Add the Fluent config**

In `src/KayeDM.Infrastructure/Data/AppDbContext.cs`, inside the existing `builder.Entity<Order>(entity => { ... })` block, add after the `Property(o => o.VoidReason)...` line:

```csharp
            entity.Property(o => o.VoidReason).HasMaxLength(250);
            entity.Property(o => o.OversoldOverride).HasDefaultValue(false);
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 3: Generate the `AddOversoldFlag` migration**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations add AddOversoldFlag --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj --output-dir Data/Migrations
```

Expected: `Done.` The generated migration should contain a single `AddColumn` for `OversoldOverride` on `Orders` with a default value — no dropped/recreated columns. If the diff shows anything else, stop and inspect before applying.

- [ ] **Step 4: Apply it**

```bash
dotnet ef database update --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected: `Done.` Verify with:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('Orders') AND name = 'OversoldOverride';"
```

Expected: `OversoldOverride` returned.

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): add Order.OversoldOverride column via AddOversoldFlag migration"
```

---

### Task 4: `OversoldException` + `AvailabilityCalculator` (TDD)

**Files:**
- Create: `src/KayeDM.Domain/Exceptions/OversoldException.cs`
- Create: `src/KayeDM.Infrastructure/Inventory/AvailabilityCalculator.cs`
- Create: `tests/KayeDM.Tests/Inventory/AvailabilityCalculatorTests.cs`

**Interfaces:**
- Consumes: `DishBatch`, `WasteLog`, `Order`, `OrderLine`, `OrderStatus` (Task 0, existing); `AppDbContext.DishBatches`/`WasteLogs`/`Orders`/`OrderLines` (Tasks 1–2, existing).
- Produces: `KayeDM.Domain.Exceptions.OversoldException : DomainException` — consumed by Task 7 (thrown) and Task 9 (caught, distinctly from `DomainException`). `KayeDM.Infrastructure.Inventory.AvailabilityCalculator` with `Task<int> GetProducedServingsAsync(AppDbContext db, int menuItemId, DateTime date)`, `Task<int> GetSoldServingsAsync(AppDbContext db, int menuItemId, DateTime date)`, `Task<int> GetWastedServingsAsync(AppDbContext db, int menuItemId, DateTime date)`, `Task<int> GetAvailableServingsAsync(AppDbContext db, int menuItemId, DateTime date)` — consumed by `InventoryService` (Task 6) and `OrderService` (Task 7).

This is the load-bearing domain rule the Week 3 prompt calls out explicitly (availability math, including the "crew meals count as sold" rule) — build it test-first.

- [ ] **Step 1: `OversoldException`**

`src/KayeDM.Domain/Exceptions/OversoldException.cs`:

```csharp
namespace KayeDM.Domain.Exceptions;

public class OversoldException : DomainException
{
    public OversoldException(string message) : base(message)
    {
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/KayeDM.Tests/Inventory/AvailabilityCalculatorTests.cs`:

```csharp
using FluentAssertions;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Inventory;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Inventory;

public class AvailabilityCalculatorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly DateTime _today = DateTime.Now.Date;

    public AvailabilityCalculatorTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _db.MenuItems.Add(new MenuItem { Id = 1, Name = "Adobo", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 1 });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetProducedServingsAsync_MultipliesTraysByServingsPerTray_AcrossBatchesSameDay()
    {
        _db.DishBatches.AddRange(
            new DishBatch { Id = 1, MenuItemId = 1, Date = _today, TraysProduced = 3m, ServingsPerTray = 10, ProducedAt = DateTime.Now },
            new DishBatch { Id = 2, MenuItemId = 1, Date = _today, TraysProduced = 0.5m, ServingsPerTray = 10, ProducedAt = DateTime.Now });
        _db.SaveChanges();

        var produced = await AvailabilityCalculator.GetProducedServingsAsync(_db, 1, _today);

        produced.Should().Be(35);
    }

    [Fact]
    public async Task GetSoldServingsAsync_ExcludesVoidedOrders()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260704-001",
            CreatedAt = DateTime.Now,
            Status = OrderStatus.Voided,
            PaymentMethod = PaymentMethod.Cash,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 5, UnitPriceAtSale = 90m } }
        });
        _db.SaveChanges();

        var sold = await AvailabilityCalculator.GetSoldServingsAsync(_db, 1, _today);

        sold.Should().Be(0);
    }

    [Fact]
    public async Task GetSoldServingsAsync_IncludesCrewMealOrders_BecauseTheDriverStillAteTheFood()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260704-002",
            CreatedAt = DateTime.Now,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            IsCrewMeal = true,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 3, UnitPriceAtSale = 0m } }
        });
        _db.SaveChanges();

        var sold = await AvailabilityCalculator.GetSoldServingsAsync(_db, 1, _today);

        sold.Should().Be(3);
    }

    [Fact]
    public async Task GetWastedServingsAsync_MultipliesTraysWastedByBatchServingsPerTray()
    {
        _db.DishBatches.Add(new DishBatch { Id = 1, MenuItemId = 1, Date = _today, TraysProduced = 5m, ServingsPerTray = 10, ProducedAt = DateTime.Now });
        _db.SaveChanges();
        _db.WasteLogs.Add(new WasteLog { DishBatchId = 1, TraysWasted = 1.5m, Reason = WasteReason.EndOfDay, LoggedAt = DateTime.Now, LoggedById = "system" });
        _db.SaveChanges();

        var wasted = await AvailabilityCalculator.GetWastedServingsAsync(_db, 1, _today);

        wasted.Should().Be(15);
    }

    [Fact]
    public async Task GetAvailableServingsAsync_SubtractsSoldAndWasted_FromProduced()
    {
        _db.DishBatches.Add(new DishBatch { Id = 1, MenuItemId = 1, Date = _today, TraysProduced = 2m, ServingsPerTray = 10, ProducedAt = DateTime.Now });
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260704-003",
            CreatedAt = DateTime.Now,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 4, UnitPriceAtSale = 90m } }
        });
        _db.SaveChanges();
        _db.WasteLogs.Add(new WasteLog { DishBatchId = 1, TraysWasted = 1m, Reason = WasteReason.Spoiled, LoggedAt = DateTime.Now, LoggedById = "system" });
        _db.SaveChanges();

        var available = await AvailabilityCalculator.GetAvailableServingsAsync(_db, 1, _today);

        // 20 produced - 4 sold - 10 wasted = 6
        available.Should().Be(6);
    }
}
```

- [ ] **Step 3: Run the tests to confirm they fail to compile (no `AvailabilityCalculator` yet)**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~AvailabilityCalculatorTests"`
Expected: build error — `AvailabilityCalculator` does not exist in `KayeDM.Infrastructure.Inventory`.

- [ ] **Step 4: Implement `AvailabilityCalculator`**

`src/KayeDM.Infrastructure/Inventory/AvailabilityCalculator.cs`:

```csharp
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Inventory;

// Shared by InventoryService (its own short-lived context, for the POS strip
// and variance report) and OrderService (the context it's about to insert an
// order into, for the oversell check) — one formula, one place.
public static class AvailabilityCalculator
{
    public static async Task<int> GetProducedServingsAsync(AppDbContext db, int menuItemId, DateTime date)
    {
        var batches = await db.DishBatches
            .Where(b => b.MenuItemId == menuItemId && b.Date == date)
            .Select(b => new { b.TraysProduced, b.ServingsPerTray })
            .ToListAsync();

        return (int)batches.Sum(b => b.TraysProduced * b.ServingsPerTray);
    }

    // "Sold" counts every completed order's lines for this item on this date —
    // voided orders are excluded, crew-meal orders are included (the driver
    // still ate the food; a crew-meal order is priced at ₱0, it is not voided).
    public static async Task<int> GetSoldServingsAsync(AppDbContext db, int menuItemId, DateTime date)
    {
        var nextDay = date.AddDays(1);

        return await db.OrderLines
            .Where(l => l.MenuItemId == menuItemId
                && l.Order.Status == OrderStatus.Completed
                && l.Order.CreatedAt >= date && l.Order.CreatedAt < nextDay)
            .SumAsync(l => (int?)l.Quantity) ?? 0;
    }

    public static async Task<int> GetWastedServingsAsync(AppDbContext db, int menuItemId, DateTime date)
    {
        var wastedServings = await db.WasteLogs
            .Where(w => w.DishBatch.MenuItemId == menuItemId && w.DishBatch.Date == date)
            .Select(w => w.TraysWasted * w.DishBatch.ServingsPerTray)
            .ToListAsync();

        return (int)wastedServings.Sum();
    }

    public static async Task<int> GetAvailableServingsAsync(AppDbContext db, int menuItemId, DateTime date)
    {
        var produced = await GetProducedServingsAsync(db, menuItemId, date);
        var sold = await GetSoldServingsAsync(db, menuItemId, date);
        var wasted = await GetWastedServingsAsync(db, menuItemId, date);

        return produced - sold - wasted;
    }
}
```

- [ ] **Step 5: Run the tests again to confirm they pass**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~AvailabilityCalculatorTests"`
Expected: 5 tests run, all PASS.

- [ ] **Step 6: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): add OversoldException and AvailabilityCalculator with tests for produced/sold/wasted math"
```

---

### Task 5: Application layer — Inventory DTOs and `IInventoryService`

**Files:**
- Create: `src/KayeDM.Application/Inventory/InventoryModels.cs`
- Create: `src/KayeDM.Application/Inventory/IInventoryService.cs`

**Interfaces:**
- Consumes: `WasteReason` (Task 0).
- Produces: `CreateDishBatchRequest(int MenuItemId, decimal TraysProduced, int ServingsPerTray)`, `DishBatchDto(int Id, int MenuItemId, string MenuItemName, DateTime Date, decimal TraysProduced, int ServingsPerTray, DateTime ProducedAt)`, `LogWasteRequest(int DishBatchId, decimal TraysWasted, WasteReason Reason)`, `WasteLogDto(int Id, int DishBatchId, string MenuItemName, decimal TraysWasted, WasteReason Reason, DateTime LoggedAt)`, `MenuItemAvailabilityDto(int MenuItemId, string MenuItemName, bool HasBatchToday, int? AvailableServings)`, `VarianceRow(DateOnly Date, int MenuItemId, string MenuItemName, int Produced, int Sold, int Wasted, decimal VariancePercent)`, `IInventoryService` — all consumed by Tasks 6, 8, 9, 10, 11.

Plain contracts, no behavior to test.

- [ ] **Step 1: DTOs**

`src/KayeDM.Application/Inventory/InventoryModels.cs`:

```csharp
using KayeDM.Domain.Enums;

namespace KayeDM.Application.Inventory;

public record CreateDishBatchRequest(int MenuItemId, decimal TraysProduced, int ServingsPerTray);

public record DishBatchDto(
    int Id,
    int MenuItemId,
    string MenuItemName,
    DateTime Date,
    decimal TraysProduced,
    int ServingsPerTray,
    DateTime ProducedAt);

public record LogWasteRequest(int DishBatchId, decimal TraysWasted, WasteReason Reason);

public record WasteLogDto(
    int Id,
    int DishBatchId,
    string MenuItemName,
    decimal TraysWasted,
    WasteReason Reason,
    DateTime LoggedAt);

public record MenuItemAvailabilityDto(int MenuItemId, string MenuItemName, bool HasBatchToday, int? AvailableServings);

// Variance = Produced - Sold - Wasted (the same "available" formula, viewed as
// an end-of-range accounting check rather than a live POS number — a
// well-run day should land near zero; negative means the day oversold).
public record VarianceRow(
    DateOnly Date,
    int MenuItemId,
    string MenuItemName,
    int Produced,
    int Sold,
    int Wasted,
    decimal VariancePercent);
```

- [ ] **Step 2: `IInventoryService`**

`src/KayeDM.Application/Inventory/IInventoryService.cs`:

```csharp
namespace KayeDM.Application.Inventory;

public interface IInventoryService
{
    Task<DishBatchDto> CreateBatchAsync(CreateDishBatchRequest request);
    Task<List<DishBatchDto>> GetTodaysBatchesAsync();
    Task<WasteLogDto> LogWasteAsync(LogWasteRequest request);

    // Feeds the POS availability strip: every active menu item, whether a
    // batch was logged today, and remaining servings if so (null = no batch,
    // meaning the item has no tracked limit today but remains sellable).
    Task<List<MenuItemAvailabilityDto>> GetTodaysAvailabilityAsync();

    Task<List<VarianceRow>> GetVarianceAsync(DateOnly from, DateOnly to);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(application): add Inventory DTOs and IInventoryService interface"
```

---

### Task 6: `InventoryService` implementation + DI registration

**Files:**
- Create: `src/KayeDM.Infrastructure/Inventory/InventoryService.cs`
- Create: `tests/KayeDM.Tests/Inventory/InventoryServiceTests.cs`
- Modify: `src/KayeDM.Web/Program.cs`

**Interfaces:**
- Consumes: `IInventoryService` and all Inventory DTOs (Task 5); `AppDbContext`, `DishBatch`, `WasteLog`, `MenuItem` (Tasks 0–2, existing); `AvailabilityCalculator` (Task 4); `DomainException` (existing).
- Produces: `KayeDM.Infrastructure.Inventory.InventoryService : IInventoryService` — consumed by the `/inventory/*` pages (Tasks 8, 10, 11) and the POS page (Task 9) via DI.

Batch creation and waste logging are plain CRUD (same shape as `MenuItemService`/`BusService` company CRUD — no dedicated tests, per Week 1/2 precedent). `GetTodaysAvailabilityAsync` and `GetVarianceAsync` compose `AvailabilityCalculator`, whose math is already covered by Task 4's tests, so only a thin integration test is needed here to confirm the wiring produces the right shape (null vs. computed availability, one row per dish per day).

- [ ] **Step 1: Write the service**

`src/KayeDM.Infrastructure/Inventory/InventoryService.cs`:

```csharp
using KayeDM.Application.Inventory;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Inventory;

public class InventoryService : IInventoryService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public InventoryService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<DishBatchDto> CreateBatchAsync(CreateDishBatchRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var menuItem = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == request.MenuItemId)
            ?? throw new DomainException($"Menu item {request.MenuItemId} not found.");

        if (request.TraysProduced <= 0)
        {
            throw new DomainException("Trays produced must be positive.");
        }

        if (request.ServingsPerTray <= 0)
        {
            throw new DomainException("Servings per tray must be positive.");
        }

        var batch = new DishBatch
        {
            MenuItemId = request.MenuItemId,
            Date = DateTime.Now.Date,
            TraysProduced = request.TraysProduced,
            ServingsPerTray = request.ServingsPerTray,
            ProducedAt = DateTime.Now
        };

        db.DishBatches.Add(batch);
        await db.SaveChangesAsync();

        return new DishBatchDto(batch.Id, batch.MenuItemId, menuItem.Name, batch.Date, batch.TraysProduced, batch.ServingsPerTray, batch.ProducedAt);
    }

    public async Task<List<DishBatchDto>> GetTodaysBatchesAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var today = DateTime.Now.Date;
        return await db.DishBatches
            .AsNoTracking()
            .Include(b => b.MenuItem)
            .Where(b => b.Date == today)
            .OrderBy(b => b.MenuItem.Name)
            .Select(b => new DishBatchDto(b.Id, b.MenuItemId, b.MenuItem.Name, b.Date, b.TraysProduced, b.ServingsPerTray, b.ProducedAt))
            .ToListAsync();
    }

    public async Task<WasteLogDto> LogWasteAsync(LogWasteRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var batch = await db.DishBatches.Include(b => b.MenuItem).FirstOrDefaultAsync(b => b.Id == request.DishBatchId)
            ?? throw new DomainException($"Dish batch {request.DishBatchId} not found.");

        if (request.TraysWasted <= 0)
        {
            throw new DomainException("Trays wasted must be positive.");
        }

        var log = new WasteLog
        {
            DishBatchId = batch.Id,
            TraysWasted = request.TraysWasted,
            Reason = request.Reason,
            LoggedAt = DateTime.Now,
            LoggedById = "system"
        };

        db.WasteLogs.Add(log);
        await db.SaveChangesAsync();

        return new WasteLogDto(log.Id, batch.Id, batch.MenuItem.Name, log.TraysWasted, log.Reason, log.LoggedAt);
    }

    public async Task<List<MenuItemAvailabilityDto>> GetTodaysAvailabilityAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var today = DateTime.Now.Date;
        var menuItems = await db.MenuItems.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.SortOrder).ToListAsync();

        var result = new List<MenuItemAvailabilityDto>();
        foreach (var item in menuItems)
        {
            var hasBatch = await db.DishBatches.AnyAsync(b => b.MenuItemId == item.Id && b.Date == today);
            int? available = hasBatch ? await AvailabilityCalculator.GetAvailableServingsAsync(db, item.Id, today) : null;
            result.Add(new MenuItemAvailabilityDto(item.Id, item.Name, hasBatch, available));
        }

        return result;
    }

    public async Task<List<VarianceRow>> GetVarianceAsync(DateOnly from, DateOnly to)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var toDate = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        var batches = await db.DishBatches
            .AsNoTracking()
            .Include(b => b.MenuItem)
            .Where(b => b.Date >= fromDate && b.Date < toDate)
            .ToListAsync();

        var rows = new List<VarianceRow>();
        foreach (var batch in batches)
        {
            var produced = await AvailabilityCalculator.GetProducedServingsAsync(db, batch.MenuItemId, batch.Date);
            var sold = await AvailabilityCalculator.GetSoldServingsAsync(db, batch.MenuItemId, batch.Date);
            var wasted = await AvailabilityCalculator.GetWastedServingsAsync(db, batch.MenuItemId, batch.Date);
            var variance = produced - sold - wasted;
            var variancePercent = produced == 0 ? 0m : Math.Round((decimal)variance / produced * 100m, 1);

            rows.Add(new VarianceRow(DateOnly.FromDateTime(batch.Date), batch.MenuItemId, batch.MenuItem.Name, produced, sold, wasted, variancePercent));
        }

        return rows
            .GroupBy(r => (r.Date, r.MenuItemId))
            .Select(g => g.First())
            .OrderBy(r => r.Date).ThenBy(r => r.MenuItemName)
            .ToList();
    }
}
```

- [ ] **Step 2: Write the tests**

`tests/KayeDM.Tests/Inventory/InventoryServiceTests.cs`:

```csharp
using FluentAssertions;
using KayeDM.Application.Inventory;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Inventory;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Inventory;

public class InventoryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly InventoryService _sut;

    public InventoryServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new InventoryService(new TestDbContextFactory(options));

        _db.MenuItems.Add(new MenuItem { Id = 1, Name = "Adobo", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 1 });
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
    public async Task GetTodaysAvailabilityAsync_ReturnsNullAvailability_WhenNoBatchLoggedToday()
    {
        var result = await _sut.GetTodaysAvailabilityAsync();

        result.Should().ContainSingle(r => r.MenuItemId == 1 && !r.HasBatchToday && r.AvailableServings == null);
    }

    [Fact]
    public async Task GetTodaysAvailabilityAsync_ComputesRemaining_WhenBatchExists()
    {
        await _sut.CreateBatchAsync(new CreateDishBatchRequest(1, 2m, 10));

        var result = await _sut.GetTodaysAvailabilityAsync();

        result.Should().ContainSingle(r => r.MenuItemId == 1 && r.HasBatchToday && r.AvailableServings == 20);
    }

    [Fact]
    public async Task GetVarianceAsync_ReturnsOneRowPerDishPerDay_WithComputedVariancePercent()
    {
        await _sut.CreateBatchAsync(new CreateDishBatchRequest(1, 2m, 10));
        var today = DateOnly.FromDateTime(DateTime.Now.Date);

        var rows = await _sut.GetVarianceAsync(today, today);

        rows.Should().ContainSingle();
        rows[0].Produced.Should().Be(20);
        rows[0].VariancePercent.Should().Be(100m);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~InventoryServiceTests"`
Expected: 3 tests run, all PASS.

- [ ] **Step 4: Register `InventoryService` in DI**

In `src/KayeDM.Web/Program.cs`, add the using and registration:

```csharp
using KayeDM.Application.Buses;
using KayeDM.Application.Inventory;
using KayeDM.Application.Menu;
using KayeDM.Application.Orders;
using KayeDM.Infrastructure.Buses;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Inventory;
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
builder.Services.AddScoped<IInventoryService, InventoryService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
```

- [ ] **Step 5: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): implement InventoryService with batch/waste/availability/variance and tests"
```

---

### Task 7: `OrderService` — wire the oversell check into `CreateOrderAsync` (TDD)

**Files:**
- Modify: `src/KayeDM.Application/Orders/OrderModels.cs`
- Modify: `src/KayeDM.Infrastructure/Orders/OrderService.cs`
- Create: `tests/KayeDM.Tests/Orders/OversellOrderServiceTests.cs`

**Interfaces:**
- Consumes: `AvailabilityCalculator`, `OversoldException` (Task 4).
- Produces: `CreateOrderRequest` gains a trailing `bool OversoldOverride = false` parameter (existing call sites unaffected). `OrderService.CreateOrderAsync` now throws `OversoldException` when a line exceeds today's computed availability and `OversoldOverride` was not set, and persists `Order.OversoldOverride = true` only when an actual oversell was confirmed — consumed by the POS page (Task 9).

Domain rule 2 from the blueprint ("an order cannot sell more servings than a batch has available — warn, allow override with flag") is unit-tested here directly against `OrderService`, the same way the crew-meal allowance rule was tested in Week 2.

- [ ] **Step 1: Extend `CreateOrderRequest`**

In `src/KayeDM.Application/Orders/OrderModels.cs`, change:

```csharp
public record CreateOrderRequest(
    IReadOnlyList<OrderLineRequest> Lines,
    PaymentMethod PaymentMethod,
    decimal AmountTendered,
    string? CashierId,
    int? BusTripId = null);
```

to:

```csharp
public record CreateOrderRequest(
    IReadOnlyList<OrderLineRequest> Lines,
    PaymentMethod PaymentMethod,
    decimal AmountTendered,
    string? CashierId,
    int? BusTripId = null,
    bool OversoldOverride = false);
```

- [ ] **Step 2: Write the failing oversell tests**

`tests/KayeDM.Tests/Orders/OversellOrderServiceTests.cs`:

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

public class OversellOrderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly OrderService _sut;
    private readonly DateTime _today = DateTime.Now.Date;

    public OversellOrderServiceTests()
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
    public async Task CreateOrderAsync_Throws_WhenQuantityExceedsAvailability_AndNoOverride()
    {
        _db.DishBatches.Add(new DishBatch { Id = 1, MenuItemId = 1, Date = _today, TraysProduced = 1m, ServingsPerTray = 2, ProducedAt = DateTime.Now });
        _db.SaveChanges();

        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 3) }, PaymentMethod.Cash, 300m, null);

        var act = async () => await _sut.CreateOrderAsync(request);

        await act.Should().ThrowAsync<OversoldException>();
    }

    [Fact]
    public async Task CreateOrderAsync_Succeeds_AndFlagsOrder_WhenOverrideConfirmed()
    {
        _db.DishBatches.Add(new DishBatch { Id = 1, MenuItemId = 1, Date = _today, TraysProduced = 1m, ServingsPerTray = 2, ProducedAt = DateTime.Now });
        _db.SaveChanges();

        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 3) }, PaymentMethod.Cash, 300m, null, OversoldOverride: true);

        var result = await _sut.CreateOrderAsync(request);

        var order = await _db.Orders.FindAsync(result.Id);
        order!.OversoldOverride.Should().BeTrue();
    }

    [Fact]
    public async Task CreateOrderAsync_DoesNotFlagOrder_WhenOverrideTrueButNothingWasOversold()
    {
        _db.DishBatches.Add(new DishBatch { Id = 1, MenuItemId = 1, Date = _today, TraysProduced = 5m, ServingsPerTray = 10, ProducedAt = DateTime.Now });
        _db.SaveChanges();

        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 1) }, PaymentMethod.Cash, 100m, null, OversoldOverride: true);

        var result = await _sut.CreateOrderAsync(request);

        var order = await _db.Orders.FindAsync(result.Id);
        order!.OversoldOverride.Should().BeFalse();
    }

    [Fact]
    public async Task CreateOrderAsync_Succeeds_WhenNoBatchLoggedToday_RegardlessOfQuantity()
    {
        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 50) }, PaymentMethod.Cash, 5000m, null);

        var act = async () => await _sut.CreateOrderAsync(request);

        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 3: Run the tests to confirm they fail**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~OversellOrderServiceTests"`
Expected: 4 tests run; the first two FAIL (no oversell check exists yet — no exception thrown / flag never set), the latter two PASS already.

- [ ] **Step 4: Wire the check into `CreateOrderAsync`**

In `src/KayeDM.Infrastructure/Orders/OrderService.cs`, add the using:

```csharp
using KayeDM.Infrastructure.Inventory;
```

Then replace the `foreach (var lineRequest in request.Lines)` loop inside `CreateOrderAsync` (the one building `order.Lines`/`lineResults`/`total`) with:

```csharp
        var today = DateTime.Now.Date;
        var wasOversold = false;

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

            var hasBatchToday = await db.DishBatches.AnyAsync(b => b.MenuItemId == menuItem.Id && b.Date == today);
            if (hasBatchToday)
            {
                var available = await AvailabilityCalculator.GetAvailableServingsAsync(db, menuItem.Id, today);
                if (lineRequest.Quantity > available)
                {
                    if (!request.OversoldOverride)
                    {
                        throw new OversoldException(
                            $"Selling {lineRequest.Quantity} of {menuItem.Name} exceeds available servings ({available} left). Confirm to sell anyway.");
                    }

                    wasOversold = true;
                }
            }

            var unitPrice = menuItem.Price;
            var lineTotal = unitPrice * lineRequest.Quantity;
            total += lineTotal;

            order.Lines.Add(new OrderLine
            {
                MenuItemId = menuItem.Id,
                Quantity = lineRequest.Quantity,
                UnitPriceAtSale = unitPrice
            });

            lineResults.Add(new OrderLineResult(menuItem.Id, menuItem.Name, lineRequest.Quantity, unitPrice, lineTotal));
        }

        order.OversoldOverride = wasOversold;
```

Add the `using KayeDM.Domain.Exceptions;` line is already present (for `DomainException`) — `OversoldException` lives in the same namespace, no extra using needed.

- [ ] **Step 5: Run the tests again to confirm they pass**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~OversellOrderServiceTests"`
Expected: 4 tests run, all PASS.

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"`
Expected: `Passed! - Failed: 0`.

- [ ] **Step 7: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): enforce availability check in CreateOrderAsync with OversoldException and override flag"
```

---

### Task 8: `/inventory/production` page

**Files:**
- Create: `src/KayeDM.Web/Components/Pages/InventoryProduction.razor`
- Modify: `src/KayeDM.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `IInventoryService`, `DishBatchDto`, `CreateDishBatchRequest` (Task 5, registered in DI by Task 6); `IMenuItemService`, `MenuItemDto` (existing).

- [ ] **Step 1: Write the page**

`src/KayeDM.Web/Components/Pages/InventoryProduction.razor`:

```razor
@page "/inventory/production"
@using System.ComponentModel.DataAnnotations
@using KayeDM.Application.Inventory
@using KayeDM.Application.Menu
@using KayeDM.Domain.Exceptions
@inject IInventoryService InventoryService
@inject IMenuItemService MenuItemService

<PageTitle>Tray Production</PageTitle>

<h1>Today's Tray Production</h1>

<EditForm Model="_form" OnValidSubmit="SubmitAsync">
    <DataAnnotationsValidator />
    <ValidationSummary />
    <div class="mb-2">
        <label>Dish</label>
        <InputSelect class="form-control" @bind-Value="_form.MenuItemId">
            <option value="0">-- select --</option>
            @foreach (var item in _menuItems)
            {
                <option value="@item.Id">@item.Name</option>
            }
        </InputSelect>
    </div>
    <div class="mb-2">
        <label>Trays Produced</label>
        <InputNumber class="form-control" @bind-Value="_form.TraysProduced" step="0.5" />
    </div>
    <div class="mb-2">
        <label>Servings per Tray</label>
        <InputNumber class="form-control" @bind-Value="_form.ServingsPerTray" />
    </div>
    <button type="submit" class="btn btn-success">Log Batch</button>
</EditForm>

@if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}

<h2 class="mt-4">Today's Batches</h2>

@if (_batches is null)
{
    <p>Loading…</p>
}
else if (_batches.Count == 0)
{
    <p>No batches logged today.</p>
}
else
{
    <table class="table">
        <thead>
            <tr>
                <th>Dish</th>
                <th>Trays Produced</th>
                <th>Servings/Tray</th>
                <th>Total Servings</th>
                <th>Produced At</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var batch in _batches)
            {
                <tr>
                    <td>@batch.MenuItemName</td>
                    <td>@batch.TraysProduced</td>
                    <td>@batch.ServingsPerTray</td>
                    <td>@(batch.TraysProduced * batch.ServingsPerTray)</td>
                    <td>@batch.ProducedAt.ToString("h:mm tt")</td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    private List<MenuItemDto> _menuItems = new();
    private List<DishBatchDto>? _batches;
    private BatchForm _form = new();
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        _menuItems = await MenuItemService.GetAllAsync();
        await LoadBatchesAsync();
    }

    private async Task LoadBatchesAsync() => _batches = await InventoryService.GetTodaysBatchesAsync();

    private async Task SubmitAsync()
    {
        _error = null;
        if (_form.MenuItemId <= 0)
        {
            _error = "Select a dish.";
            return;
        }

        try
        {
            await InventoryService.CreateBatchAsync(new CreateDishBatchRequest(_form.MenuItemId, _form.TraysProduced, _form.ServingsPerTray));
            _form = new BatchForm();
            await LoadBatchesAsync();
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }

    private class BatchForm
    {
        public int MenuItemId { get; set; }

        [Range(0.5, 100)]
        public decimal TraysProduced { get; set; } = 1m;

        [Range(1, 200)]
        public int ServingsPerTray { get; set; } = 10;
    }
}
```

- [ ] **Step 2: Add the nav link**

In `src/KayeDM.Web/Components/Layout/NavMenu.razor`, add after the "Crew Meal Report" nav item:

```razor
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="inventory/production">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Tray Production
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
git commit -m "feat(web): add /inventory/production morning batch entry page"
```

---

### Task 9: POS updates — availability strip + oversell confirm flow

**Files:**
- Modify: `src/KayeDM.Web/Components/Pages/Pos.razor`

**Interfaces:**
- Consumes: `IInventoryService.GetTodaysAvailabilityAsync` (Task 6); `OversoldException` (Task 4); `CreateOrderRequest.OversoldOverride` (Task 7).

- [ ] **Step 1: Inject `IInventoryService` and load availability**

In `src/KayeDM.Web/Components/Pages/Pos.razor`, add the injection near the top:

```razor
@using KayeDM.Application.Inventory
@inject IInventoryService InventoryService
```

In `@code`, add a field and load it alongside menu items:

```csharp
    private List<MenuItemAvailabilityDto> _availability = new();

    protected override async Task OnInitializedAsync()
    {
        _menuItems = await MenuItemService.GetAllAsync();
        _availability = await InventoryService.GetTodaysAvailabilityAsync();
        await RefreshTripsAsync();
    }
```

- [ ] **Step 2: Show the availability strip on each menu button**

Replace the menu-grid button markup:

```razor
                <button class="menu-button" @onclick="() => AddLine(item)">
                    <div>@item.Name</div>
                    <div>@FormatPeso(item.Price)</div>
                </button>
```

with:

```razor
                <button class="menu-button" @onclick="() => AddLine(item)">
                    <div>@item.Name</div>
                    <div>@FormatPeso(item.Price)</div>
                    @{
                        var availability = _availability.FirstOrDefault(a => a.MenuItemId == item.Id);
                    }
                    @if (availability is not null)
                    {
                        @if (!availability.HasBatchToday)
                        {
                            <div class="availability-badge">no batch</div>
                        }
                        else
                        {
                            <div class="availability-badge @(availability.AvailableServings <= 5 ? "availability-low" : "")">
                                @availability.AvailableServings left
                            </div>
                        }
                    }
                </button>
```

- [ ] **Step 3: Catch `OversoldException` distinctly and offer the confirm-and-retry button**

Replace the `CompleteAsync` method:

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
                    BusTripId: _selectedBusTripId,
                    OversoldOverride: _oversoldConfirmed);

                result = await OrderService.CreateOrderAsync(request);
            }

            _lastOrderNumber = result.OrderNumber;
            _lastChange = result.ChangeGiven;
            _ticket.Clear();
            Tendered = 0;
            _isCrewMealMode = false;
            _selectedBusTripId = null;
            _oversoldConfirmed = false;
            _oversoldWarning = null;
            await RefreshTripsAsync();
            _availability = await InventoryService.GetTodaysAvailabilityAsync();
        }
        catch (OversoldException ex)
        {
            _oversoldWarning = ex.Message;
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

    private async Task ConfirmOversoldAndCompleteAsync()
    {
        _oversoldConfirmed = true;
        await CompleteAsync();
    }
```

Add the two new fields next to `_error`:

```csharp
    private string? _error;
    private string? _oversoldWarning;
    private bool _oversoldConfirmed;
```

- [ ] **Step 4: Show the warning + confirm button in the markup**

Replace:

```razor
        @if (_error is not null)
        {
            <div class="alert alert-danger">@_error</div>
        }
```

with:

```razor
        @if (_oversoldWarning is not null)
        {
            <div class="alert alert-warning">
                @_oversoldWarning
                <button type="button" class="btn btn-sm btn-warning" @onclick="ConfirmOversoldAndCompleteAsync">Confirm &amp; Sell Anyway</button>
            </div>
        }
        @if (_error is not null)
        {
            <div class="alert alert-danger">@_error</div>
        }
```

- [ ] **Step 5: Reset the oversell state when the ticket is cleared**

In `ClearTicket()`, add:

```csharp
    private void ClearTicket()
    {
        _ticket.Clear();
        Tendered = 0;
        _lastOrderNumber = null;
        _error = null;
        _oversoldWarning = null;
        _oversoldConfirmed = false;
        _isCrewMealMode = false;
        _selectedBusTripId = null;
    }
```

- [ ] **Step 6: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add availability strip and oversell confirm flow to /pos"
```

---

### Task 10: `/inventory/waste` page

**Files:**
- Create: `src/KayeDM.Web/Components/Pages/InventoryWaste.razor`
- Modify: `src/KayeDM.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `IInventoryService.GetTodaysBatchesAsync`, `LogWasteAsync` (Task 6).

- [ ] **Step 1: Write the page**

`src/KayeDM.Web/Components/Pages/InventoryWaste.razor`:

```razor
@page "/inventory/waste"
@using System.ComponentModel.DataAnnotations
@using KayeDM.Application.Inventory
@using KayeDM.Domain.Enums
@using KayeDM.Domain.Exceptions
@inject IInventoryService InventoryService

<PageTitle>Waste Log</PageTitle>

<h1>Log Waste</h1>

@if (_batches is null)
{
    <p>Loading…</p>
}
else if (_batches.Count == 0)
{
    <p>No batches logged today — nothing to waste yet.</p>
}
else
{
    <EditForm Model="_form" OnValidSubmit="SubmitAsync">
        <DataAnnotationsValidator />
        <ValidationSummary />
        <div class="mb-2">
            <label>Batch</label>
            <InputSelect class="form-control" @bind-Value="_form.DishBatchId">
                <option value="0">-- select --</option>
                @foreach (var batch in _batches)
                {
                    <option value="@batch.Id">@batch.MenuItemName (@batch.TraysProduced trays)</option>
                }
            </InputSelect>
        </div>
        <div class="mb-2">
            <label>Trays Wasted</label>
            <InputNumber class="form-control" @bind-Value="_form.TraysWasted" step="0.5" />
        </div>
        <div class="mb-2">
            <label>Reason</label>
            <InputSelect class="form-control" @bind-Value="_form.Reason">
                @foreach (var reason in Enum.GetValues<WasteReason>())
                {
                    <option value="@reason">@reason</option>
                }
            </InputSelect>
        </div>
        <button type="submit" class="btn btn-success">Log Waste</button>
    </EditForm>
}

@if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}

@if (_lastLogged is not null)
{
    <div class="alert alert-success">Logged @_lastLogged.TraysWasted tray(s) wasted for @_lastLogged.MenuItemName (@_lastLogged.Reason).</div>
}

@code {
    private List<DishBatchDto>? _batches;
    private WasteForm _form = new();
    private string? _error;
    private WasteLogDto? _lastLogged;

    protected override async Task OnInitializedAsync() => _batches = await InventoryService.GetTodaysBatchesAsync();

    private async Task SubmitAsync()
    {
        _error = null;
        _lastLogged = null;

        if (_form.DishBatchId <= 0)
        {
            _error = "Select a batch.";
            return;
        }

        try
        {
            _lastLogged = await InventoryService.LogWasteAsync(new LogWasteRequest(_form.DishBatchId, _form.TraysWasted, _form.Reason));
            _form = new WasteForm();
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }

    private class WasteForm
    {
        public int DishBatchId { get; set; }

        [Range(0.5, 100)]
        public decimal TraysWasted { get; set; } = 0.5m;

        public WasteReason Reason { get; set; } = WasteReason.EndOfDay;
    }
}
```

- [ ] **Step 2: Add the nav link**

In `src/KayeDM.Web/Components/Layout/NavMenu.razor`, add after the "Tray Production" nav item:

```razor
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="inventory/waste">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Waste Log
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
git commit -m "feat(web): add /inventory/waste logging page"
```

---

### Task 11: `/inventory/variance` page

**Files:**
- Create: `src/KayeDM.Web/Components/Pages/InventoryVariance.razor`
- Modify: `src/KayeDM.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `IInventoryService.GetVarianceAsync` (Task 6).

- [ ] **Step 1: Write the page**

`src/KayeDM.Web/Components/Pages/InventoryVariance.razor`:

```razor
@page "/inventory/variance"
@using KayeDM.Application.Inventory
@inject IInventoryService InventoryService

<PageTitle>Variance Report</PageTitle>

<h1>Produced / Sold / Wasted Variance</h1>

<div class="mb-3">
    <label>From</label>
    <input type="date" class="form-control" @bind="_fromInput" />

    <label>To</label>
    <input type="date" class="form-control" @bind="_toInput" />

    <button class="btn btn-primary mt-2" @onclick="LoadAsync">Run Report</button>
</div>

@if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}

@if (_rows is not null)
{
    @if (_rows.Count == 0)
    {
        <p>No batches in this date range.</p>
    }
    else
    {
        <table class="table">
            <thead>
                <tr>
                    <th>Date</th>
                    <th>Dish</th>
                    <th>Produced</th>
                    <th>Sold</th>
                    <th>Wasted</th>
                    <th>Variance %</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var row in _rows)
                {
                    <tr>
                        <td>@row.Date.ToString("MMM d, yyyy")</td>
                        <td>@row.MenuItemName</td>
                        <td>@row.Produced</td>
                        <td>@row.Sold</td>
                        <td>@row.Wasted</td>
                        <td>@row.VariancePercent%</td>
                    </tr>
                }
            </tbody>
        </table>
    }
}

@code {
    private string _fromInput = DateTime.Now.Date.ToString("yyyy-MM-dd");
    private string _toInput = DateTime.Now.Date.ToString("yyyy-MM-dd");
    private List<VarianceRow>? _rows;
    private string? _error;

    private async Task LoadAsync()
    {
        _error = null;
        _rows = null;

        if (!DateOnly.TryParse(_fromInput, out var from) || !DateOnly.TryParse(_toInput, out var to))
        {
            _error = "Select a valid date range.";
            return;
        }

        if (to < from)
        {
            _error = "'To' date must be on or after 'From' date.";
            return;
        }

        _rows = await InventoryService.GetVarianceAsync(from, to);
    }
}
```

- [ ] **Step 2: Add the nav link**

In `src/KayeDM.Web/Components/Layout/NavMenu.razor`, add after the "Waste Log" nav item:

```razor
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="inventory/variance">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Variance Report
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
git commit -m "feat(web): add /inventory/variance report page"
```

---

### Task 12: Domain layer — expense enums/entities

**Files:**
- Create: `src/KayeDM.Domain/Enums/ExpenseCategoryType.cs`
- Create: `src/KayeDM.Domain/Enums/ExpensePaymentMethod.cs`
- Create: `src/KayeDM.Domain/Entities/ExpenseCategory.cs`
- Create: `src/KayeDM.Domain/Entities/Expense.cs`

**Interfaces:**
- Produces: `KayeDM.Domain.Enums.ExpenseCategoryType { Ingredients, Utilities, Wages, Rent, Supplies, Maintenance, Other }`, `KayeDM.Domain.Enums.ExpensePaymentMethod { Cash, GCash, BankTransfer }` (deliberately separate from `KayeDM.Domain.Enums.PaymentMethod`, which stays `{ Cash, GCash }` for `Order` — an expense can be paid by bank transfer, an order cannot), `KayeDM.Domain.Entities.ExpenseCategory`, `KayeDM.Domain.Entities.Expense` — consumed by every later task.

Plain data/enum types with no behavior to unit-test — write them directly.

- [ ] **Step 1: `ExpenseCategoryType` enum**

`src/KayeDM.Domain/Enums/ExpenseCategoryType.cs`:

```csharp
namespace KayeDM.Domain.Enums;

public enum ExpenseCategoryType
{
    Ingredients,
    Utilities,
    Wages,
    Rent,
    Supplies,
    Maintenance,
    Other
}
```

- [ ] **Step 2: `ExpensePaymentMethod` enum**

`src/KayeDM.Domain/Enums/ExpensePaymentMethod.cs`:

```csharp
namespace KayeDM.Domain.Enums;

// Separate from KayeDM.Domain.Enums.PaymentMethod (Cash/GCash only, used by
// Order) because expenses can also be paid by bank transfer.
public enum ExpensePaymentMethod
{
    Cash,
    GCash,
    BankTransfer
}
```

- [ ] **Step 3: `ExpenseCategory` entity**

`src/KayeDM.Domain/Entities/ExpenseCategory.cs`:

```csharp
using KayeDM.Domain.Enums;

namespace KayeDM.Domain.Entities;

public class ExpenseCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ExpenseCategoryType Type { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 4: `Expense` entity**

`src/KayeDM.Domain/Entities/Expense.cs`:

```csharp
using KayeDM.Domain.Enums;

namespace KayeDM.Domain.Entities;

public class Expense
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public int ExpenseCategoryId { get; set; }
    public ExpenseCategory ExpenseCategory { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public ExpensePaymentMethod PaymentMethod { get; set; }
    public string? Vendor { get; set; }
    public string? ReceiptRef { get; set; }

    // TODO Week 4: replace with real Identity user id.
    public string LoggedById { get; set; } = string.Empty;
    public DateTime LoggedAt { get; set; }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(domain): add ExpenseCategory, Expense entities and their enums"
```

---

### Task 13: `AppDbContext` — Expense tables + `AddExpenseTables` migration

**Files:**
- Modify: `src/KayeDM.Infrastructure/Data/AppDbContext.cs`
- Create (generated): `src/KayeDM.Infrastructure/Data/Migrations/*_AddExpenseTables.cs`

**Interfaces:**
- Consumes: `ExpenseCategory`, `Expense` (Task 12).
- Produces: `AppDbContext.ExpenseCategories` (`DbSet<ExpenseCategory>`), `AppDbContext.Expenses` (`DbSet<Expense>`) — consumed by `ExpenseService` (Task 15).

- [ ] **Step 1: Add the DbSets and Fluent config**

In `src/KayeDM.Infrastructure/Data/AppDbContext.cs`, add the DbSets after `WasteLogs`:

```csharp
    public DbSet<WasteLog> WasteLogs => Set<WasteLog>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
```

and Fluent config inside `OnModelCreating`, after the `WasteLog` block:

```csharp
        builder.Entity<ExpenseCategory>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(100).IsRequired();
        });

        builder.Entity<Expense>(entity =>
        {
            entity.Property(e => e.Description).HasMaxLength(250).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(10, 2);
            entity.Property(e => e.Vendor).HasMaxLength(150);
            entity.Property(e => e.ReceiptRef).HasMaxLength(100);
            entity.Property(e => e.LoggedById).HasMaxLength(450);

            entity.HasOne(e => e.ExpenseCategory)
                .WithMany()
                .HasForeignKey(e => e.ExpenseCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 3: Generate the `AddExpenseTables` migration**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations add AddExpenseTables --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj --output-dir Data/Migrations
```

Expected: `Done.`

- [ ] **Step 4: Apply it**

```bash
dotnet ef database update --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected: `Done.` Verify with:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT name FROM sys.tables WHERE name IN ('ExpenseCategories','Expenses');"
```

Expected: both table names returned.

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): add ExpenseCategory/Expense to AppDbContext and AddExpenseTables migration"
```

---

### Task 14: Application layer — Expense DTOs and `IExpenseService`

**Files:**
- Create: `src/KayeDM.Application/Expenses/ExpenseModels.cs`
- Create: `src/KayeDM.Application/Expenses/IExpenseService.cs`

**Interfaces:**
- Consumes: `ExpenseCategoryType`, `ExpensePaymentMethod` (Task 12).
- Produces: `ExpenseCategoryDto(int Id, string Name, ExpenseCategoryType Type, bool IsActive)`, `ExpenseCategoryUpsertDto(string Name, ExpenseCategoryType Type)`, `CreateExpenseRequest(DateTime Date, int ExpenseCategoryId, string Description, decimal Amount, ExpensePaymentMethod PaymentMethod, string? Vendor, string? ReceiptRef)`, `UpdateExpenseRequest` (same shape), `ExpenseDto(int Id, DateTime Date, int ExpenseCategoryId, string CategoryName, string Description, decimal Amount, ExpensePaymentMethod PaymentMethod, string? Vendor, string? ReceiptRef, DateTime LoggedAt)`, `ExpenseMonthlySummaryRow(string CategoryName, IReadOnlyDictionary<string, decimal> AmountsByMonth, decimal Total)`, `ExpenseMonthlySummaryResult(IReadOnlyList<string> Months, IReadOnlyList<ExpenseMonthlySummaryRow> Rows, IReadOnlyDictionary<string, decimal> TotalsByMonth, decimal GrandTotal)`, `IExpenseService` — all consumed by Tasks 15–18.

Plain contracts, no behavior to test.

- [ ] **Step 1: DTOs**

`src/KayeDM.Application/Expenses/ExpenseModels.cs`:

```csharp
using KayeDM.Domain.Enums;

namespace KayeDM.Application.Expenses;

public record ExpenseCategoryDto(int Id, string Name, ExpenseCategoryType Type, bool IsActive);

public record ExpenseCategoryUpsertDto(string Name, ExpenseCategoryType Type);

public record CreateExpenseRequest(
    DateTime Date,
    int ExpenseCategoryId,
    string Description,
    decimal Amount,
    ExpensePaymentMethod PaymentMethod,
    string? Vendor,
    string? ReceiptRef);

public record UpdateExpenseRequest(
    DateTime Date,
    int ExpenseCategoryId,
    string Description,
    decimal Amount,
    ExpensePaymentMethod PaymentMethod,
    string? Vendor,
    string? ReceiptRef);

public record ExpenseDto(
    int Id,
    DateTime Date,
    int ExpenseCategoryId,
    string CategoryName,
    string Description,
    decimal Amount,
    ExpensePaymentMethod PaymentMethod,
    string? Vendor,
    string? ReceiptRef,
    DateTime LoggedAt);

// AmountsByMonth is keyed by "yyyy-MM" — Months carries the same keys in
// display order so the page never needs to sort a Dictionary itself.
public record ExpenseMonthlySummaryRow(string CategoryName, IReadOnlyDictionary<string, decimal> AmountsByMonth, decimal Total);

public record ExpenseMonthlySummaryResult(
    IReadOnlyList<string> Months,
    IReadOnlyList<ExpenseMonthlySummaryRow> Rows,
    IReadOnlyDictionary<string, decimal> TotalsByMonth,
    decimal GrandTotal);
```

- [ ] **Step 2: `IExpenseService`**

`src/KayeDM.Application/Expenses/IExpenseService.cs`:

```csharp
namespace KayeDM.Application.Expenses;

public interface IExpenseService
{
    Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool includeInactive = false);
    Task<ExpenseCategoryDto> CreateCategoryAsync(ExpenseCategoryUpsertDto dto);
    Task<ExpenseCategoryDto> UpdateCategoryAsync(int id, ExpenseCategoryUpsertDto dto);
    Task SetCategoryActiveAsync(int id, bool isActive);

    // Inserts the 7 ExpenseCategoryType defaults if the table is empty. Safe
    // to call on every app start.
    Task SeedDefaultCategoriesAsync();

    Task<ExpenseDto> CreateExpenseAsync(CreateExpenseRequest request);
    Task<ExpenseDto> UpdateExpenseAsync(int id, UpdateExpenseRequest request);
    Task DeleteExpenseAsync(int id);
    Task<List<ExpenseDto>> GetExpensesAsync(DateOnly? from, DateOnly? to, int? categoryId);

    Task<ExpenseMonthlySummaryResult> GetMonthlySummaryAsync(DateOnly from, DateOnly to, int? categoryId);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(application): add Expense DTOs and IExpenseService interface"
```

---

### Task 15: `ExpenseService` implementation + DI + tests (TDD on monthly summary)

**Files:**
- Create: `src/KayeDM.Infrastructure/Expenses/ExpenseService.cs`
- Create: `tests/KayeDM.Tests/Expenses/ExpenseServiceTests.cs`
- Modify: `src/KayeDM.Web/Program.cs`

**Interfaces:**
- Consumes: `IExpenseService` and all Expense DTOs (Task 14); `AppDbContext`, `ExpenseCategory`, `Expense` (Task 13); `DomainException` (existing).
- Produces: `KayeDM.Infrastructure.Expenses.ExpenseService : IExpenseService` — consumed by the `/expenses/*` pages (Tasks 16–18) and `Program.cs` startup seeding (Task 18).

Category CRUD and expense CRUD are plain CRUD (same shape as `MenuItemService` — no dedicated tests). `GetMonthlySummaryAsync` carries the aggregation logic the Week 3 prompt calls out explicitly, so it's built test-first.

- [ ] **Step 1: Write the service, with `GetMonthlySummaryAsync` stubbed**

`src/KayeDM.Infrastructure/Expenses/ExpenseService.cs`:

```csharp
using KayeDM.Application.Expenses;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Expenses;

public class ExpenseService : IExpenseService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public ExpenseService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool includeInactive = false)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var query = db.ExpenseCategories.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.Type, c.IsActive))
            .ToListAsync();
    }

    public async Task<ExpenseCategoryDto> CreateCategoryAsync(ExpenseCategoryUpsertDto dto)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = new ExpenseCategory { Name = dto.Name, Type = dto.Type, IsActive = true };
        db.ExpenseCategories.Add(entity);
        await db.SaveChangesAsync();

        return new ExpenseCategoryDto(entity.Id, entity.Name, entity.Type, entity.IsActive);
    }

    public async Task<ExpenseCategoryDto> UpdateCategoryAsync(int id, ExpenseCategoryUpsertDto dto)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new DomainException($"Expense category {id} not found.");

        entity.Name = dto.Name;
        entity.Type = dto.Type;
        await db.SaveChangesAsync();

        return new ExpenseCategoryDto(entity.Id, entity.Name, entity.Type, entity.IsActive);
    }

    public async Task SetCategoryActiveAsync(int id, bool isActive)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new DomainException($"Expense category {id} not found.");

        entity.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    public async Task SeedDefaultCategoriesAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        if (await db.ExpenseCategories.AnyAsync())
        {
            return;
        }

        foreach (var type in Enum.GetValues<ExpenseCategoryType>())
        {
            db.ExpenseCategories.Add(new ExpenseCategory { Name = type.ToString(), Type = type, IsActive = true });
        }

        await db.SaveChangesAsync();
    }

    public async Task<ExpenseDto> CreateExpenseAsync(CreateExpenseRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == request.ExpenseCategoryId)
            ?? throw new DomainException($"Expense category {request.ExpenseCategoryId} not found.");

        if (request.Amount <= 0)
        {
            throw new DomainException("Expense amount must be positive.");
        }

        var entity = new Expense
        {
            Date = request.Date.Date,
            ExpenseCategoryId = request.ExpenseCategoryId,
            Description = request.Description,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            Vendor = request.Vendor,
            ReceiptRef = request.ReceiptRef,
            LoggedById = "system",
            LoggedAt = DateTime.Now
        };

        db.Expenses.Add(entity);
        await db.SaveChangesAsync();

        return new ExpenseDto(entity.Id, entity.Date, entity.ExpenseCategoryId, category.Name, entity.Description, entity.Amount, entity.PaymentMethod, entity.Vendor, entity.ReceiptRef, entity.LoggedAt);
    }

    public async Task<ExpenseDto> UpdateExpenseAsync(int id, UpdateExpenseRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id)
            ?? throw new DomainException($"Expense {id} not found.");

        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == request.ExpenseCategoryId)
            ?? throw new DomainException($"Expense category {request.ExpenseCategoryId} not found.");

        if (request.Amount <= 0)
        {
            throw new DomainException("Expense amount must be positive.");
        }

        entity.Date = request.Date.Date;
        entity.ExpenseCategoryId = request.ExpenseCategoryId;
        entity.Description = request.Description;
        entity.Amount = request.Amount;
        entity.PaymentMethod = request.PaymentMethod;
        entity.Vendor = request.Vendor;
        entity.ReceiptRef = request.ReceiptRef;

        await db.SaveChangesAsync();

        return new ExpenseDto(entity.Id, entity.Date, entity.ExpenseCategoryId, category.Name, entity.Description, entity.Amount, entity.PaymentMethod, entity.Vendor, entity.ReceiptRef, entity.LoggedAt);
    }

    public async Task DeleteExpenseAsync(int id)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id)
            ?? throw new DomainException($"Expense {id} not found.");

        db.Expenses.Remove(entity);
        await db.SaveChangesAsync();
    }

    public async Task<List<ExpenseDto>> GetExpensesAsync(DateOnly? from, DateOnly? to, int? categoryId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var query = db.Expenses.AsNoTracking().Include(e => e.ExpenseCategory).AsQueryable();

        if (from is not null)
        {
            var fromDate = from.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(e => e.Date >= fromDate);
        }

        if (to is not null)
        {
            var toDate = to.Value.ToDateTime(TimeOnly.MinValue).AddDays(1);
            query = query.Where(e => e.Date < toDate);
        }

        if (categoryId is not null)
        {
            query = query.Where(e => e.ExpenseCategoryId == categoryId);
        }

        return await query
            .OrderByDescending(e => e.Date)
            .Select(e => new ExpenseDto(e.Id, e.Date, e.ExpenseCategoryId, e.ExpenseCategory.Name, e.Description, e.Amount, e.PaymentMethod, e.Vendor, e.ReceiptRef, e.LoggedAt))
            .ToListAsync();
    }

    public Task<ExpenseMonthlySummaryResult> GetMonthlySummaryAsync(DateOnly from, DateOnly to, int? categoryId) => throw new NotImplementedException();
}
```

- [ ] **Step 2: Write the failing monthly-summary tests**

`tests/KayeDM.Tests/Expenses/ExpenseServiceTests.cs`:

```csharp
using FluentAssertions;
using KayeDM.Application.Expenses;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Expenses;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Expenses;

public class ExpenseServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ExpenseService _sut;

    public ExpenseServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new ExpenseService(new TestDbContextFactory(options));

        _db.ExpenseCategories.AddRange(
            new ExpenseCategory { Id = 1, Name = "Ingredients", Type = ExpenseCategoryType.Ingredients, IsActive = true },
            new ExpenseCategory { Id = 2, Name = "Utilities", Type = ExpenseCategoryType.Utilities, IsActive = true });
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
    public async Task GetMonthlySummaryAsync_GroupsByCategory_AndSumsPerMonth()
    {
        _db.Expenses.AddRange(
            new Expense { Date = new DateTime(2026, 6, 5), ExpenseCategoryId = 1, Description = "Rice", Amount = 1000m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "system", LoggedAt = DateTime.Now },
            new Expense { Date = new DateTime(2026, 6, 20), ExpenseCategoryId = 1, Description = "Meat", Amount = 500m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "system", LoggedAt = DateTime.Now },
            new Expense { Date = new DateTime(2026, 7, 3), ExpenseCategoryId = 2, Description = "Electric bill", Amount = 2000m, PaymentMethod = ExpensePaymentMethod.BankTransfer, LoggedById = "system", LoggedAt = DateTime.Now });
        _db.SaveChanges();

        var result = await _sut.GetMonthlySummaryAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31), categoryId: null);

        result.Months.Should().Equal("2026-06", "2026-07");

        var ingredientsRow = result.Rows.Single(r => r.CategoryName == "Ingredients");
        ingredientsRow.AmountsByMonth["2026-06"].Should().Be(1500m);
        ingredientsRow.AmountsByMonth["2026-07"].Should().Be(0m);
        ingredientsRow.Total.Should().Be(1500m);

        var utilitiesRow = result.Rows.Single(r => r.CategoryName == "Utilities");
        utilitiesRow.AmountsByMonth["2026-07"].Should().Be(2000m);

        result.TotalsByMonth["2026-06"].Should().Be(1500m);
        result.TotalsByMonth["2026-07"].Should().Be(2000m);
        result.GrandTotal.Should().Be(3500m);
    }

    [Fact]
    public async Task GetMonthlySummaryAsync_FiltersByCategory_WhenProvided()
    {
        _db.Expenses.AddRange(
            new Expense { Date = new DateTime(2026, 6, 5), ExpenseCategoryId = 1, Description = "Rice", Amount = 1000m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "system", LoggedAt = DateTime.Now },
            new Expense { Date = new DateTime(2026, 6, 6), ExpenseCategoryId = 2, Description = "Water bill", Amount = 300m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "system", LoggedAt = DateTime.Now });
        _db.SaveChanges();

        var result = await _sut.GetMonthlySummaryAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), categoryId: 1);

        result.Rows.Should().ContainSingle();
        result.GrandTotal.Should().Be(1000m);
    }
}
```

- [ ] **Step 3: Run the tests to confirm they fail**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~ExpenseServiceTests"`
Expected: 2 tests run, both FAIL with `System.NotImplementedException`.

- [ ] **Step 4: Implement `GetMonthlySummaryAsync` for real**

Replace the stub line in `src/KayeDM.Infrastructure/Expenses/ExpenseService.cs`:

```csharp
    public Task<ExpenseMonthlySummaryResult> GetMonthlySummaryAsync(DateOnly from, DateOnly to, int? categoryId) => throw new NotImplementedException();
```

with:

```csharp
    public async Task<ExpenseMonthlySummaryResult> GetMonthlySummaryAsync(DateOnly from, DateOnly to, int? categoryId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var toDate = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        var query = db.Expenses.AsNoTracking().Include(e => e.ExpenseCategory)
            .Where(e => e.Date >= fromDate && e.Date < toDate);

        if (categoryId is not null)
        {
            query = query.Where(e => e.ExpenseCategoryId == categoryId);
        }

        var expenses = await query.ToListAsync();

        var months = new List<string>();
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            months.Add(month.ToString("yyyy-MM"));
        }

        var categories = expenses
            .Select(e => (e.ExpenseCategoryId, e.ExpenseCategory.Name))
            .Distinct()
            .OrderBy(c => c.Name)
            .ToList();

        var rows = new List<ExpenseMonthlySummaryRow>();
        foreach (var (categoryIdValue, categoryName) in categories)
        {
            var amountsByMonth = new Dictionary<string, decimal>();
            foreach (var month in months)
            {
                amountsByMonth[month] = expenses
                    .Where(e => e.ExpenseCategoryId == categoryIdValue && e.Date.ToString("yyyy-MM") == month)
                    .Sum(e => e.Amount);
            }

            rows.Add(new ExpenseMonthlySummaryRow(categoryName, amountsByMonth, amountsByMonth.Values.Sum()));
        }

        var totalsByMonth = months.ToDictionary(month => month, month => rows.Sum(r => r.AmountsByMonth[month]));

        return new ExpenseMonthlySummaryResult(months, rows, totalsByMonth, rows.Sum(r => r.Total));
    }
```

- [ ] **Step 5: Run the tests again to confirm they pass**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~ExpenseServiceTests"`
Expected: 2 tests run, both PASS.

- [ ] **Step 6: Register `ExpenseService` in DI**

In `src/KayeDM.Web/Program.cs`, add the using and registration:

```csharp
using KayeDM.Application.Buses;
using KayeDM.Application.Expenses;
using KayeDM.Application.Inventory;
using KayeDM.Application.Menu;
using KayeDM.Application.Orders;
using KayeDM.Infrastructure.Buses;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Expenses;
using KayeDM.Infrastructure.Inventory;
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
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
```

(Leave the rest of `Program.cs` unchanged for now — startup seeding is wired in Task 18.)

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"`
Expected: `Passed! - Failed: 0`.

- [ ] **Step 8: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 9: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): implement ExpenseService with CRUD, seeding, and monthly summary aggregation tests"
```

---

### Task 16: `/expenses` page

**Files:**
- Create: `src/KayeDM.Web/Components/Pages/Expenses.razor`
- Modify: `src/KayeDM.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `IExpenseService` (Task 15, registered in DI).

- [ ] **Step 1: Write the page**

`src/KayeDM.Web/Components/Pages/Expenses.razor`:

```razor
@page "/expenses"
@using System.ComponentModel.DataAnnotations
@using KayeDM.Application.Expenses
@using KayeDM.Domain.Enums
@using KayeDM.Domain.Exceptions
@inject IExpenseService ExpenseService

<PageTitle>Expenses</PageTitle>

<h1>Expenses</h1>

<EditForm Model="_form" OnValidSubmit="SubmitAsync">
    <DataAnnotationsValidator />
    <ValidationSummary />
    <div class="mb-2">
        <label>Date</label>
        <input type="date" class="form-control" @bind="_form.DateInput" />
    </div>
    <div class="mb-2">
        <label>Category</label>
        <InputSelect class="form-control" @bind-Value="_form.ExpenseCategoryId">
            <option value="0">-- select --</option>
            @foreach (var category in _categories)
            {
                <option value="@category.Id">@category.Name</option>
            }
        </InputSelect>
    </div>
    <div class="mb-2">
        <label>Description</label>
        <InputText class="form-control" @bind-Value="_form.Description" />
    </div>
    <div class="mb-2">
        <label>Amount</label>
        <InputNumber class="form-control" @bind-Value="_form.Amount" step="0.01" />
    </div>
    <div class="mb-2">
        <label>Payment Method</label>
        <InputSelect class="form-control" @bind-Value="_form.PaymentMethod">
            @foreach (var method in Enum.GetValues<ExpensePaymentMethod>())
            {
                <option value="@method">@method</option>
            }
        </InputSelect>
    </div>
    <div class="mb-2">
        <label>Vendor (optional)</label>
        <InputText class="form-control" @bind-Value="_form.Vendor" />
    </div>
    <div class="mb-2">
        <label>Receipt Ref (optional)</label>
        <InputText class="form-control" @bind-Value="_form.ReceiptRef" />
    </div>
    <button type="submit" class="btn btn-success">@(_editingId is null ? "Add Expense" : "Save Changes")</button>
    @if (_editingId is not null)
    {
        <button type="button" class="btn btn-link" @onclick="CancelEdit">Cancel</button>
    }
</EditForm>

@if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}

<h2 class="mt-4">Filter</h2>
<div class="mb-3">
    <label>From</label>
    <input type="date" class="form-control" @bind="_filterFrom" />
    <label>To</label>
    <input type="date" class="form-control" @bind="_filterTo" />
    <label>Category</label>
    <select class="form-control" @bind="_filterCategoryId">
        <option value="0">-- all --</option>
        @foreach (var category in _categories)
        {
            <option value="@category.Id">@category.Name</option>
        }
    </select>
    <button class="btn btn-primary mt-2" @onclick="LoadExpensesAsync">Filter</button>
</div>

@if (_expenses is null)
{
    <p>Loading…</p>
}
else if (_expenses.Count == 0)
{
    <p>No expenses match this filter.</p>
}
else
{
    <table class="table">
        <thead>
            <tr>
                <th>Date</th>
                <th>Category</th>
                <th>Description</th>
                <th>Amount</th>
                <th>Method</th>
                <th>Vendor</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var expense in _expenses)
            {
                <tr>
                    <td>@expense.Date.ToString("MMM d, yyyy")</td>
                    <td>@expense.CategoryName</td>
                    <td>@expense.Description</td>
                    <td>@FormatPeso(expense.Amount)</td>
                    <td>@expense.PaymentMethod</td>
                    <td>@expense.Vendor</td>
                    <td>
                        <button class="btn btn-sm btn-secondary" @onclick="() => StartEdit(expense)">Edit</button>
                        <button class="btn btn-sm btn-outline-danger" @onclick="() => DeleteAsync(expense.Id)">Delete</button>
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    private List<ExpenseCategoryDto> _categories = new();
    private List<ExpenseDto>? _expenses;
    private ExpenseForm _form = new();
    private int? _editingId;
    private string? _error;

    private string _filterFrom = "";
    private string _filterTo = "";
    private int _filterCategoryId;

    protected override async Task OnInitializedAsync()
    {
        _categories = await ExpenseService.GetCategoriesAsync();
        await LoadExpensesAsync();
    }

    private async Task LoadExpensesAsync()
    {
        DateOnly? from = DateOnly.TryParse(_filterFrom, out var f) ? f : null;
        DateOnly? to = DateOnly.TryParse(_filterTo, out var t) ? t : null;
        int? categoryId = _filterCategoryId > 0 ? _filterCategoryId : null;

        _expenses = await ExpenseService.GetExpensesAsync(from, to, categoryId);
    }

    private void StartEdit(ExpenseDto expense)
    {
        _editingId = expense.Id;
        _form = new ExpenseForm
        {
            DateInput = expense.Date.ToString("yyyy-MM-dd"),
            ExpenseCategoryId = expense.ExpenseCategoryId,
            Description = expense.Description,
            Amount = expense.Amount,
            PaymentMethod = expense.PaymentMethod,
            Vendor = expense.Vendor,
            ReceiptRef = expense.ReceiptRef
        };
    }

    private void CancelEdit()
    {
        _editingId = null;
        _form = new ExpenseForm();
    }

    private async Task SubmitAsync()
    {
        _error = null;

        if (_form.ExpenseCategoryId <= 0)
        {
            _error = "Select a category.";
            return;
        }

        if (!DateOnly.TryParse(_form.DateInput, out var date))
        {
            _error = "Select a valid date.";
            return;
        }

        try
        {
            if (_editingId is null)
            {
                var request = new CreateExpenseRequest(
                    date.ToDateTime(TimeOnly.MinValue), _form.ExpenseCategoryId, _form.Description, _form.Amount, _form.PaymentMethod, _form.Vendor, _form.ReceiptRef);
                await ExpenseService.CreateExpenseAsync(request);
            }
            else
            {
                var request = new UpdateExpenseRequest(
                    date.ToDateTime(TimeOnly.MinValue), _form.ExpenseCategoryId, _form.Description, _form.Amount, _form.PaymentMethod, _form.Vendor, _form.ReceiptRef);
                await ExpenseService.UpdateExpenseAsync(_editingId.Value, request);
            }

            CancelEdit();
            await LoadExpensesAsync();
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }

    private async Task DeleteAsync(int id)
    {
        await ExpenseService.DeleteExpenseAsync(id);
        await LoadExpensesAsync();
    }

    private static string FormatPeso(decimal amount) => string.Format("₱{0:N2}", amount);

    private class ExpenseForm
    {
        public string DateInput { get; set; } = DateTime.Now.Date.ToString("yyyy-MM-dd");
        public int ExpenseCategoryId { get; set; }

        [Required]
        public string Description { get; set; } = "";

        [Range(0.01, 10_000_000)]
        public decimal Amount { get; set; }

        public ExpensePaymentMethod PaymentMethod { get; set; } = ExpensePaymentMethod.Cash;
        public string? Vendor { get; set; }
        public string? ReceiptRef { get; set; }
    }
}
```

- [ ] **Step 2: Add the nav link**

In `src/KayeDM.Web/Components/Layout/NavMenu.razor`, add after the "Variance Report" nav item:

```razor
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="expenses">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Expenses
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
git commit -m "feat(web): add /expenses quick entry and filterable list page"
```

---

### Task 17: `/expenses/categories` page

**Files:**
- Create: `src/KayeDM.Web/Components/Pages/ExpenseCategories.razor`
- Modify: `src/KayeDM.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `IExpenseService.GetCategoriesAsync`, `CreateCategoryAsync`, `UpdateCategoryAsync`, `SetCategoryActiveAsync` (Task 15).

- [ ] **Step 1: Write the page**

`src/KayeDM.Web/Components/Pages/ExpenseCategories.razor`:

```razor
@page "/expenses/categories"
@using System.ComponentModel.DataAnnotations
@using KayeDM.Application.Expenses
@using KayeDM.Domain.Enums
@inject IExpenseService ExpenseService

<PageTitle>Expense Categories</PageTitle>

<h1>Expense Categories</h1>

<button class="btn btn-primary mb-3" @onclick="StartCreate">+ Add Category</button>

@if (_categories is null)
{
    <p>Loading…</p>
}
else if (_categories.Count == 0)
{
    <p>No expense categories yet.</p>
}
else
{
    <table class="table">
        <thead>
            <tr>
                <th>Name</th>
                <th>Type</th>
                <th>Status</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var category in _categories)
            {
                <tr>
                    <td>@category.Name</td>
                    <td>@category.Type</td>
                    <td>@(category.IsActive ? "Active" : "Inactive")</td>
                    <td>
                        <button class="btn btn-sm btn-secondary" @onclick="() => StartEdit(category)">Edit</button>
                        <button class="btn btn-sm btn-outline-danger" @onclick="() => ToggleActiveAsync(category)">
                            @(category.IsActive ? "Deactivate" : "Activate")
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
            <label>Type</label>
            <InputSelect class="form-control" @bind-Value="_form.Type">
                @foreach (var type in Enum.GetValues<ExpenseCategoryType>())
                {
                    <option value="@type">@type</option>
                }
            </InputSelect>
        </div>
        <button type="submit" class="btn btn-success">Save</button>
        <button type="button" class="btn btn-link" @onclick="CancelEdit">Cancel</button>
    </EditForm>
}

@code {
    private List<ExpenseCategoryDto>? _categories;
    private bool _editing;
    private int _editingId;
    private CategoryForm _form = new();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync() => _categories = await ExpenseService.GetCategoriesAsync(includeInactive: true);

    private void StartCreate()
    {
        _editingId = 0;
        _form = new CategoryForm();
        _editing = true;
    }

    private void StartEdit(ExpenseCategoryDto category)
    {
        _editingId = category.Id;
        _form = new CategoryForm { Name = category.Name, Type = category.Type };
        _editing = true;
    }

    private void CancelEdit() => _editing = false;

    private async Task SaveAsync()
    {
        var dto = new ExpenseCategoryUpsertDto(_form.Name, _form.Type);
        if (_editingId > 0)
        {
            await ExpenseService.UpdateCategoryAsync(_editingId, dto);
        }
        else
        {
            await ExpenseService.CreateCategoryAsync(dto);
        }

        _editing = false;
        await LoadAsync();
    }

    private async Task ToggleActiveAsync(ExpenseCategoryDto category)
    {
        await ExpenseService.SetCategoryActiveAsync(category.Id, !category.IsActive);
        await LoadAsync();
    }

    private class CategoryForm
    {
        [Required]
        public string Name { get; set; } = "";

        public ExpenseCategoryType Type { get; set; } = ExpenseCategoryType.Other;
    }
}
```

- [ ] **Step 2: Add the nav link**

In `src/KayeDM.Web/Components/Layout/NavMenu.razor`, add after the "Expenses" nav item:

```razor
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="expenses/categories">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Expense Categories
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
git commit -m "feat(web): add /expenses/categories CRUD page"
```

---

### Task 18: `/expenses/report` page + wire startup category seeding

**Files:**
- Create: `src/KayeDM.Web/Components/Pages/ExpenseReport.razor`
- Modify: `src/KayeDM.Web/Components/Layout/NavMenu.razor`
- Modify: `src/KayeDM.Web/Program.cs`

**Interfaces:**
- Consumes: `IExpenseService.GetMonthlySummaryAsync` (Task 15).

- [ ] **Step 1: Write the page**

`src/KayeDM.Web/Components/Pages/ExpenseReport.razor`:

```razor
@page "/expenses/report"
@using KayeDM.Application.Expenses
@inject IExpenseService ExpenseService

<PageTitle>Expense Report</PageTitle>

<h1>Monthly Expense Summary</h1>

<div class="mb-3">
    <label>From</label>
    <input type="date" class="form-control" @bind="_fromInput" />
    <label>To</label>
    <input type="date" class="form-control" @bind="_toInput" />
    <label>Category</label>
    <select class="form-control" @bind="_categoryId">
        <option value="0">-- all --</option>
        @foreach (var category in _categories)
        {
            <option value="@category.Id">@category.Name</option>
        }
    </select>
    <button class="btn btn-primary mt-2" @onclick="LoadAsync">Run Report</button>
</div>

@if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}

@if (_summary is not null)
{
    @if (_summary.Rows.Count == 0)
    {
        <p>No expenses in this range.</p>
    }
    else
    {
        <table class="table">
            <thead>
                <tr>
                    <th>Category</th>
                    @foreach (var month in _summary.Months)
                    {
                        <th>@month</th>
                    }
                    <th>Total</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var row in _summary.Rows)
                {
                    <tr>
                        <td>@row.CategoryName</td>
                        @foreach (var month in _summary.Months)
                        {
                            <td>@FormatPeso(row.AmountsByMonth[month])</td>
                        }
                        <td>@FormatPeso(row.Total)</td>
                    </tr>
                }
            </tbody>
            <tfoot>
                <tr>
                    <th>Grand Total</th>
                    @foreach (var month in _summary.Months)
                    {
                        <th>@FormatPeso(_summary.TotalsByMonth[month])</th>
                    }
                    <th>@FormatPeso(_summary.GrandTotal)</th>
                </tr>
            </tfoot>
        </table>
    }
}

@code {
    private List<ExpenseCategoryDto> _categories = new();
    private string _fromInput = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).ToString("yyyy-MM-dd");
    private string _toInput = DateTime.Now.Date.ToString("yyyy-MM-dd");
    private int _categoryId;
    private ExpenseMonthlySummaryResult? _summary;
    private string? _error;

    protected override async Task OnInitializedAsync() => _categories = await ExpenseService.GetCategoriesAsync(includeInactive: true);

    private async Task LoadAsync()
    {
        _error = null;
        _summary = null;

        if (!DateOnly.TryParse(_fromInput, out var from) || !DateOnly.TryParse(_toInput, out var to))
        {
            _error = "Select a valid date range.";
            return;
        }

        if (to < from)
        {
            _error = "'To' date must be on or after 'From' date.";
            return;
        }

        int? categoryId = _categoryId > 0 ? _categoryId : null;
        _summary = await ExpenseService.GetMonthlySummaryAsync(from, to, categoryId);
    }

    private static string FormatPeso(decimal amount) => string.Format("₱{0:N2}", amount);
}
```

- [ ] **Step 2: Add the nav link**

In `src/KayeDM.Web/Components/Layout/NavMenu.razor`, add after the "Expense Categories" nav item:

```razor
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="expenses/report">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Expense Report
            </NavLink>
        </div>
```

- [ ] **Step 3: Wire startup category seeding**

In `src/KayeDM.Web/Program.cs`, after `var app = builder.Build();` and before `// Configure the HTTP request pipeline.`, add:

```csharp
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var expenseService = scope.ServiceProvider.GetRequiredService<IExpenseService>();
    await expenseService.SeedDefaultCategoriesAsync();
}

// Configure the HTTP request pipeline.
```

- [ ] **Step 4: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 5: Manually verify seeding**

Run the app once (`dotnet run --project src/KayeDM.Web`), stop it, then check:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT Name, Type FROM ExpenseCategories ORDER BY Id;"
```

Expected: 7 rows, one per `ExpenseCategoryType` value (Ingredients, Utilities, Wages, Rent, Supplies, Maintenance, Other).

- [ ] **Step 6: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add /expenses/report monthly summary page and wire default category seeding on startup"
```

---

### Task 19: Final verification

**Files:** none (verification only, commit only if a bug fix is needed).

**Interfaces:** none — this task exercises everything built in Tasks 0–18.

- [ ] **Step 1: Full build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"`
Expected: `Passed! - Failed: 0`. Total should be 15 (Weeks 1–2) + 5 (`AvailabilityCalculatorTests`) + 3 (`InventoryServiceTests`) + 4 (`OversellOrderServiceTests`) + 2 (`ExpenseServiceTests`) = 29.

- [ ] **Step 3: Confirm migration history**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations list --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected order: `InitialCreate`, `AddOrderTables`, `AddBusCompanyAndTrip`, `AddCrewMealCredit`, `LinkOrderToBusTrip`, `AddDishBatch`, `AddWasteLog`, `AddOversoldFlag`, `AddExpenseTables` — all `(Applied)`.

- [ ] **Step 4: Manual smoke of every new route**

Run the app (`dotnet run --project src/KayeDM.Web`) and confirm each of these returns 200 with no server exceptions logged: `/inventory/production`, `/inventory/waste`, `/inventory/variance`, `/expenses`, `/expenses/categories`, `/expenses/report`, and re-check `/pos` still loads with the availability strip visible.

If a Claude-in-Chrome (or Playwright) browser tool is connected, additionally walk the golden path interactively: log a batch on `/inventory/production` → confirm it appears on the POS availability strip → sell past the available count on `/pos` and confirm the `OversoldException` warning + confirm-and-complete flow works → log waste on `/inventory/waste` against that batch → confirm `/inventory/variance` reflects produced/sold/wasted for today → add an expense on `/expenses` → confirm it appears in `/expenses/report`'s monthly summary. If no browser tool is connected, state that explicitly rather than claiming the walkthrough was done (per Week 2's precedent, Task 12).

- [ ] **Step 5: Record the outcome**

Append a line to `.superpowers/sdd/progress.md` summarizing the result (pass/fail counts, any deviations), matching the style of the existing Week 1/Week 2 entries.
