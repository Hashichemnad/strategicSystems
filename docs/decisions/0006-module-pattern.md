# 0006 — Module pattern, persistence and extra platform projects

- **Status:** Accepted (Phase 0)
- **Context:** CLAUDE.md §5 describes the modular monolith. Phase 0 fixes how a module plugs in and where shared plumbing lives.

## Decision
**Module contract** (`Strategy.Platform.Http.Modules.IModule`): `Name` (stable code), `AddServices(services, configuration)`,
`MapEndpoints(endpoints)`. The host lists compiled modules explicitly in `ModuleCatalog`. Whether a module is
**enabled** is configuration: `Modules:<Name>:Enabled`, moving to the Settings centre in Phase 2. A disabled module keeps
its endpoints mapped, so the OpenAPI document stays complete, but they answer `404 module.disabled`.

**Persistence** (`Strategy.Platform.Persistence`):
- `ModuleDbContext` base: one PostgreSQL **schema per module** (`HasDefaultSchema`), configurations from the module's assembly,
  `xmin` row version on every `AuditableEntity` (`Version` in the API → 409 on a stale value).
- `AddModuleDbContext<T>(schema)`: shared `NpgsqlDataSource`, snake_case naming, migrations history table
  `<schema>.__ef_migrations_history`, and a `/health/ready` check per module.
- `AuditFieldsInterceptor` sets `created_*` / `updated_*` (truncated to PostgreSQL microseconds) and **refuses hard deletes**
  of auditable entities. The field-level audit trail arrives in Phase 3.
- Migrations: `dotnet ef migrations add <Name> -p src/Modules/<M>/Strategy.Modules.<M> -s src/Host/Strategy.Host -c <M>DbContext -o Infrastructure/Migrations`.
  They are applied by `Strategy.Host --migrate` (install / upgrade step, exits) or at startup when `Database:MigrateOnStartup=true` (development, tests).

**Two platform projects not named in CLAUDE.md §5:**
- `Strategy.Platform.Persistence`: the EF Core / Npgsql conventions above. This keeps `Strategy.Platform.Kernel` pure (no EF, no ASP.NET), which the architecture tests enforce.
- `Strategy.Platform.Http`: endpoint conventions shared by every module's `Api/` folder (`IModule`, `MapModuleGroup`,
  `RequirePermission`, Problem Details mapping, the request-context middleware).

**Template module:** `Strategy.Modules.Sample` (`docs/design/sample.md`) stays in the repository as the reference to copy.
It is disabled in production configuration.

## Consequences
- Architecture tests enforce: modules reference Platform + `.Contracts` only; Contracts reference only Kernel and other Contracts, and use no EF / ASP.NET / Npgsql; Platform never references modules; Kernel references nothing internal.
- Adding a module: copy the Sample layout, add it to `ModuleCatalog`, create its first migration, add its translations.
