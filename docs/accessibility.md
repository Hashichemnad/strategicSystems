# Accessibility — WCAG 2.1 AA audit (release 1.0)

Target: **WCAG 2.1 Level AA**, identical quality in Arabic (RTL) and English (LTR), light and dark themes, down to 360 px
(SPEC §0.3, CLAUDE.md §10). Result: **no axe-core WCAG 2.1 A / AA violation on any audited screen** in either language,
after the fixes listed below; keyboard, focus and reflow checks pass. Residual items that an automated tool cannot judge are
listed at the end.

## 1. Method

| Part | How |
|---|---|
| Automated rules | [axe-core](https://github.com/dequelabs/axe-core) 4.14 through `@axe-core/playwright` (MPL-2.0, test-only; `docs/THIRD_PARTY_LICENSES.md`), tags `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa` (best-practice rules are reported by axe but are not the gate). Helper: `web/e2e/a11y.ts` (`expectAccessible`) — waits for the screen to settle (network idle, no `aria-busy`, fonts), runs axe, fails the test with rule, impact and elements, and appends every result to `web/test-results/a11y/violations.jsonl` |
| Screens | `web/e2e/a11y.spec.ts` in both Playwright projects (`ar`, `en`), as the demo administrator (every office role): sign-in pages, all main screens, all Settings / administration areas, **record pages with every tab** (the first record of each list), the agile project (board, backlog, sprints), a **dark-theme subset**. It runs after the other specs on the same database, so lists and record pages have data (like the Phase 17 reporting spec) |
| Embedded in flows | `web/e2e/setup.spec.ts` checks every step of the first-run setup wizard (stage 1 and 2) on a fresh installation; `web/e2e/wasel.spec.ts` checks every vendor-portal screen it photographs (MFA enrolment, projects, overview, proposal, monthly submission…) |
| Keyboard and focus | Scripted in `a11y.spec.ts`: Tab through the shell (skip link first, a visible focus indicator on every stop, no trap, ≥ 10 distinct stops); a dialog keeps focus inside, Escape closes it and focus returns to the button that opened it; the program-request wizard is reached, chosen and moved through with the keyboard only (Tab to *Fill manually*, Enter, Tab to *Next*, Enter → next step or announced errors) |
| Reflow (1.4.10) | Main pages at **360 px** and at **683 px** (1366 px at 200 % zoom): no horizontal page scroll (`scrollWidth ≤ clientWidth`); wide tables scroll inside their own focusable region |
| Unit level | Vitest + Testing Library on the shared components (`src/ui/ui.test.tsx`): named selects, filter tabs without dangling `aria-controls`, dialog focus return; ESLint `jsx-a11y` on every file |

Run: `npx playwright test e2e/a11y.spec.ts` (after the rest of the suite on the same database) and
`npx playwright test -c playwright.setup.config.ts` for the setup wizard.

## 2. Screens covered (both `ar` and `en`)

| Area | Screens |
|---|---|
| Signed out | Sign-in, forgot password, Wasel sign-in; setup wizard stage 1 (token, organisation, branding, languages, administrator), MFA enrolment |
| Shell & personal | My Work (all cards), approvals, notifications, profile (tokens, MFA, delegations), AI *Ask*, sample module + item dialog |
| Dashboards & reports | Dashboards list, executive dashboard (incl. dark), a system dashboard (`kpi.scorecard`), reports, report builder, schedules |
| Organisation | Units, org chart, employees, employee record (all tabs) |
| Portfolio | Dashboard, requests, new-request wizard, request record (all tabs), projects, project record (all ~30 tabs incl. Gantt, RAID, RACI, health, vendors, monthly), agile project (board, backlog, sprints), portfolio Gantt, capacity, portfolios, reviews, vendors + vendor record |
| Wasel (vendor) | MFA enrolment, projects, project overview, proposal, monthly submission (`wasel.spec.ts`) |
| KPI | Dashboard, cards, card record (all tabs), new card, pending, approvals, scorecard, strategy map, reports |
| Risk | Dashboard, register, risk record (all tabs), new risk wizard, project risks, my actions, mechanism (matrix), reports |
| Teams & Tasks | Teams list / dashboard / team record (all tabs); tasks list / dashboard / task record (all tabs) |
| Requests, Document Registry, IP | Requests home / mine / queue / request record; Document Registry home, documents, document record, dashboard; IP register, IP record, dashboard |
| Benchmarking & CAP | Bench home, plan, studies, study record, practices, comparators; CAP home, remarks, remark record, my actions, sources, source record, suggestions |
| Audit Management | Home, findings, finding record (all tabs), plans, plan record, universe, calendar, reports, my department |
| Audit trail | Audit log, chain verification |
| Settings (42 areas) | Regional, branding, systems, features, lookups, numbering, translations, notifications, files, jobs, **health**, approvals, AI, MCP, dashboards, reporting, tasks (+ reassign), teams, portfolio, KPI, risk, request types, Document Registry, IP, CAP, Benchmarking, Audit Management, business hours, holidays, roles, assignments, system access, transfer, access review, users, identity providers, security, email, levels, import, **setup wizard stage 2** |

Each run of `a11y.spec.ts` audits about 550 screen states (≈ 275 per language: page × tab × theme), plus 15 setup-wizard and 9 Wasel screens per language in their own specs. Records the main suite does not always leave behind (an open request in the queue, an audit plan) are optional and reported as a skipped annotation.

## 3. Findings and fixes

Counts are axe *nodes* (elements) before the fixes, from the first full runs (an empty database, then the database of the
complete E2E suite with record pages); **after: 0 for every rule**.

| Rule (WCAG) | Before | Cause | Fix (files) |
|---|---|---|---|
| `color-contrast` (1.4.3) | 375 elements on 34 screen states | Default primary (no branding colour yet, setup wizard) 4.41 : 1 with white text; status chips: success / info / warning text on their soft backgrounds 3.97–4.29 : 1; dark danger badge 4.46 : 1; white text on admin-chosen risk band colours (orange 2.8 : 1); faded empty RAID cells; "no logo" hint on the dark preview | Tokens: default `--primary` and light `--success` / `--info` / `--warning` darkened, dark `--danger` lightened (`src/styles/index.css`); band text colour chosen by WCAG contrast ratio instead of a brightness threshold (`features/risk/scoring.ts` `textOn`, unit tests); notification badge on `danger-foreground`; RAID empty cells dashed instead of faded; branding preview text colours |
| `button-name` (4.1.2) | 106 on 48 states | Filter selects (Radix trigger) without a label — e.g. the shared `UnitSelect`, employee filters / status; two icon-only buttons on audit recommendations | `Select` is never unnamed: explicit `aria-label` where the screen knows the field, otherwise the placeholder or the chosen option (`src/ui/select.tsx`); labels added in `org/EmployeesPage`, `org/EmployeePage`; `aria-label` + tooltip on the audit recommendation edit / cancel buttons |
| `label-content-name-mismatch` (2.5.3) | 380 on 270 states | Notification bell named by `aria-label` while showing a count (every screen); date-picker "Open calendar" label hiding the visible date / placeholder; capacity toggle; RAID matrix and risk heat-map cells; agile *Move to…* menu; status-report download buttons; Gantt rows whose arrow / warning glyphs and percentage were not in their name | Named by their content: visible text first, the full description as screen-reader-only text (`NotificationBell`, `ui/date-picker.tsx`, `ui/dashboard/HeatGrid.tsx`, `CapacityPage`, `RaidTab`, `BoardTab`, `StageStandards`); Gantt arrows and delay mark drawn as shapes, the percentage outside the row button (`ui/gantt/Gantt.tsx`) |
| `aria-required-parent` / `aria-required-children` (1.3.1) | 116 on 16 states | Risk matrix cells (`gridcell`) without rows; heat grid with no columns rendered an empty row; view switches marked as `tablist` containing plain buttons | Read-only risk matrix as rows with row / column headers, editable one stays a radio group (`features/risk/components.tsx`); empty heat grid says "No data to show." instead of an empty grid (`ui/dashboard/HeatGrid.tsx`); audit findings views and calendar ranges are a group of pressed buttons |
| `aria-valid-attr-value` (4.1.2) | 10 on 10 states | Tabs used as list filters pointed `aria-controls` at panels that do not exist | `TabsTrigger filter` drops `aria-controls` (`ui/tabs.tsx`; Tasks, Teams, KPI pending, program requests) |
| `scrollable-region-focusable` (2.1.1) | 12 on 8 states | Overflowing boxes (health check tables, audit log, setup stepper) not reachable by keyboard | New `ScrollRegion` (`ui/scroll-region.tsx`): while its content overflows it is a named, focusable region (arrow keys scroll it); used by every `Table` and the health page; the stepper now wraps instead of scrolling |
| `nested-interactive` (4.1.2) | 6 on 5 states | The Gantt `svg` had `role="img"` but contains focusable row buttons | Gantt `svg` is a named `group` (`ui/gantt/Gantt.tsx`, unit tests updated) |

Other defects found by the scripted checks:

| Check | Finding | Fix |
|---|---|---|
| Dialog focus (2.4.3) | Dialogs opened from state (not from a `DialogTrigger`: table row actions, toolbar buttons — almost all of them) returned focus to the page body on close | `DialogContent` remembers the opener when it mounts and gives focus back on close (`ui/dialog.tsx`, unit test) |
| Reflow 360 px (1.4.10) | Top bar 15 px too wide in Arabic (language toggle text); My Work cards and wide tables (KPI cards, health) widened the page; long version strings on the health page did not wrap | Top bar shows the language toggle as an icon below `sm` (name kept), tighter gaps; grid items may shrink (`:where(main .grid) > * { min-width: 0 }`); `ScrollRegion` is `relative` so screen-reader-only text inside a scrolled table no longer widens the page; health messages wrap anywhere |
| Setup wizard (owner report from the setup stream) | Import step showed two "Next" buttons and a second page heading; Arabic stepper cut off the last step | Embedded Settings pages render their header as a sub-heading (`EmbeddedPage` in `ui/page-header.tsx`); stage 2 buttons are named "Next setup step" / "Previous setup step" (`setup.json` ar + en); the stepper wraps |

## 4. Manual checks (release 1.0, both languages)

| Check | Result |
|---|---|
| Skip link "Skip to content" is the first Tab stop and moves focus to `main` | Pass (scripted) |
| Every interactive element shows a focus ring (`outline` / ring token) in light and dark | Pass (scripted for the shell; tokens shared by all components) |
| Language switch flips `dir` / `lang` without reload; mirrored icons and logical CSS only (`scripts/check-rtl-classes.mjs` in lint) | Pass |
| 200 % zoom and 360 px: no horizontal page scroll on the main pages; tables scroll in their own region | Pass (scripted) |
| Charts: every ECharts chart has an `aria` description and *Show as table* (ADR 0148); heat-maps are keyboard grids | Pass (Phase 17, re-checked) |
| Forms: labels, hints and errors linked (`FormField`), errors announced (`role="alert"`) | Pass |

## 5. Residual items

| Item | Why it remains | Impact |
|---|---|---|
| Screen-reader walkthrough with NVDA / JAWS / VoiceOver | Not available in the build sandbox; axe and scripted checks cannot judge the quality of announcements | Recommended as part of the owner's acceptance on a Windows machine with NVDA (Arabic and English voices) |
| Arabic-Indic digits and Hijri dates read by screen readers | Depends on the screen reader's Arabic support | Low; Latin digits are the default (Settings → Regional) |
| Gantt chart is navigable by keyboard row by row, but the timeline itself is visual | A visual timeline; each row's name carries its dates, progress and status, and the project's Milestones tab is the tabular equivalent | Low |
| Colours chosen by administrators (branding primary colour, risk band colours) | Band text adapts automatically; a very light primary colour can still lower contrast of primary buttons | The Branding screen should be used with a colour of at least 4.5 : 1 against white (administrator guide) |
| Best-practice rules (`region`, `heading-order`, `landmark-*`) | Not part of WCAG 2.1 AA; reported by axe for information | None for conformance |
