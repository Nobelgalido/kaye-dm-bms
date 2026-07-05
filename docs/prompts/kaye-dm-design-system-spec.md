# Kaye & DM BMS — Design System & UI Redesign Spec

Companion to `week4-5-app-shell.md`. The app-shell prompt defines navigation STRUCTURE and BEHAVIOR; this file defines the VISUAL SYSTEM every page must follow. Where the two overlap, behavior rules come from the shell prompt and visual rules come from here.

---

## Design thesis

**Filipino highway signage.** Kaye & DM is a provincial bus meal stop; its visual world is the highway — signboards, route markers, road paint, the badge on the side of a bus. The app should read as clear, confident signage: instantly legible in a rush, warm without being cute. Not a generic SaaS dashboard.

The brand source of truth is the logo (yellow octagon badge, royal-blue bus, coral script, white cutlery). Every token below derives from it.

**This is a working POS.** Design serves speed. If a visual choice slows a cashier down or delays feedback, it's wrong regardless of how it looks.

---

## 1. Design tokens (implement as CSS custom properties in `app.css`)

### Color

```css
:root {
  /* Brand */
  --route-blue:        #1E40AF;  /* primary — logo bus blue */
  --route-blue-deep:   #14276B;  /* sidebar, dark surfaces */
  --route-blue-tint:   #E8EDFB;  /* selected/hover tints, chart fills */
  --signal-yellow:     #FFC933;  /* accents ONLY: active marker, badges, warnings */
  --sili-coral:        #EF5B4C;  /* destructive, alerts, the "mealstop" voice */
  --palay-green:       #2E8B57;  /* success, positive deltas */

  /* Neutrals */
  --ink:               #1C2333;  /* primary text */
  --ink-soft:          #5A6478;  /* secondary text, labels */
  --paper:             #FAFAF7;  /* app background — warm off-white */
  --surface:           #FFFFFF;  /* cards, panels */
  --line:              #E4E4DC;  /* borders, dividers */

  /* Semantic aliases */
  --danger: var(--sili-coral);
  --success: var(--palay-green);
  --warning-bg: #FFF7DE;
  --danger-bg:  #FDEBE9;
  --success-bg: #E9F5EE;
}
```

Rules:
- Yellow is an ACCENT, never a surface. No yellow cards, no yellow page sections. Its jobs: the active-nav route marker, count badges, warning highlights, the oversell-confirm accent.
- Coral is reserved for destructive/alert semantics + tiny brand moments (one word in the login page tagline may use it). Never for decoration.
- Charts: sales/revenue series = route blue; expenses = coral; net = green; bus-arrival markers = signal yellow.

### Typography

```css
--font-display: 'Archivo', system-ui, sans-serif;   /* headings, KPI numbers — signage DNA */
--font-body:    'Figtree', system-ui, sans-serif;   /* everything else */
--font-mono:    'IBM Plex Mono', monospace;         /* order numbers, receipt view, money in tables */
```

Load via Google Fonts in the host page (weights: Archivo 600/700/800, Figtree 400/500/600, Plex Mono 500). If offline dev is a concern, self-host the woff2 files in `wwwroot/fonts`.

Scale (rem): 0.75 caption · 0.875 body-sm · 1.0 body · 1.125 h4 · 1.375 h3 · 1.75 h2 · 2.25 h1 · 3.0 KPI.

Rules:
- Page titles: Archivo 700, tight letter-spacing (-0.01em), `--ink`.
- Section labels/eyebrows: Figtree 600, 0.75rem, uppercase, letter-spacing 0.08em, `--ink-soft`.
- ALL numeric money/quantity displays: `font-variant-numeric: tabular-nums` so columns and tickers don't jitter. KPI numbers use Archivo 800; table money uses Plex Mono 500.
- Body text is never lighter than 400 and never below 0.875rem — this app runs on a mid-range laptop in a bright canteen.

### Space, radius, elevation

