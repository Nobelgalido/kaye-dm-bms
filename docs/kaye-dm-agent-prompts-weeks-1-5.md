# Kaye & DM BMS — Agent Prompts, Weeks 1–5

Each prompt is self-contained. Paste ONE prompt per session, always attaching `kaye-dm-bms-blueprint.md` as context. Run them in order; each assumes the previous week is done and committed.

Shared rules appear in every prompt because agents don't remember across sessions. Before pasting Week N, update the "Current state" section if reality diverged from plan.

---
---

# WEEK 1 PROMPT — Core POS

## Context

You are working on **Kaye & DM BMS**, an ASP.NET Core 8 + Blazor Server + EF Core 8 + SQL Server portfolio project. The full blueprint is attached — read it for domain understanding and conventions, but **implement only the Week 1 scope below.**

## Current state (do not redo)

- Solution `KayeDM.BMS` exists: `KayeDM.Domain`, `KayeDM.Application`, `KayeDM.Infrastructure` (class libs), `KayeDM.Web` (Blazor Web App, .NET 8, Interactive Server, Global interactivity, no auth scaffold), `KayeDM.Tests` (xUnit).
- References: Web → Application + Infrastructure; Infrastructure → Application; Application → Domain; Tests → all three.
- Packages pinned to **8.0.11**: EFCore.SqlServer, EFCore.Design (Infra + Web), AspNetCore.Identity.EntityFrameworkCore; Tests has FluentAssertions + EFCore.Sqlite. **Never upgrade to EF 9/10; target stays net8.0.**
- `AppDbContext : IdentityDbContext`, `MenuItem` entity, LocalDB connection string (`KayeDmBms`), and `InitialCreate` migration may already exist — check first; build on them, never regenerate.

## Scope — ONLY this

1. **Entities:** verify/complete `MenuItem`; add `Order`, `OrderLine`; enums `MenuCategory`, `OrderStatus` (Completed, Voided), `PaymentMethod` (Cash, GCash). Follow blueprint §4 exactly: `UnitPriceAtSale` snapshot on OrderLine; `Order.BusTripId` as plain nullable int with `// TODO Week 2: FK to BusTrip` (no FK yet); `Order.IsCrewMeal` bool defaulting false.
2. **EF config:** `HasPrecision(10,2)` on all money columns; max lengths on strings; order number as string `yyyyMMdd-NNN`.
3. **Migration:** one migration named `AddOrderTables`.
4. **Services:** `IMenuItemService`, `IOrderService` interfaces in Application, implementations in Infrastructure. OrderService: create order with lines, total from lines, change from tendered, daily sequential order number, void with reason.
5. **`/menu` page:** item table, add/edit form, activate/deactivate toggle, SortOrder editing. Plain Blazor, no UI library.
6. **`/pos` page** per blueprint Module A: category tabs + big-button grid (SortOrder, active only) on the left; ticket panel (lines, qty steppers, remove, running total) on the right; payment strip (Cash/GCash toggle, ₱100/₱200/₱500/₱1000 quick buttons + numeric input, computed change, Complete → persist, clear, show order number) at the bottom. Crew-meal toggle and bus assignment are Week 2 — leave clean extension points only.
7. **Tests:** 5–8 xUnit tests — order total math, change calculation, daily number sequencing, void rule. SQLite in-memory for service tests.
8. **Cleanup:** delete template pages `Counter.razor`, `Weather.razor` and their nav links.

## Hard constraints (apply every week)

- **Migrations are sacred:** one per schema change, descriptive names, NEVER delete/regenerate/squash/edit existing ones. Wrong migration → new corrective migration.
- `dotnet ef` CLI style: `--project src/KayeDM.Infrastructure --startup-project src/KayeDM.Web`.
- Layering: no EF Core in Domain; Web talks to Application interfaces only.
- No MediatR, AutoMapper, or repository wrappers. Plain services + constructor injection.
- Prefer pure Blazor over JS interop. Currency format `"₱{0:N2}"`. File-scoped namespaces, nullable enabled.

## Out of scope this week

Buses, crew meals, inventory, waste, expenses, dashboard, closing, auth, seeding, charts, Docker, deployment.

## Deliverable format (every week)

1. Files created/modified, grouped by project
2. Exact `dotnet ef` commands run or to run
3. Deviations from blueprint + why
4. How to run and manually test
5. Uncertainties flagged explicitly — never silently guess domain rules

---
---

# WEEK 2 PROMPT — Bus & Crew Module

## Context

