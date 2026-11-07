# Design note — Phase 19 stream S: Global search (`Strategy.Platform.Search`)

**Scope.** SPEC-ADDENDUM §A1 (wins over SPEC.md): a search box in the top bar (focus with `/` or Ctrl+K; Phase 20 reuses the same
component as the Modern theme's command palette) that searches every system the user can access — records by number, title, external ID
(§B, contributed by stream X) and key fields; results grouped by system; permission-filtered with 404 semantics; Arabic-normalised;
keyboard navigable; recent items. Plan: `docs/design/phase-19-plan.md` (stream S). ADR 0160. Related: ADR 0034 (Links registry), CLAUDE.md §6
(Arabic search columns), §7 (access), §13 (MCP).

## 1. Projects

| Project | Contents |
|---|---|
| `Strategy.Platform.Search.Contracts` | `ISearchContributor`, `SearchQuery`, `SearchHit`, `SearchField`, `SearchRanking` (pure ranking + C# trigram similarity), `AddSearchContributor<T>(systemCode?)`. Kernel + Links.Contracts only (no EF Core / ASP.NET, architecture tests) |
| `Strategy.Platform.Search` | `SearchService`, `RecentItems`, `SearchDbContext` (schema **`search`**), endpoints `/api/v1/search`, MCP / Ask tool `search` |
| `Strategy.Platform.Persistence` (`QuickSearch.cs`, additive) | `HasQuickSearch(...)` (generated, stored `quick_search` column = SQL twin of `SearchText.Normalise` + GIN trigram index), `HasTrigramIndex(...)` for existing C#-maintained columns, `QuickSearch(...)` query helper (filter + order + take) |

The host registers `SearchComponent` in `PlatformCatalog` (after Links). Modules reference the contracts project and register one
contributor each.

## 2. Contract (fixed with the other streams)

```csharp
public interface ISearchContributor { Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken ct); }
public sealed record SearchQuery(string Text, string Normalised, int Limit);
public sealed record SearchHit(string ResourceType, Guid Id, double Score, string? MatchedOn, string? MatchedText);
services.AddSearchContributor<RiskSearchContributor>("risk");   // system code: not queried when the user cannot open the system
services.AddSearchContributor<ExternalRefSearchContributor>();   // no system: cross-system (stream X), always queried, hits still resolved
```

- `ResourceType` is a linkable type (ADR 0034). The service **always** resolves hits through `ILinkRegistry.ResolveAsync` and keeps only
  `visible` views: number, title, status and link come from the owning module's link source, never from the contributor. A record the
  user may not read is dropped — not shown, not counted (404 semantics).
- On top, each contributor applies the user's data scope **in its database query** (the module's existing scoped reader: `RisksAsync`,
  `VisibleAsync`, `ApplyScopeAsync`…), so the top-N is not filled by invisible records.

## 3. Matching and ranking

Search term: trimmed text (≤ 200 chars) → `SearchText.Normalise` (diacritics / tatweel removed, alef forms unified, ى → ي, ة → ه,
Arabic-Indic digits → ASCII, lower case, spaces collapsed). Normalised length < 2 → 400 (`validation.minimumLength`, field `q`).

Database filter (one query per type, scoped, `LIMIT query.Limit`): every word of the term (any order, at most 8) with
`quick_search ILIKE '%word%'` (GIN `gin_trgm_ops` index makes the infix search index-assisted); ordered by exact number, number prefix, then `similarity(quick_search, term)`.

`quick_search` is a **generated stored column** (`GENERATED ALWAYS AS (…) STORED`) whose SQL mirrors `SearchText.Normalise`
(`regexp_replace` of tashkeel U+064B–U+065F, U+0670 and tatweel, `translate` of أ إ آ ٱ ى ة and both digit sets, `lower`, whitespace
collapsed) over number + titles + key fields. No code writes it, so it cannot drift; it is skipped by the audit interceptor
(computed columns are derived data). Tables that already keep a C#-maintained normalised column (Requests `title_search`, Document Registry
`search_text`, IP `search_text`) reuse it and only get the trigram index.

Ranking (`SearchRanking.Rank`, pure, unit-tested) on the fetched row, in C#:

| Match | Score | `matchedOn` |
|---|---|---|
| number equals the term (case-insensitive, normalised) | 1.00 | `number` |
| number starts with the term | 0.90 | `number` |
| number contains the term | 0.75 | `number` |
| a title contains every word of the term | 0.50 + 0.20 × trigram similarity (title vs term) | `title` |
| a key field contains every word | 0.30 + 0.10 × similarity | the field key (e.g. `event`), with a ~120-character snippet as `matchedText` |

