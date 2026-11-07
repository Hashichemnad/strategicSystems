# Phase 19 — plan (acceptance fixes, external references, Members & Responsibilities)

Requirements: `docs/SPEC-ADDENDUM.md` §A, §B, §D (wins over SPEC.md where they differ) · prompt: `docs/BUILD_PLAN-PART2.md` "Phase 19".
Known gaps closed here: `docs/release-notes/known-gaps-review.md` (no global search, rich text as plain text, KPI picker 200 entries, no
"take over as creator", no e-mail on password / MFA change, Windows scripts only run under PowerShell 7 on Linux).

Each work item gets its own design note (written first) and ADR(s). The work is built in parallel **streams**, merged one by one into
`claude/peaceful-archimedes-x2jl4p` (as in Phase 18).

| Stream | Scope | Design note | ADRs |
|---|---|---|---|
| **S — Global search** (A1) | New `Strategy.Platform.Search` (+ `.Contracts`): `ISearchContributor` per module (scoped in the database query), results always resolved through `ILinkRegistry` (only *visible* badges are returned → out-of-scope records never appear, 404 semantics); Arabic normalisation (`SearchText`); grouped by system; recent items (server-side, re-checked on read); top-bar search box with keyboard navigation (↑ ↓ Enter Esc, `/` and Ctrl+K focus) | `docs/design/search.md` | 0160 |
| **R — Rich text** (A2) | Tiptap editor component in `/web/src/ui` (bold, italic, lists, headings, links, tables, per-paragraph RTL / LTR); server allow-list sanitiser in the Kernel (`RichText.Sanitize`, `RichText.ToPlainText`); DOMPurify on render; plain-text projections for exports / e-mails / search / AI; the SPEC's rich-text fields switched to the editor; existing plain-text values stay unchanged and render as text paragraphs | `docs/design/rich-text.md` | 0161 |
| **M — Security e-mails + A8** | Bilingual security notifications (password changed / reset, MFA enrolled / removed / recovery codes regenerated, account locked, personal access token created, optional new device / IP sign-in) sent once in the user's language; KPI picker search-as-you-type everywhere; Tasks "take over as creator" for a leaver's tasks | `docs/design/identity.md` (§ added), `docs/design/tasks.md` (§ added) | 0162, 0169 |
| **O — Windows / ZIP / slow networks** (A4–A6) | `.ps1` scripts under Windows PowerShell 5.1 and 7 (native stderr of `docker compose` not an error), install / upgrade from a folder without `.git`, npm / NuGet retries and timeouts in the Docker build; CI job on `windows-latest`; Pester / bats-style tests | `docs/design/release.md` (§ added) | 0163 |
| **X — External references** (B) | New `Strategy.Platform.Integration` (+ `.Contracts`, schema `integration`): source systems (Settings → Integrations → Source systems), `integration.external_ref` with both uniqueness rules, record-page "External references" section, origin badge, upsert by external ID in every Excel import (Organisation units / employees, holidays, audit findings) with a validation report, `GET/PUT …/by-external/{source}/{externalId}`, MCP read tool, exports / reports / audit; global-search contributor for external IDs | `docs/design/external-references.md` | 0164, 0165 |
| **D1 — Members** (D1) | System → Settings → Members over the existing access model (system access grants + role assignments with principal kinds user / unit incl. sub-units / everyone; no rule changes); per-system member roles (administrators, users, optional extra roles) as configuration; delegated administration (`<system>.members.manage`, only that system) | `docs/design/members-responsibilities.md` §1 | 0166, 0167 |
| **D2 — Responsibilities** (D2) | System → Settings → Responsibilities: `IResponsibilitySource` per module (KPI, Risk, Portfolio, CAP, Audit, Benchmarking, Teams, Tasks), filters, preview, bulk reassignment with reason, notification to the old and the new person, audit entries, re-routing of open approvals / tasks through each module's own commands (Portfolio PM / owner through the change-request rules) | `docs/design/members-responsibilities.md` §2 | 0168 |

## Shared contracts (fixed before the streams start)

- **Search** (`Strategy.Platform.Search.Contracts`):
  `ISearchContributor.SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken)` → `IReadOnlyList<SearchHit>`;
  `SearchQuery(string Text, string Normalised, int Limit)`; `SearchHit(string ResourceType, Guid Id, double Score, string? MatchedOn, string? MatchedText)`.
  `ResourceType` is a linkable type of the Links registry; the search service resolves hits through `ILinkRegistry.ResolveAsync` and drops
  everything that is not *visible*. The external-reference contributor (stream X) returns hits of any type found by external ID.
- **Responsibilities** (`Strategy.Platform.Access.Contracts`, stream D2): one source per (system, responsibility kind); list / preview / reassign
  run as the acting user, inside the owning module.
- ADR numbers: 0160–0169 (Phase 20 starts at 0170).

## Order of merging
O → R → M → S → X (+ external-ID search contributor) → D1 → D2, then: OpenAPI + TS client regenerated once, full quality gates (CLAUDE.md §16),
E2E in `ar` and `en`, screenshots in `docs/screenshots/phase-19/`, `docs/phase-summaries/phase-19.md`, known-gaps review and release-notes
*Known limitations* updated, BUILD_PLAN-PART2 progress row 19 ticked.
