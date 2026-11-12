# Phase 23 — Salesforce standby sync and the version 1.0.0 release: summary

Status: **built — awaiting owner acceptance** · version **1.0.0**, tag `v1.0.0` (covers Phases 0–23).
Requirements: [`docs/SPEC-ADDENDUM.md`](../SPEC-ADDENDUM.md) §G (and §B, the external references it builds on) · design note:
[`docs/design/salesforce-sync.md`](../design/salesforce-sync.md), written and committed before the code. This session ran without an
interactive approval step, so the design note was written first and implemented as it stands; its open choices are ADRs **0200–0208**
(proposed). The Phase 21 known gap "codes without labels" is closed by ADR **0209** (proposed).
Guides: [`docs/guides/en/salesforce-sync.md`](../guides/en/salesforce-sync.md) · [`docs/guides/ar/salesforce-sync.md`](../guides/ar/salesforce-sync.md).

## What was built

| Area | Result | ADR |
|---|---|---|
| **Connector framework** (`Strategy.Platform.Integration/Sync`) | Schema `integration` gains connections, mapping packs and object mappings, runs and run log, exceptions, people matches, source history, outages and outage records (migration `SalesforceStandbySync`). Connector-neutral engine: per enabled object by load order, change detection by hash, external references (Phase 19) as identity, one DI scope per record, new audit channel **`sync`** | 0200, 0205 |
| **Salesforce connector** | OAuth 2.0 **JWT bearer** (RS256 assertion built in-house, private key PEM encrypted with Data Protection, only the thumbprint shown), configurable API version, describe global / object, REST `queryAll` with `nextRecordsUrl`, **Bulk API 2.0** `queryAll` jobs with CSV pages for full / large loads (`SELECT COUNT()` against the bulk threshold for deltas), incremental by `SystemModstamp` with a 5-minute overlap, `IsDeleted` → "Deleted in Salesforce", `/limits` per run (stops below a reserve), 3 retries with back-off, token renewal on 401. **Read-only by construction**: the client refuses any request other than GET, the token POST and Bulk *query* job creation (unit- and integration-tested) | 0201 |
| **Sync targets** | Modules declare the record types and the allow-listed properties the sync may write (`AddSyncTarget`); the generic `EntitySyncWriter` writes them through the module's own DbContext (EF change tracking, audit); custom writer for organisation units (hierarchy and versions); hooks for derived data (record roles of projects, teams, tasks, CAP, audit, benchmarking; KPI performance recalculated with `PerformanceCalculator`; risks re-rated by the framework; KRIs linked to KPI cards). No approval instance, event or notification is produced for synced records. Targets: org units; Portfolio request, project, work plan, project KPI / risk / stakeholder / planning item / challenge / change request / contract / lesson / meeting; Teams team, member, yearly team and member evaluations; KPI card, plan, result; Risk risk, control, KRI; CAP source, remark, action; Audit report, finding, recommendation; Benchmarking study; Document Registry document; IP record; Tasks task | 0202 |
| **Mapping model and editor backend** | Field mappings (direct, picklist → code, record type, reference → external reference, person → employee, constant), value maps with fallback, transforms (trim, upper / lower, percent → ratio, date only, HTML → text, truncate, Yes/No), defaults, ignore list, SOQL filter (checked), external-ID templates (`{Id}-{Evaluation_Year__c}` for one evaluation per year), parent-child load order with multi-pass for self-references. **Validation against the live describe** (fields incl. one relationship hop, picklist values, kinds, required targets, unmapped custom fields) before an object can be enabled; any edit clears it. Pack export / import (`strategy.sync-pack/1`) | 0203 |
| **ADoF Salesforce pack** | Embedded, one file per system (`Sync/Packs/adof-salesforce/*.json`), seeded with a disabled connection. Objects visible to the integration user today (G3: departments, users, teams and members, PMS request, project, work plan and all PMS children, files, approval and field history) mapped from the org's model; objects of the other systems (KPI, Risk incl. `Risk_Kpi_Relation__c`, CAP, Audit, Benchmarking, Document Registry, IP, Tasks / timesheets) **provisional and disabled** until *Validate* confirms them. Model differences documented per object (AR / EN notes) and implemented: additive risk scores re-rated, team / member scores → one evaluation per year, risk actions that became controls → controls (second mapping with a filter), KPI "-" text targets → Range target type, `Approval_Status__c` → stage + sub-state snapshot with the approval history as source history | 0203 |
| **People matching** | `User` and `Contact` matched to existing employees by e-mail, employee number or Federation ID (configurable order, ambiguous matches not guessed); never creates employees; *Map person* for the rest | 0204 |
| **Exceptions queue** | One open exception per record with reason (unmapped value, unknown user, missing parent, invalid value, required missing, write failed, file rejected), field, value and the source payload; *Map value* (adds to the value map and re-processes), *Map person*, re-process one / many from the stored payload (no Salesforce call; older payloads never overwrite newer data), ignore; missing parents retried at the end of each run | 0205 |
| **Files and source history** | Latest `ContentVersion` of documents linked to synced records and classic `Attachment`s uploaded through the Files service (allow-list, magic number, size, scan) on the owner type's attachment purpose; new versions add file versions. `ProcessInstance` / `ProcessInstanceStep` and `…__History` into `integration.source_history`; read-only "Source history" on the record page | 0208 |
| **Standby guard** | `IStandbyGuard` (Kernel) implemented by Integration. **One write rule** in `StandbyWriteInterceptor` on every module DbContext: with a connection in standby and channel ≠ `sync`, updates / deletes of synced records and changes of their audited child rows → 409 `integration.sync.readOnly` — UI, REST, MCP (tool error `read_only`), imports, AI drafts and jobs alike. Approval engine refuses submit / decide; Quartz trigger listener vetoes jobs of the suspended systems (run shown as "suspended"); notifications of suspended systems are not queued. Local-only records stay editable | 0206 |
| **Failover** | Mode state machine `disabled → standby ⇄ primary → reconciling → standby` (transition table unit-tested). *Make this system primary* with reason and **MFA re-prompt** (`IStepUpVerifier`: TOTP or e-mail code, audited) opens an outage and flags records with a pending Salesforce approval "Resubmit required" (cleared when submitted here). **Outage changes report** from the audit trail (channel ≠ `sync`), grouped by system and record, old → new values with the Salesforce ID, Excel (RTL sheet for Arabic) and PDF (Gotenberg), re-entered marks. *Return to standby* needs the report exported / acknowledged; records changed during the outage hold newer Salesforce versions as conflicts; *Keep local* / *Take Salesforce* per record; *Close outage* when none is pending | 0207 |
| **Schedule, monitoring, alerts** | Every N hours (anchored), daily, weekly on days, in the installation time zone, next five runs previewed; job `integration.salesforce-sync` (every minute: due and *Sync now* requests), job `integration.sync-monitor` (late alerts, log retention); bilingual e-mail alerts on failure and when late (once per late period); health-page check `salesforceSync` (ok / late / failed / exceptions / primary) | — |
| **Permissions** | `integration.sync.view`, `…manage`, `…failover`; new role `integration.sync-operator` (MFA); the Integrations administrator holds all three | — |
| **Screens** (AR + EN) | Settings → Integrations → **Salesforce sync**: status, connection, schedule, mappings with the live-describe editor, exceptions, runs, failover (make primary, outage report, return to standby, conflicts, close). Record pages: "Synced from Salesforce — read-only", "Deleted in Salesforce", "Resubmit required" badges and the source history panel | — |
| **Demo / E2E stand-in** | `MockSalesforceOrg` (Host): the in-process fake org used by the integration tests and, with `Development:MockSalesforce=true` in Development only, by the E2E screenshots (`/dev/mock-salesforce`) | — |
| **Phase 21 gap closed** | Every code field of the Phase 17 datasets, Phase 21 report types / dossiers and Phase 22 feeds has AR / EN labels (fixed choices or module label sources); one formatter for reports, charts, dossiers and exports; a catalogue test fails on any unlabelled code field | 0209 |
| **Release 1.0.0** | CHANGELOG (Phases 0–23, dated 2026-11-12), in-app release notes (AR / EN) updated, tag `v1.0.0` | — |

