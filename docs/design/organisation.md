# Design note — Organisation and Employees (`Strategy.Platform.Organisation`)

**Purpose.** SPEC §0.1: one shared organisation structure and one employee register. Every system uses them for ownership, filtering, data scopes and approver resolution. Nothing here is organisation-specific: level names, unit codes and structure are data, entered in the UI or imported from Excel/CSV.

## Scope (Phase 1)
- **Organisation levels.** These are configurable names, such as Organisation, Sector, Department, Section and Unit, with an order.
- **Units.** The hierarchy has unlimited depth. Each unit has a code, AR/EN names, a level, a parent, a head, a deputy, a cost centre, an active flag and effective dates.
  - History is preserved on every structural change, so a re-org does not break historical records.
- **Org chart** as of any date.
- **Employees.** The record holds every SPEC field, a photo, assignment history (unit, manager and job title over time) and a status (`active` / `on_leave` / `left`).
- **Delegations.** An employee hands their approvals to another employee for a date range, optionally limited to some systems or processes. The approval engine consumes these in Phase 4.
- **Import.** Bilingual Excel templates. Uploads are .xlsx or .csv. A validate pass produces a report, and a commit pass is all-or-nothing.
- **Directory sync.** The `IDirectorySyncSource` interface exists with a no-op stub. The AD/HR sync job is scheduled with Quartz in Phase 3, and connectors come per customer.

## Data model (schema `core`)
| Table | Key columns | Notes |
|---|---|---|
| `org_level` | `code` (unique), `name_ar`, `name_en`, `sort`, `active` | Seeded: `organisation`, `sector`, `department`, `section`, `unit` |
| `org_unit` | `code` (unique), `name_ar`, `name_en`, `level_code`, `parent_id`, `path`, `depth`, `head_employee_id`, `deputy_employee_id`, `cost_centre`, `active`, `effective_from`, `effective_to`, `name_search` | Current state; auditable, `xmin` |
| `org_unit_version` | `unit_id`, `valid_from`, `valid_to` (null = current), `parent_id`, `path`, `depth`, `code`, names, `level_code`, `head_employee_id`, `deputy_employee_id`, `cost_centre`, `active` | One row per effective-dated state |
| `employee` | `employee_no` (unique), `name_ar`, `name_en`, `email` (unique, lower-cased), `mobile`, `job_title_ar`, `job_title_en`, `grade`, `unit_id`, `manager_employee_id`, `status`, `join_date`, `name_search` | Auditable, `xmin` |
| `employee_assignment` | `employee_id`, `unit_id`, `manager_employee_id`, `job_title_ar`, `job_title_en`, `valid_from`, `valid_to` | History of placement |
| `employee_photo` | `employee_id` (PK), `content_type`, `content`, `updated_at` | PNG/JPEG/WebP checked by magic number, ≤ 1 MB (ADR 0014) |
| `delegation` | `delegator_employee_id`, `delegate_employee_id`, `from_date`, `to_date`, `scope` (`text[]` of system codes / process keys; empty = all), `reason`, `status`, `cancel_reason` | Auditable, `xmin` |
| `org_settings` | singleton row: `enforce_level_order` | Moves to the Settings centre in Phase 2 |

- **`path`** is the chain of unit ids from the root, formatted as `/<id>/<id>/…/`. It makes "unit and below" a `LIKE 'path%'` index scan, which Phase 2 data scopes use.
- **`name_search`** holds the normalised AR/EN names (diacritics and tatweel removed; alef, ya/alef maqsura and ta marbuta/ha unified; lower case). It is written by the domain and indexed with a trigram index (`pg_trgm`).

### Unit history rules
| Change | Effect |
|---|---|
| Create | Unit and first version `valid_from = effective_from` |
| Update names / head / deputy / cost centre / level, or move to a new parent | The current version is closed at the change's effective date (default: today), a new one opens, and the current row is updated |
| Move (re-parent) | Same as above for the unit **and every descendant**, because their paths change. All in one transaction |
| Deactivate / reactivate | New version with `active` flipped. Deactivation is refused while the unit has active child units or active employees |

Validation rules:
- The effective date may not be earlier than the start of the current version.
- A unit cannot be moved under itself or under one of its descendants (`org.unit.cycle`).
- A child's level must sort after its parent's level (`org.unit.levelOrder`), unless `enforce_level_order` is off.

**As-of queries.** `GetTree(asOf)` and `GetPath(unitId, asOf)` read `org_unit_version` where `valid_from <= asOf < valid_to`. A historical record keeps its `unit_id`, and asking for its path as of its date shows the structure at that time.

### Employee rules
- `employee_no` and `email` are unique.
- The unit must exist and be active.
- The manager must exist, must not be the employee, and must not be a subordinate (no reporting cycle).
- A change of unit, manager or job title closes the current `employee_assignment` row and opens a new one at the effective date.
- Status `left` is the soft delete. Business records are never hard-deleted.

