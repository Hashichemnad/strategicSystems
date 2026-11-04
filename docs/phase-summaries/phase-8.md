# Phase 8 — Portfolio Management A: Program Requests: summary

**Status:** built and verified in the build session · awaiting owner acceptance
**Branch:** `claude/vigilant-edison-4sumu4`
**Planning:** the plan is the design note [`docs/design/portfolio.md`](../design/portfolio.md) (whole Portfolio module, Phase 8 detailed),
written and committed before the code (commit `docs(portfolio): Phase 8 design note…`). As in Phases 5–7 the session ran without an
interactive approval step, so the plan was committed first and implemented as written; its open choices are ADRs 0047–0051.

## What was built

| Area | Result |
|---|---|
| **Module** (`Strategy.Modules.Portfolio` + `.Contracts`, schema `portfolio`) | `request` (Program Request `PR-XXXX`, every SPEC §1.4 A field), `proposal_assessment` (versioned, append-only), `project` (Project / Initiative record, `PROJ-…`). System `portfolio` is a **business system** (`SystemKind.Module`, enabled by `Modules:Portfolio:Enabled`, default on; system access granted in Settings → Systems). `IPortfolioDirectory` in Contracts |
| Configuration (Settings → Portfolio) | Threshold (AED 50 000), **configurable prioritisation model** (the four ADoF criteria with weights, bands High ≥ 80 % / Medium ≥ 60 % / Low), start / end date windows (3 years), the approval step that sets the approved budget, the **Project Scope template**, "write the assessment on creation". Lookups `portfolio.strategic-objective` (**seeded empty**, Q-P7), sources, program types, delivery options (`other` requires text, ADR 0047). Numbering `PR-{SEQ:4}` and `PROJ-{UNIT}-{YYYY}-{SEQ:3}` |
| Calculators (pure, table-tested) | `ClassificationCalculator` (Q-P5: all six screening answers Yes **and** budget > threshold → Project; PM Office override with a reason), `PriorityCalculator` (Q-P6: Σ(score × weight) ÷ Σweight ÷ 5, bands) |
| Validation (`RequestRules`, ADR 0047) | Format rules on every save (lengths, ≥ 0 with **2 decimals**, **4-digit years**, share 0–1, scores 1–5, active codes, **"Other" needs text**, end ≥ start); at submission **Year 1–4 = Estimated Budget exactly**, **start in the current year or the next three**, **end ≤ current year + 3**, **Operation or Service**, **previous program name for continuous programs**, value + year when a return / reduction is expected, all screening and priority answers, the cost path complete. All failures returned together with field paths and AR / EN keys |
| Lifecycle (`RequestLifecycle`) | Draft → In approval → Approved / Rejected / Cancelled; **return for correction** back to Draft (approval status *returned*); cancel with a reason (own draft; PM Office in approval cancels the instance too). Step in progress kept on the request (code + AR / EN name from the engine) |
| Approval process `portfolio.request` (ADR 0048) | Seeded **Section Manager** (`sectionManager != null and != departmentDirector and != submitter`, Q-P4) → **Department Director** → **PM Office** (role) → **DG Office** (`requiresBudget and estimatedBudget > threshold`) → **Budget Section** (`requiresBudget`); variables `requiresBudget`, `estimatedBudget`, `threshold`, `classification`, `evaluationScore`; editable in the Approval Settings centre. The budget step's approver sets the **Approved Budget** (default: the estimated budget) |
| Cost estimation vs project scope; Administration hand-off (ADR 0051) | Cost-estimation file, or project scope (template download) → *Send to Administration* → record role `procurement` for the holders of `portfolio.administration`, notified → quotations uploaded and confirmed → requester notified. Files through the Files service (purposes `portfolio.request-document / cost-estimation / project-scope / quotation`) |
| Automatic Project / Initiative (ADR 0049) | On final approval, in the engine's transaction: `PROJ-{UNIT}-{YYYY}-NNN` (`{UNIT}` = owning unit of the Portfolio system, else the request unit), request data copied, budget = approved budget, owner = department director of the unit, stage **Initiation** (project) / **Planning** (initiative, Q-P2), record roles owner / requester, requester notified. The lifecycle after creation is Phase 9 |
| Current status, My Work, notifications | Request messages of SPEC §1.6 (upload cost estimation / project scope, send to Administration, waiting quotations, submit, resubmit after corrections, waiting ⟨step⟩ with approvers and their actions, approved — project created, rejected, cancelled); project banner "waiting for someone in the section to take ownership". My Work: returned requests, quotations ready, quotations to upload. Templates `portfolio.scopeSubmitted`, `.quotationsUploaded`, `.requestApproved`, `.requestCancelled` (engine templates for approvers and outcomes) |
| AI (drafts only) | **Start from a document**: shared extraction with form `portfolio.request`; **`IAiFormChoiceSource`** (new, additive) supplies the configured objectives / sources / types / options as choices and one score field per configured criterion (ADR 0050); several documents merged in the browser (most confident wins), highlighted drafts with confidence and source, missing fields listed, **create disabled until every value is reviewed**. **Proposal Assessment** (feature `portfolio-assessment`): summary, gaps, strengths, risks, strategic alignment (configured codes only), suggested classification and priority (scores clamped, score / band recomputed), **similar requests / projects computed by the server** within the caller's scope, recommendations; **versioned** (regenerate = new version, old kept), read by everyone who may read the request. Record source (approver brief, Ask) and search sources for requests and projects. The offline demo provider answers the assessment prompt |
| MCP (ADR 0031) | `portfolio_search_requests`, `portfolio_my_requests`, `portfolio_get_request`, `portfolio_get_project` (read, on), `portfolio_create_draft_request` (write, **off**, draft only) |
| Links | Requests are **task-able** (tasks linked from the request's Tasks tab; editors follow them) |
| Web (AR + EN, RTL / LTR) | **Program requests** list (views, status / classification / priority filters, search), **New request** (fill manually / start from a document), **six-step wizard** (save as draft at each step; live Year 1–4 total vs budget, live classification and score), **record page** (header chips, stepper Draft › Manager › PM Office › [DG Office] › [Budget] › decision, current-status banner with actions, tabs Overview · Proposal Assessment · Approvals (shared panel + approver brief) · Cost files · Documents · Tasks · History; dialogs submit, cancel, classification override, approved budget, uploads), **Projects** list and page, **Settings → Portfolio**. Sidebar entries for the system |

## Acceptance checks (BUILD_PLAN Phase 8)

| Check | Evidence |
|---|---|
| Three demo requests (no budget / small / large) follow the three approval routes; final approval creates the Project or Initiative | Integration `The_three_approval_routes_follow_budget_and_threshold…` (no budget: director → PM Office, an **Initiative** at Planning; 30 000 from a section: **section manager** → director → PM Office → **Budget**; 250 000 all Yes: director → PM Office → **DG Office** → **Budget** with approved budget 240 000 set by the budget approver only; **Project** `PROJ-DEP-…-YYYY-002`, Initiation, data copied, owner = director, requester notified); `Owning_unit_code_numbers_projects` (`PROJ-SEC-…-YYYY-001`). E2E (ar + en) runs the three routes: `approval-route-large-at-budget`, `approval-route-small`, `approval-route-no-budget`, `request-approved`, `project-created` |
| Year 1–4 costs not equal to budget blocks submission with a clear AR / EN message | Integration `Submission_applies_every_request_rule_with_clear_keys` (`portfolio.errors.yearCostsMismatch` with total 100001.00 / budget 100000.00, request stays Draft); unit `RequestRulesTests`; Vitest translates it in both languages; E2E screenshot `request-submit-blocked` and the wizard's live check `request-wizard-budget` |
| Upload a sample case study → form pre-filled, gaps listed, Proposal Assessment visible to approvers | Integration `A_case_study_fills_the_request_form_as_drafts…` (configured objective accepted, unknown code dropped, injected closing tag inside the document, **no request created**) and `Proposal_assessment_is_versioned_visible_to_approvers…` (v1 / v2 kept, invented objective dropped, similar request found, documents read; director (approver) and department viewer read it, outsiders 404; approver brief carries the latest summary). E2E `start-from-document`, `start-from-document-review` (17 drafts, missing fields, save disabled, nothing saved), `proposal-assessment`, `proposal-assessment-v2` |

**Also asked for in the prompt:**

| Request | Evidence |
|---|---|
| Every validation rule in §1.4 A | Unit `RequestRulesTests` (boundaries: previous year / year + 3 / year + 4, Dec 31 / Jan 1, 999 / 1000 / 9999 / 10000, 100.005, `other`, continuous, operation / service, cost path) + integration |
| Classification and evaluation-score tables | Unit `ClassificationCalculatorTests`, `PriorityCalculatorTests` (boundaries 80 % / 60 %, weights, inactive criteria); Vitest mirrors |
| Return-for-correction loop | Integration `Return_for_correction_loops_back…` (draft + returned, banner *resubmit*, My Work item, edit, resubmit = attempt 2, approved); E2E `request-returned`, `request-resubmitted-approved` |
| Project created with correct number and data | see above |
| AI-filled request never saved without confirmation | Integration (count unchanged); Vitest (`Wizard` cannot finish / save while values wait for review); E2E (API count 0 before confirming) |
| Requester sees only own, department viewer their unit, PM Office all | Integration `Requester_sees_only_own_requests…` (list and 404s, 403 for visible-not-editable, approver sees through the engine's record role) |
| Cancel with reason, reject final, PM Office override | Integration `Reject_is_final_and_cancel_needs_a_reason`, `Pm_office_overrides_the_classification_with_a_reason` |
| Administration quotation hand-off | Integration `Project_scope_goes_to_administration…` (invisible before, record role after, notifications both ways, submission waits for quotations) |
| Task-able, MCP | Integration `Requests_are_task_able_and_mcp_tools_follow_the_rules` |
| Lifecycle table | Unit `RequestLifecycleTests` (5 statuses × 8 actions) |

## Quality gates run

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` · `dotnet format --verify-no-changes` | 0 warnings, 0 errors · clean |
| `dotnet test --solution Strategy.slnx` | **2 141 / 2 141** (unit, architecture, integration on real PostgreSQL). New: **55** Portfolio unit tests, **12** Portfolio integration tests (9 API + 3 AI) |
| `tools/check-api-client.sh` · `tools/check-vulnerable.sh` | OpenAPI document and TS client up to date · no High / Critical |
| `npm run lint` · `format:check` · `typecheck` · `npm test` | clean · **108 / 108** Vitest (13 new: Portfolio message keys in AR / EN, wizard calculations, document merge, finish blocked while AI values wait) |
| `npm run build` · `npm audit --audit-level=high` · `npm run lint:licenses` | OK · no High / Critical (the same 14 moderate dev-tool advisories as earlier phases) · 613 packages allowed |
| `npx playwright test` | **82 / 82**, the whole suite in Arabic and English on a fresh database (Phase 8: 2 scenarios × ar + en, screenshots with `E2E_SCREENSHOTS=phase-8`) |

Existing tests updated for Phase 8 facts: the effective roles of a new user include `portfolio.requester` (the everyone role); the
authorization matrix sends a multipart body to the two Portfolio upload routes (as it already did for photo / import / logo uploads); the
MCP administration test now switches MCP on itself instead of relying on the order of the class's tests (it failed when the order
changed). The E2E caught one real defect, fixed: editing a draft through the API without a unit cleared the requesting unit.

**Dependencies added:** none (NuGet or npm).

## Decisions (ADRs)
All five were **accepted by the owner** as proposed before the pull request.
- [0047](../decisions/0047-program-request-validation.md) Format rules on every save, business rules at submission; the `other` code convention.
- [0048](../decisions/0048-request-approval-chain-and-approved-budget.md) Seeded chain and conditions; one *in-approval* status with the step's code and name; approved budget set by the budget step's approver (default: estimated).
- [0049](../decisions/0049-project-auto-creation-and-numbering.md) Project creation in the approval transaction; `PROJ-{UNIT}-{YYYY}-{SEQ:3}` with the Portfolio system's owning unit.
- [0050](../decisions/0050-proposal-assessment-and-dynamic-ai-choices.md) `IAiFormChoiceSource`; several documents merged in the browser; assessment versions; similar records computed without the model.
- [0051](../decisions/0051-administration-quotation-hand-off.md) Hand-off by record role to the Administration role holders.

ADR numbers 0047–0051 were taken in this session; the KPI phase (built in parallel) may need to renumber its own on merge.

## Deviations
- **From the SPEC:** none known. Interpretations are in the ADRs.
- **From the plan / CLAUDE.md:**
  - Cost files are uploaded from the request page (Cost files tab / banner), not inside the wizard: the request must exist first.
  - The Project page is a minimal record view (stage path, banner, data); Take Ownership and the lifecycle are Phase 9.
  - `portfolio.project` is not yet a linkable type: the Teams tests register a fake type with that name until Phase 9 wires formation.
- **Platform changes (additive):** `IAiFormChoiceSource` (AI.Contracts) + merge in the extraction service; `Wizard.finishDisabled` (UI kit); audit `AuditedRecord` `request`; demo AI provider answers the assessment; `Modules:Portfolio:Enabled` (appsettings, compose, `.env.example`, test host); development seed: Portfolio system access for everyone, Mariam holds the PM Office, DG Office, Budget Section, Administration and administrator roles, three demo strategic objectives.

## Known gaps (by design: later phases or follow-ups)
- Project lifecycle, `LinkedRecordClosed` publication and project teams: Phase 9. Portfolio dashboards and exports: Phases 10 / 17.
- Strategic objectives are a Portfolio lookup; when the KPI system (Phase 12) owns an objectives catalogue, the lookup can be fed from it (codes are stored, so no migration of requests is needed).

## How to demo
```bash
REGISTRY_PREFIX=mirror.gcr.io/ docker compose -f deploy/docker-compose.yml up -d postgres gotenberg mailpit
dotnet run --project src/Host/Strategy.Host
cd web && npm ci && npm run dev        # http://localhost:5173
```
1. **Ahmed** (`ahmed@strategy.local` / `Strategy#Demo2026`): Program requests → *New program request* → *Fill manually* (or *Start from a
   document* with a business case; review / accept the highlighted values) → save; on the request page upload the cost estimation file →
   *Submit for approval* (a Year 1–4 total different from the budget is refused with the message).
2. **Mariam** (MFA; department director, PM Office, DG Office, Budget Section in the demo): approve from My Work or the Approvals tab; at the
   Budget step *Set approved budget*. Budget > AED 50 000 goes through DG Office; no budget stops after PM Office.
3. The request shows "Approved — PROJ-… created"; open the project. Return a request to see the correction loop.
4. Settings → Portfolio: threshold, criteria and weights, bands, the project scope template.

## Cloud environment notes
- PostgreSQL 16 of the container (`max_connections = 600`) through `STRATEGY_TEST_POSTGRES`; Docker started manually for Gotenberg and
  Mailpit (`mirror.gcr.io/`); Tesseract installed for the OCR tests; E2E with `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium-1243/chrome-linux64/chrome`
  on a fresh `strategy` database.
- Screenshots (`E2E_SCREENSHOTS=phase-8`) in [`docs/screenshots/phase-8/`](../screenshots/phase-8/) as `ar-*` / `en-*` pairs:
  `request-start`, `request-wizard`, `request-wizard-budget`, `request-wizard-screening`, `request-submit-blocked`,
  `approval-route-large-at-budget`, `approval-route-small`, `approval-route-no-budget`, `request-returned`, `request-resubmitted-approved`,
  `request-approved`, `project-created`, `requests-list`, `start-from-document`, `start-from-document-review`, `proposal-assessment`,
  `proposal-assessment-v2`.
