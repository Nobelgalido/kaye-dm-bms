# Week 4.5 App Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the Blazor Server app shell — role-based post-login landing, a grouped/collapsible sidebar, and a consistent visual system (tokens, fonts, PageHeader, route-line signature, login/access-denied pages) — per `docs/prompts/kaye-dm-week4-5-app-shell-prompt.md`, styled per `docs/prompts/kaye-dm-design-system-spec.md`.

**Architecture:** All changes live in `KayeDM.BMS/src/KayeDM.Web`. Two pure-logic helpers (role→home-route, active-route matching) go in `KayeDM.Application/Navigation` so they're unit-testable with the existing xUnit project without adding a Blazor test framework. Everything else is Razor components + scoped CSS. No schema changes, no migrations, no new NuGet packages — this is a hard constraint from both source docs.

**Tech Stack:** ASP.NET Core 8, Blazor Server (InteractiveServer render mode), ASP.NET Core Identity (cookie auth, `Owner`/`Cashier` roles already seeded), xUnit + FluentAssertions.

## Global Constraints

- No EF Core migrations, no schema changes, no new NuGet packages (project references between existing projects are fine; this plan doesn't add any).
- Pure Blazor + CSS for all interactivity — no JS interop, no Bootstrap accordion JS, no client-side JS libraries.
- Never edit existing migrations; never touch `SeedDataGenerator` or `IdentitySeeder` logic.
- Scope is the shell only: sidebar, login, access-denied, PageHeader rollout, and the design tokens those need. Do **not** redesign POS, Dashboard, or any CRUD/report page body content — that is explicitly out of scope per the shell prompt ("the full UI consistency pass is Week 5's job").
- Colors/type/space/motion tokens must come from `docs/prompts/kaye-dm-design-system-spec.md` §1 verbatim — no invented values.
- Every interactive element needs a visible focus ring, hit target ≥ 40px, and `prefers-reduced-motion` must be respected.
- Commit in logical steps on branch `week-4-5-app-shell`. Do not touch `main`. Merge happens via PR after user review — no squash.
- Logo assets already exist at `KayeDM.BMS/src/KayeDM.Web/wwwroot/favicon.ico` and `wwwroot/img/kaye-dm-logo-3d-{64,192,512}.png` / `kaye-dm-logo.png` — use these exact paths, do not regenerate them.

---

## Known deviations from the source docs (confirm these read correctly, don't silently "fix" them mid-plan)

1. **POS gets no PageHeader.** `Pos.razor` currently has no `<h1>` at all. The shell prompt says "Consistent page header component... used by all pages" but also "Do not restyle page content beyond the shared header component" and POS is explicitly reserved for Week 5's full treatment. Inserting a PageHeader would eat vertical space from the payment strip on a 1366×768 canteen laptop. This plan leaves POS's markup untouched except its `<PageTitle>` suffix.
2. **`AccessDenied` hardcodes "Back to POS".** In the current role matrix, `Owner` can reach every page and `Cashier` is only ever blocked from Owner-only pages — there is no route an authenticated user hits NotAuthorized on except "Cashier tried an Owner page." So a static "Back to POS" (matching the design spec's literal copy) is correct; a dynamic role-aware label would be speculative generality for a case that can't occur today.
3. **Footer user chip shows the account email**, not a display name — the blueprint's optional `AppUser.DisplayName` field doesn't exist (no schema changes allowed in this task), and seeded usernames are the email addresses.
4. **Dashboard section dividers and the sales-by-hour bus-arrival chart markers (route-line usages #2 and #3 in the design spec) are NOT built in this task** — they live on the Dashboard page, which is out of scope. Only usages #1 (active nav marker) and #4 (login page) are implemented.

---

### Task 1: Branch setup and docs housekeeping

**Files:**
- Modify: none (git operations only)
- Move: `docs/kaye-dm-agent-prompts-weeks-1-5.md` (currently shown as deleted in git status) — its replacement already exists untracked at `docs/prompts/kaye-dm-agent-prompts-weeks-1-5.md`
- Stage: `docs/prompts/` (untracked), `docs/superpowers/plans/2026-07-05-week4-auth-closing-dashboard-seeder.md` (untracked)

**Interfaces:** N/A (no code)

- [ ] **Step 1: Confirm working tree state**

Run: `git status`
Expected: deleted `docs/kaye-dm-agent-prompts-weeks-1-5.md`, untracked `docs/prompts/`, untracked `docs/superpowers/plans/2026-07-05-week4-auth-closing-dashboard-seeder.md`. No other changes.

- [ ] **Step 2: Create the branch**

Run: `git checkout -b week-4-5-app-shell`
Expected: `Switched to a new branch 'week-4-5-app-shell'`. Uncommitted changes from Step 1 carry over automatically.

- [ ] **Step 3: Commit the docs housekeeping**

```bash
git add docs/kaye-dm-agent-prompts-weeks-1-5.md docs/prompts/ docs/superpowers/plans/2026-07-05-week4-auth-closing-dashboard-seeder.md
git commit -m "docs: reorganize prompt files into docs/prompts/, add Week 4 plan doc"
```

Expected: commit succeeds, `git status` on the new branch is clean.

---

### Task 2: Role-home and nav-route-matcher helpers (TDD)

**Files:**
- Create: `KayeDM.BMS/src/KayeDM.Application/Navigation/RoleHome.cs`
- Create: `KayeDM.BMS/src/KayeDM.Application/Navigation/NavRouteMatcher.cs`
- Test: `KayeDM.BMS/tests/KayeDM.Tests/Navigation/RoleHomeTests.cs`
- Test: `KayeDM.BMS/tests/KayeDM.Tests/Navigation/NavRouteMatcherTests.cs`

**Interfaces:**
- Produces: `KayeDM.Application.Navigation.RoleHome.Owner` (`const string` = `"/dashboard"`), `RoleHome.Cashier` (`const string` = `"/pos"`), `RoleHome.Resolve(IEnumerable<string> roles) : string`. `KayeDM.Application.Navigation.NavRouteMatcher.IsActive(string currentPath, IEnumerable<string> routePrefixes) : bool`. Both are consumed by Task 6 (`NavGroup`), Task 7 (`Sidebar`), and Task 8 (`Home`, `AccessDenied`).

- [ ] **Step 1: Write the failing tests**

Create `KayeDM.BMS/tests/KayeDM.Tests/Navigation/RoleHomeTests.cs`:

```csharp
using FluentAssertions;
using KayeDM.Application.Navigation;

namespace KayeDM.Tests.Navigation;

public class RoleHomeTests
{
    [Fact]
    public void Resolve_OwnerRole_ReturnsDashboard()
    {
        RoleHome.Resolve(new[] { "Owner" }).Should().Be("/dashboard");
    }

    [Fact]
    public void Resolve_CashierRole_ReturnsPos()
    {
        RoleHome.Resolve(new[] { "Cashier" }).Should().Be("/pos");
    }

    [Fact]
    public void Resolve_OwnerAndCashier_PrefersOwner()
    {
        RoleHome.Resolve(new[] { "Cashier", "Owner" }).Should().Be("/dashboard");
    }

    [Fact]
    public void Resolve_NoRoles_DefaultsToPos()
    {
        RoleHome.Resolve(Array.Empty<string>()).Should().Be("/pos");
    }
}
```

Create `KayeDM.BMS/tests/KayeDM.Tests/Navigation/NavRouteMatcherTests.cs`:

```csharp
using FluentAssertions;
using KayeDM.Application.Navigation;

namespace KayeDM.Tests.Navigation;

public class NavRouteMatcherTests
{
    [Theory]
    [InlineData("/expenses/report", "expenses", true)]
    [InlineData("/expenses", "expenses", true)]
    [InlineData("expenses/categories", "expenses", true)]
    [InlineData("/pos", "expenses", false)]
    [InlineData("/expensesreport", "expenses", false)]
    public void IsActive_MatchesExactOrNestedPrefix(string currentPath, string prefix, bool expected)
    {
        NavRouteMatcher.IsActive(currentPath, new[] { prefix }).Should().Be(expected);
    }

    [Fact]
    public void IsActive_MatchesAnyOfMultiplePrefixes()
    {
        NavRouteMatcher.IsActive("/closing", new[] { "pos", "closing" }).Should().BeTrue();
    }

    [Fact]
    public void IsActive_CaseInsensitive()
    {
        NavRouteMatcher.IsActive("/POS", new[] { "pos" }).Should().BeTrue();
    }

    [Fact]
    public void IsActive_NoPrefixesMatches_ReturnsFalse()
    {
        NavRouteMatcher.IsActive("/menu", Array.Empty<string>()).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run (from `KayeDM.BMS/`): `dotnet test --filter "FullyQualifiedName~Navigation"`
Expected: build error — `KayeDM.Application.Navigation` namespace doesn't exist yet.

- [ ] **Step 3: Implement the helpers**

Create `KayeDM.BMS/src/KayeDM.Application/Navigation/RoleHome.cs`:

```csharp
namespace KayeDM.Application.Navigation;

public static class RoleHome
{
    public const string Owner = "/dashboard";
    public const string Cashier = "/pos";

    public static string Resolve(IEnumerable<string> roles)
    {
        var roleSet = roles as ICollection<string> ?? roles.ToList();
        return roleSet.Contains("Owner") ? Owner : Cashier;
    }
}
```

Create `KayeDM.BMS/src/KayeDM.Application/Navigation/NavRouteMatcher.cs`:

```csharp
namespace KayeDM.Application.Navigation;

public static class NavRouteMatcher
{
    public static bool IsActive(string currentPath, IEnumerable<string> routePrefixes)
    {
        var normalizedPath = Normalize(currentPath);
        return routePrefixes.Any(prefix =>
        {
            var normalizedPrefix = Normalize(prefix);
            return normalizedPath == normalizedPrefix
                || normalizedPath.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string Normalize(string path) => path.Trim('/').ToLowerInvariant();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~Navigation"`
Expected: `Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9`

- [ ] **Step 5: Commit**

```bash
git add src/KayeDM.Application/Navigation tests/KayeDM.Tests/Navigation
git commit -m "feat(application): add RoleHome and NavRouteMatcher navigation helpers"
```

---

### Task 3: Design tokens, fonts, and favicon

**Files:**
- Modify: `KayeDM.BMS/src/KayeDM.Web/wwwroot/app.css`
- Modify: `KayeDM.BMS/src/KayeDM.Web/Components/App.razor`
- Delete: `KayeDM.BMS/src/KayeDM.Web/wwwroot/favicon.png` (superseded by `favicon.ico`)

**Interfaces:**
- Produces: CSS custom properties (`--route-blue`, `--route-blue-deep`, `--route-blue-tint`, `--signal-yellow`, `--sili-coral`, `--palay-green`, `--ink`, `--ink-soft`, `--paper`, `--surface`, `--line`, `--danger`, `--success`, `--warning-bg`, `--danger-bg`, `--success-bg`, `--font-display`, `--font-body`, `--font-mono`, `--s-1`..`--s-7`, `--r-sm`, `--r-md`, `--r-full`, `--shadow-1`, `--shadow-2`, `--t-fast`, `--t-base`, `--ease`) and utility classes `.route-line` / `.route-line__dot`, consumed by every task from here on.

- [ ] **Step 1: Replace `app.css` with the tokenized version**

Read the current file first, then replace its full contents with:

```css
:root {
  /* Brand */
  --route-blue:        #1E40AF;
  --route-blue-deep:   #14276B;
  --route-blue-tint:   #E8EDFB;
  --signal-yellow:     #FFC933;
  --sili-coral:        #EF5B4C;
  --palay-green:       #2E8B57;

  /* Neutrals */
  --ink:               #1C2333;
  --ink-soft:          #5A6478;
  --paper:             #FAFAF7;
  --surface:           #FFFFFF;
  --line:              #E4E4DC;

  /* Semantic aliases */
  --danger: var(--sili-coral);
  --success: var(--palay-green);
  --warning-bg: #FFF7DE;
  --danger-bg:  #FDEBE9;
  --success-bg: #E9F5EE;

  /* Typography */
  --font-display: 'Archivo', system-ui, sans-serif;
  --font-body:    'Figtree', system-ui, sans-serif;
  --font-mono:    'IBM Plex Mono', monospace;

  /* Space (4px base scale) */
  --s-1: 4px; --s-2: 8px; --s-3: 12px; --s-4: 16px; --s-5: 24px; --s-6: 32px; --s-7: 48px;

  /* Radius */
  --r-sm: 8px;
  --r-md: 12px;
  --r-full: 999px;

  /* Elevation */
  --shadow-1: 0 1px 2px rgb(20 39 107 / 0.06), 0 1px 3px rgb(20 39 107 / 0.08);
  --shadow-2: 0 4px 12px rgb(20 39 107 / 0.10), 0 2px 4px rgb(20 39 107 / 0.06);

  /* Motion */
  --t-fast: 120ms;
  --t-base: 180ms;
  --ease: cubic-bezier(0.2, 0, 0, 1);
}

html, body {
    font-family: var(--font-body);
    background-color: var(--paper);
    color: var(--ink);
}

a, .btn-link {
    color: var(--route-blue);
}

.btn-primary {
    color: #fff;
    background-color: var(--route-blue);
    border-color: var(--route-blue);
}

.btn:focus, .btn:active:focus, .btn-link.nav-link:focus, .form-control:focus, .form-check-input:focus {
  box-shadow: 0 0 0 0.1rem white, 0 0 0 0.25rem var(--route-blue);
}

.content {
    padding-top: 1.1rem;
}

h1:focus {
    outline: none;
}

.valid.modified:not([type=checkbox]) {
    outline: 1px solid var(--success);
}

.invalid {
    outline: 1px solid var(--danger);
}

.validation-message {
    color: var(--danger);
}

.blazor-error-boundary {
    background: url(data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNTYiIGhlaWdodD0iNDkiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgeG1sbnM6eGxpbms9Imh0dHA6Ly93d3cudzMub3JnLzE5OTkveGxpbmsiIG92ZXJmbG93PSJoaWRkZW4iPjxkZWZzPjxjbGlwUGF0aCBpZD0iY2xpcDAiPjxyZWN0IHg9IjIzNSIgeT0iNTEiIHdpZHRoPSI1NiIgaGVpZ2h0PSI0OSIvPjwvY2xpcFBhdGg+PC9kZWZzPjxnIGNsaXAtcGF0aD0idXJsKCNjbGlwMCkiIHRyYW5zZm9ybT0idHJhbnNsYXRlKC0yMzUgLTUxKSI+PHBhdGggZD0iTTI2My41MDYgNTFDMjY0LjcxNyA1MSAyNjUuODEzIDUxLjQ4MzcgMjY2LjYwNiA1Mi4yNjU4TDI2Ny4wNTIgNTIuNzk4NyAyNjcuNTM5IDUzLjYyODMgMjkwLjE4NSA5Mi4xODMxIDI5MC41NDUgOTIuNzk1IDI5MC42NTYgOTIuOTk2QzI5MC44NzcgOTMuNTEzIDI5MSA5NC4wODE1IDI5MSA5NC42NzgyIDI5MSA5Ny4wNjUxIDI4OS4wMzggOTkgMjg2LjYxNyA5OUwyNDAuMzgzIDk5QzIzNy45NjMgOTkgMjM2IDk3LjA2NTEgMjM2IDk0LjY3ODIgMjM2IDk0LjM3OTkgMjM2LjAzMSA5NC4wODg2IDIzNi4wODkgOTMuODA3MkwyMzYuMzM4IDkzLjAxNjIgMjM2Ljg1OCA5Mi4xMzE0IDI1OS40NzMgNTMuNjI5NCAyNTkuOTYxIDUyLjc5ODUgMjYwLjQwNyA1Mi4yNjU4QzI2MS4yIDUxLjQ4MzcgMjYyLjI5NiA1MSAyNjMuNTA2IDUxWk0yNjMuNTg2IDY2LjAxODNDMjYwLjczNyA2Ni4wMTgzIDI1OS4zMTMgNjcuMTI0NSAyNTkuMzEzIDY5LjMzNyAyNTkuMzEzIDY5LjYxMDIgMjU5LjMzMiA2OS44NjA4IDI1OS4zNzEgNzAuMDg4N0wyNjEuNzk1IDg0LjAxNjEgMjY1LjM4IDg0LjAxNjEgMjY3LjgyMSA2OS43NDc1QzI2Ny44NiA2OS43MzA5IDI2Ny44NzkgNjkuNTg3NyAyNjcuODc5IDY5LjMxNzkgMjY3Ljg3OSA2Ny4xMTgyIDI2Ni40NDggNjYuMDE4MyAyNjMuNTg2IDY2LjAxODNaTTI2My41NzYgODYuMDU0N0MyNjEuMDQ5IDg2LjA1NDcgMjU5Ljc4NiA4Ny4zMDA1IDI1OS43ODYgODkuNzkyMSAyNTkuNzg2IDkyLjI4MzcgMjYxLjA0OSA5My41Mjk1IDI2My41NzYgOTMuNTI5NSAyNjYuMTE2IDkzLjUyOTUgMjY3LjM4NyA5Mi4yODM3IDI2Ny4zODcgODkuNzkyMSAyNjcuMzg3IDg3LjMwMDUgMjY2LjExNiA4Ni4wNTQ3IDI2My41NzYgODYuMDU0N1oiIGZpbGw9IiNGRkU1MDAiIGZpbGwtcnVsZT0iZXZlbm9kZCIvPjwvZz48L3N2Zz4=) no-repeat 1rem/1.8rem, #b32121;
    padding: 1rem 1rem 1rem 3.7rem;
    color: white;
}

    .blazor-error-boundary::after {
        content: "An error has occurred."
    }

.darker-border-checkbox.form-check-input {
    border-color: #929292;
}

/* Route line signature element — used only for the active nav marker (Sidebar.razor.css)
   and the login card divider (RouteDivider.razor). Do not add more usages here;
   dashboard section dividers and chart markers are out of scope for this task. */
.route-line {
    position: relative;
    height: 2px;
    background-image: repeating-linear-gradient(to right, var(--line) 0 6px, transparent 6px 12px);
    margin: var(--s-4) 0;
}

.route-line__dot {
    position: absolute;
    left: 0;
    top: 50%;
    transform: translate(-50%, -50%);
    width: 8px;
    height: 8px;
    border-radius: var(--r-full);
    background: var(--rd-color, var(--route-blue));
}

@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: 0.001ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.001ms !important;
    scroll-behavior: auto !important;
  }
}
```

- [ ] **Step 2: Wire fonts and favicon in `App.razor`**

Modify `KayeDM.BMS/src/KayeDM.Web/Components/App.razor` head section to:

```html
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link href="https://fonts.googleapis.com/css2?family=Archivo:wght@600;700;800&family=Figtree:wght@400;500;600&family=IBM+Plex+Mono:wght@500&display=swap" rel="stylesheet" />
    <link rel="stylesheet" href="bootstrap/bootstrap.min.css" />
    <link rel="stylesheet" href="app.css" />
    <link rel="stylesheet" href="KayeDM.Web.styles.css" />
    <link rel="icon" type="image/x-icon" href="favicon.ico" />
    <HeadOutlet @rendermode="InteractiveServer" />
</head>
```

(Only the two `preconnect` lines, the Google Fonts `<link>`, and the favicon `href`/`type` change from the current file.)

- [ ] **Step 3: Remove the superseded placeholder favicon**

Run: `git rm src/KayeDM.Web/wwwroot/favicon.png`

- [ ] **Step 4: Build to confirm no breakage**

Run (from `KayeDM.BMS/`): `dotnet build --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/KayeDM.Web/wwwroot/app.css src/KayeDM.Web/Components/App.razor
git commit -m "feat(web): add design system tokens, Google Fonts, and favicon"
```

---

### Task 4: PageHeader shared component and rollout

**Files:**
- Create: `KayeDM.BMS/src/KayeDM.Web/Components/Shared/PageHeader.razor`
- Create: `KayeDM.BMS/src/KayeDM.Web/Components/Shared/PageHeader.razor.css`
- Modify: `KayeDM.BMS/src/KayeDM.Web/Components/_Imports.razor` (add global usings)
- Modify (h1 → PageHeader, PageTitle suffix): `BusArrivals.razor`, `BusCompanies.razor`, `BusReport.razor`, `Closing.razor`, `Dashboard.razor`, `ExpenseCategories.razor`, `ExpenseReport.razor`, `Expenses.razor`, `InventoryProduction.razor`, `InventoryVariance.razor`, `InventoryWaste.razor`, `Menu.razor` (all in `KayeDM.BMS/src/KayeDM.Web/Components/Pages/`)
- Modify (PageTitle suffix only, no h1 — see deviation #1): `Pos.razor`

**Interfaces:**
- Produces: `<PageHeader Title="string" ActionContent="RenderFragment?">` component, consumed by the 12 pages above.
- Consumes: nothing new.

- [ ] **Step 1: Create `PageHeader.razor`**

```razor
<div class="page-header">
    <h1 class="page-header__title">@Title</h1>
    @if (ActionContent is not null)
    {
        <div class="page-header__actions">@ActionContent</div>
    }
</div>

@code {
    [Parameter, EditorRequired]
    public string Title { get; set; } = "";

    [Parameter]
    public RenderFragment? ActionContent { get; set; }
}
```

- [ ] **Step 2: Create `PageHeader.razor.css`**

```css
.page-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: var(--s-4);
    margin-bottom: var(--s-5);
}

.page-header__title {
    font-family: var(--font-display);
    font-weight: 700;
    font-size: 2.25rem;
    letter-spacing: -0.01em;
    color: var(--ink);
    margin: 0;
}

.page-header__actions {
    flex-shrink: 0;
}
```

- [ ] **Step 3: Add global usings to `_Imports.razor`**

Modify `KayeDM.BMS/src/KayeDM.Web/Components/_Imports.razor` to:

```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using KayeDM.Web
@using KayeDM.Web.Components
@using ApexCharts
@using Microsoft.AspNetCore.Components.Authorization
@using KayeDM.Web.Components.Shared
@using KayeDM.Web.Components.Layout
@using KayeDM.Application.Navigation
```

- [ ] **Step 4: Roll out `PageHeader` and PageTitle suffixes across the 12 pages**

For each file below, make exactly two changes: the `<PageTitle>` gets `— Kaye & DM BMS` appended, and the `<h1>...</h1>` line becomes a `<PageHeader Title="..." />` (self-closing, no `ActionContent` — none of these pages currently have header-level actions).

`BusArrivals.razor`: `<PageTitle>Bus Arrivals</PageTitle>` → `<PageTitle>Bus Arrivals — Kaye & DM BMS</PageTitle>`; `<h1>Bus Arrivals</h1>` → `<PageHeader Title="Bus Arrivals" />`

`BusCompanies.razor`: `<PageTitle>Bus Companies</PageTitle>` → `<PageTitle>Bus Companies — Kaye & DM BMS</PageTitle>`; `<h1>Bus Companies</h1>` → `<PageHeader Title="Bus Companies" />`

`BusReport.razor`: `<PageTitle>Crew Meal Report</PageTitle>` → `<PageTitle>Crew Meal Report — Kaye & DM BMS</PageTitle>`; `<h1>Monthly Crew Meal Report</h1>` → `<PageHeader Title="Monthly Crew Meal Report" />`

`Closing.razor`: `<PageTitle>Daily Closing</PageTitle>` → `<PageTitle>Daily Closing — Kaye & DM BMS</PageTitle>`; `<h1>Daily Closing — @DateTime.Now.ToString("MMM d, yyyy")</h1>` → `<PageHeader Title="@($"Daily Closing — {DateTime.Now:MMM d, yyyy}")" />`

`Dashboard.razor`: `<PageTitle>Dashboard</PageTitle>` → `<PageTitle>Dashboard — Kaye & DM BMS</PageTitle>`; `<h1>Analytics Dashboard</h1>` → `<PageHeader Title="Analytics Dashboard" />`

`ExpenseCategories.razor`: `<PageTitle>Expense Categories</PageTitle>` → `<PageTitle>Expense Categories — Kaye & DM BMS</PageTitle>`; `<h1>Expense Categories</h1>` → `<PageHeader Title="Expense Categories" />`

`ExpenseReport.razor`: `<PageTitle>Expense Report</PageTitle>` → `<PageTitle>Expense Report — Kaye & DM BMS</PageTitle>`; `<h1>Monthly Expense Summary</h1>` → `<PageHeader Title="Monthly Expense Summary" />`

`Expenses.razor`: `<PageTitle>Expenses</PageTitle>` → `<PageTitle>Expenses — Kaye & DM BMS</PageTitle>`; `<h1>Expenses</h1>` → `<PageHeader Title="Expenses" />`

`InventoryProduction.razor`: `<PageTitle>Tray Production</PageTitle>` → `<PageTitle>Tray Production — Kaye & DM BMS</PageTitle>`; `<h1>Today's Tray Production</h1>` → `<PageHeader Title="Today's Tray Production" />`

`InventoryVariance.razor`: `<PageTitle>Variance Report</PageTitle>` → `<PageTitle>Variance Report — Kaye & DM BMS</PageTitle>`; `<h1>Produced / Sold / Wasted Variance</h1>` → `<PageHeader Title="Produced / Sold / Wasted Variance" />`

`InventoryWaste.razor`: `<PageTitle>Waste Log</PageTitle>` → `<PageTitle>Waste Log — Kaye & DM BMS</PageTitle>`; `<h1>Log Waste</h1>` → `<PageHeader Title="Log Waste" />`

`Menu.razor`: `<PageTitle>Menu</PageTitle>` → `<PageTitle>Menu — Kaye & DM BMS</PageTitle>`; `<h1>Menu Items</h1>` → `<PageHeader Title="Menu Items" />`

`Pos.razor` (PageTitle only, no PageHeader — deviation #1): `<PageTitle>POS</PageTitle>` → `<PageTitle>POS — Kaye & DM BMS</PageTitle>`

- [ ] **Step 5: Build to confirm no breakage**

Run: `dotnet build --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/KayeDM.Web/Components/Shared src/KayeDM.Web/Components/_Imports.razor src/KayeDM.Web/Components/Pages
git commit -m "feat(web): add PageHeader component and roll it out across all pages"
```

---

### Task 5: RouteDivider shared component

**Files:**
- Create: `KayeDM.BMS/src/KayeDM.Web/Components/Shared/RouteDivider.razor`

**Interfaces:**
- Produces: `<RouteDivider DotColorToken="string" />` (default `"route-blue"`), consumed by Task 9 (Login page).
- Consumes: `.route-line` / `.route-line__dot` CSS classes from Task 3.

- [ ] **Step 1: Create `RouteDivider.razor`**

```razor
<div class="route-line" style="--rd-color: var(--@DotColorToken)">
    <span class="route-line__dot"></span>
</div>

@code {
    [Parameter]
    public string DotColorToken { get; set; } = "route-blue";
}
```

- [ ] **Step 2: Build to confirm no breakage**

Run: `dotnet build --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/KayeDM.Web/Components/Shared/RouteDivider.razor
git commit -m "feat(web): add RouteDivider component for the route-line signature element"
```

---

### Task 6: NavGroup reusable component

**Files:**
- Create: `KayeDM.BMS/src/KayeDM.Web/Components/Layout/NavGroup.razor`
- Create: `KayeDM.BMS/src/KayeDM.Web/Components/Layout/NavGroup.razor.css`

**Interfaces:**
- Consumes: `KayeDM.Application.Navigation.NavRouteMatcher.IsActive(string, IEnumerable<string>)` from Task 2.
- Produces: `<NavGroup Label="string" RoutePrefixes="string[]">ChildContent</NavGroup>`, consumed by Task 7 (Sidebar).

- [ ] **Step 1: Create `NavGroup.razor`**

```razor
@implements IDisposable
@inject NavigationManager Nav

<div class="nav-group">
    <button type="button" class="nav-group__header" @onclick="Toggle" aria-expanded="@_expanded.ToString().ToLowerInvariant()">
        <span class="nav-group__label">@Label</span>
        <span class="nav-group__chevron @(_expanded ? "nav-group__chevron--open" : "")" aria-hidden="true">▾</span>
    </button>
    <div class="nav-group__body @(_expanded ? "nav-group__body--open" : "")">
        @ChildContent
    </div>
</div>

@code {
    [Parameter, EditorRequired]
    public string Label { get; set; } = "";

    [Parameter, EditorRequired]
    public string[] RoutePrefixes { get; set; } = Array.Empty<string>();

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private bool _expanded;

    protected override void OnInitialized()
    {
        Nav.LocationChanged += OnLocationChanged;
        SyncExpanded();
    }

    protected override void OnParametersSet() => SyncExpanded();

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        SyncExpanded();
        InvokeAsync(StateHasChanged);
    }

    private void SyncExpanded()
    {
        var path = new Uri(Nav.Uri).AbsolutePath;
        if (NavRouteMatcher.IsActive(path, RoutePrefixes))
        {
            _expanded = true;
        }
    }

    private void Toggle() => _expanded = !_expanded;

    public void Dispose() => Nav.LocationChanged -= OnLocationChanged;
}
```

- [ ] **Step 2: Create `NavGroup.razor.css`**

```css
.nav-group {
    margin-bottom: var(--s-1);
}

.nav-group__header {
    all: unset;
    display: flex;
    align-items: center;
    justify-content: space-between;
    width: 100%;
    box-sizing: border-box;
    padding: var(--s-2) var(--s-3);
    cursor: pointer;
    font-family: var(--font-body);
    font-weight: 600;
    font-size: 0.75rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(255, 255, 255, 0.45);
}

.nav-group__header:focus-visible {
    outline: 2px solid var(--signal-yellow);
    outline-offset: 2px;
}

.nav-group__chevron {
    display: inline-block;
    transition: transform var(--t-base) var(--ease);
}

.nav-group__chevron--open {
    transform: rotate(180deg);
}

.nav-group__body {
    max-height: 0;
    overflow: hidden;
    transition: max-height var(--t-base) var(--ease);
}

.nav-group__body--open {
    max-height: 500px;
}
```

- [ ] **Step 3: Build to confirm no breakage**

Run: `dotnet build --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (NavGroup isn't used by any page yet, so it just needs to compile standalone).

- [ ] **Step 4: Commit**

```bash
git add src/KayeDM.Web/Components/Layout/NavGroup.razor src/KayeDM.Web/Components/Layout/NavGroup.razor.css
git commit -m "feat(web): add reusable NavGroup component with route-aware auto-expand"
```

---

### Task 7: Sidebar component and MainLayout shell

**Files:**
- Create: `KayeDM.BMS/src/KayeDM.Web/Components/Layout/Sidebar.razor`
- Create: `KayeDM.BMS/src/KayeDM.Web/Components/Layout/Sidebar.razor.css`
- Delete: `KayeDM.BMS/src/KayeDM.Web/Components/Layout/NavMenu.razor`
- Delete: `KayeDM.BMS/src/KayeDM.Web/Components/Layout/NavMenu.razor.css`
- Modify: `KayeDM.BMS/src/KayeDM.Web/Components/Layout/MainLayout.razor`
- Modify: `KayeDM.BMS/src/KayeDM.Web/Components/Layout/MainLayout.razor.css`

**Interfaces:**
- Consumes: `NavGroup` from Task 6.
- Produces: `<Sidebar />`, consumed by `MainLayout.razor`.

- [ ] **Step 1: Create `Sidebar.razor`**

```razor
@implements IDisposable
@inject NavigationManager Nav

<aside class="sidebar-shell">
    <div class="sidebar-header">
        <a href="/" class="sidebar-brand">
            <img src="img/kaye-dm-logo-3d-64.png" alt="Kaye &amp; DM" width="40" height="40" />
            <span class="sidebar-brand__text">Kaye &amp; DM</span>
        </a>
        <button type="button" class="sidebar-toggle" aria-label="Toggle navigation"
                aria-expanded="@_mobileOpen.ToString().ToLowerInvariant()" @onclick="ToggleMobile">
            <span></span><span></span><span></span>
        </button>
    </div>

    <nav class="sidebar-nav @(_mobileOpen ? "sidebar-nav--open" : "")">
        <AuthorizeView Roles="Owner">
            <div class="nav-item">
                <NavLink class="nav-link" href="dashboard">Dashboard</NavLink>
            </div>
        </AuthorizeView>

        <NavGroup Label="Sales" RoutePrefixes="@(new[] { "pos", "closing" })">
            <div class="nav-item">
                <NavLink class="nav-link" href="pos">POS</NavLink>
            </div>
            <AuthorizeView Roles="Owner">
                <div class="nav-item">
                    <NavLink class="nav-link" href="closing">Daily Closing</NavLink>
                </div>
            </AuthorizeView>
        </NavGroup>

        <NavGroup Label="Buses" RoutePrefixes="@(new[] { "buses" })">
            <div class="nav-item">
                <NavLink class="nav-link" href="buses/arrivals">Arrivals</NavLink>
            </div>
            <AuthorizeView Roles="Owner">
                <div class="nav-item">
                    <NavLink class="nav-link" href="buses/companies">Companies</NavLink>
                </div>
            </AuthorizeView>
            <AuthorizeView Roles="Owner">
                <div class="nav-item">
                    <NavLink class="nav-link" href="buses/report">Crew Meal Report</NavLink>
                </div>
            </AuthorizeView>
        </NavGroup>

        <NavGroup Label="Inventory" RoutePrefixes="@(new[] { "inventory" })">
            <div class="nav-item">
                <NavLink class="nav-link" href="inventory/production">Production</NavLink>
            </div>
            <div class="nav-item">
                <NavLink class="nav-link" href="inventory/waste">Waste</NavLink>
            </div>
            <AuthorizeView Roles="Owner">
                <div class="nav-item">
                    <NavLink class="nav-link" href="inventory/variance">Variance</NavLink>
                </div>
            </AuthorizeView>
        </NavGroup>

        <AuthorizeView Roles="Owner">
            <NavGroup Label="Expenses" RoutePrefixes="@(new[] { "expenses" })">
                <div class="nav-item">
                    <NavLink class="nav-link" href="expenses">Entry</NavLink>
                </div>
                <div class="nav-item">
                    <NavLink class="nav-link" href="expenses/categories">Categories</NavLink>
                </div>
                <div class="nav-item">
                    <NavLink class="nav-link" href="expenses/report">Report</NavLink>
                </div>
            </NavGroup>
        </AuthorizeView>

        <AuthorizeView Roles="Owner">
            <div class="nav-item">
                <NavLink class="nav-link" href="menu">Menu</NavLink>
            </div>
        </AuthorizeView>
    </nav>

    <div class="sidebar-footer">
        <AuthorizeView>
            <Authorized>
                <div class="user-chip">
                    <span class="user-chip__name">@context.User.Identity?.Name</span>
                    <span class="role-badge @(context.User.IsInRole("Owner") ? "role-badge--owner" : "role-badge--cashier")">
                        @(context.User.IsInRole("Owner") ? "Owner" : "Cashier")
                    </span>
                </div>
                <form method="post" action="account/logout">
                    <AntiforgeryToken />
                    <button type="submit" class="sidebar-logout">Log Out</button>
                </form>
            </Authorized>
        </AuthorizeView>
    </div>
</aside>

@code {
    private bool _mobileOpen;

    protected override void OnInitialized() => Nav.LocationChanged += OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        _mobileOpen = false;
        InvokeAsync(StateHasChanged);
    }

    private void ToggleMobile() => _mobileOpen = !_mobileOpen;

    public void Dispose() => Nav.LocationChanged -= OnLocationChanged;
}
```

- [ ] **Step 2: Create `Sidebar.razor.css`**

```css
.sidebar-shell {
    display: flex;
    flex-direction: column;
    height: 100%;
    background: var(--route-blue-deep);
    color: rgba(255, 255, 255, 0.78);
}

.sidebar-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: var(--s-3);
    padding: var(--s-3) var(--s-4);
    background: rgba(0, 0, 0, 0.15);
}

.sidebar-brand {
    display: flex;
    align-items: center;
    gap: var(--s-2);
    text-decoration: none;
    color: white;
}

.sidebar-brand__text {
    font-family: var(--font-display);
    font-weight: 700;
    font-size: 1.125rem;
    color: white;
}

.sidebar-toggle {
    display: none;
    flex-direction: column;
    justify-content: center;
    gap: 4px;
    width: 40px;
    height: 40px;
    background: transparent;
    border: 1px solid rgba(255, 255, 255, 0.2);
    border-radius: var(--r-sm);
    cursor: pointer;
}

.sidebar-toggle span {
    display: block;
    height: 2px;
    background: white;
    border-radius: 1px;
}

.sidebar-toggle:focus-visible {
    outline: 2px solid var(--signal-yellow);
    outline-offset: 2px;
}

.sidebar-nav {
    flex: 1;
    overflow-y: auto;
    padding: var(--s-3) var(--s-2);
}

.nav-item {
    margin-bottom: 2px;
}

.nav-item ::deep .nav-link {
    position: relative;
    display: flex;
    align-items: center;
    min-height: 40px;
    padding: var(--s-2) var(--s-3);
    border-radius: var(--r-sm);
    color: rgba(255, 255, 255, 0.78);
    text-decoration: none;
    font-family: var(--font-body);
    font-weight: 500;
    font-size: 0.875rem;
}

.nav-item ::deep .nav-link:hover {
    background: rgba(255, 255, 255, 0.06);
    color: white;
}

.nav-item ::deep .nav-link.active {
    background: rgba(255, 255, 255, 0.08);
    color: white;
}

.nav-item ::deep .nav-link.active::before {
    content: "";
    position: absolute;
    left: 0;
    top: 4px;
    bottom: 4px;
    width: 3px;
    background-image: repeating-linear-gradient(to bottom, var(--signal-yellow) 0 4px, transparent 4px 8px);
    border-radius: var(--r-full);
}

.nav-item ::deep .nav-link:focus-visible {
    outline: 2px solid var(--signal-yellow);
    outline-offset: 2px;
}

.sidebar-footer {
    padding: var(--s-3) var(--s-4);
    background: rgba(0, 0, 0, 0.15);
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: var(--s-2);
}

.user-chip {
    display: flex;
    align-items: center;
    gap: var(--s-2);
    min-width: 0;
}

.user-chip__name {
    font-size: 0.8125rem;
    color: rgba(255, 255, 255, 0.78);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    max-width: 120px;
}

.role-badge {
    flex-shrink: 0;
    padding: 2px var(--s-2);
    border-radius: var(--r-full);
    font-size: 0.6875rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.04em;
}

.role-badge--owner {
    background: var(--signal-yellow);
    color: var(--ink);
}

.role-badge--cashier {
    background: var(--route-blue-tint);
    color: var(--route-blue-deep);
}

.sidebar-logout {
    background: transparent;
    border: none;
    color: rgba(255, 255, 255, 0.6);
    font-size: 0.8125rem;
    cursor: pointer;
    padding: var(--s-1) var(--s-2);
}

.sidebar-logout:hover {
    color: white;
}

.sidebar-logout:focus-visible {
    outline: 2px solid var(--signal-yellow);
    outline-offset: 2px;
}

@media (max-width: 899.98px) {
    .sidebar-toggle {
        display: flex;
    }

    .sidebar-nav {
        display: none;
    }

    .sidebar-nav--open {
        display: block;
    }
}
```

- [ ] **Step 3: Delete the old NavMenu files**

Run: `git rm src/KayeDM.Web/Components/Layout/NavMenu.razor src/KayeDM.Web/Components/Layout/NavMenu.razor.css`

- [ ] **Step 4: Rewrite `MainLayout.razor`**

```razor
@inherits LayoutComponentBase
@inject NavigationManager NavigationManager

<div class="app-shell">
    <div class="app-shell__sidebar">
        <Sidebar />
    </div>

    <main class="app-shell__main">
        <article class="app-shell__content" @key="NavigationManager.Uri">
            @Body
        </article>
    </main>
</div>

<div id="blazor-error-ui">
    An unhandled error has occurred.
    <a href="" class="reload">Reload</a>
    <a class="dismiss">🗙</a>
</div>
```

- [ ] **Step 5: Rewrite `MainLayout.razor.css`**

```css
.app-shell {
    display: flex;
    min-height: 100vh;
    background: var(--paper);
}

.app-shell__sidebar {
    width: 260px;
    flex-shrink: 0;
    position: sticky;
    top: 0;
    height: 100vh;
}

.app-shell__main {
    flex: 1;
    min-width: 0;
}

.app-shell__content {
    max-width: 1240px;
    padding: var(--s-6);
    animation: fade-up var(--t-base) var(--ease);
}

@keyframes fade-up {
    from {
        opacity: 0;
        transform: translateY(6px);
    }
    to {
        opacity: 1;
        transform: translateY(0);
    }
}

@media (max-width: 899.98px) {
    .app-shell {
        flex-direction: column;
    }

    .app-shell__sidebar {
        width: 100%;
        height: auto;
        position: static;
    }

    .app-shell__content {
        padding: var(--s-4);
    }
}

#blazor-error-ui {
    background: lightyellow;
    bottom: 0;
    box-shadow: 0 -1px 2px rgba(0, 0, 0, 0.2);
    display: none;
    left: 0;
    padding: 0.6rem 1.25rem 0.7rem 1.25rem;
    position: fixed;
    width: 100%;
    z-index: 1000;
}

#blazor-error-ui .dismiss {
    cursor: pointer;
    position: absolute;
    right: 0.75rem;
    top: 0.5rem;
}
```

- [ ] **Step 6: Build to confirm no breakage**

Run: `dotnet build --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add -A src/KayeDM.Web/Components/Layout
git commit -m "feat(web): replace NavMenu with grouped Sidebar and restructure MainLayout shell"
```

---

### Task 8: Role-based redirect and access-denied view

**Files:**
- Modify: `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Home.razor`
- Modify: `KayeDM.BMS/src/KayeDM.Web/Components/Routes.razor`
- Create: `KayeDM.BMS/src/KayeDM.Web/Components/Layout/AccessDenied.razor`
- Create: `KayeDM.BMS/src/KayeDM.Web/Components/Layout/AccessDenied.razor.css`

**Interfaces:**
- Consumes: `RoleHome.Resolve(IEnumerable<string>)` from Task 2.

- [ ] **Step 1: Rewrite `Home.razor`**

```razor
@page "/"
@attribute [Authorize]
@using System.Security.Claims

<PageTitle>Kaye & DM BMS</PageTitle>

@code {
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthenticationStateTask!;
        var roles = authState.User.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value);

        NavigationManager.NavigateTo(RoleHome.Resolve(roles), replace: true);
    }
}
```

- [ ] **Step 2: Create `AccessDenied.razor`**

(Hardcodes "Back to POS" per deviation #2 — Owner can reach every page, so the only NotAuthorized-while-authenticated case is a Cashier hitting an Owner-only route.)

```razor
<div class="access-denied">
    <div class="access-denied__card">
        <div class="access-denied__glyph" aria-hidden="true">🚫</div>
        <h1 class="access-denied__title">This area is for the owner</h1>
        <p class="access-denied__message">You don't have permission to view this page.</p>
        <a class="access-denied__action" href="pos">Back to POS</a>
    </div>
</div>
```

- [ ] **Step 3: Create `AccessDenied.razor.css`**

```css
.access-denied {
    display: flex;
    align-items: center;
    justify-content: center;
    min-height: 60vh;
    padding: var(--s-6);
}

.access-denied__card {
    max-width: 420px;
    text-align: center;
    background: var(--surface);
    border: 1px solid var(--line);
    border-radius: var(--r-md);
    box-shadow: var(--shadow-1);
    padding: var(--s-6);
}

.access-denied__glyph {
    font-size: 2.5rem;
    margin-bottom: var(--s-3);
}

.access-denied__title {
    font-family: var(--font-display);
    font-weight: 700;
    font-size: 1.375rem;
    color: var(--ink);
    margin: 0 0 var(--s-2);
}

.access-denied__message {
    color: var(--ink-soft);
    margin: 0 0 var(--s-4);
}

.access-denied__action {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    height: 40px;
    padding: 0 var(--s-4);
    background: var(--route-blue);
    color: white;
    text-decoration: none;
    border-radius: var(--r-sm);
    font-weight: 600;
}

.access-denied__action:focus-visible {
    outline: 2px solid var(--route-blue);
    outline-offset: 2px;
}
```

- [ ] **Step 4: Update `Routes.razor` to distinguish "not logged in" from "logged in, wrong role"**

```razor
<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                @if (context.User.Identity?.IsAuthenticated == true)
                {
                    <AccessDenied />
                }
                else
                {
                    <RedirectToLogin />
                }
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

(The `@using Microsoft.AspNetCore.Components.Authorization` and `@using KayeDM.Web.Components.Layout` lines can be dropped from the top of this file — both are now global via `_Imports.razor` from Task 4.)

- [ ] **Step 5: Build to confirm no breakage**

Run: `dotnet build --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/KayeDM.Web/Components/Pages/Home.razor src/KayeDM.Web/Components/Routes.razor src/KayeDM.Web/Components/Layout/AccessDenied.razor src/KayeDM.Web/Components/Layout/AccessDenied.razor.css
git commit -m "feat(web): add role-based post-login redirect and friendly access-denied view"
```

---

### Task 9: Login page visual treatment

**Files:**
- Modify: `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Login.razor`
- Create: `KayeDM.BMS/src/KayeDM.Web/Components/Pages/Login.razor.css`

**Interfaces:**
- Consumes: `<RouteDivider DotColorToken="string" />` from Task 5.

- [ ] **Step 1: Rewrite `Login.razor`**

```razor
@page "/account/login"

<PageTitle>Log In — Kaye & DM BMS</PageTitle>

<div class="login-page">
    <div class="login-card">
        <img class="login-card__logo" src="img/kaye-dm-logo-3d-192.png" alt="Kaye &amp; DM" width="120" height="120" />
        <h1 class="login-card__title">Kaye &amp; DM BMS</h1>
        <RouteDivider DotColorToken="sili-coral" />

        @if (HasError)
        {
            <div class="login-card__error" role="alert">Invalid email or password.</div>
        }

        <form method="post" action="account/do-login" class="login-card__form">
            <AntiforgeryToken />
            <input type="hidden" name="returnUrl" value="@ReturnUrl" />
            <div class="login-card__field">
                <label for="login-email">Email</label>
                <input id="login-email" type="email" name="email" required autocomplete="username" />
            </div>
            <div class="login-card__field">
                <label for="login-password">Password</label>
                <input id="login-password" type="password" name="password" required autocomplete="current-password" />
            </div>
            <button type="submit" class="login-card__submit">Log In</button>
        </form>

        <p class="login-card__tagline">Kaye &amp; DM <span class="login-card__tagline-coral">mealstop</span> · Sorsogon</p>
    </div>
</div>

@code {
    [SupplyParameterFromQuery]
    private string? Error { get; set; }

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    private bool HasError => Error is not null;
}
```

- [ ] **Step 2: Create `Login.razor.css`**

```css
.login-page {
    min-height: 100vh;
    display: flex;
    align-items: center;
    justify-content: center;
    background: var(--paper);
    padding: var(--s-4);
}

.login-card {
    width: 100%;
    max-width: 380px;
    background: var(--surface);
    border: 1px solid var(--line);
    border-radius: var(--r-md);
    box-shadow: var(--shadow-2);
    padding: var(--s-6);
    text-align: center;
}

.login-card__logo {
    margin: 0 auto var(--s-3);
    display: block;
}

.login-card__title {
    font-family: var(--font-display);
    font-weight: 700;
    font-size: 1.375rem;
    letter-spacing: -0.01em;
    color: var(--ink);
    margin: 0;
}

.login-card__error {
    background: var(--danger-bg);
    color: var(--danger);
    border-radius: var(--r-sm);
    padding: var(--s-2) var(--s-3);
    font-size: 0.875rem;
    margin-bottom: var(--s-3);
    text-align: left;
}

.login-card__form {
    display: flex;
    flex-direction: column;
    gap: var(--s-3);
    text-align: left;
    margin-top: var(--s-3);
}

.login-card__field label {
    display: block;
    font-family: var(--font-body);
    font-weight: 600;
    font-size: 0.875rem;
    color: var(--ink);
    margin-bottom: var(--s-1);
}

.login-card__field input {
    width: 100%;
    box-sizing: border-box;
    height: 40px;
    padding: 0 var(--s-3);
    border: 1px solid var(--line);
    border-radius: var(--r-sm);
    font-size: 1rem;
    font-family: var(--font-body);
}

.login-card__field input:focus-visible {
    outline: 2px solid var(--route-blue);
    outline-offset: 2px;
}

.login-card__submit {
    height: 40px;
    border: none;
    border-radius: var(--r-sm);
    background: var(--route-blue);
    color: white;
    font-family: var(--font-body);
    font-weight: 600;
    cursor: pointer;
    margin-top: var(--s-1);
}

.login-card__submit:hover {
    filter: brightness(0.94);
}

.login-card__submit:active {
    transform: scale(0.97);
}

.login-card__submit:focus-visible {
    outline: 2px solid var(--signal-yellow);
    outline-offset: 2px;
}

.login-card__tagline {
    margin: var(--s-4) 0 0;
    font-size: 0.8125rem;
    color: var(--ink-soft);
}

.login-card__tagline-coral {
    color: var(--sili-coral);
}
```

- [ ] **Step 3: Build to confirm no breakage**

Run: `dotnet build --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/KayeDM.Web/Components/Pages/Login.razor src/KayeDM.Web/Components/Pages/Login.razor.css
git commit -m "feat(web): apply full visual treatment to the login page"
```

---

### Task 10: Full verification pass

**Files:** none (verification only)

**Interfaces:** N/A

- [ ] **Step 1: Full build**

Run (from `KayeDM.BMS/`): `dotnet build --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 2: Full test suite**

Run: `dotnet test --nologo`
Expected: all tests pass — 51 pre-existing + 9 new from Task 2 = 60 total, 0 failed.

- [ ] **Step 3: Run the app**

Run: `dotnet run --project src/KayeDM.Web --urls http://localhost:5217`
(Leave running in the background for the manual walkthrough below.)

- [ ] **Step 4: Manual/browser walkthrough as `owner@kayedm.local` (password `KayeDM#2026`)**

- Navigate to `http://localhost:5217/` while logged out → lands on the styled login page.
- Log in as Owner → redirected to `/dashboard`.
- Navigate to `/` while authenticated → redirected to `/dashboard` again (not the login page).
- Confirm the sidebar shows: Dashboard, Sales (POS, Daily Closing), Buses (Arrivals, Companies, Crew Meal Report), Inventory (Production, Waste, Variance), Expenses (Entry, Categories, Report), Menu, plus footer with email + "Owner" yellow badge + Log Out.
- Click into `/expenses/report` → confirm the Expenses group is auto-expanded and the Report link shows the active yellow marker.
- Resize to ~390px width → hamburger toggle appears, sidebar nav is hidden until toggled open.
- Check console for JS errors (there should be none — no JS interop was added).

- [ ] **Step 5: Manual/browser walkthrough as `cashier@kayedm.local` (password `KayeDM#2026`)**

- Log out, log back in as Cashier → redirected to `/pos`.
- Confirm the sidebar shows exactly: POS, Arrivals, Production, Waste (plus header/footer) — no Dashboard, no Companies/Crew Meal Report, no Variance, no Expenses group, no Menu.
- Manually navigate to `/dashboard` → confirm the friendly `AccessDenied` card renders ("This area is for the owner" + "Back to POS" button that returns to `/pos`), not a blank page or raw 403.

- [ ] **Step 6: Screenshot check at three widths**

Using the claude-in-chrome tool (or manual resize), capture the sidebar + a couple of representative pages (e.g. `/dashboard`, `/expenses/report`) at 1366×768, 1920×1080, and ~390px. Confirm no horizontal scrolling and the hamburger only appears below ~900px.

- [ ] **Step 7: Stop the app**

Stop the background `dotnet run` process (and run `dotnet build-server shutdown` if a worktree/file-lock issue comes up — see the "Windows worktree cleanup file locks" memory note, though this plan doesn't use a worktree so it's unlikely to be needed).

- [ ] **Step 8: Final review pass**

Re-read `docs/prompts/kaye-dm-week4-5-app-shell-prompt.md`'s "Deliverable format" section and prepare the four items (files changed, sidebar description per role, deviations, how to verify) for the PR description — this happens in the `finishing-a-development-branch` flow, not as a commit.