No match on any fetched field → no hit (the generated column and the C# fields are the same text, so this only filters edge cases).

## 4. Types and fields

| Type | System | Number | Titles | Key fields (`matchedOn`) | Column |
|---|---|---|---|---|---|
| `portfolio.request` | portfolio | number | name AR / EN | partner name (`partner`), previous programme (`previousProgram`) | new `quick_search` |
| `portfolio.project` | portfolio | number | name AR / EN | — | new |
| `portfolio.milestone` | portfolio | code | title AR / EN | main deliverable (`deliverable`) | new |
| `portfolio.story` | portfolio | code | title | — | new |
| `kpi.card` | kpi | code | name AR / EN | process name (`process`) | new |
| `kpi.result` | kpi | card code · period year | card name AR / EN | via the card (`card`) | card's column (join) |
| `risk.risk` | risk | number | title AR / EN | event (`event`) | new |
| `risk.action` | risk | number | title AR / EN | — | new |
| `teams.team` | teams | number | name AR / EN | subject (`subject`) | new |
| `teams.recommendation` | teams | number | text | — | new |
| `teams.meeting` | teams | number | title | location (`location`) | new |
| `tasks.task` | tasks | number | title AR / EN | — | new |
| `requests.request` | requests | number | title AR / EN | — | existing `title_search` + index |
| `docreg.document` | docreg | document no. | name AR / EN | description (`description`) | existing `search_text` + index |
| `ip.record` | ip | number | name AR / EN | description (`description`), document no. (`documentNo`) | existing `search_text` + index |
| `bench.study` | bench | number | title AR / EN | request number (`requestNumber`) | new |
| `cap.source` | cap | number | title AR / EN | — | new |
| `cap.remark` | cap | number | text AR / EN | — | new |
| `cap.action` | cap | number | details AR / EN | — | new |
| `auditmgmt.report` | auditmgmt | number | title AR / EN | auditor body (`auditor`) | new |
| `auditmgmt.finding` | auditmgmt | number | title AR / EN | — | new |
| `sample.item` | sample | number | name AR / EN | — | new |

External IDs (§B) come from stream X's cross-system contributor (`matchedOn = externalId`).

## 5. Service and API

`SearchService.SearchAsync(user, q, system?, limit)`:
1. Internal principals only (vendor → 403; unauthenticated → 401 by the pipeline).
2. Systems the user may open now: `IUserRoleQuery.OpenSystemsAsync` (enabled **and** granted). A contributor whose system is not in the
   set — or not the `system` filter — is **not called**.
3. Contributors run in parallel, each in its own DI scope (DbContexts are not thread-safe; same pattern as My Work cards), 5 s time-out;
   a failing contributor is logged and skipped (never a broken search).
4. Hits de-duplicated by (type, id) keeping the best score → `ILinkRegistry.ResolveAsync` → only `visible` → grouped by the type's
   system → each group sorted by score (then number) and cut to `limit`; groups ordered by their best score, then the system sort order.

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/search?q=&system=&limit=` | Groups: `systemCode`, `nameAr`, `nameEn`, `items[]` (`type`, `id`, `typeNameAr/En`, `number`, `titleAr/En`, `statusCode`, `statusLabelAr/En`, `tone`, `link`, `matchedOn`, `matchedText`, `score`). `limit` 1–50 (default 5). Self-service (signed-in), global rate limit |
| `GET /api/v1/search/recent` | The user's recent items (≤ 20), re-resolved through the Links registry on every read: a record whose access was revoked, or whose system was switched off, disappears |
| `POST /api/v1/search/recent` `{type, id}` | Records a visit; 404 when the record is not visible to the caller (nothing stored); 204 otherwise |
| `GET /api/v1/search/types` | Enabled linkable types with their link template (the SPA records visits of record pages centrally) |

Reads are not audited (CLAUDE.md §8 audits writes, sign-ins, exports, AI and MCP calls — the MCP `search` tool call is audited by the MCP
gateway as every tool call). Recent items are personal navigation history, not business data: table `search.recent_item`
(`user_id`, `resource_type`, `resource_id`, `visited_at`; unique per user + record), upserted on visit and trimmed to 20 (hard delete of
the oldest rows is allowed: not a business record), not audited.

## 6. MCP / Ask

Read tool **`search`** (system `search`, kind Read, enabled by default): arguments `query` (2–200), `system?`, `limit?` (1–25) → the same
`SearchService`, so the same permission and visibility rules; returns groups with type, id, number, titles, status, link and matched-on.

## 7. Web

- `web/src/ui/command-search.tsx` — reusable `CommandSearch` (ARIA 1.2 combobox + listbox): input with `role="combobox"`,
  `aria-expanded`, `aria-controls`, `aria-activedescendant`; options grouped (`role="group"` with system heading); ↑ / ↓ move (wraps),
  Home / End, Enter opens, Esc closes (second Esc clears); recent items when empty; skeleton while loading; "no results" state; logical
  properties only; full width on phones (360 px: icon button opens it as a sheet over the top bar).
- `web/src/features/search/GlobalSearch.tsx` — wires it to the API (debounce 200 ms, TanStack Query), `/` and Ctrl / ⌘+K focus,
  navigates on Enter and posts the visit; `useRecordVisits()` in the app shell posts visits of record pages centrally (path matched against
  the link templates of `/api/v1/search/types`).
- Translations: namespace `search` (`ar`, `en`).
- Tests: Vitest for keyboard behaviour; Playwright `web/e2e/search.spec.ts` (projects `ar` and `en`), screenshots
  `docs/screenshots/$E2E_SCREENSHOTS/search-{results,recent}-{ar,en}.png`.

## 8. Tests

Integration (`tests/Strategy.IntegrationTests/Search/GlobalSearchTests.cs`): two users with Own scope — the other's record never returned by
number, title or key field (and not counted); user without system access gets nothing from that system; disabled system not searched;
Arabic normalisation (أحمد / احمد, ة / ه, diacritics, Arabic-Indic digits); recent items drop a record after access is revoked; vendor
principal refused; validation (too short → 400 with `messageKey`); MCP tool. Unit: `SearchRanking`, trigram similarity, the SQL twin of
`SearchText.Normalise` (run against PostgreSQL in the integration test).
