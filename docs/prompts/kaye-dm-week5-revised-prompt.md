# Kaye & DM BMS — Week 5 Prompt (REVISED — supersedes the Week 5 section in kaye-dm-agent-prompts-weeks-1-5.md)

Attach alongside: `docs/kaye-dm-bms-blueprint.md` and `docs/prompts/design-system-spec.md`. The design spec governs every visual decision in this week — treat it as law.

---

## Context

Final week of **Kaye & DM BMS**. Weeks 1–4.5 complete: full feature set, auth/roles, analytics dashboard, seeder, and the new app shell (Route Blue sidebar, design tokens, shared components) built against the design-system spec. **This week: reports consolidation, full design-system application across every remaining page, polish, Docker, and docs. No new entities, no schema changes** (corrective migrations only for genuine bugs).

## Current state (do not redo)

App shell, sidebar, login, access-denied, POS, and dashboard already carry the full design treatment from Week 4.5. Design tokens, fonts, and shared components (PageHeader, RouteDivider, badges, empty states, toasts) exist. Remaining CRUD/report pages have tokens applied but layouts unchanged. Migration history intact — never touch it. Packages pinned (8.0.11 + chart lib); no new packages.

## Scope — ONLY this

### 1. Reports module (consolidation, not a rewrite)
- New Owner-only **Reports** nav group between Expenses and Menu: Overview (`/reports`) · Crew Meals · Variance · Expenses · Daily Closings.
- **Move** the three existing report NavLinks out of Buses/Inventory/Expenses groups — Reports becomes their single navigation home. Do NOT move or refactor the report pages, routes, or services themselves.
- **`/reports` landing page:** card grid using the design-system card pattern — one card per report with a small glyph, name, one-line description in the interface voice ("Monthly crew meals owed per bus company"), Route Line divider, whole card clickable with hover elevation.
- **`/closing/history` (new page):** table of past Daily Closings — date, sales, expenses, net (green/coral by sign), closed-by; row click opens the snapshot detail. Reuses existing DailyClosing data and services; read-only.

### 2. Report page redesign — "fixed frame, scrolling data"
Apply to `/buses/report`, `/inventory/variance`, `/expenses/report`, `/closing/history`:
- Layout: PageHeader + filter bar (sticky at top of content), then a **summary strip** of 2–4 KPI-style cards (e.g. expense report: total spent, top category, entry count; crew report: total meals, meals by role; variance: total waste %, worst dish), then the data table in a **fixed-height scrollable region** (`max-height` sized so header + filters + summary + table frame fit a 1366×768 viewport with zero page scroll; the table scrolls internally with a sticky header row).
- Filters apply without full page reload; result region does a quick fade (--t-base) on refresh.
- Money right-aligned in Plex Mono, tabular numerals everywhere, totals row pinned at the bottom of the scroll region where the report has one.
- Empty states per the design system when a filter returns nothing.

### 3. Full design-system application to ALL remaining pages
Every page not already treated in Week 4.5 — `/menu`, `/buses/companies`, `/buses/arrivals`, `/inventory/production`, `/inventory/waste`, `/expenses`, `/expenses/categories`, `/closing` — gets the complete treatment:
- No default-Bootstrap-looking surfaces anywhere. Every list uses the design-system table spec; every form uses the form spec (labels above, focus rings, coral validation); every action uses the button spec with pending states; status values use the badge pills.
- Each page: PageHeader with title + primary action slot, correct empty state, consistent spacing from the token scale.
- Forms in dialogs or panels get the card treatment; destructive actions get a confirm step in coral.

### 4. Motion pass — responsive, never ambient
- Interactions respond: row hover tint, clickable-card lift, button press scale, filter-apply fade, nav behavior as already built.
- Numbers in summary strips count up once on first load (same 600ms pattern as the dashboard); no re-animation on filter change — values swap with the fade only.
- Nothing moves without user input. No looping/ambient animation, no scroll-triggered effects. `prefers-reduced-motion` zeroes everything.

### 5. POS final polish
- Keyboard shortcuts: F1–F5 category tabs, Enter completes payment when valid, Esc clears ticket (confirm if lines exist). Visible hint row for shortcuts.
- Double-submit guards verified on every async button app-wide.

### 6. Error handling
- Global Blazor error boundary with a friendly, design-system-styled message; domain exceptions surface as inline alerts; closed-date rejections name the specific rule.

### 7. Docker
- Multi-stage `Dockerfile` for KayeDM.Web; `docker-compose.yml` with app + `mcr.microsoft.com/mssql/server:2022-latest`, healthcheck, `SEED_ON_START=true` env flag triggering auto-migrate + auto-seed on first run, connection string via env var. Must work from a clean clone with a single `docker compose up`.

### 8. Docs
- **README.md** per blueprint §12: domain story (waves, crew meals, trays), stack, Mermaid architecture diagram, highlights, screenshot placeholders, `docker compose up` instructions + seeded logins, migration-history-as-feature note, VB.NET-rebuild framing.
- **docs/architecture.md:** layering diagram + ADR-lite bullets (Blazor Server, no pattern libraries, tray inventory model, migration policy, oversell override, client-side sum trade-off, fixed-frame report layout).
- **docs/demo-script.md:** the 45-second demo flow as numbered steps, updated for the new shell/Reports flow: bus arrives → 6 rapid orders → crew meal → owner: dashboard → Reports → close day.

### 9. Verification
- Playwright screenshots: every page at 1366×768 and one mobile-width shell shot; both roles walked end to end; confirm zero page-level scroll on the four report pages at 1366×768 with seeded data; full suite green; `docker compose up` verified from a clean clone.

## Hard constraints

- Design-system spec is law: tokens only, no new colors/fonts/sizes outside it (add to tokens first if genuinely missing). No Tailwind, no component libraries, no JS animation libraries, no new NuGet packages (Docker needs none).
- No schema changes; corrective migrations only for real bugs, never edits to existing migrations. Never touch the seeder's fixed-seed logic.
- Branch `week-5-reports-polish`, PR after review, no squash, no commits to main while open.
- Screenshots at every checkpoint — reviews are visual, not descriptive.

## Checkpoints (stop for review with screenshots)

1. Reports group + `/reports` landing + `/closing/history` working
2. One report page fully converted to fixed-frame layout (I approve the pattern before it propagates to the other three)
3. All remaining pages design-system complete
4. Docker + docs done, final walkthrough

## Deliverable format

Same 5-point format as prior weeks, plus the screenshot set per checkpoint.
