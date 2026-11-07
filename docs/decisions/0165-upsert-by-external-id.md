# 0165 — Upsert by external ID in imports, REST and MCP

- **Status:** Proposed (Phase 19, stream X) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §B "Upsert by external ID everywhere": Excel / CSV imports with optional Source system + External ID columns
  (update if found, else create, validation report); `GET / PUT /api/v1/{module}/{type}/by-external/{source}/{externalId}`; an MCP read tool.

## Decision
- **Imports** (Organisation units and employees, holidays, audit findings): two optional columns `source_system` / `external_id` (AR / EN
  headers in templates and reports). Found reference → that record is updated through the importer's existing update path; otherwise the
  importer's usual match (unit code, employee no.) or a create, then the reference is linked in the same transaction (`is_origin` when the
  row created the record). Report entries: unknown / inactive source, one of the two columns missing, duplicate external ID in the file,
  external ID already linked to another record, record already referenced by another external ID of that source. Re-running a file creates
  nothing new. Holiday imports now report `updated`; audit finding imports update the finding and skip recommendation rows of a finding that
  already exists (counted as `skipped`) — recommendations have their own lifecycle (follow-ups, revisions) that an import must not overwrite.
- **REST**: one helper, `Strategy.Platform.Http.Integration.ByExternalEndpoints.MapByExternal<TDto>()` (+ `.WithUpsert<TBody>()`), wired per
  type in a few lines. GET returns the module's GET-by-id DTO (404 when there is no reference or the module refuses the record). PUT takes a
  per-process advisory lock on (source, type, external ID), finds the reference, then **updates** through the module's update command with
  the record's current version (200) or — after checking the type's create permission (403) — **creates** through the module's create
  command and links the reference with `is_origin` (201). Validation, scope (404), audit and notifications are the module's own; repeating
  the same PUT is idempotent (an update without changes). If linking fails after a create (not possible for a new record id except a database
  failure) the record stays without reference and the error is returned; the lock makes the concurrent-create case impossible.
- **Types with GET and PUT**: `sample.item`, `risk.risk`, `kpi.card`, `cap.source`, `teams.team`, `auditmgmt.report`, `auditmgmt.finding`
  (body carries the report id used on create), `org.unit`, `org.employee`.
- **Types with GET only** and why:
  - `portfolio.request` — created through the request wizard and its approval workflow; the draft has a separate submit step.
  - `portfolio.project` — created only from an approved request (charter stage).
  - `portfolio.milestone`, `portfolio.story` — child records without a GET-by-id endpoint (read through the project); not wired.
  - `kpi.result` — created by the period-window job; values are entered through submit / approve.
  - `teams.meeting`, `teams.recommendation` — created inside a team (parent-scoped commands with their own workflow).
  - `cap.remark`, `cap.action` — created from a source / remark through the CAP workflow (and integration events).
  - `bench.study` — created through the study wizard with plan approval.
  - `requests.request` — Request Framework drafts per configurable type (dynamic form schema) and submission workflow.
  - `docreg.document`, `ip.record` — created through registration wizards with file upload and review workflow.
  - `tasks.task` — create may create several tasks (multi-assignee / assignment group); update only on one.
  - `calendar.holiday` — no GET-by-id endpoint (holidays are listed per year); upsert through the holiday import.
- **MCP**: read tool `find_by_external_reference` (source, type, external ID) → record summary + link through the resolver (same visibility
  as the record page); `not_found` otherwise. Registered like the other read tools (on by default), audited by the tool gateway.

## Consequences
New referenceable types add a `MapByExternal` line (and `.WithUpsert` when they have plain create + update commands). Phase 23's sync uses
the same store but its own mapping engine; Phase 22 clients can use the REST PUT with their client-credential principal.
