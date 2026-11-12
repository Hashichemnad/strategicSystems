# Design note — Phase 23: Salesforce standby sync (`Strategy.Platform.Integration` → `Sync/`)

**Scope.** SPEC-ADDENDUM §G (all) on top of §B (external references, Phase 19) — one-way copy **Salesforce → this system** of all 11
systems' data plus departments, users / employees, teams and tasks, so this system can take over when Salesforce is unavailable. **This
system never writes to Salesforce**: the connector only sends `GET` requests and Bulk API 2.0 *query* jobs (a query job is created with a
`POST` to `/jobs/query`, which reads data; no DML, no Apex, no metadata calls — ADR 0201). After an outage, local changes are re-entered in
Salesforce by people, helped by the outage changes report (§G6).

ADRs **0200–0208** (proposed): 0200 sync engine and the `sync` channel · 0201 Salesforce connector · 0202 sync targets declared by modules ·
0203 mapping packs as data · 0204 people matching · 0205 change detection, exceptions queue and re-processing · 0206 standby guard ·
0207 failover, outage and return to standby · 0208 files and source history. (0209: labels of fixed-code report fields, the Phase 21 gap.)

Reading done for this note: SPEC-ADDENDUM §B, §G; BUILD_PLAN-PART2 Phase 23; phase summaries 19 and 22; design notes
`external-references.md`, `integrations.md`; ADRs 0020 (outbox / Quartz), 0023 (shared transaction), 0043 (integration events),
0155 (outbound URL policy), 0164 / 0165 (external references, upsert); SPEC §1 A–C (Program Request, Project, Work plan), §2 A–C
(KPI card / plan / result), §3 (Risk ↔ KPI), §4 (teams, evaluation), and the Salesforce PMS data model (objects and fields listed in §8).

---

## 1. Building blocks and where they live

| Piece | Project / folder | Notes |
|---|---|---|
| Sync target contract (`SyncTargetDefinition`, field kinds, `AddSyncTarget`, custom writer interface) | `Strategy.Platform.Integration.Contracts/SyncTargets.cs` | Kernel only (architecture test) |
| Standby guard contract (`IStandbyGuard`, `RecordReadOnlyException`) | `Strategy.Platform.Kernel/Integration/StandbyGuard.cs` | consumed by Persistence, Workflow, Scheduling, Notifications without referencing Integration |
| Read-only interceptor | `Strategy.Platform.Persistence/StandbyWriteInterceptor.cs` | added to every module DbContext next to the audit interceptors |
| Connector framework: connections, schedule, mapping packs, runs, run log, exceptions, people map, source history, outages | `Strategy.Platform.Integration/Sync/*` | schema `integration` (existing), one new migration |
| Salesforce connector | `Strategy.Platform.Integration/Sync/Salesforce/*` | `HttpClient` named `salesforce`; test doubles replace the primary handler |
| Generic entity writer | `Strategy.Platform.Integration/Sync/EntitySyncWriter.cs` | writes declared properties of a module entity through **that module's DbContext** (EF change tracking, audit interceptor, channel `sync`) |
| Module declarations | `src/Modules/<M>/…/Infrastructure/SyncTargets.cs`, Organisation `Application/OrgSyncTargets.cs` | each module declares which of its record types the sync may write and which properties |
| ADoF Salesforce mapping pack | `Strategy.Platform.Integration/Sync/Packs/adof-salesforce.json` (embedded) | seeded on first start (inactive connection), export / import as JSON |
| Step-up MFA | `Strategy.Platform.Identity/Application/StepUpVerifier.cs` | `IStepUpVerifier` (TOTP or e-mail code of the signed-in user) |
| Screens | `web/src/features/integration/sync/*` | Settings → Integrations → **Salesforce sync** (tabs below) |

The sync runs with the **system principal** and channel **`sync`** (new channel, CLAUDE.md §8 list extended; ADR 0200): every write it makes is
audited like any other business change, and the standby guard lets exactly this channel write synced records.

## 2. Data model (schema `integration`, new tables)

