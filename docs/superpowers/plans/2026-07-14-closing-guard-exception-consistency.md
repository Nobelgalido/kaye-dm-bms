# Closing-Guard & Exception-Handling Consistency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close three gaps where a closed-day guard is applied inconsistently across mutating services, fix a TOCTOU race in closing creation, and make Razor admin pages consistently surface `DomainException` as an inline error instead of crashing the Blazor circuit.

**Architecture:** No new abstractions. Each backend fix calls the existing `ClosingGuard.EnsureDateNotClosedAsync` static helper at the point a mutation would otherwise silently bypass it, following the exact call pattern already used in `ExpenseService.CreateExpenseAsync`/`UpdateExpenseAsync`. The `ClosingService` race fix removes a separate read-then-write check and lets the existing unique index on `DailyClosing.Date` be the single source of truth, caught and translated to a friendly `DomainException`. Each frontend fix follows the exact `try/catch (DomainException) { _error = ex.Message; }` pattern already used in `Expenses.razor`'s `SubmitAsync`.

**Tech Stack:** .NET 8, Blazor Server, EF Core 8 (Sqlite in tests), xUnit + FluentAssertions.

## Global Constraints

- Follow existing code style exactly: file-scoped namespaces, `IDbContextFactory<AppDbContext>` per-call context creation, `DomainException`/`DateClosedException` for business-rule violations.
- No new NuGet packages. There is no bUnit or other Blazor component-test harness in `KayeDM.Tests.csproj` — do not add one for this plan. Frontend tasks are verified by `dotnet build` + a manual run-through, not automated tests.
- Every backend task must have a failing-then-passing xUnit test before being considered done.
- Do not touch the concurrency/transaction issues in `OrderService` (oversell race, order-number race, crew-meal allowance race) — those are a separate, larger piece of work, out of scope here.

---

## File Structure

- Modify `KayeDM.BMS/src/KayeDM.Infrastructure/Expenses/ExpenseService.cs` — add closing guard to `DeleteExpenseAsync`.
- Modify `KayeDM.BMS/src/KayeDM.Infrastructure/Inventory/InventoryService.cs` — add closing guard to `CreateBatchAsync` and `LogWasteAsync`.
- Modify `KayeDM.BMS/src/KayeDM.Infrastructure/Closing/ClosingService.cs` — replace racy pre-check in `CreateClosingAsync` with a catch on the unique-index violation.
- Modify `KayeDM.BMS/tests/KayeDM.Tests/Closing/ClosingLockTests.cs` — add an `InventoryService` to the shared fixture and add three new closed-date tests.
- Modify `KayeDM.BMS/tests/KayeDM.Tests/Closing/ClosingServiceTests.cs` — add one new race-simulation test.
- Modify `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Expenses.razor` — wrap `DeleteAsync` in try/catch, reuse existing `_error` field.
- Modify `KayeDM.BMS/src/KayeDM.Web/Components/Pages/ExpenseCategories.razor` — add `_error` field, wrap `SaveAsync`/`ToggleActiveAsync`.
- Modify `KayeDM.BMS/src/KayeDM.Web/Components/Pages/BusCompanies.razor` — add `_error` field, wrap `SaveAsync`/`ToggleActiveAsync`.
- Modify `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Menu.razor` — add `_error` field, wrap `SaveAsync`/`ToggleActiveAsync`.

---

### Task 1: `ExpenseService.DeleteExpenseAsync` — enforce closing guard

**Files:**
- Modify: `KayeDM.BMS/src/KayeDM.Infrastructure/Expenses/ExpenseService.cs:153-162`
- Test: `KayeDM.BMS/tests/KayeDM.Tests/Closing/ClosingLockTests.cs`

**Interfaces:**
- Consumes: `ClosingGuard.EnsureDateNotClosedAsync(AppDbContext db, DateTime date, string action)` (existing, `KayeDM.Infrastructure.Closing`), throws `DateClosedException` (existing, `KayeDM.Domain.Exceptions`).
- Produces: `DeleteExpenseAsync` now throws `DateClosedException` when `entity.Date` is on/before a closed date — no signature change.

- [ ] **Step 1: Write the failing test**

