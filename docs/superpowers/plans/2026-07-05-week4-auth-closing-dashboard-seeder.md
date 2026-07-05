# Week 4 — Auth & Roles, Daily Closing, Analytics Dashboard, Seed Data Generator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **This plan has three mandatory user checkpoints — do not proceed past them without explicit user sign-off.**

**Goal:** Implement Week 4 scope from `docs/kaye-dm-agent-prompts-weeks-1-5.md` (WEEK 4 PROMPT): wire ASP.NET Core Identity with `Owner`/`Cashier` roles and route gating, daily closing with an immutable lock rule, an analytics dashboard (KPIs, 7 chart types, rule-based insights), and a fixed-seed demo data generator.

**Architecture:** Same 5-project layering as Weeks 1–3. `ClosingService` and `DashboardService` follow the established `IDbContextFactory<AppDbContext>` pattern and query entities directly across module boundaries (the same style `AvailabilityCalculator` already uses to read `DishBatch`/`WasteLog`/`Order`/`OrderLine` together) — no changes to `IOrderService`/`IBusService`/`IExpenseService`/`IInventoryService` signatures are needed for Closing or Dashboard. Auth is wired with a deliberately minimal, non-scaffolded approach: two plain minimal-API endpoints (`/account/login`, `/account/logout`) handle the actual sign-in/sign-out (`SignInManager` calls must run in a genuine HTTP request, never inside an already-interactive Blazor Server circuit, per ASP.NET Core's "don't set headers after the response starts" rule), with a simple Razor page rendering the login form. `AuthorizeRouteView` + `AddCascadingAuthenticationState()` gate every route; `[Authorize(Roles = "...")]` attributes on individual pages match blueprint §6's page map exactly.

**Tech Stack:** Same as Weeks 1–3, plus `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (already installed), ASP.NET Core Identity (`AddIdentityCore`/`AddSignInManager`/`AddIdentityCookies`), and `Blazor-ApexCharts` (MIT-licensed, actively maintained .NET wrapper for ApexCharts.js) for the dashboard charts — the only new package this week, version pinned to whatever `dotnet add package Blazor-ApexCharts` resolves at Task 12.

## Global Constraints

- Packages pinned to **8.0.11** for every `Microsoft.EntityFrameworkCore.*` / `Microsoft.AspNetCore.Identity.EntityFrameworkCore` package. Never upgrade to EF 9/10. `TargetFramework` stays `net8.0`. `Blazor-ApexCharts` is the only new dependency allowed this week.
- Migrations are sacred: one per schema change, descriptive names, never delete/regenerate/squash/edit an existing migration. This week's expected migrations, in order: `AddDailyClosing` only — confirmed at Task 0 that Identity tables already exist from `InitialCreate` (this `AppDbContext` has extended `IdentityDbContext<IdentityUser>` since Week 1, which already includes full role support via the single-generic-parameter overload), so **no `AddIdentitySchema` migration is needed**; verify this doesn't change before skipping it. No `WireLoggedByToIdentity` migration is needed either — `LoggedById` is already a plain string column, we're just populating it with a real value instead of `"system"`.
- `dotnet ef` CLI calls always use `--project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj`.
- Layering: no EF Core types in `KayeDM.Domain`; `KayeDM.Web` talks to `KayeDM.Application` interfaces only, never to `AppDbContext` directly. The one exception this week: `KayeDM.Web` resolves the current user's id via `AuthenticationStateProvider` in Razor components (standard Blazor pattern, not a layering violation) and passes it into request DTOs — services never reach into `HttpContext`/claims themselves.
- No MediatR, AutoMapper, or repository wrappers. Plain services + constructor injection.
- Services take `IDbContextFactory<AppDbContext>` and call `await using var db = await _dbContextFactory.CreateDbContextAsync();` per call.
- Prefer pure Blazor over JS interop. Currency format is always `"₱{0:N2}"`. File-scoped namespaces. Nullable reference types enabled everywhere.
- Date binding rule (corrected after a Week 3 mistake): `<input type="date">`/`"time"`/`"datetime-local"` bind cleanly via plain `@bind` to `DateOnly`/`TimeOnly`/`DateTime` (or `<InputDate>` inside an `EditForm`) — never fall back to a `string` workaround for these. Only `type="month"` genuinely lacks Blazor binding support (see `/buses/report`).
- All commands below assume the working directory is `C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS` unless a step explicitly `cd`s elsewhere.
- Out of scope this week (do not build): Docker, deployment, README/GIF, exports/printing, keyboard shortcuts.

---

## PART A — Auth & Roles

### Task 0: Confirm Identity schema, register Identity services in `Program.cs`

**Files:**
- Modify: `src/KayeDM.Web/Program.cs`

**Interfaces:**
- Consumes: `AppDbContext` (already `IdentityDbContext<IdentityUser>`, existing).
- Produces: `SignInManager<IdentityUser>`, `UserManager<IdentityUser>`, `RoleManager<IdentityRole>` all registered in DI — consumed by Tasks 1, 2, 4.

- [ ] **Step 1: Confirm no migration is needed**

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT name FROM sys.tables WHERE name LIKE 'AspNet%' ORDER BY name;"
```

Expected: `AspNetRoleClaims`, `AspNetRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserRoles`, `AspNetUserTokens`, `AspNetUsers` — all 7 already exist (created by `InitialCreate` since `AppDbContext : IdentityDbContext<IdentityUser>` was the base class from Week 1, and the single-type-parameter overload already includes `IdentityRole`/`string` key). If any are missing, stop and re-plan this task — do not proceed with the assumption below.

- [ ] **Step 2: Register Identity in `Program.cs`**

In `src/KayeDM.Web/Program.cs`, add the usings:

```csharp
using Microsoft.AspNetCore.Identity;
```

Add after the existing service registrations (after `builder.Services.AddScoped<IExpenseService, ExpenseService>();`):

```csharp
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
    .AddIdentityCookies(cookieOptions =>
    {
        // Defaults are /Account/Login and /Account/AccessDenied -- neither
        // exists in this app (the /account/login match for unauthenticated
        // users only worked earlier by case-insensitive routing coincidence).
        // Wrong-role-but-authenticated users go home, not back to login.
        cookieOptions.ApplicationCookie?.Configure(options =>
        {
            options.LoginPath = "/account/login";
            options.AccessDeniedPath = "/";
        });
    });

builder.Services.AddAuthorizationCore();

builder.Services.AddIdentityCore<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
```

- [ ] **Step 3: Add auth middleware to the pipeline**

In `src/KayeDM.Web/Program.cs`, insert before the existing `app.UseAntiforgery();` line:

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
```

- [ ] **Step 4: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): register ASP.NET Core Identity services and auth middleware"
```

---

### Task 1: Seed `Owner`/`Cashier` roles and the two demo users

**Files:**
- Create: `src/KayeDM.Infrastructure/Identity/IdentitySeeder.cs`
- Modify: `src/KayeDM.Web/Program.cs`

**Interfaces:**
- Consumes: `RoleManager<IdentityRole>`, `UserManager<IdentityUser>` (Task 0).
- Produces: `KayeDM.Infrastructure.Identity.IdentitySeeder.SeedAsync(IServiceProvider services)` static method — called once at startup, consumed only by `Program.cs`.

- [ ] **Step 1: Write the seeder**

`src/KayeDM.Infrastructure/Identity/IdentitySeeder.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace KayeDM.Infrastructure.Identity;

public static class IdentitySeeder
{
    private const string OwnerRole = "Owner";
    private const string CashierRole = "Cashier";
    private const string SeedPassword = "KayeDM#2026";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();

        foreach (var role in new[] { OwnerRole, CashierRole })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        await EnsureUserAsync(userManager, "owner@kayedm.local", OwnerRole);
        await EnsureUserAsync(userManager, "cashier@kayedm.local", CashierRole);
    }

    private static async Task EnsureUserAsync(UserManager<IdentityUser> userManager, string email, string role)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            return;
        }

        var user = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, SeedPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed user {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(user, role);
    }
}
```

- [ ] **Step 2: Add the using for `Microsoft.Extensions.DependencyInjection`**

`GetRequiredService` needs `using Microsoft.Extensions.DependencyInjection;` — add it to the top of `IdentitySeeder.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace KayeDM.Infrastructure.Identity;
```

- [ ] **Step 3: Wire it into startup**

In `src/KayeDM.Web/Program.cs`, add the using:

```csharp
using KayeDM.Infrastructure.Identity;
```

In the existing startup-seeding scope block, add the call alongside `SeedDefaultCategoriesAsync`:

```csharp
using (var scope = app.Services.CreateScope())
{
    var expenseService = scope.ServiceProvider.GetRequiredService<IExpenseService>();
    await expenseService.SeedDefaultCategoriesAsync();
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 5: Manually verify seeding**

```bash
dotnet run --project src/KayeDM.Web
```

Stop it after a few seconds, then:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT u.Email, r.Name FROM AspNetUsers u JOIN AspNetUserRoles ur ON ur.UserId = u.Id JOIN AspNetRoles r ON r.Id = ur.RoleId;"
```

Expected: `owner@kayedm.local | Owner` and `cashier@kayedm.local | Cashier`.

- [ ] **Step 6: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): seed Owner/Cashier roles and demo users on startup"
```

---

### Task 2: Login page + login/logout minimal API endpoints

**Files:**
- Create: `src/KayeDM.Web/Components/Pages/Login.razor`
- Modify: `src/KayeDM.Web/Program.cs`

**Interfaces:**
- Consumes: `SignInManager<IdentityUser>` (Task 0).
- Produces: `GET /account/login` (page), `POST /account/do-login`, `POST /account/logout` — consumed by Task 3's `NavMenu` logout form and `NotAuthorized` redirect.

Sign-in/sign-out must run as a genuine HTTP request-response (they set the auth cookie), never as a Blazor Server interactive circuit event — that's why these are plain minimal-API endpoints, not component event handlers. The login page only renders a plain HTML `<form>` that posts to the endpoint; it does not use `EditForm`/`OnValidSubmit`. The POST endpoint is deliberately at a **different path** than the page itself (`/account/do-login`, not `/account/login`) — mapping a `MapPost` at the exact same route string as an existing `@page` component throws `AmbiguousMatchException` at request time (both the component's own endpoint and the minimal API endpoint match), which only surfaces when you actually submit the form, not at build time.

- [ ] **Step 1: Write the login page**

`src/KayeDM.Web/Components/Pages/Login.razor`:

```razor
@page "/account/login"
@using Microsoft.AspNetCore.Components

<PageTitle>Log In</PageTitle>

<h1>Log In</h1>

@if (HasError)
{
    <div class="alert alert-danger">Invalid email or password.</div>
}

<form method="post" action="account/do-login">
    <AntiforgeryToken />
    <input type="hidden" name="returnUrl" value="@ReturnUrl" />
    <div class="mb-2">
        <label>Email</label>
        <input type="email" name="email" class="form-control" required />
    </div>
    <div class="mb-2">
        <label>Password</label>
        <input type="password" name="password" class="form-control" required />
    </div>
    <button type="submit" class="btn btn-primary">Log In</button>
</form>

@code {
    [SupplyParameterFromQuery]
    private string? Error { get; set; }

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    private bool HasError => Error is not null;
}
```

- [ ] **Step 2: Add the login/logout endpoints**

In `src/KayeDM.Web/Program.cs`, add the usings:

```csharp
using Microsoft.AspNetCore.Authentication;
```

After `app.MapRazorComponents<App>().AddInteractiveServerRenderMode();`, add:

```csharp
app.MapPost("/account/do-login", async (HttpContext context, SignInManager<IdentityUser> signInManager) =>
{
    var form = await context.Request.ReadFormAsync();
    var email = form["email"].ToString();
    var password = form["password"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    var result = await signInManager.PasswordSignInAsync(email, password, isPersistent: true, lockoutOnFailure: false);

    var target = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
    return result.Succeeded
        ? Results.LocalRedirect(target)
        : Results.LocalRedirect($"/account/login?error=1&returnUrl={Uri.EscapeDataString(target)}");
});

app.MapPost("/account/logout", async (SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.LocalRedirect("/account/login");
});
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Manually verify login**

```bash
dotnet run --project src/KayeDM.Web --urls http://localhost:5217
```

Navigate to `http://localhost:5217/account/login`, submit `owner@kayedm.local` / `KayeDM#2026`. Expected: redirected to `/`, and:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT COUNT(*) FROM AspNetUsers WHERE Email = 'owner@kayedm.local';"
```

confirms the user exists (sign-in itself has no visible DB side effect beyond the cookie — the point of this check is just to confirm the seeded user is reachable). Stop the app.

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add login page and login/logout minimal API endpoints"
```

---

### Task 3: Route gating — `AuthorizeRouteView`, per-page `[Authorize]`, `NavMenu` auth UI

**Files:**
- Modify: `src/KayeDM.Web/Components/Routes.razor`
- Modify: `src/KayeDM.Web/Components/Layout/NavMenu.razor`
- Modify: every existing page under `src/KayeDM.Web/Components/Pages/*.razor` (add `@attribute [Authorize(...)]`)

**Interfaces:**
- Consumes: `AddCascadingAuthenticationState()` (Task 0), login/logout endpoints (Task 2).
- Produces: every route gated per blueprint §6; unauthenticated users redirected to `/account/login`.

Role mapping per blueprint §6: **Cashier** — `/pos`, `/buses/arrivals`, `/inventory/production`, `/inventory/waste`. **Owner** — everything else (`/menu`, `/buses/companies`, `/buses/report`, `/inventory/variance`, `/expenses`, `/expenses/categories`, `/expenses/report`, `/dashboard` (Task 18), `/closing` (Task 10)). `Home.razor` (`/`) stays open to both authenticated roles (landing page after login). `/account/login` stays anonymous (no `[Authorize]`).

- [ ] **Step 1: Gate the router**

In `src/KayeDM.Web/Components/Routes.razor`, replace the whole file:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@using KayeDM.Web.Components.Layout

<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                <RedirectToLogin />
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

- [ ] **Step 2: Add the `RedirectToLogin` helper component**

`AuthorizeRouteView`'s `NotAuthorized` content renders inside the existing interactive circuit, so a plain component using `NavigationManager` works here (unlike the login form itself, this doesn't need to set a cookie — it only needs to navigate).

Create `src/KayeDM.Web/Components/Layout/RedirectToLogin.razor`:

```razor
@inject NavigationManager NavigationManager

@code {
    protected override void OnInitialized()
    {
        var returnUrl = Uri.EscapeDataString(new Uri(NavigationManager.Uri).PathAndQuery);
        NavigationManager.NavigateTo($"account/login?returnUrl={returnUrl}", forceLoad: true);
    }
}
```

- [ ] **Step 3: Add `@attribute [Authorize(...)]` to every existing page**

