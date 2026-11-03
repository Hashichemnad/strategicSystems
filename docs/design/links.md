# Design note — Phase 6: Links registry (`Strategy.Platform.Links`)

**Scope.** BUILD_PLAN Phase 6 "Platform.Links"; SPEC §0.0 cross-system links table and its integration rule ("links go through each
system's internal service, never by writing into another system's tables; every linked record shows a live badge of the other system's
record; disabling a system hides its links without breaking the others"); §4.4 F (team-able), §9 (remark sources), §11.6 (task-able).
CLAUDE.md §5 (`Links`: generic cross-system links registry). ADR 0034.

## 1. Projects

| Project | Contents |
|---|---|
| `Strategy.Platform.Links.Contracts` | What modules use: `LinkCapabilities`, `LinkableTypeDefinition` + `AddLinkableType`, `ILinkTargetSource` + `AddLinkTargetSource<T>`, `ILinkRegistry`, `ILinkStore`, DTOs. No EF Core / ASP.NET (architecture tests) |
| `Strategy.Platform.Links` | `LinksDbContext` (schema **`links`**), `LinkRegistry`, `LinkStore`, `LinksComponent`, endpoints under `/api/v1/links` |

Modules reference only the contracts project; the host registers the component.

## 2. Declaring a linkable type

```csharp
services.AddLinkableType(new LinkableTypeDefinition(
    ResourceType: "sample.item", SystemCode: "sample", NameAr: "بند تجريبي", NameEn: "Sample item",
    Capabilities: [LinkCapabilities.TaskAble],               // task-able · team-able · remark-source
    ReadPermission: "sample.item.read",                      // who may see the badge at all (plus the record check)
    LinkTemplate: "/sample/items/{id}",
    FollowLinkedPermission: "sample.item.update"));          // "linked-record role": sees every item linked to the record
services.AddLinkTargetSource<SampleLinkTargets>();
```

`ILinkTargetSource` (implemented by the owning module, one per resource type):

```csharp
Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken ct);
```

It returns **only the records the user may read** (the module applies its own `IAccessService` check — the same 404 rule as its own
endpoints). `LinkTarget` = id, number, title AR / EN, status code, status label AR / EN, tone (`neutral` / `info` / `success` / `warning`
/ `danger`), `IsClosed` (the source record is finished: "closing the source record can prompt to close open tasks", SPEC §11.6).

## 3. `ILinkRegistry` (one entry point for every link, permission-aware)

| Member | Behaviour |
|---|---|
| `Types(capability?)` | Declared types, filtered by capability; types of **disabled systems** are left out |
| `ResolveAsync(refs, user)` | One `LinkView` per reference: `visible` (title, number, status badge, link), `restricted` (exists or not — the user may not see it: no title, no link, so nothing leaks), `unavailable` (type unknown or its system switched off). Batched per type: one source call per type |
| `CanLinkAsync(user, type, id, capability)` | Type declares the capability, its system is on, the user holds its read permission and the source returns the record for the user |
| `CanFollowLinkedAsync(user, type, id)` | The user holds `FollowLinkedPermission` **on that record** (`IAccessService.CanAsync` with the record's reference, i.e. system role in scope or record role) — e.g. the PM sees every task of the project |

Access is never decided by the registry alone: it asks `IAccessService` and the module's source, both server-side.

## 4. Link storage — `links.record_link`

| Column | Notes |
|---|---|
| `id`, `kind` | `kind` = the capability that created it (`task-able`, `team-able`, `remark-source`) |
| `from_type`, `from_id` | The linking record (e.g. `tasks.task`) |
| `to_type`, `to_id` | The linked record (e.g. `sample.item`) |
| `active`, `removed_at`, `removed_reason`, audit columns, `xmin` | No hard delete: a removed link is inactive with a reason |

Unique partial index on active `(kind, from_type, from_id, to_type, to_id)`; index on `(to_type, to_id)`. Audited (`links.record_link`).
`ILinkStore.AddAsync / RemoveAsync` take an optional transaction context so the link commits with the owner's change (ADR 0023). The
owner of the *from* record writes the link (Tasks writes task → record links); nobody writes another module's tables.

## 5. API (`/api/v1/links`, signed-in internal users; every record checked as above)

| Endpoint | Purpose |
|---|---|
| `GET /types?capability=task-able` | Linkable types for pickers (labels AR / EN) |
| `GET /resolve?type=&id=` | Badge of one record (404 when not visible) — used by the link display component |
| `GET /to/{type}/{id}?kind=` | Links pointing at a record the user may read (404 otherwise), the *from* side resolved and restricted entries counted, not shown |

## 6. Web

`<LinkBadge type id />` (shared, in `features/links`): system name · number · title · status chip, a link when visible; "restricted
record" text otherwise. `<LinkedTasksPanel type id />` (in `features/tasks`) is what a task-able record page includes (Sample proves it).

## 7. Tests
Unit: registry filtering by capability and disabled system. Integration: Sample item resolves for its reader, is `restricted` for an
outsider (no title in the payload) and `unavailable` when the Sample system is switched off; `CanLink` refuses a record outside the scope;
link rows are written with the task and audited; `/to/` hides links the user may not see.
