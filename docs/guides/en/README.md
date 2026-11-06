# Integrated Strategy System — guides (English)

Version 1.0. The same guides exist in Arabic: [`../ar/README.md`](../ar/README.md).

## For everyone

| Guide | Who it is for |
|---|---|
| [My Work, approvals, notifications and your profile](user-my-work.md) | every user: home page, approvals and delegation, notifications, language, two-step verification, access tokens |
| [Dashboards and reports](user-dashboards.md) | everyone who reads dashboards; report authors; executives |

## Systems

| Guide | System (sidebar) |
|---|---|
| [Portfolio Management](user-portfolio.md) | *Program requests*, *Projects & initiatives*, *Portfolio dashboard*, *Portfolio Gantt* — requesters, project managers, PM Office |
| [Wasel vendor portal](user-wasel.md) | vendors (supplier users) and the project managers who work with them |
| [Performance Management (KPI)](user-kpi.md) | *Performance* — KPI owners, Strategy Office |
| [Risk Management](user-risk.md) | *Risk Management* — risk owners, action owners, Risk Office, Risk Committee |
| [Teams and committees](user-teams.md) | *Teams & committees* — requesters, leaders, secretaries, members, HR |
| [Task Management](user-tasks.md) | *My tasks* — everyone |
| [Requests, Document Registry and Intellectual Property](user-requests.md) | *Requests*, *Document library*, *Intellectual property* |
| [Benchmarking](user-benchmarking.md) | *Benchmarking* — coordinators, PM Office |
| [Corrective Action Plan](user-cap.md) | *Corrective actions* — remark owners, action assignees, PM Office |
| [Audit Management](user-audit.md) | *Audit management* — Audit Office, departments, audit committee |

## For administrators

| Guide | Contents |
|---|---|
| [Administrator guide](administration.md) | first-run setup wizard, every Settings area, roles and permissions, approvals, business hours, AI and MCP, identity providers, audit log, health page |
| [Connecting Claude and other MCP clients](mcp.md) | Claude Desktop, claude.ai, Claude Code, other clients; OAuth and personal access tokens |
| Installation guide | written with the installer (see [`deploy/README.md`](../../../deploy/README.md) until then) |

Reference documents: [identity providers](../../identity-providers.md) · [SPEC](../../SPEC.md) · [design notes](../../design/).

## Conventions in these guides

- Labels in **bold** are the exact names on the screen. Paths such as **Settings → Approvals** mean: open **Settings** from the profile
  menu, then the page in the left-hand settings navigation.
- What you see depends on your roles. The sidebar shows only the systems you may open, and each record shows only the actions you may
  take now (the *current status* banner). If something described here is missing for you, you do not hold the role — ask your
  administrator.
- Approval chains, lists, numbering formats, deadlines and thresholds are configured by each organisation. The guides give the values
  that ship with the product (SPEC Appendix A); your installation may differ.
- Screenshots come from the automated end-to-end runs (demo organisation) in `docs/screenshots/phase-*/`.
