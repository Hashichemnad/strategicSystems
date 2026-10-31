# Phase 2 — Access control and Settings centre: summary

**Status:** built and verified in the build session · awaiting owner acceptance
**Branch:** `claude/wonderful-allen-54wmu6`
**Planning:** the plan is in the design notes [`docs/design/access.md`](../design/access.md) and [`docs/design/settings.md`](../design/settings.md), written before the code. This session ran without an interactive approval step, so the plan was written first and then implemented as it stands. Its open choices are recorded as ADRs 0015–0017.

## What was built

| Area | Result |
|---|---|
| Access contract (`Strategy.Platform.Access.Contracts`) | **Core contract:** `IAccessService.CanAsync(principal, permission, resource?)` and `ApplyScopeAsync(query, principal, permission, ScopeMap<T>)`. **Supporting types:**<ul><li>`ResourceRef` carries the owner and unit, so a record check needs no second query</li><li>`DataScope`</li><li>the code-declared catalogue (`IAccessCatalogueSource`: system, permissions with AR/EN labels, default roles, record roles)</li><li>`IRecordRoleService`, `ISystemState`, `IUserDirectory`, `IUserRoleQuery`, `IRoleTransferHook`, `AccessChangeSignal`</li></ul>The split lets `Platform.Http` and every module depend on the contract only. |
| Access (`Strategy.Platform.Access`, schema `access`) | **Three layers:**<ul><li>**System access** (user / unit and below / everyone)</li><li>**System roles** with a **data scope** per permission (own, team, unit, unit and below, all)</li><li>**Record roles** (`access.record_role`)</li></ul>**Effective access** = system enabled AND system access AND (role permission in scope OR record-role permission). **Data model:**<ul><li>systems registry with **module enable / disable** and **owning unit**</li><li>roles and assignments with effective dates</li><li>locked access-administrator role</li></ul>**Supporting pieces:**<ul><li>**Catalogue synchronisation** after migrations: permissions follow the code; roles are seeded once and stay editable.</li><li>**Bulk role transfer**, logged, with the hook ready for Phase 4 re-routing.</li><li>**Effective access** of a user.</li><li>**Access review** report, with Excel export in AR (right-to-left sheet) and EN.</li><li>A per-user snapshot cached for 60 s; it is dropped on every access change and on organisation changes.</li></ul> |
| How modules use it | `RequirePermission(...)` on the endpoint gives 403 when the user has no possible grant. **Handlers:** a record outside the read scope → **404**; readable but the action not allowed → **403**. **Lists:** `ApplyScopeAsync` turns the scopes into one SQL predicate built from the module's `ScopeMap`: `IN (…)` on unit / owner ids, plus the record ids from record roles. **Sample module, the worked example:**<ul><li>owner + unit</li><li>a record role `editor`, granted through its own API</li><li>a number from the Numbering service</li></ul> |
| Settings (`Strategy.Platform.Settings`, schema `settings`) | **Typed settings:**<ul><li>**Regional**: time zone, Hijri display **off by default**, Arabic-Indic digits, currency, date format</li><li>**Branding**: names, colour, e-mail header and footer, light / dark logos</li></ul>**Lists:** **lookups** (AR/EN, order, active, parent value, metadata). **Numbering:**<ul><li>tokens `{YYYY} {YY} {MM} {UNIT} {SEQ:n}`</li><li>counter partitioned by the rendered prefix</li><li>incremented **in the caller's transaction**</li></ul>**Switches and labels:**<ul><li>**feature flags**</li><li>**translation overrides**, served publicly and merged live on the client</li></ul>Public client settings drive formatting, brand colour and logo. |
| Phase 1 interim pieces replaced | **ADR 0009 → roles (ADR 0016):**<ul><li>`is_system_admin` dropped; existing administrators are migrated to the four platform administrator roles</li><li>MFA "for administrators" = holders of a role that requires MFA</li><li>last-administrator guard</li><li>`set-admin`, `isSystemAdmin` and the interim access class are removed</li><li>Development / Testing no longer open modules to everyone</li></ul>**ADR 0014:**<ul><li>**employee grade is a lookup** (`employee.grade`); legacy free-text grades are added at upgrade, and the import reports grade errors per column</li><li>org settings and security screens are listed in the Settings centre</li><li>identity e-mails use the branding</li></ul>**Sidebar:** filtered by **real access** (`/api/v1/access/me`) instead of the cosmetic admin check. |
| Web | **Settings centre** (`/settings`): hub plus a secondary navigation filtered by permission.<ul><li>**General:** Regional · Branding · Systems · Features</li><li>**Data:** Lists · Numbering (live preview) · Translations (every bundled key, AR/EN)</li><li>**Access:** **Roles & permissions** (matrix of roles × permissions with a scope per cell; record-role tab) · **Assignments** (user / unit / everyone, dates, end) · **System access** · **Bulk transfer** · **Access review** (filters, Excel AR/EN)</li><li>the Phase 1 identity and organisation administration pages</li></ul>**Elsewhere:**<ul><li>user and unit pickers</li><li>grade dropdown</li><li>token creation hidden when the feature is off</li><li>org pages show their actions by permission</li><li>AR/EN for everything</li></ul> |

