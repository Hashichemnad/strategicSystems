# Design note — Phase 19 stream X: External references (`Strategy.Platform.Integration`)

**Scope.** SPEC-ADDENDUM §B (wins over SPEC.md): source systems, the shared `integration.external_ref` table with both uniqueness rules,
the record-page "External references" section and origin badge, upsert by external ID in every Excel / CSV import, `GET / PUT …/by-external/{source}/{externalId}`,
an MCP read tool, exports / reports / audit trail, and the global-search hook. First consumer: Phase 23 (Salesforce standby sync); Phase 22
(integration clients) reuses the REST upsert. ADRs **0164** (references, uniqueness, origin) and **0165** (upsert in imports, REST and MCP).

## 1. Projects

| Project | Contents |
|---|---|
| `Strategy.Platform.Integration.Contracts` | What modules and platform components use: `IExternalReferenceStore`, `ExternalReference`, `ExternalReferenceInput`, `SourceSystemInfo`, `ExternalRecordMatch`, `ExternalReferenceErrors` (message keys), `SyncStatuses`, the **referenceable types** registry (`ReferenceableTypeDefinition` + `IReferenceableRecordSource` + `AddReferenceableType`) and `IExternalImportColumns` helpers (`ExternalImport.Columns`, `ExternalImport.Check`). Kernel only — no EF Core / ASP.NET (architecture test). |
| `Strategy.Platform.Integration` | `IntegrationComponent` (`/api/v1/integration`, schema **`integration`**), `IntegrationDbContext` + migration, `SourceSystem` and `ExternalRef` entities (`[Audited]`), `ExternalReferenceStore`, `ReferenceResolver`, source-system administration, catalogue (permissions), lookup `integration.source_type`, the MCP tool `find_by_external_reference`. |
| `Strategy.Platform.Http` (+ reference to the contracts) | `ByExternalEndpoints.MapByExternal(...)`: the reusable GET / PUT by external ID helper modules wire per type in a few lines. |

## 2. Data model (schema `integration`)

**`integration.source_system`** — `id`, `code` (unique, `^[a-z0-9][a-z0-9_-]{1,49}$`, stable, never changes), `name_ar`, `name_en` (one required),
`type_code` (lookup `integration.source_type`: `salesforce`, `sap`, `oracle`, `dynamics`, `custom` — editable picklist), `description`,
`active`, stamps, `xmin`. No hard delete: **deactivate** (references stay readable; new references and imports with an inactive source are refused).

**`integration.external_ref`** — `id`, `source_system` (code), `entity_type` (resource type), `external_id` (≤ 255), `record_id`, `is_origin`
(the record was created from this source — drives the origin badge), `first_seen_at`, `last_synced_at`, `source_modified_at`,
`source_hash` (≤ 128), `sync_status` (`synced` · `pending` · `error` · `deleted_in_source`, codes for Phase 23), stamps, `xmin`.
Unique **`ux_external_ref_external`** (source_system, entity_type, external_id) and **`ux_external_ref_record`** (source_system, entity_type, record_id);
index (entity_type, record_id); index (external_id) for search. No FK to other schemas (CLAUDE.md §5). `[Audited("integration.external_ref")]`.
Unlink is an explicit, audited delete of the mapping row (not of a business record), so the unique indexes stay plain (ADR 0164).

## 3. Referenceable record types

A reference may point at any **linkable type** of the Links registry (portfolio.project, risk.risk, kpi.card, …) or at a type registered
with `AddReferenceableType` for records that are not linkable: **`org.unit`**, **`org.employee`** (Organisation), **`calendar.holiday`** (Calendar).
Each registered type gives its read permission and an `IReferenceableRecordSource` (batched: returns only records the user may read).
`ReferenceResolver.ResolveAsync(type, ids, user)` uses the Links registry for linkable types and the source for the others — the single
visibility check behind the record-page endpoint, the MCP tool and search hits. Unknown type → validation error `integration.errors.unknownType`.

## 4. Store contract (`IExternalReferenceStore`)

| Method | Use |
|---|---|
| `FindAsync(source, type, externalId)` | upsert lookups (imports, REST, sync) |
| `ListForRecordAsync(type, id)` / `ListForRecordsAsync(type, ids)` | record page, exports, report columns |
| `LinkAsync(input, transactionContext)` → `Result<ExternalReference>` | create or refresh a reference (idempotent for the same triple + record; sync fields updated) |
| `UpdateSyncAsync(…)` | sync fields only (Phase 23) |
| `UnlinkAsync(source, type, externalId, transactionContext)` | remove the mapping (audited) |
| `FindByExternalIdAsync(text, limit)` | (type, id) pairs whose external ID equals / starts with the text — for the global-search contributor (stream S) |
| `GetSourceAsync(code)` / `ValidateSourceAsync(code)` | source checks for imports / REST |
| `LockAsync(source, type, externalId)` | session advisory lock serialising concurrent upserts of the same external ID (REST PUT) |

`transactionContext` follows ADR 0023 (`ILinkStore`): pass the module's DbContext with its open transaction and the reference commits with the
record. Inside a joined transaction the insert runs under a **savepoint**, so a unique violation does not abort the caller's transaction.

**Validation (message keys, `integration.errors.*`)**: `unknownSource`, `inactiveSource`, `unknownType`, `externalIdRequired`, `externalIdTooLong`,
`externalIdTaken` (the external ID is linked to another record), `recordAlreadyReferenced` (the record already has another external ID from that
source). A PostgreSQL unique violation on either index is mapped to the same error (`ux_external_ref_external` → `externalIdTaken`,
`ux_external_ref_record` → `recordAlreadyReferenced`), so two concurrent writers get the same answer as a sequential one.