```css
/* 4px base scale */
--s-1: 4px; --s-2: 8px; --s-3: 12px; --s-4: 16px; --s-5: 24px; --s-6: 32px; --s-7: 48px;

--r-sm: 8px;   /* buttons, inputs */
--r-md: 12px;  /* cards, panels */
--r-full: 999px; /* badges, pills */

--shadow-1: 0 1px 2px rgb(20 39 107 / 0.06), 0 1px 3px rgb(20 39 107 / 0.08);
--shadow-2: 0 4px 12px rgb(20 39 107 / 0.10), 0 2px 4px rgb(20 39 107 / 0.06);
```

Rules:
- Page content: max-width 1240px, padding `--s-6` desktop / `--s-4` mobile.
- Card interiors: `--s-5`. Gap between cards: `--s-5`. Vertical rhythm between page sections: `--s-6`.
- Never mix arbitrary pixel values — every margin/padding comes from the scale.

### Motion

```css
--t-fast: 120ms;  /* press feedback, toggles */
--t-base: 180ms;  /* hovers, expands, fades */
--ease: cubic-bezier(0.2, 0, 0, 1);
```

Rules:
- Buttons: press = scale(0.97) at `--t-fast`; hover = background shift only. POS grid buttons must respond on `:active` INSTANTLY — no transition delay on the way down.
- Nav group expand/collapse: max-height + chevron rotate at `--t-base`.
- Page content: single fade-up on route change (opacity 0→1, translateY 6px→0, `--t-base`). No per-element stagger cascades — this is an admin tool.
- Dashboard: KPI numbers count up once on load (600ms, ease-out) — this is the app's ONE orchestrated moment. Charts appear behind skeleton shimmer placeholders, no bounce.
- Wrap ALL motion in `@media (prefers-reduced-motion: reduce) { ... }` overrides that zero the transitions and skip the count-up.
- Nothing animates on scroll. No parallax, no floating shapes, no gradient orbs.

---

## 2. Signature element — the Route Line

A dashed line evoking road lane markings. It is the app's identity thread, used in exactly these places and nowhere else:

1. **Active nav item:** a 3px `--signal-yellow` vertical dashed line on the left edge of the active `NavLink` (CSS: `border-image` or a repeating-linear-gradient pseudo-element), plus `--route-blue-tint`… no — sidebar is dark, so active item gets a subtle lighter-blue fill + the yellow dashed marker.
2. **Section dividers** on the dashboard and reports: a horizontal 2px dashed `--line` rule with a small route-blue dot at the left terminus.
3. **Bus-arrival markers** on the sales-by-hour chart: vertical dashed yellow lines with a tiny bus glyph label — the chart moment that ties the whole thesis together.
4. **The login card:** one dashed route line runs beneath the logo, coral dot at its end, like a route map "you are here."

Implementation: one `.route-line` utility class + one `RouteDivider.razor` component. Do not scatter dashes anywhere else.

---

## 3. Component specs

