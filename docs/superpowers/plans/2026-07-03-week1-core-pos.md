# Week 1 — Core POS Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the KayeDM.BMS solution skeleton per `docs/kaye-dm-bms-blueprint.md` §3 and implement Week 1 scope from `kaye-dm-agent-prompts-weeks-1-5.md`: MenuItem/Order/OrderLine entities, two migrations (`InitialCreate`, `AddOrderTables`), `IMenuItemService`/`IOrderService`, a working `/menu` CRUD page, a working `/pos` page (order entry → cash/GCash payment → save), and 7 xUnit tests covering order math and void rules.

**Architecture:** 5-project solution (`KayeDM.Domain` ← `KayeDM.Application` ← `KayeDM.Infrastructure`, `KayeDM.Web` → Application + Infrastructure, `KayeDM.Tests` → all three) restructured under `src/`/`tests/` per blueprint §3. Plain services with constructor injection, no MediatR/AutoMapper/repository wrappers. EF Core migrations built incrementally in two waves (Identity+MenuItem, then Order+OrderLine) to seed the "migrations as a feature" history the blueprint (§9) wants across all 5 weeks.

**Tech Stack:** ASP.NET Core 8 (Blazor Server, Interactive Server, Global interactivity — already configured in `App.razor`), EF Core 8.0.11, SQL Server (LocalDB), ASP.NET Core Identity (schema only this week, no login UI), xUnit + FluentAssertions + EF Core Sqlite (in-memory) for tests.

## Global Constraints

- Packages pinned to **8.0.11** for every `Microsoft.EntityFrameworkCore.*` and `Microsoft.AspNetCore.Identity.EntityFrameworkCore` package. Never upgrade to EF 9/10. `TargetFramework` stays `net8.0` in every csproj (the .NET 10 SDK installed on this machine can still build net8.0 — `Microsoft.AspNetCore.App 8.0.28` runtime is present).
- Migrations are sacred: one per schema change, descriptive names, never delete/regenerate/squash/edit an existing migration. A wrong migration gets a corrective migration, not an edit.
- `dotnet ef` CLI calls always use `--project <path-to-Infrastructure.csproj> --startup-project <path-to-Web.csproj>`.
- Layering: no EF Core types in `KayeDM.Domain`; `KayeDM.Web` talks to `KayeDM.Application` interfaces only, never to `AppDbContext` directly.
- No MediatR, AutoMapper, or repository wrappers. Plain services + constructor injection.
- Prefer pure Blazor over JS interop. Currency format is always `"₱{0:N2}"`. File-scoped namespaces are NOT required by the existing scaffold's style (current stub files use block namespaces) — new files in this plan use file-scoped `namespace X;` for brevity; this is a deviation from the current stub style but matches the prompt's stated convention ("File-scoped namespaces, nullable enabled") — flag this in the Week 1 deliverable notes.
- Nullable reference types enabled everywhere (already set in every csproj).
- Out of scope this week (do not build): buses, crew meals beyond the bare `Order.IsCrewMeal`/`BusTripId` extension points, inventory, waste, expenses, dashboard, closing, auth UI, seeding, charts, Docker, deployment.
- All commands below assume the working directory is the repo root `C:\Users\monst\source\repos\kaye-dm-bms` unless a step explicitly `cd`s elsewhere — paths are written relative to that root (e.g. `KayeDM.BMS/src/...`).

---

### Task 0: Git init + restructure solution into `src/`/`tests/`

**Files:**
- Create: `.gitignore` (repo root)
- Move: `KayeDM.BMS/KayeDM.Domain/` → `KayeDM.BMS/src/KayeDM.Domain/`
- Move: `KayeDM.BMS/KayeDM.Application/` → `KayeDM.BMS/src/KayeDM.Application/`
- Move: `KayeDM.BMS/KayeDM.Infrastructure/` → `KayeDM.BMS/src/KayeDM.Infrastructure/`
- Move: `KayeDM.BMS/KayeDM.Web/` → `KayeDM.BMS/src/KayeDM.Web/`
- Move: `KayeDM.BMS/KayeDM.Tests/` → `KayeDM.BMS/tests/KayeDM.Tests/`
- Modify: `KayeDM.BMS/KayeDM.BMS.slnx`
- Delete: `KayeDM.BMS/src/KayeDM.Domain/Class1.cs`, `KayeDM.BMS/src/KayeDM.Application/Class1.cs`, `KayeDM.BMS/src/KayeDM.Infrastructure/Class1.cs`, `KayeDM.BMS/tests/KayeDM.Tests/UnitTest1.cs`

**Interfaces:** None yet — this task only establishes project structure and references.

- [ ] **Step 1: Initialize git and commit the current scaffold as-is**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git init
```

Create `.gitignore`:

```
bin/
obj/
.vs/
*.user
```

```bash
git add -A
git commit -m "chore: initial scaffold, blueprint, and weekly prompts"
```

- [ ] **Step 2: Remove build artifacts and move projects into src/tests**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
rm -rf KayeDM.Domain/bin KayeDM.Domain/obj
rm -rf KayeDM.Application/bin KayeDM.Application/obj
rm -rf KayeDM.Infrastructure/bin KayeDM.Infrastructure/obj
rm -rf KayeDM.Web/bin KayeDM.Web/obj
rm -rf KayeDM.Tests/bin KayeDM.Tests/obj
rm -rf .vs
mkdir -p src tests
mv KayeDM.Domain src/
mv KayeDM.Application src/
mv KayeDM.Infrastructure src/
mv KayeDM.Web src/
mv KayeDM.Tests tests/
```

- [ ] **Step 3: Delete the template stub files**

```bash
rm "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\src\KayeDM.Domain\Class1.cs"
rm "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\src\KayeDM.Application\Class1.cs"
rm "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\src\KayeDM.Infrastructure\Class1.cs"
rm "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\UnitTest1.cs"
```

