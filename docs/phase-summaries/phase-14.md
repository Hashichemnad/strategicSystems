# Phase 14 — Request Framework, Document Registry, IP Registry: summary

**Status:** built and verified in the build session · awaiting owner acceptance
**Branch:** `claude/jolly-brahmagupta-n7069d`
**Planning:** the plan is the design note [`docs/design/requests.md`](../design/requests.md), written and committed before the code
(commit `docs(requests): Phase 14 design note…`). This session ran without an interactive approval step (as Phases 5–12), so the plan was written
first and then implemented; its open choices are ADRs 0080–0088 (numbered from 0080 so the Portfolio phase built in parallel keeps its range).
Two points of the plan changed during the build and the design note was updated (see *Deviations*).

## What was built

| Area | Result |
|---|---|
| **Request Framework** (`Strategy.Modules.Requests` + `.Contracts`, schema `requests`, system `requests`) | **Request types** in one table: *built-in* (declared in code by their module with code-defined fields and stages, seeded once, then relabelled / extended in Settings) and *custom* (form builder, no code). **Form schema** in jsonb: sections, 15 field types (text, textarea, richtext, number, currency, date, checkbox, picklist, multipicklist, employee, unit, file, record — any Links-registry type, table with typed columns, secret), `visibleWhen` / `requiredWhen`, AR / EN labels and help. **Server-side validation** (`FormSchemaValidator` for the builder, `FormDataValidator` for data: types, lengths, ranges, decimals, lookup codes, existing employees / units, the request's own files, readable records, table rows, Emirates ID format; drafts check types, submissions everything; hidden fields dropped; unknown keys refused). **Stage / action engine**: approval stages on the Phase 4 engine (with an *approve form* enforced in the hook — the generic task screens cannot bypass it), processing stages with processor roles, SLA in working time, actions complete / custom / return / reject with mini-forms; explicit lifecycle table (draft · in_review · returned · in_progress · completed · rejected · cancelled). **Handlers** of built-in types run in the framework's transaction (ADR 0023). **Catalogue**, **My Requests**, **processor queue** with SLA and safe bulk complete, request page with current status, **PDF templates** with merge fields (HTML-encoded, Gotenberg), **embeddable submit page** and deep links, AI document-to-form for every type, MCP tools `my_requests`, `request_status` (read, on), `submit_request_draft` (write, **off**, never submits), search source, task-able, current status, My Work, 5 AR / EN templates, overdue job |
| **Document Registry** (`Strategy.Modules.DocumentRegistry` + `.Contracts`, schema `docreg`) | Built-in types `docreg.document` (new document or new version) and `docreg.withdrawal`. Review (approval `docreg.review`, default one Strategy Office / PM Office step) with **level, type, sensitivity**; **Document ID** `{ORG}-{UNIT}-{TYPE}-{YYYY}-{SEQ:2}` and **version** major.minor on approval; **publish** (signed PDF, share-to-all — refused for Secret, effective date, expiry from the type rule, acknowledgement) → previous version **superseded**, requester notified, **`DocumentPublished`** through the outbox. **Validity by type** (seeded: policies / procedures 24 months, manuals / strategies 36, legal and forms until superseded), daily job `docreg.validity`: reminders **60 / 30 days** once per expiry, **Expired** status, owner **confirms still valid** (extended, logged), submits a new version or requests withdrawal. **One visibility rule** (`DocumentVisibility`, sensitivity clearance in Settings: all employees / owner unit and below / role keys / share-to-all allowed) for library, page, versions, compare, download, dashboard, search, Ask, MCP, AI, Links badges and `IDocumentDirectory`. Library with facets and full-text search in titles and file text; **version compare** (word-level LCS); read acknowledgements; **"uncontrolled when printed"** stamp on downloads (Gotenberg + PdfPig, never modifies the stored file); dashboard; AI: classify, summary, duplicate / conflict check, what changed, Q&A with sources; MCP `search_documents`, `get_document`, `document_expiry_list` |
| **IP Registry** (`Strategy.Modules.IpRegistry` + `.Contracts`, schema `ip`) | Built-in type `ip.registration`: work, supporting files, **contributors** (employee or external, role, contribution % totalling 100, **Emirates ID** as a `secret` field), declaration. Review (`ip.review`) with property and protection type → IP record `IP-Registry-NNNN`, contributors with the Emirates ID **re-encrypted** for the register. On the IP record: **filing** (date, application no.), **registration** (number, date, first publication date, expiry proposed from the protection rule — copyright 50 years from first publication, patent / industrial design 20, utility model 10, trademark 10 renewable from registration — certificate) with certificate notices to contributors, **rejected by MOET** → re-file or close, **internal-only** registration (`IP-INT-{YYYY}-{SEQ:4}`), **renewal** periods; daily job `ip.renewals` (reminders at **6 and 3 months**, expiry). **Auto-creation** from `DocumentPublished` when the document type is flagged *Creates IP register entry* (idempotent per version, Links-registry link to the document). **MOET registration form**: bilingual PDF (Arabic right, English left) with the clear Emirates IDs — IP administrators only, audited; **reveal** endpoint likewise. Dashboard, My IP filter, AI: suggest types, similarity (IP records and visible documents), MOET description AR / EN; MCP `search_ip_records`, `get_ip_record` (masked) |
| **Platform (additive)** | Workflow: `IApprovalProcessSource` — dynamic processes (one per custom request type), version 1 seeded on first use (ADR 0081). Settings: numbering tokens `{TYPE}` and `{ORG}` (ADR 0082). Host: SPA fallback for routes whose last segment is a dotted type code |
| **Web** (AR + EN, RTL / LTR) | Requests: catalogue, wizard (one step per section, review, save as draft, AI fill as a draft to accept), My Requests, processor queue, request page (stepper, current status, decide dialog with approve form — plus *Suggest classification* and *Check duplicates* for documents, stage actions with mini-forms, PDF menu, tabs), embeddable page, Settings → Request types and **form builder** with live AR / EN preview, stages and PDF templates. Documents: library, document page (validity actions, versions, compare, acknowledgements, linked records, tasks, history), compare page, dashboard, Settings → Document Registry. IP: register, record page (actions, contributors with masked IDs and reveal, registration, documents, linked, history), dashboard, Settings → IP. Shared reusable `FormRenderer`. History tab for requests, documents and IP records |

## Acceptance checks (BUILD_PLAN Phase 14)

| Check | Evidence |
|---|---|
| Employee submits a new document from the catalogue; Strategy Office classifies, approves, publishes; library shows it to the right audience only | Integration `DocumentRegistryTests.Submission_review_with_classification_publication_and_a_new_version_that_supersedes` (catalogue lists the type; the generic task approval is refused without classification — `approveFormRequired`; unknown classification code refused; Document ID `ORG-<unit>-POL-2026-01`, version 1.0, expiry +24 months; new version keeps the ID, 2.0 current, 1.0 superseded; a second revision while one is in progress refused; compare) and `Sensitivity_hides_documents_in_library_page_download_search_and_mcp` (department B sees Internal + shared only, the owner unit also Confidential, the office everything; page / download 404; global search tool, `search_documents` and `get_document` follow the same rule; a request's record field refuses a document the user cannot see). E2E (ar + en): `request-catalogue`, `document-submission`, `document-review`, `library-sensitivity` |
| Publishing a flagged document type creates the IP record; MOET form PDF prints in Arabic | Integration `IpRegistryTests.Publishing_a_document_of_a_flagged_type_creates_an_ip_record_linked_to_it` (manual flagged → record `source=document`, `approved_to_file`, requester as author, linked badge on the document; policy not flagged → none). MOET form: `Request_review_filing_registration_certificate_…` (Arabic HTML with `dir="rtl"`, names and the clear Emirates ID for administrators, 403 for the office). E2E: `ip-from-document`, `document-linked-ip`, `moet-form-pdf` (the real PDF rendered with pdftoppm) |
| An admin creates a new simple request type with the form builder without code | Integration `RequestFrameworkTests.An_admin_builds_a_type_without_code_and_it_runs_from_catalogue_to_completion` (only type administrators reach the builder; a bad schema refused; the type in the catalogue; `TRN-` numbers; draft and submit validation incl. required-when; the run-time process `requests.type.<code>` with the default office step; processing with SLA, the completion mini-form validated, completed; My Requests; PDF template). E2E: `form-builder` |

**Also asked for in the prompt:**

| Request | Evidence |
|---|---|
| Form schema validated server-side | Unit `FormValidationTests` (every type, references, unknown keys, required-when / hidden, drafts vs submission, table cells and row counts, conditions, builder mistakes); integration (above) |
| Document versioning and supersede | Integration (above); unit `DocumentRulesTests.Version_numbers` |
| Expiry reminders / confirm / expire with the fake clock | Integration `Expiry_reminders_at_60_and_30_days_then_expired_and_confirmation_extends` (no reminder at 70 days, one at 59 — twice-run idempotent, 30 at 29, expired after, still in the library as expired, outsider 403 on confirm, owner confirms +24 months from today, back to published); unit `Reminders_are_due_once_per_count`. E2E `expiry-reminder` (the job run from the API) |
| Sensitivity hides documents (search, Ask, MCP included) | Integration (above) |
| Emirates ID never unmasked in API responses, logs or audit values | Integration `Request_review_filing_registration_certificate_with_the_emirates_id_never_unmasked`: request and IP responses, request and IP history (audit) endpoints, `audit.entry` values / data, outbox payloads, request jsonb and the encrypted contributor column contain no digits of it; a **capturing Serilog sink** proves the logs never contain it; the mask sent back keeps the stored value; reveal and MOET form only for IP administrators. Unit `SecretValuesTests` |
| Internal-only protection, renewal reminders | Integration `Internal_only_protection_registers_internally_and_renewal_reminders_then_expiry` (filing refused, internal number, no expiry; reminders 6 then 3 months, idempotent, expired) |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` · `dotnet format --verify-no-changes` | 0 warnings, 0 errors · clean |
| `dotnet test --solution Strategy.slnx` | **2 645** tests (unit, architecture, integration on real PostgreSQL with Mailpit / Gotenberg / Keycloak / OpenLDAP containers): 2 640 green in the full parallel run; the 5 `ExternalProviderTests` failed because the OpenLDAP container exited at start-up and passed when the class was re-run. New: **83** unit tests, **8** integration tests (the Phase 14 scenarios above) |
| `tools/check-vulnerable.sh` · `tools/check-api-client.sh` | no High / Critical · OpenAPI document and TS client up to date |
| `npm run lint` (incl. the RTL class check) · `format:check` · `typecheck` · `npm test` | clean · **174 / 174** Vitest (new: message keys of `requests`, `docreg`, `ip` in AR and EN, form logic, form renderer, IP logic, document helpers) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (the same 14 moderate dev-tool advisories as earlier phases) · 613 packages allowed |
| `npx playwright test e2e/requests.spec.ts` | **2 / 2** (ar + en), screenshots with `E2E_SCREENSHOTS=phase-14` |

Existing tests updated for Phase 14 facts: the job dashboard lists `docreg.validity`, `ip.renewals`, `requests.stage-overdue` (API test and E2E: 16 jobs); the effective roles of a new user include `requests.requester`, `docreg.reader`, `ip.requester`; the authorization matrix sends a file to the `/ai-fill` upload route.

**Dependencies added:** none. PdfPig (Apache-2.0) was already a dependency (AI text extraction) and is now also used by the Document Registry for the stamp.

## Decisions (ADRs) — all **accepted by the owner** before the pull request
- [0080](../decisions/0080-request-framework-types-forms-stages.md) Built-in and custom request types, form schema, stages and handlers.
- [0081](../decisions/0081-dynamic-approval-processes-and-approve-forms.md) Run-time approval processes for custom types (additive Workflow contract) and approve forms enforced in the hook.
- [0082](../decisions/0082-numbering-type-and-org-tokens.md) Numbering tokens `{TYPE}` / `{ORG}`; Document ID and version numbers.
- [0083](../decisions/0083-document-sensitivity-clearance.md) Sensitivity clearance and one visibility rule for every read path.
- [0084](../decisions/0084-document-validity-and-expiry.md) Validity by type, reminders, confirmation and expiry.
- [0085](../decisions/0085-uncontrolled-copy-watermark.md) "Uncontrolled when printed" stamp on downloads.
- [0086](../decisions/0086-emirates-id-encryption-and-masking.md) Emirates ID encryption, masking and who may see it.
- [0087](../decisions/0087-ip-from-documents-renewal-internal.md) IP from published documents, expiry, renewal and internal-only protection.
- [0088](../decisions/0088-request-pdf-templates-and-embedding.md) Request PDF templates and the embeddable submit page.

## Owner decision applied before the pull request
With the ADRs accepted, the owner changed one seeded value and added a type (ADR 0087 updated): **copyright = 50 years from first publication**
(works owned by the organisation), not 20 years from registration; new protection type **Utility model, 10 years**. Patent 20, industrial design
20, trademark 10 (renewable) and trade secret (internal-only) are unchanged. Each protection rule now says whether its term counts from the
registration date or the first publication; the registration action records the first publication date (records created from a published
document take its publication date) and refuses a publication-based term without it unless the office gives an explicit expiry. Tests:
unit `Terms_count_from_registration_or_first_publication`, `Seeded_protection_terms`; integration (copyright registration refused without the
date, publication after registration refused, expiry 2025-06-15 + 50 = 2075-06-15; utility model registration 2026-02-01 → 2036-02-01;
records from documents carry the publication date); Vitest `proposeExpiry`.

## Deviations
- **From the SPEC:** none known. Interpretations are in the ADRs (share-to-all refused for Secret; a level without a clearance rule is office-only; confirmation extends from the current expiry).
- **From the plan (design note updated):**
  - **IP filing / registration / MOET outcome / renewal are actions on the IP record**, not stages of the request: the request covers submission and review; the same record actions serve records created from documents.
  - **The signed final file stays attached to its publishing request** and the version points at it; the Document Registry checks visibility itself on download. Copying it inside the publish transaction wrote on a second connection while the audit-chain lock was held (deadlock).
  - A returned request goes back to **the stage that returned it**, not to the first stage.
- **Platform changes (additive):** `IApprovalProcessSource` in Workflow; `{TYPE}` / `{ORG}` numbering tokens and their labels; SPA fallback for dotted route segments; History tab entries `serviceRequest`, `document`, `ipRecord`; dev seed (everyone may open the three systems; Mariam holds their office and administrator roles). Shared files were changed additively only (router, sidebar, settings navigation, translation namespaces, `BUILD_PLAN.md` checklist row).

## Known gaps
- The **demo AI provider** does not answer the new prompts; the AI features work with a configured provider and degrade gracefully (buttons hidden) when AI is off.
- **Public links** for Public documents are a setting only (no anonymous endpoint yet, ADR 0083).
- The form builder edits only the *simple stage template* for custom types; richer stage graphs are code (built-in types).
- DOMPurify is not a dependency yet: rich-text values are shown as plain text (safe), not as formatted HTML.
- The `unitIds` submitter rule is stored but has no editor in the builder (everyone / roles only).
- Benchmarking (Phase 15) will add its built-in type the same way.

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml --profile dev up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host
cd web && npm ci && npm run dev        # http://localhost:5173
```
1. **Mariam** (`mariam@strategy.local` / `Strategy#Demo2026`, MFA): Settings → Request types → *New type*: add sections and fields, see the AR / EN preview, switch on processing, save. Settings → Document Registry: tick *Creates IP register entry* for *Manual / guide*.
2. **Ahmed** (`ahmed@strategy.local`): Requests → the catalogue shows the new type, *Submit a document* and *IP registration*. Submit a document with a draft file.
3. Mariam: Requests → the request → *Decide* → approve with level / type / sensitivity → *Publish* with the signed PDF. Document library shows it to the right audience; *Submit new version*, then *Compare*.
4. Publish a *manual*: Intellectual property shows the record created from it; *Record filing*, *Record registration*; contributors show masked Emirates IDs, *Reveal* and *MOET form* for the IP administrator.

## Cloud environment notes
- PostgreSQL 16 local (`max_connections = 600`, `STRATEGY_TEST_POSTGRES`); Docker started manually for Gotenberg / Mailpit through `mirror.gcr.io/`; Tesseract installed for the OCR tests; E2E against database `strategy` with `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium`.
- Screenshots (`E2E_SCREENSHOTS=phase-14`) in [`docs/screenshots/phase-14/`](../screenshots/phase-14/) as `ar-*` / `en-*` pairs: `request-catalogue`, `form-builder`, `document-submission`, `document-review`, `library-sensitivity`, `version-compare`, `expiry-reminder`, `ip-request`, `moet-form-pdf`, `ip-register`, `ip-from-document`, `document-linked-ip`.
- The three web areas were built by parallel sub-agents on the shared wiring prepared first; all front-end gates were re-run on the merged result.
