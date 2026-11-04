# 0080 — Request Framework: built-in and custom types, form schema, stages and handlers

- **Status:** Accepted by the owner (Phase 14, before the pull request)
- **Context:** SPEC §5: Document Registry, IP and Benchmarking are "request → review → process → register" systems built on one framework;
  module-specific fields are defined in code, organisations add fields without code; stages and actions are configurable per type; new
  types can be added by configuration. CLAUDE.md §5: stages are code unless the SPEC says they are configurable — here it does.

## Decision
- One module `Strategy.Modules.Requests` (schema `requests`) owns **request types**, **requests**, the form engine and the stage / action
  engine. Document Registry and IP are separate modules that register **built-in types** through `Requests.Contracts`
  (`RequestTypeRegistration` + `IRequestTypeHandler`) and keep their own registers.
- **Built-in types**: code-defined fields (`system: true`) and stage codes cannot be changed in the builder; labels, extra fields, SLA,
  catalogue and submitter settings can. **Custom types** are created by administrators from a *simple template*: approval stage
  (process `requests.type.<code>`) → optional processing stage → completed; either stage may be switched off (not both).
- **Form schema** is a jsonb document (sections → fields; types text, textarea, richtext, number, currency, date, checkbox, picklist,
  multipicklist, employee, unit, file, record, table, secret; `visibleWhen` / `requiredWhen` on earlier fields). Pure validators check the
  schema when saved and the data on every save (types / lengths) and submit (everything). Hidden fields are not required and are dropped.
  Each request stores the schema version it was filled with; older requests keep validating against their own version's code-defined fields
  only for display.
- **Stages**: `approval` (Phase 4 engine, optional approve mini-form) · `processing` (processor roles, actions complete / custom / return /
  reject with mini-forms) · implicit `completed`. Request statuses: draft, in_review, returned, in_progress, completed, rejected, cancelled
  (table-tested lifecycle).
- **Handlers** run inside the framework's transaction (ADR 0023) on submitted / approved / rejected / returned / cancelled / action and may
  set the register record (`subject`), an outcome, or a different next stage. A failure rolls the action back.
- Processors of a stage: holders of the stage's role keys (default the system's office role) or `<system>.request.process` with scope All.

## Consequences
One catalogue, one My Requests, one queue and one request page for every type; Benchmarking (Phase 15) adds a built-in type the same way.
A richer stage designer (arbitrary graphs) is not offered: the simple template covers the SPEC's examples; built-in types define richer
stages in code.
