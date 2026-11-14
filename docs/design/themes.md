# Design note — Phase 20: Themes and application workspaces

**Scope.** SPEC-ADDENDUM §C (C1–C5, wins over SPEC.md): three themes (Classic / Enterprise / Modern) × light / dark × Arabic / English,
admin-controlled in **Settings → Appearance**; application workspaces (app launcher, one workspace per system with its own header, tabs,
home page and accent colour) in Enterprise and Modern; Classic unchanged. Related: CLAUDE.md §10 (localisation, RTL, UX), Phase 0 (design
tokens, app shell), Phase 17 (system dashboards, My Work cards), Phase 19 (global search, `command-search`), `docs/accessibility.md`.
ADRs **0170–0175**.

## 1. Principles

1. **One component library, one set of screens.** A theme is a *token set* (CSS custom properties) plus *layout slots* chosen by the
   shell and by a handful of shared layout components. No page is written twice; no page tests for a theme name except through the shared
   components listed in §4.
2. **Classic is the reference.** Classic's tokens are today's tokens byte for byte; the Classic shell is today's `Sidebar` + `TopBar`. The
   Phase 0–19 screenshots and E2E specs stay valid in Classic, which remains the default theme of a new or upgraded installation.
3. **Routes never change.** Workspaces are derived from the URL (`/portfolio/...` is the Portfolio app). A deep link opens the same page in
   every theme; only the frame around it differs (ADR 0172).
4. **No third-party branding.** The Enterprise theme borrows *working patterns* familiar to users of large CRM suites (highlights panel,
   path bar, tabs per app, related lists, utility bar). It uses Lucide icons and IBM Plex Sans / IBM Plex Sans Arabic, its own colours, and
   is called "Enterprise" (AR «المؤسسي») everywhere. No vendor name, logo, icon set or font appears in code, strings or docs shipped
   with the product.
5. **Accessibility is per theme.** Every token set meets WCAG 2.1 AA contrast (text 4.5 : 1, UI parts 3 : 1) in light and dark; the axe
   audit runs in all three themes (§9).

## 2. Theme and mode resolution (ADR 0171)

Settings area `appearance` (typed `AppearanceSettings`, one JSON document like Branding):

| Field | Default | Meaning |
|---|---|---|
| `enabledThemes` | `["classic","enterprise","modern"]` | Themes users may be given. Must contain the default theme; at least one |
| `defaultTheme` | `classic` | Theme of everyone who has no (allowed) choice, and of the sign-in pages |
| `defaultMode` | `system` | `light` / `dark` / `system` |
| `allowUserTheme` | **false** | "Allow users to choose their theme" (C1) |
| `allowUserMode` | true | Users may pick light / dark / system (today's behaviour; an accessibility preference — off forces `defaultMode`) |
| `landingRules` | `[]` | Ordered `{ roleCode, app }` rules (§6) |
| `apps` | `{}` | Per-app overrides (§5.3) |

Resolution (pure `AppearanceResolver` in `Strategy.Platform.Settings`, unit-tested table; the server is the only place it runs):

```
theme = allowUserTheme && user.uiTheme ∈ enabledThemes ? user.uiTheme : defaultTheme
mode  = allowUserMode  && user.theme   ∈ {light,dark,system} ? user.theme : defaultMode
```

- `GET /api/v1/identity/me` returns `appearance: { theme, mode, themeChoice, landingApp, favouriteApps }` — the **effective** theme and mode.
- `PUT /api/v1/identity/me/preferences` accepts `uiTheme`, `landingApp`, `favouriteApps` beside `language` / `theme` (mode). A `uiTheme`
  is refused with 422 `appearance.errors.themeChoiceOff` when choosing is off, `validation.unknownCode` when the theme is not enabled;
  a mode is refused when `allowUserMode` is off. A user's stored choice is kept when an admin later turns choosing off (it applies again if
  turned back on) but is ignored by the resolver meanwhile.
