# Design note — Members and Responsibilities per system (SPEC-ADDENDUM §D)

## §1 Members — see stream D1

## §2 Responsibilities (SPEC-ADDENDUM §D2, ADR 0168)

**Purpose.** System → Settings → Responsibilities: assign / reassign who is responsible for updating records, singly or in bulk, with
filters, a preview and a reason. It complements the bulk *role* transfer (access.md, roles belong to systems) and the Tasks *Reassign open
tasks of an employee* screen (ADR 0040, tasks belong to people): here the responsibility lives **on the record** and the owning module moves it.

### Contract (`Strategy.Platform.Access.Contracts/Responsibilities.cs`)
| Type | Role |
|---|---|
| `IResponsibilitySource` | one per (system, kind), implemented and registered by the owning module (`AddResponsibilitySource<T>()`) |
| `ResponsibilityKindDefinition` | code, AR / EN label, target (`employee` / `unit`), restricted candidates?, note key, status options |
| `ListAsync(ResponsibilityQuery)` | current person / unit / status / text filters, paging; scoped by `<system>.responsibilities.manage` **in the database query** |
| `EligibleAsync` | the employees who may take it over (null = anyone): Audit = Audit Office members |
| `PreviewAsync` / `ReassignAsync(ResponsibilityChange)` | selected `{id, version}` items, new person / unit id, reason |
| `ResponsibilitySource<T>` | base class: loads the selected records **within scope**, checks the row version (changed meanwhile → skipped), same person, eligibility, then the module's own `CheckAsync` and `ApplyAsync` |
| `IResponsibilityNotifier` | platform notices to the previous and the new person (unit → its head), queued in the module's transaction |
| `IApprovalRerouting` (Workflow contracts) | the pending steps of the records' approvals follow the new people (record role, team role and "of the record" org rules) |

Outcomes: preview `allowed` · `change-request` · `blocked` (message key); reassignment `moved` · `change-request` · `skipped` (message key).
Ids outside the scope come back as `responsibilities.outcome.notFound` without number / title (404 semantics, nothing moved).

### Platform service and endpoints (`Strategy.Platform.Access`, `/api/v1/access/systems/{code}/responsibilities`)
| Method & path | Notes |
|---|---|
| `GET /` | kinds of the system (`ListResponsibilityKinds`) |
| `GET /{kind}?personId=&unitId=&status=&text=&page=&pageSize=` | rows with names of the responsible person and unit |
| `GET /{kind}/candidates?search=` | eligible people of a restricted kind (empty otherwise: the UI uses the employee picker) |
| `POST /{kind}/preview` | `{items:[{id,version}], newId, reason}` → outcome per record |
| `POST /{kind}/reassign` | same body, `Idempotency-Key` supported → `{moved, changeRequests, skipped, new, items}` |

Every call: signed-in **internal** user holding `<system>.responsibilities.manage` of **that** system (403 otherwise; vendors 403;
unknown system or kind 404). Validation: 1–200 items, reason required (≤ 1000), new person active employee (unit: active unit), restricted
kinds: eligible person (`responsibilities.outcome.notEligible` validation error). The reason is the ambient `IAuditReason` of every field
change the module saves; one audit event `access.responsibilities.reassign` summarises moved / change requests / skipped.

### Permission
`<system>.responsibilities.manage` (scopable, AR / EN labels from `ResponsibilityPermissions.Definition`) declared by each module catalogue
and seeded into the system's office / administrator role with scope All (KPI administrator + Strategy Office, Risk Office, PM Office +
Portfolio administrator, CAP Office, Audit Office, Benchmarking Office, Teams administrator, Task administrator). Its data scope narrows the
records listed and moved (owner / unit of the record, as the module's read scope map). A delegated system administrator who holds only this
permission reaches Settings → Responsibilities and nothing else of Settings.

### Kinds per system
| System | Kind | Record / field | How it moves |
|---|---|---|---|
| kpi | `card-person` · `card-unit` | KPI card responsible employee / unit | card definition change (new card version with the reason) + `OwnershipSync` of current / future plans and results; result approvals in progress re-routed with the new owner / unit |
| risk | `risk-owner` · `action-owner` · `control-owner` | risk owner; treatment action owner; control owner | domain setters, `RiskRoleSync` (action / control owner record roles), risk approvals re-routed |
| portfolio | `project-manager` · `project-owner` | project PM / owner | **through an Ownership change request** (SPEC §1.9: "changeable only by approved Ownership change request"): one change request per project created **and submitted** (approval chain `portfolio.change-request`); applied by the PM after approval as today. Preview says `needsChangeRequest`; a project with an approval running or not live is blocked |
| portfolio | `story-assignee` | user story assignee (Agile) | direct |
| cap | `remark-owner` · `action-assignee` | remark owner; action assignee | `Remark.Reassign`, `CapAction.Reassign`, `CapService.SyncRolesAsync`, approvals re-routed |
| auditmgmt | `report-lead-auditor` · `finding-auditor` | report lead auditor; finding responsible auditor | **Audit Office members only** (holders of `auditmgmt.audit-office`, server-side); departments untouched |
| bench | `study-coordinator` | study responsible employee (coordinator) | `BenchRoles.SyncAsync`, approvals re-routed |
| teams | `team-leader` · `evaluator` | the leader member of an operating team; the evaluator of an open / returned evaluation | leader: membership replaced by a successor with the same role (as *End membership* with successor), `TeamRoleSync`; evaluator: record role moved; approvals re-routed |
| tasks | `task-assignee` | open task assignee | the existing `ReassignEmployeeTasks` command per current assignee (ADR 0040: deadlines, timeline, record roles, its own notices) — needs `tasks.reassignment.manage` too |

Notifications: `access.responsibilityAssigned` (new person, one summary) and `access.responsibilityRemoved` (each previous person, one
summary), bilingual, seeded, editable. Tasks keeps its own reassignment notices (no duplicate). Portfolio change requests notify through the
change-request templates; nothing is "moved" until applied.

### Screen
Settings → Responsibilities (`/settings/responsibilities/$system`), also linked from each system's settings page: system tabs (systems the
user manages), kind selector, filters (current person, unit, status, text), selectable table with select-all-on-page, new person picker
(eligible people only for restricted kinds) or unit picker, reason (required), **Preview** (allowed / change request / blocked per row),
**Confirm**, result summary (moved / change requests / skipped with reasons). ar + en, RTL, 360 px.
