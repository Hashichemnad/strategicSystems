# 0129 — CAP and Benchmarking AI features and MCP tools

- **Status:** Proposed (Phase 15)
- **Context:** SPEC §9.8 / §8.6; CLAUDE.md §12 (drafts only, structured JSON, untrusted text delimited) and §13 (read on, write off).

## Decision
- CAP: `cap-extract` (report → remarks list for PMO confirmation), `cap-root-cause` (5 Whys guide, likely causes), `cap-suggest` (actions
  + duplicate check without a model by trigram similarity over visible open actions — "link instead"), `cap-quality` (deterministic flags
  + model advice for the PMO), `cap-summary` (monthly bilingual leadership summary).
- Benchmarking: `bench-comparators`, `bench-questionnaire`, `bench-analysis` (visit notes → comparisons, gaps, draft plan items),
  `bench-summary`, `bench-search` (keywords from a natural-language question; works without a provider).
- MCP read tools (on): `cap_my_remarks`, `cap_my_actions`, `cap_remarks_by_source`, `cap_overdue_actions`, `bench_studies`,
  `bench_study_details`, `bench_best_practices`. Write tool (off): `cap_update_achievement_draft` (draft only). No approve tool.

## Consequences
Every AI output is a draft the user accepts; nothing is saved by the model.
