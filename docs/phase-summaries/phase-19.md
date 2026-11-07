# Phase 19 — Acceptance fixes, external references, Members & Responsibilities: summary

Status: **built — awaiting owner acceptance** · version stays **1.0.0** (no tag until the end of Phase 23, SPEC-ADDENDUM A7).
Requirements: [`docs/SPEC-ADDENDUM.md`](../SPEC-ADDENDUM.md) §A, §B, §D · plan: [`docs/design/phase-19-plan.md`](../design/phase-19-plan.md).
ADRs **0160–0169 proposed** for owner acceptance.

## Housekeeping (A7)
- `docs/SPEC-ADDENDUM.md` and `docs/BUILD_PLAN-PART2.md` added to the docs index (README) and CLAUDE.md §1 as part of version 1.0; the
  superseded 1.1 drafts (`SPEC-1.1.md`, `BUILD_PLAN-1.1.md`) removed.
- No `v1.0.0` tag existed (local or remote) — nothing to remove. CHANGELOG 1.0.0 marked *unreleased* until Phase 23.
- `BUILD_PLAN.md` progress table: Phases 0–18 accepted by the owner (7 Nov 2026), rows 2 and 3 ticked.

## What was built

| Item | Result | Design / ADR |
|---|---|---|
| **A1 Global search** | New `Strategy.Platform.Search` (+ Contracts, schema `search`). One `ISearchContributor` per module covering all 22 linkable types, each scoped **in its own database query**; every hit then resolved through the Links registry and only *visible* records kept — out-of-scope records are neither shown nor counted (404 semantics). Stored generated `quick_search` columns (SQL twin of `SearchText.Normalise`) + trigram indexes in 9 modules. Ranking: exact number > number prefix > title > key field. Top-bar search (`/`, Ctrl/⌘+K), grouped by system, ARIA combobox with ↑ ↓ Home End Enter Esc, recent items (server-side, re-checked on read), 360 px overlay; reusable `command-search` component for the Phase 20 command palette; MCP read tool `search` | `search.md`, 0160 |
| **A2 Rich text** | Kernel `RichText` (HtmlSanitizer allow-list: no scripts / `on*` / `style`, http(s) / mailto links with forced `rel`, `dir` on blocks), `ToPlainText` projections for AI / MCP record reads, PDFs and search; Tiptap editor (bold, italic, lists, headings, links, tables, per-paragraph RTL / LTR, accessible toolbar, lazy chunk) + `RichTextView` (DOMPurify, same allow-list). Applied to the SPEC's rich-text fields: project meeting notes, request description / justifications, project description, team meeting agenda / minutes / decisions, Request Framework `richtext` fields (previously unsanitised). Existing plain-text values are **not rewritten** and render as text paragraphs | `rich-text.md`, 0161 |
| **A3 Security e-mails** | Eight mandatory bilingual templates (password changed / reset, MFA enrolled / removed / recovery codes, account locked, personal access token created, optional new-device sign-in — off by default, hashed device fingerprints). Sent **exactly once** (idempotency key recorded with the outbox message, `notify.sent_request`), subject and `lang` in the recipient's language first (all notification e-mails), never a secret or link; locked on in preferences; Wasel vendors included | `identity.md` §, 0162 |
| **A4–A6 Windows / ZIP / slow networks** | One `Invoke-Native` helper (native stderr never an error in 5.1), UTF-8 without BOM everywhere, ASCII-only scripts, `.git` optional (failed `rev-parse` = no revision, release unpacked inside another repository ignored), npm / NuGet / apt retries and timeouts as build args. CI jobs `deploy-windows` (Windows PowerShell 5.1 **and** 7 on `windows-latest`, fake docker, release folder without `.git` and with a non-ASCII path) and `deploy-scripts` (bash, PowerShell 7, PSScriptAnalyzer). Owner checklist for a real Windows + Docker Desktop run: `docs/operations/windows-acceptance.md` | `release.md` §14, 0163 |
| **A8 KPI picker** | `GET /api/v1/kpi/cards/lookup` (scoped, Arabic-normalised, ≤ 50, `ids=` for the selected label) and one shared `KpiCardPicker` (search as you type) in Risk KRIs, Portfolio project KPIs, Benchmarking and Audit Management | `kpi.md` §, 0169 |
| **A8 Take over as creator** | *Reassign open tasks of an employee* → move the **creator** of a leaver's open tasks (preview, select, new creator, reason): record role re-granted, waiting approvals follow the new creator, audit with reason, both people notified; a task already assigned to the new creator is skipped | `tasks.md` §, 0169 |
| **B External references** | New `Strategy.Platform.Integration` (+ Contracts, schema `integration`): Settings → Integrations → **Source systems** (type list seeded: Salesforce, SAP, Oracle, Microsoft Dynamics, Custom); `integration.external_ref` **unique** on (source, type, external ID) and on (source, type, record) — validation errors with message keys, the unique index mapped to the same error under concurrency; references commit with the record. Record pages: **External references** section and **origin badge** (20 record pages + units). **Upsert by external ID** in every import (units, employees, holidays, audit findings) with validation report; `GET …/by-external/{source}/{externalId}` for 19 types and `PUT` (create or update through the module's own commands, advisory lock) for 9 (list and reasons in ADR 0165); MCP read tool `find_by_external_reference`; report-builder columns `externalSource` / `externalId`; audited entity; **global search finds records by external ID** (cross-system contributor, every hit re-checked) | `external-references.md`, 0164, 0165 |
| **D1 Members** | System → Settings → **Members** for all 12 systems over the existing model (system access + role assignments, principals person / unit incl. sub-units / everyone; nothing in the access rules changed): Administrators, Users, extra roles; per-system defaults declared by each module, overridable by the platform administrator (`access.system_member_roles`); add / remove with dates and reason; audited and notified. **Delegated administration**: `<system>.members.manage` (only that system; never platform roles, role permissions or settings). The permission matrix is now **"Advanced permissions"** | `members-responsibilities.md` §1, 0166, 0167 |
| **D2 Responsibilities** | System → Settings → **Responsibilities** for KPI (card person / unit), Risk (risk / action / control owner), Portfolio (PM / owner **through submitted Ownership change requests**, story assignee), CAP (remark owner, action assignee), Audit (report lead auditor / finding auditor — Audit Office only), Benchmarking (coordinator), Teams (leader, evaluator), Tasks (open tasks): filters, select, preview (allowed / blocked with reason), reassign with reason; only selected and in-scope records move; stale versions skipped; old and new person notified; audit per field + summary event; **pending approvals re-routed** (`IApprovalRerouting`). `<system>.responsibilities.manage` per system | `members-responsibilities.md` §2, 0168 |

## Requested tests

| Test | Evidence |
|---|---|
| Search never returns out-of-scope records (incl. by external ID) | `Search/GlobalSearchTests` (12: number, title, key field, not counted, no system access, switched-off system, Arabic normalisation, recent items revoked, vendor, validation); `ExternalReferenceTests.Global_search_finds_a_record_by_external_id_only_for_users_who_may_read_it` |
| Rich text rejects scripts and unsafe attributes | `Kernel/RichTextTests` (35-payload XSS corpus, attribute rules, plain-text projection, legacy detection); `Teams/TeamsRichTextTests`, `Portfolio/RichTextPortfolioTests` (endpoints store sanitised HTML, legacy values unchanged); Vitest `ui/rich-text.test.tsx` (DOMPurify) |
| Each security e-mail sent once in the user's language | `Identity/SecurityEmailTests` (12: each event, Arabic / English recipient, no secrets, notifier and outbox run twice → one e-mail, vendor, preferences locked, new-device switch); Mailpit delivery; E2E `security-emails.spec.ts` |
| external_ref uniqueness and upsert (import, API) create no duplicates | `Integration/ExternalReferenceTests` (both rules, concurrent writer on the unique index, unit / employee / holiday / finding imports re-run → updated, REST create then update, out-of-scope 404, missing permission 403, vendor refused, MCP visible only); `ExternalImportTests` (unit) |
| A system administrator cannot manage another system's Members or platform settings | `Access/MembersTests` (Portfolio delegate: 403 on KPI Members, settings, role permissions, member-role configuration, platform roles; unit with sub-unit and everyone; removal ends access; notification once; audit) |
| Bulk reassignment moves only the selected records and notifies both people | `Access/*ResponsibilityTests` (KPI, Risk, Portfolio, CAP, Audit, Benchmarking, Teams, Tasks: only selected records, audit with reason, old and new person notified once, approvals re-routed, Portfolio via change request, Audit Office only, Risk admin 403 on KPI, out-of-scope not moved, stale skipped) |
| `.ps1` scripts under Windows PowerShell 5.1 and from a folder without `.git` | CI `deploy-windows` (5.1 + 7) and `deploy-scripts` (bash + PowerShell 7 + PSScriptAnalyzer), 72 assertions each; manual run on real Windows with Docker Desktop: `docs/operations/windows-acceptance.md` (owner) |

## Defects found while verifying (fixed in this phase)
- **Windows: wrong docker for binary pipes.** `Invoke-ComposeBinary` started a bare `docker`; Windows `CreateProcess` searches System32
  before `PATH`, so another `docker.exe` there (GitHub's runner image has one) answered `pg_dump` / `tar` instead of the one PowerShell runs.
  Now the docker PowerShell resolves is used.
- **Windows PowerShell 5.1: restore input changed.** Closing `Process.StandardInput` (a `StreamWriter`) flushes the encoding preamble on
  .NET Framework — a BOM after the dump / archive with a UTF-8 console. The raw pipe is now written and closed.
- **Arabic plurals.** Five Arabic messages (Responsibilities selection / confirm, holiday import, business hours) had only `one` / `other`;
  i18next fell back to English for 2 or more ("2 records selected" on the Arabic page). All six forms added and a test now requires them.
- **Sample editors were Sample administrators.** The template module's member defaults made its `editor` role the administrator list, so
  every Sample editor held `sample.members.manage` and saw Settings. Sample now has no administrator list.
- ESLint ran out of memory on the CI runner (heap raised in the `lint` script); IDE0005 with the CI SDK; a missing `validation.maximumItems`
  translation; the authorization matrix's everyone list lacked the new source-system read permission.

## Quality gates
<!-- filled in after the final runs -->

## Deviations
- **From the SPEC / addendum:** none known.
- **From the plan:** PUT by external ID is offered for the 9 record types that have create + update commands; the others (records created
  only through a workflow wizard, e.g. Portfolio requests) offer GET only — listed with reasons in ADR 0165. Units / employees are
  referenceable but not in global search (they are not linkable types). Reference changes appear in the global Audit Log
  (`integration.external_ref`), not on the record's own History tab.

## Known gaps
- The run of the `.ps1` scripts on a **real Windows machine with Docker Desktop** is an owner acceptance step (CI uses a fake docker,
  because GitHub's Windows runners only run Windows containers).
- Search does not index people or units; free-text rich-text fields are not searched.
- On system settings pages the Members / Advanced permissions bar and the Responsibilities link are two separate elements (Phase 20 reworks
  these pages per theme).
- Assignment groups keep their original creator for group actions after *take over as creator* (each task's approval moves) — ADR 0169.

## How to demo
1. Top bar → type a project number, part of a title, or an external ID → ↑ ↓ Enter; empty box shows recent items.
2. Portfolio → new request → format the description (bold, list, RTL / LTR paragraph) → save → formatted view.
3. Profile → change password → Mailpit shows the security e-mail in the user's language.
4. Settings → Integrations → Source systems → add "SF"; Organisation → Import employees with *Source system* / *External ID* columns →
   re-import shows *updated*; open the employee's unit / a record → External references and origin badge.
5. Portfolio → Settings → **Members** (add a person, a unit, all employees); KPI → Settings → **Responsibilities** (filter, select two cards,
   preview, reason, reassign).

## Screenshots
[`docs/screenshots/phase-19/`](../screenshots/phase-19/) (Arabic and English): global search (results, recent, mobile), rich-text editor and
view, source systems and import with external IDs, external references on a record, Members page (add / remove / mobile),
Responsibilities (list, preview, result, mobile), security e-mail and security settings, KPI picker search, take over as creator.