## Requested tests

| Requirement | Evidence |
|---|---|
| Fake Salesforce in CI, no live org | `MockSalesforceOrg` (in-process `ISalesforceTransport`: recorded-style describe, REST `queryAll` with paging, Bulk API 2.0 jobs with CSV pages and locators, JWT token endpoint verifying the RS256 assertion, `/limits`, file downloads). Every request is logged; any non-read request is recorded as a violation and every test asserts there are none |
| Idempotent re-runs create no duplicates | `Full_load_and_incremental_runs_are_idempotent_pick_only_changes_and_mark_deletions`: second run and a second full load → 0 created / 0 updated, one record per number |
| Incremental picks only changed records | same test: after one change and one new record the REST delta reads only them (plus the record on the watermark overlap, unchanged) |
| Deletions marked, not removed | same test: `IsDeleted` → reference `deleted_in_source`, record still there, sync-state badge |
| Picklist and user mapping, unmapped value → exceptions → fix → re-process | `Unmapped_values_and_unknown_users_go_to_the_exceptions_queue_and_reprocess_after_the_fix`: new status value and an unknown user → exceptions with reason / field / value / payload; *Map value* and *Map person* re-process and create the records; bulk re-process clears the queue |
| Parent-child order | `Projects_load_parent_before_child_with_work_plan_files_and_approval_history`: child department and work-plan task listed before their parents (multi-pass); project → request reference, milestone → header phase |
| Files and approval history imported | same test: charter PDF through the Files service on the project, approval step and pending instance as source history; second run imports nothing twice |
| Standby read-only on API, UI, MCP and imports | `Standby_makes_synced_records_read_only_on_api_approvals_mcp_import_and_suspends_jobs_and_notifications`: REST update 409 `integration.sync.readOnly` (the UI uses the same endpoints and shows the badge), submit for approval 409, MCP write tool → `read_only`, organisation import of a synced unit 409; local records stay editable |
| Jobs and notifications suspended | same test: job gate vetoes `portfolio.*` jobs and not the sync job; a notification of a suspended system is not queued |
| Failover unlocks and re-enables jobs | `Failover_unlocks_records_reports_every_change_and_return_to_standby_honours_the_choices`: wrong MFA code and missing reason refused; records editable, jobs not suspended, sync now refused while primary; "Resubmit required" from the pending Salesforce approval |
| Outage report lists every change | same test: old → new values with the Salesforce IDs, no `sync` channel entries, Excel export (acknowledges), re-entered mark |
| Return to standby honours keep-local choices | same test: report acknowledgement required; both records held as conflicts; *Keep local* keeps the local value through a later run, *Take Salesforce* applies at once; close refused while a conflict is pending; after closing Salesforce wins again and records are read-only |
| Schedule with the fake clock | `Scheduler_runs_due_connections_on_the_fake_clock_and_alerts_on_failure_and_delay` (due run, next run, *Sync now* picked up, failure e-mail, late e-mail once, health check) and unit `SalesforceSyncRulesTests` (interval / daily / weekly in Asia/Dubai, validation) |
| Configuration and permissions | `Connection_validation_and_permissions_guard_the_configuration`: invalid key refused, test connection (JWT verified by the fake), live-describe validation, edit clears confirmation, unconfirmed object cannot be enabled, unsafe filter refused, non-admin 403, key never returned; authorization matrix covers every new endpoint |
| Pure rules | `SalesforceSyncRulesTests` (mode transition table, field mapping kinds / transforms / conversions / issues / hash / external-ID templates, SOQL builder and filter safety, Bulk CSV, JWT assertion, read-only request guard) and the module tests `TeamsSyncTargetsTests`, `KpiSyncTests`, `RiskSyncTests` (pack against declarations, Range rule, re-rating) |
| E2E (ar + en) | `web/e2e/salesforce-sync.spec.ts` against the Development stand-in org: configuration, full load, status, exceptions, read-only project, make primary with a TOTP code, outage report, return to standby with a conflict, keep local, close — **2 / 2** |

