# Phase 5 — AI platform and MCP server: summary

**Status:** built and verified in the build session · awaiting owner acceptance
**Branch:** `claude/focused-lovelace-f7moiy`
**Planning:** the plan is the design notes [`docs/design/ai.md`](../design/ai.md) and [`docs/design/mcp.md`](../design/mcp.md) (plus the
Phase 5 section of [`docs/design/sample.md`](../design/sample.md)), written and committed before the code. This session ran without an
interactive approval step, so the plan was written first and then implemented as it stands; its open choices are ADRs 0028–0032.

## What was built

| Area | Result |
|---|---|
| AI platform (`Strategy.Platform.AI` + `.Contracts`, schema `ai`) | **Providers** (`ai.provider`): Anthropic Claude (official `Anthropic` C# SDK), OpenAI, **Azure OpenAI** (v1 endpoint) and **OpenAI-compatible** servers (Ollama / vLLM) through Microsoft.Extensions.AI `IChatClient` adapters; endpoint, **encrypted key** (`ISecretProtector`, never returned, audited masked), default model + model list (**Fetch models**), temperature, answer tokens, time-out, active. **Test connection** (works while AI is off, audited). An offline **demo provider** for Development / E2E only (`AI:EnableDemoProvider`) |
| Switches and routing | Settings area `ai`: **master switch (off on a new installation)**, default provider, **per feature: on / off, provider, model**. **Per-role access = one permission per feature** (`ai.extract/brief/translate/similar/ask.use`) held by the seeded role *AI user* (everyone), narrowed in Settings → Roles (ADR 0028). `GET /ai/capabilities` drives the UI; a disabled feature answers **404 `ai.featureDisabled`** |
| One gateway | `AiGateway`: internal principal → master → feature → permission → provider → **monthly limits** → **data controls + PII masking** → versioned prompt → call with time-out → JSON → **schema validation** (one repair round) → unmasking → **usage log + audit (channel `ai`)**. Provider failures, time-outs and invalid output become error codes, never 500s, and never block a business action |
| Prompts and output (ADR 0029) | Embedded, versioned templates `Prompts/<feature>.v1.md` (front matter, system / user parts, trusted placeholders only); in-house validator for the JSON-schema subset the templates use; modules can add templates from their own assembly |
| Prompt-injection defences | Untrusted text only as data in `<untrusted_document nonce>` blocks with a fresh nonce; tag look-alikes neutralised; "data, never instructions" in the system prompt; no tools for extraction; read-only, permission-checked tools for Ask; schema validation; **source snippets verified against the document** (unverified → flagged, confidence ≤ 0.3); a person reviews everything |
| Data controls | Per system: may be sent / **excluded fields**; uploaded documents on / off; **masking** of e-mails, Emirates ID numbers, phone numbers and the record's people (restored in the output) |
| Usage and limits | `ai.usage_log` (user, feature, provider, model, tokens in / out, latency, outcome, record); **monthly token limit (installation)** and **monthly requests per user**, counted in the installation's time zone → 422 `ai.limitReached`; Settings → AI → Usage (this month by feature, paged log) |
| Document text extraction (ADR 0030) | Recognised by content, not name: **PDF** (PdfPig; Arabic lines put in reading order from glyph positions, Latin / digit runs kept LTR), **Word** (paragraphs + tables) and **PowerPoint** (slides in order + notes) with DocumentFormat.OpenXml; NFKC for Arabic presentation forms; page / slide markers; 60 000 characters; 10 MB; file not stored (audit keeps name, size, SHA-256) |
| **OCR for scans** (ADR 0033, added before the pull request at the owner's request) | Optional **Tesseract 5** text recognition with **Arabic and English** data, **bundled** in the application image and run as a separate process (per-page time-out, no native interop; `INSTALL_OCR=false` builds without it). **Settings → AI → OCR: on / off (off by default)**, languages (`ara+eng`), scanned-page limit (20), and whether the engine is installed on the server. **PDF pages without a text layer** are recognised from their page image (PNG via PdfPig, or the JPEG stream — no GPL rasteriser needed); **PNG / JPEG / TIFF** uploads are recognised directly. Recognised text sits under `[page n scan]` / `[image scan]`; values quoted from it are marked **`fromScan`** and the review shows a **"From scan"** chip with the translated location ("page 2", "image"); the toast counts the scanned pages; the audit event records them. With OCR off, scans are explained (`ai.extract.ocrOff` / `noText`) and no model is called |
| Shared AI features | **Document-to-form extraction** (`POST /ai/forms/{formKey}/extract`; modules declare AI forms), **approver brief** (`POST /ai/brief/{type}/{id}`; changed fields since the last submission **computed from the audit trail**, return comments included; paragraph + points from the model), **AR ⇄ EN translation**, **similar records** (trigram similarity within the user's scope, no model), **Ask the system** (read tools as functions, run with the user's own permissions, ≤ 5 rounds, references to records) |
| Tool registry (ADR 0031) | `IAppTool` contract in `AI.Contracts`; registered by each component / module; start-up guard: **an approve tool can never be default-enabled**; `ToolExecutor` audits every call (`<channel>.tool.call`) |
| MCP server (`Strategy.Platform.Mcp`, schema `mcp`, ADR 0032) | Official `ModelContextProtocol.AspNetCore` SDK at **`/mcp`**, Streamable HTTP, stateless; `tools/list` / `tools/call` from the registry filtered by admin switches, enabled systems and the caller's permissions. Gate: **off by default** (404), **tokens only** (401 + `WWW-Authenticate` with resource metadata), **vendor principals 403**, PAT without `mcp` scope 403. **Rate limits** per user and per tool. Every call and refusal audited, channel `mcp`, with the client |
| OAuth 2.1 (OpenIddict) | Authorization code + **PKCE required**, public clients, refresh tokens; **Data Protection tokens** (no certificate; survive restarts); token + authorization entry validation (revocation is immediate); discovery at `/.well-known/oauth-authorization-server` and `openid-configuration`, protected-resource metadata (RFC 9728), **dynamic client registration** (RFC 7591) with allowed redirect hosts, **resource indicators** (only `<host>/mcp`); SPA **consent page** with a protected one-time ticket bound to user, client, redirect URI and PKCE challenge; remembered consent; OAuth tokens can never call `/api` |
| Core MCP tools | `whoami`, `my_notifications`, `my_pending_approvals`, `record_status` (404 semantics), `search_records` (permission-filtered), `get_record` (AI exclusions applied), `approval_decide` (**approve, off by default**; when enabled, the engine still requires the caller to be an approver of the task) |
| Sample module (worked example) | AI form `sample.item`, record source (brief, `get_record`), search source (`search_records`, similar items) — `Ai/SampleAi.cs` |
| Web | **Settings → AI** (master switch; providers with key, Fetch models and **Test connection**; features with provider / model and who may use them; data sharing, masking, documents, monthly limits; usage), **Settings → MCP server** (switch, server address and how to connect, OAuth registration and redirect hosts, token lifetimes, rate limit, tools with kind / switch / limit and the approve warning, connected clients with Disconnect), **OAuth consent page**. **AI-filled form review**: *Fill from a document* → highlighted fields with "AI draft", confidence, source quote (page / slide), **Accept / Accept all; editing counts as review; Create stays disabled until every AI value is reviewed**. Translate buttons under bilingual fields, **similar items** while typing a name, **approver brief** on the shared approval panel ("AI-generated", changed fields table, disclaimer), **Ask** in the top bar. Everything hidden when the capability is off. AR + EN, RTL / LTR |
| Deployment | `ForwardedHeaders:Enabled` / `KnownProxies` (opt-in) so the OAuth issuer is the public HTTPS address behind a proxy; `Mcp:RequireHttps` (default true); compose variables and [`deploy/README.md`](../../deploy/README.md) section; [`docs/mcp-guide.md`](../mcp-guide.md) for customers |

## Acceptance checks (BUILD_PLAN Phase 5)

| Check | Evidence |
|---|---|
| Admin configures a provider and passes **Test connection**; switching provider needs no code change | **Integration** `AiPlatformTests.Provider_key_is_encrypted_never_returned_and_test_connection_reports_the_outcome` (key encrypted in the DB and in the audit snapshot, decrypted only for the adapter; test passes / fails with `ai.errors.providerError`; model list). Every feature resolves its client through the adapter of the configured type. **E2E** (ar + en) `ai.spec` adds a provider in the UI and passes Test connection; screenshots `ai-settings-test-connection`, `ai-settings-features`, `ai-settings-data` |
| Uploading a sample business case fills a demo form as highlighted drafts with sources; nothing saved until confirmed | **Integration** `Extraction_maps_the_document_to_form_fields_as_drafts_and_saves_nothing`: Word business case → fields mapped and coerced (`1,250,000 AED` → `1250000`), unverified source flagged and capped, the injected closing tag neutralised, e-mail masked, **no item exists afterwards**, audit `ai.extract` (channel `ai`, SHA-256, no text). **Vitest** `SampleItemDialog.test`: Create disabled until each value is accepted or edited, only the extract call made. **E2E** fills the form from a document in both languages, then accepts and saves; screenshot `ai-form-review` |
| With AI switched off, no AI buttons appear and the app works normally | **Integration** `Switched_off_ai_answers_feature_disabled_and_capabilities_hide_every_feature` (404 `ai.featureDisabled` on translate / extract / ask, capabilities all false, items still created; one feature off hides only that feature). **Vitest**: no AI buttons with AI off (Arabic) |
| Claude connects to `/mcp` via OAuth, lists tools, sees only permitted data; calls audited with channel `mcp` | **Integration** `McpServerTests` with the **official MCP C# SDK client**: `Oauth_registration_authorization_with_pkce_and_consent_gives_a_token_for_mcp_only` (discovery, dynamic registration, refused redirect host, sign-in redirect, consent ticket bound to the PKCE challenge, code + verifier → tokens, `/mcp` as the user, OAuth token refused on `/api`, refresh, revoke → 401); `Tools_run_with_the_token_owners_permissions_and_every_call_is_audited` (Alice never sees Bob's item through `search_records` / `get_record` / `record_status`; ≥ 8 audit entries `mcp.tool.call`, channel `mcp`, client `pat:…`). **E2E** consent page in both languages (screenshot `mcp-consent`). **Not ticked:** a live connection from Claude Desktop / Claude Code needs an HTTPS installation reachable by the client; steps in [`docs/mcp-guide.md`](../mcp-guide.md) |

**Also asked for in this phase:**

| Request | Evidence |
|---|---|
| Fake `IChatClient`, no real API calls in CI | `tests/…/AI/FakeAi.cs` (`FakeChatClient` + adapter type `fake`), registered only in tests |
| Extraction maps to fields and is never saved without confirmation | see above; unit `ExtractionPostProcessingTests` (clipping, Arabic-Indic digits, dates, choices, unknown / duplicate fields, snippet locations, confidence cap, schema limits names) |
| Disabled AI hides UI and APIs answer "feature disabled" | see above; `AiRoleAccessTests` (feature removed from the everyone role → 403 and hidden; a narrower role restores it) |
| MCP calls run with the token owner's permissions, 404 semantics kept | `Tools_run_with_…` (`not_found` for out-of-scope records); `Deactivated_users_lose_their_oauth_and_token_access_at_once` |
| Vendor principals refused | `Vendor_principals_are_refused` (MCP 403 `mcp.vendorRefused`, AI 403) |
| Every AI and MCP call audited | `ai.<feature>` events with channel `ai` (extraction test), `ai.tool.call` (Ask test), `mcp.tool.call` incl. refusals (MCP tests), `ai.provider.test`, `mcp.client.register / consent.grant / client.revoke` |
| Usage limits enforced | `Monthly_request_limit_per_user_is_enforced`, `Monthly_token_limit_for_the_installation_is_enforced`; unit `MonthStartTests` (installation time zone, DST, unknown zone) |
| Approve / reject tools not enabled by default | `ToolCatalog` start-up guard; `Tools_run_with_…` (not listed); `Administrators_switch_tools_and_limits_…` (enabled by an admin, still `not_found` for a task that is not the caller's); Settings → MCP shows it off |
| OCR with a scanned Arabic and an English sample (owner request) | **Unit** `OcrTests` with the real engine on committed samples (`tests/Strategy.UnitTests/AI/Samples`: an Arabic page as JPEG, an English page as PNG and TIFF, rendered with the product's fonts and degraded like office scans): the Arabic scan is read in reading order (`الاسم بالعربية: برنامج الأرشيف الرقمي`, `1250000`, no direction marks), the English TIFF line by line; an **image-only PDF** (Arabic JPEG page + English PNG page + one text page) gives `[page 1 scan]`, `[page 2 scan]`, `[page 3]`, and a source quoted from page 1 is `fromScan` while one from page 3 is not; the page limit; OCR off → `ai.extract.ocrOff` / `noText`; a missing engine is reported unavailable without failing. **Integration** `Scanned_documents_are_read_with_ocr_when_it_is_on_and_their_values_are_marked_from_scan` (off: explained, no model call; on: `OcrAvailable`, `scannedPages` 1, warning `ai.extract.fromScan`, field verified and `fromScan` on page 1, `[page 1 scan]` in the prompt). **Vitest**: the "From scan" chip and translated location. **E2E** (ar + en): the administrator switches OCR on in Settings → AI; a user fills the form from a scanned image (Arabic scan / English scan); screenshot `ai-form-review-scan` |
| Approver brief | `Approver_brief_uses_record_access_and_computes_the_changes_from_the_audit_trail` (outsider 404 with no model call; after return + rename, `NameEn` before / after from the audit trail; the return comment reaches the prompt). **E2E** screenshot `approver-brief` |
| Data sharing | `Excluded_fields_and_systems_never_reach_the_model` (excluded `Amount` absent from the prompt; system not allowed → 422 and no call) |
| Similar records and Ask the system | `Similar_records_and_ask_the_system_only_see_what_the_user_may_read` (trigram hits only within the own scope; the model receives only Alice's record via `search_records`; references; approve tool not offered). **E2E** screenshot `ask-the-system` |
| Prompt-injection defences, masking, schema validation, text extraction | unit `PromptTests` (tag look-alikes, nonces, placeholders), `PersonalDataMaskerTests`, `JsonSchemaValidatorTests` (12 cases), `DocumentTextExtractorTests` (Arabic + English Word, PowerPoint with notes, PDF pages, RTL reading order, presentation forms, unsupported / unreadable / empty, kind by content), `RedirectRulesTests`, `McpRateLimiterTests` |
| Every server message key has AR + EN text | **Vitest** `messageKeys.test`: scans the AI and MCP C# sources for `ai.*` / `mcp.*` keys and checks both languages |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet format --verify-no-changes` | clean |
| `dotnet test` | **1 172 / 1 172** after the OCR addition: unit 421 (71 new: schema validator, masking, prompts and injection, extraction post-processing, text extraction with Arabic reading order, **OCR on the Arabic and English scans and image-only PDFs**, month boundaries, redirect rules, rate limiter) · architecture 30 (licences of every new package) · integration 721 on real PostgreSQL (20 new AI / MCP / OCR tests; the generated authorization matrix covers every new endpoint). In the full parallel run the Gotenberg PDF snapshot test failed once at container start-up (`reporting.errors.pdfFailed`), as noted in Phase 4; it passes when re-run |
| `tools/check-vulnerable.sh` · `tools/check-api-client.sh` | no High / Critical · OpenAPI document and TS client up to date |
| `npm run lint` · `format:check` · `typecheck` | clean |
| `npm test` (Vitest) | **81 / 81** (new: AI-filled form review in English with a "From scan" value, AI off in Arabic, Arabic translation draft; AI / MCP message keys in both languages) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (the same 14 moderate dev-tool advisories as Phases 3–4) · 610 packages allowed |
| `npx playwright test` | **68 / 68** (34 scenarios × ar + en; 5 new Phase 5 scenarios: AI and MCP settings with Test connection and OCR, the MCP consent flow, the AI-filled form review, a scanned page read by OCR, the approver brief and Ask) |
| `--migrate` on an empty database | schemas `ai` and `mcp` added (migrations run by every integration test host and the E2E host) |

**OS packages added to the image:** Tesseract 5 with Arabic and English data (Apache-2.0) and Leptonica (BSD-2-Clause), see ADR 0033 for
the shared libraries they bring. **Dependencies added** (all listed in [`THIRD_PARTY_LICENSES.md`](../THIRD_PARTY_LICENSES.md); transitive licences checked by the
architecture tests): Microsoft.Extensions.AI (+ Abstractions, + OpenAI) MIT · OpenAI MIT · **Anthropic** Apache-2.0 ·
ModelContextProtocol.AspNetCore Apache-2.0 · OpenIddict.AspNetCore / EntityFrameworkCore Apache-2.0 · DocumentFormat.OpenXml MIT; PdfPig
(Apache-2.0) moves from tests-only to the product. No npm package added.

## Decisions (ADRs)
- [0028](../decisions/0028-ai-provider-registry-and-settings.md) Provider registry and adapters (Azure through the OpenAI SDK's v1 endpoint),
  settings, per-role access as permissions, gateway order, usage log, monthly limits, demo provider.
- [0029](../decisions/0029-prompts-output-validation-and-injection-defences.md) Versioned prompt templates, in-house schema-subset validator
  with one repair round, prompt-injection defences, data controls and reversible masking.
- [0030](../decisions/0030-document-text-extraction-and-similar-records.md) PDF / Word / PowerPoint extraction with free libraries, Arabic
  reading order; similar records by trigram similarity.
- [0031](../decisions/0031-tool-registry.md) One tool registry for MCP and Ask the system; approve tools never default-enabled.
- [0032](../decisions/0032-mcp-server-and-oauth.md) Stateless MCP server, OpenIddict with Data Protection tokens, dynamic registration,
  consent ticket, resource indicators, offline access, in-memory rate limits.
- [0033](../decisions/0033-ocr-for-scanned-documents.md) Optional OCR: Tesseract bundled in the image and run as a separate process (not a
  sidecar), Arabic + English data, page images taken from scanned PDFs without a rasteriser, "from scan" marking, off by default.
  **Owner confirmation asked:** Tesseract's OS package brings LGPL shared libraries (glib, cairo, pango) into the image, used dynamically by
  the separate `tesseract` program like glibc; `INSTALL_OCR=false` leaves them out.

## Deviations
- **From the SPEC:** none.
- **From the plan / prompt:**
  - Per-role access to AI features is the permission model (one permission per feature) instead of a role list inside Settings → AI
    (ADR 0028); Settings → AI shows each feature's permission and points to Roles.
  - Legacy `.doc` / `.ppt`, embeddings for similar records, and Client ID Metadata Documents for MCP clients are not built (ADRs 0030,
    0032). Scans stored as JBIG2 / CCITT images inside PDFs (some black-and-white scanners) are not decoded for OCR (ADR 0033).
  - The MCP rate limiter keeps its counters in memory (single-instance host).
- **Contracts changed:** `IApprovalEngine.ListAttemptsAsync` (+ `ApprovalAttempt`, `ApprovalDecision`); `AuditEvent.Channel` (and
  `AuditRecord.EventChannel`) so AI and MCP events carry their channel while keeping the acting user; `StrategyClaims.Client`;
  `TokenScopeRules.IsToken` covers OAuth tokens; `AuthSchemes.OAuth` (Identity forwards non-`sst_` bearers to it).
- **Platform administrators** now also receive the `ai.administrator` and `mcp.administrator` roles (first administrator, dev seed).
  Existing installations assign them in Settings → Assignments. Every user now holds the seeded *AI user* role (assigned to everyone).
- **CLAUDE.md:** schema list includes `mcp`.
- **Development seed:** the offline demo provider is configured and AI is switched on; the MCP server is on.

## Fixes found along the way
- Masking restored placeholders in nested JSON by re-parenting nodes (500 on extraction): strings are now replaced in place.
- Server validation messages of the new endpoints used keys that had no translation; they now use the existing `validation.*` keys, and a
  Vitest scans the AI / MCP sources for untranslated keys.
- OpenIddict rejected the RFC 8707 `resource` parameter that MCP clients send; the authorization endpoint now validates it against the
  installation's own `/mcp`. Refresh tokens needed `offline_access`, now granted with `mcp`.
- Revoking a client could hit an OpenIddict concurrency error; deleting the registration (with its authorizations and tokens) is enough
  now that validation checks token entries.

## Known gaps (by design: later phases or follow-ups)
- Modules from Phase 6 on add their own AI forms, record / search sources and MCP tools (projects, KPIs, risks…).
- A live end-to-end check with Claude Desktop / claude.ai / Claude Code against an HTTPS installation is for the owner's acceptance
  ([`docs/mcp-guide.md`](../mcp-guide.md)); real provider keys were not used in this session.
- No embeddings; names inside free text are not detected for masking (only the record's people); OCR reads PNG / JPEG / TIFF and the
  JPEG / decodable page images of scanned PDFs, not JBIG2 / CCITT ones.
- The setup wizard's "AI provider (optional)" step and the admin health page's AI check come with Phase 18.

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml --profile dev up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host          # Development: migrations, seeders, demo seed (demo AI provider, MCP on) on an empty database
cd web && npm ci && npm run dev                       # http://localhost:5173
```
1. **Mariam** (`mariam@strategy.local` / `Strategy#Demo2026`, MFA): Settings → **Artificial intelligence**: the master switch, the *Demo
   provider (offline)* → **Test connection**. Add an Anthropic / OpenAI / Azure / Ollama provider with a real key to try a real model; make
   it the default or route one feature to it in *Features*. *Data sharing and limits*: exclude `Amount` for the Sample system, set a
   monthly limit, switch **text recognition (OCR)** on (the card says whether Tesseract is installed). *Usage* shows every call.
2. **Ahmed** (`ahmed@strategy.local`): Sample module → **New item** → **Fill from a document** → upload a `.docx` / `.pdf` / `.pptx` /
   `.txt` with lines such as `Name (English): Digital archive`, `Amount (AED): 1,250,000` (the demo provider reads "Label: value" lines; a
   real model reads free text) — or a **scan** (`tests/Strategy.UnitTests/AI/Samples/scan-ar.jpg`, or a scanned PDF) with OCR on: the
   values then carry **"From scan"**. The fields fill as **AI drafts** with confidence and source; **Create** stays disabled until you accept or
   edit them. Try **Translate from English** under the Arabic name, and watch **Similar items** while typing a name.
3. Ahmed submits the item; **Mariam** opens it → *Approvals* → **Generate brief**. Return it with a comment, let Ahmed rename and resubmit,
   generate again: the brief lists the changed field (before / after) from the history.
4. **Ask** (top bar): "Which items mention archive?" — the answer cites only records you may see. Settings → AI → *Usage* and the Audit Log
   (channel `ai`) show the calls.
5. **MCP**: Settings → **MCP server** shows the address and the tools (approve off). Profile → Access tokens → new token with
   *AI assistants (MCP)*; then
   `claude mcp add --transport http strategy http://localhost:5080/mcp --header "Authorization: Bearer sst_…"` and ask Claude Code
   "what approvals are waiting for me?". Or add it without the header and authenticate through `/mcp` in Claude Code (consent page).
   Audit Log, channel `mcp`, lists each call. Switching AI off in Settings removes every AI button; the application works as before.

## Cloud environment notes
- Tesseract installed with `apt-get install tesseract-ocr tesseract-ocr-ara tesseract-ocr-eng` (5.3.4) for the OCR tests and the E2E host.
- Docker is started manually (`dockerd`); images through `mirror.gcr.io/`. Integration tests reuse one PostgreSQL with
  `STRATEGY_TEST_POSTGRES`; E2E ran against a fresh database `strategy_e2e` with `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium`.
- Screenshots (generated by the E2E suite with `E2E_SCREENSHOTS=phase-5`) are in [`docs/screenshots/phase-5/`](../screenshots/phase-5/) as
  `ar-*` / `en-*` pairs: `ai-settings-test-connection`, `ai-settings-features`, `ai-settings-data` (with the OCR card), `ai-form-review`, `ai-form-review-scan`, `approver-brief`,
  `ask-the-system`, `mcp-settings`, `mcp-consent`. Earlier phases' screenshots were not retaken (the full suite ran into a scratch folder).
