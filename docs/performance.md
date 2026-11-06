# Performance report — version 1.0 (Phase 18)

Load test of the non-functional requirements (SPEC §1.10, §0.4, §0.2) against a **Release build** of the host on a database seeded with the
Phase 17 volume, with k6 (ADR 0158). All NFRs are met with a wide margin at 100 concurrent active users; the saturation point of the test
machine is shown under *Capacity*.

## Result

| NFR (SPEC) | Measured (page = all requests the SPA issues in parallel for it) | p50 | p95 | p99 | max | Target | Result |
|---|---|---|---|---|---|---|---|
| Record pages < 3 s (§1.10) | project record: project, milestones, health, change requests, current status — 100 users | 48 ms | 148 ms | 216 ms | 540 ms | p95 < 3 s | **Pass** |
|  | same, baseline (5 users in a closed loop, no think time) | 196 ms | 380 ms | 475 ms | 669 ms | p95 < 3 s | **Pass** |
| Gantt < 5 s for 30 milestones (§1.10) | project + Gantt data of the 30-milestone project (and of other projects) — 100 users | 38 ms | 137 ms | 221 ms | 486 ms | p95 < 5 s | **Pass** |
|  | same, baseline (30-milestone project only) | 207 ms | 397 ms | 510 ms | 608 ms | p95 < 5 s | **Pass** |
| Executive dashboard < 3 s on the Phase 17 volume (§0.4) | `executive/data?kind=year` — 100 users | 61 ms | 162 ms | 235 ms | 498 ms | p95 < 3 s | **Pass** |
|  | same, baseline (Director General scope All and a unit-scoped viewer, alternating) | 206 ms | 422 ms | 576 ms | 2.74 s¹ | p95 < 3 s | **Pass** |
| My Work | cards + approvals inbox — baseline | 376 ms | 651 ms | 733 ms | 888 ms | p95 < 3 s | **Pass** |
| List pages | projects, requests, KPI cards, risks, tasks, CAP remarks, findings (page of 25) — 100 users | 24 ms | 97 ms | 152 ms | 452 ms | p95 < 3 s | **Pass** |
| 500 concurrent active projects (§1.10) | 735 active projects (1 050 in total); 100 users browse random active projects for 4 min (55 695 requests) | — | — | — | — | no errors, timings above | **Pass** (0 errors) |
| Notifications within 5 minutes (§0.2) | task assigned → "New task" e-mail received by Mailpit, 10 times | 1.05 s | 1.07 s | — | 1.07 s | max < 5 min | **Pass** (0 lost) |

¹ First request of a viewer after start-up (the scope / cache warm-up); every later one is below 600 ms.

Error rate: **0 %** of 55 695 requests (100-user run) and 0 % of 89 858 requests (300-user run). Every request is checked for HTTP 200.

## Data volume

Seeded by `tests/Strategy.IntegrationTests/Performance/PerformanceVolumeSeed.cs` (the Phase 17 volume seeders, ADR 0158):

| Records | Count |
|---|---|
| Program requests / projects | 1 050 / 1 050 (735 active, 105 completed, 105 on hold, 105 cancelled) |
| Milestones | 5 per project, 30 for the Gantt project (5 275) |
| KPI cards / annual plans / results | 1 000 / 1 000 / ≈ 4 000 |
| Risks | 1 000 |
| Tasks | 10 000 |
| Audit findings (+ recommendations) | 2 000 |
| CAP remarks (+ actions) | 2 000 |
| Departments (units) / people | 12 / 12 + test users |
| Reporting facts | refreshed for the last 12 months before the run |

## Environment

- One virtual machine: 4 vCPU (Intel Xeon @ 2.30 GHz), 16 GB RAM, Linux; PostgreSQL 16 (local service, default configuration except
  `max_connections = 600`), the application host (`dotnet Strategy.Host.dll`, Release, .NET 10), Mailpit and the k6 container **on the same
  machine** (k6 itself uses ≈ 0.3 CPU).
- **Other workloads ran in parallel** on this machine during the measurements (other Phase 18 builds, integration tests and E2E runs; load
  average 5–26 on 4 CPUs). The numbers are therefore pessimistic for a dedicated server.
- Host settings for the test: `ASPNETCORE_ENVIRONMENT=Development` (module configuration of the development profile; the demo seed is
  off), `RateLimiting__PermitLimit=1000000`, `RateLimiting__AuthPermitLimit=100000` — **the rate limits were raised** because every virtual
  user comes from one IP address (production keeps 300 requests / minute per client IP). Connection pool `Maximum Pool Size=200`.
- Authentication: personal access tokens (`api.read` / `api.write`) of a Director General (scope All in every system, executive viewer) and a
  unit-scoped executive viewer — the same access service and scope filtering as the UI. Times are API times (server + network on localhost);
  browser rendering is covered by the E2E suite.

## Capacity (300 users)

A second run with 300 concurrent users (same mix, 1–3 s think time) completed with **0 errors** at 174 requests / s, but the host used all
4 CPUs and the p95 of the record page rose to 3.7 s, executive dashboard 3.9 s, Gantt 3.7 s (p50 ≈ 1.9 s), lists 2.6 s. Individually, warm
requests take 7–40 ms (project 15 ms, Gantt 7 ms, executive dashboard 32 ms, My Work cards 37 ms); the limit is CPU throughput of the shared
4-vCPU machine, not a slow query. **Sizing guidance** (administrator / installation guide): 4 vCPU / 8 GB for up to ≈ 150 concurrently active
users; 8 vCPU / 16 GB (application and database on separate machines recommended) beyond that.

## Tuning

None was needed: every NFR passed on the first run. Checked while analysing the 300-user run: no request is slow on its own (above), the
database CPU stays low (≈ 10 % per backend) while the application process is CPU-bound, personal access tokens update `last_used_at` at most
once per minute, the executive dashboard reads cached facts (ADR 0141).

## How to re-run

```bash
# Prerequisites: Docker (k6 and Mailpit images), PostgreSQL 16 reachable with an admin login, .NET 10 SDK, psql.
REGISTRY_PREFIX=mirror.gcr.io/ PERF_ADMIN_CONNECTION="Host=localhost;Port=5432;Username=postgres;Password=postgres" tests/perf/run.sh
LOAD_VUS=300 tests/perf/run.sh          # capacity run
KEEP=1 tests/perf/run.sh                # keep the database, Mailpit and the host afterwards
```

`run.sh` drops and re-creates the database `strategy_perf`, seeds it (≈ 30 s), starts the Release host on port 5197, runs `notify.js`
(notification latency) and `nfr.js` (≈ 9 min: 2 min baseline of each NFR, then a ramp to `LOAD_VUS` users for 4 min) and writes the k6
output and summaries to `tests/perf/results/` (not committed). The k6 thresholds fail the run when an NFR is missed.

## Known limits of the test

- API timings, not browser timings (rendering of the SPA is covered by the E2E suite).
- Single machine for application, database and load generator; a dedicated customer server will do better.
- The seeded data is written by the volume seeders (binary COPY, ADR 0158), so it carries no audit history; History tabs were not part of
  the measured pages.