Add the attribute line directly after the `@page "..."` directive in each file (add `@using Microsoft.AspNetCore.Authorization` alongside it if not already present via `_Imports.razor` — it isn't, so add it per-file):

`src/KayeDM.Web/Components/Pages/Home.razor` — add after its `@page` line (Owner + Cashier, i.e. any authenticated user):
```razor
@attribute [Authorize]
@using Microsoft.AspNetCore.Authorization
```

`src/KayeDM.Web/Components/Pages/Pos.razor`, `BusArrivals.razor`, `InventoryProduction.razor`, `InventoryWaste.razor` (Cashier + Owner):
```razor
@attribute [Authorize(Roles = "Owner,Cashier")]
@using Microsoft.AspNetCore.Authorization
```

`src/KayeDM.Web/Components/Pages/Menu.razor`, `BusCompanies.razor`, `BusReport.razor`, `InventoryVariance.razor`, `Expenses.razor`, `ExpenseCategories.razor`, `ExpenseReport.razor` (Owner only):
```razor
@attribute [Authorize(Roles = "Owner")]
@using Microsoft.AspNetCore.Authorization
```

(`/dashboard` and `/closing` get the same Owner-only attribute when they're created in Tasks 10 and 18 — no need to touch them now.)

- [ ] **Step 4: Add auth UI + role-gated links to `NavMenu`**

In `src/KayeDM.Web/Components/Layout/NavMenu.razor`, add at the top:

```razor
@using Microsoft.AspNetCore.Components.Authorization
```

Wrap the Owner-only nav items (`Menu`, `Bus Companies`, `Crew Meal Report`, `Variance Report`, `Expenses`, `Expense Categories`, `Expense Report`) each in:

```razor
<AuthorizeView Roles="Owner">
    <div class="nav-item px-3">
        <NavLink class="nav-link" href="menu">
            <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Menu
        </NavLink>
    </div>
</AuthorizeView>
```

(repeat the `<AuthorizeView Roles="Owner">` wrapper for each Owner-only link listed above, using each link's own markup — `POS`, `Bus Arrivals`, `Tray Production`, `Waste Log` stay unwrapped since both roles see them, `Home` stays unwrapped).

At the bottom of the nav, before the closing `</nav>`, add the login-state UI:

```razor
        <AuthorizeView>
            <Authorized>
                <div class="nav-item px-3">
                    <span class="nav-link">@context.User.Identity?.Name</span>
                </div>
                <div class="nav-item px-3">
                    <form method="post" action="account/logout">
                        <AntiforgeryToken />
                        <button type="submit" class="nav-link btn btn-link">Log Out</button>
                    </form>
                </div>
            </Authorized>
            <NotAuthorized>
                <div class="nav-item px-3">
                    <NavLink class="nav-link" href="account/login">Log In</NavLink>
                </div>
            </NotAuthorized>
        </AuthorizeView>
```

- [ ] **Step 5: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): gate all routes with AuthorizeRouteView and per-page role attributes"
```

---

### Task 4: Replace `LoggedById` TODOs with the real signed-in user id

**Files:**
- Modify: `src/KayeDM.Application/Inventory/InventoryModels.cs`
- Modify: `src/KayeDM.Infrastructure/Inventory/InventoryService.cs`
- Modify: `src/KayeDM.Application/Expenses/ExpenseModels.cs`
- Modify: `src/KayeDM.Infrastructure/Expenses/ExpenseService.cs`
- Modify: `src/KayeDM.Web/Components/Pages/InventoryWaste.razor`
- Modify: `src/KayeDM.Web/Components/Pages/Expenses.razor`

**Interfaces:**
- Consumes: `AuthenticationStateProvider` (standard Blazor service, available once Task 0's `AddCascadingAuthenticationState()` is registered).
- Produces: `LogWasteRequest`/`CreateExpenseRequest` gain a `string LoggedById` parameter — services use it instead of the hardcoded `"system"` string. No migration (column already exists).

- [ ] **Step 1: Add `LoggedById` to `LogWasteRequest`**

In `src/KayeDM.Application/Inventory/InventoryModels.cs`, change:

```csharp
public record LogWasteRequest(int DishBatchId, decimal TraysWasted, WasteReason Reason);
```

to:

```csharp
public record LogWasteRequest(int DishBatchId, decimal TraysWasted, WasteReason Reason, string LoggedById);
```

- [ ] **Step 2: Use it in `InventoryService.LogWasteAsync`**

In `src/KayeDM.Infrastructure/Inventory/InventoryService.cs`, change:

```csharp
            LoggedAt = DateTime.Now,
            LoggedById = "system"
        };

        db.WasteLogs.Add(log);
```

to:

```csharp
            LoggedAt = DateTime.Now,
            LoggedById = request.LoggedById
        };

        db.WasteLogs.Add(log);
```

- [ ] **Step 3: Add `LoggedById` to `CreateExpenseRequest`**

In `src/KayeDM.Application/Expenses/ExpenseModels.cs`, change:

```csharp
public record CreateExpenseRequest(
    DateTime Date,
    int ExpenseCategoryId,
    string Description,
    decimal Amount,
    ExpensePaymentMethod PaymentMethod,
    string? Vendor,
    string? ReceiptRef);
```

to:

```csharp
public record CreateExpenseRequest(
    DateTime Date,
    int ExpenseCategoryId,
    string Description,
    decimal Amount,
    ExpensePaymentMethod PaymentMethod,
    string? Vendor,
    string? ReceiptRef,
    string LoggedById);
```

(`UpdateExpenseRequest` doesn't need `LoggedById` — an edit doesn't change who originally logged it.)

- [ ] **Step 4: Use it in `ExpenseService.CreateExpenseAsync`**

In `src/KayeDM.Infrastructure/Expenses/ExpenseService.cs`, change:

```csharp
            Vendor = request.Vendor,
            ReceiptRef = request.ReceiptRef,
            LoggedById = "system",
            LoggedAt = DateTime.Now
        };
```

to:

```csharp
            Vendor = request.Vendor,
            ReceiptRef = request.ReceiptRef,
            LoggedById = request.LoggedById,
            LoggedAt = DateTime.Now
        };
```

- [ ] **Step 5: Resolve the current user id in `InventoryWaste.razor`**

In `src/KayeDM.Web/Components/Pages/InventoryWaste.razor`, add the injection:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@inject AuthenticationStateProvider AuthenticationStateProvider
```

Change `SubmitAsync`:

```csharp
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
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userId = authState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

            _lastLogged = await InventoryService.LogWasteAsync(new LogWasteRequest(_form.DishBatchId, _form.TraysWasted, _form.Reason, userId));
            _form = new WasteForm();
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }
```

- [ ] **Step 6: Resolve the current user id in `Expenses.razor`**

In `src/KayeDM.Web/Components/Pages/Expenses.razor`, add the injection:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@inject AuthenticationStateProvider AuthenticationStateProvider
```

Change the `CreateExpenseRequest` construction inside `SubmitAsync`:

```csharp
            if (_editingId is null)
            {
                var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
                var userId = authState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

                var request = new CreateExpenseRequest(
                    _form.Date.ToDateTime(TimeOnly.MinValue), _form.ExpenseCategoryId, _form.Description, _form.Amount, _form.PaymentMethod, _form.Vendor, _form.ReceiptRef, userId);
                await ExpenseService.CreateExpenseAsync(request);
            }
```

- [ ] **Step 7: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): resolve LoggedById from the signed-in user instead of hardcoded 'system'"
```

---

### Task 5: CHECKPOINT 1 — auth verification (STOP for user review)

**Files:** none (verification only).

- [ ] **Step 1: Full build + full test suite**

```bash
dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"
dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"
```

Expected: 0 errors, 30/30 tests passing (no new tests were added in Part A — auth is verified interactively, not via xUnit).

- [ ] **Step 2: Confirm `AddIdentitySchema` was/wasn't needed**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations list --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected: still exactly the 9 migrations from Week 3 — no `AddIdentitySchema` in the list, confirming Task 0's Step 1 finding held (Identity tables were already present from `InitialCreate`).

- [ ] **Step 3: Interactive login walkthrough**

Run the app, then verify with a browser tool (Playwright/Claude-in-Chrome) or manually:
1. Navigate to `/pos` while logged out → redirected to `/account/login`.
2. Log in as `cashier@kayedm.local` / `KayeDM#2026` → lands on `/`, nav shows only `Home`, `POS`, `Bus Arrivals`, `Tray Production`, `Waste Log`, plus the logged-in-as indicator and Log Out button.
3. As Cashier, navigate directly to `/dashboard` and `/closing` by URL → confirm both are blocked (redirected to login or shown "not authorized" — whichever `AuthorizeRouteView`'s default behavior produces for an authenticated-but-wrong-role user; if it silently shows nothing useful, note this as a finding rather than silently accepting it).
4. Log out, log in as `owner@kayedm.local` / `KayeDM#2026` → full nav visible, `/dashboard` and `/closing` are reachable (both will 404/be-blank until Tasks 10 and 18 exist yet — that's expected at this point, the check here is only that the route isn't blocked by role).
5. Log a waste entry or expense as Owner → confirm via `sqlcmd` that `LoggedById` now stores the Owner's actual `AspNetUsers.Id`, not `"system"`.

- [ ] **Step 4: STOP — present findings to the user**

Report: build/test status, the `AddIdentitySchema` confirmation, and the walkthrough results (especially step 3's Cashier-blocked-from-Owner-routes behavior). **Do not proceed to Part B until the user has reviewed this and given the go-ahead.**

---

## PART B — Daily Closing

### Task 6: `DailyClosing` entity + `AddDailyClosing` migration

**Files:**
- Create: `src/KayeDM.Domain/Entities/DailyClosing.cs`
- Modify: `src/KayeDM.Infrastructure/Data/AppDbContext.cs`
- Create (generated): `src/KayeDM.Infrastructure/Data/Migrations/*_AddDailyClosing.cs`

**Interfaces:**
- Produces: `KayeDM.Domain.Entities.DailyClosing`, `AppDbContext.DailyClosings` (`DbSet<DailyClosing>`) — consumed by Tasks 8, 9, 10.

- [ ] **Step 1: `DailyClosing` entity**

`src/KayeDM.Domain/Entities/DailyClosing.cs`:

```csharp
namespace KayeDM.Domain.Entities;

public class DailyClosing
{
    public int Id { get; set; }

    // Date-only in practice — always stored/queried at midnight, one row per calendar day.
    public DateTime Date { get; set; }

    public decimal TotalSales { get; set; }
    public decimal CashSales { get; set; }
    public decimal GCashSales { get; set; }
    public int OrderCount { get; set; }
    public int VoidedCount { get; set; }
    public int CrewMealsGiven { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetForDay { get; set; }

    public string ClosedById { get; set; } = string.Empty;
    public DateTime ClosedAt { get; set; }
}
```

- [ ] **Step 2: Add the DbSet and Fluent config**

In `src/KayeDM.Infrastructure/Data/AppDbContext.cs`, add the DbSet after `Expenses`:

```csharp
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<DailyClosing> DailyClosings => Set<DailyClosing>();
```

and Fluent config inside `OnModelCreating`, after the `Expense` block:

```csharp
        builder.Entity<DailyClosing>(entity =>
        {
            entity.Property(c => c.TotalSales).HasPrecision(10, 2);
            entity.Property(c => c.CashSales).HasPrecision(10, 2);
            entity.Property(c => c.GCashSales).HasPrecision(10, 2);
            entity.Property(c => c.TotalExpenses).HasPrecision(10, 2);
            entity.Property(c => c.NetForDay).HasPrecision(10, 2);
            entity.Property(c => c.ClosedById).HasMaxLength(450);

            entity.HasIndex(c => c.Date).IsUnique();
        });
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Generate the `AddDailyClosing` migration**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations add AddDailyClosing --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj --output-dir Data/Migrations
```

Expected: `Done.` Inspect the generated migration — it should contain exactly one `CreateTable` for `DailyClosings` with a unique index on `Date`, nothing else. If it touches any other table (a repeat of the Week 2/3 convention-discovery issue), stop, remove the migration, and check whether any other domain property was added ahead of its own task without Fluent config yet.

- [ ] **Step 5: Apply it**

```bash
dotnet ef database update --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected: `Done.` Verify:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT name FROM sys.tables WHERE name = 'DailyClosings';"
```

- [ ] **Step 6: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): add DailyClosing entity and AddDailyClosing migration"
```

---

### Task 7: Closing DTOs + `IClosingService`

**Files:**
- Create: `src/KayeDM.Application/Closing/ClosingModels.cs`
- Create: `src/KayeDM.Application/Closing/IClosingService.cs`

**Interfaces:**
- Produces: `TodaysFiguresDto`, `DailyClosingDto`, `IClosingService` — consumed by Task 10 (implementation), Task 11 (`/closing` page).

- [ ] **Step 1: DTOs**

`src/KayeDM.Application/Closing/ClosingModels.cs`:

```csharp
namespace KayeDM.Application.Closing;

public record TodaysFiguresDto(
    DateOnly Date,
    decimal TotalSales,
    decimal CashSales,
    decimal GCashSales,
    int OrderCount,
    int VoidedCount,
    int CrewMealsGiven,
    decimal TotalExpenses,
    decimal NetForDay,
    bool AlreadyClosed);

public record DailyClosingDto(
    int Id,
    DateOnly Date,
    decimal TotalSales,
    decimal CashSales,
    decimal GCashSales,
    int OrderCount,
    int VoidedCount,
    int CrewMealsGiven,
    decimal TotalExpenses,
    decimal NetForDay,
    string ClosedById,
    DateTime ClosedAt);
```

- [ ] **Step 2: `IClosingService`**

`src/KayeDM.Application/Closing/IClosingService.cs`:

```csharp
namespace KayeDM.Application.Closing;

public interface IClosingService
{
    // Always computes for today — closing is only ever done for the current day.
    Task<TodaysFiguresDto> GetTodaysFiguresAsync();

    Task<DailyClosingDto> CreateClosingAsync(string closedById);

    Task<bool> IsDateClosedAsync(DateOnly date);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(application): add Closing DTOs and IClosingService interface"
```

---

### Task 8: `DateClosedException` + `ClosingGuard` shared helper (TDD)

**Files:**
- Create: `src/KayeDM.Domain/Exceptions/DateClosedException.cs`
- Create: `src/KayeDM.Infrastructure/Closing/ClosingGuard.cs`
- Create: `tests/KayeDM.Tests/Closing/ClosingGuardTests.cs`

**Interfaces:**
- Consumes: `AppDbContext.DailyClosings` (Task 6).
- Produces: `KayeDM.Domain.Exceptions.DateClosedException : DomainException`, `KayeDM.Infrastructure.Closing.ClosingGuard.EnsureDateNotClosedAsync(AppDbContext db, DateTime date, string action)` — consumed by Task 9 (`OrderService`, `ExpenseService`).

A closed date locks itself **and every date before it** ("on/before it" per blueprint domain rule 3) — the check is "does any `DailyClosing` exist with `Date >= targetDate`", not just an exact-date match.

- [ ] **Step 1: `DateClosedException`**

`src/KayeDM.Domain/Exceptions/DateClosedException.cs`:

```csharp
namespace KayeDM.Domain.Exceptions;

public class DateClosedException : DomainException
{
    public DateClosedException(string message) : base(message)
    {
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/KayeDM.Tests/Closing/ClosingGuardTests.cs`:

```csharp
using FluentAssertions;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Closing;
using KayeDM.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Closing;

public class ClosingGuardTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public ClosingGuardTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task EnsureDateNotClosedAsync_Throws_WhenExactDateIsClosed()
    {
        var today = DateTime.Now.Date;
        _db.DailyClosings.Add(new DailyClosing { Date = today, ClosedById = "u1", ClosedAt = DateTime.Now });
        _db.SaveChanges();

        var act = async () => await ClosingGuard.EnsureDateNotClosedAsync(_db, today, "create order");

        await act.Should().ThrowAsync<DateClosedException>();
    }

    [Fact]
    public async Task EnsureDateNotClosedAsync_Throws_WhenAnEarlierDateThanAClosedDate()
    {
        var today = DateTime.Now.Date;
        _db.DailyClosings.Add(new DailyClosing { Date = today, ClosedById = "u1", ClosedAt = DateTime.Now });
        _db.SaveChanges();

        var yesterday = today.AddDays(-1);
        var act = async () => await ClosingGuard.EnsureDateNotClosedAsync(_db, yesterday, "create expense");

        await act.Should().ThrowAsync<DateClosedException>();
    }

    [Fact]
    public async Task EnsureDateNotClosedAsync_DoesNotThrow_WhenNoClosingCoversTheDate()
    {
        var today = DateTime.Now.Date;

        var act = async () => await ClosingGuard.EnsureDateNotClosedAsync(_db, today, "create order");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureDateNotClosedAsync_DoesNotThrow_WhenOnlyAnEarlierClosingExists()
    {
        var today = DateTime.Now.Date;
        _db.DailyClosings.Add(new DailyClosing { Date = today.AddDays(-2), ClosedById = "u1", ClosedAt = DateTime.Now });
        _db.SaveChanges();

        var act = async () => await ClosingGuard.EnsureDateNotClosedAsync(_db, today, "create order");

        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 3: Run the tests to confirm they fail to compile**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~ClosingGuardTests"`
Expected: build error — `ClosingGuard` and `DateClosedException` don't exist yet.

- [ ] **Step 4: Implement `ClosingGuard`**

`src/KayeDM.Infrastructure/Closing/ClosingGuard.cs`:

```csharp
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Closing;

public static class ClosingGuard
{
    // "On/before a closed date" — a closing on date D locks D and everything
    // before it, so the check is whether any closing exists on or after the
    // target date.
    public static async Task EnsureDateNotClosedAsync(AppDbContext db, DateTime date, string action)
    {
        var isClosed = await db.DailyClosings.AnyAsync(c => c.Date >= date.Date);
        if (isClosed)
        {
            throw new DateClosedException($"Cannot {action} — {date.Date:MMM d, yyyy} is on or before a closed date.");
        }
    }
}
```

- [ ] **Step 5: Run the tests again to confirm they pass**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~ClosingGuardTests"`
Expected: 4 tests run, all PASS.

- [ ] **Step 6: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): add DateClosedException and ClosingGuard with tests for the on/before-a-closed-date rule"
```

---

### Task 9: Enforce the closing lock in `OrderService` and `ExpenseService` (TDD)

**Files:**
- Modify: `src/KayeDM.Infrastructure/Orders/OrderService.cs`
- Modify: `src/KayeDM.Infrastructure/Expenses/ExpenseService.cs`
- Create: `tests/KayeDM.Tests/Closing/ClosingLockTests.cs`

**Interfaces:**
- Consumes: `ClosingGuard` (Task 8).
- Produces: `OrderService.CreateOrderAsync`/`CreateCrewMealOrderAsync`/`VoidOrderAsync` and `ExpenseService.CreateExpenseAsync`/`UpdateExpenseAsync` all throw `DateClosedException` when the relevant date is locked.

This is the accounting-critical rule — build it test-first, one test per guarded method.

- [ ] **Step 1: Write the failing tests**

`tests/KayeDM.Tests/Closing/ClosingLockTests.cs`:

```csharp
using FluentAssertions;
using KayeDM.Application.Expenses;
using KayeDM.Application.Orders;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Expenses;
using KayeDM.Infrastructure.Orders;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Closing;

public class ClosingLockTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly OrderService _orderService;
    private readonly ExpenseService _expenseService;
    private readonly DateTime _today = DateTime.Now.Date;

    public ClosingLockTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _orderService = new OrderService(new TestDbContextFactory(options));
        _expenseService = new ExpenseService(new TestDbContextFactory(options));

        _db.MenuItems.Add(new MenuItem { Id = 1, Name = "Adobo", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 1 });
        _db.ExpenseCategories.Add(new ExpenseCategory { Id = 1, Name = "Ingredients", Type = ExpenseCategoryType.Ingredients, IsActive = true });
        _db.DailyClosings.Add(new DailyClosing { Date = _today, ClosedById = "u1", ClosedAt = DateTime.Now });
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
    public async Task CreateOrderAsync_Throws_WhenTodayIsClosed()
    {
        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 1) }, PaymentMethod.Cash, 100m, null);

        var act = async () => await _orderService.CreateOrderAsync(request);

        await act.Should().ThrowAsync<DateClosedException>();
    }

    [Fact]
    public async Task VoidOrderAsync_Throws_WhenOrderDateIsClosed()
    {
        // Insert directly (bypassing CreateOrderAsync, which is itself now
        // guarded) to get a completed order dated today under a closing.
        var order = new Order
        {
            OrderNumber = "20260705-999",
            CreatedAt = _today,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 90m } }
        };
        _db.Orders.Add(order);
        _db.SaveChanges();

        var act = async () => await _orderService.VoidOrderAsync(order.Id, "customer changed mind");

        await act.Should().ThrowAsync<DateClosedException>();
    }

    [Fact]
    public async Task CreateExpenseAsync_Throws_WhenDateIsClosed()
    {
        var request = new CreateExpenseRequest(_today, 1, "Rice", 500m, ExpensePaymentMethod.Cash, null, null, "u1");

        var act = async () => await _expenseService.CreateExpenseAsync(request);

        await act.Should().ThrowAsync<DateClosedException>();
    }

    [Fact]
    public async Task UpdateExpenseAsync_Throws_WhenExpenseDateIsClosed()
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

        var request = new UpdateExpenseRequest(_today, 1, "Rice (corrected)", 550m, ExpensePaymentMethod.Cash, null, null);
        var act = async () => await _expenseService.UpdateExpenseAsync(expense.Id, request);

        await act.Should().ThrowAsync<DateClosedException>();
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~ClosingLockTests"`
Expected: 4 tests run, all FAIL (no `DateClosedException` thrown yet).