- [ ] **Step 4: Update the solution file to point at the new paths**

Replace the full contents of `KayeDM.BMS/KayeDM.BMS.slnx`:

```xml
<Solution>
  <Project Path="src/KayeDM.Domain/KayeDM.Domain.csproj" />
  <Project Path="src/KayeDM.Application/KayeDM.Application.csproj" />
  <Project Path="src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj" />
  <Project Path="src/KayeDM.Web/KayeDM.Web.csproj" />
  <Project Path="tests/KayeDM.Tests/KayeDM.Tests.csproj" />
</Solution>
```

- [ ] **Step 5: Wire up project references**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet add src/KayeDM.Application/KayeDM.Application.csproj reference src/KayeDM.Domain/KayeDM.Domain.csproj
dotnet add src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj reference src/KayeDM.Application/KayeDM.Application.csproj
dotnet add src/KayeDM.Web/KayeDM.Web.csproj reference src/KayeDM.Application/KayeDM.Application.csproj
dotnet add src/KayeDM.Web/KayeDM.Web.csproj reference src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj
dotnet add tests/KayeDM.Tests/KayeDM.Tests.csproj reference src/KayeDM.Domain/KayeDM.Domain.csproj
dotnet add tests/KayeDM.Tests/KayeDM.Tests.csproj reference src/KayeDM.Application/KayeDM.Application.csproj
dotnet add tests/KayeDM.Tests/KayeDM.Tests.csproj reference src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj
```

- [ ] **Step 6: Build to confirm the restructure didn't break anything**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.` with 0 errors (warnings about unused stub deletions are fine since those files are gone).

- [ ] **Step 7: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "chore: restructure solution into src/ and tests/ per blueprint §3"
```

---

### Task 1: Add NuGet packages and confirm the EF Core CLI tool

**Files:**
- Modify: `KayeDM.BMS/src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj`
- Modify: `KayeDM.BMS/src/KayeDM.Web/KayeDM.Web.csproj`
- Modify: `KayeDM.BMS/tests/KayeDM.Tests/KayeDM.Tests.csproj`

**Interfaces:** None — package additions only.

- [ ] **Step 1: Check whether the `dotnet-ef` global tool is already installed**

Run: `dotnet tool list --global`
If `dotnet-ef` is not listed, install it pinned to match the package version:

```bash
dotnet tool install --global dotnet-ef --version 8.0.11
```

If it's already installed at a different version, that's fine — the CLI tool version doesn't need to match the package version exactly, but keep it 8.x.

- [ ] **Step 2: Add EF Core + Identity packages to Infrastructure**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet add src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj package Microsoft.EntityFrameworkCore.SqlServer --version 8.0.11
dotnet add src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Design --version 8.0.11
dotnet add src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 8.0.11
```

- [ ] **Step 3: Add EF Core Design to Web (needed for `dotnet ef` to resolve the startup project)**

```bash
dotnet add src/KayeDM.Web/KayeDM.Web.csproj package Microsoft.EntityFrameworkCore.Design --version 8.0.11
```

- [ ] **Step 4: Add test packages**

```bash
dotnet add tests/KayeDM.Tests/KayeDM.Tests.csproj package FluentAssertions --version 6.12.1
dotnet add tests/KayeDM.Tests/KayeDM.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.11
```

- [ ] **Step 5: Restore and build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "chore: add EF Core, Identity, and test packages pinned to 8.0.11"
```

---

### Task 2: Domain layer — enums, entities, domain exception

**Files:**
- Create: `KayeDM.BMS/src/KayeDM.Domain/Enums/MenuCategory.cs`
- Create: `KayeDM.BMS/src/KayeDM.Domain/Enums/OrderStatus.cs`
- Create: `KayeDM.BMS/src/KayeDM.Domain/Enums/PaymentMethod.cs`
- Create: `KayeDM.BMS/src/KayeDM.Domain/Exceptions/DomainException.cs`
- Create: `KayeDM.BMS/src/KayeDM.Domain/Entities/MenuItem.cs`
- Create: `KayeDM.BMS/src/KayeDM.Domain/Entities/Order.cs`
- Create: `KayeDM.BMS/src/KayeDM.Domain/Entities/OrderLine.cs`

**Interfaces:**
- Produces: `KayeDM.Domain.Enums.MenuCategory { Ulam, Rice, Drinks, Snacks, Dessert }`, `KayeDM.Domain.Enums.OrderStatus { Completed, Voided }`, `KayeDM.Domain.Enums.PaymentMethod { Cash, GCash }`, `KayeDM.Domain.Exceptions.DomainException(string message)`, `KayeDM.Domain.Entities.MenuItem`, `KayeDM.Domain.Entities.Order`, `KayeDM.Domain.Entities.OrderLine` — all consumed by every later task.

These are plain data/enum types with no behavior to unit-test — write them directly.

- [ ] **Step 1: Enums**

`KayeDM.BMS/src/KayeDM.Domain/Enums/MenuCategory.cs`:

```csharp
namespace KayeDM.Domain.Enums;

public enum MenuCategory
{
    Ulam,
    Rice,
    Drinks,
    Snacks,
    Dessert
}
```

`KayeDM.BMS/src/KayeDM.Domain/Enums/OrderStatus.cs`:

```csharp
namespace KayeDM.Domain.Enums;

public enum OrderStatus
{
    Completed,
    Voided
}
```

`KayeDM.BMS/src/KayeDM.Domain/Enums/PaymentMethod.cs`:

```csharp
namespace KayeDM.Domain.Enums;

