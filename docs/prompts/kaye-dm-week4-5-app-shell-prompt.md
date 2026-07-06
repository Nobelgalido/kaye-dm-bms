# Kaye & DM BMS — Week 4.5 Prompt: App Shell, Sidebar Navigation & Login Redirect

Paste this AFTER the Week 4 scope (auth, closing, dashboard, seeder) is complete, reviewed, and merged. Attach `docs/kaye-dm-bms-blueprint.md` as usual.

---

## Context

Continuing **Kaye & DM BMS** (blueprint attached). Weeks 1–4 are complete: full feature set, ASP.NET Core Identity with `Owner`/`Cashier` roles, analytics dashboard, daily closing, seeder. This task restructures the app shell: role-based post-login landing and a grouped, collapsible sidebar. **UI/navigation work only — no schema changes, no migrations, no new features, no new packages.**

## Current state (do not redo)

All Week 1–4 deliverables. Migration history intact (do not touch it). Auth works with seeded `owner@kayedm.local` / `cashier@kayedm.local`. Logo assets exist in `wwwroot` (`favicon.ico`, `img/kaye-dm-logo-3d-64.png` and other sizes) — if they are not yet referenced anywhere, wiring them into the shell is in scope here.

## Scope — ONLY this

### 1. Role-based login redirect
- After successful sign-in: `Owner` → `/dashboard`, `Cashier` → `/pos`.
- Navigating to `/` while authenticated applies the same role-based redirect; unauthenticated → login page.
- A Cashier who manually enters an Owner-only URL gets a friendly access-denied view with a link back to `/pos` — not a blank page or raw 403.

### 2. Sidebar navigation (replace the default template nav)
Structure, in this order:

- **Header:** 64px logo (`img/kaye-dm-logo-3d-64.png`) + "Kaye & DM" wordmark, links to the user's role home.
- **Dashboard** — single item, Owner only
- **Sales** (group): POS · Daily Closing (Closing is Owner only)
- **Buses** (group): Arrivals · Companies (Owner) · Crew Meal Report (Owner)
- **Inventory** (group): Production · Waste · Variance (Owner)
- **Expenses** (group, Owner only): Entry · Categories · Report
- **Menu** — single item, Owner only
- **Footer:** current user display name + role badge + logout button.

Behavior requirements:
- Groups are collapsible: clicking the group header toggles its submenu, chevron icon rotates. Implement as a reusable `NavGroup` Blazor component with an `expanded` state and CSS max-height transition. **Pure Blazor + CSS — no JS interop, no Bootstrap accordion JS.**
- **Auto-expand the group containing the active route** on load and on navigation (match `NavigationManager.Uri` against the group's route prefixes). The current page must never be hidden inside a collapsed group.
- Active item highlighting via `NavLink`'s `active` class: left border marker + tinted background using the logo blue as the accent color.
- **Role gating at the nav level** with `AuthorizeView`: Cashier sees only POS, Arrivals, Production, Waste (plus header/footer). Never render links the user cannot open. Page-level authorization stays in place as the real enforcement — nav hiding is UX, not security.
- Sidebar is fixed on desktop widths; below ~900px it collapses to a hamburger toggle. Keep this simple — a CSS class toggle on a button, still no JS interop.

### 3. Visual pass on the shell only
- Consistent page header component (title + optional action button slot) used by all pages — if pages already have ad-hoc headers, unify them.
- Color usage: logo blue as primary accent (active states, buttons), yellow only for small accents (badges, hover ticks) — never as a large surface color.
- Favicon wired in the host page head; app name in the browser title as "Page — Kaye & DM BMS".
- Do not restyle page content beyond the shared header component — the full UI consistency pass is Week 5's job.

### 4. Verification
- Playwright (or manual) walkthrough as BOTH seeded users: login redirect lands correctly per role; Cashier's sidebar shows exactly the four allowed items; Cashier hitting `/dashboard` by URL gets the friendly denied view; active group auto-expands on `/expenses/report`; mobile hamburger toggles.
- Full test suite still green; build clean.

## Hard constraints

- **No migrations, no schema changes, no new NuGet packages.** If you believe one is unavoidable, stop and ask before acting.
- Pure Blazor + CSS for all interactivity in this task.
- Never edit existing migrations. Never touch the seeder logic.
- Commit in logical steps on a branch `week-4-5-app-shell`, merge via PR after my review, no squash.

## Deliverable format

1. Files created/modified, grouped by project
2. Screenshot-equivalent description of the sidebar for each role (or Playwright screenshots if available)
3. Deviations + why
4. How to run and manually verify both role flows
5. Anything uncertain, flagged explicitly — do not guess on role visibility rules
