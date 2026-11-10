# Phase 20 — Themes and application workspaces: summary

Status: **built — awaiting owner acceptance** · version stays **1.0.0** (no tag until the end of Phase 23, SPEC-ADDENDUM A7).
Requirements: [`docs/SPEC-ADDENDUM.md`](../SPEC-ADDENDUM.md) §C (C1–C5) · design note: [`docs/design/themes.md`](../design/themes.md).
ADRs **0170–0175 proposed** for owner acceptance.

## What was built

| Item | Result | Design / ADR |
|---|---|---|
| **Three themes × light / dark × AR / EN** | One component library, token sets per theme on `<html data-theme>` (`web/src/styles/index.css`): **Classic** = the Phase 0–19 tokens byte for byte (default, unchanged); **Enterprise** = compact (root 93.75 %), square, cool grey canvas, white work surfaces, light elevation; **Modern** = warm paper canvas, 0.875 rem radius, soft elevation, generous spacing, **Noto Kufi Arabic** headings in Arabic (OFL), Arabic line height 1.75, one 160 ms entrance motion that `prefers-reduced-motion` removes. New layout tokens (`--control-h`, `--cell-py`, `--shadow-card`, `--header`, `--app-accent`, `--font-heading`) carry density, elevation and accent; Classic's values equal the old sizes | `themes.md` §3, 0170 |
| **Layout slots, no duplicated screens** | Only the shell and six shared components read the theme (`useAppearance`): `AppShell` (Classic sidebar / workspace shell), **`RecordLayout`** (stack · Enterprise highlights panel + path + two columns with related lists · Modern card layout with key facts), **`SplitView`** + `useListView` + `ListViewToggle` (table / list + preview), `StageStepper` (circles · **path bar** with "Current stage" guidance · progress track), `TabsList` / `TabsTrigger` (underline · boxed with accent top bar · pills), `PageHeader` (title scale). Adopted on the project record page and the project list; every other screen follows through the shared components and tokens | `themes.md` §4, 0170 |
| **Settings → Appearance** | Typed area `appearance` (versioned, audited, `settings.general.manage`): available themes, default theme (Classic) and mode (same as device), **Allow users to choose their theme — off by default**, allow light / dark (on: today's behaviour), **landing app per role** (ordered rules), per-app **name AR / EN, icon, accent colour (contrast warning < 3 : 1), tab order, hidden tabs, home dashboard**; live token previews of each theme; branding note (logo and primary colour apply in every theme) | `themes.md` §7, 0171 |
| **Theme / mode resolution on the server** | `AppearanceResolver` (Platform.Settings): user theme only while choosing is on **and** the theme is enabled, else the default; mode likewise with `allowUserMode`. `GET /identity/me` returns the effective values; `PUT /identity/me/preferences` accepts `uiTheme` / `landingApp` / `favouriteApps` (migration `Appearance` on `identity.users`) and refuses a choice that is not allowed (422 `appearance.errors.*`); `GET /settings/public` adds the defaults for the sign-in pages | 0171 |
| **App launcher and workspaces** | App registry (`web/src/layout/apps.ts`, 16 apps = server catalogue): the 11 systems + Sample, shared **My Work, Dashboards, Tasks, Teams, Organisation, Settings**. Launcher (grid button): search in both languages, favourites first, systems, shared apps, star to pin, arrow-key navigation. Workspace per app: header with icon / accent / name, **its own tabs** (Enterprise tab bar · Modern collapsible sidebar with favourites and recent items), the module's secondary navigation hidden there (one navigation per page). **Home page** `/apps/<app>`: quick actions (permission-filtered), *My items in …* (My Work filtered by system), the Phase 17 system dashboard. Routes unchanged: the current app is derived from the URL, so **deep links work in every theme**; `/my-work` added | `themes.md` §5, 0172 |
| **Landing app per role** | Enterprise / Modern on `/`: the user's own default app → the first admin rule for one of their roles → My Work. Classic keeps My Work at `/`. Profile → Preferences → Appearance: theme (only when allowed), default app, favourite apps | `themes.md` §6, 0173 |
| **Enterprise patterns** | Tabs per app, highlights panel (title, chips, primary actions, six key fields) with accent top border, **path bar**, two-column body (tabs · related lists with counts that open the related tab), table lists with a split-view toggle, **utility bar** (recent items; quick note kept in this browser only) | 0174 |
| **Modern patterns** | Collapsible app sidebar (state remembered), **command palette Ctrl / ⌘ + K** (records from the Phase 19 global search — same permission-filtered results —, pages = apps and tabs, actions = quick actions + language / mode / theme when allowed), split view by default (cards with status chips and progress bars + preview), card record layout with progress track | 0174 |

### No vendor branding
The Enterprise theme reproduces *working patterns* only. Icons are Lucide, fonts IBM Plex Sans / IBM Plex Sans Arabic (and Noto Kufi Arabic
for Modern headings); the theme is called "Enterprise" / «المؤسسي». No vendor name, logo, icon set or font appears in the product, its
strings or its documentation.

## Requested tests

| Test | Evidence |
|---|---|
| Theme and mode resolution (admin default, user choice only when allowed) | `UnitTests/Settings/AppearanceResolverTests` (8-row resolution table, choice validation, defaults, validation by field — 11 tests); `IntegrationTests/Settings/AppearanceApiTests` (4: defaults Classic / system and choice refused while off; admin default applies to everyone, choice honoured only while allowed **and** enabled, ignored again when turned off, mode choice off; only settings managers change appearance — 403 / 401, validation by field, 409, audited; default app and favourites validated and kept on language-only updates); E2E *the theme menu offers themes only while choosing is allowed* |
| App launcher shows only accessible systems | Vitest `layout/apps.test.ts` (system access, permissions, no access before `/access/me`, overrides never add access, tab order / hidden tabs, every label in AR and EN, icons and accent contrast ≥ 3 : 1, app codes = server catalogue — 24 tests); E2E *app launcher shows only the systems the user can open* (as the demo editor: every system tile is a system they may open, no Settings app; favourites first) |
| Landing app per role | Vitest `resolveLandingApp` table (6 rows); E2E: admin rule → the editor lands on Sample; own default app wins; Classic stays on My Work |
| Every main screen in every theme × AR / EN × light / dark without horizontal scroll at 360 px | E2E `themes.spec.ts`: 8 main screens (launcher, app home, list, project record, request wizard, My Work, executive dashboard, Settings → Appearance) × 3 themes × 2 modes, in projects `ar` and `en`: screenshot at 1366 px and `scrollWidth ≤ clientWidth` at 360 px — **96 screen states per language, all pass** |
| axe WCAG 2.1 AA in all three themes | `a11y.spec.ts` theme pass (Enterprise and Modern × light and dark: My Work, launcher, palette, Portfolio and KPI app homes, project list, project record, Settings → Appearance) — **60 screen states, 0 violations**; Classic: the existing audit (main screens + settings now including `/apps/portfolio`, `/my-work` and Settings → Appearance) |
| Visual-regression baseline per theme | Playwright projects `visual-ar` / `visual-en` (`visual.spec.ts`, opt-in `E2E_VISUAL=1`): UI kit × 3 themes × 2 modes and the launcher in the workspace themes; baselines in `web/e2e/visual.spec.ts-snapshots/` (ADR 0175) |
| Classic unchanged | The whole existing E2E suite runs in Classic (the default); Classic token values and shell markup are the Phase 19 ones |

## Defects found while verifying (fixed in this phase)
- Modern top bar 24 px too wide at 360 px (menu + launcher + palette): the app name moves to the sidebar and the theme menu to Profile on phones.
- The palette button was named by `aria-label` while showing other text (axe `label-content-name-mismatch`, WCAG 2.5.3): now named by its visible text.
- Theme previews on the Appearance page used the page's dark tokens for parts of a light preview (contrast): token blocks now apply to any
  `data-theme` element in light and dark.
- Signing a second user in on a page that shares the administrator's E2E session invalidated that session for the rest of the worker: the
  regular-user tests use their own session.

## Quality gates

Local runs in the build sandbox (SDK 10.0.114; PostgreSQL 16, Mailpit and Gotenberg in Docker; Chromium 1243). The GitHub Actions workflow
runs when the pull request is opened.

| Gate | Result |
|---|---|
| `dotnet build` (Host and dependencies) | 0 warnings, 0 errors |
| `dotnet format --verify-no-changes` | clean (after formatting the two new test files) |
| Unit tests | **2 157 / 2 157** (incl. `AppearanceResolverTests` 11; the 7 OCR tests need Tesseract, installed for the run as CLAUDE.md §18 says) |
| Architecture tests | **32 / 32** |
| Integration tests (Testcontainers PostgreSQL, Mailpit, Gotenberg) | **2 725 / 2 725** in one run (incl. `AppearanceApiTests` 4 / 4, settings, identity, setup wizard, security, migrations, audit chain) |
| `tools/check-api-client.sh` | OpenAPI document and TS client regenerated and committed |
| Web: `typecheck`, `lint` (ESLint + RTL guard), `format:check`, `lint:licenses` | pass; 669 npm packages allowed (new: `@fontsource/noto-kufi-arabic`, OFL-1.1) |
| Vitest | **460 / 460** (incl. `layout/apps.test.ts` 24) |
| `npm audit --audit-level=high` | pass (8 moderate, dev tooling — unchanged from Phase 19) |
| `npm run build` | OK |
| E2E `themes.spec.ts` (`ar` + `en`) | **22 / 22** (6 theme × mode walks of 8 screens + 360 px reflow, Enterprise record, palette, launcher, landing app, theme menu — per language) |
| E2E accessibility (`ar-a11y` + `en-a11y`) | theme pass **2 / 2, 0 violations in 60 screen states**; Classic *main screens* and *settings and administration* (now with `/apps/portfolio`, `/my-work`, Settings → Appearance) **4 / 4** |
| E2E Classic regression subset (`shell`, `platform`, `profile`, `settings`, `search`; `ar` + `en`) | **36 / 36** |
| Visual regression (`E2E_VISUAL=1`, `visual-ar` + `visual-en`) | baselines created, re-run **12 / 12** |

Not run in this sandbox: the complete E2E suite of all phases (the subset above covers the shell, settings, profile and search that Phase 20
touches; every other screen is unchanged in Classic) and the container image / Trivy job (no dependency change besides the OFL font
package) — CI runs them.

## Deviations
- **From the addendum:** none known.
- **Choices recorded as ADRs (SPEC silent):** users keep choosing light / dark unless the administrator turns it off (`allowUserMode`, ADR
  0171); Classic keeps My Work at `/` (no landing redirect, ADR 0173); no landing rules are seeded (role codes are installation data);
  per-app settings reuse `settings.general.manage` (no new permission).

## Known gaps
- `RecordLayout` (highlights + related lists) and `SplitView` are adopted on the **project** record page and the **project** list; the
  other record and list pages get the theme's header, path bar, tabs, cards and density through the shared components but keep their
  single-column arrangement until moved to `RecordLayout` (mechanical, per page).
- Workspace tabs filter by permission; a few module navigations also hid entries by module options (e.g. Audit Office-only calendar); in the
  workspace themes such a tab can be visible to a user whose page then answers with its own access check.
- Inline edit and mass actions remain where earlier phases built them (no new endpoints in Phase 20).
- Visual-regression baselines are opt-in (`E2E_VISUAL=1`): pixel baselines depend on the machine's font rendering; re-baseline on the
  reference machine with `--update-snapshots` when tokens change.

## How to demo
1. Settings → **Appearance** → set the default theme to *Enterprise* (or turn on *Allow users to choose their theme*) → Save.
2. Open `/` → the launcher (grid button) → **Portfolio**: home page with quick actions, *My items in Portfolio* and the system dashboard;
   tabs across the top; a project → highlights, path, related lists; the utility bar at the bottom.
3. Profile → Preferences → Appearance → *Modern* → the app sidebar, **Ctrl + K** (type a project number or "Projects"), the project list in
   list + preview.
4. Settings → Appearance → *Landing app per role* → add a rule for a role → that user opens Portfolio (or their own default app) on sign-in.
5. Switch language and dark mode in each theme; resize to 360 px.

## Screenshots
[`docs/screenshots/phase-20/`](../screenshots/phase-20/) — `<lang>-<theme>-<mode>-<screen>.png` for `ar` / `en` × `classic` /
`enterprise` / `modern` × `light` / `dark` × `launcher`, `app-home`, `list`, `record`, `wizard`, `my-work`, `executive`, `appearance`
(96 images), plus `enterprise-light-record-milestones`, `modern-light-palette` and `enterprise-light-launcher-user`.