Continuing **Kaye & DM BMS** (blueprint attached). Week 1 is complete: MenuItem/Order/OrderLine entities, `AddOrderTables` migration, menu CRUD at `/menu`, working POS at `/pos` with cash/GCash payment, order services, and passing tests. **Implement only Week 2 scope.**

## Current state (do not redo)

Everything from Week 1's "Current state" plus Week 1 deliverables. `Order.BusTripId` exists as a plain nullable int marked `// TODO Week 2`. Packages remain pinned to 8.0.11 — do not touch versions.

## Scope — ONLY this

1. **Entities:** `BusCompany` (Name, ContactPerson, `CrewMealAllowancePerTrip` int, IsActive), `BusTrip` (BusCompanyId FK, BusNumber, ArrivedAt, DepartedAt?, Route, EstimatedPassengers?), `CrewMealCredit` (BusTripId FK, `CrewRole` enum Driver/Conductor/Assistant, OrderId FK, LoggedAt). Per blueprint §4.
2. **Migrations:** `AddBusCompanyAndTrip`, then `AddCrewMealCredit`, then a third migration `LinkOrderToBusTrip` converting `Order.BusTripId` into a real FK. Three separate migrations, in that order — this deliberate incremental history is a feature of the project.
3. **Domain rule (unit-tested):** crew meal credits per trip must not exceed `BusCompany.CrewMealAllowancePerTrip`. Enforce in the service; throw a domain exception with a clear message on violation.
4. **Services:** `IBusService` (company CRUD, trip logging, trip board query, allowance-remaining calc), extend `IOrderService` with crew-meal order creation (total forced to ₱0, requires trip + crew role, creates the CrewMealCredit atomically with the order).
5. **`/buses/companies` page:** CRUD with allowance field.
6. **`/buses/arrivals` page:** quick-log form (company dropdown, bus number, route, arrive button → timestamps now) + today's trip board showing per trip: company, bus no., arrived time, meals used vs. allowance remaining, optional depart button.
7. **POS updates:** "Assign to bus" dropdown listing trips arrived within the last 45 minutes (company + bus no.); **crew meal mode** toggle — when on, requires trip + crew role selection, zeroes the ticket total, Complete creates the ₱0 order + credit.
8. **`/buses/report` page:** month picker + company picker → table of that company's trips and credited meals for the month, with totals (trips, meals by role, total meals). Simple HTML table; export comes later.
9. **Tests:** 5–8 new tests — allowance cap (at limit, over limit), crew order is ₱0, credit-order atomicity, 45-minute trip window query.

## Hard constraints

Same as Week 1: sacred migrations, `dotnet ef` CLI flags, layering, no pattern libraries, pure Blazor, ₱ formatting, nullable enabled, packages stay 8.0.11.

## Out of scope this week

Inventory, waste, expenses, dashboard, closing, auth, seeding, charts, Docker, deployment, report export/print.

## Deliverable format

Same 5-point format as Week 1.

---
---

# WEEK 3 PROMPT — Tray Inventory + Expense Module

## Context

Continuing **Kaye & DM BMS** (blueprint attached). Weeks 1–2 complete: POS with crew-meal mode and bus assignment, bus company/trip management, monthly crew meal report, domain rules tested. **Implement only Week 3 scope.**

## Current state (do not redo)

All Week 1–2 deliverables and migrations (`InitialCreate`, `AddOrderTables`, `AddBusCompanyAndTrip`, `AddCrewMealCredit`, `LinkOrderToBusTrip`). Packages pinned 8.0.11.

## Scope — ONLY this

### Tray inventory
1. **Entities:** `DishBatch` (MenuItemId FK, Date, `TraysProduced` decimal — halves allowed, `ServingsPerTray` int, ProducedAt), `WasteLog` (DishBatchId FK, `TraysWasted` decimal, `Reason` enum EndOfDay/Spoiled/Dropped, LoggedAt, LoggedById string — plain string until auth in Week 4, `// TODO Week 4`).
2. **Migrations:** `AddDishBatch`, then `AddWasteLog` (two separate migrations).
3. **Availability rule:** available servings = (TraysProduced × ServingsPerTray) − servings sold today for that MenuItem − (TraysWasted × ServingsPerTray). Selling past availability shows a confirm-override warning on the POS but is allowed (real canteens oversell); overridden orders are flagged (add `Order.OversoldOverride` bool via its own migration `AddOversoldFlag`).
4. **`/inventory/production` page:** morning batch entry — dish, trays produced, servings/tray; today's batch list.
5. **POS availability strip:** per visible menu item show remaining servings; red state at ≤5; items with no batch today show "no batch" but remain sellable.
6. **`/inventory/waste` page:** log waste against today's batches with reason.
7. **`/inventory/variance` page:** per dish per day — produced / sold / wasted / variance %, date-range filter.