- `GET /api/v1/settings/public` adds `defaultTheme` and `defaultMode` (sign-in pages, setup wizard, before `me` loads).
- `GET /api/v1/settings/appearance` (any signed-in user: the shell needs the app overrides and landing rules) and
  `PUT /api/v1/settings/appearance` (`settings.general.manage`, versioned, 409 on conflict, audited as a settings change like Branding).
- Browser: `<html data-theme="classic|enterprise|modern">` + the existing `dark` class. The last effective values are cached in
  `localStorage` (`strategy.uiTheme`) so a reload does not flash Classic before `me` arrives; the server answer always wins.

## 3. Design tokens per theme (ADR 0170)

All tokens live in `web/src/styles/index.css`. `:root` / `.dark` stay the Classic values; `[data-theme='enterprise']`,
`[data-theme='enterprise'].dark`, `[data-theme='modern']`, `[data-theme='modern'].dark` override them. Components use only semantic
tokens (`bg-card`, `text-muted-foreground`, `rounded-lg`, `shadow-[var(--shadow-card)]`, `h-[var(--control-h)]`…).

### 3.1 Colour (light / dark; oklch)

| Token | Classic (unchanged) | Enterprise | Modern |
|---|---|---|---|
| `--background` | 0.985 / 0.17 | cool grey 0.955 0.006 250 / 0.16 0.015 255 | warm paper 0.985 0.004 85 / 0.165 0.012 260 |
| `--card` | white / 0.21 | white / 0.205 0.015 255 | white / 0.205 0.012 260 |
| `--border` | 0.91 / 0.30 | 0.86 0.008 250 / 0.32 | 0.92 0.006 85 / 0.29 |
| `--primary` (fallback when no branding colour) | teal 0.48 0.09 186 / 0.70 | blue 0.48 0.15 258 / 0.72 0.13 255 | indigo-teal 0.47 0.1 200 / 0.74 0.1 195 |
| `--sidebar` | 0.99 / 0.19 | navy app bar 0.30 0.06 258 / 0.14 | 0.975 0.006 85 / 0.18 |
| `--header` (new: workspace header surface) | = card | white / 0.205 | = background |
| status `--success/warning/danger/info` + `*-soft` | unchanged | Classic values (already AA) | Classic values, softer `*-soft` backgrounds checked ≥ 4.5 : 1 |

Organisation branding (`--brand-primary`) replaces `--primary` in every theme (C1). Per-app accent `--app-accent` (§5) is set on the
workspace and used only for decoration that is not text-on-colour except the app icon tile (white icon, accents chosen ≥ 3 : 1 with white;
an admin-chosen accent below 3 : 1 gets a warning on the Appearance screen, like the branding colour).

### 3.2 Typography

| | Classic | Enterprise | Modern |
|---|---|---|---|
| Body font | IBM Plex Sans / IBM Plex Sans Arabic (Arabic first in `ar`) | same | same |
| Headings (`--font-heading`) | body font | body font | **Noto Kufi Arabic** in Arabic (OFL), IBM Plex Sans in English |
| Root size | 16 px | **15 px** (compact; every rem-based size shrinks ≈ 6 %) | 16 px |
| Line height (body) | 1.5 | 1.45 | 1.6 (1.75 in Arabic) |
| Page title | 1.5 rem semibold | 1.25 rem semibold | 1.625 rem semibold |

### 3.3 Spacing, radius, elevation, density

| Token | Classic | Enterprise | Modern |
|---|---|---|---|
| `--radius` | 0.625 rem | 0.25 rem | 0.875 rem |
| `--shadow-card` | none (border only) | 0 1px 2px rgb(0 0 0 / .08) | 0 1px 2px rgb(0 0 0 / .04), 0 4px 16px rgb(0 0 0 / .05) |
| `--control-h` / `--control-h-sm` | 2.5 / 2 rem | 2.125 / 1.75 rem | 2.5 / 2 rem |
| `--cell-py` (table rows) | 0.75 rem | 0.4 rem | 0.875 rem |
| `--page-gap` (main padding) | 1.5–2 rem | 1 rem | 2–2.5 rem |
| Density | comfortable | **compact** | spacious-efficient |
| Motion | none added | none | 150–200 ms ease-out on panels, palette and tiles; **off** under `prefers-reduced-motion` |

