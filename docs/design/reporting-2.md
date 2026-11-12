# Reporting 2.0 — report types, report builder, record dossiers (Phase 21)

Requirements: SPEC-ADDENDUM §E (E1 report types, E2 report builder, E3 record dossiers), SPEC §0.4. Builds on Phase 17
([`reporting.md`](reporting.md), ADRs 0140–0149): the same `Strategy.Platform.Reporting` component, schema `reporting`, the module
datasets of ADR 0144 and the delivery pipeline of ADR 0145. ADRs of this phase: **0180–0187**.

## 1. Boundaries (unchanged rules, new pieces)

- Every module still answers for its own records only, **on its own DbContext and its own scoped query** (`IReportingDatasetSource.RunAsync`).
  The reporting layer never writes SQL against a module schema and never joins across schemas: it asks each module for rows and
  **joins in memory** on declared keys.
- New, **additive** pieces of `Strategy.Platform.Reporting.Contracts` (the *reportable-metadata contract*, ADR 0180):

| Piece | Purpose |
|---|---|
| `DatasetFieldType.Reference` | a record id used as a relationship key; never shown to people as a column; filter operators `eq`, `in`, `empty`, `notEmpty` only |
| `DatasetFieldType.RichText` | sanitised rich text (Phase 19); every output uses the plain-text projection |
| `DatasetDefinition.KeyField` | the `Reference` field holding a row's own id (what children join on) |
| `DatasetDefinition.JoinOnly` | a dataset with no scoped list of its own (approval history): only reachable through a relationship, restricted by the engine to the keys of parent rows the runner sees; never a primary object |
| `DatasetRelationship(Code, ParentDataset, ParentField, ChildDataset, ChildField, LabelAr, LabelEn)` | parent row ↔ child rows where `child.ChildField = parent.ParentField` |
| `IReportingDatasetSource.Relationships` (default interface member, empty) | relationships a module declares — the module that **owns the reference** declares it, also across systems (by dataset code only, no project reference) |
| `IReportableMetadata` (implemented by `MetricRegistry`) | read-only view: datasets, fields, relationships, `RelationshipsFrom`, `RunAsync(query, user)` |

**Phase 22 (OData `$expand`)** uses `IReportableMetadata` only: entity sets = non-join-only datasets, navigation properties =
`RelationshipsFrom(dataset)`, keys = `KeyField`; every expanded row comes from `RunAsync` as the client principal with
`ChildField in (parent keys)`, exactly as the report engine does (§4). Fields with a `Permission` the caller lacks must be dropped.

## 2. Reportable record types, fields and relationships

Codes are stable (seeds, saved reports and OData use them). ★ = new dataset in this phase. Every parent dataset gained a `Reference` key
field `id` (and children their parent key) — added to the existing `DatasetQuery<T>` declarations; existing fields are unchanged.