### Expense module
8. **Entities:** `ExpenseCategory` (Name, `Type` enum Ingredients/Utilities/Wages/Rent/Supplies/Maintenance/Other, IsActive), `Expense` (Date, ExpenseCategoryId FK, Description, Amount decimal, `PaymentMethod` enum Cash/GCash/BankTransfer, Vendor?, ReceiptRef?, LoggedById string `// TODO Week 4`, LoggedAt).
9. **Migrations:** `AddExpenseTables` (one migration for both, they ship together).
10. **`/expenses` page:** quick entry form + filterable list (date range, category), edit/delete.
11. **`/expenses/categories` page:** CRUD, with the seven default types seeded via code on startup if table empty.
12. **`/expenses/report` page:** date-range + category filters; monthly summary table (rows = categories, columns = months in range, cells = totals, grand totals row/column).
13. **Tests:** 6–10 new tests — availability math (incl. waste and oversell flag), variance calculation, expense monthly summary aggregation.

## Hard constraints

Same as prior weeks. Note the migration sequence this week is: `AddDishBatch` → `AddWasteLog` → `AddOversoldFlag` → `AddExpenseTables`. Four migrations, exactly this order, never combined.

## Out of scope this week

Dashboard/charts, daily closing, auth/roles, seed data generator, Docker, deployment, exports.

## Deliverable format

Same 5-point format.

---
---

# WEEK 4 PROMPT — Analytics Dashboard, Daily Closing, Auth, Seeder

## Context

Continuing **Kaye & DM BMS** (blueprint attached). Weeks 1–3 complete: POS with availability strip and crew-meal mode, bus module + report, tray inventory with variance, expense module + report. **Implement only Week 4 scope.**

## Current state (do not redo)

All prior deliverables; migration history (9 total, confirmed applied via `dotnet ef migrations list`): InitialCreate, AddOrderTables, AddBusCompanyAndTrip, AddCrewMealCredit, LinkOrderToBusTrip, AddDishBatch, AddWasteLog, AddOversoldFlag, AddExpenseTables. Identity package installed but not wired. Packages pinned 8.0.11.

`WasteLog.LoggedById` and `Expense.LoggedById` are plain strings hardcoded to `"system"` by their services — both marked `// TODO Week 4`, waiting on Identity wiring (Week 4 scope item 4 below).

Date-input rule for Blazor `@bind` on native `<input>` elements: `type="date"`/`"time"`/`"datetime-local"` bind cleanly to `DateOnly`/`TimeOnly`/`DateTime` (or use `<InputDate>` inside an `EditForm`) — don't fall back to free-text string inputs for these. Only `type="month"` lacks built-in Blazor binding support and genuinely needs the text-input workaround (see `/buses/report`).

## Scope — ONLY this

### Auth & roles (do first — other features gate on it)
1. Wire ASP.NET Core Identity using the existing `IdentityDbContext`. Migration: `AddIdentitySchema` if Identity tables aren't already in InitialCreate — check first.
2. Roles `Owner`, `Cashier`; seeded users `owner@kayedm.local` / `cashier@kayedm.local` (password `KayeDM#2026` both — document this in README later). Login page, logout, no registration.
3. Gate routes per blueprint §6 page map: Cashier sees `/pos`, `/buses/arrivals`, `/inventory/production`, `/inventory/waste`; Owner sees everything.
4. Replace the `LoggedById` string TODOs on WasteLog/Expense with the real user id; migration `WireLoggedByToIdentity` only if a schema change is needed (FK), otherwise no migration.

### Daily closing
5. **Entity:** `DailyClosing` per blueprint §4 including `TotalExpenses`, `NetForDay`. Migration: `AddDailyClosing`.
6. **Rules (unit-tested):** one closing per date; immutable once created; a closed date rejects new/edited orders, voids, and expenses on/before it (enforce in services).
7. **`/closing` page (Owner):** shows today's computed figures (sales by method, order count, voids, crew meals, expenses, net) → Confirm creates the snapshot.

### Analytics dashboard
8. Add a Blazor chart library now (pick a maintained, MIT-licensed one compatible with .NET 8 Blazor Server; justify the pick in your summary; pin its version).
9. **`/dashboard` page (Owner)** per blueprint Module E:
   - KPI row with date-range selector: sales, expenses, net profit, order count, avg ticket, crew meals given + estimated cost (avg completed-order line price × count)
   - Sales-by-hour bar/line for selected day **with bus arrival markers overlaid**
   - Revenue vs. expenses vs. net by day, 30-day trend
   - Expense breakdown by category (selectable month)
   - Top dishes by revenue (7/30 day toggle); waste % by dish
   - Sales per bus company: orders assigned to a company's trips, plus orders completed within ±20 min of that company's arrivals as "wave-attributed" (label the heuristic in the UI)
   - Payment method split