## 4. Layout variants and slots

| Slot | Classic | Enterprise | Modern |
|---|---|---|---|
| **Navigation** (`AppShell`) | single sidebar with every system (today) | top bar with **app launcher** (grid icon) + app name; **horizontal tab bar of the current app**; utility bar at the bottom | top bar with launcher; **collapsible sidebar inside the current app**: app tabs, favourites (pinned apps), recent items; collapsed state remembered |
| **Global search** | top-bar box (Phase 19) | top-bar box | top-bar button + **command palette (Ctrl+K)**: records (Phase 19 search), pages (apps and tabs the user can open), actions (new request / risk / task…, switch language, mode, theme when allowed) |
| **Record page** (`RecordLayout`, ADR 0221) | stack: header · stepper · status banner · summary · **side menu of sections** + the active section | **highlights panel** (title, chips, key fields, primary actions) · **path bar** (chevron stages with "current stage" guidance) · status banner · **side menu** (top accent bar, accent marker on the active section) + the active section in a card | **card layout**: header card, stepper inside a card, status, summary · **side menu** (rounded, accent marker) with the **key facts card** under it + the active section |
| **Record sections** (`RecordSideMenu`, ADR 0221) — all themes | One menu, no header tab strip and no separate related lists: every section in the page's order with its count, `aria-current="page"` on the active one, arrow keys / Home / End, one Tab stop. Clicking writes `?tab=<key>` (router schemas unchanged). From `md` up it sits at the inline start (12 rem, 14 rem from `lg`); **below `md` (tested at 360 px) it is a drawer** (Radix Dialog sheet from the inline-start edge) opened by the **Sections** button in the record header, which names the current section; choosing a section closes it and focus returns to the button | | |
| **List page** (`SplitView`) | table (today) | table; **split view** toggle (list + preview) | **split view by default** on wide screens (list + preview card), table below `lg` |
| **Page header** (`PageHeader`) | today | compact, on a white highlights strip | larger title in the heading font |
| **Tabs** (`TabsList`) — tab sets inside a section and on non-record pages | underline tabs | boxed tabs with a top accent bar | pill tabs on a muted track |
| **Stage stepper** | circles and lines | **path**: chevron segments, completed / current / upcoming | circles with a filled progress track |
| **Cards / tiles** | bordered | bordered + light shadow, square corners | rounded, soft shadow, generous padding |
| **Utility bar** | — | bottom bar: *Recent items*, *Quick note* (drafts in the browser only) | — |

Implementation: `useAppearance()` exposes `{ theme, mode, layout }`. Only these shared components read it: `AppShell`, `RecordLayout`,
`RecordSideMenu`, `SplitView`, `StageStepper`, `TabsList`, `PageHeader` (and the launcher / palette themselves). Everything else follows the
tokens. Pages opt into the richer layouts by using `RecordLayout` / `SplitView`. Since release 1.0.2 every record page (17) uses
`RecordLayout` with `sections` (`{ key, label, count?, hidden?, testId?, content }`), `activeSection` and `onSectionChange` (ADR 0221); a new
record page does the same — there is no tab-strip variant.

## 5. Application workspaces (ADR 0172)

### 5.1 App registry (code: `web/src/layout/apps.ts`)

One definition per app: `code`, `system` (access gate, may be empty for shared apps), `labelKey`, `descriptionKey`, default `icon`
(Lucide), default `accent`, `basePaths` (URL prefixes that belong to the app), `tabs` (each `{ key, to, labelKey, permissions }`, the same
entries as the Classic sidebar — one source of truth with `navigation.ts`), `home` (`/apps/<code>`), `dashboard` (Phase 17 system
dashboard code), `quickActions` (`{ key, to, labelKey, permissions }`).

