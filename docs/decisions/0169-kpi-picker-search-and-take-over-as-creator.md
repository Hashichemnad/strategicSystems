# 0169 — KPI picker search as you type; "take over as creator" for a leaver's tasks

- **Status:** Accepted (Phase 19, stream M)
- **Context:** SPEC-ADDENDUM A8 closes two known limitations: KPI pickers that showed only the first cards of a list (Portfolio's project
  KPI select showed 20, other pickers re-used the full card list), and ADR 0040's open point — tasks a leaver *created* for other people
  keep the leaver as creator and approver.

## Decision

### KPI picker
- One lookup endpoint in the KPI module, `GET /api/v1/kpi/cards/lookup` (`kpi.card.read`, data scope in the query, ≤ 50 per page, `ids=`
  to resolve the selected cards' labels), searching the code and both names with Arabic normalisation done in SQL with the same rules as
  `SearchText.Normalise` — no stored search column (KPI catalogues are small; no migration that other streams could collide with).
- One shared React component (`KpiCardPicker`) used by every screen that picks a card. Other modules keep reading cards through
  `IKpiCatalog` on the server; the picker talks only to the KPI module.

### Take over as creator
- Administrative path only, on the existing *Reassign open tasks of an employee* screen, with the same permission and data scope
  (`tasks.reassignment.manage` over the leaving employee): preview → select tasks → new creator → reason. Command
  `TransferCreatorRole` in the Tasks module: creator changed through the domain method, record roles re-granted, timeline event, audit event
  with the reason, notifications to the old and the new creator. Creator approval is task-level (ADR 0035), so open approvals follow the
  new creator.
- **The leaver's manager** gets the right through configuration, not code: grant `tasks.reassignment.manage` to the *Manager* role with
  data scope **Team** (direct reports). Default unchanged (Task administrator, All) — the safest option.
- **No self-service take-over** on the task page: follow-up people have comment / upload rights only, and an administrator role never
  approves tasks on its own (tasks design §6). Taking over someone's approval duty stays a reasoned administrative act.

## Consequences
- Tasks created by a leaver no longer wait for an approval nobody can give, once an administrator (or a manager with the configured right)
  moves them.
- Assignment groups keep their original creator for group actions (message all, extend all); their tasks' approval follows the task creator.
