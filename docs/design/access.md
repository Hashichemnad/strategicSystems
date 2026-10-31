# Design note — Access control (`Strategy.Platform.Access`)

**Purpose.** SPEC §0.2 *Access management* and CLAUDE.md §7. There is one access model and one entry point for every endpoint, job, API call and (later) MCP tool. Three layers apply:
1. **System access**
2. **System roles** with a data scope
3. **Record roles**

On top of these come system ownership, bulk role transfer, the access review report and a permission matrix screen per system.

**Effective access** = the system is enabled **AND** the user has system access **AND** (a system-role permission within its scope **OR** a record-role permission on that record).

## Projects
| Project | Contents | Referenced by |
|---|---|---|
| `Strategy.Platform.Access.Contracts` (namespace `Strategy.Platform.Access`) | <ul><li>`IAccessService`, `ResourceRef`, `ScopeMap<T>`, `DataScope`, `StrategyClaims`</li><li>the catalogue types (`SystemDefinition`, `PermissionDefinition`, `RecordRoleDefinition`, `IAccessCatalogueSource`)</li><li>`IRecordRoleService` (grant / revoke for modules), `ISystemState` (enabled?), `IUserDirectory` (implemented by Identity), `IUserRoleQuery` (used by Identity), `IRoleTransferHook`</li></ul>No EF / ASP.NET | Http, Identity, Organisation, Settings, every module |
| `Strategy.Platform.Access` | <ul><li>`AccessDbContext` (schema `access`)</li><li>`AccessService`, catalogue synchronisation and default seed</li><li>admin API `/api/v1/access`, access review (ClosedXML), bulk transfer</li></ul> | Host |

The split is needed because `Platform.Http` (endpoint filter) needs the contract, and the access component needs `Platform.Http` for its own endpoints. Contracts keep the old namespace, so callers did not change their `using`s.

## Permission catalogue (code-declared, synchronised at start-up)
Each platform component and module declares its catalogue in code and registers it with `services.AddAccessCatalogue(new XxxAccessCatalogue())`:

```csharp
public sealed class SampleAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new("sample", SystemKind.Module, Sort: 900);
    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(SamplePermissions.Read,   "عرض البنود",   "View items",   Scopable: true),
        new(SamplePermissions.Create, "إنشاء بند",    "Create items", Scopable: false),
        new(SamplePermissions.Update, "تعديل البنود", "Edit items",   Scopable: true),
    ];
    public IEnumerable<RecordRoleDefinition> RecordRoles =>
        [new(SamplePermissions.ResourceType, "editor", "محرر البند", "Item editor", [SamplePermissions.Read, SamplePermissions.Update])];
    public IEnumerable<RoleSeed> DefaultRoles =>
        [new("viewer", "مستعرض", "Viewer", [new(SamplePermissions.Read, DataScope.All)])];
}
```

- **Permission code format:** `<system>.<object>.<action>`. The system is the first segment.
- **Synchronisation:** at start-up the catalogue is upserted into `access.permission`. Labels (AR/EN) always follow the code. Permissions removed from code are deleted together with their role links.
- **Seeding:** systems, record-role definitions and default roles are inserted **only when missing**, so administrators' edits survive upgrades.
- **Scopable permissions:** `Scopable = false` marks a permission where a data scope makes no sense (create, administration). A grant of such a permission always behaves as `All`.