**Sidebar (visual layer over the shell prompt's structure):** background `--route-blue-deep`; text rgba(255,255,255,0.78); group headers Figtree 600 0.75rem uppercase rgba(255,255,255,0.45); active item = white text + rgba(255,255,255,0.08) fill + yellow route-line marker; hover = rgba(255,255,255,0.06). Logo block at top on a slightly darker strip. Footer: user chip with role badge (`Owner` = yellow pill w/ dark text, `Cashier` = blue-tint pill).

**Buttons:** primary = `--route-blue` bg, white text, `--r-sm`; hover darkens 6%; secondary = white bg, `--line` border, `--ink`; destructive = coral. Height 40px standard, 56px+ for POS grid. Disabled = 45% opacity + not-allowed cursor. Every async action button shows an inline spinner + disables while pending (double-submit guard is a design rule, not just logic).

**Cards:** `--surface`, `--r-md`, `--shadow-1`, 1px `--line` border. Hover elevation (`--shadow-2`) ONLY on cards that are actually clickable.

**KPI cards (dashboard):** eyebrow label, Archivo 800 number (tabular), delta chip below (green ↑ / coral ↓ with sign). Net-profit KPI gets a route-blue left border 4px — it's the number the owner cares about.

**Tables:** header row = paper bg, eyebrow-style labels; rows 48px, `--line` bottom borders only (no vertical grid); money columns right-aligned in Plex Mono; row hover `--route-blue-tint` at 40%; sticky header on scrollable reports.

**Forms:** labels above inputs (Figtree 600 0.875rem); inputs 40px, `--r-sm`, `--line` border, focus = 2px route-blue ring (`outline-offset: 2px` — keyboard focus must be visible app-wide); validation errors in coral text below the field, input border goes coral; never rely on color alone — include the message.

**Badges/status:** pills (`--r-full`): Completed = green-bg/green text; Voided = coral; Crew Meal = yellow-bg/ink; Closed day = ink-bg/white.

**Toasts:** bottom-right, `--surface`, left border 4px in semantic color, auto-dismiss 4s, name the action ("Day closed", "Order #20260705-041 saved").

**Empty states:** every list/table gets one — small line-art glyph (reuse cutlery/bus motifs from the logo where sensible), one sentence naming the action ("No trips logged yet — log the first arrival when a bus pulls in"), and the primary action button. No sad-face illustrations.

**POS screen (highest-stakes surface):** menu grid buttons ≥ 96px tall, dish name in Figtree 600 + price in Plex Mono, category color-coded by a 4px top border (rotate blue/green/coral/yellow/ink-soft per category); availability count as a corner pill (yellow ≤5, coral = 0/oversell); ticket panel on `--surface` with sticky total footer (Archivo 800, tabular); tendered quick-buttons as secondary buttons; change display large and green when positive. Crew-meal mode flips the ticket header to a yellow banner "CREW MEAL — DLTB Bus 8112" so the state is unmissable.

**Login page:** centered card on `--paper`, 3D logo (192px asset), app name in Archivo 700, the route-line signature beneath, two inputs + one button. Optional small text: "Kaye & DM mealstop · Sorsogon" with "mealstop" in coral Figtree — the one permitted brand-voice moment.

**Access-denied page (from shell prompt):** same empty-state pattern — glyph, "This area is for the owner", button "Back to POS".

---

## 4. Scope & execution order

Full treatment (rebuild the markup as needed): app shell/sidebar, login, access-denied, POS, dashboard, daily closing.
Token treatment (apply tokens + shared components, don't redesign layout): all CRUD/report pages — they inherit the system via shared `PageHeader`, card, table, form, badge, and empty-state styles.

Order: tokens + fonts in `app.css` → shared components (PageHeader, RouteDivider, badges, empty state, toast) → shell/sidebar → login + access-denied → POS → dashboard/closing → token pass over remaining pages.

## 5. Hard constraints

- Pure CSS + Blazor. No Tailwind, no component libraries (MudBlazor etc.), no JS animation libraries. Remove or neutralize Bootstrap's default component styles where they conflict; keeping its grid utilities is acceptable.
- No schema changes, no migrations, no new NuGet packages. Google Fonts link or self-hosted woff2 only.
- Every interactive element: visible keyboard focus ring, hit target ≥ 40px (≥ 56px on POS), AA contrast minimum (check yellow-on-white pairings — yellow text on white is FORBIDDEN, yellow is bg-accent only with ink text).
- `prefers-reduced-motion` respected globally.
- Verify with screenshots at 1366×768 (canteen laptop), 1920×1080, and ~390px mobile width for the hamburger shell. The POS must remain fully usable at 1366×768 with no scrolling of the payment strip.
- Do not invent new colors, sizes, or fonts outside the tokens. If a needed value is missing, add it to the token block first, then use it.
