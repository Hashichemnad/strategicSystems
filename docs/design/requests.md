# Design note — Phase 14: Request Framework, Document Registry, IP Registry

`Strategy.Modules.Requests` · `Strategy.Modules.DocumentRegistry` · `Strategy.Modules.IpRegistry` (each with its `.Contracts`)

**SPEC:** §5 (Request Framework), §6 (Document Registry), §7 (Intellectual Property Registry), §0.0 (Document Registry → IP link), §0.2,
§0.5, Appendix A (Q-RQ2) and the §5–8 decision (*single PMO step as default for all three, editable in the global Approval Settings centre;
documents use expiry-by-type or valid-until-superseded, no separate review cycle*).
**Decisions:** ADRs 0080–0088 (numbered from 0080 so the Portfolio phase built in parallel keeps its range).

This session runs without an interactive approval step (as Phases 5–12): this note is the plan, written and committed before the code; its
open choices are the ADRs.

---

## 1. Shape of the solution

SPEC §5: Document Registry, IP and Benchmarking are all *request → review → process → register* systems built on **one** framework; each
adds its own register, screens and rules; new request types are added by configuration.

| Project | Owns | Schema | System code |
|---|---|---|---|
| `Strategy.Modules.Requests` (+ `.Contracts`) | request types (built-in and custom), form schema + server-side validation, requests, stage / action engine, approvals through the Phase 4 engine, catalogue, My Requests, processor queues, PDF templates, embeddable submit page, request MCP tools, AI document-to-form | `requests` | `requests` |
| `Strategy.Modules.DocumentRegistry` (+ `.Contracts`) | documents, versions, publication, validity / expiry, withdrawal, library and sensitivity clearance, compare, acknowledgements, dashboard, AI | `docreg` | `docreg` |
| `Strategy.Modules.IpRegistry` (+ `.Contracts`) | IP records, contributors (encrypted Emirates ID), registration periods, MOET form, filing / registration / renewal, dashboard, AI | `ip` | `ip` |

Boundaries (architecture tests, CLAUDE.md §5): Document Registry and IP reference **`Requests.Contracts` only**; IP references
`DocumentRegistry.Contracts` only for the integration event type; nothing references another module's internals or DbContext; no foreign keys
across schemas.

```
            Requests.Contracts  ◄───────── implements IRequestTypeHandler ───────── DocumentRegistry / IpRegistry
   (RequestTypeRegistration, IRequestTypeHandler,                                 (their own registers, screens, jobs)
    RequestTransition, IRequestDirectory)
                    ▲
                    │ framework calls the handler inside its transaction (ADR 0023)
            Requests module ──► Workflow (IApprovalEngine, hooks) · Settings (numbering, lookups) · Files · Notifications · Audit · AI · MCP
```

## 2. Request Framework (`requests`)

### 2.1 Request types — `request_type` (ADR 0080)

Two kinds, one table:

* **Built-in types** are declared in code by their owning module with a `RequestTypeRegistration` (Contracts): code, system, labels,
  **code-defined fields** (`system: true`, cannot be removed or retyped in the builder), stages and actions, handler. A seeder inserts the row
  once (labels, catalogue settings, form schema); afterwards admins may relabel, add **extra fields**, change SLA / catalogue / submitter
  settings — never the code-defined fields or the stage codes. Phase 14 registers:
  `docreg.document` (new document or new version), `docreg.withdrawal` (withdraw / obsolete with approval), `ip.registration`.
* **Custom types** are created by an administrator in **Settings → Request types** (form builder) without code: owner system `requests`,
  stages from the *simple template* (§2.4), all fields admin-defined.

Columns: `code` (unique, `^[a-z][a-z0-9-]{1,40}$`; built-in codes are prefixed with their system), `system_code`, `kind`
(`builtin`/`custom`), `name_ar/en`, `description_ar/en`, `category` (lookup `requests.category`), `icon`, `number_prefix` (numbering token
`{TYPE}`), `owning_unit_id` (null → owning unit of the system, §0.2), `submitters` (jsonb: `everyone` | `roleKeys[]` | `unitIds[]`),
`form` (jsonb **form schema**, §2.2), `stages` (jsonb, §2.4), `pdf_templates` (jsonb, §2.8), `active`, `show_in_catalogue`, `embeddable`,
`schema_version` (incremented on every form change; each request stores the version it was filled with), `sort`, audit columns, `xmin`.

### 2.2 Form schema (jsonb) and server-side validation