- [ ] **Step 3: Guard `OrderService`**

In `src/KayeDM.Infrastructure/Orders/OrderService.cs`, add the using:

```csharp
using KayeDM.Infrastructure.Closing;
```

At the top of `CreateOrderAsync`, right after the `await using var db = ...` line:

```csharp
        await ClosingGuard.EnsureDateNotClosedAsync(db, DateTime.Now.Date, "create an order");
```

At the top of `CreateCrewMealOrderAsync`, same placement:

```csharp
        await ClosingGuard.EnsureDateNotClosedAsync(db, DateTime.Now.Date, "create a crew meal order");
```

In `VoidOrderAsync`, after the order is fetched (after the `?? throw new DomainException(...)` line, before the `if (order.Status == OrderStatus.Voided)` check):

```csharp
        await ClosingGuard.EnsureDateNotClosedAsync(db, order.CreatedAt.Date, "void this order");
```

- [ ] **Step 4: Guard `ExpenseService`**

In `src/KayeDM.Infrastructure/Expenses/ExpenseService.cs`, add the using:

```csharp
using KayeDM.Infrastructure.Closing;
```

At the top of `CreateExpenseAsync`, right after the `await using var db = ...` line:

```csharp
        await ClosingGuard.EnsureDateNotClosedAsync(db, request.Date.Date, "create an expense");
```

At the top of `UpdateExpenseAsync`, after the existing expense is fetched (guard the *existing* expense's date — editing a currently-open expense to move it under a closed date is covered by guarding `request.Date` too, so guard both):

```csharp
        var entity = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id)
            ?? throw new DomainException($"Expense {id} not found.");

        await ClosingGuard.EnsureDateNotClosedAsync(db, entity.Date, "edit this expense");
        await ClosingGuard.EnsureDateNotClosedAsync(db, request.Date.Date, "edit this expense");
```

- [ ] **Step 5: Run the tests again to confirm they pass**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~ClosingLockTests"`
Expected: 4 tests run, all PASS.

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"`
Expected: `Passed! - Failed: 0`. All Week 1–3 tests must still pass unmodified — none of them create closings, so none should trip the new guard.

- [ ] **Step 7: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): enforce closing lock in OrderService and ExpenseService"
```

---

### Task 10: `ClosingService` implementation + DI + tests (TDD)

**Files:**
- Create: `src/KayeDM.Infrastructure/Closing/ClosingService.cs`
- Create: `tests/KayeDM.Tests/Closing/ClosingServiceTests.cs`
- Modify: `src/KayeDM.Web/Program.cs`

**Interfaces:**
- Consumes: `IClosingService` (Task 7); `AvailabilityCalculator`-style direct queries against `Orders`, `OrderLines`, `Expenses`, `DailyClosings`.
- Produces: `KayeDM.Infrastructure.Closing.ClosingService : IClosingService` — consumed by the `/closing` page (Task 11).

- [ ] **Step 1: Write the service**

`src/KayeDM.Infrastructure/Closing/ClosingService.cs`:

```csharp
using KayeDM.Application.Closing;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Closing;

public class ClosingService : IClosingService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public ClosingService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<TodaysFiguresDto> GetTodaysFiguresAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var today = DateTime.Now.Date;
        var tomorrow = today.AddDays(1);

        // Project only the columns this method actually uses — never pull
        // whole Order entities just to sum/count a handful of fields.
        var completedOrders = await db.Orders
            .Where(o => o.CreatedAt >= today && o.CreatedAt < tomorrow && o.Status == OrderStatus.Completed)
            .Select(o => new { o.AmountTendered, o.ChangeGiven, o.PaymentMethod, o.IsCrewMeal })
            .ToListAsync();

        var voidedCount = await db.Orders
            .CountAsync(o => o.CreatedAt >= today && o.CreatedAt < tomorrow && o.Status == OrderStatus.Voided);

        // Materialize the narrow projection, then sum client-side. This is
        // forced by a SQLite test-provider limitation (it can't translate
        // SUM over decimal into SQL) — not a stylistic choice.
        var todaysExpenseAmounts = await db.Expenses
            .Where(e => e.Date >= today && e.Date < tomorrow)
            .Select(e => e.Amount)
            .ToListAsync();
        var totalExpenses = todaysExpenseAmounts.Sum();

        var totalSales = completedOrders.Sum(o => o.AmountTendered - o.ChangeGiven);
        var cashSales = completedOrders.Where(o => o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.AmountTendered - o.ChangeGiven);
        var gcashSales = completedOrders.Where(o => o.PaymentMethod == PaymentMethod.GCash).Sum(o => o.AmountTendered - o.ChangeGiven);
        var crewMealsGiven = completedOrders.Count(o => o.IsCrewMeal);

        var alreadyClosed = await db.DailyClosings.AnyAsync(c => c.Date == today);

        return new TodaysFiguresDto(
            DateOnly.FromDateTime(today),
            totalSales,
            cashSales,
            gcashSales,
            completedOrders.Count,
            voidedCount,
            crewMealsGiven,
            totalExpenses,
            totalSales - totalExpenses,
            alreadyClosed);
    }

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

    public async Task<bool> IsDateClosedAsync(DateOnly date)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var target = date.ToDateTime(TimeOnly.MinValue);
        return await db.DailyClosings.AnyAsync(c => c.Date >= target);
    }
}
```

- [ ] **Step 2: Write the tests**

`tests/KayeDM.Tests/Closing/ClosingServiceTests.cs`:

```csharp
using FluentAssertions;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Closing;
using KayeDM.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Closing;

public class ClosingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ClosingService _sut;
    private readonly DateTime _today = DateTime.Now.Date;

    public ClosingServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new ClosingService(new TestDbContextFactory(options));

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
    public async Task GetTodaysFiguresAsync_ComputesNetForDay_FromSalesMinusExpenses()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001",
            CreatedAt = DateTime.Now,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 100m,
            ChangeGiven = 10m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 90m } }
        });
        _db.ExpenseCategories.Add(new ExpenseCategory { Id = 1, Name = "Ingredients", Type = ExpenseCategoryType.Ingredients, IsActive = true });
        _db.SaveChanges();
        _db.Expenses.Add(new Expense { Date = _today, ExpenseCategoryId = 1, Description = "Rice", Amount = 30m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "u1", LoggedAt = DateTime.Now });
        _db.SaveChanges();

        var figures = await _sut.GetTodaysFiguresAsync();

        figures.TotalSales.Should().Be(90m);
        figures.TotalExpenses.Should().Be(30m);
        figures.NetForDay.Should().Be(60m);
    }

    [Fact]
    public async Task CreateClosingAsync_PersistsSnapshot_MatchingTodaysFigures()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001",
            CreatedAt = DateTime.Now,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 90m,
            ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 90m } }
        });
        _db.SaveChanges();

        var closing = await _sut.CreateClosingAsync("owner-1");

        closing.TotalSales.Should().Be(90m);
        closing.ClosedById.Should().Be("owner-1");
    }

    [Fact]
    public async Task CreateClosingAsync_Throws_WhenTodayAlreadyClosed()
    {
        await _sut.CreateClosingAsync("owner-1");

        var act = async () => await _sut.CreateClosingAsync("owner-1");

        await act.Should().ThrowAsync<KayeDM.Domain.Exceptions.DomainException>();
    }

    [Fact]
    public async Task IsDateClosedAsync_ReturnsTrue_ForDatesOnOrBeforeAClosing()
    {
        await _sut.CreateClosingAsync("owner-1");

        var todayClosed = await _sut.IsDateClosedAsync(DateOnly.FromDateTime(_today));
        var yesterdayClosed = await _sut.IsDateClosedAsync(DateOnly.FromDateTime(_today.AddDays(-1)));
        var tomorrowClosed = await _sut.IsDateClosedAsync(DateOnly.FromDateTime(_today.AddDays(1)));

        todayClosed.Should().BeTrue();
        yesterdayClosed.Should().BeTrue();
        tomorrowClosed.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~ClosingServiceTests"`
Expected: 4 tests run, all PASS.

- [ ] **Step 4: Register `ClosingService` in DI**

In `src/KayeDM.Web/Program.cs`, add the using and registration:

```csharp
using KayeDM.Application.Closing;
using KayeDM.Infrastructure.Closing;
```

```csharp
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IClosingService, ClosingService>();
```

- [ ] **Step 5: Run the full test suite + build**

```bash
dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"
dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"
```

Expected: all tests pass, 0 build errors.

- [ ] **Step 6: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): implement ClosingService with immutability and net-calculation tests"
```

---

### Task 11: `/closing` page

**Files:**
- Create: `src/KayeDM.Web/Components/Pages/Closing.razor`
- Modify: `src/KayeDM.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `IClosingService` (Task 10, registered in DI); `AuthenticationStateProvider` (for `closedById`).

- [ ] **Step 1: Write the page**

`src/KayeDM.Web/Components/Pages/Closing.razor`:

```razor
@page "/closing"
@attribute [Authorize(Roles = "Owner")]
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using KayeDM.Application.Closing
@using KayeDM.Domain.Exceptions
@inject IClosingService ClosingService
@inject AuthenticationStateProvider AuthenticationStateProvider

<PageTitle>Daily Closing</PageTitle>

<h1>Daily Closing — @DateTime.Now.ToString("MMM d, yyyy")</h1>

@if (_figures is null)
{
    <p>Loading…</p>
}
else if (_figures.AlreadyClosed)
{
    <div class="alert alert-success">Today has already been closed. No further orders, voids, or expenses can be logged for today or earlier.</div>
}
else
{
    <table class="table">
        <tbody>
            <tr><th>Total Sales</th><td>@FormatPeso(_figures.TotalSales)</td></tr>
            <tr><th>Cash Sales</th><td>@FormatPeso(_figures.CashSales)</td></tr>
            <tr><th>GCash Sales</th><td>@FormatPeso(_figures.GCashSales)</td></tr>
            <tr><th>Order Count</th><td>@_figures.OrderCount</td></tr>
            <tr><th>Voided Count</th><td>@_figures.VoidedCount</td></tr>
            <tr><th>Crew Meals Given</th><td>@_figures.CrewMealsGiven</td></tr>
            <tr><th>Total Expenses</th><td>@FormatPeso(_figures.TotalExpenses)</td></tr>
            <tr><th>Net For Day</th><td>@FormatPeso(_figures.NetForDay)</td></tr>
        </tbody>
    </table>

    <button class="btn btn-danger btn-lg" @onclick="ConfirmCloseAsync">Confirm — Close Today</button>
}

@if (_error is not null)
{
    <div class="alert alert-danger">@_error</div>
}

@code {
    private TodaysFiguresDto? _figures;
    private string? _error;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync() => _figures = await ClosingService.GetTodaysFiguresAsync();

    private async Task ConfirmCloseAsync()
    {
        _error = null;
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userId = authState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

            await ClosingService.CreateClosingAsync(userId);
            await LoadAsync();
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
    }

    private static string FormatPeso(decimal amount) => string.Format("₱{0:N2}", amount);
}
```

- [ ] **Step 2: Add the nav link**

In `src/KayeDM.Web/Components/Layout/NavMenu.razor`, add an `<AuthorizeView Roles="Owner">`-wrapped link after the Expense Report link (before the login-state block added in Task 3):

```razor
<AuthorizeView Roles="Owner">
    <div class="nav-item px-3">
        <NavLink class="nav-link" href="closing">
            <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Daily Closing
        </NavLink>
    </div>
</AuthorizeView>
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add /closing daily Z-reading page"
```

---

### Task 12: CHECKPOINT 2 — closing-lock verification (STOP for user review)

**Files:** none (verification only).

- [ ] **Step 1: Full build + full test suite**

```bash
dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"
dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"
```

Expected: 0 errors. Test count should be 30 (Weeks 1–3) + 4 (`ClosingGuardTests`) + 4 (`ClosingLockTests`) + 4 (`ClosingServiceTests`) = 42, all passing — this is exactly the accounting-critical rule the user asked to see proven before continuing.

- [ ] **Step 2: Interactive walkthrough**

As Owner: log into `/pos`, complete one order, log one expense on `/expenses`, then go to `/closing` and confirm the figures match. Click "Confirm — Close Today". Then:
1. Try to create another order on `/pos` → expect the `DateClosedException` message surfaced as an inline error (via `catch (DomainException ex)`), not a crash.
2. Try to log another expense on `/expenses` for today's date → same expected rejection.
3. Reload `/closing` → confirm it now shows "already closed."

- [ ] **Step 3: STOP — present findings to the user**

Report: build/test status, the 42-test count, and the walkthrough results confirming closed dates actually reject new orders and expenses. **Do not proceed to Part C until the user has reviewed this and given the go-ahead.**

---

## PART C — Analytics Dashboard

### Task 13: Add the chart library

**Files:**
- Modify: `src/KayeDM.Web/KayeDM.Web.csproj`
- Modify: `src/KayeDM.Web/Program.cs`
- Modify: `src/KayeDM.Web/Components/_Imports.razor`

**Interfaces:**
- Produces: `ApexCharts` package registered for DI and available to Razor pages — consumed by Task 22.

`Blazor-ApexCharts` (MIT-licensed, actively maintained wrapper around ApexCharts.js) is the one new dependency this week, matching the blueprint's own suggestion (§2 Tech Stack table).

- [ ] **Step 1: Add the package**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet add src/KayeDM.Web/KayeDM.Web.csproj package Blazor-ApexCharts
```

Record whatever version this resolves to (check `src/KayeDM.Web/KayeDM.Web.csproj` after the command) — that becomes the pinned version for the rest of the project, same convention as the EF Core packages. (Resolved to **6.1.0**.)

- [ ] **Step 2: Register the service**

In `src/KayeDM.Web/Program.cs`, add the using:

```csharp
using ApexCharts;
```

Add the registration:

```csharp
builder.Services.AddApexCharts();
```

- [ ] **Step 3: Add the global using**

In `src/KayeDM.Web/Components/_Imports.razor`, add:

```razor
@using ApexCharts
```

- [ ] **Step 4: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add Blazor-ApexCharts for dashboard charts"
```

---

### Task 14: Dashboard DTOs + `IDashboardService`

**Files:**
- Create: `src/KayeDM.Application/Dashboard/DashboardModels.cs`
- Create: `src/KayeDM.Application/Dashboard/IDashboardService.cs`

**Interfaces:**
- Produces: all Dashboard DTOs and `IDashboardService` — consumed by Tasks 15–19 (implementation), Task 22 (page).

- [ ] **Step 1: DTOs**

`src/KayeDM.Application/Dashboard/DashboardModels.cs`:

```csharp
namespace KayeDM.Application.Dashboard;

public record DashboardKpiDto(
    decimal TotalSales,
    decimal TotalExpenses,
    decimal NetProfit,
    int OrderCount,
    decimal AverageTicket,
    int CrewMealsGiven,
    decimal CrewMealsEstimatedCost);

public record HourlySalesPoint(int Hour, decimal Sales, int OrderCount);

public record BusArrivalMarker(DateTime ArrivedAt, string CompanyName, string BusNumber);

public record SalesByHourResult(DateOnly Date, IReadOnlyList<HourlySalesPoint> Hours, IReadOnlyList<BusArrivalMarker> Arrivals);

public record DailyTrendPoint(DateOnly Date, decimal Revenue, decimal Expenses, decimal Net);

public record ExpenseCategoryBreakdownRow(string CategoryName, decimal Amount);

public record TopDishRow(int MenuItemId, string MenuItemName, decimal Revenue, int QuantitySold);

public record WasteByDishRow(int MenuItemId, string MenuItemName, int Produced, int Wasted, decimal WastePercent);

// DirectSales/Count = orders explicitly assigned to one of this company's trips
// via the POS "assign to bus" dropdown. WaveAttributed* = unassigned orders
// completed within +/-20 minutes of any of this company's arrivals -- a
// heuristic, labeled as such in the UI, and can double-count an order across
// companies when waves overlap (multiple buses arriving together).
public record BusCompanySalesRow(
    int BusCompanyId,
    string CompanyName,
    decimal DirectSales,
    int DirectOrderCount,
    decimal WaveAttributedSales,
    int WaveAttributedOrderCount);

public record PaymentMethodSplitRow(string PaymentMethod, decimal Amount, int OrderCount);

public record InsightCallout(string Title, string Detail);

public record DashboardInsightsRequest(DateOnly From, DateOnly To);
```

- [ ] **Step 2: `IDashboardService`**

`src/KayeDM.Application/Dashboard/IDashboardService.cs`:

```csharp
namespace KayeDM.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardKpiDto> GetKpisAsync(DateOnly from, DateOnly to);
    Task<SalesByHourResult> GetSalesByHourAsync(DateOnly date);
    Task<List<DailyTrendPoint>> GetRevenueExpenseTrendAsync(DateOnly from, DateOnly to);
    Task<List<ExpenseCategoryBreakdownRow>> GetExpenseBreakdownAsync(int year, int month);
    Task<List<TopDishRow>> GetTopDishesAsync(int days);
    Task<List<WasteByDishRow>> GetWasteByDishAsync(int days);
    Task<List<BusCompanySalesRow>> GetSalesPerBusCompanyAsync(DateOnly from, DateOnly to);
    Task<List<PaymentMethodSplitRow>> GetPaymentMethodSplitAsync(DateOnly from, DateOnly to);
    Task<List<InsightCallout>> GetInsightsAsync(DateOnly from, DateOnly to);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(application): add Dashboard DTOs and IDashboardService interface"
```

---

### Task 15: `DashboardService` part 1 — KPIs, sales-by-hour, revenue trend, expense breakdown (TDD)

**Files:**
- Create: `src/KayeDM.Infrastructure/Dashboard/DashboardService.cs`
- Create: `tests/KayeDM.Tests/Dashboard/DashboardServiceTests.cs`

**Interfaces:**
- Consumes: `IDashboardService` (Task 14); `AppDbContext.Orders`/`OrderLines`/`Expenses`/`BusTrips`.
- Produces: `KayeDM.Infrastructure.Dashboard.DashboardService : IDashboardService` (partial — `GetTopDishesAsync`/`GetWasteByDishAsync`/`GetSalesPerBusCompanyAsync`/`GetPaymentMethodSplitAsync`/`GetInsightsAsync` stubbed here, implemented in Task 16).

- [ ] **Step 1: Write the service with the four Task-16 methods stubbed**

`src/KayeDM.Infrastructure/Dashboard/DashboardService.cs`:

```csharp
using KayeDM.Application.Dashboard;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public DashboardService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<DashboardKpiDto> GetKpisAsync(DateOnly from, DateOnly to)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var toDate = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        // Project only the columns this method actually uses — never pull
        // whole Order entities just to sum/count a handful of fields.
        var completedOrders = await db.Orders
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt < toDate && o.Status == OrderStatus.Completed)
            .Select(o => new { o.AmountTendered, o.ChangeGiven, o.IsCrewMeal })
            .ToListAsync();

        var totalSales = completedOrders.Sum(o => o.AmountTendered - o.ChangeGiven);

        // Materialize the narrow projection, then sum client-side. This is
        // forced by a SQLite test-provider limitation (it can't translate
        // SUM over decimal into SQL) — not a stylistic choice.
        var expenseAmounts = await db.Expenses
            .Where(e => e.Date >= fromDate && e.Date < toDate)
            .Select(e => e.Amount)
            .ToListAsync();
        var totalExpenses = expenseAmounts.Sum();

        var orderCount = completedOrders.Count;
        var averageTicket = orderCount == 0 ? 0m : totalSales / orderCount;
        var crewMealsGiven = completedOrders.Count(o => o.IsCrewMeal);

        var nonCrewLinePrices = await db.OrderLines
            .Where(l => l.Order.CreatedAt >= fromDate && l.Order.CreatedAt < toDate
                && l.Order.Status == OrderStatus.Completed && !l.Order.IsCrewMeal)
            .Select(l => l.UnitPriceAtSale)
            .ToListAsync();
        var avgLinePrice = nonCrewLinePrices.Count == 0 ? 0m : nonCrewLinePrices.Average();
        var crewMealsEstimatedCost = avgLinePrice * crewMealsGiven;

        return new DashboardKpiDto(totalSales, totalExpenses, totalSales - totalExpenses, orderCount, averageTicket, crewMealsGiven, crewMealsEstimatedCost);
    }

    public async Task<SalesByHourResult> GetSalesByHourAsync(DateOnly date)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        var orders = await db.Orders
            .Where(o => o.CreatedAt >= dayStart && o.CreatedAt < dayEnd && o.Status == OrderStatus.Completed)
            .Select(o => new { o.CreatedAt, Sales = o.AmountTendered - o.ChangeGiven })
            .ToListAsync();

        var hours = Enumerable.Range(0, 24)
            .Select(h => new HourlySalesPoint(
                h,
                orders.Where(o => o.CreatedAt.Hour == h).Sum(o => o.Sales),
                orders.Count(o => o.CreatedAt.Hour == h)))
            .ToList();

        // No .Include() needed — projecting t.BusCompany.Name directly in
        // Select translates to a SQL join and pulls only the columns named
        // in the DTO constructor, not the whole BusTrip/BusCompany rows.
        var arrivals = await db.BusTrips
            .AsNoTracking()
            .Where(t => t.ArrivedAt >= dayStart && t.ArrivedAt < dayEnd)
            .Select(t => new BusArrivalMarker(t.ArrivedAt, t.BusCompany.Name, t.BusNumber))
            .ToListAsync();

        return new SalesByHourResult(date, hours, arrivals);
    }

    public async Task<List<DailyTrendPoint>> GetRevenueExpenseTrendAsync(DateOnly from, DateOnly to)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var points = new List<DailyTrendPoint>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var dayStart = day.ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);

            // Materialize then sum client-side — the SQLite EF Core provider
            // (used by tests) can't translate SUM over decimal into SQL.
            var dayOrderTotals = await db.Orders
                .Where(o => o.CreatedAt >= dayStart && o.CreatedAt < dayEnd && o.Status == OrderStatus.Completed)
                .Select(o => o.AmountTendered - o.ChangeGiven)
                .ToListAsync();
            var revenue = dayOrderTotals.Sum();

            var dayExpenseAmounts = await db.Expenses
                .Where(e => e.Date >= dayStart && e.Date < dayEnd)
                .Select(e => e.Amount)
                .ToListAsync();
            var expenses = dayExpenseAmounts.Sum();

            points.Add(new DailyTrendPoint(day, revenue, expenses, revenue - expenses));
        }

        return points;
    }

    public async Task<List<ExpenseCategoryBreakdownRow>> GetExpenseBreakdownAsync(int year, int month)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        // Narrow SQL projection first (Where + Select of only CategoryName
        // and Amount, no whole entities) — then group and sum client-side.
        // GroupBy+SUM over decimal can't be translated to SQL by the SQLite
        // test provider, so the aggregation itself must happen after
        // materializing, not as a stylistic choice.
        var rows = await db.Expenses
            .AsNoTracking()
            .Where(e => e.Date >= monthStart && e.Date < monthEnd)
            .Select(e => new { CategoryName = e.ExpenseCategory.Name, e.Amount })
            .ToListAsync();

        return rows
            .GroupBy(r => r.CategoryName)
            .Select(g => new ExpenseCategoryBreakdownRow(g.Key, g.Sum(r => r.Amount)))
            .OrderByDescending(r => r.Amount)
            .ToList();
    }

    public Task<List<TopDishRow>> GetTopDishesAsync(int days) => throw new NotImplementedException();
    public Task<List<WasteByDishRow>> GetWasteByDishAsync(int days) => throw new NotImplementedException();
    public Task<List<BusCompanySalesRow>> GetSalesPerBusCompanyAsync(DateOnly from, DateOnly to) => throw new NotImplementedException();
    public Task<List<PaymentMethodSplitRow>> GetPaymentMethodSplitAsync(DateOnly from, DateOnly to) => throw new NotImplementedException();
    public Task<List<InsightCallout>> GetInsightsAsync(DateOnly from, DateOnly to) => throw new NotImplementedException();
}
```

- [ ] **Step 2: Write the tests for the four implemented methods**