10. **Insight callouts:** 2–4 rule-based highlights computed from data (best wave by average take, highest-waste dish with tray-reduction suggestion, best/worst net day in range). Plain computed rules — no AI.

### Seed data generator
11. `dotnet run --project src/KayeDM.Web -- --seed` (or a dedicated console entry in Infrastructure — your call, justify it): wipes and regenerates demo data with a **fixed random seed** per blueprint §7 — 25 menu items (Filipino canteen staples, ₱15–₱120), 4 bus companies (allowance 2–4), 30 days of history with wave clustering (9:30–10:30, 13:30–14:30, 18:00–19:30), 60–140 orders/day 85% cash, batches + 3–12% waste, crew meals on ~90% of trips, expenses at 65–75% of revenue with 2–3 net-negative days, and a DailyClosing for every past day.
12. **Tests:** 5–8 new — closing immutability, closed-date locks (orders and expenses), net calculation, wave-attribution query.

## Hard constraints

Same as prior weeks. Expected new migrations this week, in order: (`AddIdentitySchema` if needed) → `AddDailyClosing` → (`WireLoggedByToIdentity` if needed). Chart library is the ONLY new dependency allowed.

## Out of scope this week

Docker, deployment, README/GIF, exports/printing, keyboard shortcuts.

## Deliverable format

Same 5-point format, plus: state which chart library you chose and why.

---
---

# WEEK 5 PROMPT — Polish, Docker, Deployment Prep, Docs

## Context

Final week of **Kaye & DM BMS** (blueprint attached). Weeks 1–4 complete: full feature set, auth, dashboard, closing, seeder. **Implement only Week 5 scope.** This week is polish and packaging — no new features, no new entities, no schema changes unless fixing a genuine bug (corrective migration only).

## Current state (do not redo)

Full app working locally against LocalDB with seeded demo data. All migrations intact. Packages pinned 8.0.11 + the chart library.

## Scope — ONLY this

1. **POS polish:** keyboard shortcuts (F1–F5 category tabs, Enter completes payment when valid, Esc clears ticket — confirm if lines exist); visible focus states; empty states for every list/table in the app ("No trips logged today" etc.); loading states on save buttons (disable + spinner) to prevent double-submit.
2. **UI consistency pass:** one shared layout polish — app name/logo text "Kaye & DM BMS", nav grouped by module, consistent page headers, consistent ₱ formatting everywhere, dates as `MMM d, yyyy h:mm tt`, PH timezone assumption documented.
3. **Error handling:** global error boundary with a friendly message; domain exceptions surface as inline alerts, not crashes; closed-date rejections show the specific rule violated.
4. **Docker:** multi-stage `Dockerfile` for KayeDM.Web; `docker-compose.yml` with the app + `mcr.microsoft.com/mssql/server:2022-latest`, healthcheck, auto-migrate + auto-seed on first run via env flag (`SEED_ON_START=true`), connection string via environment variable. Must work with a single `docker compose up`.
5. **README.md** per blueprint §12: domain explanation (waves, crew meals, trays), stack, architecture diagram (Mermaid), highlights, screenshots placeholders (I'll capture them), run instructions (compose one-liner + seeded logins), migration-history-as-feature note, and the "rebuilt from VB.NET capstone" framing.
6. **docs/architecture.md:** layering diagram, key decisions in ADR-lite bullets (Blazor Server choice, no-pattern-libraries choice, tray-based inventory model, migration history policy, oversell-override rationale).
7. **Smoke checklist:** a `docs/demo-script.md` with the 45-second demo flow from blueprint §8 as numbered steps, so recording the GIF is mechanical.
8. **Final test pass:** ensure the full suite runs green with `dotnet test`; add any missing test for bugs found during polish.

## Hard constraints

Same as all prior weeks. **No schema changes** except corrective migrations for real bugs. No new packages except what Docker requires (none expected).

## Out of scope

Deployment to a live host (I'll handle Fly.io/Azure separately), GIF recording, screenshots, CI pipeline (that's project #2's territory).

## Deliverable format

Same 5-point format, plus: confirm `docker compose up` works from a clean clone with no local .NET/SQL installed assumptions beyond Docker.
