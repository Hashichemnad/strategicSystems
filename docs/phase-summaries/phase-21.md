# Phase 21 — Reporting 2.0: report types, report builder, record dossiers: summary

Status: **built — awaiting owner acceptance** · version stays **1.0.0** (no tag until the end of Phase 23, SPEC-ADDENDUM A7).
Requirements: [`docs/SPEC-ADDENDUM.md`](../SPEC-ADDENDUM.md) §E (E1–E3), SPEC §0.4 · design note:
[`docs/design/reporting-2.md`](../design/reporting-2.md) (§11 "As built") · ADRs **0180–0186** (proposed in this phase, awaiting the owner).

## What was built

| Item | Result | Design / ADR |
|---|---|---|
| **Reportable metadata contract** (for Phase 22 too) | Additive to `Strategy.Platform.Reporting.Contracts`: `DatasetFieldType.Reference` (record id = join key, never a column) and `RichText` (plain-text projection), `DatasetDefinition.KeyField` / `JoinOnly`, `DatasetRelationship`, `IReportingDatasetSource.Relationships` (default empty), **`IReportableMetadata`** (datasets, fields, relationships, `RelationshipsFrom`, `RunAsync` as a user). The module that owns a reference declares the relationship — across systems by dataset code only. Phase 22 mapping (entity sets / navigation / keys) documented in the design note §1 | `reporting-2.md` §1–2, 0180 |
| **Record types and relationships in every module** | DATASETS_COUNT datasets (16 new: Portfolio change requests, project risks, RAID, stakeholders, meetings, lessons, project KPIs; Risk controls, KRIs; Teams evaluations; Audit reports, follow-ups, finding ↔ risk links; Benchmarking topics, improvement plan items; CAP sources; Document versions; join-only **approval history** from Workflow) and RELATIONSHIPS_COUNT relationships, including every cross-system pair of E1: project ↔ team members, project KPI ↔ KPI card results, benchmark ↔ CAP remarks → actions, finding ↔ risk, risk ↔ KRI results, document ↔ IP records (plus project / milestone ↔ tasks, requests ↔ approvals) | `reporting-2.md` §2 |
| **Report types** | `reporting.report_type` (migration `ReportingTwo`): primary + related objects as a tree (≤ 3 levels below the primary, ≤ 6 objects, siblings allowed), "with" (inner) / "with or without" (outer), offered fields; every dataset is also an implicit single-object type. **17 seeded** (portfolio 5, requests 1, KPI 1, risk 2, audit 2, CAP 1, benchmarking 2, teams 1, documents 2), re-seeded only while unedited. **Settings → Report types** editor (primary, add related object along a relationship offered by the metadata, join, offered fields with "needs a permission" marks) | §3, 0181 |
| **Joins in the reporting layer** | Primary rows from the module's scoped query (≤ 10 000, flagged truncated), each related object queried in chunks of 500 keys **through its own module's scoped query**, joined in memory (`ReportComposer`); siblings listed one after another (no cross product); output ≤ 50 000 rows; 30 s timeout; all limits in Settings → Reporting. No SQL across schemas | §4, 0181 |
| **Report builder** | Columns from every object; filters with **relative dates** (today … last / next N days, weeks, months, quarters / years by fiscal start); grouping **up to 3 levels with subtotals** and grand total; **summary formulas** sum / avg / min / max; **charts** bar, stacked bar, line, pie, donut (server-computed, ECharts on screen — `pieOption` added to the chart kit —, SVG in PDF); **conditional highlighting**; exports **Excel** (group lines, RTL sheet, chart data sheet), **PDF** (chart + grouped table, Gotenberg) and **CSV** (UTF-8 BOM, formula guard). Phase 17 reports open in the new builder and keep running unchanged | §5, 0182 |
| **My reports and folders** | Every user saves private reports and private folders (`reporting.report.run`); shared folders, sharing and publishing need `reporting.report.share` (Report author). Reports home: My reports / Shared with me / folders, folder sharing to users, roles, units | §5, 0183 |
| **Record dossiers** | `reporting.dossier_template`; **Settings → Dossier templates** designer: record type, language (ar / en / bilingual), cover, page header / footer, ordered sections — fields, text, related list (path of up to 3 relationships, filters, sort), chart, KPI trend, Gantt, risk matrix, approval history, attachments —, conditions on record fields, page breaks; preview as PDF or Word. **7 seeded templates** (project dossier with 18 sections, KPI card dossier, risk profile, audit report pack, team evaluation report, benchmark study report, CAP source report). Data for every section through the report engine as the reader; sections bound to something the reader may not read are **omitted** | §6, 0184 |
| **PDF and Word** | One `DossierDocument` → HTML for Gotenberg (Arabic fonts, RTL, header and footer lines per record, cover, SVG charts / Gantt / matrix) and **Word with DocumentFormat.OpenXml** (MIT, already in the solution): heading styles, header / footer parts, `bidi` / `bidiVisual` / `rtl` for Arabic, charts / Gantt / matrix as shaded native tables | 0185 |
| **One record, a filtered set, schedules** | Dossier action on the project page; **Reports → Dossiers**: one record (picker), a filtered set → **one merged PDF** or **ZIP** (PDF / Word per record, named by record number), **monthly schedule** with recipients by record field (e.g. each project manager) or users / roles / units, built as each recipient, idempotent per period | 0186 |

## Requested tests