All tables have the standard stamps and `xmin`; configuration tables are `[Audited]`; run log / exceptions / source history are technical
logs (not entity-audited, like Phase 22's delivery log) except the actions taken on them (re-process, ignore, map), which are audited as events.

**`sync_connection`** — one per source system (v1 UI manages the Salesforce one): `source_system` (code, unique), `connector` (`salesforce`),
`enabled`, `mode` (`disabled` · `standby` · `primary` · `reconciling`, §6), `login_url` (`https://login.salesforce.com`, `https://test.salesforce.com`
or the My Domain URL — HTTPS, checked by the outbound URL policy, ADR 0155), `client_id` (Connected App consumer key), `username` (integration
user), `private_key_protected` (PEM, Data Protection purpose `integration.salesforce.key.v1`, never returned — the API shows only the
certificate thumbprint / "key stored"), `api_version` (default `v62.0`), `bulk_threshold` (default 2 000 records), `page_size` (REST, 2 000),
`schedule` (jsonb, §4), `next_run_at`, `late_after_minutes` (default 2 × interval), `mapping_pack_id`, `instance_url` (learned at token time),
`last_limits` (jsonb: daily API requests max / remaining, Bulk API batches, at), `last_test_at`, `last_test_ok`.

**`sync_mapping_pack`** — `code` (unique, e.g. `adof-salesforce`), name AR / EN, `pack_version`, description, `connector`. **`sync_object_mapping`** —
per pack and source object: `source_object` (API name), `kind` (`record` · `people` · `files` · `attachments` · `approvals` · `field_history`),
`target_type` (sync target, e.g. `portfolio.project`; null for people / history kinds), `enabled`, `load_order`, `confirmed_at` (live describe
validation passed, required before enabling), `where` (optional SOQL condition, validated: no `;`, no sub-queries outside the allow-list),
`watermark_field` (default `SystemModstamp`; `CreatedDate` for history objects), `external_id_template` (default `{Id}`; e.g.
`{Id}-{year:End_Date__c}` for "one evaluation per year"), `use_bulk` (default true; false for objects Bulk API cannot query, e.g. `ProcessInstance`),
`fields` (jsonb list of field mappings, §3), `defaults` (jsonb target field → value), `ignore` (jsonb source fields deliberately not mapped),
`notes_ar` / `notes_en` (documented model differences), `watermark` (last `SystemModstamp` seen), `last_full_load_at`.

**`sync_run`** — `connection_id`, `trigger` (`schedule` · `manual` · `full` · `reprocess`), `mode` (`full` · `incremental`), `status` (`running` ·
`succeeded` · `partial` (some records → exceptions) · `failed`), started / finished, `objects` (jsonb per object: read, created, updated,
unchanged, deleted, failed, conflicts, api calls, bulk / rest), `api_calls`, `message_key` (+ params) on failure. **`sync_run_log`** — run, at,
level, object, message key + params (no record data, no secrets).

**`sync_exception`** — `connection_id`, `run_id`, `source_object`, `external_id`, `reason` (`unmappedValue` · `unknownUser` · `missingParent` ·
`invalidValue` · `requiredMissing` · `writeFailed` · `fileRejected` · `unknownTarget`), `field`, `value`, `detail` (message key + params),
`payload` (jsonb, the source record as read — the input of re-processing), `source_modified_at`, `status` (`open` · `resolved` · `ignored`),
`attempts`, `resolved_at` / `by`. One open exception per (connection, object, external ID): a newer failure of the same record replaces the
payload and reason.

**`sync_person`** — people matching (ADR 0204): `source_system`, `source_object` (`User` / `Contact`), `external_id`, `employee_id`,
`matched_by` (`email` · `employeeNo` · `federationId` · `manual`), `email`. Unique (source, external ID).

**`source_history`** — read-only history from the source (ADR 0208): `source_system`, `entity_type`, `record_id`, `kind` (`approval` · `field`),
`external_id` (unique per source: `ProcessInstanceStep.Id` / `…__History.Id`), `occurred_at`, `actor_name`, `process_name`, `step_status`
(`Started` · `Pending` · `Approved` · `Rejected` · `Removed` · `Reassigned` · `NoResponse`), `comments`, `field`, `old_value`, `new_value`.

**`sync_outage`** — `connection_id`, `status` (`open` · `reconciling` · `closed`), `started_at` / `by`, `reason`, `returned_at` / `by`,
`report_acknowledged_at` / `by`, `closed_at` / `by`. **`sync_outage_record`** — `outage_id`, `entity_type`, `record_id`, `external_id`,
`resubmit_required` (open Salesforce approval at failover), `changed_locally` (from the audit trail at return), `re_entered` (+ by / at),
`conflict` (Salesforce sent a newer version while the outage is reconciling), `resolution` (`pending` · `keep_local` · `take_source`),
`pending_payload` (jsonb, the Salesforce version waiting for a decision), `pending_object`. Unique (outage, entity type, record id).

`integration.external_ref` (Phase 19) is reused unchanged: every synced record has one reference with `is_origin = true`, `sync_status`
`synced` / `deleted_in_source` / `error`, `source_modified_at` = `SystemModstamp`, `source_hash` = SHA-256 of the mapped source values.

## 3. Mapping model (ADR 0203)

A **field mapping** (`sync_object_mapping.fields[]`):

| Property | Meaning |
|---|---|
| `source` | Salesforce field API name (`Project_Stage__c`, `RecordType.DeveloperName`, `Owner.Email` — one relationship hop allowed) |
| `target` | sync-target field code (from the target's declaration, §5) |
| `kind` | `direct` (type-converted) · `picklist` (value → code through `values`; multi-select → JSON array of codes) · `reference` (Salesforce Id → internal record through `external_ref` of `referenceType`) · `person` (User / Contact Id → employee through `sync_person`) · `recordType` (record-type developer name → code through `values`) · `constant` (`value`) |
| `values` | value map `{ "Initiation": "initiation", … }` for picklist / record type; `*` key = fallback |
| `transform` | optional: `trim` · `upper` · `lower` · `percentToRatio` (÷ 100) · `ratio` · `dateOnly` · `htmlToText` · `truncate:<n>` · `yesNo` (Yes/No/TBD → true/false/null) |
| `required` | missing value → exception `requiredMissing` (target-required fields are always required) |

Rules: an unmapped picklist / record-type value (no entry, no `*`) → exception `unmappedValue` (field, value). A reference whose parent has
no external reference → `missingParent` (deferred within the run: same-object parents, e.g. work-plan header phases and parent departments,
are retried in further passes while a pass makes progress; other parents come earlier by `load_order`). An unknown person → `unknownUser`.
`defaults` fill target fields that the source leaves empty. `ignore` documents the source fields not mapped (the validator lists unmapped
fields that are neither mapped nor ignored as warnings).

**Validation against the live describe** (`POST …/mappings/{id}/validate`): every `source` field exists (relationship hop checked), every
`values` key is an active picklist value of the field (new Salesforce values not yet mapped are listed), reference fields point at an object
mapped with the expected target type, target fields exist and their kinds fit (date ↔ date / datetime, number ↔ double / currency / percent,
etc.). An object mapping can be **enabled only after a successful validation** (`confirmed_at`), and any later edit clears it.

**Pack export / import**: JSON document `{ format: "strategy.sync-pack/1", code, version, name, objects: [ … ] }` (no connection settings, no
secrets, no watermarks). Import creates or replaces the pack by code (objects keep their enabled flag only when unchanged); audited.

## 4. Schedule (Settings → Integrations → Salesforce sync → Schedule)

`schedule` = `{ kind: "interval" | "daily" | "weekly", everyHours (1–24), at ("HH:mm", installation time zone), days ([1–7], ISO Monday = 1),
businessHoursOnly (bool) }`. `SyncSchedule.Next(schedule, after, timeZone, businessHours?)` is a pure function (table tests with the fake clock):
interval runs are anchored at `at` (default 00:00) of the day so they do not drift; daily at `at`; weekly at `at` on the chosen days; with
`businessHoursOnly` a time outside the installation's business hours moves to the next business-hours start (`IBusinessHoursCalculator`, ADR 0027).
Job **`integration.salesforce-sync`** (every minute) runs a connection when `enabled`, mode `standby` / `reconciling` and `next_run_at <= now`
(one run at a time per connection: PostgreSQL advisory lock; a run that is still going is not started twice). **Sync now** (`POST …/sync-now`,
optional `full: true`, optional object list) queues the same run immediately. **Initial full load**: the first run of an object (no watermark),
and any `full` run, reads everything with Bulk API 2.0; later runs are incremental. Per-object enable / disable in the mapping editor.

## 5. Sync targets (ADR 0202)

A module declares, in its own project, the record types the sync may write:

```csharp
services.AddSyncTarget(SyncTarget.Entity<PortfolioDbContext, Project>(Project.EntityType, PortfolioPermissions.SystemCode, "مشروع", "Project")
    .Text("number", nameof(Project.Number), required: true).Text("nameAr", nameof(Project.NameAr)) …
    .Choice("stage", nameof(Project.Stage), ProjectStages.ProjectPath) .Lookup("programType", nameof(Project.ProgramTypeCode), "portfolio.program-type")
    .Person("pmEmployeeId", nameof(Project.PmEmployeeId)) .Unit("unitId", nameof(Project.UnitId)) .Reference("requestId", nameof(Project.RequestId), ProgramRequest.EntityType)
    .Date(…) .Decimal(…) .Ratio(…) .Bool(…) .Codes(…) /* jsonb array of codes */ .Defaults(…) .After<ProjectSyncHook>());
```

Field kinds: `text` (max length from the EF model), `date`, `datetime`, `decimal`, `ratio` (numeric(9,4), 1.0 = 100%), `int`, `bool`, `code`
(fixed codes from code, e.g. stage), `lookup` (lookup type: value must be an active code of `settings.lookup_value`), `codes` (jsonb array of
lookup codes), `person` (employee id), `unit` (organisation unit id), `reference` (id of another sync target). The engine converts and checks
every value before writing (`invalidValue` with the target field otherwise).

**Generic entity writer** (`EntitySyncWriter`): resolves the module's DbContext from DI, finds the entity by id (or creates it through its private
parameterless constructor, the one EF uses), sets the declared properties through `context.Entry(e).Property(name).CurrentValue`, applies
defaults, saves in a transaction with the external reference (`IExternalReferenceStore.LinkAsync(…, transactionContext: context)`, ADR 0023).
No raw SQL, change tracking and audit as always (CLAUDE.md §2.5). The module's domain methods (workflow transitions, numbering, events) are
deliberately **not** run: a synced record arrives in the state Salesforce gives it; no approval instance, no integration event, no
notification is produced (G3 "no live approval instance is created for synced records"). Optional per-target **hooks** (`ISyncTargetHook`)
run inside the same transaction for derived data the module owns — e.g. Portfolio grants the Project Manager / Project Owner **record
roles** of a synced project so scope-based access works as for local records; Teams grants team-member record roles.

**Custom writers** (`ISyncTargetWriter`) for types whose invariants are not plain properties: **`org.unit`** (hierarchy path / depth / unit
versions — written through `UnitAdministration`'s create / update with the parent resolved first) and **`org.employee`** (people objects only
*match* employees, §7; creating missing employees is an opt-in setting `createUnmatchedEmployees`, default off, through the employee
administration).

Targets declared in Phase 23 (module → types): Organisation `org.unit`, `org.employee` · Portfolio `portfolio.request`, `portfolio.project`,
`portfolio.milestone`, `portfolio.project-kpi`, `portfolio.project-risk`, `portfolio.stakeholder`, `portfolio.planning-item`, `portfolio.challenge`,
`portfolio.change-request`, `portfolio.contract`, `portfolio.lesson`, `portfolio.meeting` · Teams `teams.team`, `teams.team_member`,
`teams.team_evaluation`, `teams.member_evaluation` · KPI `kpi.card`, `kpi.plan`, `kpi.result` · Risk `risk.risk`, `risk.control`, `risk.kri` ·
CAP `cap.source`, `cap.remark`, `cap.action` · Audit `auditmgmt.report`, `auditmgmt.finding`, `auditmgmt.recommendation` · Benchmarking
`bench.study` · Document Registry `docreg.document` · IP `ip.record` · Tasks `tasks.task`. (`GET …/sync/targets` lists them with fields for the editor.)

## 6. Modes, failover and return to standby (ADR 0207)

`SyncModeLifecycle` (explicit transition table, unit-tested for every allowed and forbidden pair):

| From \ action | `activate` | `deactivate` | `makePrimary` | `returnToStandby` | `closeOutage` |
|---|---|---|---|---|---|
| `disabled` | → `standby` (connection tested OK) | ✗ | ✗ | ✗ | ✗ |
| `standby` | ✗ | → `disabled` | → `primary` (opens outage) | ✗ | ✗ |
| `primary` | ✗ | ✗ | ✗ | → `reconciling` (report acknowledged) | ✗ |
| `reconciling` | ✗ | ✗ | → `primary` (same outage re-opened) | ✗ | → `standby` (no pending conflicts) |

- **Standby** (`standby`, `reconciling`): the sync runs; the **standby guard** is on (§7).
- **Make this system primary** (`POST …/make-primary { reason, mfaMethod, code }`, permission `integration.sync.failover`, **MFA re-prompt**
  through `IStepUpVerifier` — TOTP, or an e-mail code requested with `POST …/step-up/email-code`; a wrong code is refused and audited):
  stops scheduled runs (a running run is cancelled at the next object boundary), switches the guard off (records unlock, workflows / jobs /
  notifications resume), opens an **outage** (`sync_outage`, start time, reason, user) and flags every synced record whose Salesforce approval
  was **pending** at that moment (`ProcessInstance.Status = 'Pending'` from the source history) as **`resubmit_required`**: the record already
  sits at the corresponding stage (mapped from `Approval_Status__c` / `Status__c`); its header shows "Resubmit required" and the approval
  history from Salesforce stays visible, so the owner submits it again here and approvers act here. The flag clears when an approval is
  submitted for the record here (`IApprovalEngine.SubmitAsync` tells the guard) or when the outage closes.
- **Outage changes report** (§9) while primary or reconciling.
- **Return to standby** (`POST …/return-to-standby { acknowledged: true }`): requires the report to have been exported or acknowledged
  (`report_acknowledged_at`); computes from the audit trail every synced record changed here during the outage → `changed_locally`;
  switches to `reconciling` (records read-only again, sync resumes). For a `changed_locally` record whose resolution is `pending` or
  `keep_local`, a newer Salesforce version is **not applied**: it is stored as `pending_payload` and marked `conflict`. The admin chooses
  per record **Keep local** (Salesforce version discarded until the outage closes) or **Take Salesforce** (the pending version is applied at once
  through the normal pipeline; later versions apply normally). **Close outage** (`POST …/close-outage`, no `pending` conflict left) → `standby`;
  from then on Salesforce wins again for every record.

## 7. Standby guard (ADR 0206)

`IStandbyGuard` (Kernel) is implemented by Integration (cached mode, 5 s; external-reference look-ups on PostgreSQL through the data source,
not through a DbContext, so it can run inside another context's save):

- **Writes** — `StandbyWriteInterceptor` (every module DbContext): when the guard is active and the channel is not `sync`, any update or
  delete of an audited entity that has an origin reference from a connection in standby, and any insert / update / delete of an audited child
  row whose **audit parent** (`[AuditParent]`) is such a record, throws `RecordReadOnlyException` → **409** Problem Details
  `code: integration.sync.readOnly`, `messageKey: integration.sync.errors.readOnly` (params: source name). This one rule covers the UI, REST
  API, MCP tools, imports, AI-accepted drafts and jobs alike (server-side, CLAUDE.md §2.4). Local-only data (settings, preferences, users'
  saved views, records created here that have no Salesforce reference) stays editable.
- **Approvals** — `IApprovalEngine.SubmitAsync` / `DecideAsync` refuse a read-only subject with the same error before anything is written.
- **Jobs** — a Quartz trigger listener vetoes jobs of the **suspended systems** (setting `suspendedSystems`, default: the 11 business systems
  `portfolio, kpi, risk, teams, requests, docreg, ip, bench, cap, auditmgmt, tasks`) while the guard is active; the job dashboard shows the run
  as "suspended (standby)". Platform jobs (outbox, audit chain, reporting refresh, integration) keep running, so dashboards stay current.
- **Notifications** — `INotificationService.SendAsync` drops (and logs) notifications whose template belongs to a suspended system while the
  guard is active: no duplicate e-mails while Salesforce is primary. Security e-mails and platform notifications are unaffected.
- **UI** — the record page's references query returns `standby: { readOnly, sourceName, deletedInSource, resubmitRequired }`; the header badge
  "Synced from Salesforce — read-only" (`OriginBadge`) and a banner explain why actions are refused; "Deleted in Salesforce" badge for
  `deleted_in_source`. Browse, search, reports and dashboards work on synced data at any time.

## 8. Salesforce connector (ADR 0201) and the ADoF pack

**Authentication**: OAuth 2.0 JWT bearer flow — JWT `{ iss: client_id, sub: username, aud: login_url, exp: now + 3 min }` signed RS256 with
the stored private key (in-memory only), `POST {login_url}/services/oauth2/token` (`grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`) →
`access_token`, `instance_url` (checked by the outbound URL policy). No password stored. Token cached per connection until a 401.
**Reading**: describe global / describe object (`/services/data/{v}/sobjects/{o}/describe`); REST `queryAll` (includes `IsDeleted = true`
rows) with `nextRecordsUrl` paging for deltas; **Bulk API 2.0** `queryAll` jobs (`POST /jobs/query`, poll, CSV results with `Sforce-Locator`
paging) for full / large loads (`SELECT COUNT()` above `bulk_threshold`). Incremental condition `{watermark_field} >= watermark − 5 min`
(commit lag), ordered by the watermark; records whose hash is unchanged are skipped, so the overlap creates nothing twice. **Limits**:
`/limits` read once per run (DailyApiRequests, DailyBulkV2QueryJobs) → status page; below 10 % remaining the run stops with
`integration.sync.errors.apiLimit`. **Retries**: 3 attempts with exponential back-off (2, 4, 8 s, on `IClock`-free delays in tests) on network
errors, 5xx, `REQUEST_LIMIT_EXCEEDED` is not retried; 401 renews the token once.
**Deleted records**: `IsDeleted = true` → `external_ref.sync_status = deleted_in_source` (record kept, badge "Deleted in Salesforce"); a record
deleted and restored in Salesforce returns to `synced`.

**ADoF Salesforce pack** (`adof-salesforce`, version 1) — load order and model differences (each object's `notes_ar` / `notes_en`):

| # | Object | Target | Notes (model differences) |
|---|---|---|---|
| 10 | `Department__c` | `org.unit` | `Record_Type__c` Department / Section → level codes; `Parent_Department__c` → parent (multi-pass); `Short_Code__c` → code (fallback `Name`) |
| 20 | `User` | people | match by `Email`, `EmployeeNumber`, `FederationIdentifier` (order configurable) |
| 21 | `Contact` | people | match by `Email`, then `Employee_Number__c` — PMS people lookups (`Project_Manager__c`, `Risk_Owner__c`, …) point at Contacts |
| 30 | `Project_Team__c` | `teams.team` | `Team_Type__c` → team type codes; status values → codes |
| 31 | `Project_Team__c` (2nd mapping) | `teams.team_evaluation` | scores stored on the team row → **one evaluation record per year** (`{Id}-{year:End_Date__c}`); source score kept |
| 32 | `Project_Team_Association__c` | `teams.team_member` | `Member_Role__c` → member role codes; `Participation_Level__c` |
| 33 | `Project_Team_Association__c` (2nd) | `teams.member_evaluation` | member evaluation fields on the association row → one evaluation per year; `Evaluation_Point__c` kept as source score |
| 40 | `PMS_Project_Request__c` | `portfolio.request` | `Status__c` / `Approval_Status__c` → request status (approval steps become source approval history) |
| 41 | `PMS_Project__c` | `portfolio.project` | `Approval_Status__c` → **stage + sub-state snapshot**; no live approval instance; `Program_Classification__c` → classification |
| 42 | `PMS_Work_Plan__c` | `portfolio.milestone` | `Header_Phase__c` → parent phase (multi-pass); weight / achieve → ratios |
| 43–51 | `PMS_Project_Kpi__c`, `PMS_Project_Risk__c`, `PMS_Project_Stakeholder__c`, `PMS_Project_Planning_Item__c`, `PMS_Challenges_Constraints__c`, `PMS_Change_Request__c`, `PMS_Contract__c`, `PMS_Lesson_Learned__c`, `PMS_Team_Meeting__c` | project children | project risk: Salesforce **additive** `Inherent_Risk_Score__c` kept as `sourceScore`, the rating is re-derived by the framework (probability × impact) |
| 60–62 | `Org_Kpi_Goal__c`, `Org_Kpi_Measure__c`, `Org_Kpi_Process__c` | `kpi.card`, `kpi.plan`, `kpi.result` | text targets with "-" → **Range** target type (`Range_Min_Value__c` / `Range_Max_Value__c`); performance re-calculated by `PerformanceCalculator`, source value kept |
| 70–72 | Risk objects incl. `Risk_Kpi_Relation__c` | `risk.risk`, `risk.control`, `risk.kri` | additive scores re-rated; "actions that became controls" → controls |
| 80–95 | CAP, Audit, Benchmarking, Document Registry, IP, Tasks / timesheets | module targets | object and field names from the 1.0 source documents' ERDs — **provisional, disabled** until the live describe confirms them (G3) |
| 100 | `ContentDocumentLink` + `ContentVersion`, `Attachment` | files | §10 |
| 110 | `ProcessInstance` / `ProcessInstanceStep` | approval history | §10 |
| 120 | `PMS_Project__History`, `PMS_Project_Request__History`, `Department__History`, … | field history | §10 |

The objects visible to the connected org user today (G3 list) are confirmed against the live describe; the others ship **disabled with
`confirmed_at` empty**: the integration user must first be granted read access to them, then the editor's *Validate* confirms (or reports)
the field names and the admin enables them.

## 9. Outage changes report (§G6)

`GET …/outages/{id}/report` reads the audit trail (`IAuditQuery.SearchAsync` per page, from `started_at` to `returned_at` / now, channel ≠
`sync` / `job`-from-sync) for every business system, and groups entries by **system → record**: record type label, number / title (resolver,
the caller's visibility — the report needs `integration.sync.failover`, which is an administrator permission), **Salesforce ID**
(origin reference, empty for records created here), action (create / update / delete / approval decision / workflow transition), field,
**old → new** value, user, time, and the `re_entered` mark. Approval decisions come from the workflow audit events of the record. Export
**Excel** (ClosedXML, one sheet per system, RTL sheet for Arabic) and **PDF** (Gotenberg through the Reporting PDF renderer, bilingual
header); exporting sets `report_acknowledged_at`. `POST …/outages/{id}/records/{recordKey}/re-entered` marks / unmarks a record group (audited).

## 10. Files and source history (ADR 0208)

- **Files**: for every synced record, `ContentDocumentLink` rows (`LinkedEntityId` = the record's Salesforce Id; the link object is queried
  per batch of synced Ids, as Salesforce requires a filter on `LinkedEntityId` or `ContentDocumentId`) → latest `ContentVersion`
  (`VersionData` downloaded) → `IFileService.UploadAsync` on the owner type's attachment purpose (`IFileOwnerAccess.AttachmentPurpose`), owner
  = the internal record. Reference `files.file` / `ContentDocumentId`; a new `ContentVersion` adds a version (`ReplacesFileId`). Classic
  `Attachment` (`ParentId`, `Body`) the same way (`files.file` / Attachment Id). A file the purpose refuses (type, size, virus) → exception
  `fileRejected`. Files are scanned and allow-listed exactly like uploads.
- **Approval history**: `ProcessInstanceStep` (`ProcessInstance.TargetObjectId`, `ProcessInstance.ProcessDefinition.Name`,
  `ProcessInstance.Status`, `StepStatus`, `Actor.Name`, `Comments`, `CreatedDate`) → `source_history` kind `approval`; pending
  `ProcessInstance` rows drive "Resubmit required" at failover.
- **Field history**: `<Object>__History` (`ParentId`, `Field`, `OldValue`, `NewValue`, `CreatedDate`, `CreatedBy.Name`) → kind `field`.
- Shown read-only in a **"Source history"** panel of the record's *External references* section (`GET /api/v1/integration/references/{type}/{id}/source-history`,
  same 404 rule as the references), approval timeline and field changes separately; never editable, never re-entered into workflow.

## 11. Pipeline of one run

1. Lock the connection (advisory lock), create `sync_run`, read limits, renew token.
2. For each enabled object mapping by `load_order`: choose Bulk / REST, query changed rows (`queryAll`), and for every row:
   deleted → mark reference `deleted_in_source`; else map fields (exceptions collected) → find reference → outage conflict rules (§6) →
   hash unchanged → `unchanged`; else write (target writer, same transaction as the reference) → created / updated. Errors per record
   never stop the object; a connector error (auth, limits, HTTP after retries) stops the run (`failed`), keeping the watermarks of objects
   already finished.
3. Same-object deferred rows (multi-pass), then open `missingParent` exceptions of this connection are re-tried once.
4. Watermark = max watermark value of the rows read (only advanced when the object completed). Run status, counts, `next_run_at`.
5. Failure → notification `integration.sync.failed` to holders of `integration.sync.manage` (bilingual template); the monitor job
   (`integration.sync-monitor`, every 15 min) sends `integration.sync.late` once per late period when `now > last success + late_after_minutes`.

**Re-process** (`POST …/exceptions/{id}/reprocess`, or bulk by object / reason): re-runs step 2 for the stored payload with the current mapping
(no Salesforce call); a payload older than the reference's `source_modified_at` is resolved as superseded. **Fix helpers**: *Map value*
(adds the value → code to the field's value map, clears `confirmed_at` only when the value is not in the describe) and *Map person*
(manual employee for a User / Contact) then re-process.

## 12. Permissions (system `integration`)

| Permission | AR / EN | Default roles |
|---|---|---|
| `integration.sync.view` | عرض حالة المزامنة / View sync status | `integration.administrator`, `integration.sync-operator` |
| `integration.sync.manage` | إدارة المزامنة والربط / Manage the sync, connection and mappings | `integration.administrator`, `integration.sync-operator` |
| `integration.sync.failover` | التحويل إلى نظام أساسي والعودة / Failover and return to standby | `integration.administrator` |

`integration.sync-operator` (new, MFA required). All endpoints check the permission on the server; secrets never leave the server; every
action audited (`integration.sync.*` events: connection changed, key stored, test, sync now, mapping validated / enabled, pack imported /
exported, exception re-processed / ignored / mapped, make primary (with reason), return to standby, conflict choice, re-entered mark, close).

## 13. Screens (Settings → Integrations → Salesforce sync; AR + EN, RTL)

**Status** (mode chip, last run, duration, next run, per-object counts, API usage bar, open exceptions, outage banner, *Sync now*, *Full load*) ·
**Connection** (login URL, consumer key, username, API version, key upload (PEM), *Test connection*, bulk threshold) · **Schedule** (kind,
hours, time, days, business hours, late threshold, next runs preview) · **Mappings** (objects with order / enabled / confirmed; object
editor: live describe field list with types and picklist values, field mappings, value maps, defaults, ignore list, notes, *Validate*; pack
export / import) · **Exceptions** (filters, payload view, *Map value*, *Map person*, *Re-process*, *Ignore*) · **Runs** (history, run log) ·
**Failover** (*Make this system primary* dialog with reason + MFA code; outage panel: *Outage changes report* (grouped table, export
Excel / PDF, re-entered checkboxes), *Return to standby* (acknowledgement), conflicts list with *Keep local* / *Take Salesforce*, *Close outage*).
Record pages: badge "Synced from Salesforce — read-only", "Deleted in Salesforce", "Resubmit required"; Source history panel.

## 14. Tests (no live Salesforce in CI)

`FakeSalesforce` (tests, an `HttpMessageHandler` stub with recorded describe JSON and in-memory records per object; parses the SOQL the
connector sends — `SELECT`, `FROM`, `WHERE SystemModstamp >= …` / `LinkedEntityId IN (…)`, `ORDER BY`, `COUNT()` — and serves REST pages,
Bulk 2.0 jobs (CSV) and the JWT token endpoint, asserting the assertion's signature and that **no write verb** reaches a data endpoint).
Unit: schedule (fake clock), mode lifecycle table, field mapping / conversions / transforms, hash, JWT assertion, SOQL builder, CSV parsing.
Integration (PostgreSQL): initial full load creates records + references; re-run creates no duplicates; incremental picks only changed records;
deletions marked not removed; picklist + person mapping incl. unmapped value → exception → map → re-process; parent-child order and
multi-pass; files and approval history imported; standby read-only on REST API, MCP tool, import and the approval engine (409 / refusal),
jobs vetoed and notifications dropped; failover (MFA required, wrong code refused) unlocks and re-enables; outage report lists every change
with old → new and the Salesforce ID; return to standby honours keep-local and take-Salesforce; permissions (non-admin 403, vendor refused).
E2E `web/e2e/salesforce-sync.spec.ts` (ar + en) with screenshots (docs/screenshots/phase-23).
