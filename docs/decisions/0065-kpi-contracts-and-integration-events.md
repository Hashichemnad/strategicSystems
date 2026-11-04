# 0065 — KPI contracts for other systems and integration events

- **Status:** Accepted by the owner (Phase 12, before the pull request)
- **Context:** SPEC §2.3 D, §2.8, Appendix A Q-P6; BUILD_PLAN Phase 12: contracts now, consumers later (Portfolio Phase 9, Risk Phase 13,
  CAP Phase 15).

## Decision
- `Strategy.Modules.Kpi.Contracts`: `IKpiCatalog` (search / get card, year performance; scope-filtered for the caller), `IKpiPerformance`
  (the K5 calculator and RAG band — the single source of truth for project KPI goal status), `IKpiLinkedRecordSource` (implemented by
  consumers: records linked to a card, shown on the card through the Links registry with permission-aware badges).
- Integration events through the outbox (ADR 0043), published in the approval transaction: `KpiResultApproved` (every final approval) and
  `KpiTargetMissed` (approved result in the red band — amber optional in Settings — with the count of consecutive red periods).
- Raising a CAP remark "in one click / automatically" is CAP's reaction to `KpiTargetMissed` (Phase 15).