`tests/KayeDM.Tests/Dashboard/DashboardServiceTests.cs`:

```csharp
using FluentAssertions;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Dashboard;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Dashboard;

public class DashboardServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly DashboardService _sut;
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Now);

    public DashboardServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new DashboardService(new TestDbContextFactory(options));

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
    public async Task GetKpisAsync_ComputesNetProfit_AndCrewMealsEstimatedCost()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 90m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 90m } }
        });
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-002", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            IsCrewMeal = true, AmountTendered = 0m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 0m } }
        });
        _db.ExpenseCategories.Add(new ExpenseCategory { Id = 1, Name = "Ingredients", Type = ExpenseCategoryType.Ingredients, IsActive = true });
        _db.SaveChanges();
        _db.Expenses.Add(new Expense { Date = DateTime.Now.Date, ExpenseCategoryId = 1, Description = "Rice", Amount = 20m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "u1", LoggedAt = DateTime.Now });
        _db.SaveChanges();

        var kpis = await _sut.GetKpisAsync(_today, _today);

        kpis.TotalSales.Should().Be(90m);
        kpis.NetProfit.Should().Be(70m);
        kpis.CrewMealsGiven.Should().Be(1);
        kpis.CrewMealsEstimatedCost.Should().Be(90m);
    }

    [Fact]
    public async Task GetSalesByHourAsync_BucketsOrdersByHour_AndIncludesArrivalMarkers()
    {
        var hourNow = DateTime.Now.Hour;
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 90m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 90m } }
        });
        _db.BusCompanies.Add(new BusCompany { Id = 1, Name = "DLTB", CrewMealAllowancePerTrip = 2, IsActive = true });
        _db.BusTrips.Add(new BusTrip { Id = 1, BusCompanyId = 1, BusNumber = "8112", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now });
        _db.SaveChanges();

        var result = await _sut.GetSalesByHourAsync(_today);

        result.Hours.Should().HaveCount(24);
        result.Hours.Single(h => h.Hour == hourNow).Sales.Should().Be(90m);
        result.Arrivals.Should().ContainSingle(a => a.CompanyName == "DLTB" && a.BusNumber == "8112");
    }

    [Fact]
    public async Task GetRevenueExpenseTrendAsync_ReturnsOnePointPerDay_WithCorrectNet()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 100m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 100m } }
        });
        _db.SaveChanges();

        var trend = await _sut.GetRevenueExpenseTrendAsync(_today, _today);

        trend.Should().ContainSingle();
        trend[0].Revenue.Should().Be(100m);
        trend[0].Net.Should().Be(100m);
    }

    [Fact]
    public async Task GetExpenseBreakdownAsync_GroupsByCategory_ForTheGivenMonth()
    {
        _db.ExpenseCategories.AddRange(
            new ExpenseCategory { Id = 1, Name = "Ingredients", Type = ExpenseCategoryType.Ingredients, IsActive = true },
            new ExpenseCategory { Id = 2, Name = "Utilities", Type = ExpenseCategoryType.Utilities, IsActive = true });
        _db.SaveChanges();
        _db.Expenses.AddRange(
            new Expense { Date = new DateTime(2026, 7, 5), ExpenseCategoryId = 1, Description = "Rice", Amount = 500m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "u1", LoggedAt = DateTime.Now },
            new Expense { Date = new DateTime(2026, 7, 6), ExpenseCategoryId = 2, Description = "Electric", Amount = 300m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "u1", LoggedAt = DateTime.Now });
        _db.SaveChanges();

        var breakdown = await _sut.GetExpenseBreakdownAsync(2026, 7);

        breakdown.Should().HaveCount(2);
        breakdown.Single(r => r.CategoryName == "Ingredients").Amount.Should().Be(500m);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~DashboardServiceTests"`
Expected: 4 tests run, all PASS. (The other 5 `IDashboardService` methods are stubbed and not exercised yet — Task 16 implements and tests them.)

- [ ] **Step 4: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): implement DashboardService KPIs, sales-by-hour, revenue trend, and expense breakdown with tests"
```

---

### Task 16: `DashboardService` part 2 — top dishes, waste %, wave-attributed bus sales, payment split, insights (TDD)

**Files:**
- Modify: `src/KayeDM.Infrastructure/Dashboard/DashboardService.cs`
- Modify: `tests/KayeDM.Tests/Dashboard/DashboardServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext.DishBatches`/`WasteLogs`/`BusTrips`/`BusCompanies` (existing).
- Produces: the remaining 5 `IDashboardService` methods, fully implemented.

The wave-attribution query gets its own dedicated test — this is the one Week 4 explicitly calls out by name.

- [ ] **Step 1: Replace the five stub methods**

In `src/KayeDM.Infrastructure/Dashboard/DashboardService.cs`, replace:

```csharp
    public Task<List<TopDishRow>> GetTopDishesAsync(int days) => throw new NotImplementedException();
    public Task<List<WasteByDishRow>> GetWasteByDishAsync(int days) => throw new NotImplementedException();
    public Task<List<BusCompanySalesRow>> GetSalesPerBusCompanyAsync(DateOnly from, DateOnly to) => throw new NotImplementedException();
    public Task<List<PaymentMethodSplitRow>> GetPaymentMethodSplitAsync(DateOnly from, DateOnly to) => throw new NotImplementedException();
    public Task<List<InsightCallout>> GetInsightsAsync(DateOnly from, DateOnly to) => throw new NotImplementedException();
```

with:

```csharp
    public async Task<List<TopDishRow>> GetTopDishesAsync(int days)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var since = DateTime.Now.Date.AddDays(-(days - 1));

        // Narrow SQL projection first (Where + Select of only the columns
        // used below, no whole entities) — then group and sum client-side.
        // GroupBy+SUM over decimal (UnitPriceAtSale * Quantity) can't be
        // translated to SQL by the SQLite test provider, so the aggregation
        // itself must happen after materializing, not as a stylistic choice.
        var rows = await db.OrderLines
            .AsNoTracking()
            .Where(l => l.Order.CreatedAt >= since && l.Order.Status == OrderStatus.Completed)
            .Select(l => new { l.MenuItemId, MenuItemName = l.MenuItem.Name, l.UnitPriceAtSale, l.Quantity })
            .ToListAsync();

        return rows
            .GroupBy(r => new { r.MenuItemId, r.MenuItemName })
            .Select(g => new TopDishRow(g.Key.MenuItemId, g.Key.MenuItemName, g.Sum(r => r.UnitPriceAtSale * r.Quantity), g.Sum(r => r.Quantity)))
            .OrderByDescending(r => r.Revenue)
            .ToList();
    }

    public async Task<List<WasteByDishRow>> GetWasteByDishAsync(int days)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var since = DateTime.Now.Date.AddDays(-(days - 1));

        // Narrow projections — only the columns actually used below.
        var batches = await db.DishBatches
            .AsNoTracking()
            .Where(b => b.Date >= since)
            .Select(b => new { b.Id, b.MenuItemId, MenuItemName = b.MenuItem.Name, b.TraysProduced, b.ServingsPerTray })
            .ToListAsync();

        var wasteByBatch = await db.WasteLogs
            .AsNoTracking()
            .Where(w => w.DishBatch.Date >= since)
            .Select(w => new { w.DishBatchId, w.TraysWasted })
            .ToListAsync();

        var rows = new List<WasteByDishRow>();
        foreach (var group in batches.GroupBy(b => new { b.MenuItemId, b.MenuItemName }))
        {
            var produced = (int)group.Sum(b => b.TraysProduced * b.ServingsPerTray);
            var batchIds = group.Select(b => b.Id).ToHashSet();
            var wastedTrays = wasteByBatch.Where(w => batchIds.Contains(w.DishBatchId)).Sum(w => w.TraysWasted);
            var servingsPerTray = group.First().ServingsPerTray;
            var wasted = (int)(wastedTrays * servingsPerTray);
            var wastePercent = produced == 0 ? 0m : Math.Round((decimal)wasted / produced * 100m, 1);

            rows.Add(new WasteByDishRow(group.Key.MenuItemId, group.Key.MenuItemName, produced, wasted, wastePercent));
        }

        return rows.OrderByDescending(r => r.WastePercent).ToList();
    }

    public async Task<List<BusCompanySalesRow>> GetSalesPerBusCompanyAsync(DateOnly from, DateOnly to)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var toDate = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        // Narrow projections — only the columns actually used below.
        var companies = await db.BusCompanies.AsNoTracking()
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();
        var trips = await db.BusTrips
            .AsNoTracking()
            .Where(t => t.ArrivedAt >= fromDate && t.ArrivedAt < toDate)
            .Select(t => new { t.Id, t.BusCompanyId, t.ArrivedAt })
            .ToListAsync();
        var completedOrders = await db.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt < toDate && o.Status == OrderStatus.Completed)
            .Select(o => new { o.BusTripId, o.CreatedAt, Sales = o.AmountTendered - o.ChangeGiven })
            .ToListAsync();

        var rows = new List<BusCompanySalesRow>();
        foreach (var company in companies)
        {
            var companyTrips = trips.Where(t => t.BusCompanyId == company.Id).ToList();
            var tripIds = companyTrips.Select(t => t.Id).ToHashSet();

            var directOrders = completedOrders.Where(o => o.BusTripId is not null && tripIds.Contains(o.BusTripId.Value)).ToList();

            // Wave-attributed: unassigned orders completed within +/-20 minutes
            // of any of this company's arrivals. A heuristic (see DTO comment)
            // -- intentionally not mutually exclusive with other companies.
            var waveOrders = completedOrders
                .Where(o => o.BusTripId is null
                    && companyTrips.Any(t => Math.Abs((o.CreatedAt - t.ArrivedAt).TotalMinutes) <= 20))
                .ToList();

            rows.Add(new BusCompanySalesRow(
                company.Id, company.Name,
                directOrders.Sum(o => o.Sales), directOrders.Count,
                waveOrders.Sum(o => o.Sales), waveOrders.Count));
        }

        return rows.OrderByDescending(r => r.DirectSales + r.WaveAttributedSales).ToList();
    }

    public async Task<List<PaymentMethodSplitRow>> GetPaymentMethodSplitAsync(DateOnly from, DateOnly to)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var toDate = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        // Narrow SQL projection first — then group and sum client-side.
        // GroupBy+SUM over decimal can't be translated to SQL by the SQLite
        // test provider, so the aggregation itself must happen after
        // materializing, not as a stylistic choice.
        var rows = await db.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt < toDate && o.Status == OrderStatus.Completed)
            .Select(o => new { o.PaymentMethod, o.AmountTendered, o.ChangeGiven })
            .ToListAsync();

        return rows
            .GroupBy(r => r.PaymentMethod)
            .Select(g => new PaymentMethodSplitRow(g.Key.ToString(), g.Sum(r => r.AmountTendered - r.ChangeGiven), g.Count()))
            .ToList();
    }

    public async Task<List<InsightCallout>> GetInsightsAsync(DateOnly from, DateOnly to)
    {
        var insights = new List<InsightCallout>();

        var trend = await GetRevenueExpenseTrendAsync(from, to);
        if (trend.Count > 0)
        {
            var best = trend.OrderByDescending(t => t.Net).First();
            var worst = trend.OrderBy(t => t.Net).First();
            insights.Add(new InsightCallout("Best day", $"{best.Date:MMM d} had the best net for the period at {best.Net:C}."));
            if (worst.Net < best.Net)
            {
                insights.Add(new InsightCallout("Worst day", $"{worst.Date:MMM d} had the worst net for the period at {worst.Net:C}."));
            }
        }

        var days = to.DayNumber - from.DayNumber + 1;
        var waste = await GetWasteByDishAsync(days);
        var worstWaste = waste.Where(w => w.Produced > 0).OrderByDescending(w => w.WastePercent).FirstOrDefault();
        if (worstWaste is not null && worstWaste.WastePercent > 0)
        {
            insights.Add(new InsightCallout(
                $"{worstWaste.MenuItemName} waste rate {worstWaste.WastePercent}%",
                $"Over the last {days} day(s) — consider producing 1 fewer tray."));
        }

        var busSales = await GetSalesPerBusCompanyAsync(from, to);
        var ranked = busSales
            .Select(b => new { b.CompanyName, TotalOrders = b.DirectOrderCount + b.WaveAttributedOrderCount, TotalSales = b.DirectSales + b.WaveAttributedSales })
            .Where(b => b.TotalOrders > 0)
            .Select(b => new { b.CompanyName, AverageTake = b.TotalSales / b.TotalOrders })
            .ToList();
        if (ranked.Count > 0)
        {
            var overallAverage = ranked.Average(r => r.AverageTake);
            var best = ranked.OrderByDescending(r => r.AverageTake).First();
            if (overallAverage > 0 && best.AverageTake > overallAverage)
            {
                var pctAbove = Math.Round((best.AverageTake - overallAverage) / overallAverage * 100m, 0);
                insights.Add(new InsightCallout(
                    $"{best.CompanyName} averages {best.AverageTake:C} per order",
                    $"{pctAbove}% above the range average."));
            }
        }

        return insights;
    }
```

- [ ] **Step 2: Add the tests**

In `tests/KayeDM.Tests/Dashboard/DashboardServiceTests.cs`, add these test methods inside the `DashboardServiceTests` class, before the final closing brace:

```csharp
    [Fact]
    public async Task GetTopDishesAsync_RanksByRevenue_OverTheWindow()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 180m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 2, UnitPriceAtSale = 90m } }
        });
        _db.SaveChanges();

        var rows = await _sut.GetTopDishesAsync(7);

        rows.Should().ContainSingle();
        rows[0].Revenue.Should().Be(180m);
        rows[0].QuantitySold.Should().Be(2);
    }

    [Fact]
    public async Task GetWasteByDishAsync_ComputesWastePercent_OverTheWindow()
    {
        _db.DishBatches.Add(new DishBatch { Id = 1, MenuItemId = 1, Date = DateTime.Now.Date, TraysProduced = 2m, ServingsPerTray = 10, ProducedAt = DateTime.Now });
        _db.SaveChanges();
        _db.WasteLogs.Add(new WasteLog { DishBatchId = 1, TraysWasted = 0.5m, Reason = WasteReason.EndOfDay, LoggedAt = DateTime.Now, LoggedById = "u1" });
        _db.SaveChanges();

        var rows = await _sut.GetWasteByDishAsync(7);

        rows.Should().ContainSingle();
        rows[0].Produced.Should().Be(20);
        rows[0].Wasted.Should().Be(5);
        rows[0].WastePercent.Should().Be(25m);
    }

    [Fact]
    public async Task GetSalesPerBusCompanyAsync_SeparatesDirectFromWaveAttributedSales()
    {
        _db.BusCompanies.Add(new BusCompany { Id = 1, Name = "DLTB", CrewMealAllowancePerTrip = 2, IsActive = true });
        _db.BusTrips.Add(new BusTrip { Id = 1, BusCompanyId = 1, BusNumber = "8112", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now });
        _db.SaveChanges();

        // Direct: explicitly assigned to the trip.
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            BusTripId = 1, AmountTendered = 100m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 100m } }
        });
        // Wave-attributed: unassigned, but completed 10 minutes after arrival.
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-002", CreatedAt = DateTime.Now.AddMinutes(10), Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 50m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 50m } }
        });
        // Outside the window: unassigned, 30 minutes after arrival -- should NOT attribute.
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-003", CreatedAt = DateTime.Now.AddMinutes(30), Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 999m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 999m } }
        });
        _db.SaveChanges();

        var today = _today;
        var rows = await _sut.GetSalesPerBusCompanyAsync(today, today);

        var dltb = rows.Single(r => r.CompanyName == "DLTB");
        dltb.DirectSales.Should().Be(100m);
        dltb.DirectOrderCount.Should().Be(1);
        dltb.WaveAttributedSales.Should().Be(50m);
        dltb.WaveAttributedOrderCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPaymentMethodSplitAsync_GroupsByMethod()
    {
        _db.Orders.AddRange(
            new Order { OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash, AmountTendered = 100m, ChangeGiven = 0m, Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 100m } } },
            new Order { OrderNumber = "20260705-002", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.GCash, AmountTendered = 50m, ChangeGiven = 0m, Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 50m } } });
        _db.SaveChanges();

        var split = await _sut.GetPaymentMethodSplitAsync(_today, _today);

        split.Should().HaveCount(2);
        split.Single(s => s.PaymentMethod == "Cash").Amount.Should().Be(100m);
        split.Single(s => s.PaymentMethod == "GCash").Amount.Should().Be(50m);
    }

    [Fact]
    public async Task GetInsightsAsync_ReturnsAtLeastOneInsight_WhenTrendDataExists()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 100m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 100m } }
        });
        _db.SaveChanges();

        var insights = await _sut.GetInsightsAsync(_today, _today);

        insights.Should().NotBeEmpty();
    }
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj" --filter "FullyQualifiedName~DashboardServiceTests"`
Expected: 9 tests run, all PASS.