| App | System | Tabs (in default order) | Home dashboard |
|---|---|---|---|
| My Work (shared) | — | My Work, Approvals, Notifications | — (My Work page) |
| Dashboards (shared) | — | Executive, Dashboards, Reports | executive |
| Portfolio | portfolio | Home, Requests, Projects, Dashboard, Gantt, Portfolios, Vendors, Capacity, Reviews | `portfolio.overview` |
| KPI | kpi | Home, Dashboard, Cards, Pending, Approvals, Scorecard, Strategy map, Reports | `kpi.scorecard` |
| Risk | risk | Home, Dashboard, Register, My actions, Project risks, Mechanism, Reports | `risk.profile` |
| Teams (shared) | teams | Home, Teams, Dashboard | `teams.overview` |
| Tasks (shared) | tasks | Home, Tasks, Dashboard | `tasks.performance` |
| Requests | requests | Home, Requests, My requests, Queue | — |
| Document Registry | docreg | Home, Documents, Dashboard | `docreg.library` |
| IP Registry | ip | Home, Register, Dashboard | `ip.register` |
| Benchmarking | bench | Home, Dashboard, Plan, Studies, Practices, Comparators | `bench.plan` |
| CAP | cap | Home, Dashboard, Remarks, My actions, Sources, Suggestions | `cap.closure` |
| Audit Management | auditmgmt | Home, Dashboard, Findings, Plans, Universe, Calendar, Reports, My department | `auditmgmt.closure` |
| Organisation (shared) | — | Units, Chart, Employees, Levels, Import | — |
| Settings (shared, admins) | — | Settings, Audit log, Chain verification | — |
| Sample | sample | Sample | — |

**Visibility.** An app is in the launcher when the user may open its system (`/api/v1/access/me` `systems`) **and** holds the permission
of at least one of its tabs; tabs are filtered the same way (the existing `isVisible`). The server still checks every call (CLAUDE.md §7).

**Current app.** The longest `basePaths` prefix of the URL; pages outside every app (profile, notifications, audit log for non-admins…)
keep the last app's frame, or My Work.

### 5.2 Workspace home page `/apps/$app`

Header (icon tile in the accent colour, display name, description, favourite star) · **quick actions** (only those the user may do) ·
**my items in this system** (My Work approvals, action items and submissions filtered by `systemCode`) · **system dashboard** (the Phase
17 dashboard of the app's `dashboard` code, rendered by the existing `DashboardPage` in embedded mode; hidden without
`reporting.dashboard.read`). Home pages exist in every theme (Classic users can open `/apps/kpi` from a deep link) but Classic's
navigation never links to them.

### 5.3 Per-system appearance settings (Settings → Appearance → Applications)

Per app: display name AR / EN (empty = default translation), icon (from a curated list of ~40 Lucide icons, stored as a code), accent
colour (`#rrggbb`), tab order and hidden tabs (by tab key; unknown keys ignored, new tabs appended), home dashboard (any dashboard code the
user may read; empty = default). Overrides never grant access: a hidden tab's page remains reachable by URL if the user has the permission,
exactly as in Classic.

### 5.4 App launcher

Grid button in the top bar (Enterprise, Modern). Dialog with a search box (filters by name in both languages), **Favourites** first, then
**Apps** (systems), then **Shared** (My Work, Dashboards, Tasks, Teams, Organisation, Settings). Each tile: icon on accent, name, one-line
description, star toggle (saved to the profile). Arrow keys move between tiles, Enter opens, Escape closes; at 360 px a single column.

## 6. Landing app (ADR 0173)

Applies when a signed-in user opens `/` in Enterprise or Modern (Classic keeps My Work at `/`, unchanged):

