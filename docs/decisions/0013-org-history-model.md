# 0013 — Organisation history: effective-dated versions and a materialised path

- **Status:** Accepted (Phase 1)
- **Context:** SPEC §0.1: "history kept when the structure changes (re-orgs must not break historical records)", with unlimited depth. Phase 2 data scopes need fast "unit and below" queries.

## Decision
- **Current and history tables.**
  - `core.org_unit` holds the current state. Records in every module reference units by id only.
  - `core.org_unit_version` holds one row per effective-dated state, as a half-open interval `[valid_from, valid_to)` of `date`.
  - A change closes the current version at its effective date and opens a new one. A change effective on the same date as the current version replaces that version instead of creating a zero-length one.
- **Materialised path.**
  - `path` (`/<id>/<id>/`) and `depth` are stored on the unit and on every version.
  - Moving a unit rewrites the path of the unit and all its descendants, each with a new version, in one transaction through EF change tracking (no raw SQL, CLAUDE.md rule 5).
  - "Unit and below" is `path LIKE '<path>%'`, served by a `text_pattern_ops` index.
- **Employee placement** keeps the same style of history in `core.employee_assignment`, covering unit, manager and job title.
- **As-of reads** come from the version tables. Historical screens and approver resolution for past dates use `GetPathAsync(unitId, asOf)`.

## Alternatives considered
- `ltree`: needs an extension and labels that are not uuids.
- A closure table: heavier writes and more rows.
- Temporal tables: not native to PostgreSQL.

The path plus versions approach is simple and fully testable.

## Consequences
- Back-dated moves are allowed only as far as the start of the current version. Changing history further back is not supported, and is refused with `org.unit.effectiveDateTooEarly`.
