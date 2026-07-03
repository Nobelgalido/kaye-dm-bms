# Kaye & DM BMS — Project Blueprint

Ground-up rebuild of a Business Management System for **Kaye & DM Food Stop**, a provincial bus meal stop in Sorsogon, Philippines. Originally built as an academic capstone in VB.NET / .NET Framework / MySQL; rebuilt here in the modern .NET stack as a portfolio piece.

**Status:** Portfolio project (no production deployment, no hardware integration).
**Author:** Alfred Nobel Galido — github.com/Nobelgalido

---

## 1. Why This Project Exists

A bus meal stop is not a normal restaurant:

- **Demand arrives in waves.** Three buses pull in at once; 150 passengers must order, eat, and leave in 20–30 minutes. Then dead time until the next wave.
- **Bus crews eat free.** Stopovers feed drivers and conductors at no charge — that is the incentive for the bus to stop there. This creates a real accounting obligation: crew meals must be logged per trip and reported per bus company monthly.
- **Inventory is tray-based, not SKU-based.** Food is cooked in batches ("3 trays of adobo"). What matters is trays produced vs. sold vs. wasted per day — not per-ingredient deduction.

The system models these three realities. That domain specificity is the point: it demonstrates modeling a real business, not cloning a POS tutorial.

---

## 2. Tech Stack

| Layer | Choice | Rationale |
|---|---|---|
| Runtime | .NET 8 (ASP.NET Core) | Target stack for junior .NET roles |
| UI | Blazor Server | Interactive POS without a JS SPA; low-latency on one server |
| ORM | EF Core 8 | Migrations history is a deliberate feature (see §9) |
| Database | SQL Server (LocalDB dev / Azure SQL free tier or Dockerized SQL Server for demo) | Matches MSSQL requirement in target job postings |
| Auth | ASP.NET Core Identity, minimal (2 seeded roles) | Enough to show role-gating, nothing more |
| Charts | Blazor-compatible chart lib (e.g., ApexCharts Blazor wrapper) | Dashboard visuals |
| Testing | xUnit + FluentAssertions; bUnit optional for 2–3 component tests | Show testing habit, not coverage theater |
| Deployment | Fly.io / Azure App Service free tier + Dockerfile | Live demo link in README |

**Explicit non-goals:** offline mode, receipt printers, cash drawers, GCash/payment gateway integration (payment method is an enum), multi-branch, printing (receipts render as HTML).

---

## 3. Solution Structure

```
KayeDM.BMS.sln
├── src/
│   ├── KayeDM.Domain/           # Entities, enums, domain rules. No dependencies.
│   ├── KayeDM.Application/      # Services, DTOs, interfaces (IOrderService etc.)
│   ├── KayeDM.Infrastructure/   # EF Core DbContext, migrations, repositories, seeding
│   └── KayeDM.Web/              # Blazor Server app (pages, components, auth)
├── tests/
│   └── KayeDM.Tests/            # xUnit: domain rules + service tests
├── docs/
│   ├── architecture.md          # Diagram + decisions (ADR-lite)
│   └── screenshots/
├── Dockerfile
└── README.md
```

**Layering rule:** Web → Application → Domain; Infrastructure implements Application interfaces. Keep it simple — plain services with constructor injection, no MediatR, no repository-over-repository abstractions. A junior repo drowning in patterns reads as cargo-culting.

---

## 4. Domain Model

### Entities (~13 tables)

**MenuItem**
- `Id`, `Name`, `Category` (enum: Ulam, Rice, Drinks, Snacks, Dessert), `Price` (decimal), `IsActive`, `SortOrder`
- `SortOrder` drives POS grid layout (best-sellers top-left).

**Order**
- `Id`, `OrderNumber` (daily sequence, e.g., `20260703-041`), `CreatedAt`, `CashierId`, `Status` (enum: Completed, Voided), `PaymentMethod` (enum: Cash, GCash), `AmountTendered`, `ChangeGiven`, `BusTripId?` (nullable FK — set when order belongs to a wave), `IsCrewMeal` (bool)
- Total is computed from lines; never stored redundantly except on `DailyClosing` snapshot.

