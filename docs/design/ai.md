# Design note — AI platform (`Strategy.Platform.AI`, schema `ai`)

**Phase 5 · SPEC §0.5 · CLAUDE.md §2 rule 6, §12.** Written before the code; open choices are ADRs 0028–0032.

## 1. Principles
- **AI drafts, people decide.** No AI endpoint creates, saves, submits, approves or rejects anything. Every AI result is returned to the
  caller as a draft; the user accepts or edits it and then saves through the normal endpoint (which is audited as the user's own action).
- **One gateway.** Every feature calls `IAiGateway`; nothing talks to a provider SDK directly. The gateway enforces, in this order: internal
  principal → master switch → feature switch → per-role permission (`ai.<feature>.use`) → provider configured → monthly limits → data controls
  and masking → prompt → provider call with time-out → JSON parse → **schema validation** (one repair retry) → unmasking → usage log + audit.
- **Works without AI.** Master switch off (the default on a new installation) → `GET /ai/capabilities` reports nothing available, the UI shows
  no AI action, and every AI endpoint answers **404 `ai.featureDisabled`** (`ai.errors.featureDisabled`). No business endpoint depends on AI.

## 2. Providers (ADR 0028)
| Table `ai.provider` (AuditableEntity, audited `ai.provider`) | Notes |
|---|---|
| `name` | admin label |
| `type` | `anthropic` · `openai` · `azure-openai` · `openai-compatible` (+ `demo` only when `AI:EnableDemoProvider=true`, Development / Testing) |
| `endpoint` | optional for Anthropic / OpenAI (SDK default); required for Azure (`https://<resource>.openai.azure.com/openai/v1/`) and self-hosted (`http://ollama:11434/v1/`) |
| `api_key_protected` | `ISecretProtector` purpose `ai.provider.key.v1`; audited masked; API returns `hasApiKey` only |
| `default_model`, `models` (jsonb) | model list fetched from the provider (**Fetch models**) or typed |
| `temperature`, `max_output_tokens`, `timeout_seconds`, `active` | per provider |

- **Adapters** (`IAiProviderAdapter`, one per type) build an `IChatClient` (Microsoft.Extensions.AI): Anthropic official SDK
  (`AnthropicClient.AsIChatClient`), OpenAI SDK (`OpenAIClient.GetChatClient(model).AsIChatClient()`) for OpenAI, Azure OpenAI (v1 endpoint) and
  OpenAI-compatible servers. Adding a provider = one adapter class; features are untouched.
- **Test connection** `POST /ai/providers/{id}/test` (optionally with an unsaved key / endpoint / model): a one-line prompt, answers
  `{success, latencyMs, model, messageKey}`; audited `ai.provider.test`.
- **Model per feature:** each feature may name a provider and a model; otherwise the default provider and its default model.

## 3. Settings (`settings.setting` area `ai`, edited in Settings → AI, audited as settings changes)
```
enabled (master, default false) · defaultProviderId
features: { <key>: { enabled (default true), providerId?, model? } }
dataSharing: { maskPersonalData (default true), allowDocuments (default true), systems: { <code>: { allowed, excludedFields[] } } }
limits: { monthlyTokens?, monthlyRequestsPerUser? }   (null = no limit)
```
**Per-role access** uses the access model (no second mechanism): one permission per feature `ai.<feature>.use`; the seeded role
*AI user* (`ai.user`, assigned to everyone) holds all of them. Restrict a feature by removing it from that role and granting a narrower role.
Administration: `ai.settings.manage` (providers, switches, data, limits, test) and `ai.usage.read`; role *AI administrator* (MFA), part of the
platform administrator roles.

## 4. Features (registered in code: `AiFeatureDefinition`, modules may add more)
| Key | Endpoint (`/api/v1/ai`) | Output (validated JSON) | Provider |
|---|---|---|---|
| `extract` | `POST /forms/{formKey}/extract` (multipart file) | `{language, fields:[{name, value, confidence, source}]}` | yes |
| `brief` | `POST /brief/{resourceType}/{resourceId}` | `{summary, changes[], attentionPoints[]}` + **computed** changed fields | yes |
| `translate` | `POST /translate` `{text, from, to, systemCode?}` | `{translation}` | yes |
| `similar` | `POST /similar/{resourceType}` `{text, excludeId?}` | ranked records (full-text + trigram) | **no** (baseline) |
| `ask` | `POST /ask` `{question, history[]}` | `{answer, references[], toolCalls[]}` | yes (tool calling) |

`GET /capabilities` (any signed-in user): `{enabled, features:{key: available}}` = master ∧ feature ∧ permission ∧ provider (or no provider needed).

### 4.1 Document-to-form extraction
- Modules declare an **AI form** (`AiFormDefinition`): key, system, the permission needed to use it (e.g. `sample.item.create`), and fields
  (name, type `text|longText|number|date|choice`, AR/EN label, hint, max length, choices).
- Upload: ≤ 10 MB, **PDF / DOCX / PPTX / TXT** recognised by content (magic number + OpenXML part), not by name. The document is **not stored**;
  the audit event keeps name, size and SHA-256.
- Text extraction (ADR 0030): PdfPig (Apache-2.0) per page, DocumentFormat.OpenXml (MIT) for Word paragraphs / tables and PowerPoint
  slides + notes; NFKC normalisation turns Arabic presentation forms into letters; `[page n]` / `[slide n]` markers; 60 000 characters max
  (truncation reported). **Scans** (ADR 0033): with OCR on (Settings → AI, off by default), PDF pages without a usable text layer (rendered
  with PDFium at the scan's resolution — JBIG2 and CCITT included) and PNG / JPEG / TIFF images are read by Tesseract (`ara+eng`) under `[page n scan]` / `[image scan]` markers; values quoted from them are marked
  `fromScan` ("From scan" in the review). With OCR off: `ai.extract.ocrOff` / `ai.extract.noText`.
- Post-processing (never trusts the model): unknown fields dropped; values coerced to the field type (number, ISO date, allowed choice, max
  length); the **source snippet must be found in the document** (normalised comparison) — otherwise `sourceVerified=false` and confidence
  capped at 0.3; the page / slide of the snippet is computed from the markers.
- Result: `{formKey, fileName, truncated, language, fields:[{name, value, confidence, source, sourceVerified, location}], warnings}`.
  **Nothing is saved.** The review UI fills the form with highlighted drafts; the user accepts or edits each field (or *Accept all*) and the
  normal **Save** stays disabled while an unreviewed AI value remains.

### 4.2 Approver brief
- Modules implement `IAiRecordSource` per resource type: the record's fields (name = audit field name, AR/EN label, value) for a user who may
  read it, else null (→ 404).
- The workflow contract gains `IApprovalEngine.ListAttemptsAsync` (attempts with submit / decision comments). With a previous attempt, the
  **changed fields since the last submission are computed from the audit trail** (`IAuditQuery.AsOfAsync` at the previous submission vs now),
  so the "what changed" facts never come from the model. The model writes the paragraph in the user's language.
- Shown on the shared approval panel ("Approver brief" button, "AI-generated" label, regenerate). Not stored.

### 4.3 Translation, similar records, Ask the system
- **Translate:** the button next to a bilingual field fills the other language as a highlighted draft.
- **Similar records:** modules implement `IRecordSearchSource` (search + similar, scope applied in the database query); the Sample item
  uses `pg_trgm` similarity on the names. No provider call, no tokens.
- **Ask the system:** the model gets the **read tools** of the tool registry (§ MCP design note) as functions, executed **with the asking
  user's principal** (same `IAccessService`), at most 5 rounds. Write / approve tools are never given to it. Every tool call is audited
  (`ai.tool.call`, channel `ai`). Answer + references (deep links).

## 5. Prompts and output validation (ADR 0029)
- Templates are versioned embedded resources `Prompts/<feature>.v<n>.md` (front matter `feature`, `version`), bilingual instructions, the
  output **JSON schema** inlined. Modules can add templates from their own assembly (`<module>/Ai/Prompts`).
- Output: the first JSON object in the reply (code fences stripped) is validated by an in-house validator for the schema subset we emit
  (type, properties, required, additionalProperties, enum, items, maxLength, maxItems, minimum / maximum). One repair retry with the
  validation errors; then `ai.invalidOutput` (502-like 422). No provider-specific "JSON mode" is required, so any provider works.

## 6. Prompt-injection defences (uploaded documents and record text)
1. Untrusted text is passed **only in the user message, as data**, inside `<untrusted_document id="…" nonce="R">…</untrusted_document>`
   with a random nonce per call; any occurrence of the tag name in the content is neutralised before wrapping.
2. The system prompt states that content inside those tags is data, never instructions, and that the only output is the schema.
3. Extraction runs with **no tools**; Ask the system gets read-only tools that are permission-checked anyway.
4. Output is schema-validated and post-processed against the field list (unknown fields dropped, values coerced, snippets verified).
5. Nothing is acted upon automatically; a person reviews every value. Length limits on input and output.

## 7. Data controls and masking
- Per system: may data of this system be sent (`allowed`), and **excluded fields** (removed before sending, by field name). Documents can be
  disallowed globally (`allowDocuments`).
- **PII masking** (on by default): e-mail addresses, phone numbers, Emirates ID numbers and the names of people the caller supplies (record
  owner, submitter, approvers) are replaced by placeholders (`[EMAIL_1]`, `[PERSON_2]`…) before sending and restored in the output.

## 8. Usage log and limits
`ai.usage_log` (append-only, not audited field by field): time, user, feature, provider, model, input / output tokens, latency, success,
error code, record type / id, channel. **Monthly limits** (installation tokens, requests per user) are checked before each call in the
installation's time zone → 422 `ai.limitReached`. Settings → AI → Usage: this month per feature / user, the log with filters.
Every call is also an audit event `ai.<feature>` with channel **`ai`** (feature, provider, model, tokens, record, outcome — never prompts,
document text or keys).

## 9. Permissions
| Permission | Default role |
|---|---|
| `ai.extract.use`, `ai.brief.use`, `ai.translate.use`, `ai.similar.use`, `ai.ask.use` | *AI user* (everyone) |
| `ai.settings.manage`, `ai.usage.read` | *AI administrator* (MFA) |
Vendor (Wasel) principals: refused on every AI endpoint (403 `ai.vendorRefused`).

## 10. Screens
Settings → **AI**: master switch; *Providers* (list, editor with key field, Fetch models, **Test connection**); *Features* (switch, provider,
model, who has access → link to roles); *Data sharing* (systems, excluded fields, masking, documents); *Limits*; *Usage*.
Shared components: `AiDraftField` highlight + "AI draft" chip + confidence + source popover; `FillFromDocument` dialog; `TranslateButton`;
`SimilarRecords`; `ApproverBrief` (approval panel); **Ask** drawer in the top bar. All hidden when `capabilities` says unavailable.

## 11. Tests (no real provider in CI: `FakeChatClient` adapter registered in tests)
Unit: schema validator, masker (mask / unmask round trip), prompt rendering and nonce neutralisation, snippet verification and coercion,
text extraction (generated DOCX / PPTX in Arabic and English, PDF), limit month boundaries. Integration: settings + encrypted key, test
connection, switches off → 404 and capabilities false, per-role permission, extraction maps fields and **creates no record**, brief 404 outside
scope and computed changes, translate, similar within scope, ask uses tools with the user's permissions, usage limits, audit channel `ai`,
vendor refused. E2E (ar + en): AI settings, test connection, AI-filled form review, approver brief.
