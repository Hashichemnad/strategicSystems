# Seed packs — neutral defaults and the optional demo pack

Decision: [ADR 0153](../decisions/0153-seed-packs-and-removable-demo-pack.md).

## 1. Neutral default pack (every installation)

Applied automatically on every start (after `--migrate`); it only inserts what is missing and never overwrites an administrator's edit.
It contains **no organisation data** — no units, employees, users or business records.

| Content | Where it comes from | Edit in |
|---|---|---|
| Systems and the permission catalogue (AR / EN labels) | each component's / module's access catalogue | Settings → Roles (permission matrix) |
| Default roles and role → permission maps, MFA-required roles | same | Settings → Roles, Role assignments |
| Lists (lookups) with AR / EN labels | each module's lookup definitions | Settings → Lists |
| Numbering formats | each module's numbering definitions | Settings → Numbering |
| Notification templates (AR / EN subject and body) | each module's templates | Settings → Notifications |
| Approval processes and their default steps (Appendix A) | each module's approval process definitions | Settings → Approvals |
| Typed settings defaults (Appendix A: thresholds, windows, SLAs …) | the settings records' defaults | each system's Settings page |
| Feature switches | feature definitions | Settings → Features |
| Scheduled jobs and their default schedules | job definitions | Settings → Scheduled jobs |

The first administrator is created by the set-up wizard (ADR 0150) or `--create-admin`; the organisation structure comes from the wizard's
import step or Settings → Import.

## 2. Demo pack (optional, for presentations and training)

> Do **not** load the demo pack on an installation that holds real data. It is meant for a separate demonstration or training installation.

### What it creates

| System | Demo data |
|---|---|
| Organisation and users | Demo organisation `DEMO-ORG` with a sector, the Strategy & Performance, Finance and Internal Audit departments and a PMO section; eight demo people with accounts (below) |
| Task Management | five tasks from the Director General and the Strategy Director to their staff (one overdue, one in progress, one group task) |
| Teams Management | a standing committee and a work team awaiting formation approval, a draft team |
| Performance Management | two demo objectives, four quarterly KPI cards with this year's plans and results (approved for past quarters, the latest pending approval) |
| Risk Management | six risks across the departments: four registered, two awaiting the Risk Office |
| Portfolio Management | a program request in approval and a draft request |
| Document Registry / IP / Benchmarking | a document submission with its draft file, an IP registration request and a benchmarking study request (in review), a draft document request |
| Corrective Action Plan | a workshop source with four remarks (two accepted by their owners) |
| Audit Management | an issued internal-audit report with four findings and recommendations (one overdue) |

All records are created through the systems' own commands as the demo people (approval chains, notifications and the audit trail run as in
real use; audit channel `import`, reason "demo pack"), so My Work, approval queues and dashboards are filled.

### Demo people

All sign in with the password chosen when loading the pack. Office roles require two-step verification, set up at the first sign-in.

| E-mail | Person | Main roles |
|---|---|---|
| `director@demo.example.org` | Sara Al Mansoori — Director General (head of the organisation) | the office and administrator roles of every system, Risk and Audit committees, executive dashboard, report author |
| `strategyhead@demo.example.org` | Khalid Al Shamsi — Strategy Director | head of Strategy & Performance, Strategy Office (KPI), risk coordinator |
| `pm@demo.example.org` | Layla Al Hosani — Project Manager | PM Office, requester, team leader |
| `analyst@demo.example.org` | Omar Al Kaabi — Performance Analyst | KPI owner, requester, task assignee |
| `financehead@demo.example.org` | Noura Al Dhaheri — Finance Director | head of Finance, budget section, risk coordinator |
| `accountant@demo.example.org` | Yousef Al Nuaimi — Accountant | KPI / risk owner, task assignee |
| `audithead@demo.example.org` | Maryam Al Balushi — Chief Audit Executive | head of Internal Audit, Audit Office, risk coordinator |
| `auditor@demo.example.org` | Hamad Al Muhairi — Internal Auditor | Audit Office |

Every demo person may open every system; what they see inside follows their roles and data scope as for real users.

### Load

* **Set-up wizard**: the optional *Demo data* step (password for the demo users).
* **Settings → Demo data** (permission `settings.setup.manage`): enter the password, *Load demo data*; the table shows the records per system.
* **Command line** (after `--migrate`), password on standard input:

  ```bash
  read -rsp 'Demo users password: ' DEMO_PASSWORD && echo
  printf '%s' "$DEMO_PASSWORD" | docker compose -f deploy/docker-compose.yml --profile app run --rm -T app --seed-demo --password-file /dev/stdin
  unset DEMO_PASSWORD
  ```

  Outside Docker: `dotnet Strategy.Host.dll --seed-demo --password-file /dev/stdin`. Exit code 0 = loaded, 1 = a system failed (see the
  log; run again to retry only the failed systems), 2 = usage / password policy, 3 = database not migrated.

Loading twice changes nothing.

### Remove

* **Settings → Demo data** → *Remove demo data*, or `--remove-demo` on the command line.
* Every demo record is cancelled, withdrawn, closed or deactivated with the reason "Demo data removed" through its normal transition; demo
  users are deactivated and locked, demo employees set to *left*, demo units deactivated. **Nothing is deleted** (CLAUDE.md §6): removed
  items stay visible as cancelled / inactive records and in the audit trail. Removing twice changes nothing.
* For an installation that must be completely empty, reinstall on a new database instead.
