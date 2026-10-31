# 0015 — Data scope semantics and how scopes reach the database

- **Status:** Accepted (Phase 2)
- **Context:** SPEC §0.2 names five data scopes: own, my team, my unit, my unit and below, and all. It does not define "team", or which unit counts as "my unit". CLAUDE.md §7 requires list filtering in the database query, and one `IAccessService` for everything.

## Decision
- **Own:** the record's owner employee is the user's employee, or the owner user is the user. Each module states which column is the owner in a `ScopeMap<T>`.
- **Team:** own, plus records owned by the user's **direct reports**, i.e. employees whose current manager is the user's employee.
  - This is the narrowest reading, so it is the safe default.
  - Teams of the Teams module (Phase 7) are covered by record roles, not by this scope.
- **Unit:** the user's employee's current unit. **Unit and below:** that unit and its current subtree, from the materialised path in `core.org_unit` through `IOrgDirectory`.
- **Combining scopes:** when several roles grant the same permission, their scopes are combined with OR. Record roles add the ids of the records they cover.
- **Which unit:** a user who heads a unit without being placed in it does **not** get that unit by being its head. Scopes follow placement. Heads are placed in their unit in the organisation data.
- **Query shape:** `ApplyScopeAsync` resolves id lists (subtree units, direct reports, record-role record ids) once per request. It then builds one OR-predicate from the module's `ScopeMap<T>` expressions, which EF Core translates to SQL `IN (…)`. No cross-schema joins.
- **Contract change:** the method becomes asynchronous (`ApplyScopeAsync`) and takes the `ScopeMap<T>`. `ResourceRef` carries the owner and unit attributes, so a record check needs no second query.

## Consequences
- Modules must give every scoped entity an owner and/or a unit. Sample items gain `owner_employee_id` and `unit_id` as the template.
- Changing "Team" to a wider reading later (the whole reporting line) is one change in `AccessService` and this ADR.
