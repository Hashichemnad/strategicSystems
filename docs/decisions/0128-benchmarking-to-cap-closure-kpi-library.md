# 0128 — Analysis approval → CAP, closure rule, KPI benchmark update, best-practice library, impact follow-up

- **Status:** Proposed (Phase 15)
- **Context:** SPEC §8.2 / §8.3 / §9.5: PMO approval of the analysis creates a CAP remarks source with one remark per development-plan
  item and proposed actions; the study shows them live and closes after PMO final approval when all are completed with evidence; results
  may update the KPI card's benchmark fields after PMO approval; practices become a searchable library; optional impact review.

## Decision
- In the `bench.analysis` approval hook (same transaction): `ICapRemarkSources.EnsureSourceAsync` (type `benchmarking`, linked
  `bench.study`, viewers = coordinator and topic owners) and `AddRemarkAsync` per plan item (external key = item id, proposed action
  pre-filled with the item owner and due date). No action data is stored in Benchmarking.
- Closure (`bench.closure`) is refused unless `GetProgressAsync(...).AllActionsCompleted`; re-checked on approval. Benchmarking handles
  `CapRemarkStatusChanged` to notify *ready for closure* once. Closure publishes `LinkedRecordClosed(bench.study, id, "completed")`.
- Additive `Kpi.Contracts.IKpiBenchmarkWriter.UpdateBenchmarkAsync(cardId, description, result, sourceRef)`: an audited change of the
  card's benchmark description / result only (not a definition change under ADR 0063, because the PMO approved the analysis).
- Practice rows flagged *add to library* become best-practice entries; PMO adds / deactivates manual entries.
- Impact follow-up after `impactFollowUpMonths` (6; 0 = off): notice once, review records implemented / KPIs moved / notes.

## Consequences
Larger improvements can still be raised as Portfolio requests from the CAP action (budget routing).