**OrderLine**
- `Id`, `OrderId`, `MenuItemId`, `Quantity`, `UnitPriceAtSale` (snapshot — prices change)

**BusCompany**
- `Id`, `Name` (e.g., DLTB, Isarog, Penafrancia), `ContactPerson`, `CrewMealAllowancePerTrip` (int — how many free meals per stop), `IsActive`

**BusTrip**
- `Id`, `BusCompanyId`, `BusNumber` (plate/body no.), `ArrivedAt`, `DepartedAt?`, `Route` (e.g., "Manila → Sorsogon"), `EstimatedPassengers` (int, optional)
- A "wave" = trips with `ArrivedAt` within the same window. Dashboard groups by this.

**CrewMealCredit**
- `Id`, `BusTripId`, `CrewRole` (enum: Driver, Conductor, Assistant), `OrderId` (the ₱0-charged order), `LoggedAt`
- Business rule: credits per trip ≤ `BusCompany.CrewMealAllowancePerTrip`. Enforced in domain, tested in xUnit.

**DishBatch** (the tray)
- `Id`, `MenuItemId`, `Date`, `TraysProduced` (decimal — half trays allowed), `ServingsPerTray` (int), `ProducedAt`
- Available servings = produced × servings/tray − sold − wasted.

**WasteLog**
- `Id`, `DishBatchId`, `TraysWasted` (decimal), `Reason` (enum: EndOfDay, Spoiled, Dropped), `LoggedAt`, `LoggedById`

**DailyClosing** (Z-reading snapshot)
- `Id`, `Date`, `TotalSales`, `CashSales`, `GCashSales`, `OrderCount`, `VoidedCount`, `CrewMealsGiven`, `TotalExpenses`, `NetForDay` (sales − expenses), `ClosedById`, `ClosedAt`
- Immutable once created; one per date. Locks both orders and expenses for that date.

**Expense**
- `Id`, `Date`, `ExpenseCategoryId`, `Description`, `Amount` (decimal), `PaymentMethod` (enum: Cash, GCash, BankTransfer), `Vendor?` (optional — e.g., "Sorsogon Public Market"), `ReceiptRef?` (optional OR number), `LoggedById`, `LoggedAt`
- Locked once the day's `DailyClosing` exists, same as orders.

**ExpenseCategory**
- `Id`, `Name`, `Type` (enum: Ingredients, Utilities, Wages, Rent, Supplies, Maintenance, Other), `IsActive`
- Seeded defaults; owner can add custom categories.

**AppUser** (Identity)
- Standard Identity user + `DisplayName`. Roles: `Owner`, `Cashier` (seeded).

**AuditNote** (optional, phase 5)
- Lightweight log of voids and closing edits. Skip if time-pressed.

### Key relationships
```
BusCompany 1—* BusTrip 1—* CrewMealCredit *—1 Order
Order 1—* OrderLine *—1 MenuItem 1—* DishBatch 1—* WasteLog
```

### Domain rules to enforce (and unit-test)
1. Crew meal credits per trip cannot exceed the company's allowance.
2. An order cannot sell more servings than a batch has available (warn, allow override with flag — real canteens oversell trays).
3. `DailyClosing` locks the day: no new orders, voids, or expenses dated on/before a closed date.
4. Voiding requires Owner role and a reason.
5. `UnitPriceAtSale` snapshots at order time; menu price edits never rewrite history.

---

## 5. Feature Modules

### Module A — POS (the product)
The cashier screen. Design target: **6 orders entered in under 60 seconds.**

- Left: category tabs + big-button menu grid (touch-friendly, `SortOrder`-driven).
- Right: running ticket (lines, qty steppers, total).
- Bottom: payment strip — Cash/GCash toggle, tendered-amount quick buttons (₱100/₱200/₱500/₱1000 + numpad), auto change calculation, one-tap **Complete**.
- Optional "Assign to bus" dropdown showing trips arrived in the last 45 min.
- Crew meal mode: toggle that zeroes the total, requires selecting trip + crew role, creates the `CrewMealCredit`.
- Keyboard shortcuts (F-keys for categories, Enter to complete) — small touch, big demo impression.