1. the user's own `landingApp` (profile) if they can open that app;
2. else the **first** admin rule (Settings → Appearance → Landing app per role) whose `roleCode` the user holds
   (`/api/v1/access/me` `roles`) and whose app they can open;
3. else **My Work** (today's home page).

The setup redirect for administrators (ADR 0150) still runs first. `/` with `?home=1` (the *My Work* app) always shows My Work.
Pure function `resolveLandingApp(...)` (Vitest table); saved codes are validated on the server against the app codes list.

## 7. Settings → Appearance (`/settings/appearance`, `settings.general.manage`)

Sections: **Themes** (enable each theme — Classic can be disabled only if it is not the default; default theme; default mode; "Allow users
to choose their theme" — off by default; "Allow users to choose light or dark"), live **preview** cards per theme (tokens only, no
screenshot images), **Branding** note (link to Settings → Branding; logo and primary colour apply in every theme), **Landing app per role**
(ordered rules: role, app; add / remove / move up / down), **Applications** (table of apps: icon, accent, names, tabs dialog, home
dashboard). Save is one versioned document (409 on conflict). Profile → Preferences shows **Theme** (only when allowed, enabled themes
only), **Mode** (when allowed), **Default app** and **Favourite apps** (Enterprise / Modern).

## 8. Command palette (Modern) and Enterprise utility bar (ADR 0174)

- **Palette:** Ctrl/⌘+K (and the top-bar button) opens a dialog built on the Phase 19 `command-search` combobox: the same
  `/api/v1/search` results grouped by system, plus *Pages* (apps and tabs the user may open, matched in both languages) and *Actions*
  (quick actions of the visible apps, language, mode, theme when allowed). Arrow keys / Enter / Escape; empty query shows recent items and
  pages. In Classic and Enterprise Ctrl+K keeps focusing the top-bar search (Phase 19).
- **Utility bar (Enterprise):** fixed at the bottom on `md` and wider: *Recent items* (server recent items, Phase 19) and *Quick note*
  (a scratch note kept in `localStorage` only — never sent to the server). Hidden below `md`.

## 9. Tests

| Requirement | Test |
|---|---|
| Theme and mode resolution (admin default; user choice only when allowed) | `AppearanceResolverTests` (unit, table); `AppearanceApiTests` (integration: defaults, admin saves, user choice refused when off / theme not enabled / mode off, choice applied when on and ignored again when turned off, 403 without `settings.general.manage`, 409 stale version, validation, public defaults, audited) |
| App launcher shows only accessible systems | Vitest `apps.test.ts` (visibility by system and permission, overrides never add access, tab order / hidden tabs); E2E `themes.spec.ts` as a sample editor (only Sample + shared apps) and as the administrator |
| Landing app per role | Vitest `resolveLandingApp` table; E2E: admin sets a rule → the editor lands on that app; own default wins |
| Every main screen renders in every theme × AR / EN × light / dark without horizontal scroll at 360 px | E2E `themes.spec.ts`: 8 main screens × 3 themes × 2 modes in projects `ar` and `en`, at 1366 px (screenshots) and at 360 px (`scrollWidth ≤ clientWidth`) |
| axe WCAG 2.1 AA in all three themes | `a11y.spec.ts` gains a **theme pass**: main screens, launcher, palette, app home, project record and Appearance page in Enterprise and Modern, light and dark (Classic is the existing audit) |
| Visual regression baseline per theme | Playwright project `visual` (`toHaveScreenshot`, committed baselines under `web/e2e/visual.spec.ts-snapshots/`): UI kit page and app launcher per theme × mode × language (static content, deterministic) |
| Classic unchanged | the existing E2E suite runs in Classic (the default) unchanged |

## 10. Out of scope / later

- Inline edit and mass actions in list views (C3) exist where Phases 1–18 built them; Phase 20 does not add new inline-edit endpoints.
- Per-user tab customisation inside an app (only admins order tabs).
- Report / dossier screens of Phase 21 follow the same tokens automatically.