## Data model (schema `access`)
| Table | Key columns | Notes |
|---|---|---|
| `system` | `code` (unique), `kind` (`platform` / `module`), `enabled`, `owning_unit_id`, `sort` | Platform systems (`org`, `identity`, `access`, `settings`) cannot be disabled and need no system-access grant. A module's first `enabled` value comes from `Modules:<Name>:Enabled`; after that it is set only in Settings |
| `permission` | `code` (PK), `system_code`, `object`, `action`, `label_ar`, `label_en`, `scopable`, `sort` | Synchronised from code |
| `role` | `system_code`, `code` (unique per system), `name_ar`, `name_en`, `description_ar/en`, `builtin`, `active`, `requires_mfa`, `locked` | `locked` roles (the access administrator) cannot lose permissions or be deactivated, so nobody is locked out |
| `role_permission` | `role_id`, `permission_code`, `scope` | One row per granted permission, holding the data scope |
| `role_assignment` | `role_id`, `principal_kind` (`user` / `unit` / `everyone`), `principal_id`, `valid_from`, `valid_to` (dates, inclusive), `note` | `unit` covers every user whose employee is in the unit **or below** |
| `system_access` | `system_code`, `principal_kind`, `principal_id` | The same principal kinds as assignments |
| `record_role_definition` | `resource_type`, `role_code`, `system_code`, `name_ar`, `name_en` | Seeded from code |
| `record_role_permission` | `definition_id`, `permission_code` | Editable in the matrix |
| `record_role` | `resource_type`, `resource_id`, `principal_kind` (`employee` / `user`), `principal_id`, `role_code`, `valid_from`, `valid_to` (timestamptz) | Written only through `IRecordRoleService`, never by SQL |
| `role_transfer` | `role_ids`, `from`, `to`, `moved_count`, `reason` | A log of bulk transfers (the hash-chained audit trail comes in Phase 3) |

Every table except `permission` and `role_permission` is auditable (`created_*`, `updated_*`, `xmin`).

