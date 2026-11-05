# 0141 — Scope-correct cached aggregates: unit partitions, additive components, live fallback

- **Status:** Proposed (Phase 17)
- **Context:** SPEC §0.4: "the same dashboard shows different data to different people"; CLAUDE.md §7: lists filtered by scope in the
  database. A global cache cannot know the viewer; a per-user cache cannot meet < 3 s for leadership on 10 000 tasks after every change.
  Scopes are unit-based (Unit, UnitAndBelow, All) plus Own / Team and record roles; some records are restricted beyond scope
  (confidential tasks, document sensitivity, project costs).

## Decision
- Facts are partitioned by **owning unit** and **partition** (`public`, `confidential`, `sensitivity:<code>`, `costs`) and store additive
  components only (count, numerator / denominator, weight / weighted sum), so any set of units recombines exactly.
- New additive `IAccessService.GetScopeAsync(principal, permission)` returns the effective scope, the expanded unit set and the viewer's
  record-role grants on the resource type, from the cached access snapshot.
- Cached path when exact: scope All (every unit) or Unit / UnitAndBelow (those units), restricted partitions only if the module's
  `VisiblePartitionsAsync(user)` lists them. **Live path** (module computes for this user on its scoped query, cached 60 s in memory)
  when the scope is Own / Team, when the viewer holds record-role grants outside the scope units, or reads a restricted partition
  record by record.
- Missing system access or permission → the metric is absent (never shown as 0).

## Consequences
Numbers are exact for every viewer and never include a record the viewer cannot open; leadership and managers are served from the cache.
