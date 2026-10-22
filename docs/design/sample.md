# Design note — Sample module (`_Template`)

**Purpose.** Phase 0 proves the module pattern end to end: DB → API → generated TS client → page in AR and EN.
The module has no business meaning. It is kept as the **reference template** for new modules: copy its structure,
not its content. It runs only where `Modules:Sample:Enabled=true` (Development and Testing); production config
ships it disabled, and its endpoints then answer `404 module.disabled`.

## Scope
- One aggregate, `SampleItem`: bilingual name and description, a status, audit columns and a row version.
- Commands: create, update (optimistic concurrency), activate, deactivate (no hard delete, per CLAUDE.md §6).
- Queries: get by id, paged list with search, status filter and whitelisted sorting.
- One Contracts interface, `ISampleItemDirectory`, to show how another module would read it.

## Data model (schema `sample`)
| Table `sample.sample_items` | Type | Notes |
|---|---|---|
| `id` | `uuid` | Guid v7 |
| `name_ar`, `name_en` | `varchar(200)` null | at least one is required |
| `description_ar`, `description_en` | `varchar(2000)` null | |
| `status` | `varchar(32)` | code: `active` / `inactive` (labels in i18n) |
| `created_at`, `created_by`, `updated_at`, `updated_by` | `timestamptz`, `uuid` | set by `AuditFieldsInterceptor` |
| `xmin` | system column | concurrency token (`Version` in the API) |

Migrations history: `sample.__ef_migrations_history`.

## State machine
| From \ command | Activate | Deactivate | Update |
|---|---|---|---|
| `active` | ✗ `sample.item.alreadyActive` (422) | → `inactive` | ✓ |
| `inactive` | → `active` | ✗ `sample.item.alreadyInactive` (422) | ✗ `sample.item.inactive` (422) |

## API (`/api/v1/sample`)
| Method | Route | Permission | Result |
|---|---|---|---|
| GET | `/items?page&pageSize&sort&search&status` | `sample.item.read` | `PagedResult<SampleItemDto>`; scope applied in the DB query |
| GET | `/items/{id}` | `sample.item.read` | `SampleItemDto`; 404 when missing **or out of scope** |
| POST | `/items` | `sample.item.create` | 201 + `SampleItemDto`; 400 validation |
| PUT | `/items/{id}` | `sample.item.update` | `SampleItemDto`; 409 on a stale `version` |
| POST | `/items/{id}/activate`, `/items/{id}/deactivate` | `sample.item.update` | `SampleItemDto`; 422 on an invalid transition |

Errors are Problem Details with `code` + `messageKey` (+ `errors[]` for validation).

## Approvals, events, jobs
None. The module uses no approval process, publishes no integration events and runs no jobs.

## Screens
`/sample`: a list page (PageHeader, search, status filter, sortable Table, paging, EmptyState, Skeleton), with a
create/edit dialog built with react-hook-form + zod, server validation errors mapped to fields, and toasts. Works in AR (RTL) and EN (LTR).

## Tests
Unit: domain transitions, validator. Integration (Testcontainers): every endpoint's happy path, validation 400,
404, 409 concurrency, 422 transition, module disabled → 404, Production placeholder → 403 / 404. E2E: create → list in AR and EN.