## Quality gates

Local runs in the build sandbox (SDK 10.0.114, PostgreSQL 16, Chromium 1243, Docker for Mailpit / Gotenberg).

| Gate | Result |
|---|---|
| `dotnet build` (Host and tests) · `dotnet format Strategy.slnx --verify-no-changes` | 0 warnings, 0 errors · clean |
| Unit tests | **2 298 / 2 305** — the 7 failures are the OCR tests, which need Tesseract (not installed in this sandbox; CI installs it, CLAUDE.md §18) |
| Architecture tests | **32 / 32** |
| Integration tests (real PostgreSQL, full suite) | **3 389 / 3 392** on the full run; the 3 failures: the OCR integration test (Tesseract), the health-page list (gains `salesforceSync`) and the authorization matrix (sent a file to the JSON pack import) — both expected updates fixed and their classes re-run green (**2 940 / 2 940**, incl. the job dashboard list gaining the two sync jobs) |
| Salesforce sync integration tests | **7 / 7** |
| `npm run lint` (incl. RTL logical-class check) · `format:check` · `typecheck` | clean |
| `npm test` (Vitest) | **482 / 482** (server message keys of the sync translated in AR and EN, badges, source history, make-primary dialog, exceptions, outage report) |
| `npm run build` | OK |
| OpenAPI document · TS client | regenerated, no drift |
| E2E `e2e/salesforce-sync.spec.ts` (ar + en) | **2 / 2** |