```json
{ "sections": [ { "code": "main", "titleAr": "…", "titleEn": "…",
    "fields": [ { "code": "nameAr", "type": "text", "labelAr": "…", "labelEn": "…", "helpAr": "…", "helpEn": "…",
                  "required": true, "system": true, "maxLength": 300,
                  "visibleWhen": { "field": "isNewVersion", "op": "eq", "value": true } } ] } ] }
```

Field types (SPEC §5.1): `text`, `textarea`, `richtext` (sanitised on the server, Tiptap in the SPA), `number` (min / max / decimals),
`currency` (numeric(18,2) + currency code), `date` (min / max), `checkbox`, `picklist` / `multipicklist` (lookup type code, active codes only),
`employee`, `unit` (checked against the Organisation directory), `file` (ids of files uploaded to the request; purpose
`requests.attachment`), `record` (a record of another system through the **Links registry**: `targetType`, readable by the user),
`table` (repeating child rows with typed `columns`, min / max rows), `secret` (sensitive text, optional `format: emiratesId`; ADR 0086).
Visibility rules: `visibleWhen` with `eq`/`ne`/`in`/`filled`/`empty` on another field of the form; a hidden field is **not** required and
its value is dropped on save. `requiredWhen` uses the same shape.

`FormSchemaValidator` (pure, unit-tested) checks the schema itself when an admin saves it (unique codes, known types, valid lookups / record
types, conditions referencing earlier fields, table columns not nesting tables). `FormDataValidator` (pure + an async resolver for lookups,
employees, units, files and records) validates submitted values: type, required / requiredWhen, max length, ranges, decimals, picklist
codes, table rows, Emirates ID checksum-free format `784-YYYY-NNNNNNN-N`. Errors are field paths (`fields.contributors[2].share`) with message
keys. **Drafts** validate types and lengths only; **submit** validates everything (the same split as Phase 8). Unknown keys are refused.
Values are stored as a jsonb object keyed by field code; the API never trusts client-computed values.

### 2.3 Requests — `request`, `request_event`