public enum PaymentMethod
{
    Cash,
    GCash
}
```

- [ ] **Step 2: Domain exception**

`KayeDM.BMS/src/KayeDM.Domain/Exceptions/DomainException.cs`:

```csharp
namespace KayeDM.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
```

- [ ] **Step 3: Entities**

`KayeDM.BMS/src/KayeDM.Domain/Entities/MenuItem.cs`:

```csharp
using KayeDM.Domain.Enums;

namespace KayeDM.Domain.Entities;

public class MenuItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MenuCategory Category { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;

    // Drives POS grid layout — best-sellers top-left.
    public int SortOrder { get; set; }
}
```

`KayeDM.BMS/src/KayeDM.Domain/Entities/Order.cs`:

```csharp
using KayeDM.Domain.Enums;

namespace KayeDM.Domain.Entities;

public class Order
{
    public int Id { get; set; }

    // Daily sequence, e.g. "20260703-041".
    public string OrderNumber { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // Identity user id (AspNetUsers.Id). Nullable this week — no login UI until Week 4.
    public string? CashierId { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Completed;
    public PaymentMethod PaymentMethod { get; set; }
    public decimal AmountTendered { get; set; }
    public decimal ChangeGiven { get; set; }

    // TODO Week 2: FK to BusTrip
    public int? BusTripId { get; set; }

    public bool IsCrewMeal { get; set; } = false;

    // Set by IOrderService.VoidOrderAsync. Blueprint §4 doesn't list a storage
    // column for this, but domain rule 4 ("voiding requires a reason") needs one.
    public string? VoidReason { get; set; }

    public List<OrderLine> Lines { get; set; } = new();
}
```

`KayeDM.BMS/src/KayeDM.Domain/Entities/OrderLine.cs`:

```csharp
namespace KayeDM.Domain.Entities;

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public int Quantity { get; set; }

    // Snapshot at sale time — menu price edits must never rewrite history.
    public decimal UnitPriceAtSale { get; set; }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(domain): add MenuItem, Order, OrderLine entities and enums"
```

---

### Task 3: AppDbContext (Identity + MenuItem) + connection string + `InitialCreate` migration

**Files:**
- Create: `KayeDM.BMS/src/KayeDM.Infrastructure/Data/AppDbContext.cs`
- Modify: `KayeDM.BMS/src/KayeDM.Web/appsettings.json`
- Modify: `KayeDM.BMS/src/KayeDM.Web/Program.cs`
- Create (generated): `KayeDM.BMS/src/KayeDM.Infrastructure/Data/Migrations/*_InitialCreate.cs` and `AppDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: `KayeDM.Domain.Entities.MenuItem` (Task 2).
- Produces: `KayeDM.Infrastructure.Data.AppDbContext : IdentityDbContext<IdentityUser>` with `DbSet<MenuItem> MenuItems` — consumed by every later task that touches the database.

- [ ] **Step 1: Write AppDbContext with only Identity + MenuItem**

`KayeDM.BMS/src/KayeDM.Infrastructure/Data/AppDbContext.cs`:

```csharp
using KayeDM.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MenuItem>(entity =>
        {
            entity.Property(m => m.Name).HasMaxLength(100).IsRequired();
            entity.Property(m => m.Price).HasPrecision(10, 2);
        });
    }
}
```

- [ ] **Step 2: Add the connection string**

Replace the contents of `KayeDM.BMS/src/KayeDM.Web/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "KayeDmBms": "Server=(localdb)\\mssqllocaldb;Database=KayeDmBms;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 3: Register the DbContext in Program.cs**

Replace the contents of `KayeDM.BMS/src/KayeDM.Web/Program.cs`:

```csharp
using KayeDM.Infrastructure.Data;
using KayeDM.Web.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("KayeDmBms")));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

- [ ] **Step 4: Build before generating the migration**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 5: Generate the `InitialCreate` migration**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations add InitialCreate --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj --output-dir Data/Migrations
```

Expected: `Done. To undo this action, use 'ef migrations remove'` and new files under
`src/KayeDM.Infrastructure/Data/Migrations/`.

- [ ] **Step 6: Apply it to LocalDB and verify**

```bash
dotnet ef database update --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected: `Done.` Verify with:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT name FROM sys.tables ORDER BY name;"
```

Expected output includes `MenuItems`, `AspNetUsers`, `AspNetRoles`, and the other standard Identity tables.

- [ ] **Step 7: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): add AppDbContext (Identity + MenuItem) and InitialCreate migration"
```

---

### Task 4: Extend AppDbContext with Order/OrderLine + `AddOrderTables` migration

**Files:**
- Modify: `KayeDM.BMS/src/KayeDM.Infrastructure/Data/AppDbContext.cs`
- Create (generated): `KayeDM.BMS/src/KayeDM.Infrastructure/Data/Migrations/*_AddOrderTables.cs`

**Interfaces:**
- Consumes: `KayeDM.Domain.Entities.Order`, `OrderLine` (Task 2); `AppDbContext` (Task 3).
- Produces: `AppDbContext.Orders` (`DbSet<Order>`), `AppDbContext.OrderLines` (`DbSet<OrderLine>`) — consumed by `OrderService` (Task 7).

- [ ] **Step 1: Add the DbSets and Fluent config**

In `KayeDM.BMS/src/KayeDM.Infrastructure/Data/AppDbContext.cs`, add two DbSets and extend `OnModelCreating`:

```csharp
using KayeDM.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MenuItem>(entity =>
        {
            entity.Property(m => m.Name).HasMaxLength(100).IsRequired();
            entity.Property(m => m.Price).HasPrecision(10, 2);
        });

        builder.Entity<Order>(entity =>
        {
            entity.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
            entity.HasIndex(o => o.OrderNumber).IsUnique();
            entity.Property(o => o.CashierId).HasMaxLength(450);
            entity.Property(o => o.AmountTendered).HasPrecision(10, 2);
            entity.Property(o => o.ChangeGiven).HasPrecision(10, 2);
            entity.Property(o => o.VoidReason).HasMaxLength(250);

            entity.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OrderLine>(entity =>
        {
            entity.Property(l => l.UnitPriceAtSale).HasPrecision(10, 2);

            entity.HasOne(l => l.MenuItem)
                .WithMany()
                .HasForeignKey(l => l.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 3: Generate the `AddOrderTables` migration**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations add AddOrderTables --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj --output-dir Data/Migrations
```

Expected: `Done.` and a new migration file alongside `InitialCreate`.

- [ ] **Step 4: Apply it**

```bash
dotnet ef database update --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected: `Done.` Verify with:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT name FROM sys.tables WHERE name IN ('Orders','OrderLines');"
```

Expected: both table names returned.

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): add Order/OrderLine to AppDbContext and AddOrderTables migration"
```

---

### Task 5: Application layer — DTOs and service interfaces

**Files:**
- Create: `KayeDM.BMS/src/KayeDM.Application/Menu/MenuItemDto.cs`
- Create: `KayeDM.BMS/src/KayeDM.Application/Menu/MenuItemUpsertDto.cs`
- Create: `KayeDM.BMS/src/KayeDM.Application/Menu/IMenuItemService.cs`
- Create: `KayeDM.BMS/src/KayeDM.Application/Orders/OrderModels.cs`
- Create: `KayeDM.BMS/src/KayeDM.Application/Orders/IOrderService.cs`

**Interfaces:**
- Consumes: `KayeDM.Domain.Enums.MenuCategory`, `PaymentMethod` (Task 2).
- Produces: `MenuItemDto(int Id, string Name, MenuCategory Category, decimal Price, bool IsActive, int SortOrder)`, `MenuItemUpsertDto(string Name, MenuCategory Category, decimal Price, int SortOrder)`, `IMenuItemService`, `OrderLineRequest(int MenuItemId, int Quantity)`, `CreateOrderRequest(IReadOnlyList<OrderLineRequest> Lines, PaymentMethod PaymentMethod, decimal AmountTendered, string? CashierId)`, `OrderLineResult(int MenuItemId, string MenuItemName, int Quantity, decimal UnitPriceAtSale, decimal LineTotal)`, `OrderResult(int Id, string OrderNumber, DateTime CreatedAt, decimal Total, decimal AmountTendered, decimal ChangeGiven, IReadOnlyList<OrderLineResult> Lines)`, `IOrderService` — all consumed by Tasks 6, 7, 8, 9.

Plain contracts, no behavior to test.

- [ ] **Step 1: MenuItem DTOs + interface**

`KayeDM.BMS/src/KayeDM.Application/Menu/MenuItemDto.cs`:

```csharp
using KayeDM.Domain.Enums;

namespace KayeDM.Application.Menu;

public record MenuItemDto(int Id, string Name, MenuCategory Category, decimal Price, bool IsActive, int SortOrder);
```

`KayeDM.BMS/src/KayeDM.Application/Menu/MenuItemUpsertDto.cs`:

```csharp
using KayeDM.Domain.Enums;

namespace KayeDM.Application.Menu;

public record MenuItemUpsertDto(string Name, MenuCategory Category, decimal Price, int SortOrder);
```

`KayeDM.BMS/src/KayeDM.Application/Menu/IMenuItemService.cs`:

```csharp
namespace KayeDM.Application.Menu;

public interface IMenuItemService
{
    Task<List<MenuItemDto>> GetAllAsync(bool includeInactive = false);
    Task<MenuItemDto?> GetByIdAsync(int id);
    Task<MenuItemDto> CreateAsync(MenuItemUpsertDto dto);
    Task<MenuItemDto> UpdateAsync(int id, MenuItemUpsertDto dto);
    Task SetActiveAsync(int id, bool isActive);
}
```

- [ ] **Step 2: Order DTOs + interface**

`KayeDM.BMS/src/KayeDM.Application/Orders/OrderModels.cs`:

```csharp
using KayeDM.Domain.Enums;

namespace KayeDM.Application.Orders;

public record OrderLineRequest(int MenuItemId, int Quantity);

public record CreateOrderRequest(
    IReadOnlyList<OrderLineRequest> Lines,
    PaymentMethod PaymentMethod,
    decimal AmountTendered,
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

`KayeDM.BMS/src/KayeDM.Application/Orders/IOrderService.cs`:

```csharp
namespace KayeDM.Application.Orders;

public interface IOrderService
{
    Task<OrderResult> CreateOrderAsync(CreateOrderRequest request);
    Task VoidOrderAsync(int orderId, string reason);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(application): add MenuItem/Order DTOs and service interfaces"
```

---

### Task 6: MenuItemService implementation + DI registration

**Files:**
- Create: `KayeDM.BMS/src/KayeDM.Infrastructure/Menu/MenuItemService.cs`
- Modify: `KayeDM.BMS/src/KayeDM.Web/Program.cs`

**Interfaces:**
- Consumes: `IMenuItemService`, `MenuItemDto`, `MenuItemUpsertDto` (Task 5); `AppDbContext`, `MenuItem` (Tasks 2–3).
- Produces: `KayeDM.Infrastructure.Menu.MenuItemService : IMenuItemService` — consumed by the `/menu` page (Task 8) via DI.

No dedicated tests for this service per the Week 1 prompt's test scope (order math/void only) — it's CRUD over one table with no business rules to verify beyond "the SQL round-trips," which the manual browser check in Task 11 covers.

- [ ] **Step 1: Implement the service**

`KayeDM.BMS/src/KayeDM.Infrastructure/Menu/MenuItemService.cs`:

```csharp
using KayeDM.Application.Menu;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Menu;

public class MenuItemService : IMenuItemService
{
    private readonly AppDbContext _db;

    public MenuItemService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<MenuItemDto>> GetAllAsync(bool includeInactive = false)
    {
        var query = _db.MenuItems.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(m => m.IsActive);
        }

        return await query
            .OrderBy(m => m.SortOrder)
            .Select(m => new MenuItemDto(m.Id, m.Name, m.Category, m.Price, m.IsActive, m.SortOrder))
            .ToListAsync();
    }

    public async Task<MenuItemDto?> GetByIdAsync(int id)
    {
        var menuItem = await _db.MenuItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        return menuItem is null
            ? null
            : new MenuItemDto(menuItem.Id, menuItem.Name, menuItem.Category, menuItem.Price, menuItem.IsActive, menuItem.SortOrder);
    }

    public async Task<MenuItemDto> CreateAsync(MenuItemUpsertDto dto)
    {
        var entity = new MenuItem
        {
            Name = dto.Name,
            Category = dto.Category,
            Price = dto.Price,
            SortOrder = dto.SortOrder,
            IsActive = true
        };

        _db.MenuItems.Add(entity);
        await _db.SaveChangesAsync();

        return new MenuItemDto(entity.Id, entity.Name, entity.Category, entity.Price, entity.IsActive, entity.SortOrder);
    }

    public async Task<MenuItemDto> UpdateAsync(int id, MenuItemUpsertDto dto)
    {
        var entity = await _db.MenuItems.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new DomainException($"Menu item {id} not found.");

        entity.Name = dto.Name;
        entity.Category = dto.Category;
        entity.Price = dto.Price;
        entity.SortOrder = dto.SortOrder;

        await _db.SaveChangesAsync();

        return new MenuItemDto(entity.Id, entity.Name, entity.Category, entity.Price, entity.IsActive, entity.SortOrder);
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        var entity = await _db.MenuItems.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new DomainException($"Menu item {id} not found.");

        entity.IsActive = isActive;
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Register it in DI**

In `KayeDM.BMS/src/KayeDM.Web/Program.cs`, add usings and a registration line:

```csharp
using KayeDM.Application.Menu;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Menu;
using KayeDM.Web.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("KayeDmBms")));

builder.Services.AddScoped<IMenuItemService, MenuItemService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
```

(Leave the rest of `Program.cs` from Task 3 unchanged.)

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): implement MenuItemService and register it in DI"
```

---

### Task 7: OrderService implementation (TDD) + DI registration

**Files:**
- Create: `KayeDM.BMS/src/KayeDM.Infrastructure/Orders/OrderService.cs`
- Create: `KayeDM.BMS/tests/KayeDM.Tests/Orders/OrderServiceTests.cs`
- Modify: `KayeDM.BMS/src/KayeDM.Web/Program.cs`

**Interfaces:**
- Consumes: `IOrderService`, `CreateOrderRequest`, `OrderLineRequest`, `OrderResult`, `OrderLineResult` (Task 5); `AppDbContext`, `Order`, `OrderLine`, `MenuItem` (Tasks 2–4); `DomainException` (Task 2).
- Produces: `KayeDM.Infrastructure.Orders.OrderService : IOrderService` — consumed by the `/pos` page (Task 9) via DI.

This is the task with real business logic (order total math, change calculation, daily sequential numbering, void rule) — build it test-first.

- [ ] **Step 1: Write a stub that compiles but isn't implemented yet**

`KayeDM.BMS/src/KayeDM.Infrastructure/Orders/OrderService.cs`:

```csharp
using KayeDM.Application.Orders;
using KayeDM.Infrastructure.Data;

namespace KayeDM.Infrastructure.Orders;

public class OrderService : IOrderService
{
    private readonly AppDbContext _db;

    public OrderService(AppDbContext db)
    {
        _db = db;
    }

    public Task<OrderResult> CreateOrderAsync(CreateOrderRequest request) => throw new NotImplementedException();

    public Task VoidOrderAsync(int orderId, string reason) => throw new NotImplementedException();
}
```

- [ ] **Step 2: Write the failing tests**

`KayeDM.BMS/tests/KayeDM.Tests/Orders/OrderServiceTests.cs`:

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

public class OrderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly OrderService _sut;

    public OrderServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new OrderService(_db);

        _db.MenuItems.AddRange(
            new MenuItem { Id = 1, Name = "Adobo", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 1 },
            new MenuItem { Id = 2, Name = "Rice", Category = MenuCategory.Rice, Price = 15m, IsActive = true, SortOrder = 2 });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreateOrderAsync_ComputesTotal_FromLines()
    {
        var request = new CreateOrderRequest(
            new[] { new OrderLineRequest(1, 2), new OrderLineRequest(2, 1) },
            PaymentMethod.Cash, 200m, null);

        var result = await _sut.CreateOrderAsync(request);

        result.Total.Should().Be(195m);
    }

    [Fact]
    public async Task CreateOrderAsync_ComputesChange_FromTendered()
    {
        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 1) }, PaymentMethod.Cash, 100m, null);

        var result = await _sut.CreateOrderAsync(request);

        result.ChangeGiven.Should().Be(10m);
    }

    [Fact]
    public async Task CreateOrderAsync_Throws_WhenTenderedLessThanTotal()
    {
        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 1) }, PaymentMethod.Cash, 50m, null);

        var act = async () => await _sut.CreateOrderAsync(request);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task CreateOrderAsync_Throws_WhenNoLines()
    {
        var request = new CreateOrderRequest(Array.Empty<OrderLineRequest>(), PaymentMethod.Cash, 0m, null);

        var act = async () => await _sut.CreateOrderAsync(request);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task CreateOrderAsync_GeneratesSequentialDailyOrderNumbers()
    {
        var request = new CreateOrderRequest(new[] { new OrderLineRequest(2, 1) }, PaymentMethod.Cash, 20m, null);

        var first = await _sut.CreateOrderAsync(request);
        var second = await _sut.CreateOrderAsync(request);
        var third = await _sut.CreateOrderAsync(request);

        var today = DateTime.Now.ToString("yyyyMMdd");
        first.OrderNumber.Should().Be($"{today}-001");
        second.OrderNumber.Should().Be($"{today}-002");
        third.OrderNumber.Should().Be($"{today}-003");
    }

    [Fact]
    public async Task VoidOrderAsync_SetsStatusVoided_AndStoresReason()
    {
        var created = await _sut.CreateOrderAsync(
            new CreateOrderRequest(new[] { new OrderLineRequest(2, 1) }, PaymentMethod.Cash, 20m, null));

        await _sut.VoidOrderAsync(created.Id, "Customer changed mind");

        var order = await _db.Orders.FindAsync(created.Id);
        order!.Status.Should().Be(OrderStatus.Voided);
        order.VoidReason.Should().Be("Customer changed mind");
    }

    [Fact]
    public async Task VoidOrderAsync_Throws_WhenReasonIsEmpty()
    {
        var created = await _sut.CreateOrderAsync(
            new CreateOrderRequest(new[] { new OrderLineRequest(2, 1) }, PaymentMethod.Cash, 20m, null));

        var act = async () => await _sut.VoidOrderAsync(created.Id, "");

        await act.Should().ThrowAsync<DomainException>();
    }
}
```

- [ ] **Step 3: Run the tests to confirm they fail**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~OrderServiceTests"`
Expected: 7 tests run, all FAIL with `System.NotImplementedException`.

- [ ] **Step 4: Implement OrderService for real**

Replace `KayeDM.BMS/src/KayeDM.Infrastructure/Orders/OrderService.cs`:

```csharp
using KayeDM.Application.Orders;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Orders;

public class OrderService : IOrderService
{
    private readonly AppDbContext _db;

    public OrderService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<OrderResult> CreateOrderAsync(CreateOrderRequest request)
    {
        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new DomainException("An order must have at least one line.");
        }

        var menuItemIds = request.Lines.Select(l => l.MenuItemId).Distinct().ToList();
        var menuItems = await _db.MenuItems
            .Where(m => menuItemIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id);

        var order = new Order
        {
            OrderNumber = await NextOrderNumberAsync(),
            CreatedAt = DateTime.Now,
            CashierId = request.CashierId,
            Status = OrderStatus.Completed,
            PaymentMethod = request.PaymentMethod
        };

        decimal total = 0m;
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

        if (request.AmountTendered < total)
        {
            throw new DomainException(
                $"Amount tendered ({request.AmountTendered:N2}) is less than the order total ({total:N2}).");
        }

        order.AmountTendered = request.AmountTendered;
        order.ChangeGiven = request.AmountTendered - total;

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        return new OrderResult(order.Id, order.OrderNumber, order.CreatedAt, total, order.AmountTendered, order.ChangeGiven, lineResults);
    }

    public async Task VoidOrderAsync(int orderId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A void reason is required.");
        }

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new DomainException($"Order {orderId} not found.");

        if (order.Status == OrderStatus.Voided)
        {
            throw new DomainException($"Order {orderId} is already voided.");
        }

        order.Status = OrderStatus.Voided;
        order.VoidReason = reason;
        await _db.SaveChangesAsync();
    }

    private async Task<string> NextOrderNumberAsync()
    {
        var today = DateTime.Now.Date;
        var countToday = await _db.Orders.CountAsync(o => o.CreatedAt >= today && o.CreatedAt < today.AddDays(1));
        return $"{today:yyyyMMdd}-{countToday + 1:D3}";
    }
}
```

- [ ] **Step 5: Run the tests again to confirm they pass**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~OrderServiceTests"`
Expected: 7 tests run, all PASS.

- [ ] **Step 6: Register OrderService in DI**

In `KayeDM.BMS/src/KayeDM.Web/Program.cs`:

```csharp
using KayeDM.Application.Menu;
using KayeDM.Application.Orders;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Menu;
using KayeDM.Infrastructure.Orders;
using KayeDM.Web.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("KayeDmBms")));

builder.Services.AddScoped<IMenuItemService, MenuItemService>();
builder.Services.AddScoped<IOrderService, OrderService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
```

(Leave the rest of `Program.cs` unchanged.)

- [ ] **Step 7: Build and run the full test suite**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"`
Expected: `Passed! - Failed: 0, Passed: 7, Skipped: 0`.

- [ ] **Step 8: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): implement OrderService with tests for total/change/numbering/void"
```

---

### Task 8: `/menu` page

**Files:**
- Create: `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Menu.razor`

**Interfaces:**
- Consumes: `IMenuItemService`, `MenuItemDto`, `MenuItemUpsertDto` (Task 5, registered in DI by Task 6); `MenuCategory` (Task 2).

- [ ] **Step 1: Write the page**

`KayeDM.BMS/src/KayeDM.Web/Components/Pages/Menu.razor`:

```razor
@page "/menu"
@using System.ComponentModel.DataAnnotations
@using KayeDM.Application.Menu
@using KayeDM.Domain.Enums
@inject IMenuItemService MenuItemService

<PageTitle>Menu</PageTitle>

<h1>Menu Items</h1>

<button class="btn btn-primary mb-3" @onclick="StartCreate">+ Add Item</button>

@if (_items is null)
{
    <p>Loading…</p>
}
else if (_items.Count == 0)
{
    <p>No menu items yet.</p>
}
else
{
    <table class="table">
        <thead>
            <tr>
                <th>Name</th>
                <th>Category</th>
                <th>Price</th>
                <th>Sort</th>
                <th>Status</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var item in _items)
            {
                <tr>
                    <td>@item.Name</td>
                    <td>@item.Category</td>
                    <td>@FormatPeso(item.Price)</td>
                    <td>@item.SortOrder</td>
                    <td>@(item.IsActive ? "Active" : "Inactive")</td>
                    <td>
                        <button class="btn btn-sm btn-secondary" @onclick="() => StartEdit(item)">Edit</button>
                        <button class="btn btn-sm btn-outline-danger" @onclick="() => ToggleActiveAsync(item)">
                            @(item.IsActive ? "Deactivate" : "Activate")
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
            <label>Category</label>
            <InputSelect class="form-control" @bind-Value="_form.Category">
                @foreach (var category in Enum.GetValues<MenuCategory>())
                {
                    <option value="@category">@category</option>
                }
            </InputSelect>
        </div>
        <div class="mb-2">
            <label>Price</label>
            <InputNumber class="form-control" @bind-Value="_form.Price" />
        </div>
        <div class="mb-2">
            <label>Sort Order</label>
            <InputNumber class="form-control" @bind-Value="_form.SortOrder" />
        </div>
        <button type="submit" class="btn btn-success">Save</button>
        <button type="button" class="btn btn-link" @onclick="CancelEdit">Cancel</button>
    </EditForm>
}

@code {
    private List<MenuItemDto>? _items;
    private bool _editing;
    private int _editingId;
    private EditModel _form = new();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync() => _items = await MenuItemService.GetAllAsync(includeInactive: true);

    private void StartCreate()
    {
        _editingId = 0;
        _form = new EditModel();
        _editing = true;
    }

    private void StartEdit(MenuItemDto item)
    {
        _editingId = item.Id;
        _form = new EditModel { Name = item.Name, Category = item.Category, Price = item.Price, SortOrder = item.SortOrder };
        _editing = true;
    }

    private void CancelEdit() => _editing = false;

    private async Task SaveAsync()
    {
        var dto = new MenuItemUpsertDto(_form.Name, _form.Category, _form.Price, _form.SortOrder);
        if (_editingId > 0)
        {
            await MenuItemService.UpdateAsync(_editingId, dto);
        }
        else
        {
            await MenuItemService.CreateAsync(dto);
        }

        _editing = false;
        await LoadAsync();
    }

    private async Task ToggleActiveAsync(MenuItemDto item)
    {
        await MenuItemService.SetActiveAsync(item.Id, !item.IsActive);
        await LoadAsync();
    }

    private static string FormatPeso(decimal amount) => string.Format("₱{0:N2}", amount);

    private class EditModel
    {
        [Required]
        public string Name { get; set; } = "";

        public MenuCategory Category { get; set; } = MenuCategory.Ulam;

        [Range(0.01, 100000)]
        public decimal Price { get; set; }

        public int SortOrder { get; set; }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add /menu CRUD page"
```

---

### Task 9: `/pos` page

**Files:**
- Create: `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Pos.razor`

**Interfaces:**
- Consumes: `IMenuItemService`, `MenuItemDto` (Task 5/6); `IOrderService`, `CreateOrderRequest`, `OrderLineRequest`, `OrderResult` (Task 5/7); `MenuCategory`, `PaymentMethod` (Task 2); `DomainException` (Task 2).

- [ ] **Step 1: Write the page**

`KayeDM.BMS/src/KayeDM.Web/Components/Pages/Pos.razor`:

```razor
@page "/pos"
@using KayeDM.Application.Menu
@using KayeDM.Application.Orders
@using KayeDM.Domain.Enums
@using KayeDM.Domain.Exceptions
@inject IMenuItemService MenuItemService
@inject IOrderService OrderService

<PageTitle>POS</PageTitle>

<div class="pos-layout">
    <div class="pos-menu">
        <div class="category-tabs">
            @foreach (var category in Enum.GetValues<MenuCategory>())
            {
                <button class="btn @(category == _activeCategory ? "btn-primary" : "btn-outline-primary")"
                        @onclick="() => _activeCategory = category">
                    @category
                </button>
            }
        </div>
        <div class="menu-grid">
            @foreach (var item in _menuItems.Where(m => m.Category == _activeCategory))
            {
                <button class="menu-button" @onclick="() => AddLine(item)">
                    <div>@item.Name</div>
                    <div>@FormatPeso(item.Price)</div>
                </button>
            }
        </div>
    </div>

    <div class="pos-ticket">
        <h3>Ticket</h3>
        @if (_ticket.Count == 0)
        {
            <p>No items yet.</p>
        }
        else
        {
            @foreach (var line in _ticket)
            {
                <div class="ticket-line">
                    <span>@line.MenuItem.Name</span>
                    <button @onclick="() => ChangeQty(line, -1)">-</button>
                    <span>@line.Quantity</span>
                    <button @onclick="() => ChangeQty(line, 1)">+</button>
                    <span>@FormatPeso(line.MenuItem.Price * line.Quantity)</span>
                    <button @onclick="() => RemoveLine(line)">Remove</button>
                </div>
            }
        }
        <div class="ticket-total">Total: @FormatPeso(Total)</div>
    </div>

    <div class="pos-payment">
        <div>
            <button class="btn @(_paymentMethod == PaymentMethod.Cash ? "btn-primary" : "btn-outline-primary")"
                    @onclick="() => _paymentMethod = PaymentMethod.Cash">Cash</button>
            <button class="btn @(_paymentMethod == PaymentMethod.GCash ? "btn-primary" : "btn-outline-primary")"
                    @onclick="() => _paymentMethod = PaymentMethod.GCash">GCash</button>
        </div>
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
        <button class="btn btn-success btn-lg" disabled="@(!CanComplete)" @onclick="CompleteAsync">Complete</button>
        <button class="btn btn-link" @onclick="ClearTicket">Clear</button>

        @if (_lastOrderNumber is not null)
        {
            <div class="alert alert-success">Order @_lastOrderNumber saved. Change: @FormatPeso(_lastChange)</div>
        }
        @if (_error is not null)
        {
            <div class="alert alert-danger">@_error</div>
        }
    </div>
</div>

@code {
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
    private bool CanComplete => _ticket.Count > 0 && Tendered >= Total;

    protected override async Task OnInitializedAsync()
    {
        _menuItems = await MenuItemService.GetAllAsync();
    }

    private void AddLine(MenuItemDto item)
    {
        var existing = _ticket.FirstOrDefault(l => l.MenuItem.Id == item.Id);
        if (existing is not null)
        {
            existing.Quantity++;
        }
        else
        {
            _ticket.Add(new TicketLine { MenuItem = item, Quantity = 1 });
        }
    }

    private void ChangeQty(TicketLine line, int delta)
    {
        line.Quantity += delta;
        if (line.Quantity <= 0)
        {
            _ticket.Remove(line);
        }
    }

    private void RemoveLine(TicketLine line) => _ticket.Remove(line);

    private void ClearTicket()
    {
        _ticket.Clear();
        Tendered = 0;
        _lastOrderNumber = null;
        _error = null;
    }

    private static string FormatPeso(decimal amount) => string.Format("₱{0:N2}", amount);

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
    }

    private class TicketLine
    {
        public MenuItemDto MenuItem { get; set; } = null!;
        public int Quantity { get; set; }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add /pos order-entry and payment page"
```

---

### Task 10: Cleanup template pages + nav

**Files:**
- Delete: `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Counter.razor`
- Delete: `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Weather.razor`
- Modify: `KayeDM.BMS/src/KayeDM.Web/Components/Layout/NavMenu.razor`

**Interfaces:** None — navigation-only change.

- [ ] **Step 1: Delete template pages**

```bash
rm "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\src\KayeDM.Web\Components\Pages\Counter.razor"
rm "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\src\KayeDM.Web\Components\Pages\Weather.razor"
```

- [ ] **Step 2: Replace their nav links with Menu/POS**

Replace the contents of `KayeDM.BMS/src/KayeDM.Web/Components/Layout/NavMenu.razor`:

```razor
<div class="top-row ps-3 navbar navbar-dark">
    <div class="container-fluid">
        <a class="navbar-brand" href="">KayeDM.Web</a>
    </div>
</div>

<input type="checkbox" title="Navigation menu" class="navbar-toggler" />

<div class="nav-scrollable" onclick="document.querySelector('.navbar-toggler').click()">
    <nav class="flex-column">
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="" Match="NavLinkMatch.All">
                <span class="bi bi-house-door-fill-nav-menu" aria-hidden="true"></span> Home
            </NavLink>
        </div>

        <div class="nav-item px-3">
            <NavLink class="nav-link" href="pos">
                <span class="bi bi-plus-square-fill-nav-menu" aria-hidden="true"></span> POS
            </NavLink>
        </div>

        <div class="nav-item px-3">
            <NavLink class="nav-link" href="menu">
                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Menu
            </NavLink>
        </div>
    </nav>
</div>
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "chore(web): remove template pages, point nav at /pos and /menu"
```

---

### Task 11: Final verification + Week 1 deliverable summary

**Files:** None created — verification only.

**Interfaces:** None.

- [ ] **Step 1: Full build and full test run**

```bash
dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"
dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"
```

Expected: `Build succeeded.` and `Passed! - Failed: 0, Passed: 7, Skipped: 0`.

- [ ] **Step 2: Run the app and manually exercise both pages**

```bash
dotnet run --project "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\src\KayeDM.Web\KayeDM.Web.csproj"
```

With the app running (default `https://localhost:<port>` from `launchSettings.json`), use a Playwright browser session to:
1. Navigate to `/menu`, add 3–4 menu items across different categories (at least one `Ulam` and one `Rice`), confirm they appear in the table with correct peso formatting, toggle one inactive and confirm the status flips.
2. Navigate to `/pos`, confirm the category tabs filter the grid, click menu buttons to build a ticket, verify the running total, use a quick-amount button to set tendered, confirm change computes correctly, click Complete, and confirm the order number banner appears and the ticket clears.
3. Try to Complete with tendered less than the total (button should stay disabled per `CanComplete`), then verify enabling it by raising tendered.

Stop the server (`Ctrl+C` or kill the background process) once verified.

- [ ] **Step 3: Write the Week 1 deliverable summary**

Post the summary in the required 5-point format (files touched, exact `dotnet ef` commands run, deviations from blueprint, how to run/test, uncertainties) as a chat message — do not create a new doc file for it unless asked.

Known deviations/uncertainties to include:
- `Order.VoidReason` was added — blueprint §4 doesn't list a storage column for void reasons, but domain rule 4 needs one.
- `Order.CashierId` is nullable this week since there's no login UI yet (Week 4 scope); the POS page currently always passes `CashierId: null`.
- New files use file-scoped `namespace X;` while the original template stubs used block-style namespaces — flagged per Global Constraints.
- Solution was restructured from a flat `KayeDM.BMS/<Project>/` layout into `KayeDM.BMS/src/` + `KayeDM.BMS/tests/` to match blueprint §3, since the prompt's "current state" (assuming this layout and an existing `InitialCreate` migration/`MenuItem` entity) didn't match what was actually on disk — the repo was pre-Week-1, not mid-Week-1.

- [ ] **Step 4: Final commit if anything changed during manual verification**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git status
```

If clean, no commit needed. If manual testing left stray seeded data you don't want in LocalDB, that's fine — it's a dev database, not source-controlled.