Add to `KayeDM.BMS/tests/KayeDM.Tests/Closing/ClosingLockTests.cs`, after `UpdateExpenseAsync_Throws_WhenExpenseDateIsClosed`:

```csharp
    [Fact]
    public async Task DeleteExpenseAsync_Throws_WhenExpenseDateIsClosed()
    {
        var expense = new Expense
        {
            Date = _today,
            ExpenseCategoryId = 1,
            Description = "Rice",
            Amount = 500m,
            PaymentMethod = ExpensePaymentMethod.Cash,
            LoggedById = "u1",
            LoggedAt = DateTime.Now
        };
        _db.Expenses.Add(expense);
        _db.SaveChanges();

        var act = async () => await _expenseService.DeleteExpenseAsync(expense.Id);

        await act.Should().ThrowAsync<DateClosedException>();
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test KayeDM.BMS/tests/KayeDM.Tests --filter DeleteExpenseAsync_Throws_WhenExpenseDateIsClosed`
Expected: FAIL — no exception thrown (the delete currently succeeds even though today is closed).

- [ ] **Step 3: Write minimal implementation**

In `KayeDM.BMS/src/KayeDM.Infrastructure/Expenses/ExpenseService.cs`, replace:

```csharp
    public async Task DeleteExpenseAsync(int id)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id)
            ?? throw new DomainException($"Expense {id} not found.");

        db.Expenses.Remove(entity);
        await db.SaveChangesAsync();
    }
```

with:

```csharp
    public async Task DeleteExpenseAsync(int id)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id)
            ?? throw new DomainException($"Expense {id} not found.");

        await ClosingGuard.EnsureDateNotClosedAsync(db, entity.Date, "delete this expense");

        db.Expenses.Remove(entity);
        await db.SaveChangesAsync();
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test KayeDM.BMS/tests/KayeDM.Tests --filter ClosingLockTests`
Expected: PASS (all `ClosingLockTests`, including the new one).

- [ ] **Step 5: Commit**

```bash
git add KayeDM.BMS/src/KayeDM.Infrastructure/Expenses/ExpenseService.cs KayeDM.BMS/tests/KayeDM.Tests/Closing/ClosingLockTests.cs
git commit -m "fix(expenses): enforce closing guard on expense deletion"
```

---

### Task 2: `InventoryService` — enforce closing guard on batch creation and waste logging

**Files:**
- Modify: `KayeDM.BMS/src/KayeDM.Infrastructure/Inventory/InventoryService.cs:18-48,64-89`
- Test: `KayeDM.BMS/tests/KayeDM.Tests/Closing/ClosingLockTests.cs`

**Interfaces:**
- Consumes: `ClosingGuard.EnsureDateNotClosedAsync` (existing), `DishBatch.Date` (existing entity property).
- Produces: `CreateBatchAsync` throws `DateClosedException` when today is closed; `LogWasteAsync` throws `DateClosedException` when the batch's `Date` is on/before a closed date — no signature change.

- [ ] **Step 1: Write the failing tests**

In `KayeDM.BMS/tests/KayeDM.Tests/Closing/ClosingLockTests.cs`, add these usings at the top (alongside the existing ones):

```csharp
using KayeDM.Application.Inventory;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Inventory;
```

(`KayeDM.Domain.Enums` may already be present via `KayeDM.Domain.Enums` import for `PaymentMethod`/`OrderStatus` — check before duplicating; `WasteReason` lives in the same namespace so no separate using is needed if `KayeDM.Domain.Enums` is already imported.)

Add a field and constructor wiring for `InventoryService`, alongside the existing `_orderService`/`_expenseService`:

```csharp
    private readonly InventoryService _inventoryService;
```

In the constructor, after `_expenseService = new ExpenseService(new TestDbContextFactory(options));`:

```csharp
        _inventoryService = new InventoryService(new TestDbContextFactory(options));
```

Add these two tests after `DeleteExpenseAsync_Throws_WhenExpenseDateIsClosed` (from Task 1):