`request`: `number` (numbering `requests.request`, default `{TYPE}-{YYYY}-{SEQ:4}`, `{TYPE}` = the type's prefix — ADR 0082),
`type_id`, `type_code`, `system_code`, `schema_version`, `title_ar/en` (from the type's title fields), `requester_user_id`,
`requester_employee_id`, `unit_id` (requester's unit, editable when the form has a `unit` field marked `requestUnit`), `status`, `stage`,
`data` (jsonb, form values; `secret` values encrypted), `process_data` (jsonb, values captured by action mini-forms, keyed by stage),
`subject_type` / `subject_id` (the register record a handler created, e.g. `docreg.version`), `outcome` (handler-defined code, e.g.
`published`, `registered`), `submitted_at`, `stage_entered_at`, `stage_due_at` (processing SLA in working time, ADR 0027), `completed_at`,
`cancel_reason`, `source` (`ui`, `embed`, `mcp`, `ai`, `system`), audit columns, `xmin`.

`request_event` (append-only timeline): action, from / to stage and status, actor, on-behalf-of, comment, mini-form data (secrets masked),
time.

Statuses (code, translatable labels): `draft` · `in_review` (approval stage) · `returned` · `in_progress` (processing stage) · `completed` ·
`rejected` · `cancelled`. Transition table `RequestLifecycle` (unit-tested: every allowed and forbidden pair):

| From \ action | save | submit | approval approved | approval rejected | approval returned | stage action | cancel |
|---|---|---|---|---|---|---|---|
| draft | draft | first stage | — | — | — | — | cancelled (own draft: hard delete never; status) |
| in_review | — | — | next stage | rejected | returned | — | cancelled (requester or admin; instance cancelled) |
| returned | returned | first stage (new attempt) | — | — | — | — | cancelled |
| in_progress | — | — | — | — | — | action's target stage / completed / rejected / returned | cancelled (admin only) |
| completed · rejected · cancelled | — | — | — | — | — | — | — |

### 2.4 Stages and actions (ADR 0080)

`stages` is an ordered list; each stage has `code`, names, `kind`:

* `approval` — runs an approval process of the Phase 4 engine (`processKey`). Optional **approve form** (mini-form fields required before
  the instance can complete — e.g. document classification). Approve / Reject / Return come from the engine (comments, delegation, SLA,
  history, escalation).
* `processing` — work by **processors** (`processorRoleKeys`, default the system's office role; plus the permission
  `<system>.request.process` with scope All). Actions: `{ code, labelAr, labelEn, kind: complete|custom|return|reject, toStage?, form[],
  requiresComment }`. `return` sends the request back to the requester (`returned`), `reject` ends it.
* the implicit final stage `completed`.

Simple template for custom types: `review` (approval process `requests.type.<code>`, **default one step: role
`requests.office` — PM Office / Strategy Office**, SLA 24 working hours) → optional `processing` (processors, *Complete* with an optional
completion form) → completed. The builder can switch the approval stage or the processing stage off (at least one stays), rename stages,
change the processing SLA and edit the completion form.

**Mini-forms** use the same schema and validator as the request form. For approval stages the framework's decision endpoint
(`POST /requests/{id}/decide`) validates the approve form, keeps it in a scoped `PendingDecision`, then calls
`IApprovalEngine.DecideAsync`; the framework's approval hook stores the values in `process_data` and **refuses** completion of the instance
when a required approve-form field is missing — so the generic My Work approval panel cannot bypass classification (the pattern of
ADR 0064).

### 2.5 Approvals (ADR 0081)

* Built-in types register their processes in code (`docreg.review`, `docreg.withdrawal`, `ip.review`), resource type `requests.request`,
  read permission `requests.request.read`, approver record role `requests.request/approver`, link `/requests/{id}`. Default chain: **one
  step, role `<system>.office` ("PM Office / Strategy Office")**, SLA 24 working hours, comment on reject / return (SPEC §5–8 decision).
* Custom types: process `requests.type.<code>` created at run time. The Workflow component gains an **additive** contract
  `IApprovalProcessSource` (dynamic definitions merged into the registry, cached, refreshed on change) and seeds version 1 of a dynamic
  process the first time it is used or shown in the Approval Settings centre. After that the centre owns the chain, as for every process.
* Variables offered to entry conditions: `requestType`, `unitLevel` (built-in), plus every `number` / `currency` / `checkbox` / `picklist`
  field of the form (`f_<code>`), so admins can write `f_amount > 50000`.
* One framework approval hook handles every request process key (it asks the type registry which keys are request processes) and moves the
  request: approved → next stage; rejected → `rejected`; returned → `returned`; each transition calls the type's handler (§3, §4) in the same
  transaction.

### 2.6 Handler contract for built-in types (Contracts)

```csharp
public interface IRequestTypeHandler
{
    string TypeCode { get; }
    Task<IReadOnlyList<FieldError>> ValidateAsync(RequestHandlerContext context, CancellationToken ct);          // module rules on submit
    Task<IReadOnlyCollection<string>> AvailableActionsAsync(RequestHandlerContext context, CancellationToken ct); // hide e.g. "register internally"
    Task<Result<RequestTransitionOutcome>> OnTransitionAsync(RequestTransition transition, object transactionContext, CancellationToken ct);
}
```

`RequestTransition`: kind (`submitted`, `approved`, `rejected`, `returned`, `cancelled`, `action`), request snapshot (ids, number,
requester, unit, **decrypted** form values — server side only), stage from / to, action code, mini-form values, actor. The outcome may set
`subject_type/id`, `outcome`, an override of the next stage, and a display title. A failure rolls everything back. Handlers join the
framework's transaction with `db.JoinTransactionAsync(transactionContext)` (ADR 0023).

### 2.7 Requester and processor experience

* **Catalogue** (`/requests/new`): active types the user may submit (submitters rule + system access), grouped by category, searchable, AR
  / EN descriptions; *Start* opens the wizard (one step per form section, save as draft, inline validation, review step).
* **My Requests** (`/requests/mine`): live status, current stage, next action / who (current-status engine), returned ones first; cancel own
  draft / pending request; resubmit after return.
* **Processor queues** (`/requests/queue`): per type and stage, filters (type, stage, status, unit, overdue), **SLA indicator** (due /
  overdue in working time), bulk action only where safe: *bulk complete* for processing stages whose completion form has no required field.
* **Request page** (`/requests/{id}`): header (number, title, status chips), **stage stepper**, current-status banner with the user's next
  actions, form values (secrets masked), attachments, approvals panel (engine), mini-form dialogs for actions, timeline, Documents, History
  (audit), Tasks.
* **Embeddable submit page** (`/embed/requests/{type}`): chrome-less page for the organisation's intranet (only types flagged `embeddable`),
  the same SSO login (redirect to the normal sign-in and back), `frame-ancestors` from Settings → Requests (`allowedEmbedOrigins`, default
  none = same origin only). **Deep link** `/requests/new/{type}` opens the wizard. Settings shows both links per type.

### 2.8 Documents out — PDF templates (ADR 0088)

Each type may define templates `{ code, nameAr, nameEn, htmlAr, htmlEn, who: requester|processor|admin, stages[] }`: bilingual HTML with merge
fields `{{request.number}}`, `{{request.title}}`, `{{field.<code>}}`, `{{field.<table>.rows}}` loops (`{{#rows contributors}}…{{/rows}}`),
`{{process.<stage>.<field>}}`, `{{org.name}}`, `{{today}}`. Rendering: an in-house mustache-like renderer that **HTML-encodes every value**
(no raw HTML except sanitised richtext), then the Phase 3 `IPdfRenderer` (Gotenberg, embedded Arabic fonts). Downloads are audited
(export). Built-in templates: Document Registry — none needed (the register has its own pages); IP — the **MOET registration form**
rendered by the IP module (needs decrypted Emirates IDs, §4.4). Custom types: the admin writes templates in the builder with a live preview.

### 2.9 Access (`requests`)

| Permission | Meaning |
|---|---|
| `requests.request.read` (scoped: Own = requester) | read requests (also by processors of the type and approvers through record roles) |
| `requests.request.submit` | use the catalogue (plus the type's submitters rule) |
| `requests.request.process` (All) | act as processor of custom types whose processor roles include one of the user's roles |
| `requests.request.manage` (All) | cancel any request, see all, reassign |
| `requests.type.manage` | Settings → Request types (form builder, stages, templates) |
| `requests.ai.extract` | AI document-to-form |

Roles: *Requester* (everyone: read Own, submit, AI extract), *Requests office* (`requests.office`, MFA: read All, process, manage — default
approver of custom types), *Request types administrator* (type manage, MFA). Record role `requests.request/approver` (engine) gives read.
Visibility of one request = read scope **or** processor of its type **or** record role. 404 outside, 403 for visible-but-forbidden.

### 2.10 Notifications, current status, My Work, MCP, AI

* Templates (AR / EN, editable): `requests.submitted` (to processors when entering processing), `requests.returned`, `requests.rejected`,
  `requests.completed` (to the requester), `requests.stageOverdue` (processors, once). Approval notifications come from the engine.
* Current status per request: "Waiting for {approvers} to approve" (engine), "Waiting for {processors} — {stage}", "Returned to you —
  correct and resubmit", "Completed — {outcome}".
* My Work: requests returned to me, requests at a processing stage I can act on; My Work card *Requests needing my action*.
* MCP (ADR 0031): `my_requests` (read, on), `request_status` (read, on), `submit_request_draft` (write, **off**: creates / updates a **draft**,
  never submits). No approve tool.
* AI document-to-form for **every** type (§0.5): upload a document → the model fills the type's fields (schema-validated JSON, untrusted
  document text delimited) → the wizard shows the values as an AI draft to accept. Prompt `requests-form-fill.v1.md`.
* Search source for requests; requests are **task-able** (Links registry).

## 3. Document Registry (`docreg`)

### 3.1 Configuration

* Lookups: `docreg.level` (Strategic, Legislative, Operational), `docreg.type` (Corporate Policy `POL`, Procedure `PRC`, Manual / Guide
  `MAN`, Legal Document `LEG`, Strategy `STR`, Form `FRM`), `docreg.sensitivity` (**Public · Internal · Confidential · Secret**, Q-RQ2).
* Settings area `docreg` (Settings → Document Registry, permission `docreg.settings.manage`):
  * **Document types** (per type code): numbering code (`{TYPE}` token), validity rule `superseded` | `months` with N, **creates IP register
    entry** flag, read acknowledgement required by default. Seed (SPEC §6.2.5, ISO 9001 / GEM): policies `months 24`, procedures `months 24`,
    manuals / guides `months 36`, legal / legislative and forms `superseded`, strategies `months 36`; `creates IP` off for all (flagged per
    installation, e.g. Manual / Guide).
  * **Sensitivity clearance** per level: `allEmployees`, `ownerUnit` (owner unit and below), `roleKeys[]`, `allowShareToAll`. Seed: Public
    and Internal → all employees; Confidential → owner unit + `docreg.office` + `docreg.leadership`; Secret → `docreg.office` +
    `docreg.leadership` (no share-to-all). Public documents may have an optional public link (setting `publicLinks`, default off).
  * Reminder days before expiry (default **60, 30**); watermark on downloads (on, bilingual text editable); organisation code `{ORG}`
    (default `ORG`).
* Numbering: `docreg.document` default `{ORG}-{UNIT}-{TYPE}-{YYYY}-{SEQ:2}` (e.g. `DOF-LG-LEG-2026-01`), ADR 0082.

### 3.2 Data model

* `document`: `document_no` (null until approved), `name_ar/en`, `description`, `owner_unit_id`, `owner_employee_id`, `level`, `doc_type`,
  `sensitivity`, `current_version_id`, `status`, `effective_date`, `expiry_date`, `share_to_all`, `ack_required`, `tags`, `ip_record_id`,
  `name_search` (normalised Arabic, trigram), audit, `xmin`.
* `document_version`: `document_id`, `major`, `minor`, `label` ("1.0"), `request_id` (framework request), `change_summary`
  (reason for change), `status` (`draft`, `submitted`, `under_review`, `returned`, `rejected`, `approved`, `current`, `superseded`,
  `withdrawn`), `requested_by`, `reviewed_by`, `approved_at`, `published_at`, `published_by`, `final_file_id` (purpose `docreg.final`, owner
  `docreg.version`), `text_extract` (for search / compare / Q&A; trimmed), `ai_summary_ar/en` (accepted drafts only).
* `acknowledgement`: `version_id`, `employee_id`, `acknowledged_at` (unique per version / employee).
* `validity_event`: `document_id`, kind (`reminder-60`, `reminder-30`, `confirmed`, `expired`, `extended`), old / new expiry, actor, at —
  idempotency of the job and the "logged" confirmation.

Document statuses (SPEC §6.2): `draft · submitted · under_review · returned · rejected · approved` (awaiting publish) `· published ·
expired · superseded · under_revision · withdrawn`. For a new version of a published document the **document** stays `published` (or
`expired`) and shows `under_revision`-flag via its in-progress version; the version carries the review statuses.

### 3.3 Flow on the framework

| Request type `docreg.document` | |
|---|---|
| Form (code-defined) | `isNewVersion` (checkbox) · `existingDocument` (record `docreg.document`, visibleWhen isNewVersion; must be published / expired, readable, owner unit or office, no version in progress) · `nameAr`, `nameEn` (prefilled from the document for new versions) · `ownerUnit` (unit, default requester's unit, `requestUnit`) · `description` · `draftFile` (file, required) · `changeReason` (required for new versions) |
| Stage `review` | approval `docreg.review` (one step `docreg.office`); **approve form**: `level`, `docType`, `sensitivity` (picklists), `reviewMonths` override (optional) |
| Stage `publish` | processing by `docreg.office`; action **Publish**: `finalFile` (file, PDF), `shareToAll`, `effectiveDate` (default today), `expiryDate` (default from the type rule, editable), `ackRequired`; action **Return** to requester |
| Handler | submit → create document (new) + version `submitted`; approved → classification, **Document ID** (new documents, numbering in the same transaction) and **version** (new: 1.0; new version: next major — `minor` is used by the office's *Republish correction* action later), version `approved`; publish → copy the final file to the version (purpose `docreg.final`), version `current`, previous current version → `superseded`, document `published`, effective / expiry dates, requester notified, **`DocumentPublished` integration event** through the outbox (§4.6) |

`docreg.withdrawal` (request type): `document` (record), `reason` → approval `docreg.withdrawal` → document `withdrawn` (archive; not in
the active library), version `withdrawn`. Owners request it from the document page or from the expiry reminder.

### 3.4 Validity and expiry (ADR 0084) — job `docreg.validity` (daily 02:15, idempotent)

* Expiry date = effective date + N months for `months` types, null for `superseded`. Office may edit it on publish or later (audited).
* Reminders to the document owner (and the owner unit's head when no owner) at **60 and 30 days** (settings) before expiry — once each
  (`validity_event`), in-app + e-mail, with three actions on the document page: **Confirm still valid** (expiry + the type's N months from
  the current expiry, logged), **Submit new version** (opens the wizard pre-filled; current version stays valid until the new one is
  published), **Request withdrawal** (withdrawal request).
* At expiry without action → `expired` (still visible in the library, marked "Expired"; dashboard and Strategy Office notified once).
  Publishing a new version supersedes regardless of expiry. Dates in the installation time zone; the fake clock drives the tests.

### 3.5 Sensitivity enforcement (ADR 0083) — one rule for every read path

`DocumentVisibility.ApplyAsync(query, user)` is the **only** way to read documents: library list, document page, version list, file
download (file owner access hook), compare, search source (global search + Ask), MCP tools, AI Q&A, acknowledgements, dashboard figures,
`IDocumentDirectory` (Contracts) and the Links badge. A published / expired document is visible when: `share_to_all` **or** the user is
cleared for its sensitivity (all employees, or holds one of its role keys, or — when `ownerUnit` — belongs to the owner unit or below) **or**
the user is the owner / requester of a version **or** holds `docreg.document.manage` (All). Drafts / in-review documents are visible to
their requester, the owner and the office only. Clearance is computed once per request (role holders through `IRoleHolderQuery`, the
user's unit path) and applied **in the SQL query**. Out of clearance → 404 (existence never leaks; counts on the dashboard are computed on
the filtered query).

### 3.6 Library, versions, compare, acknowledgement, watermark

* **Library**: browse level → type → unit (facets with counts), full-text search in title, description and the published file's extracted
  text (normalised Arabic, trigram / ILIKE on `*_search`), filters (status incl. expired, sensitivity, unit, expiring within N days), sorted
  by name / date.
* **Document page**: current version viewer (pdf.js), metadata, version history with change summaries, **compare** any two versions (text
  diff of the extracted text, in-house word-level LCS; AI "what changed" summary optional), approvals of the version's request, related
  documents (Links), IP link badge, acknowledgements (count, mine, *Acknowledge*), validity panel (expiry, reminders sent, actions).
* **Read acknowledgement**: when `ack_required`, every employee who can see the document gets it on My Work until acknowledged; the office
  sees the list per version.
* **Watermark** (ADR 0085): downloads of the final PDF through `GET /docreg/documents/{id}/download` carry an "Uncontrolled when printed /
  نسخة غير خاضعة للرقابة عند الطباعة" stamp on every page (setting on by default). The stamp is rendered once per text as a shaped PDF by
  Gotenberg and overlaid with PdfPig (already a dependency); when Gotenberg is unavailable the file is served with a header band
  generated by PdfPig in Latin text only, never failing the download. The stored file is never modified.

### 3.7 Dashboard, My Work, notifications, MCP, AI

* Dashboard (`docreg.dashboard.read`, filtered by visibility): documents by level / type / unit, expiring soon (90 days) and expired,
  pending approvals (requests in review / publish), recently published.
* My Work card: *Documents expiring that I own* + *requests needing my action* (framework card) + acknowledgements due.
* Templates: `docreg.published` (requester + owner), `docreg.expiryReminder`, `docreg.expired` (owner + office), `docreg.ackRequired`,
  `docreg.withdrawn`.
* MCP: `search_documents`, `get_document` (read, on; visibility rule), `document_expiry_list` (read, on). No write tools.
* AI (ADR 0029 defences; drafts only): **auto-classify** level / type / sensitivity from the draft file (suggestion in the review dialog),
  **summary AR / EN**, **duplicate / conflict check** against visible published documents (trigram on titles + extracted text, then the model
  explains overlaps), **what changed** between two versions, **Q&A** over published documents the user may see (retrieval by the
  visibility rule → top passages as untrusted data → answer with citations to the source documents). Prompts `docreg-*.v1.md`.

## 4. IP Registry (`ip`)

### 4.1 Configuration

Lookups `ip.property-type` (Methodology, Software / System, Literary work, Design, Invention, Trademark, Document), `ip.protection-type`
(Copyright, Industrial design, Patent, Trademark, Trade secret / internal only), `ip.contributor-role` (Author, Inventor, Designer,
Developer, Contributor). Settings area `ip`: per protection type **term in years** (default 20; trade secret / internal: none) and
**internal only** flag (default: trade secret); renewal reminder months (default **6 and 3**); MOET form header texts. Numbering
`ip.record` default `IP-Registry-{SEQ:4}`, `ip.internal` default `IP-INT-{YYYY}-{SEQ:4}`.

### 4.2 Data model

* `ip_record`: `number`, `name_ar/en`, `description`, `property_type`, `protection_type`, `source` (`request` | `document`), `request_id`,
  `document_id`, `document_version_id`, `owner_unit_id`, `status` (SPEC §7.2: `draft · submitted · under_review · returned · rejected ·
  approved_to_file · filed · registered · rejected_by_moet · expired · renewed · withdrawn`), `filing_date`, `application_no`,
  `registration_no`, `registration_date`, `expiry_date`, `certificate_file_id`, `internal`, notes, `linked` records via Links, `name_search`,
  audit, `xmin`.
* `contributor`: `ip_record_id`, `employee_id` **or** `external_name` + `external_organisation`, `role`, `share` (contribution, ratio,
  total must be 100 %), **`emirates_id_enc`** (Data Protection, purpose `ip.contributor.emirates-id.v1`, `[AuditMasked]`),
  `emirates_id_last` (last digit for the mask), `signature_status` (`pending`, `signed`, `uploaded`), `signed_copy_file_id`,
  `certificate_sent_at`.
* `registration_period`: `ip_record_id`, `from`, `to`, `registration_no`, `certificate_file_id`, `kind` (`initial`, `renewal`).
* `reminder_event`: idempotency of the renewal job.

### 4.3 Flow on the framework — request type `ip.registration`

| | |
|---|---|
| Form (code-defined) | `nameAr`, `nameEn`, `description`, `files` (supporting files), **`contributors`** (table: `employee` (employee) · `externalName` · `externalOrganisation` · `emiratesId` (**secret**, format `emiratesId`) · `role` (picklist) · `share` (number, %)) — at least one row, employee or external name per row, shares total 100 · `declaration` (checkbox, required: rights assigned to the organisation) |
| Stage `review` | approval `ip.review` (one step `ip.office`); approve form: `propertyType`, `protectionType`, `notes` |
| Stage `filing` | processing by `ip.office`: **Record filing** (`filingDate`, `applicationNo`) → `moet`; **Register internally** (only when the protection type is internal-only; `notes`) → completed (internal registration number, status `registered`, `internal`); **Withdraw** |
| Stage `moet` | **Record registration** (`registrationNo`, `registrationDate`, `expiryDate` proposed = registration date + term, editable, `certificate` file) → completed; certificate e-mailed to all contributors (employees' e-mail; externals listed for manual sending) · **Rejected by MOET** (`reason`) → back to `filing` with status `rejected_by_moet` (re-file or *Close*) |
| Handler | submit → IP record `submitted` + contributors (Emirates IDs re-encrypted with the IP purpose); approved → types, `approved_to_file`; filing → `filed`; registered → period, expiry, certificate copy, e-mails; rejected-by-MOET → reason |

### 4.4 Emirates ID (ADR 0086)

* Request form: `secret` values are encrypted **before** they reach the entity (`{"$enc": "<cipher>", "last": "3"}`); responses, audit values
  (ciphertext only), events, notifications, logs and AI payloads carry the mask `784-****-*******-3`; request field values are never logged.
* IP contributor: encrypted column, masked DTO (`emiratesIdMasked`). **Reveal** (`GET /ip/records/{id}/contributors/{cid}/emirates-id`) and the
  **MOET form PDF** need `ip.contributor.sensitive` (IP administrators only), both audited (`ip.emiratesId.revealed`, export). Nobody else
  ever receives the clear value; the PDF itself is the only output that contains it.
* Tests: API responses (request, IP, lists, MCP, AI), `audit.entry` values, outbox / notification payloads and the captured log output never
  contain the clear value.

### 4.5 MOET registration form (bilingual PDF)

Rendered server-side (Gotenberg) from an HTML template in the module (`Templates/moet-form.html`, AR right / EN left per field): applicant
organisation (branding), work title AR / EN, property and protection type, description (AR / EN — AI pre-fill available), creation date,
contributors table with names, nationality placeholder, **Emirates ID**, role, share, signature boxes, declaration, date. Header texts from
Settings → IP. Downloaded by IP administrators at stage `filing` (and later); audited as an export.

### 4.6 Auto-creation from Document Registry (ADR 0087)

`DocumentPublished(DocumentId, VersionId, DocumentNo, NameAr, NameEn, DocType, OwnerUnitId, OwnerEmployeeId, RequesterEmployeeId,
CreatesIpEntry, PublishedAt)` lives in `DocumentRegistry.Contracts`; Document Registry publishes it in the publish transaction (outbox,
ADR 0043). IP subscribes: when `CreatesIpEntry` is true and no IP record exists for the version → IP record with `source = document`, names
and owner unit from the document, property type `document` (lookup), status `approved_to_file` (the document was already reviewed), the
requester and owner as contributors (author, shares split equally, Emirates ID empty — completed by the IP office), and a Links-registry
link IP ↔ document. Document Registry learns the IP number through `IIpDirectory` (Contracts) for the badge. Idempotent (unique index on
`document_version_id`).

### 4.7 Renewal and expiry — job `ip.renewals` (daily 02:30)

Reminders to the IP office and the owner unit head at **6 and 3 months** before expiry (once each); at expiry → `expired` (flagged).
**Record renewal** on the IP page (new period from / to, registration number, certificate) → status `renewed`, expiry moved.

### 4.8 Screens, dashboard, AI, MCP, access

* IP register (filters type, protection, status, unit, year, expiring), IP page (overview, contributors with masked Emirates IDs and
  reveal for administrators, timeline, documents / certificates, request link, document link), expiring-soon list, dashboard (by type /
  unit / year, registered vs pending, upcoming expiries), *My intellectual property* (records where I am a contributor).
* AI: **suggest property / protection type** from description and files; **similarity check** against IP records and published documents
  the user may see; **pre-fill the MOET description** in Arabic and English. MCP: `search_ip_records`, `get_ip_record` (read, masked).
* Access: *IP requester* (everyone: submit, read own / as contributor), *IP office* (`ip.office`, All, MFA, process, approve by default),
  *IP administrator* (`ip.contributor.sensitive`, settings, MFA), *IP viewer* (read All, dashboard).

## 5. Platform changes (additive only)

| Component | Change | ADR |
|---|---|---|
| Workflow.Contracts | `IApprovalProcessSource` (dynamic process definitions); lazy seeding of version 1 for dynamic processes | 0081 |
| Settings | numbering tokens `{TYPE}`, `{ORG}` (values passed in `NumberingContext`) | 0082 |
| Host / SPA | three modules in the catalogue, sidebar entries, settings navigation entries, router, translation namespaces `requests`, `docreg`, `ip` | — |

## 6. API

`/api/v1/requests`: `GET /catalogue`, `GET /types/{code}` (form for the wizard), `POST /` (draft), `PUT /{id}` (save draft), `POST /{id}/submit`,
`POST /{id}/cancel`, `POST /{id}/decide` (approval with mini-form), `POST /{id}/actions/{action}` (processing actions), `GET /{id}`,
`GET /mine`, `GET /queue`, `POST /queue/bulk-complete`, `POST /{id}/files`, `GET /{id}/files`, `GET /{id}/pdf/{template}`, `GET /{id}/audit`,
`POST /ai/fill`; admin `GET/POST/PUT /admin/types…`, `POST /admin/types/{code}/preview-pdf`, settings.
`/api/v1/docreg`: `GET /library`, `GET /documents/{id}`, `GET /documents/{id}/versions`, `GET /documents/{id}/compare?from&to`,
`GET /documents/{id}/download`, `POST /documents/{id}/confirm-valid`, `POST /documents/{id}/acknowledge`, `PUT /documents/{id}/expiry`,
`GET /dashboard`, `GET /expiring`, AI endpoints, settings.
`/api/v1/ip`: `GET /records`, `GET /records/{id}`, `GET /records/{id}/moet-form`, `GET /records/{id}/contributors/{cid}/emirates-id`,
`PUT /records/{id}/contributors` (office completes contributors), `POST /records/{id}/renewal`, `GET /dashboard`, `GET /mine`, AI, settings.

## 7. Screens (AR + EN, RTL / LTR, shared components)

Requests: catalogue · wizard (dynamic form renderer for all field types, AI fill) · My Requests · processor queue · request page ·
Settings → Request types (list, **form builder**: sections, fields with type-specific options, visibility rules, preview in both languages;
stages; PDF templates with preview; links for intranet) · embeddable page.
Document Registry: library · document page (versions, compare, acknowledgements, validity) · dashboard · expiring list · Settings → Document
Registry. IP: register · IP page · dashboard · My IP · Settings → IP.

## 8. Tests

* **Unit**: `FormSchemaValidator`, `FormDataValidator` (every type, visibility, required-when, tables, Emirates ID), `RequestLifecycle`
  (every allowed / forbidden transition), stage engine, template renderer (encoding), `ValidityCalculator`, version numbering,
  `TextDiff`, Emirates ID mask, IP shares, renewal dates.
* **Integration** (Testcontainers / shared PostgreSQL): admin creates a custom type with the builder → employee submits → default PM Office
  step → processing → completed, without code; form schema validated server-side (bad values refused, hidden-required ignored, unknown keys
  refused); document new → review with classification (generic task approval refused without it) → Document ID → publish → library; new
  version supersedes; expiry reminders 60 / 30 / expire / confirm with the fake clock; sensitivity hides documents from unauthorised users in
  library, document page, download, search, Ask tools, MCP and Q&A; publishing a flagged type creates an IP record through the outbox;
  IP filing / registration / certificate e-mail / renewal reminders; Emirates ID never unmasked in responses, audit values, outbox payloads
  or logs; authorization matrix for every new endpoint (wrong role → 403, out of scope → 404).
* **E2E** (Playwright, ar + en, screenshots `docs/screenshots/phase-14`): request catalogue, form builder, document submission and review,
  library with sensitivity, version compare, expiry reminder, IP request, MOET form PDF, IP register, auto-created IP from a published
  document.