- [ ] **Step 4: Run the full test suite + build**

```bash
dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"
dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"
```

Expected: all tests pass, 0 build errors.

- [ ] **Step 5: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): implement top dishes, waste %, wave-attributed bus sales, payment split, and insights with tests"
```

---

### Task 17: Register `DashboardService` in DI

**Files:**
- Modify: `src/KayeDM.Web/Program.cs`

**Interfaces:**
- Consumes: `IDashboardService`/`DashboardService` (Tasks 14–16).

- [ ] **Step 1: Register it**

In `src/KayeDM.Web/Program.cs`, add the using:

```csharp
using KayeDM.Application.Dashboard;
using KayeDM.Infrastructure.Dashboard;
```

```csharp
builder.Services.AddScoped<IClosingService, ClosingService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): register DashboardService in DI"
```

---

### Task 18: `/dashboard` page, part 1 — KPI row, sales-by-hour chart, revenue trend chart

**Files:**
- Create: `src/KayeDM.Web/Components/Pages/Dashboard.razor`
- Modify: `src/KayeDM.Web/Components/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `IDashboardService.GetKpisAsync`, `GetSalesByHourAsync`, `GetRevenueExpenseTrendAsync` (Tasks 15–16); `ApexChart<T>` component (Task 13).

- [ ] **Step 1: Write the page skeleton with KPI row and the first two charts**

`src/KayeDM.Web/Components/Pages/Dashboard.razor`:

```razor
@page "/dashboard"
@attribute [Authorize(Roles = "Owner")]
@using Microsoft.AspNetCore.Authorization
@using KayeDM.Application.Dashboard
@inject IDashboardService DashboardService

<PageTitle>Dashboard</PageTitle>

<h1>Analytics Dashboard</h1>

<div class="mb-3">
    <label>From</label>
    <input type="date" class="form-control" @bind="_from" @bind:after="LoadAsync" />
    <label>To</label>
    <input type="date" class="form-control" @bind="_to" @bind:after="LoadAsync" />
</div>

@if (_kpis is not null)
{
    <div class="dashboard-kpi-row">
        <div>Sales: @FormatPeso(_kpis.TotalSales)</div>
        <div>Expenses: @FormatPeso(_kpis.TotalExpenses)</div>
        <div>Net Profit: @FormatPeso(_kpis.NetProfit)</div>
        <div>Orders: @_kpis.OrderCount</div>
        <div>Avg Ticket: @FormatPeso(_kpis.AverageTicket)</div>
        <div>Crew Meals: @_kpis.CrewMealsGiven (est. @FormatPeso(_kpis.CrewMealsEstimatedCost))</div>
    </div>
}

@if (_salesByHour is not null)
{
    <h2>Sales by Hour — @_salesByHour.Date.ToString("MMM d, yyyy")</h2>
    <p class="text-muted">Bus arrival markers: @string.Join(", ", _salesByHour.Arrivals.Select(a => $"{a.CompanyName} {a.ArrivedAt:h:mm tt}"))</p>
    <ApexChart TItem="HourlySalesPoint" Title="Sales by Hour">
        <ApexPointSeries TItem="HourlySalesPoint" Items="_salesByHour.Hours" Name="Sales" SeriesType="SeriesType.Bar"
            XValue="@(h => h.Hour.ToString())" YValue="@(h => (decimal?)h.Sales)" />
    </ApexChart>
}

@if (_trend is not null)
{
    <h2>Revenue vs. Expenses vs. Net (30-day trend)</h2>
    <ApexChart TItem="DailyTrendPoint" Title="Revenue vs Expenses vs Net">
        <ApexPointSeries TItem="DailyTrendPoint" Items="_trend" Name="Revenue" SeriesType="SeriesType.Line"
            XValue="@(t => t.Date.ToString("MMM d"))" YValue="@(t => (decimal?)t.Revenue)" />
        <ApexPointSeries TItem="DailyTrendPoint" Items="_trend" Name="Expenses" SeriesType="SeriesType.Line"
            XValue="@(t => t.Date.ToString("MMM d"))" YValue="@(t => (decimal?)t.Expenses)" />
        <ApexPointSeries TItem="DailyTrendPoint" Items="_trend" Name="Net" SeriesType="SeriesType.Line"
            XValue="@(t => t.Date.ToString("MMM d"))" YValue="@(t => (decimal?)t.Net)" />
    </ApexChart>
}

@code {
    private DateOnly _from = DateOnly.FromDateTime(DateTime.Now.AddDays(-29));
    private DateOnly _to = DateOnly.FromDateTime(DateTime.Now);
    private DashboardKpiDto? _kpis;
    private SalesByHourResult? _salesByHour;
    private List<DailyTrendPoint>? _trend;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _kpis = await DashboardService.GetKpisAsync(_from, _to);
        _salesByHour = await DashboardService.GetSalesByHourAsync(_to);
        _trend = await DashboardService.GetRevenueExpenseTrendAsync(_from, _to);
    }

    private static string FormatPeso(decimal amount) => string.Format("₱{0:N2}", amount);
}
```

- [ ] **Step 2: Add the nav link**

In `src/KayeDM.Web/Components/Layout/NavMenu.razor`, add an `<AuthorizeView Roles="Owner">`-wrapped link near the Daily Closing link (added in Task 11):

```razor
<AuthorizeView Roles="Owner">
    <div class="nav-item px-3">
        <NavLink class="nav-link" href="dashboard">
            <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Dashboard
        </NavLink>
    </div>
</AuthorizeView>
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.` If `ApexChart`/`ApexPointSeries` fail to resolve or their parameter names don't match the installed package version, check the actual API surface of whatever `Blazor-ApexCharts` version Task 13 resolved (`dotnet nuget locals global-packages --list` then inspect the package, or check its README) and adjust the component usage accordingly — the exact generic constraints and property names can shift between versions.

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add /dashboard page with KPI row, sales-by-hour, and revenue trend charts"
```

---

### Task 19: `/dashboard` page, part 2 — remaining charts + insight callouts

**Files:**
- Modify: `src/KayeDM.Web/Components/Pages/Dashboard.razor`

**Interfaces:**
- Consumes: `IDashboardService.GetExpenseBreakdownAsync`, `GetTopDishesAsync`, `GetWasteByDishAsync`, `GetSalesPerBusCompanyAsync`, `GetPaymentMethodSplitAsync`, `GetInsightsAsync` (Task 16).

- [ ] **Step 1: Add the remaining sections to the markup**

In `src/KayeDM.Web/Components/Pages/Dashboard.razor`, insert after the revenue-trend `@if (_trend is not null) { ... }` block and before `@code {`:

```razor
@if (_insights is { Count: > 0 })
{
    <h2>Insights</h2>
    <ul>
        @foreach (var insight in _insights)
        {
            <li><strong>@insight.Title</strong> — @insight.Detail</li>
        }
    </ul>
}

@if (_expenseBreakdown is not null)
{
    <h2>Expense Breakdown — @_breakdownMonth.ToString("MMMM yyyy")</h2>
    <ApexChart TItem="ExpenseCategoryBreakdownRow" Title="Expense Breakdown">
        <ApexPointSeries TItem="ExpenseCategoryBreakdownRow" Items="_expenseBreakdown" Name="Amount" SeriesType="SeriesType.Bar"
            XValue="@(r => r.CategoryName)" YValue="@(r => (decimal?)r.Amount)" />
    </ApexChart>
}

<div class="mb-2">
    <label>Top dishes window</label>
    <select class="form-control" @bind="_topDishesDays" @bind:after="LoadTopDishesAndWasteAsync">
        <option value="7">Last 7 days</option>
        <option value="30">Last 30 days</option>
    </select>
</div>

@if (_topDishes is not null)
{
    <h2>Top Dishes by Revenue</h2>
    <ApexChart TItem="TopDishRow" Title="Top Dishes">
        <ApexPointSeries TItem="TopDishRow" Items="_topDishes" Name="Revenue" SeriesType="SeriesType.Bar"
            XValue="@(r => r.MenuItemName)" YValue="@(r => (decimal?)r.Revenue)" />
    </ApexChart>
}

@if (_wasteByDish is not null)
{
    <h2>Waste % by Dish</h2>
    <ApexChart TItem="WasteByDishRow" Title="Waste % by Dish">
        <ApexPointSeries TItem="WasteByDishRow" Items="_wasteByDish" Name="Waste %" SeriesType="SeriesType.Bar"
            XValue="@(r => r.MenuItemName)" YValue="@(r => (decimal?)r.WastePercent)" />
    </ApexChart>
}

@if (_busSales is not null)
{
    <h2>Sales per Bus Company</h2>
    <p class="text-muted">"Wave-attributed" sales are a heuristic: unassigned orders completed within ±20 minutes of that company's arrivals — not a confirmed assignment.</p>
    <table class="table">
        <thead>
            <tr><th>Company</th><th>Direct Sales</th><th>Wave-Attributed Sales</th></tr>
        </thead>
        <tbody>
            @foreach (var row in _busSales)
            {
                <tr>
                    <td>@row.CompanyName</td>
                    <td>@FormatPeso(row.DirectSales) (@row.DirectOrderCount orders)</td>
                    <td>@FormatPeso(row.WaveAttributedSales) (@row.WaveAttributedOrderCount orders)</td>
                </tr>
            }
        </tbody>
    </table>
}

@if (_paymentSplit is not null)
{
    <h2>Payment Method Split</h2>
    <ApexChart TItem="PaymentMethodSplitRow" Title="Payment Method Split">
        <ApexPointSeries TItem="PaymentMethodSplitRow" Items="_paymentSplit" Name="Amount" SeriesType="SeriesType.Pie"
            XValue="@(r => r.PaymentMethod)" YValue="@(r => (decimal?)r.Amount)" />
    </ApexChart>
}
```

- [ ] **Step 2: Extend the `@code` block**

In `src/KayeDM.Web/Components/Pages/Dashboard.razor`, replace:

```csharp
    private DateOnly _from = DateOnly.FromDateTime(DateTime.Now.AddDays(-29));
    private DateOnly _to = DateOnly.FromDateTime(DateTime.Now);
    private DashboardKpiDto? _kpis;
    private SalesByHourResult? _salesByHour;
    private List<DailyTrendPoint>? _trend;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _kpis = await DashboardService.GetKpisAsync(_from, _to);
        _salesByHour = await DashboardService.GetSalesByHourAsync(_to);
        _trend = await DashboardService.GetRevenueExpenseTrendAsync(_from, _to);
    }

    private static string FormatPeso(decimal amount) => string.Format("₱{0:N2}", amount);
```

with:

```csharp
    private DateOnly _from = DateOnly.FromDateTime(DateTime.Now.AddDays(-29));
    private DateOnly _to = DateOnly.FromDateTime(DateTime.Now);
    private DateOnly _breakdownMonth = DateOnly.FromDateTime(DateTime.Now);
    private int _topDishesDays = 7;

    private DashboardKpiDto? _kpis;
    private SalesByHourResult? _salesByHour;
    private List<DailyTrendPoint>? _trend;
    private List<InsightCallout>? _insights;
    private List<ExpenseCategoryBreakdownRow>? _expenseBreakdown;
    private List<TopDishRow>? _topDishes;
    private List<WasteByDishRow>? _wasteByDish;
    private List<BusCompanySalesRow>? _busSales;
    private List<PaymentMethodSplitRow>? _paymentSplit;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        await LoadTopDishesAndWasteAsync();
    }

    private async Task LoadAsync()
    {
        _kpis = await DashboardService.GetKpisAsync(_from, _to);
        _salesByHour = await DashboardService.GetSalesByHourAsync(_to);
        _trend = await DashboardService.GetRevenueExpenseTrendAsync(_from, _to);
        _insights = await DashboardService.GetInsightsAsync(_from, _to);
        _expenseBreakdown = await DashboardService.GetExpenseBreakdownAsync(_breakdownMonth.Year, _breakdownMonth.Month);
        _busSales = await DashboardService.GetSalesPerBusCompanyAsync(_from, _to);
        _paymentSplit = await DashboardService.GetPaymentMethodSplitAsync(_from, _to);
    }

    private async Task LoadTopDishesAndWasteAsync()
    {
        _topDishes = await DashboardService.GetTopDishesAsync(_topDishesDays);
        _wasteByDish = await DashboardService.GetWasteByDishAsync(_topDishesDays);
    }

    private static string FormatPeso(decimal amount) => string.Format("₱{0:N2}", amount);
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(web): add remaining dashboard charts and insight callouts"
```

---

## PART D — Seed Data Generator

**Reproducibility is the whole point of this part** (per blueprint §7 and explicit user instruction): every random draw across every step must come from **one single `Random` instance constructed with a fixed seed**, stored as a field and reused — never `new Random()` or `Random.Shared` anywhere else in this class. Tasks 21–24 all add methods to the same class and must call `_random`, not create their own. Checkpoint 3 (Task 26) proves this by running the seeder twice and diffing aggregate output.

### Task 20: `SeedDataGenerator` skeleton + `--seed` CLI wiring + wipe logic

**Files:**
- Create: `src/KayeDM.Infrastructure/Seeding/SeedDataGenerator.cs`
- Modify: `src/KayeDM.Web/Program.cs`

**Interfaces:**
- Consumes: `AppDbContext` (direct access, seeding is infrastructure-internal, not exposed as an `IXxxService`).
- Produces: `KayeDM.Infrastructure.Seeding.SeedDataGenerator.RunAsync(AppDbContext db)` — orchestrates Tasks 21–24's methods in a fixed order; consumed by `Program.cs`'s `--seed` handling.

- [ ] **Step 1: Write the skeleton with the fixed seed and wipe logic**

`src/KayeDM.Infrastructure/Seeding/SeedDataGenerator.cs`:

```csharp
using KayeDM.Domain.Entities;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Seeding;