```csharp
    [Fact]
    public async Task CreateBatchAsync_Throws_WhenTodayIsClosed()
    {
        var request = new CreateDishBatchRequest(1, 2m, 10);

        var act = async () => await _inventoryService.CreateBatchAsync(request);

        await act.Should().ThrowAsync<DateClosedException>();
    }

    [Fact]
    public async Task LogWasteAsync_Throws_WhenBatchDateIsClosed()
    {
        var batch = new DishBatch { MenuItemId = 1, Date = _today, TraysProduced = 2m, ServingsPerTray = 10, ProducedAt = DateTime.Now };
        _db.DishBatches.Add(batch);
        _db.SaveChanges();

        var request = new LogWasteRequest(batch.Id, 1m, WasteReason.Spoiled, "u1");

        var act = async () => await _inventoryService.LogWasteAsync(request);

        await act.Should().ThrowAsync<DateClosedException>();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test KayeDM.BMS/tests/KayeDM.Tests --filter "CreateBatchAsync_Throws_WhenTodayIsClosed|LogWasteAsync_Throws_WhenBatchDateIsClosed"`
Expected: FAIL — both batch creation and waste logging currently succeed on a closed day.

- [ ] **Step 3: Write minimal implementation**

In `KayeDM.BMS/src/KayeDM.Infrastructure/Inventory/InventoryService.cs`, add the using:

```csharp
using KayeDM.Infrastructure.Closing;
```

Replace the start of `CreateBatchAsync`:

```csharp
    public async Task<DishBatchDto> CreateBatchAsync(CreateDishBatchRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var menuItem = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == request.MenuItemId)
            ?? throw new DomainException($"Menu item {request.MenuItemId} not found.");
```

with:

```csharp
    public async Task<DishBatchDto> CreateBatchAsync(CreateDishBatchRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        await ClosingGuard.EnsureDateNotClosedAsync(db, DateTime.Now.Date, "log a dish batch");

        var menuItem = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == request.MenuItemId)
            ?? throw new DomainException($"Menu item {request.MenuItemId} not found.");
```

Replace the start of `LogWasteAsync`:

```csharp
    public async Task<WasteLogDto> LogWasteAsync(LogWasteRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var batch = await db.DishBatches.Include(b => b.MenuItem).FirstOrDefaultAsync(b => b.Id == request.DishBatchId)
            ?? throw new DomainException($"Dish batch {request.DishBatchId} not found.");
```

with:

```csharp
    public async Task<WasteLogDto> LogWasteAsync(LogWasteRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var batch = await db.DishBatches.Include(b => b.MenuItem).FirstOrDefaultAsync(b => b.Id == request.DishBatchId)
            ?? throw new DomainException($"Dish batch {request.DishBatchId} not found.");

        await ClosingGuard.EnsureDateNotClosedAsync(db, batch.Date, "log waste");
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test KayeDM.BMS/tests/KayeDM.Tests --filter ClosingLockTests`
Expected: PASS (all `ClosingLockTests`, including the two new ones). Also run `dotnet test KayeDM.BMS/tests/KayeDM.Tests --filter InventoryServiceTests` to confirm the existing inventory tests still pass (they call `CreateBatchAsync` on an unclosed day, so the new guard should not affect them).

- [ ] **Step 5: Commit**

```bash
git add KayeDM.BMS/src/KayeDM.Infrastructure/Inventory/InventoryService.cs KayeDM.BMS/tests/KayeDM.Tests/Closing/ClosingLockTests.cs
git commit -m "fix(inventory): enforce closing guard on batch creation and waste logging"
```

---

### Task 3: `ClosingService.CreateClosingAsync` — remove TOCTOU race, rely on unique index

**Files:**
- Modify: `KayeDM.BMS/src/KayeDM.Infrastructure/Closing/ClosingService.cs:65-101`
- Test: `KayeDM.BMS/tests/KayeDM.Tests/Closing/ClosingServiceTests.cs`

**Interfaces:**
- Consumes: existing unique index `entity.HasIndex(c => c.Date).IsUnique()` on `DailyClosing` (`AppDbContext.cs:149`), `Microsoft.EntityFrameworkCore.DbUpdateException` (already in scope via existing `using Microsoft.EntityFrameworkCore;`).
- Produces: `CreateClosingAsync` still throws `DomainException` (not `DateClosedException` — matches existing behavior) with message `"{today:MMM d, yyyy} has already been closed."` whether the duplicate existed before the call or was written concurrently during it. No signature change.

- [ ] **Step 1: Write the failing test**