## Data scopes (ADR 0015)
| Scope | A record is in scope when… |
|---|---|
| `own` | its owner employee is the user's employee, or its owner user is the user |
| `team` | `own`, or its owner is a **direct report** of the user (employees whose manager is the user's employee) |
| `unit` | its unit is the user's employee unit |
| `unit_and_below` | its unit is the user's unit or any unit below it (current structure) |
| `all` | always |

- **Several roles:** the scopes of all roles that grant the permission are combined with OR.
- **Record roles:** a record role that grants the permission adds `id ∈ {records with that record role}`.
- **No employee:** a user without an employee matches only `own` (by user) and `all`.

## Contract and how modules use it
```csharp
public interface IAccessService
{
    Task<bool> CanAsync(ClaimsPrincipal principal, string permission, ResourceRef? resource = null, CancellationToken ct = default);
    Task<IQueryable<T>> ApplyScopeAsync<T>(IQueryable<T> query, ClaimsPrincipal principal, string permission, ScopeMap<T> scope, CancellationToken ct = default) where T : class;
}
public sealed record ResourceRef(string Type, Guid Id, Guid? OwnerEmployeeId = null, Guid? OwnerUserId = null, Guid? UnitId = null);
public sealed class ScopeMap<T>(string resourceType, Expression<Func<T, Guid>> id) { OwnerEmployee / OwnerUser / Unit expressions; ResourceRef RefOf(T entity) }
```

**`CanAsync` without a resource** answers the endpoint question: is any grant possible?
- **Allowed** when the user holds the permission through a role (any scope), or holds **any** record role whose definition grants it. The handler then does the record check.
- **Refused:** 403.

**`CanAsync` with a resource** answers the record question. The scope is evaluated against the attributes carried by `ResourceRef`, and record roles against `(Type, Id)`.

**`ApplyScopeAsync`** turns the scopes into one SQL predicate built from the `ScopeMap` expressions:
- the unit and direct-report id lists are loaded once per request;
- record-role ids are loaded per resource type.

Example: the module endpoint and list query (Sample, the template module):
```csharp
items.MapGet("/", ListAsync).RequirePermission(SamplePermissions.Read);            // 403 if no grant at all
// handler
var items = await access.ApplyScopeAsync(db.Items.AsNoTracking(), context.User, SamplePermissions.Read, SampleScope.Map, ct);
// record handler
if (item is null || !await access.CanAsync(context.User, SamplePermissions.Update, SampleScope.Map.RefOf(item), ct)) return Error.NotFound();
```

**404 vs 403.** A record outside the user's read scope → 404. A record the user may read but not change → 403 (`access.forbidden`). Handlers check read first, then the action.

**Disabled system.**
- `MapModuleGroup` answers 404 `module.disabled` through `ISystemState`.
- `CanAsync` refuses the system's permissions, which also covers jobs and MCP.

**Caching.** Each user's grants are cached in memory for 60 s and dropped on any access change (a generation counter). The per-request snapshot is computed once.

**System principal.** It is allowed everything, as in Phase 1. Vendor principals are refused (Phase 11).

## Interim access replaced (ADR 0009 → ADR 0016)
- **`is_system_admin` is dropped.** The Identity migration first copies the flag into assignments of the seeded **platform administrator roles** (`org.administrator`, `identity.administrator`, `access.administrator`, `settings.administrator`).
- **MFA for administrators:** "administrators" now means users holding any role with `requires_mfa` (all four seeded administrator roles).
- **Last administrator guard:** removing or deactivating the last active holder of `access.administrator` is refused (`access.lastAdministrator`). This replaces "last system admin".
- **Everyone can read the organisation:** the seeded role `org.viewer` (unit and employee read, `all`) is assigned to `everyone`. Phase 1 behaviour is kept and remains editable.
- **API changes:**
  - `/me` no longer returns `isSystemAdmin`;
  - `POST /identity/admin/users/{id}/set-admin` is removed (roles are managed in the access screens);
  - user creation no longer takes `isSystemAdmin`.
- **`--create-admin`** assigns the four administrator roles.
- **Development / Testing no longer open modules to everyone.** The development seed grants the demo users roles instead.

## Bulk role transfer
`POST /access/transfers`:

| Field | Meaning |
|---|---|
| `roleIds` | the roles to move |
| `from` | users, or a unit |
| `to` | one user, or a unit |
| `reason` | why |

- Active matching assignments are ended (yesterday) and recreated for the target.
- Registered `IRoleTransferHook`s are called in the same request; the Phase 4 approval engine re-routes pending approvals and tasks.
- The transfer is logged in `role_transfer` and the log.

## Access review report
`GET /access/review?system=&unitId=&userId=` and `/access/review/export?lang=` (xlsx). One row per (user, system, role, source) where source is `direct` / `unit` / `everyone`, with the permissions and scopes. Record roles are listed per user (resource type, id, role).

## Endpoints (`/api/v1/access`)
| Method & path | Permission |
|---|---|
| `GET /me` | signed in. Returns the systems the user can open and the permission codes held (for the UI only) |
| `GET /systems` · `PUT /systems/{code}` (enabled, owning unit) | `access.system.read` / `access.system.manage` |
| `GET /systems/{code}/matrix` | `access.role.read` |
| `GET /roles?system=` · `POST /roles` · `PUT /roles/{id}` · `PUT /roles/{id}/permissions` | `access.role.read` / `access.role.manage` |
| `GET /record-roles?system=` · `PUT /record-roles/{id}/permissions` | `access.role.read` / `access.role.manage` |
| `GET /assignments?roleId=&userId=` · `POST /assignments` · `POST /assignments/{id}/end` | `access.assignment.read` / `access.assignment.manage` |
| `GET /system-access?system=` · `POST /system-access` · `POST /system-access/{id}/revoke` | `access.assignment.read` / `access.assignment.manage` |
| `POST /transfers` · `GET /transfers` | `access.assignment.manage` / `access.assignment.read` |
| `GET /review` · `GET /review/export` | `access.review.export` |
| `GET /users/{id}/effective` | `access.assignment.read`. Shows what a user can do |

## Screens (Settings → Access)
- **Roles & permissions:** pick a system; a matrix of roles (columns) × permissions (rows), with a scope selector per cell; a tab for record roles.
- **Assignments:** user / unit / everyone, with dates.
- **System access.**
- **Bulk transfer:** a wizard.
- **Access review:** filters and an Excel export.
- **Systems:** enable / disable a module, owning unit.

The sidebar shows only the systems the user can open and the administration entries they have permissions for.
