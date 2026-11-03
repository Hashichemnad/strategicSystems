# 0034 — Links registry: linkable types, permission-aware resolution and link storage

- **Status:** Accepted (Phase 6)
- **Context:** SPEC §0.0 asks for generic links between any system's records (task-able, team-able, remark sources) with a live badge of
  the other record, never by one system writing into another system's tables, and with disabled systems hiding their links.

## Decision
- New platform component **`Strategy.Platform.Links`** (schema **`links`**, added to the platform schemas in CLAUDE.md §5) with the
  contract project `Strategy.Platform.Links.Contracts`.
- A module **declares** a linkable type in code (`LinkableTypeDefinition`: resource type, system, AR / EN name, capabilities, read
  permission, deep link template, optional `FollowLinkedPermission`) and **implements** `ILinkTargetSource` for it: a batched lookup that
  returns title, number and status badge only for records the user may read (the module's own access check).
- `ILinkRegistry` resolves references into `visible` / `restricted` / `unavailable` views. A restricted link never carries a title or a
  link (existence of the *link* is known to whoever sees the linking record, the target's content is not). Types of disabled systems are
  `unavailable` and are not offered in pickers.
- `FollowLinkedPermission` models SPEC §11.9 "linked-record roles" generically: whoever holds that permission **on the target record**
  (system role in scope or record role) may see the items linked to it (e.g. the PM sees all tasks of the project).
- Links are stored in `links.record_link` by the owner of the linking record, in its transaction (ADR 0023); removal is a soft delete with
  a reason. Tasks also keep their linked record on the task row (the SPEC's "linked record" field) for fast filtering.

## Consequences
Teams (Phase 7), CAP remark sources (Phase 15) and Portfolio (Phases 8–10) only declare types and sources. The registry adds one source
call per type on a page that shows links.