Add to `KayeDM.BMS/tests/KayeDM.Tests/Closing/ClosingServiceTests.cs`, after `CreateClosingAsync_Throws_WhenTodayAlreadyClosed`:

```csharp
    [Fact]
    public async Task CreateClosingAsync_ThrowsDomainException_NotDbUpdateException_WhenClosingWasCreatedConcurrently()
    {
        // Simulates a second request that committed a closing for today
        // strictly between this caller's read and its own save — the unique
        // index on DailyClosing.Date is the sole guard, so this must surface
        // as the same friendly DomainException as the sequential case, not
        // a raw DbUpdateException bubbling out of SaveChangesAsync.
        _db.DailyClosings.Add(new DailyClosing { Date = _today, TotalSales = 50m, ClosedById = "owner-2", ClosedAt = DateTime.Now });
        _db.SaveChanges();

        var act = async () => await _sut.CreateClosingAsync("owner-1");

        var exception = await act.Should().ThrowAsync<KayeDM.Domain.Exceptions.DomainException>();
        exception.Which.Should().NotBeOfType<Microsoft.EntityFrameworkCore.DbUpdateException>();
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test KayeDM.BMS/tests/KayeDM.Tests --filter CreateClosingAsync_ThrowsDomainException_NotDbUpdateException_WhenClosingWasCreatedConcurrently`
Expected: PASS already, actually — the current pre-check (`alreadyClosed`) already catches this specific sequential case and throws `DomainException` before any race is possible. This test alone won't prove the fix; it's a regression guard. The real bug (raw `DbUpdateException` on a genuine concurrent write) can't be deterministically reproduced without an artificial delay hook, so Step 3 removes the race by construction (single source of truth = the DB constraint) rather than patching the symptom. Run the test anyway to confirm it's green both before and after Step 3 — it must not regress.

- [ ] **Step 3: Rewrite `CreateClosingAsync` to use the unique index as the single source of truth**

In `KayeDM.BMS/src/KayeDM.Infrastructure/Closing/ClosingService.cs`, replace:

```csharp
    public async Task<DailyClosingDto> CreateClosingAsync(string closedById)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var today = DateTime.Now.Date;

        var alreadyClosed = await db.DailyClosings.AnyAsync(c => c.Date == today);
        if (alreadyClosed)
        {
            throw new DomainException($"{today:MMM d, yyyy} has already been closed.");
        }

        var figures = await GetTodaysFiguresAsync();

        var entity = new DailyClosing
        {
            Date = today,
            TotalSales = figures.TotalSales,
            CashSales = figures.CashSales,
            GCashSales = figures.GCashSales,
            OrderCount = figures.OrderCount,
            VoidedCount = figures.VoidedCount,
            CrewMealsGiven = figures.CrewMealsGiven,
            TotalExpenses = figures.TotalExpenses,
            NetForDay = figures.NetForDay,
            ClosedById = closedById,
            ClosedAt = DateTime.Now
        };

        db.DailyClosings.Add(entity);
        await db.SaveChangesAsync();

        return new DailyClosingDto(
            entity.Id, DateOnly.FromDateTime(entity.Date), entity.TotalSales, entity.CashSales, entity.GCashSales,
            entity.OrderCount, entity.VoidedCount, entity.CrewMealsGiven, entity.TotalExpenses, entity.NetForDay,
            entity.ClosedById, entity.ClosedAt);
    }
```

with:

```csharp
    public async Task<DailyClosingDto> CreateClosingAsync(string closedById)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var today = DateTime.Now.Date;
        var figures = await GetTodaysFiguresAsync();

        var entity = new DailyClosing
        {
            Date = today,
            TotalSales = figures.TotalSales,
            CashSales = figures.CashSales,
            GCashSales = figures.GCashSales,
            OrderCount = figures.OrderCount,
            VoidedCount = figures.VoidedCount,
            CrewMealsGiven = figures.CrewMealsGiven,
            TotalExpenses = figures.TotalExpenses,
            NetForDay = figures.NetForDay,
            ClosedById = closedById,
            ClosedAt = DateTime.Now
        };

        db.DailyClosings.Add(entity);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new DomainException($"{today:MMM d, yyyy} has already been closed.");
        }

        return new DailyClosingDto(
            entity.Id, DateOnly.FromDateTime(entity.Date), entity.TotalSales, entity.CashSales, entity.GCashSales,
            entity.OrderCount, entity.VoidedCount, entity.CrewMealsGiven, entity.TotalExpenses, entity.NetForDay,
            entity.ClosedById, entity.ClosedAt);
    }
```