| Test | Evidence |
|---|---|
| A multi-level report returns only rows and related rows the user may see | `IntegrationTests/Reporting/ReportTypesTests.A_multi_level_report_returns_only_rows_and_related_rows_the_runner_may_see_and_inner_and_outer_joins_differ` — sample items (template module) → tasks (Tasks module): the editor sees both linked tasks, the assignee only their own (same rows as the Tasks screens), the unit reader only unit A's items and no task; links per object; outsider 404. Unit: `ReportTypeEngineTests.Related_rows_the_module_did_not_return_never_join` |
| Inner vs outer join results | same integration test (inner = A0 twice; outer = every readable item, task columns empty; related-object filters remove parents under "with"); unit `Outer_join_keeps_a_parent_without_related_rows_and_inner_join_drops_it`, `Siblings_are_listed_one_after_the_other_not_multiplied…`, `Composition_stops_at_the_row_limit…` |
| A field the user may not see is not offered and not returned | `ReportTypesTests.A_field_the_runner_may_not_see_is_not_offered_not_returned_and_not_usable` (not in the type's fields; dropped column; refused in filters, sort, summaries, chart, highlights; relationship keys unknown) |
| Subtotals and charts correct on a worked example | `ReportTypesTests.Subtotals_summaries_chart_and_highlights_are_correct_on_a_worked_example_and_exports_carry_them` (unit A 4 rows / 700, avg 175, min 100, max 300; unit B 2 / 120; grand 820; chart values; tones; Excel 10 lines incl. group lines and total; CSV); unit `Worked_example_two_level_grouping…`, `Three_grouping_levels_nest_in_tree_order`, `Charts_count_or_aggregate…`, `Highlights_apply…`, relative dates (12 rows + fiscal year) |
| Dossier for a project renders every seeded section in Arabic and English (PDF and Word) and omits sections the user may not see | `IntegrationTests/Reporting/DossierTests.The_project_dossier_renders_every_seeded_section…` (real Gotenberg): all 18 sections for the full reader; Word headings = the seeded titles in English and in Arabic; English PDF contains every section title, Arabic PDF has Arabic text on ≥ 3 pages; the department viewer (no access to Performance Management in that installation) gets the dossier **without** the KPI section; out-of-scope record 404. Unit `DossierRenderingTests` (every block kind in HTML and Word, RTL / LTR structure, encoding) |
| Bulk dossier for a filtered set | `DossierTests.A_filtered_set_gives_a_merged_pdf_or_a_zip…` (merged PDF with both department projects and not the third; ZIP of two Word files named by project number; nothing visible → 422) |
| Scheduled dossier e-mail | `DossierTests.A_scheduled_dossier_is_e_mailed_to_each_project_manager…` (recipient field `a.pm`: one e-mail to the project manager with only their project's dossier attached; idempotent re-run; invalid recipient field refused) |
| Report types administration, My reports, folders | `ReportTypesTests.Report_types_are_administered_only_by_reporting_administrators…` (seeded types, cross-system relationships in the metadata, validation message keys, 403 for others, inactive types hidden); `Every_user_keeps_my_reports_and_private_folders…` |
| Seeded templates valid | `DossierTests.Every_seeded_template_is_valid_and_offered_to_readers_of_its_record_type` (all 7 seeded, record picker scoped) |

QUALITY_GATES

## Decisions (ADRs, proposed — awaiting the owner)
0180 reportable metadata contract · 0181 report types and joins in the reporting layer · 0182 builder: grouping, summaries, charts,
highlighting, relative dates, CSV · 0183 My reports for everyone, folders, publishing needs Report author · 0184 dossier templates and
per-section permissions · 0185 PDF via Gotenberg, Word via DocumentFormat.OpenXml · 0186 scheduled dossiers.

## Deviations
- **From the SPEC / addendum:** none known. E2 "My reports for every user" changes the Phase 17 rule that saving needed
  `reporting.report.build`; the Phase 17 test asserting 403 for an editor's private save now asserts the new rule (ADR 0183).
- **From the design note:** project RAG colours are computed by Portfolio's health service, not stored: the "stage and RAG health" section
  shows progress status, performance and planned progress (ADR 0184). Phase 17 saved reports stay on the dataset path until re-saved.
- **Shared files changed additively:** Reporting.Contracts (contract above), every module's `Reporting/` dataset source (keys, new datasets,
  relationships), Workflow (`ApprovalHistoryDataset`, reference to Platform.Reporting), Reporting settings (limits), Portfolio project page
  (Dossier button), router, settings navigation, chart kit (`pieOption`, PieChart registration), i18n index (`reportBuilder` namespace),
  authorization-matrix "everyone" list (`reporting.dossier.run`), release notes and changelog.

KNOWN_GAPS

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml --profile dev up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host -- --seed-demo --password-file <file>   # optional demo pack
dotnet run --project src/Host/Strategy.Host
cd web && npm ci && npm run dev        # http://localhost:5173
```
1. **Mariam** (reporting administrator): *Settings → Report types* — open "Projects with milestones and tasks", add a related object along a
   relationship, choose "with" / "with or without" and the offered fields.
2. *Reports → New report*: choose the type, columns of each object, a relative date filter ("this quarter"), group by stage and unit,
   summaries, a bar chart, a red highlight below 25 % — *Run*; export Excel / PDF / CSV; save into a folder; *Reports* shows My reports,
   Shared with me and folders.
3. A project → *Dossier* → PDF or Word, Arabic / English / bilingual. *Settings → Dossier templates*: open the project dossier, reorder or
   edit sections, preview. *Reports → Dossiers*: a filtered set as one PDF or a ZIP; *Schedule*: monthly, to each project manager.
4. **Ahmed**: the same report type shows only his units' records and related records; his dossier leaves out sections he may not read.

## Screenshots
`docs/screenshots/phase-21/` (`ar-*` / `en-*`): SCREENSHOTS_LIST
