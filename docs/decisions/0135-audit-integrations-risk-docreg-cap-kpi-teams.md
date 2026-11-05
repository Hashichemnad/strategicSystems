# 0135 — Audit integrations: Risk controls, Document Registry, CAP, KPI, Teams

- **Status:** Proposed (Phase 16)
- **Context:** SPEC §10.5.

## Decision
- **Risk:** `IRiskDirectory` gains `ListControlsAsync` and `LowerControlEffectivenessAsync` (additive). Linking a finding to a risk can flag
  it for reassessment (reason `audit-finding`) and lower a linked control one level of the framework's effectiveness scale (audited in
  Risk; reaching the lowest level raises the existing `RiskControlRatedIneffective`). Links are stored in Audit (`finding_risk`).
- **Document Registry:** criteria document id; the badge resolves through `IDocumentDirectory` with the viewer's visibility.
- **CAP:** only *improvement observations* (finding type `improvement`) may be sent, through `ICapRemarkSources` (one source per audit
  report, external key = finding id, source type internal / external audit). Formal recommendations stay in Audit Management (SPEC §10.8).
- **KPI:** new `IKpiResultFeed.ProposeAsync` writes a measure value as a machine draft on the open result of the period (existing
  `DraftFromMcp` flag); the KPI owner submits through the normal approval. Mapping measure → card in Settings; empty = off.
- **Teams:** an engagement references an existing team (`ITeamsDirectory`), no copy.

## Consequences
Every integration is optional at run time (service resolved only while the other module is enabled).
