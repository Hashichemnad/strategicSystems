# 0153 — Seed packs: neutral defaults and a removable demo pack

- **Status:** Proposed (Phase 18)
- **Context:** SPEC §0.0 (re-usable product, nothing organisation-specific in code), CLAUDE.md §2.2, §6 (no hard deletes of business
  records), BUILD_PLAN Phase 18 "seed packs (neutral defaults; optional demo pack)". Sales presentations and training need a filled
  installation in minutes; customers must never get demo data by accident, and removing it must not break the audit trail.

## Decision
- **Neutral default pack = the existing seeders.** Every component and module seeds its catalogue on start (permissions with AR / EN labels,
  default roles and role → permission maps, lookups, numbering formats, notification templates, approval process definitions, Appendix A
  defaults); they insert missing codes only and never overwrite an administrator's edit. A fresh installation has **no** units, employees,
  users or records — proven by `DemoPackTests.A_fresh_installation_contains_the_neutral_default_pack_and_no_organisation_data`.
- **Demo pack = contributors.** `IDemoPackContributor` (Kernel) with `Code`, `Order`, `LoadAsync`, `RemoveAsync`, registered by each module
  in its own `Demo/` folder (no cross-module references: links between systems go by tracked keys, e.g. the Portfolio request uses the KPI
  demo objective's code). The platform contributor (Host) creates the demo organisation (units `DEMO-*`), eight demo employees and users
  (`<key>@demo.example.org`, password chosen at load time and checked against the password policy, system access to every system,
  office roles per person).
- **Through the modules' own commands, as a person.** Each step runs in its own service scope through the dispatcher or the module's
  service, acting as a demo person (`DemoPackContext.ActAsAsync`): the same validation, record-level access, approval routing (nobody approves
  their own request) and audit as in the UI. Channel `import`, audit reason `demo pack`; `demo.pack.load` / `demo.pack.remove` events are
  audited explicitly.
- **Tracked.** Every created record is written to `core.demo_record` (contributor, resource type and id, optional key, the person who
  removes it, pack version, removed-at). A second load creates nothing (contributors already loaded are *skipped*).
- **Removal without hard deletes.** `RemoveAsync` cancels, withdraws, closes or deactivates each record with the reason "Demo data
  removed" through the normal transitions (risks closed through the closure approval, requests cancelled by their requester or the office,
  KPI results recalled and cards / objectives deactivated, audit findings and the report cancelled, CAP remarks cancelled and the source
  closed, teams cancelled / closed, tasks cancelled); demo users are deactivated and locked, employees set to *left*, units deactivated
  (children first). Nothing is deleted; the audit trail keeps the whole history.
- **Entry points.** `--seed-demo --password-file <f|/dev/stdin>` and `--remove-demo` (after `--migrate`); `POST` / `DELETE
  /api/v1/setup/demo-pack` and `GET /api/v1/setup/demo-pack/status` (permission `settings.setup.manage`, ADR 0150); the set-up wizard's
  optional demo step (it probes `GET /api/v1/setup/demo-pack`, which answers 405 when the pack is installed); Settings → *Demo data*.
- **Failures are per contributor.** A failing contributor is reported (`failed` + error code, logged) and the others continue; a later
  load retries only contributors that are not loaded.

## Consequences
- Demo data looks real (approval queues, My Work, dashboards) because it went through the real workflows; the demo is slower to load than a
  bulk insert (about 30 s on the test machine).
- Removed demo records stay visible as cancelled / closed / inactive items (filterable), by design (CLAUDE.md §6). An installation that
  must start truly empty is reinstalled instead of loading the demo pack; the Settings page warns not to load it on production data.
- Module changes to commands or rules can break a contributor; `DemoPackTests` loads and removes the whole pack in CI.