This removes the racy `AnyAsync` pre-check entirely. The unique index on `DailyClosing.Date` (`AppDbContext.cs:149`) is now the only guard, and it is atomic at the database level — no interleaving of two concurrent calls can result in two rows for the same date. Whether the duplicate was written a day ago or a microsecond ago, `SaveChangesAsync` throws `DbUpdateException`, which is caught and translated to the same `DomainException` message as before.

- [ ] **Step 4: Run tests to verify everything passes**

Run: `dotnet test KayeDM.BMS/tests/KayeDM.Tests --filter ClosingServiceTests`
Expected: PASS — including the pre-existing `CreateClosingAsync_Throws_WhenTodayAlreadyClosed` (now exercises the catch block instead of the old pre-check) and the new concurrent-write test.

Run the full suite once to confirm no regressions: `dotnet test KayeDM.BMS/tests/KayeDM.Tests`
Expected: PASS, all tests green.

- [ ] **Step 5: Commit**

```bash
git add KayeDM.BMS/src/KayeDM.Infrastructure/Closing/ClosingService.cs KayeDM.BMS/tests/KayeDM.Tests/Closing/ClosingServiceTests.cs
git commit -m "fix(closing): remove TOCTOU race in CreateClosingAsync, rely on unique index"
```

---

### Task 4: `Expenses.razor` — catch `DomainException` on delete

**Files:**
- Modify: `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Expenses.razor:234-238`

**Interfaces:**
- Consumes: existing `_error` field (already declared at line 148, already rendered at lines 69-72), `ExpenseService.DeleteExpenseAsync` (now throws `DateClosedException` after Task 1).
- Produces: none consumed by later tasks — this page is self-contained.

- [ ] **Step 1: Make the change**

In `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Expenses.razor`, replace:

```csharp
    private async Task DeleteAsync(int id)
    {
        await ExpenseService.DeleteExpenseAsync(id);
        await LoadExpensesAsync();
    }
```

with:

```csharp
    private async Task DeleteAsync(int id)
    {
        _error = null;
        try
        {
            await ExpenseService.DeleteExpenseAsync(id);
            await LoadExpensesAsync();
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build KayeDM.BMS/KayeDM.BMS.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual verification**

Run the app (`dotnet run --project KayeDM.BMS/src/KayeDM.Web`), log in as Owner, close today's books from `/closing`, then go to `/expenses` and try to delete a today-dated expense. Expected: an inline "Cannot delete this expense — ... is on or before a closed date." message appears in the existing `.alert.alert-danger` block instead of the Blazor "An unhandled error has occurred" reconnect banner.

- [ ] **Step 4: Commit**

```bash
git add KayeDM.BMS/src/KayeDM.Web/Components/Pages/Expenses.razor
git commit -m "fix(web): show inline error instead of crashing circuit on blocked expense delete"
```

---

### Task 5: `ExpenseCategories.razor` — add error handling to mutations

**Files:**
- Modify: `KayeDM.BMS/src/KayeDM.Web/Components/Pages/ExpenseCategories.razor`

**Interfaces:**
- Consumes: `KayeDM.Domain.Exceptions.DomainException` (needs a new `@using`).
- Produces: none.

- [ ] **Step 1: Add the using and `_error` field, render the error, wrap the two mutation handlers**

In `KayeDM.BMS/src/KayeDM.Web/Components/Pages/ExpenseCategories.razor`, add to the top `@using` block (after `@using KayeDM.Domain.Enums`):

```razor
@using KayeDM.Domain.Exceptions
```

After the `<PageHeader ...>` block (before the `@if (_categories is null)` block), add:

```razor
@if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}
```

Replace:

```csharp
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
```

with:

```csharp
    private async Task SaveAsync()
    {
        _error = null;
        try
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
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }

    private async Task ToggleActiveAsync(ExpenseCategoryDto category)
    {
        _error = null;
        try
        {
            await ExpenseService.SetCategoryActiveAsync(category.Id, !category.IsActive);
            await LoadAsync();
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }
```

Add the field declaration next to the existing `@code` fields (after `private CategoryForm _form = new();`):

```csharp
    private string? _error;
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build KayeDM.BMS/KayeDM.BMS.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual verification**

Run the app, log in as Owner, go to `/expenses/categories`, create and edit a category to confirm normal flow still works and no error banner appears on success.

- [ ] **Step 4: Commit**

```bash
git add KayeDM.BMS/src/KayeDM.Web/Components/Pages/ExpenseCategories.razor
git commit -m "fix(web): surface DomainException as inline error on expense category mutations"
```

---

### Task 6: `BusCompanies.razor` — add error handling to mutations

**Files:**
- Modify: `KayeDM.BMS/src/KayeDM.Web/Components/Pages/BusCompanies.razor`

**Interfaces:**
- Consumes: `KayeDM.Domain.Exceptions.DomainException` (needs a new `@using`).
- Produces: none.

- [ ] **Step 1: Add the using and `_error` field, render the error, wrap the two mutation handlers**

In `KayeDM.BMS/src/KayeDM.Web/Components/Pages/BusCompanies.razor`, add to the top `@using` block (after `@using KayeDM.Application.Buses`):

```razor
@using KayeDM.Domain.Exceptions
```

After the `<PageHeader ...>` block (before the `@if (_companies is null)` block), add:

```razor
@if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}
```

Replace:

```csharp
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
```

with:

```csharp
    private async Task SaveAsync()
    {
        _error = null;
        try
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
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }

    private async Task ToggleActiveAsync(BusCompanyDto company)
    {
        _error = null;
        try
        {
            await BusService.SetCompanyActiveAsync(company.Id, !company.IsActive);
            await LoadAsync();
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }
```

Add the field declaration next to the existing `@code` fields (after `private EditModel _form = new();`):

```csharp
    private string? _error;
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build KayeDM.BMS/KayeDM.BMS.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual verification**

Run the app, log in as Owner, go to `/buses/companies`, create and edit a company to confirm normal flow still works.

- [ ] **Step 4: Commit**

```bash
git add KayeDM.BMS/src/KayeDM.Web/Components/Pages/BusCompanies.razor
git commit -m "fix(web): surface DomainException as inline error on bus company mutations"
```

---

### Task 7: `Menu.razor` — add error handling to mutations

**Files:**
- Modify: `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Menu.razor`

**Interfaces:**
- Consumes: `KayeDM.Domain.Exceptions.DomainException` (needs a new `@using`).
- Produces: none.

- [ ] **Step 1: Add the using and `_error` field, render the error, wrap the two mutation handlers**

In `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Menu.razor`, add to the top `@using` block (after `@using KayeDM.Domain.Enums`):

```razor
@using KayeDM.Domain.Exceptions
```

After the `<PageHeader ...>` block (before the `@if (_items is null)` block), add:

```razor
@if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}
```

Replace:

```csharp
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
```

with:

```csharp
    private async Task SaveAsync()
    {
        _error = null;
        try
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
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }

    private async Task ToggleActiveAsync(MenuItemDto item)
    {
        _error = null;
        try
        {
            await MenuItemService.SetActiveAsync(item.Id, !item.IsActive);
            await LoadAsync();
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }
```

Add the field declaration next to the existing `@code` fields (after `private EditModel _form = new();`):

```csharp
    private string? _error;
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build KayeDM.BMS/KayeDM.BMS.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual verification**

Run the app, log in as Owner, go to `/menu`, create and edit a menu item to confirm normal flow still works.

- [ ] **Step 4: Commit**

```bash
git add KayeDM.BMS/src/KayeDM.Web/Components/Pages/Menu.razor
git commit -m "fix(web): surface DomainException as inline error on menu item mutations"
```

---

## Final Verification

After all 7 tasks:

- [ ] Run the full test suite: `dotnet test KayeDM.BMS/tests/KayeDM.Tests` — expect all tests green (66 pre-existing + 5 new = 71).
- [ ] Run a full build: `dotnet build KayeDM.BMS/KayeDM.BMS.slnx` — expect 0 errors, 0 new warnings.
- [ ] Manual smoke test per Tasks 4-7's Step 3.
