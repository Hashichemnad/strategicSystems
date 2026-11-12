# 0209 — Labels for code fields without a lookup type

- **Status:** Proposed (Phase 23) — awaiting owner acceptance
- **Context:** Phase 21 known gap "Lookup labels". Report-builder datasets (Phase 17, ADR 0144), report types and record dossiers (ADRs
  0180–0186) labelled a code only when its field named a Settings lookup type. Fixed lifecycle codes (project stage and status, milestone status,
  task priority …) and codes kept in module configuration (request types and stages, priority bands, team types and member roles, risk rating
  bands, approval processes, strategic objectives) reached people as raw codes in tables, group lines, chart axes / legends, exports and dossiers
  (HTML, PDF, Word). The Phase 22 record contract (ADR 0190) already had fixed choices (`RecordFieldChoice`) for its feeds.

## Decision
- `DatasetField` (Reporting contracts) gets two optional, additive properties next to `LookupType`:
  - `Choices` — fixed codes with AR / EN labels, reusing the Phase 22 `RecordFieldChoice` type;
  - `LabelSource` (`FieldLabelSource(SystemCode, Dimension)`) — codes kept in module tables or settings, answered by the
    `IReportingDimensionLabelSource` of that system: the same interface that labels Phase 17 dashboard breakdowns. New or extended sources:
    Portfolio `priority-band` (settings), Requests `type` / `stage` (request types; a stage is named by the first type, in code order, that
    defines it), Teams `type` / `role`, Risk `band` / `effectiveness` (current framework), Workflow `process` (registered process names), KPI
    `objective` (existing), and `FieldLabelSource.SystemNames` (system names of the access catalogue, served by Reporting).
- Resolution order: `LookupType`, else `LabelSource`; `Choices` back up a label source that does not know a code (e.g. project risk bands
  without the Risk module); then the other language; then the code itself. Blank labels count as missing.
- Labels apply to `Lookup` fields and to `Text` fields that declare them (request type, stage and channel, approval step, strategic objective,
  process). Their type stays `Text` so saved filters (`contains`) and report definitions keep working; free text is never relabelled.
- One place formats: `ReportValueFormatter` (report types, charts, dossiers and their exports) — the Phase 17 report builder now uses it for code
  fields too. Codes are asked once per run and source; group keys, sort and filters keep working on codes.
- Each module keeps its fixed lists once (`Reporting/<Module>Choices.cs`), worded as its screens (`web/src/i18n/{ar,en}/<module>.json`), and
  shares them between its datasets and its Phase 22 feeds. Fields backed by Settings picklists now name them (e.g. project risk category,
  stakeholder classification, lesson type, risk control type / nature / frequency, CAP priority and action type on Benchmarking plan items —
  `CapLookupTypes` in CAP contracts).
- An integration test fails when a `Lookup` field of any registered dataset or record type has no labels, when a lookup type is not registered,
  when a label source names a system without one, or when a choice lacks an Arabic or an English label.

## Consequences
- Reports, charts, exports and dossiers show labels in the reader's language; administrators' wording in Settings (lookups, request types, team
  types and roles, risk framework, priority bands) flows through unchanged.
- Fixed wording lives in code next to the codes (as the SPA keeps its own translations); a new code needs its label in the module's choices,
  and the test catches a new code field without labels, not a new value in an existing list (the code is then shown as it is).
- Values that combine a code with data (e.g. a KRI's last period "Q1 2026", annual evaluation cycles "2026") are shown as they are; the
  external-reference "Source system" column still shows source-system codes (Integration, out of scope here).
- Filter inputs of the report builders still take codes; offering the choices as pick lists is a possible follow-up (the DTOs are unchanged).