### Module B — Bus & Crew
- BusCompany CRUD with allowance setting.
- "Bus arrived" quick-log: company, bus number, route → creates `BusTrip`, starts the wave clock.
- Trip board: today's arrivals, meals credited vs. allowance remaining.
- **Monthly crew meal report per company** — table + export view. This is the feature interviewers remember.

### Module C — Tray Inventory
- Morning production entry: dish, trays produced, servings per tray.
- Live availability strip on the POS (e.g., "Adobo: 14 servings left" with red state at ≤5).
- End-of-day waste logging with reason.
- Variance view: produced vs. sold vs. wasted per dish per day.

### Module D — Expenses
- Quick expense entry: date, category, description, amount, payment method, optional vendor/receipt ref. Designed for end-of-day batch entry (market run receipts).
- Category management (Owner).
- **Expense report:** filterable by date range and category; monthly summary table (category × month) with totals; export view.
- Expenses feed into daily closing (`TotalExpenses`, `NetForDay`) and the analytics dashboard.
- Domain rule: expenses on a closed date cannot be added or edited.

### Module E — Analytics Dashboard (Owner)
KPI row (today + selectable range):
- Sales, expenses, **net profit**, order count, avg ticket, crew meals given (with estimated cost = avg meal price × count — makes the "free crew meals" obligation visible in pesos).

Charts:
- **Sales-by-hour with bus arrival markers overlaid** — visually proves the wave pattern. This is the screenshot.
- **Revenue vs. expenses vs. net, by day** (30-day trend line) — the "is this business healthy" view.
- Expense breakdown by category (donut/bar, selectable month).
- Top dishes by revenue and by margin proxy (revenue − waste share), 7/30 days.
- Waste % by dish — flags over-production.
- Sales per bus company (which lines bring the passengers) — ties trips → orders in a wave window; the analytical insight no generic POS gives.
- Payment method split.

Insight callouts (computed, not AI): e.g., "Tuesday 2 PM wave averages ₱4,200 — 38% above other waves", "Dinuguan waste rate 14% over 30 days — consider producing 1 fewer tray." Simple rule-based highlights make the dashboard read as *analysis*, not just charts.

Daily closing workflow: review sales + expenses + net → confirm → creates immutable `DailyClosing`.

### Module F — Auth & Roles
- Seeded users: `owner@kayedm.local` / `cashier@kayedm.local`.
- Cashier sees POS + bus quick-log only. Owner sees everything.
- Login page, nothing fancier. No registration flow.

---

## 6. Page Map

| Route | Page | Role |
|---|---|---|
| `/pos` | POS screen | Cashier, Owner |
| `/buses/arrivals` | Trip quick-log + today's board | Cashier, Owner |
| `/buses/companies` | Company CRUD | Owner |
| `/buses/report` | Monthly crew meal report | Owner |
| `/inventory/production` | Morning batch entry | Cashier, Owner |
| `/inventory/waste` | Waste logging | Cashier, Owner |
| `/inventory/variance` | Variance report | Owner |
| `/expenses` | Expense entry + list | Owner |
| `/expenses/categories` | Category CRUD | Owner |
| `/expenses/report` | Expense report (range/category filters, monthly summary) | Owner |
| `/dashboard` | Analytics dashboard (KPIs, charts, insights) | Owner |
| `/closing` | Daily Z-reading | Owner |
| `/menu` | MenuItem CRUD | Owner |

---

## 7. Seed Data Strategy

The demo lives or dies on realistic data. Build a dedicated seeder (`dotnet run --seed`):

- ~25 menu items across categories, Filipino canteen staples, realistic PHP prices (₱15–₱120).
- 4 bus companies with different allowances (2–4 meals/trip).
- **30 days of history** with the wave pattern baked in: arrival clusters ~9:30–10:30, 13:30–14:30, 18:00–19:30; order volume spikes ±20 min around each arrival; near-zero sales between waves.
- Randomized but plausible: 60–140 orders/day, 85% cash, 2–4 trays/dish/day, 3–12% waste, occasional voids.
- Crew meals logged on ~90% of trips.
- **Expenses:** daily market runs (₱3,000–₱6,000 Ingredients), utilities twice a month, weekly wages, monthly rent, occasional supplies/maintenance — totaling ~65–75% of revenue so net profit trends look realistic, with 2–3 visibly bad days (net negative) to keep the trend chart honest.
- `DailyClosing` generated for every past day, including expense totals and net.