## 5. Source systems (Settings → Integrations → Source systems)

Endpoints `/api/v1/integration/source-systems` (list, create, update, activate, deactivate). Permissions (system `integration`, platform kind):

| Permission | AR / EN | Default roles |
|---|---|---|
| `integration.source-system.read` | عرض الأنظمة المصدر / View source systems | `integration.administrator`, `integration.viewer` (assigned to everyone) |
| `integration.source-system.manage` | إدارة الأنظمة المصدر / Manage source systems | `integration.administrator` (MFA; added to the platform administrator roles, ADR 0016) |

Lookup `integration.source_type` seeded (Salesforce, SAP, Oracle, Microsoft Dynamics, Custom). Changes audited by the interceptor.

## 6. Record page

`GET /api/v1/integration/references/{type}/{id}` (internal, signed-in): 404 unless the resolver says the record is visible; returns the
references (source code, AR / EN name, type, external ID, origin flag, first seen, last synced, source modified, sync status) and `origin`
(the first reference with `is_origin`). Web: `features/integration/ExternalReferences.tsx` — `<ExternalReferencesSection type id />`
(a details card, hidden when the record has no references) and `<OriginBadge type id />` (header chip "from {source}" / "من {source}").
Both share one query. Added to the record pages: project, program request, KPI card, KPI result, risk, team, meeting, task, service request,
document, IP record, CAP source / remark / action, benchmarking study, audit report / finding, unit (Units page details), employee, sample item.
There is no shared record-page shell, so each page adds the two components (two lines each).

History: the audit query reads one entity per record (`IAuditQuery.HistoryAsync(entityType, recordId)`), it does not join related entities.
Reference changes are audited under `integration.external_ref` and appear in the global Audit Log; the record's History tab is unchanged.

## 7. Upsert by external ID

**Imports** — every importer gets two optional columns `source_system` ("Source system" / "النظام المصدر") and `external_id`
("External ID" / "المعرّف الخارجي"), in the template and the report. Rules per row (both or neither):

1. unknown source → `integration.errors.unknownSource`; inactive → `inactiveSource`; one of the two missing → `externalIdRequired`
   / `sourceRequired`; duplicate (source, external ID) within the file → `import.errors.duplicateExternalId`.
2. Reference found → **that** record is updated (Organisation: the referenced unit / employee even if the code in the file differs; when the
   file's code belongs to another record → `integration.errors.externalIdTaken`). Not found → the importer's usual match (code / employee no.)
   or create, then the reference is linked (`is_origin` = the record was created by this row).
3. The record already has a different external ID from that source → `integration.errors.recordAlreadyReferenced`.
4. References are written in the importer's transaction (all-or-nothing). Re-running the same file → only updates, no duplicates.

| Importer | Upsert match | Notes |
|---|---|---|
| Organisation units (`OrgImporter`) | reference → unit; else unit code | report counts created / updated |
| Organisation employees | reference → employee; else employee no. | |
| Holidays (`HolidayImport`) | reference → holiday (updated through the editor's validation, overlap check excludes itself); else created | report gains `Updated` |
| Audit findings (`AuditImporter`) | reference → finding (fields updated through `UpdateFinding` rules; recommendation rows of an existing finding are skipped and counted); else created in the report | external ID on the finding's first row; rows of the same `finding_ref` must not carry a different one |

**REST** — `ByExternalEndpoints.MapByExternal<TDto>(group, path, resourceType, readPermission, name, get)` adds
`GET {path}/by-external/{source}/{externalId}` (404 when no reference or the record is not visible: the module's own GET handler decides);
`.WithUpsert<TBody>(createPermission, create, update)` adds `PUT` (lock → find → update through the module's update command with the current
version, or check the create permission (403), create through the module's command, link with `is_origin`; 201 on create, 200 on update).
Wired types and those without PUT: ADR 0165.

**MCP** — `find_by_external_reference` (read, on by default, system `integration`): arguments source, type, externalId → record summary
(type, id, number, title, link) through the resolver; `not_found` when absent or invisible. Audited by the tool gateway (channel `mcp`).

## 8. Exports and reports

Report builder (Reporting Contracts): a **generic column provider** in the engine — every dataset whose record type is referenceable
(`DatasetDefinition.ResourceType`, or derived by matching the dataset link to a linkable type's link template) offers two extra columns
`externalSource` ("Source system") and `externalId` ("External ID"), filled after the module ran the (scoped) query from the row ids
(`ListForRecordsAsync`). Not filterable / sortable / groupable; empty in grouped results. This covers every dataset of every module
(projects, requests, KPI cards / results, risks, teams, tasks, CAP, audit, benchmarking, documents, IP, service requests). Module list exports
(KPI / Teams / Audit Excel exports) are unchanged: the report builder is the export path for external IDs.

## 9. Global search (stream S)

`FindByExternalIdAsync(text, limit)` returns `(EntityType, RecordId, SourceSystem, ExternalId)`; the lead adds the `ISearchContributor`
after merging (hits resolved through the Links registry like every other hit; non-linkable types are dropped there).

## 10. Tests

Unit: source-code rule, import column checks. Integration (`tests/Strategy.IntegrationTests/Integration/`): uniqueness both ways (validation and
concurrent insert → same error); savepoint keeps the caller's transaction; source-system admin permission; audit entries; record-page 404;
import re-run (units, employees, holidays, findings) → updated, no duplicates, report issues; REST GET / PUT by external (create → update → no
duplicate, out-of-scope → 404, no create permission → 403, vendor refused); MCP tool only visible records. Vitest for the web components.
E2E `web/e2e/external-refs.spec.ts` (ar + en).