// Fixed seed (42) -- every random draw in this class must go through _random,
// never `new Random()` or `Random.Shared`, or the demo data stops being
// reproducible run-to-run. See blueprint §7.
public class SeedDataGenerator
{
    private readonly Random _random = new(42);
    private readonly DateTime _windowEnd = DateTime.Now.Date;
    private readonly DateTime _windowStart;
    private readonly HashSet<DateTime> _negativeDays;

    public SeedDataGenerator()
    {
        _windowStart = _windowEnd.AddDays(-29);

        // 2-3 designated net-negative days, picked up front so the choice
        // itself is deterministic (drawn from _random in a fixed position in
        // the sequence, before any per-day generation starts).
        var negativeDayCount = _random.Next(2, 4);
        _negativeDays = new HashSet<DateTime>();
        while (_negativeDays.Count < negativeDayCount)
        {
            var offset = _random.Next(0, 30);
            _negativeDays.Add(_windowStart.AddDays(offset));
        }
    }

    public async Task RunAsync(AppDbContext db)
    {
        await WipeAsync(db);

        var menuItems = await SeedMenuItemsAsync(db);
        var busCompanies = await SeedBusCompaniesAsync(db);

        for (var day = _windowStart; day <= _windowEnd; day = day.AddDays(1))
        {
            var trips = await SeedBusTripsForDayAsync(db, day, busCompanies);
            var batches = await SeedDishBatchesForDayAsync(db, day, menuItems);
            await SeedOrdersForDayAsync(db, day, menuItems, trips);
            await SeedWasteForDayAsync(db, batches);
            await SeedExpensesForDayAsync(db, day);

            if (day < _windowEnd)
            {
                await SeedClosingForDayAsync(db, day);
            }
        }
    }

    private static async Task WipeAsync(AppDbContext db)
    {
        // Order matters -- children before parents, respecting FK constraints.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM DailyClosings");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM WasteLogs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM CrewMealCredits");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM OrderLines");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Orders");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM DishBatches");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM BusTrips");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM BusCompanies");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Expenses");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM MenuItems");
    }
}
```

- [ ] **Step 2: Wire the `--seed` CLI flag**

In `src/KayeDM.Web/Program.cs`, add the using:

```csharp
using KayeDM.Infrastructure.Seeding;
```

Change the final lines of the file from:

```csharp
app.MapPost("/account/logout", async (SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.LocalRedirect("/account/login");
});

app.Run();
```

to:

```csharp
app.MapPost("/account/logout", async (SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.LocalRedirect("/account/login");
});

if (args.Contains("--seed"))
{
    using var seedScope = app.Services.CreateScope();
    var dbContextFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var seedDb = await dbContextFactory.CreateDbContextAsync();
    await new SeedDataGenerator().RunAsync(seedDb);
    return;
}

