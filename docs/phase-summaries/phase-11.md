# Phase 11 — Wasel vendor portal and Portfolio completion: summary

**Status:** built and verified in this session (all gates green) · ADRs 0110–0117 accepted by the owner
**Branch:** `claude/happy-planck-sl7tck`
**Planning:** the plan is the Phase 11 part of [`docs/design/portfolio.md`](../design/portfolio.md) (§46–§62), committed before the code. As in earlier
phases the session ran without an interactive approval step; open choices are ADRs 0110–0117. Phase 15 is built in a parallel session: shared
files were changed only additively (see "Platform / other modules").

## What was built

| Area | Result |
|---|---|
| **Vendor principal and sign-in** (ADR 0110) | Vendor accounts are identity users of type `vendor`. A separate cookie scheme `wasel` (`strategy.wasel`, path `/api/wasel`, SameSite Strict, server-side session with the same idle / absolute limits) and its own two-factor cookie. The default scheme selects `wasel` for every `/api/wasel/*` request and never for anything else; `AuthenticationRequirementMiddleware` also refuses a vendor principal outside `/api/wasel` and an internal one inside it. **MFA is mandatory** for vendors (enrolment at the first sign-in: authenticator app or e-mail code; recovery codes), lockout and the `auth` rate limit apply. The internal login refuses vendor accounts and the Wasel login refuses internal ones ("invalid credentials", no enumeration). Invitation e-mail `identity.waselInvitation` → `/wasel/set-password`; Wasel password reset; antiforgery token at `/api/wasel/v1/antiforgery/token` |
| **Vendor authorization** (ADR 0111) | New switchable system `wasel` with nine `wasel.*` permissions given **only** by the record roles `vendor-lead` / `vendor-contributor` on `portfolio.project` (the Contributor lacks the two submit permissions). `IAccessService` has one vendor branch: vendors may only use `wasel.*`, only through record roles granted to their user id that are valid now; internal principals are refused `wasel.*`. Grants end at `access_until + 1 day`, so expiry needs no job. Endpoints declare `RequireVendorPermission`; a per-request vendor context requires an active vendor user of an active vendor and an active assignment; milestones are limited to those assigned to the vendor (404 otherwise) |
| **Vendor register** (ADR 0112) | `portfolio.vendor` (`VND-0001` numbering, names, trade licence, expiry, country, address, contact, e-mail, phone, status Active / Suspended / Blacklisted with reason). Managed by the PM Office (`portfolio.vendor.manage`); `IVendorDirectory` in Portfolio.Contracts for other modules; contracts gain `vendor_id` and `end_date`. Vendor users with status, last sign-in, MFA, deactivate / reactivate; performance history from ratings |
| **Vendor-visible fields and DTOs** (ADR 0113) | Dedicated `Vendor*Dto` records with explicit mapping. Fixed visible fields plus switches in Settings → Portfolio → Wasel (`description`, `progress` on; `owner`, `budget`, `approvalStatus`, milestone `cost` off; milestone `weight` on). No approvals, approvers, internal comments, team, stakeholders or other vendors in any vendor response |
| **Plan proposal** (W3.2, ADR 0114) | Vendor drafts phases / milestones (dates, weights, deliverables, tasks) or epics / stories (agile); Lead submits; PM accepts with edits (diff stored and shown to the vendor; milestones created and assigned to the vendor, epics / stories through the agile service) or returns with a comment. Transition table unit-tested |
| **Vendor-then-PM monthly update** (W4.2, ADR 0114) | Per-project mode (`pm-only` default, audited switch). Vendors are e-mailed when the window opens; they fill their assigned milestones (achieve, actual dates, progress note, causality and corrective actions when delayed, evidence files — required at 100 %); only the Lead submits. PM reviews each item side by side (last approved · vendor · PM values): **Accept**, **Edit then accept** (diff stored, shown to the vendor) or **Return** (comment). Accepted values become the milestone's pending update with `pending_source` = `vendor` / `vendor-edited`; the monthly submit is refused while a vendor submission waits for review; the approval snapshot carries the source, vendor name and original vendor values, shown to approvers. After approval the vendor sees *Included in approved update*; the PM can share an outcome note (approval comments never reach the vendor) |
| **Deliverables, drafts, meetings, comments** | Deliverable upload (Files service: allow-list, magic number, size, virus scan when on) with accept / return; issues / challenges / risks proposed as drafts and created through the normal services when the PM accepts (RAID issue, challenge, project risk scored by the shared framework); meetings shared with the vendor, who confirms them; comment thread per milestone / submission / proposal / deliverable with notifications to the other side |
| **Expiry, revocation, dormant accounts** (ADR 0115) | Access date per assignment (contract end by default); revoke a member or an assignment (sessions revoked when no project is left); closure approval ends vendor access (setting `endAtClosure`); hourly job `portfolio.wasel-access` marks expired rows, ends access of closed projects and disables dormant accounts (90 days, configurable) |
| **Vendor rating** (ADR 0115) | Timeliness, quality, responsiveness, reporting (1–5) + comment; overall = average. Per milestone any time, overall at Closure or later; feeds the register's performance history and sets the Impact *vendor satisfaction* when still empty |
| **Notifications** | 13 bilingual templates (vendors by e-mail with a `/wasel` link; PM / PM Office e-mail + in-app) |
| **AI on the PM side** (ADR 0117) | `portfolio-vendor-review`: inconsistency flags computed by the server (100 % without evidence, progress jump, dates outside the access period, start well before plan, delayed without causality, end in the future) + model summary + draft return comment AR / EN. Vendors never reach AI, Ask, search or MCP |
| **Capacity view** (SPEC §1.12, ADR 0116) | Allocation % per employee of a unit (and below): Teams memberships of active teams (new `ITeamAllocationDirectory` in Teams.Contracts) + proposed charter members of active projects without a formed team (new `allocation_percent`); over-allocation warning above `capacityThreshold` (100 %) |
| **Quarterly portfolio review** (SPEC §1.12, ADR 0116) | Review per quarter, decision per project (continue / re-scope / hold / cancel + comment). Completing never changes a project: hold → new change-request type `hold` (applied as on-hold by *Apply changes*), cancel → `cancellation`, both submitted into `portfolio.change-request`; re-scope → draft details change request |
| **Gantt export font** (ADR 0117) | The PNG / PDF export embeds IBM Plex Sans Arabic / IBM Plex Sans (bundled `@fontsource`, OFL) as data-URL `@font-face` in the exported SVG — Arabic no longer depends on a system font |
| **`portfolio.sprintReviewed`** | Sent to the PM and owner when a sprint review approval is approved, returned or rejected |
| **Web** (AR + EN) | `/wasel` area with its own layout, sign-in with MFA enrolment, set password, projects, project page (Overview, Plan, Monthly update, Deliverables, Issues & risks, Meetings, Comments). Internal: project tab *Vendors (Wasel)* (assignments, invitations, members, mode, milestone assignment, proposal and submission review with diff, AI review, deliverables, drafts, comments, rating), vendor register pages, Capacity page, Portfolio reviews pages, Settings → Portfolio → Wasel, vendor chips on the Monthly tab |

