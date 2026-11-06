# 0158 — Load testing with k6 against a seeded Release host

- **Status:** Accepted by the owner (Phase 18)
- **Context:** Phase 18 must prove the NFRs on realistic volume: record pages < 3 s (SPEC §1.10), Gantt < 5 s for 30 milestones, executive
  dashboard < 3 s on the Phase 17 volume (§0.4), 500 concurrent active projects, notifications delivered within 5 minutes (§0.2). The Phase 17
  test (`ExecutivePerformanceTests`) times a single user inside `WebApplicationFactory`; it does not load a real host with concurrent users.
  The build plan allows k6 or NBomber. NBomber needs a commercial licence for organisations; k6 is AGPL-3.0.

## Decision
- **k6** (Grafana, AGPL-3.0) runs only as an external container (`grafana/k6`) on the test machine. It is a test tool: never a dependency of
  the product, never in the image, never distributed with the release — so the AGPL does not reach the product (CLAUDE.md §4.3 lists the
  allowed licences of *product* dependencies; this ADR records the exception for a test-only container, like Testcontainers images).
- **Data**: the Phase 17 volume seeders stay in the integration-test project (they write with binary COPY, which is acceptable only for a
  throw-away test database, never a business path — so they must not become a host command). A runner test
  `Performance/PerformanceVolumeSeed` (a no-op unless `STRATEGY_PERF_DATABASE` / `STRATEGY_PERF_OUTPUT` are set) seeds a named database:
  1 050 projects (735 active), one with 30 milestones, 1 000 KPI cards with results, 1 000 risks, 10 000 tasks, 2 000 findings, 2 000 remarks;
  it refreshes the reporting facts, points SMTP at Mailpit and writes personal access tokens (Director General scope All; a unit-scoped
  executive viewer) and record ids to JSON. No `--seed-volume` host command is added (the design note's option was dropped for this reason).
- **Run**: `tests/perf/run.sh` — seed, start the **Release** host (Development environment for module configuration; rate limits raised
  because every virtual user comes from one IP), run `notify.js` and `nfr.js`, keep the summaries in `tests/perf/results/` (ignored by git;
  the report is `docs/performance.md`). Authentication with personal access tokens (`api.read`, `api.write`) — the same access service as the UI.
- **Pages** are measured as the SPA loads them: the requests a page issues in parallel (`http.batch`), time until the last answers.

## Consequences
- Re-runnable by the owner on target hardware; thresholds fail the run when an NFR is missed.
- k6 must be pulled (or mirrored) on the test machine; nothing changes for installations.
- Browser rendering time is not included (API timings); the SPA's own work is measured by the E2E suite.