### Delegation rules
| Rule | Code |
|---|---|
| The delegator and the delegate must be different people | `org.delegation.self` |
| The delegate must be active | `org.delegation.delegateInactive` |
| `from_date <= to_date`, and `to_date >= today` | `validation.dateRange`, `org.delegation.inPast` |
| No overlapping active delegation from the same delegator with an overlapping scope | `org.delegation.overlap` |
| No active reverse delegation (B → A) overlapping in time, so chains cannot loop | `org.delegation.cycle` |
| Cancel only `active`; cancellation needs a reason | `org.delegation.notActive` |

## Permissions (interim enforcement: ADR 0009; catalogue in Phase 2)
| Permission | Used by |
|---|---|
| `org.unit.read` / `org.employee.read` | Directory, tree, chart, employee list (any signed-in internal user) |
| `org.unit.manage` | Create / update / move / deactivate units, levels, settings |
| `org.employee.manage` | Create / update employees, status, photo |
| `org.import.manage` | Import templates and uploads |
| `org.delegation.manage` | Delegations for other people. Own delegations are self-service |

## API (`/api/v1/org`)
| Method | Route | Permission |
|---|---|---|
| GET | `/levels` | `org.unit.read` |
| POST / PUT | `/levels`, `/levels/{id}` | `org.unit.manage` |
| GET / PUT | `/settings` | `org.unit.read` / `org.unit.manage` |
| GET | `/units/tree?asOf=` | `org.unit.read` |
| GET | `/units/{id}`, `/units/{id}/history`, `/units/{id}/path?asOf=` | `org.unit.read` |
| POST | `/units` | `org.unit.manage` |
| PUT | `/units/{id}` (with `effectiveDate`, `version`) | `org.unit.manage` |
| POST | `/units/{id}/move`, `/units/{id}/deactivate`, `/units/{id}/activate` | `org.unit.manage` |
| GET | `/employees?search&unitId&includeDescendants&status&page&pageSize&sort` | `org.employee.read` |
| GET | `/employees/{id}`, `/employees/{id}/history`, `/employees/{id}/photo` | `org.employee.read` |
| POST / PUT | `/employees`, `/employees/{id}` | `org.employee.manage` |
| POST | `/employees/{id}/change-status` | `org.employee.manage` |
| PUT | `/employees/{id}/photo` (multipart) | `org.employee.manage` |
| GET | `/delegations/mine` | self |
| GET | `/delegations?employeeId&status` | `org.delegation.manage` |
| POST | `/delegations` (self, or for anyone with `org.delegation.manage`) | self / `org.delegation.manage` |
| POST | `/delegations/{id}/cancel` | the delegator, or `org.delegation.manage` |
| GET | `/import/templates/{units\|employees}` | `org.import.manage` |
| POST | `/import/{units\|employees}?mode=validate\|commit` (multipart) | `org.import.manage` |

### Import formats
Header row cells are bilingual (`English / عربي`). Columns are matched by the stable key in row 2, which is hidden in the template.

| Kind | Columns |
|---|---|
| Units | `code`*, `parent_code`, `name_ar`, `name_en` (one required), `level_code`*, `head_employee_no`, `deputy_employee_no`, `cost_centre`, `active` (Y/N), `effective_from` |
| Employees | `employee_no`*, `name_ar`, `name_en` (one required), `email`*, `mobile`, `job_title_ar`, `job_title_en`, `grade`, `unit_code`*, `manager_employee_no`, `status`, `join_date`, `create_user` (Y/N) |

- Both kinds upsert: by `code` for units and by `employee_no` for employees.
- Parents and managers can appear anywhere in the same file.
- The report is a list of `{row, column, code, messageKey, params}`. It can be downloaded as .xlsx with an added errors column.

## Contracts for other modules
| Interface | Methods |
|---|---|
| `IOrgDirectory` | `GetUnitAsync`, `GetPathAsync(unitId, asOf)`, `GetDescendantIdsAsync(unitId)`, `GetHeadOfUnitAsync(unitId, asOf)` |
| `IEmployeeDirectory` | `GetAsync(id)`, `FindByEmailAsync`, `FindByEmployeeNoAsync`, `GetManagerAsync(employeeId, asOf)` |
| `IDelegationQuery` | `GetActiveDelegatesAsync(employeeId, date, scopeKey)` |

## Screens
| Screen | Contents |
|---|---|
| Organisation → Units | Tree with search; detail panel with Overview and History tabs; create / edit / move dialogs, each with an effective date |
| Organisation → Org chart | Top-down tree, as-of date picker, collapse/expand, mirrored in RTL |
| Organisation → Employees | List with filters and a detail page (photo, details, assignment history), create / edit dialog |
| Organisation → Levels | Editable list |
| Organisation → Import | Wizard: choose kind → download template → upload → validation report → commit |
| Profile → Delegations | My delegations: create, cancel |
