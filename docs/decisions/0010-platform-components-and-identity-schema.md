# 0010 — Platform components with endpoints, and the `identity` schema

- **Status:** Accepted (Phase 1)
- **Context:** Organisation and Identity are platform services (CLAUDE.md §5), not switchable systems. They own tables and expose HTTP endpoints. Modules use `IModule` and can be disabled; platform services must always be on.

## Decision
- `Strategy.Platform.Http.Modules.IPlatformComponent` has the same shape as `IModule` (`Name`, `AddServices`, `MapEndpoints`). The host lists components in `PlatformCatalog` and registers them before modules. Their route groups live under `/api/v1/<name>` and have **no enable switch**.
- Organisation uses the `core` schema, as CLAUDE.md §5 lists. Identity uses its own schema, **`identity`**:
  - every `ModuleDbContext` has its own migrations-history table inside its schema, so two contexts cannot share one;
  - keeping credentials apart from master data also allows tighter database grants later.
- Data Protection keys live in `core.data_protection_keys`, owned by `DataProtectionKeysDbContext` in `Strategy.Platform.Persistence`. Its history table is named `__ef_migrations_history_keys` so it does not clash with the Organisation context in the same schema.
- Identity references Organisation (platform → platform is allowed) to link users to employees. Organisation never references Identity: it raises an `EmployeeLeft` notification through `IEmployeeLifecycleHook`, which Identity implements to deactivate the user account.

## Consequences
- The architecture rules are unchanged: both projects classify as Platform.
- The module enable switch does not apply to platform endpoints.