## Acceptance checks (BUILD_PLAN Phase 2)

| Check | Evidence |
|---|---|
| "Unit-and-below" sees only its unit tree; all 5 scopes tested | **Integration** `DataScopeTests` (real PostgreSQL), on a unit tree with a viewer, a direct report in a child unit, a colleague and an outsider. For each of **own, team, unit, unit and below, all** it checks:<ul><li>the exact list (SQL filter)</li><li>200 / **404** per record</li></ul>Further cases:<ul><li>**403** on an item that is readable but not editable</li><li>combined scopes of several roles</li><li>"unit and below" follows a unit move at once</li><li>**headed units**: a viewer appointed head of another unit (with a child unit) sees it under Unit and its subtree under Unit-and-below, Team is unchanged, and the access goes as soon as the head is removed</li></ul>**Unit** `ScopeEvaluationTests`: the same table for the record check and for the query predicate. |
| Granting a record role gives access to exactly that record | `RecordRoleTests`: a user with system access but no role sees nothing (403). After an editor record role on item 1:<ul><li>the list contains only item 1</li><li>item 1 can be read and edited</li><li>item 2 → 404</li><li>create → 403</li></ul>Grant and revoke also work through the module's own API. Only someone who may edit the item can share it. |
| Disabling a system hides it from the sidebar and blocks its API | Integration `SystemAccessTests`:<ul><li>`/sample/*` → 404 `module.disabled`</li><li>`/access/me` drops the system and its permissions</li><li>re-enable works again</li><li>platform systems cannot be disabled</li><li>system-access grants by user / unit / everyone</li></ul>**E2E** `settings.spec` (ar + en): the switch in Settings → Systems hides *Sample module* from the sidebar and the API answers 404, then both come back. |
| Lookup values and numbering formats take effect without redeploy | **Integration:**<ul><li>a new grade value is accepted by the employee API at once, and refused after deactivation</li><li>dependent (parent) values</li><li>a changed pattern gives the next item `ITEM/26/001`; widening `{SEQ}` continues the counter</li><li>20 concurrent creates → 20 unique consecutive numbers</li></ul>**E2E:** value added and pattern changed in the UI (live preview), and the next item carries the new number. |
| Translation override changes a label live in both languages | **Integration:** public overrides in AR and EN; clearing an override removes it. **Vitest:** overrides apply, and clearing restores the bundled text. This caught a bug: i18next mutated the bundled resources; it now gets a copy. **E2E:** the sidebar label changes at once, in the current language and after switching. |
| Access review report exports to Excel | **Integration:** review rows (source user / everyone, system access flag). The xlsx export is opened with ClosedXML: Arabic headers on a right-to-left sheet, English headers left-to-right, the user's row present. **E2E:** the download is offered from the review page. |

**Further authorization coverage (task request).** `AuthorizationMatrixTests` is generated from the endpoint catalogue, so it covers **every Phase 0–2 endpoint** (and every later one):

| Check | Result |
|---|---|
| Every non-public endpoint, anonymous caller | 401 |
| Every permission-guarded endpoint, signed-in user without its permission | 403 (only the organisation directory reads are granted to everyone) |
| Every guarded endpoint, user holding **only** that permission | gets past the check |
| Every declared permission | exists in the seeded catalogue with AR/EN labels |