Not run in this sandbox: the full E2E / accessibility suite, visual regression, `npm audit` / `check-vulnerable.sh` (no new package: ClosedXML was already in the solution) — CI runs them on the pull request.

## Screenshots (Arabic and English, `docs/screenshots/phase-23/`)

`sync-connection` (connection settings), `sync-schedule`, `sync-mapping-editor` (live describe next to the field mappings), `sync-exceptions`,
`sync-status`, `sync-read-only-record` (project with the "Synced from Salesforce — read-only" badge), `sync-make-primary` (reason and
MFA code), `sync-outage-report`, `sync-return-to-standby` (conflicts after returning, keep local / take Salesforce) — each as `ar-*.png`
and `en-*.png`.

## How to demo

1. `dotnet run --project src/Host/Strategy.Host` with `Development__MockSalesforce=true` (Development); sign in as the administrator.
2. Settings → Integrations → **Salesforce sync** → Connection: values from `GET /dev/mock-salesforce` (login URL, consumer key, username,
   private key) → *Test connection*.
3. Mappings: *Validate* and enable Department, User, Program request, Project, Work plan, Files, Approval history → Status: *Activate* →
   *Full load*. Open the synced project: read-only badge, Source history, the charter file.
4. Exceptions: add a record with a new picklist value through `POST /dev/mock-salesforce/change`, *Sync now*, *Map value*.
5. Failover: *Make this system primary* (reason + code) → edit the project → Outage changes report → export → *Return to standby* →
   change the same record in the mock org → *Sync now* → Conflicts → *Keep local* / *Take Salesforce* → *Close outage*.

## Decisions (ADRs, proposed)
0200 sync engine and channel `sync` · 0201 Salesforce connector · 0202 sync targets declared by modules · 0203 mapping packs as data ·
0204 people matching · 0205 change detection and exceptions · 0206 standby guard · 0207 failover, outage report, return to standby ·
0208 files and source history · 0209 labels for fixed-code report fields.

## Deviations and known gaps
- **Provisional objects.** The integration user does not see the KPI, Risk, CAP, Audit, Benchmarking, Document Registry, IP and Tasks
  objects today (G3); their pack entries use plausible names and stay disabled until *Validate* confirms them against the org. Some field
  names of visible objects (e.g. `Task_Name__c` on the work plan, `Contact` people) are also confirmed only by *Validate*.
- **Employees are never created by the sync** (ADR 0204): unmatched Salesforce users / contacts go to the exceptions queue.
- **Source scores not stored**: KPI `Performance__c` and the additive risk score are recalculated here (calculator / framework); the entities
  have no field for the source value. Per-criterion team / member evaluation answers are not carried (only overall score and points).
- **"Re-entered" marks are per record**, not per individual change.
- **KPI result status**: only `Approved__c` is mapped (approved / open); "submitted, awaiting approval" arrives as open.
- **Audit reports** are derived from the observations' report picklist (no report object in Salesforce) — see the pack notes.
- **Live org check**: during the build a sub-agent read the connected org's describe and a few records (read-only) to confirm field names;
  `Evaluation_Year__c` replaced `{year:End_Date__c}` for yearly evaluations because `End_Date__c` is empty for most standing committees.
- **Mock org** (`Development:MockSalesforce`) is mapped only in the Development environment, like the UAE Pass stand-in (ADR 0154).