| System | Datasets (key) | Relationships declared (parent → child, join keys) |
|---|---|---|
| Portfolio | `portfolio.projects` (id), `portfolio.milestones` (id), `portfolio.requests` (id), ★`portfolio.change-requests`, ★`portfolio.project-risks`, ★`portfolio.raid` (issues, assumptions, dependencies, decisions), ★`portfolio.stakeholders`, ★`portfolio.meetings`, ★`portfolio.lessons`, ★`portfolio.project-kpis` (id) | projects → milestones · change-requests · project-risks · raid · stakeholders · meetings · lessons · project-kpis (`id` → `projectId`); requests → projects (`id` → `requestId`); **cross-system:** projects → `teams.members` (`teamId` → `teamId`), projects → `tasks.tasks` and milestones → `tasks.tasks` (`id` → `linkedId`), project-kpis → `kpi.cards` (`kpiCardId` → `id`) and → `kpi.results` (`kpiCardId` → `cardId`), requests → `workflow.approvals` (`id` → `resourceId`) |
| KPI | `kpi.cards` (id), `kpi.results` (cardId) | cards → results |
| Risk | `risk.risks` (id), `risk.actions`, ★`risk.controls`, ★`risk.kris` | risks → actions · controls · kris (`id` → `riskId`); **cross-system:** kris → `kpi.results` (`kpiCardId` → `cardId`) |
| Teams | `teams.teams` (id), `teams.members`, ★`teams.evaluations` | teams → members · evaluations (`id` → `teamId`) |
| Tasks | `tasks.tasks` (`linkedId` = linked record) | (targets of Portfolio / CAP relationships) |
| Requests | `requests.requests` (id) | requests → `workflow.approvals` |
| Audit Management | ★`auditmgmt.reports` (id), `auditmgmt.findings` (id), `auditmgmt.recommendations` (id), ★`auditmgmt.follow-ups`, ★`auditmgmt.finding-risks` | reports → findings; findings → recommendations · finding-risks; recommendations → follow-ups; **cross-system:** finding-risks → `risk.risks` (`riskId` → `id`) |
| Benchmarking | `bench.studies` (id), ★`bench.topics`, ★`bench.plan-items` | studies → topics · plan-items; **cross-system:** studies → `cap.remarks` (`id` → `linkedId`) |
| CAP | ★`cap.sources` (id), `cap.remarks` (id, `linkedId`), `cap.actions` | sources → remarks (`id` → `sourceId`); remarks → actions (`id` → `homeRemarkId`); remarks → `tasks.tasks` |
| Document Registry | `docreg.documents` (id), ★`docreg.versions` | documents → versions |
| IP Registry | `ip.records` (`documentId`) | **cross-system:** `docreg.documents` → ip.records (`id` → `documentId`) |
| Workflow (platform) | ★`workflow.approvals` (join-only: one row per decided step — attempt, step, outcome, actor, comment, time) | — |
| Sample | `sample.items` (id) | — |

All SPEC-ADDENDUM E1 cross-system pairs are covered: project ↔ team members, project KPI ↔ KPI card results, benchmark ↔ CAP actions
(study → remarks → actions), finding ↔ risk (finding → finding-risks → risks), risk ↔ KRI KPI (risk → KRIs → results), document ↔ IP.

## 3. Report types (ADR 0181)

- Table `reporting.report_type`: `code`, `system_code`, `label_ar/en`, `description_ar/en`, `definition` (jsonb), `status`
  (`active` / `inactive`), `seeded`, `seed_hash`, `edited`, audit columns, `xmin`.
- Definition: nodes; the first is the **primary** object; each other node = `alias`, `dataset`, `relationship`, `parent` alias,
  `join` (`inner` = "with", `outer` = "with or without"), `fields` (offered fields, null = all). Tree up to **3 related levels**
  below the primary (e.g. Projects → Milestones → Tasks) and **6 objects**; siblings allowed (Projects with Risks and KPIs).
- Every dataset is also an implicit single-object type `dataset:<code>` (so one builder serves one or many objects).
- **Seeded** per system (re-seeding updates an unedited seeded type, never an edited one): Projects with Milestones (and Tasks);
  Projects with Risks and KPIs; Projects with Change Requests; Projects with Team Members; Program Requests with Approvals; Requests
  with Approvals; KPI Cards with Results by period; Risks with Controls and Actions; Risks with KRI results; Findings with
  Recommendations and Follow-ups; Findings with Risks; Remarks with Actions; Studies with Topics and Actions; Studies with CAP remarks and
  actions; Teams with Members and Evaluations; Documents with Versions; Documents with IP records.
- Settings → Reporting → **Report types** (permission `reporting.report-type.manage`, Reporting administrator): list, create, edit
  (primary, add related object along a relationship offered by the metadata, join, offered fields), deactivate; validated against the
  metadata (`ReportTypeRules`).

## 4. Query composition, limits, permissions (ADR 0181, 0183)

Run of a report on a type, for the runner (UI, export, schedule — always the runner / recipient, never the author):
1. Resolve the type; the primary dataset must be readable (`ReadPermission`) → else **404**. Each related node: readable? A join-only node is
   readable when its parent is. An unreadable node offers no fields; as an inner join it empties the report (the runner sees no related row),
   as an outer join its columns stay empty.