app.Run();
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: build FAILS — `SeedMenuItemsAsync`, `SeedBusCompaniesAsync`, `SeedBusTripsForDayAsync`, `SeedDishBatchesForDayAsync`, `SeedOrdersForDayAsync`, `SeedWasteForDayAsync`, `SeedExpensesForDayAsync`, `SeedClosingForDayAsync` don't exist yet. This is expected — Tasks 21–24 add them. Confirm the errors are only about these missing methods, nothing else.

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): add SeedDataGenerator skeleton with fixed-seed Random and --seed CLI wiring"
```

(This commit intentionally leaves the build broken — Task 21 fixes it. Matches the TDD-style sequencing already used elsewhere in this project, e.g. Week 2 Task 5.)

---

### Task 21: Reference data — menu items + bus companies

**Files:**
- Modify: `src/KayeDM.Infrastructure/Seeding/SeedDataGenerator.cs`

**Interfaces:**
- Produces: `SeedMenuItemsAsync`, `SeedBusCompaniesAsync` — consumed by `RunAsync` (Task 20) and Tasks 22–23.

Menu items and bus companies are fixed reference lists (no `_random` draws needed — there's nothing to randomize about "which 25 dishes exist," only about how much of each sells later).

- [ ] **Step 1: Add the two methods**

In `src/KayeDM.Infrastructure/Seeding/SeedDataGenerator.cs`, add the using:

```csharp
using KayeDM.Domain.Enums;
```

Add these methods to the class, after `RunAsync`:

```csharp
    private static async Task<List<MenuItem>> SeedMenuItemsAsync(AppDbContext db)
    {
        var items = new List<MenuItem>
        {
            new() { Name = "Chicken Adobo", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 1 },
            new() { Name = "Pork Sinigang", Category = MenuCategory.Ulam, Price = 95m, IsActive = true, SortOrder = 2 },
            new() { Name = "Beef Caldereta", Category = MenuCategory.Ulam, Price = 110m, IsActive = true, SortOrder = 3 },
            new() { Name = "Kare-Kare", Category = MenuCategory.Ulam, Price = 120m, IsActive = true, SortOrder = 4 },
            new() { Name = "Pork Menudo", Category = MenuCategory.Ulam, Price = 85m, IsActive = true, SortOrder = 5 },
            new() { Name = "Dinuguan", Category = MenuCategory.Ulam, Price = 80m, IsActive = true, SortOrder = 6 },
            new() { Name = "Fried Bangus", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 7 },
            new() { Name = "Fried Chicken", Category = MenuCategory.Ulam, Price = 85m, IsActive = true, SortOrder = 8 },
            new() { Name = "Pork Sisig", Category = MenuCategory.Ulam, Price = 100m, IsActive = true, SortOrder = 9 },
            new() { Name = "Beef Tapa", Category = MenuCategory.Ulam, Price = 95m, IsActive = true, SortOrder = 10 },
            new() { Name = "Plain Rice", Category = MenuCategory.Rice, Price = 15m, IsActive = true, SortOrder = 11 },
            new() { Name = "Garlic Rice", Category = MenuCategory.Rice, Price = 20m, IsActive = true, SortOrder = 12 },
            new() { Name = "Java Rice", Category = MenuCategory.Rice, Price = 25m, IsActive = true, SortOrder = 13 },
            new() { Name = "Iced Tea", Category = MenuCategory.Drinks, Price = 20m, IsActive = true, SortOrder = 14 },
            new() { Name = "Softdrinks", Category = MenuCategory.Drinks, Price = 25m, IsActive = true, SortOrder = 15 },
            new() { Name = "Bottled Water", Category = MenuCategory.Drinks, Price = 20m, IsActive = true, SortOrder = 16 },
            new() { Name = "Buko Juice", Category = MenuCategory.Drinks, Price = 30m, IsActive = true, SortOrder = 17 },
            new() { Name = "Lumpiang Shanghai", Category = MenuCategory.Snacks, Price = 60m, IsActive = true, SortOrder = 18 },
            new() { Name = "Fish Balls", Category = MenuCategory.Snacks, Price = 30m, IsActive = true, SortOrder = 19 },
            new() { Name = "Kwek-Kwek", Category = MenuCategory.Snacks, Price = 35m, IsActive = true, SortOrder = 20 },
            new() { Name = "Banana Cue", Category = MenuCategory.Snacks, Price = 25m, IsActive = true, SortOrder = 21 },
            new() { Name = "Leche Flan", Category = MenuCategory.Dessert, Price = 40m, IsActive = true, SortOrder = 22 },
            new() { Name = "Halo-Halo", Category = MenuCategory.Dessert, Price = 65m, IsActive = true, SortOrder = 23 },
            new() { Name = "Buko Pandan", Category = MenuCategory.Dessert, Price = 45m, IsActive = true, SortOrder = 24 },
            new() { Name = "Turon", Category = MenuCategory.Dessert, Price = 30m, IsActive = true, SortOrder = 25 }
        };

        db.MenuItems.AddRange(items);
        await db.SaveChangesAsync();
        return items;
    }

    private static async Task<List<BusCompany>> SeedBusCompaniesAsync(AppDbContext db)
    {
        var companies = new List<BusCompany>
        {
            new() { Name = "DLTB", ContactPerson = "Juan Dela Cruz", CrewMealAllowancePerTrip = 3, IsActive = true },
            new() { Name = "Isarog", ContactPerson = "Maria Santos", CrewMealAllowancePerTrip = 2, IsActive = true },
            new() { Name = "Peñafrancia Tours", ContactPerson = "Pedro Reyes", CrewMealAllowancePerTrip = 4, IsActive = true },
            new() { Name = "Raymond Transport", ContactPerson = "Ana Villanueva", CrewMealAllowancePerTrip = 3, IsActive = true }
        };

        db.BusCompanies.AddRange(companies);
        await db.SaveChangesAsync();
        return companies;
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: build still FAILS (the six per-day methods still don't exist) — confirm the error list has shrunk to only those six.

- [ ] **Step 3: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): seed fixed reference data (25 menu items, 4 bus companies) in SeedDataGenerator"
```

---

### Task 22: Bus trips + orders for a day (wave clustering, crew meals, voids)

**Files:**
- Modify: `src/KayeDM.Infrastructure/Seeding/SeedDataGenerator.cs`

**Interfaces:**
- Produces: `SeedBusTripsForDayAsync`, `SeedOrdersForDayAsync` — consumed by `RunAsync` (Task 20).

Every random decision below (`_random.Next`, `.NextDouble`) uses the single shared field — no `new Random()` calls.

- [ ] **Step 1: Add a per-day order-number counter field**

In `src/KayeDM.Infrastructure/Seeding/SeedDataGenerator.cs`, add a field next to `_random`:

```csharp
    private static readonly (TimeSpan Start, TimeSpan End)[] Waves =
    {
        (new TimeSpan(9, 30, 0), new TimeSpan(10, 30, 0)),
        (new TimeSpan(13, 30, 0), new TimeSpan(14, 30, 0)),
        (new TimeSpan(18, 0, 0), new TimeSpan(19, 30, 0))
    };
```

- [ ] **Step 2: Add `SeedBusTripsForDayAsync`**

```csharp
    private async Task<List<BusTrip>> SeedBusTripsForDayAsync(AppDbContext db, DateTime day, List<BusCompany> companies)
    {
        var trips = new List<BusTrip>();

        foreach (var (start, end) in Waves)
        {
            var arrivalsThisWave = _random.Next(1, 4); // 1-3 buses per wave
            for (var i = 0; i < arrivalsThisWave; i++)
            {
                var company = companies[_random.Next(companies.Count)];
                var windowMinutes = (int)(end - start).TotalMinutes;
                var arrivedAt = day.Date + start + TimeSpan.FromMinutes(_random.Next(0, windowMinutes));

                var trip = new BusTrip
                {
                    BusCompanyId = company.Id,
                    BusNumber = (8000 + _random.Next(0, 999)).ToString(),
                    Route = "Manila-Sorsogon",
                    ArrivedAt = arrivedAt,
                    DepartedAt = arrivedAt.AddMinutes(20 + _random.Next(0, 15))
                };
                trips.Add(trip);
            }
        }

        db.BusTrips.AddRange(trips);
        await db.SaveChangesAsync();
        return trips;
    }
```

- [ ] **Step 3: Add `SeedOrdersForDayAsync`**

```csharp
    private async Task SeedOrdersForDayAsync(AppDbContext db, DateTime day, List<MenuItem> menuItems, List<BusTrip> trips)
    {
        var orderCount = _random.Next(60, 141);
        var sequence = 0;
        var createdOrders = new List<Order>();

        for (var i = 0; i < orderCount; i++)
        {
            DateTime timestamp;
            if (_random.NextDouble() < 0.8 && Waves.Length > 0)
            {
                var (start, end) = Waves[_random.Next(Waves.Length)];
                var center = day.Date + start + TimeSpan.FromTicks((end - start).Ticks / 2);
                var jitterMinutes = _random.Next(-25, 26);
                timestamp = center.AddMinutes(jitterMinutes);
            }
            else
            {
                var minute = _random.Next(8 * 60, 20 * 60); // 8am-8pm trickle
                timestamp = day.Date + TimeSpan.FromMinutes(minute);
            }

            sequence++;
            var orderNumber = $"{day:yyyyMMdd}-{sequence:D3}";
            var isCash = _random.NextDouble() < 0.85;
            var lineCount = _random.Next(1, 5);

            var order = new Order
            {
                OrderNumber = orderNumber,
                CreatedAt = timestamp,
                Status = OrderStatus.Completed,
                PaymentMethod = isCash ? PaymentMethod.Cash : PaymentMethod.GCash
            };

            decimal total = 0m;
            for (var l = 0; l < lineCount; l++)
            {
                var item = menuItems[_random.Next(menuItems.Count)];
                var quantity = _random.Next(1, 4);
                total += item.Price * quantity;
                order.Lines.Add(new OrderLine { MenuItemId = item.Id, Quantity = quantity, UnitPriceAtSale = item.Price });
            }

            var nearbyTrip = trips.FirstOrDefault(t => Math.Abs((timestamp - t.ArrivedAt).TotalMinutes) <= 20);
            if (nearbyTrip is not null && _random.NextDouble() < 0.5)
            {
                order.BusTripId = nearbyTrip.Id;
            }

            if (isCash)
            {
                var overpay = new[] { 0m, 10m, 20m, 50m }[_random.Next(4)];
                order.AmountTendered = total + overpay;
                order.ChangeGiven = overpay;
            }
            else
            {
                order.AmountTendered = total;
                order.ChangeGiven = 0m;
            }

            createdOrders.Add(order);
        }

        db.Orders.AddRange(createdOrders);
        await db.SaveChangesAsync();

        // Occasional voids -- ~4% of the day's completed orders.
        var voidCandidates = createdOrders.Where(o => _random.NextDouble() < 0.04).ToList();
        foreach (var order in voidCandidates)
        {
            order.Status = OrderStatus.Voided;
            order.VoidReason = "Customer changed mind";
        }
        if (voidCandidates.Count > 0)
        {
            await db.SaveChangesAsync();
        }

        // Crew meals on ~90% of trips, up to each trip's company allowance.
        foreach (var trip in trips)
        {
            if (_random.NextDouble() >= 0.9)
            {
                continue;
            }

            var company = await db.BusCompanies.FirstAsync(c => c.Id == trip.BusCompanyId);
            var mealsToGive = _random.Next(1, company.CrewMealAllowancePerTrip + 1);
            var roles = Enum.GetValues<CrewRole>();

            for (var m = 0; m < mealsToGive; m++)
            {
                sequence++;
                var item = menuItems[_random.Next(menuItems.Count)];
                var crewOrder = new Order
                {
                    OrderNumber = $"{day:yyyyMMdd}-{sequence:D3}",
                    CreatedAt = trip.ArrivedAt.AddMinutes(_random.Next(5, 20)),
                    Status = OrderStatus.Completed,
                    PaymentMethod = PaymentMethod.Cash,
                    BusTripId = trip.Id,
                    IsCrewMeal = true,
                    AmountTendered = 0m,
                    ChangeGiven = 0m,
                    Lines = { new OrderLine { MenuItemId = item.Id, Quantity = 1, UnitPriceAtSale = 0m } }
                };

                db.Orders.Add(crewOrder);
                db.CrewMealCredits.Add(new CrewMealCredit
                {
                    BusTripId = trip.Id,
                    CrewRole = roles[m % roles.Length],
                    Order = crewOrder,
                    LoggedAt = crewOrder.CreatedAt
                });
            }
        }

        await db.SaveChangesAsync();
    }
```

- [ ] **Step 4: Add the missing usings**

At the top of `src/KayeDM.Infrastructure/Seeding/SeedDataGenerator.cs`, ensure these are present:

```csharp
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
```

- [ ] **Step 5: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: build still FAILS (`SeedDishBatchesForDayAsync`, `SeedWasteForDayAsync`, `SeedExpensesForDayAsync`, `SeedClosingForDayAsync` remain). Confirm the error list has shrunk to only those four.

- [ ] **Step 6: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): seed wave-clustered bus trips, orders, voids, and crew meals per day"
```

---

### Task 23: Dish batches + waste logs for a day

**Files:**
- Modify: `src/KayeDM.Infrastructure/Seeding/SeedDataGenerator.cs`

**Interfaces:**
- Produces: `SeedDishBatchesForDayAsync`, `SeedWasteForDayAsync` — consumed by `RunAsync` (Task 20). `SeedDishBatchesForDayAsync`'s return value feeds `SeedWasteForDayAsync`.

- [ ] **Step 1: Add `SeedDishBatchesForDayAsync`**

```csharp
    private async Task<List<DishBatch>> SeedDishBatchesForDayAsync(AppDbContext db, DateTime day, List<MenuItem> menuItems)
    {
        var batches = new List<DishBatch>();

        foreach (var item in menuItems)
        {
            var trays = 2m + (decimal)_random.Next(0, 5) * 0.5m; // 2.0-4.0 trays, half-tray steps
            var batch = new DishBatch
            {
                MenuItemId = item.Id,
                Date = day.Date,
                TraysProduced = trays,
                ServingsPerTray = 8 + _random.Next(0, 5), // 8-12 servings/tray
                ProducedAt = day.Date.AddHours(6).AddMinutes(_random.Next(0, 60))
            };
            batches.Add(batch);
        }

        db.DishBatches.AddRange(batches);
        await db.SaveChangesAsync();
        return batches;
    }
```

- [ ] **Step 2: Add `SeedWasteForDayAsync`**

```csharp
    private async Task SeedWasteForDayAsync(AppDbContext db, List<DishBatch> batches)
    {
        var reasons = Enum.GetValues<WasteReason>();

        foreach (var batch in batches)
        {
            var wastePercent = 3m + (decimal)_random.Next(0, 10); // 3-12%
            var traysWasted = Math.Round(batch.TraysProduced * wastePercent / 100m * 2m, MidpointRounding.AwayFromZero) / 2m; // nearest 0.5 tray

            if (traysWasted <= 0m)
            {
                continue;
            }

            db.WasteLogs.Add(new WasteLog
            {
                DishBatchId = batch.Id,
                TraysWasted = traysWasted,
                Reason = reasons[_random.Next(reasons.Length)],
                LoggedAt = batch.Date.AddHours(20),
                LoggedById = "seed"
            });
        }

        await db.SaveChangesAsync();
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: build still FAILS (`SeedExpensesForDayAsync`, `SeedClosingForDayAsync` remain). Confirm the error list has shrunk to only those two.

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): seed dish batches and 3-12% waste per day"
```

---

### Task 24: Expenses (65–75% of revenue, 2–3 negative days) + `DailyClosing` per day

**Files:**
- Modify: `src/KayeDM.Infrastructure/Seeding/SeedDataGenerator.cs`

**Interfaces:**
- Consumes: `_negativeDays` (Task 20).
- Produces: `SeedExpensesForDayAsync`, `SeedClosingForDayAsync` — consumed by `RunAsync` (Task 20). This is the last pair of stub methods — after this task the seeder builds cleanly end-to-end.

- [ ] **Step 1: Add `SeedExpensesForDayAsync`**

```csharp
    private async Task SeedExpensesForDayAsync(AppDbContext db, DateTime day)
    {
        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);

        var revenue = await db.Orders
            .Where(o => o.CreatedAt >= dayStart && o.CreatedAt < dayEnd && o.Status == OrderStatus.Completed)
            .SumAsync(o => (decimal?)(o.AmountTendered - o.ChangeGiven)) ?? 0m;

        var categories = await db.ExpenseCategories.ToDictionaryAsync(c => c.Type);
        var expenses = new List<Expense>();
        decimal fixedCosts = 0m;

        if (day.DayOfWeek == DayOfWeek.Monday)
        {
            var wages = 8000m + (decimal)_random.Next(0, 2000);
            fixedCosts += wages;
            expenses.Add(NewExpense(categories, ExpenseCategoryType.Wages, "Weekly staff wages", wages, day));
        }

        if (day.Day == 1)
        {
            var rent = 15000m + (decimal)_random.Next(0, 3000);
            fixedCosts += rent;
            expenses.Add(NewExpense(categories, ExpenseCategoryType.Rent, "Monthly stall rent", rent, day));
        }

        if (day.Day == 1 || day.Day == 15)
        {
            var utilities = 2000m + (decimal)_random.Next(0, 1500);
            fixedCosts += utilities;
            expenses.Add(NewExpense(categories, ExpenseCategoryType.Utilities, "Electricity and water", utilities, day));
        }

        if (_random.NextDouble() < 0.15)
        {
            var extra = 500m + (decimal)_random.Next(0, 1500);
            fixedCosts += extra;
            var type = _random.NextDouble() < 0.5 ? ExpenseCategoryType.Supplies : ExpenseCategoryType.Maintenance;
            expenses.Add(NewExpense(categories, type, type == ExpenseCategoryType.Supplies ? "Disposables restock" : "Equipment repair", extra, day));
        }

        var isNegativeDay = _negativeDays.Contains(day.Date);
        var targetRatio = isNegativeDay
            ? 1.1m + (decimal)_random.Next(0, 20) / 100m  // 110%-130% of revenue
            : 0.65m + (decimal)_random.Next(0, 11) / 100m; // 65%-75% of revenue

        var targetTotal = revenue * targetRatio;
        var marketRun = Math.Max(3000m, targetTotal - fixedCosts);
        marketRun = Math.Min(marketRun, 6000m + fixedCosts); // keep the daily ingredients line within a plausible range even on negative days
        expenses.Add(NewExpense(categories, ExpenseCategoryType.Ingredients, "Daily market run", marketRun, day));

        db.Expenses.AddRange(expenses);
        await db.SaveChangesAsync();
    }

    private static Expense NewExpense(Dictionary<ExpenseCategoryType, ExpenseCategory> categories, ExpenseCategoryType type, string description, decimal amount, DateTime day)
    {
        return new Expense
        {
            Date = day.Date,
            ExpenseCategoryId = categories[type].Id,
            Description = description,
            Amount = Math.Round(amount, 2),
            PaymentMethod = ExpensePaymentMethod.Cash,
            LoggedById = "seed",
            LoggedAt = day.Date.AddHours(20)
        };
    }
```

- [ ] **Step 2: Add `SeedClosingForDayAsync`**

```csharp
    private static async Task SeedClosingForDayAsync(AppDbContext db, DateTime day)
    {
        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);

        // Narrow projection — only the columns actually used below, never
        // whole Order entities.
        var completedOrders = await db.Orders
            .Where(o => o.CreatedAt >= dayStart && o.CreatedAt < dayEnd && o.Status == OrderStatus.Completed)
            .Select(o => new { o.AmountTendered, o.ChangeGiven, o.PaymentMethod, o.IsCrewMeal })
            .ToListAsync();
        var voidedCount = await db.Orders
            .CountAsync(o => o.CreatedAt >= dayStart && o.CreatedAt < dayEnd && o.Status == OrderStatus.Voided);

        // Materialize the narrow projection, then sum client-side —
        // consistent with ClosingService and DashboardService. This is
        // forced by a SQLite test-provider limitation (it can't translate
        // SUM over decimal into SQL) — not a stylistic choice. This method
        // only ever runs against SQL Server, but keeping the pattern
        // consistent avoids surprises if it's ever unit tested.
        var dayExpenseAmounts = await db.Expenses
            .Where(e => e.Date >= dayStart && e.Date < dayEnd)
            .Select(e => e.Amount)
            .ToListAsync();
        var totalExpenses = dayExpenseAmounts.Sum();

        var totalSales = completedOrders.Sum(o => o.AmountTendered - o.ChangeGiven);
        var cashSales = completedOrders.Where(o => o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.AmountTendered - o.ChangeGiven);
        var gcashSales = completedOrders.Where(o => o.PaymentMethod == PaymentMethod.GCash).Sum(o => o.AmountTendered - o.ChangeGiven);

        db.DailyClosings.Add(new DailyClosing
        {
            Date = dayStart,
            TotalSales = totalSales,
            CashSales = cashSales,
            GCashSales = gcashSales,
            OrderCount = completedOrders.Count,
            VoidedCount = voidedCount,
            CrewMealsGiven = completedOrders.Count(o => o.IsCrewMeal),
            TotalExpenses = totalExpenses,
            NetForDay = totalSales - totalExpenses,
            ClosedById = "seed",
            ClosedAt = dayStart.AddHours(21)
        });

        await db.SaveChangesAsync();
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.` — this is the first time the seeder builds cleanly end-to-end.

- [ ] **Step 4: Commit**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms"
git add -A
git commit -m "feat(infra): seed expenses (65-75% of revenue, 2-3 negative days) and daily closings"
```

---

### Task 25: First real run + sanity checks

**Files:** none (this task runs the seeder, doesn't change code — unless a sanity check fails and a fix is needed, in which case fix and re-run before committing).

- [ ] **Step 1: Run the seeder**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet run --project src/KayeDM.Web -- --seed
```

Expected: runs to completion with no exceptions.

- [ ] **Step 2: Sanity-check row counts and ranges**

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT 'MenuItems', COUNT(*) FROM MenuItems UNION ALL SELECT 'BusCompanies', COUNT(*) FROM BusCompanies UNION ALL SELECT 'Orders', COUNT(*) FROM Orders UNION ALL SELECT 'DailyClosings', COUNT(*) FROM DailyClosings UNION ALL SELECT 'Expenses', COUNT(*) FROM Expenses;"
```

Expected: `MenuItems = 25`, `BusCompanies = 4`, `Orders` roughly in the 1800-4200 range (30 days × 60-140/day, plus crew meal orders), `DailyClosings = 29` (every day except the last/current one), `Expenses` > 0.

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT MIN(CreatedAt), MAX(CreatedAt) FROM Orders;"
```

Expected: spans the full 30-day window ending today.

- [ ] **Step 3: Spot-check the expense ratio**

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT SUM(TotalExpenses) * 1.0 / NULLIF(SUM(TotalSales), 0) FROM DailyClosings;"
```

Expected: roughly in the 0.65-0.85 range overall (individual days vary 65-75%, plus 2-3 negative days pull the average up somewhat — if this is wildly outside a plausible range, investigate before proceeding).

- [ ] **Step 4: If anything looks wrong, fix and re-run before committing**

Any fix here changes `SeedDataGenerator.cs` — re-run Step 1 after any change (the wipe-first logic means re-running is always safe) and re-check Steps 2–3.

---

### Task 26: CHECKPOINT 3 — reproducibility verification (STOP for user review)

**Files:** none (verification only).

This is the check the user specifically asked for: **run the seeder twice and diff aggregate numbers.** Identical output proves the fixed seed is actually threaded through every random call; any difference means some code path is drawing from an unseeded source.

- [ ] **Step 1: Capture aggregates from the run already done in Task 25**

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT COUNT(*) AS TotalOrders, SUM(AmountTendered - ChangeGiven) AS TotalSales FROM Orders WHERE Status = 0;" > run1.txt
cat run1.txt
```

- [ ] **Step 2: Run the seeder a second time**

```bash
dotnet run --project src/KayeDM.Web -- --seed
```

- [ ] **Step 3: Capture the same aggregates again**

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d KayeDmBms -Q "SELECT COUNT(*) AS TotalOrders, SUM(AmountTendered - ChangeGiven) AS TotalSales FROM Orders WHERE Status = 0;" > run2.txt
diff run1.txt run2.txt
```

Expected: `diff` produces no output — `TotalOrders` and `TotalSales` are byte-identical between the two runs. If they differ, this is a real bug: find whichever seeding method still uses `new Random()`, `Random.Shared`, `Guid.NewGuid()` for anything order-relevant, or a wall-clock read that isn't `_windowEnd`/`DateTime.Now.Date` computed once — every such call must route through `_random` or a value fixed at construction time. Do not proceed until two consecutive runs produce identical aggregates.

- [ ] **Step 4: Clean up the scratch files**

```bash
rm run1.txt run2.txt
```

- [ ] **Step 5: STOP — present the diff result to the user**

Report the exact `TotalOrders`/`TotalSales` numbers from both runs and confirm they matched (or explain what was found and fixed, if they didn't on the first attempt). **Do not proceed to final verification until the user has reviewed this and confirmed the seeder is reproducible.**

---

### Task 27: Final verification

**Files:** none (verification only, commit only if a bug fix is needed).

- [ ] **Step 1: Full build**

Run: `dotnet build "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\KayeDM.BMS.slnx"`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS\tests\KayeDM.Tests\KayeDM.Tests.csproj"`
Expected: `Passed! - Failed: 0`. Total should be 30 (Weeks 1–3) + 4 (`ClosingGuardTests`) + 4 (`ClosingLockTests`) + 4 (`ClosingServiceTests`) + 4 (`DashboardServiceTests` part 1) + 5 (`DashboardServiceTests` part 2) = 51.

- [ ] **Step 3: Confirm migration history**

```bash
cd "C:\Users\monst\source\repos\kaye-dm-bms\KayeDM.BMS"
dotnet ef migrations list --project src/KayeDM.Infrastructure/KayeDM.Infrastructure.csproj --startup-project src/KayeDM.Web/KayeDM.Web.csproj
```

Expected: exactly the 9 Week 1–3 migrations plus `AddDailyClosing` at the end — 10 total, all `(Applied)`. No `AddIdentitySchema`, no `WireLoggedByToIdentity` (both confirmed unneeded).

- [ ] **Step 4: Manual smoke of every new/changed route**

Run the app and confirm, for both roles: `/account/login` (anonymous), `/dashboard` and `/closing` (Owner-only, blocked for Cashier), `/pos` (both roles). If a Claude-in-Chrome or Playwright browser tool is connected, walk the full demo path: log in as Owner → complete an order → log an expense → view `/dashboard` (all charts render, no console errors) → `/closing` shows correct figures → confirm closing → verify the lock. If no browser tool is connected, state that explicitly rather than claiming the walkthrough was done.

- [ ] **Step 5: Record the outcome**

Append a line to `.superpowers/sdd/progress.md` summarizing the result (pass/fail counts, chart library version pinned at Task 13, any deviations), matching the style of the existing Week 1–3 entries. Note explicitly that this file is untracked in git (per the Week 3 precedent) — if working in an isolated worktree, this note must be written to the main checkout's copy, not the worktree's (which won't have the file at all).

