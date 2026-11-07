# 0168 — Responsibilities page per system: one contract, module-owned reassignment

- **Status:** Proposed (Phase 19, stream D2) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §D2: per system, an administrator assigns / reassigns who is responsible for updating records, singly or in bulk,
  with filters and a preview; each reassignment has a reason, notifies the old and the new person, is audited, and open approvals / tasks
  follow. The bulk *role* transfer (access.md) moves role assignments; ADR 0040 moves a leaver's tasks. Neither moves a responsibility that
  lives **on a record** (KPI card owner, risk owner, finding auditor…), and each module has its own rules for that field.

## Decision
- **Contract** in `Strategy.Platform.Access.Contracts` (`Responsibilities.cs`): `IResponsibilitySource` per (system, kind) with AR / EN labels,
  target (`employee` / `unit`), optional restricted candidates and status options; `ListAsync` (person / unit / status / text, paging),
  `EligibleAsync`, `PreviewAsync`, `ReassignAsync`. A base class `ResponsibilitySource<T>` holds the common checks, so modules write only
  load / describe / check / apply: records **outside the scope are reported as `notFound` without number or title** (404 semantics, nothing
  moves); the **row version** the page showed must still match (`changedMeanwhile`, never overwritten; a race at save time skips the whole
  batch); same person → `alreadyResponsible`; ineligible → `notEligible`.
- **Permission** `<system>.responsibilities.manage` (scopable; labels from `ResponsibilityPermissions.Definition`) declared by each module and
  seeded into its office / administrator role with scope All. Its **data scope** narrows the records listed and moved, through the module's
  read scope map, so a unit-level delegate manages their unit only. The platform service answers 403 unless the caller is an internal
  principal holding the permission of **that** system (a Risk administrator gets 403 on KPI); vendors are refused; unknown system / kind 404.
  The endpoints are `RequireSignedIn` with the check in the service, because the permission depends on the route's `{code}`.
- **Platform administrator:** where the system's administrator role is one of the platform administrator roles (Tasks, Teams) it holds the
  permission; for the other modules the platform administrator needs that system's office role anyway (system access, ADR 0016), so the
  permission is seeded in the office roles rather than in `access.administrator` (no module codes in the Access component).
- **Execution in the owning module, as the acting user**, with its own domain methods / commands in one transaction: KPI = a card definition
  change (new card version with the reason) + `OwnershipSync` of current and future plans / results; Risk / CAP / Benchmarking / Teams = the
  domain setter plus the module's record-role sync; Teams leader = the leader member is *replaced* by a successor with the same role (as *End
  membership* with a successor; history kept); Tasks = the existing `ReassignEmployeeTasks` command per current assignee (ADR 0040), with its
  own notices (the platform notices are not sent twice).
- **Portfolio PM / owner go through the change-request rule.** SPEC §1.9: the PM is "changeable only by approved Ownership change request".
  The bulk action creates **and submits** one Ownership change request per selected project with the reason (`ChangeRequestService`, the
  same permission / stage / "one approval at a time" checks); the preview says `needsChangeRequest`, the result reports `change-request` with
  the change request number. Nothing moves until it is approved and applied by the PM as today (ADR 0073).
- **Audit Management:** the responsible auditor of reports / findings may only be an **Audit Office member** (holders of the Audit Office or
  Chief Audit Executive role, directly or through a unit) — a server-side validation error otherwise; departments are not touched.
- **Re-routing:** new Workflow contract `IApprovalRerouting.RerouteAsync(ApprovalReroute, transactionContext)`: for the records' approvals in
  progress it updates the subject snapshot (owner / unit when given) and re-resolves the pending step when its rule depends on the record
  (record role, team role, section manager / department director / head of unit *of the record*), with the bulk transfer hook's removal /
  addition logic (approver rights, notifications, history `rerouted`); a step that would be left without approvers keeps its current ones.
- **Audit:** the platform sets the ambient `IAuditReason`, so every field change saved by the module carries the reason; one summary event
  `access.responsibilities.reassign` lists moved / change requests / skipped.
- **Notifications:** `access.responsibilityAssigned` (one summary to the new person) and `access.responsibilityRemoved` (one per previous
  person), bilingual values, seeded and editable in Settings → Notifications; queued in the module's transaction; a unit kind notifies the
  unit head.
- **Screen:** Settings → Responsibilities (`/settings/responsibilities?system=&kind=`), an entry in the Settings centre for any holder of a
  `<system>.responsibilities.manage` and a link on each system's settings page. Idempotency key on reassign.

## Consequences
- A new kind is one class in the owning module; the platform needs no change.
- Portfolio bulk PM changes need the normal approvals; a project with another approval running is blocked until it ends.
- Teams evaluator moves apply to open / returned evaluations only; the leader evaluator is not offered (rare, set per evaluation type).
