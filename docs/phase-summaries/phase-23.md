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

TEST-TABLE

## Quality gates

GATES

## Screenshots (Arabic and English, `docs/screenshots/phase-23/`)

SCREENSHOTS

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
GAPS