`MigrationTests` also upgrades a **Phase 1 database**:
- a flagged administrator becomes a holder of the four roles;
- a free-text grade becomes a list value.

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet format --verify-no-changes` | clean |
| `dotnet test --solution Strategy.slnx` | **666 / 666** passed: unit 235 · architecture 30 · integration 401 on real PostgreSQL. Integration includes 297 generated authorization-matrix cases and the external IdP tests against Keycloak and OpenLDAP |
| `tools/check-vulnerable.sh` · `tools/check-api-client.sh` | no High / Critical advisories · OpenAPI document and TS client up to date |
| `npm run lint` · `format:check` · `typecheck` | clean |
| `npm test` (Vitest) | 69 / 69 passed. New tests cover navigation by access, translation overrides and regional formatting. The existing check that every server message key has AR + EN text covers all the new keys |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (14 moderate advisories in dev tooling, `minimatch` via ESLint; the lockfile is unchanged in this phase) · 580 packages allowed |
| `npx playwright test` | **40 / 40** passed (20 scenarios × ar + en). The 7 new Phase 2 scenarios passed in every run, and the full suite ran twice |
| `--migrate` on an empty database | all schemas (`settings`, `access`, `core`, `identity`, `sample`) applied, then the seeders ran |

No dependency was added (THIRD_PARTY_LICENSES unchanged).

## Decisions (ADRs)
- [0015](../decisions/0015-data-scope-semantics.md) Data scope semantics:
  - "team" = the user plus direct reports;
  - the unit scopes cover the unit the employee is placed in **and every unit they head** (amended after owner review; deputies excluded; team unaffected);
  - scopes from several roles are combined with OR;
  - `ApplyScopeAsync` with a `ScopeMap`.
- [0016](../decisions/0016-roles-replace-interim-admin.md) Roles replace the interim administrator flag (supersedes 0009):
  - the migration;
  - MFA via `requires_mfa`;
  - the locked access-administrator role;
  - `org.viewer` assigned to everyone.
- [0017](../decisions/0017-settings-storage-and-numbering.md) Settings storage and numbering:
  - typed JSON areas;
  - counter partitioned by the rendered prefix, in the caller's transaction;
  - logos as interim bytes until Files;
  - translation overrides;
  - module switch in the systems registry.

## Deviations
- **From the SPEC:** none.
- **From the plan and the BUILD_PLAN prompt:**
  - **Contract change:** `ApplyScope` became `ApplyScopeAsync` and takes a `ScopeMap<T>`, because scopes need data from the organisation (ADR 0015).
  - **Assignments:** these are role assignments to users, units (and below) or everyone. The SPEC's "users / roles can open each system" maps to system-access grants to the same principal kinds.
  - **Sidebar:** the Administration section now has one *Settings* entry. Users, identity providers, security and e-mail are pages of the Settings centre (the routes `/admin/*` are unchanged).

## Fixes found along the way
- **Translations:** clearing a translation override did not bring the bundled text back, because i18next merged into the exported resource objects. It now initialises from a copy (Vitest test).
- **Access cache:** a unit move could leave "unit and below" stale for up to a minute. Organisation saves now raise `AccessChangeSignal` (integration test moves a unit and checks the scope at once).
- **Sidebar label:** a renamed sidebar label broke the Phase 1 shell specs. It was reverted rather than editing those tests.

## Known gaps (by design: later phases)
- **Audit trail:** permission, role, settings and transfer changes are stamped (`created_*` / `updated_*`), logged and, for transfers, kept in `access.role_transfer`. The hash-chained audit trail with old → new values arrives in **Phase 3**.
- **Bulk transfer:** re-routing of pending approvals and tasks is a hook with no implementation until the approval engine (**Phase 4**).
- **Files:** logos are stored in `settings.branding_asset` until the Files service (Phase 3).
- **Lists:** Excel import of list values comes with the first module that needs bulk lists (Q-P7). The editor is in place.
- **Translations:** other open sessions pick up an override on their next load or within 5 minutes; the editing session sees it at once.
- **Rules and thresholds:** modules register their own typed settings areas (e.g. PMS budget threshold) when they are built. The storage and validation pattern is in place.

## How to demo
```bash
docker compose -f deploy/docker-compose.yml up -d postgres mailpit
dotnet run --project src/Host/Strategy.Host          # Development: migrations, seeders, demo seed on an empty database
cd web && npm ci && npm run dev                       # http://localhost:5173
```
1. **Mariam (administrator).** Sign in as `mariam@strategy.local` / `Strategy#Demo2026` (MFA). Open **Settings**.
2. **Roles & permissions:**
   - choose *Sample module*;
   - change *Editor → Edit items* from *Own* to *Unit and below*, then save.
3. **Ahmed (regular user).** Sign in as `ahmed@strategy.local`:
   - the sidebar shows the organisation pages and *Sample module*, but no Settings;
   - `/settings` shows "no settings available";
   - the API answers 403.
4. **Systems:** switch *Sample module* off. It disappears from the sidebar and `/api/v1/sample/items` answers 404. Switch it on again.
5. **Lists → Grade:** add a value. It is offered at once in the employee form.
6. **Numbering:**
   - set `ITEM/{YY}/{SEQ:3}`; the preview updates;
   - create a sample item: it gets the new number.
7. **Translations:** search `nav.home` and override AR and EN. The sidebar changes at once; switch the language to see the other text. Clear both fields to go back.
8. **Assignments:** give Ahmed *Access reviewer*. **Access review:** filter and export to Excel (AR and EN).
9. **Bulk transfer:** move *Sample → Editor* from Ahmed to another user with a reason. The history lists it.

## Cloud environment notes
- Docker is started manually (`dockerd`); images come through `mirror.gcr.io/`. Integration tests reuse one PostgreSQL with `STRATEGY_TEST_POSTGRES`.
- The sandbox's preinstalled Chromium is a newer revision than `@playwright/test` 1.56.1 expects. The config takes an optional `E2E_CHROMIUM_PATH` (set to `/opt/pw-browsers/chromium` here); CI is unchanged.
- Screenshots (generated by the E2E suite) are in [`docs/screenshots/phase-2/`](../screenshots/phase-2/), as `ar-*` and `en-*` pairs.
  - **New in Phase 2:**
    - settings hub;
    - permission matrix and record roles;
    - assignment dialog and list;
    - access review;
    - systems;
    - lists;
    - numbering;
    - translations;
    - regional;
    - branding;
    - a regular user's sidebar.
  - **Earlier phases, retaken:** all of them, with the new sidebar.