2. Offered fields per node = type's `fields` ∩ dataset fields − `Reference` fields − fields whose `Permission` the runner lacks.
   Columns not offered are **dropped** (reported in `droppedColumns`); filters, grouping, sorting, summaries, chart and highlights on them
   are **refused** (400 `reporting.errors.fieldNotAllowed`).
3. Primary query: `RunAsync(dataset, columns + keys, primary filters)` on the module's scoped query, up to `ReportPrimaryRowLimit`
   (default 10 000; more → `truncated`).
4. Each related node, parents first: keys = distinct parent values; queried in chunks of 500 with the node's own filters + `childField in
   (keys)` — again the module's **scoped** query, so a related row the runner may not see is never returned. Filters on a related object
   filter related rows ("with": a parent whose related rows are all filtered out disappears).
5. Join in memory (`ReportComposer`): one row per leaf related row; siblings one after the other (no cross product); output limit =
   `ExportRowLimit` (default 50 000).
6. Sort (≤ 3 fields), grouping (≤ 3 levels) with subtotals, summaries, chart, highlights (§5) on the composed rows; then page (≤ 200).
7. Whole run under a timeout (`ReportTimeoutSeconds`, default 30) → 400 `reporting.errors.timeout`. Limits in Settings → Reporting.

## 5. Report builder (ADR 0182)

- Definition (stored in `reporting.report.definition`, additive to the Phase 17 shape): `reportType`, qualified `columns` (`b.title`),
  `filters` (operators per type + **`relative`** for dates: today, yesterday, this / last / next week, month, quarter, year, last / next
  N days — resolved in the installation time zone at every run, so a saved "this quarter" moves), `groupBy` (≤ 3), `sort` (≤ 3),
  `summaries` (sum, avg, min, max per numeric / date column; count always), `chart` (bar, line, pie, donut, stacked bar; category = a
  groupable column, optional series, measure = count or an aggregate of a numeric column), `highlights` (column, condition, tone
  good / warning / bad / info), `showDetails`. Phase 17 definitions (`dataset`, unqualified columns) still run unchanged.
- Result: columns (with object labels), page of rows (cells formatted in the runner's language, links per object, tones, group keys),
  **groups in tree order with subtotals** over all rows, grand total, chart data (categories labelled, series, values).
- Exports **Excel** (grouped rows with subtotal lines, RTL sheet for Arabic), **PDF** (Gotenberg; chart as server SVG + grouped table;
  bilingual header) and **CSV** (UTF-8 with BOM, labels in the chosen language, codes never translated); audited.
- **Folders** (`reporting.report_folder`): private (owner only) or shared (users / roles / units via the Phase 17 share table).
  **My reports** = the user's private reports: **every user may save private reports and folders** (`reporting.report.run`, everyone).
  Sharing a report, creating / sharing a shared folder and putting a report in a shared folder (= publishing) need `reporting.report.share`
  (Report author). Visibility: owner, or a direct share, or a shared folder the user matches.
- Scheduling: the Phase 17 schedule (`targetKind = report`) runs the new engine for type-based definitions, as each recipient (ADR 0145).

## 6. Record dossiers (ADR 0184–0186)

- Table `reporting.dossier_template`: `code`, `label_ar/en`, `dataset` (the record type), `definition` jsonb, `status`, `seeded`,
  `seed_hash`, `edited`. Settings → Reporting → **Dossier templates** (`reporting.dossier-template.manage`): list, designer, preview.
- Definition: `cover` (title, subtitle field, show organisation logo / names, date), `header` / `footer` text (AR / EN), default language
  (`ar`, `en`, `bilingual`), ordered **sections**: `key`, `titleAr/En`, `kind`, binding, optional `condition` (field, operator, values on
  the record), `pageBreak`.

| Kind | Binding | Content |
|---|---|---|
| `fields` | record fields | label / value grid |
| `text` | AR / EN text | static paragraph (from the template) |
| `related` | relationship (+ nested relationship for KPI results), columns, filters, sort, limit | table |
| `chart` | relationship, `ReportChart` | bar / line / pie (server SVG in PDF, values table in Word) |
| `trend` | relationship, category field, measure fields | lines over periods (KPI trend) |
| `gantt` | relationship, label, planned start / end, actual start / end | bars per item on a month scale |
| `matrix` | relationship, likelihood and impact fields, size | risk matrix with counts per cell |
| `approvals` | resource type | approval history (`IApprovalEngine.ListAttemptsAsync`) |
| `attachments` | owner type | file list (`IFileService.ListAsync` after `IFileOwnerAccess.CanReadAsync`) |

- **Permissions per section**: the record is read through its dataset as the runner (not visible → 404); every related section through
  the engine (§4); fields the runner may not see are left out; a section bound to an object, field or file owner the runner may not read is
  **omitted** (with no placeholder); a section with a false condition is omitted.
- Rendering: one in-memory `DossierDocument` → **PDF** via Gotenberg (HTML template, Arabic fonts embedded, RTL, page header / footer,
  cover page, SVG charts / Gantt / matrix) and **Word** via **DocumentFormat.OpenXml** (MIT): RTL paragraphs and tables (`bidi`,
  `bidiVisual`), heading styles, header / footer, charts and Gantt as native shaded tables (ADR 0185). Bilingual = Arabic and English
  values side by side.
- Runs: one record (`POST /dossiers/{template}/render`), or a **filtered set** (filters on the record type, ≤ `DossierBulkLimit`, default
  100) → **merged PDF** (one HTML, page break per record, one Gotenberg call) or **ZIP** (PDF or Word per record). Audited
  (`reporting.dossier.render`).
- **Schedules** (ADR 0186): the Phase 17 schedule with `targetKind = dossier`, `targetId` = template, options (filters, format, language
  mode, recipient rule). Recipients: users / roles / units, or **`field:<employee field>`** — each record's person (e.g. each active
  project's owner gets the dossiers of their projects). Built **as each recipient** (records they cannot see are left out; nothing →
  no e-mail), one e-mail per recipient and period with the merged PDF / ZIP attached (≤ attachment limit, else a link), idempotent per
  (schedule, recipient, period).
- **Seeded templates**: Project dossier (overview, charter, stage and RAG health, budget vs actual, milestones + Gantt, KPIs with results,
  risks and issues, change requests, team and stakeholders, meeting decisions, lessons learned, closure and impact, approval history,
  attachments), KPI card dossier, Risk profile, Audit report pack, Team evaluation report, Benchmark study report, CAP source report.

## 7. Permissions (new, system `reporting`)

| Permission | Default roles |
|---|---|
| `reporting.report.run` | everyone — now also **save private reports and private folders** (My reports) |
| `reporting.report.share` | Report author — share reports, shared folders, publish into them |
| `reporting.report-type.manage` | Reporting administrator |
| `reporting.dossier.run` | everyone (the record and every section are permission-checked) |
| `reporting.dossier-template.manage` | Reporting administrator |

Changed: saving a report needed `reporting.report.build` in Phase 17; E2 makes My reports available to every user (ADR 0183).
`reporting.report.build` stays (dashboard-like report authoring rights, kept for compatibility).

## 8. API (`/api/v1/reporting`)

`GET report-types` (usable by the caller), `GET report-types/{code}/fields`, `GET|POST|PUT report-types[/{id}]` (manage),
`GET reportable` (metadata the admin editor needs), `POST reports/run` and `reports/export` (both shapes), `GET|POST|PUT|DELETE folders`,
`PUT folders/{id}/shares`, `PUT reports/{id}/folder`, `GET dossier-templates`, `GET|POST|PUT dossier-templates[/{id}]` (manage),
`POST dossiers/{template}/render` (one record), `POST dossiers/{template}/bulk`, schedules with `targetKind = dossier`.

## 9. Front end

- `/reports`: **My reports**, **Shared with me**, folders (create, share, move); `/reports/new|:id`: builder — report type → columns by
  object → filters (relative dates) → grouping (3) → summaries → chart → highlights → run (chart above the grouped table with subtotals) →
  export Excel / PDF / CSV → save (folder) → share → schedule.
- Settings → Reporting: **Report types** (list + editor), **Dossier templates** (list + designer with section palette, order, binding,
  condition, preview PDF / Word).
- `/reports/dossiers`: run a template for one record or a filtered set (merged PDF / ZIP), schedule; record pages (project, KPI card, risk,
  audit report, team, study, CAP source) get a **Dossier** action.

## 10. Tests

Unit: `ReportTypeRules`, `ReportComposer` (inner / outer, siblings, depth), `ReportAggregation` (worked example: subtotals, summaries,
chart, highlights), relative dates, Word document structure (RTL). Integration: multi-level report only returns rows / related rows the
user may see (Portfolio → milestones → tasks with two users); inner vs outer; hidden field neither offered nor returned nor filterable;
subtotals and chart on a worked example; My reports for a user without roles, share needs Report author, folders; report-type admin;
project dossier renders every seeded section in AR and EN (PDF via Gotenberg, Word parsed back) and omits the cost section for a user
without cost rights; bulk dossier for a filtered set (merged PDF page count, ZIP entries); scheduled dossier e-mail to each project owner.
E2E (ar + en) with the screenshots listed in the phase prompt.

## 11. As built (Phase 21)

- Contract (§1) as designed; `IReportableMetadata` is the registry (`MetricRegistry`), registered scoped. 36 datasets (16 new), 38 relationships.
  ★ datasets added: Portfolio 7 (change requests, project risks, RAID, stakeholders, meetings, lessons, project KPIs), Risk 2 (controls,
  KRIs), Teams 1 (evaluations), Audit 3 (reports, follow-ups, finding-risks), Benchmarking 2 (topics, plan items), CAP 1 (sources),
  Document Registry 1 (versions), Workflow 1 (join-only approval history). The template module declares `sample.item-tasks` (used by tests).
- Engine (`ReportTypes/`): `ReportTypeRules`, `ReportComposer`, `ReportAggregation` and `RelativeDates` are pure and unit-tested;
  `ReportTypeEngine` resolves, validates (`reporting.errors.fieldNotAllowed` / `fieldUnknown` / `operator` / `summary` / `chart` /
  `highlight` / `timeout`) and composes; `ReportTypeExport` writes Excel / PDF / CSV; `ChartSvg` draws server charts for PDFs.
- `POST /reports/query` runs type-based definitions; `/reports/run` keeps the Phase 17 response for dataset definitions; exports and
  schedules accept both (`ReportService.RenderDefinitionAsync`).
- Dossiers (`Dossiers/`): `DossierRules` (validation), `DossierBuilder` (data per section as the runner), `DossierHtml` (Gotenberg),
  `DossierWord` (OpenXml), `DossierTemplateService` (admin, seeding, record picker, one / bulk / file). Sections whose bindings are not
  readable, or whose condition is false, are omitted; section failures are logged and omitted, never fatal.
- Schedules: `targetKind = dossier`, `options` jsonb (`DossierScheduleOptions`), recipients by record field resolved as the owner, content
  built as each recipient.
- Front end: `features/reporting/ReportBuilderPage.tsx` (Reports home with folders + builder), `ReportResultView.tsx`, `ReportTypesPage.tsx`,
  `DossierTemplatesPage.tsx`, `DossiersPage.tsx` (incl. `DossierButton`, used on the project page); namespace `reportBuilder`; `pieOption`
  added to the chart builders. Phase 17 saved reports open in the new builder (converted to `dataset:<code>`).
- Deviation from §5: Phase 17 reports keep running on the Phase 17 dataset path until saved again from the new builder (no data migration).