## Acceptance checks (BUILD_PLAN Phase 11)

| Check | Evidence |
|---|---|
| PM invites a vendor; vendor logs in with MFA at `/wasel`, sees only assigned project data, proposes a plan, submits a monthly update | Integration `Vendor_proposes_the_plan_and_submits_the_monthly_update_…` (invitation e-mail → password → enrolment forced → second sign-in asks for the code; proposal; Lead submission) and E2E `wasel.spec.ts` in **ar** and **en** |
| PM edits one value before accepting; vendor sees the change; approvers see which values came from the vendor | Same integration test (diff `achieve 0.3 → 0.25` read by the vendor; snapshot `source = vendor-edited` with the vendor's values and name, read by the Department Director before approving) and E2E screenshots |
| Penetration-style tests find no internal data exposure | Integration `A_vendor_session_reaches_no_internal_endpoint_…` sweeps **every** non-public `/api` endpoint with a vendor session (401 each) and every Wasel endpoint with an internal session (401 each), plus MCP; `A_vendor_sees_only_its_projects_…` (other vendor 404, non-assigned milestone 404, no cost / budget / internal fields in vendor JSON, expiry with the fake clock through `IAccessService`, revocation); `Suspending_a_vendor_…`; unit `Vendor_dtos_carry_no_internal_fields`; manual checklist below |

Manual penetration checklist (reviewed against the code): vendor cookie path `/api/wasel` (never sent elsewhere) · SameSite Strict · server-side session revocation on deactivate / suspend / revoke · no PAT / OAuth for vendors (Identity `/me` endpoints unreachable) · NotificationHub, AI gateway, MCP gate refuse vendor principals · record-role grants carry an end date · vendor DTOs separate · file downloads only for the vendor's own evidence / deliverables · uploads through the Files service checks · all vendor writes audited with channel `wasel` (integration test).

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build -c Release` · `dotnet format --verify-no-changes` | 0 warnings, 0 errors · clean |
| `dotnet test --solution Strategy.slnx` | **3 751 tests: 3 743 passed in the full run**; the 8 failures were 7 OCR unit tests (Tesseract was not installed in the sandbox at that moment — 14 / 14 pass once installed) and `McpServerTests.Vendor_principals_are_refused`, whose expected code changed by design (the principal-type wall now answers `auth.principalRefused` before the MCP gate); re-run green with `EndpointCatalogueTests`. New: Wasel integration 5 tests (`WaselTests`), unit 64 (`WaselRulesTests`), endpoint catalogue check for vendor endpoints |
| `tools/check-api-client.sh` · `tools/check-vulnerable.sh` | OpenAPI document and TS client regenerated · no High / Critical NuGet vulnerabilities |
| `npm run lint` · `format:check` · `typecheck` · `npm test` | clean (RTL class check included) · clean · clean · **304 / 304** Vitest |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK (existing chunk-size warning) · no High / Critical (8 moderate, pre-existing: ECharts and typescript-eslint/minimatch) · 617 packages allowed |
| `npx playwright test` (whole suite, fresh database) | **94 / 94** in Arabic and English (92 earlier + the new `wasel.spec.ts` in both languages); the job dashboard spec now expects 20 jobs |

Existing tests updated for Phase 11 facts: job lists (integration and E2E) include `portfolio.wasel-access`; the endpoint catalogue accepts `VendorEndpoint` metadata; the MCP vendor test expects the earlier wall; three Vitest files type the `api.POST` spy loosely (TS2589 after the client grew).

## Decisions (ADRs — accepted by the owner)
- [0110](../decisions/0110-wasel-vendor-principal-and-scheme.md) Vendor principal and a separate authentication scheme.
- [0111](../decisions/0111-vendor-authorization-through-record-roles.md) Vendor authorization through `IAccessService` and record roles.
- [0112](../decisions/0112-vendor-register-shared-supplier-master.md) Vendor register as the shared supplier master.
- [0113](../decisions/0113-vendor-visible-fields-and-dtos.md) Vendor-visible fields and separate vendor DTOs.
- [0114](../decisions/0114-vendor-proposals-submissions-and-monthly.md) Plan proposals, submissions, PM review with diff, monthly approval.
- [0115](../decisions/0115-vendor-access-expiry-notifications-rating.md) Expiry, dormant accounts, notifications, rating.
- [0116](../decisions/0116-capacity-view-and-portfolio-review.md) Capacity view and quarterly portfolio review.
- [0117](../decisions/0117-wasel-ai-gantt-font-sprint-notification.md) Wasel AI, Gantt export font, sprint review notification.

## Deviations and behaviour changes
- **Proposed tasks** in a vendor plan proposal are kept on the accepted proposal; the PM creates them in the milestone's Tasks panel (there is no task-creation contract between modules yet — ADR 0114).
- **The PM can still update a vendor milestone directly** in *Vendor then PM* mode (source `pm`), so a silent vendor never blocks the month (ADR 0114).
- **Vendors receive e-mail only**; the in-app bell stays internal (ADR 0115).
- **Change requests** gain the type `hold` (from portfolio reviews); applying it puts the project on hold.
- The internal sign-in now looks accounts up within the internal principal type only (an internal and a vendor account may share an e-mail).

## Platform / other modules (additive)
- Platform.Identity: `wasel` / `wasel.mfa` schemes, `WaselSignInService`, `IVendorAccounts`, `IVendorSignInPolicy`, two identity e-mails; `SignInService` invitation / reset accept a path and template; `FindUserAsync` by principal type.
- Platform.Access: vendor branch in `AccessService`. Platform.Http: `VendorEndpoint` metadata and `RequireVendorPermission`; the authentication middleware's principal-type wall.
- Host: `/api/wasel/v1/antiforgery/token`; `Modules:Wasel:Enabled` in appsettings, compose and `.env.example`.
- Teams: `ITeamAllocationDirectory` (contract + implementation). AI demo adapter: one branch for `vendor-review`.

## Known gaps
- **UAE Pass for vendors** is a stored setting only; the vendor UAE Pass flow is not built (ADR 0110).
- Vendor comments have no attachments or @mentions yet.
- The capacity view counts Teams allocations and charter members; tasks and sprint stories are not part of the allocation.

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host
cd web && npm ci && npm run dev        # http://localhost:5173
```
1. Mariam: Settings → Email → Mailpit. Portfolio → Vendors → *New vendor*.
2. Ahmed (PM of a project in planning): project → *Vendors (Wasel)* → assign the vendor (access until a date) → invite a Lead and a Contributor.
3. Mailpit: open the invitation → `/wasel/set-password` → sign in at `/wasel/login` → enrol the authenticator → *My projects*.
4. Vendor: *Plan* → add a phase and milestones → Lead submits. Ahmed: *Vendors (Wasel)* → review the proposal, edit a title, accept.
5. Move the project to Execution, set *Vendor then PM*, PM Office *Open window now*. Vendor: *Monthly update* → values, evidence → submit.
6. Ahmed: review side by side, *Edit then accept* one value, accept the other → *Monthly* tab shows the vendor chips → submit; approvers see the origin.
7. Vendor: the PM's change and *Included in approved update*. Portfolio → *Capacity*, *Portfolio reviews*; a project's Gantt → export PNG in Arabic.

## Cloud environment notes
- PostgreSQL 16 local (`max_connections = 600`, `STRATEGY_TEST_POSTGRES`); Gotenberg / Mailpit containers through `mirror.gcr.io`; Tesseract installed during the session.
- E2E with `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium-1243/chrome-linux64/chrome` on a fresh `strategy` database.
- The web screens were built by three parallel helpers on separate files (vendor portal; PM-side screens and register; capacity / reviews / Gantt font) and wired by the shared routes and namespaces committed first.
- Two problems found and fixed by tests: duplicate proposal keys raised a 500 (now a validation error); the deliverable upload deadlocked when wrapped in a transaction with the Files service (the file is now stored first). A third, found by the UI helper: contract / meeting / member DTOs did not return the new fields, so editing cleared them.
- Screenshots (`E2E_SCREENSHOTS=phase-11`) in [`docs/screenshots/phase-11/`](../screenshots/phase-11/) as `ar-*` / `en-*` pairs: `vendor-invitation`, `wasel-login-mfa`, `vendor-project-overview`, `plan-proposal`, `monthly-submission`, `pm-review-diff`, `vendor-sees-review`, `vendor-rating`, `capacity-view`, `portfolio-review`, `gantt-export` (the PNG produced by the export button).