Use a fixed random seed so the demo data is reproducible.

---

## 8. Demo Flow (README GIF script, ~45 seconds)

1. Bus arrives → cashier quick-logs "DLTB · Bus 8112 · Manila→Sorsogon".
2. Rapid-fire: 6 passenger orders in under a minute on the POS.
3. Driver eats → crew meal mode → DLTB trip → Driver → ₱0 order logged.
4. Switch to owner → log the day's market-run expense → analytics dashboard shows today's spike aligned with the arrival marker, plus revenue vs. expenses vs. net trend.
5. Open monthly DLTB crew meal report and the expense report.
6. Run daily closing — sales, expenses, net for the day locked in one snapshot.

One coherent story: wave hits → system keeps up → owner sees the business.

---

## 9. EF Core Migrations as a Feature

Do **not** squash migrations. Build the schema incrementally and keep every migration in git — target **10+ migrations** reflecting real evolution (add nullable column, backfill, make non-null; add index; rename; split a table).

Reason: this repo becomes the test corpus for the **EF Core Migration Safety Checker** (project #2). Demo story: "I built the BMS, then built a CI tool that audits its own migration history for production-dangerous patterns." Two projects, one narrative.

---

## 10. Testing Scope

Keep it honest and small:
- Domain rule tests (crew allowance cap, closing lock, availability math, void rules) — xUnit.
- One service-level test with EF Core InMemory or SQLite provider for the order → batch deduction flow.
- 2–3 bUnit component tests max (ticket total updates, crew-meal toggle) — optional.

Target: ~25–40 meaningful tests, not a coverage number.

---

## 11. Timeline (part-time, 4–5 weeks)

| Week | Deliverable |
|---|---|
| 1 | Solution scaffold, Domain entities, DbContext, first migrations, MenuItem CRUD, POS screen functional (order → pay → save) |
| 2 | Bus module: companies, trip quick-log, crew meal mode on POS, monthly report |
| 3 | Tray inventory: production entry, availability on POS, waste log, variance view. Expense module: entry, categories, expense report |
| 4 | Analytics dashboard (KPIs, charts, insight callouts), daily closing with net, auth/roles, seeder with 30-day history incl. expenses |
| 5 | Polish: keyboard shortcuts, empty states, Dockerfile, deploy, README (screens, GIF, architecture diagram), docs/architecture.md |

Each week maps to one scoped agent prompt (per your working pattern: one prompt per stage, review summary, flag issues).

---

## 12. README Skeleton

```
# Kaye & DM BMS
Management system for a provincial bus meal stop in Sorsogon, PH.
Ground-up rebuild of my VB.NET/.NET Framework capstone in ASP.NET Core 8.

[Live demo] · [45s demo GIF]

## The domain (why this isn't a POS tutorial)
- Demand in waves (bus arrivals) · crew meals as business obligation · tray-based inventory

## Stack
ASP.NET Core 8 · Blazor Server · EF Core 8 · SQL Server · Docker

## Architecture
(diagram) — see docs/architecture.md

## Highlights
- POS designed for 6 orders/minute rush handling
- Crew meal crediting with per-company allowance rules (domain-tested)
- Expense tracking with daily net-profit closing snapshots
- Analytics dashboard: sales-by-hour with bus-arrival overlay, revenue vs. expenses trend, per-bus-line sales, rule-based insight callouts
- 10+ real EF Core migrations kept as schema history

## Run it
docker compose up · seeded logins: owner@… / cashier@…
```

Frame the legacy→modern rebuild explicitly — it mirrors the modernization work enterprise .NET shops actually do.

---

## 13. Out of Scope (say no to these)

- Offline-first / PWA
- Hardware (printers, drawers, scanners)
- Real payment gateways
- Multi-branch / multi-tenant
- Ingredient-level inventory & recipes
- Employee scheduling/payroll
- Mobile app

If it doesn't serve the 45-second demo or an interview question, it's cut.
