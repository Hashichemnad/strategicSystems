# 0126 — Benchmarking: Plan on the Request Framework, four stages with PMO gates, access

- **Status:** Proposed (Phase 15)
- **Context:** SPEC §5 / ADR 0080: Benchmarking is a request → review → process → register system on the shared framework; SPEC §8.2: four
  stages Plan → Search → Acquire → Analyze with PMO gates; §5–8 decision: single PMO step by default.

## Decision
- The **Plan** is the built-in request type `bench.request` (catalogue, My Requests, form builder extra fields, approval stage `bench.plan`
  default PM Office). Its handler creates the study `BMS-NNNN` on submission (subject of the request), moves it to Search on approval,
  marks it returned / cancelled on return / rejection.
- Later gates are approvals on the study: `bench.search`, `bench.analysis`, `bench.closure` (default PM Office, role `bench.bench-office`).
  Acquire → Analysis needs no approval (SPEC: "sent to the concerned unit for the next step").
- Explicit stage table, one pending process at a time. Roles `bench-office` (All, MFA), `coordinator` (unit and below), `participant`
  (everyone, own + plan / library / register read), `viewer`; record roles `coordinator`, `topic-owner`, `participant` on the study.

## Consequences
New study requests appear in the same catalogue as documents and IP; extra plan fields are configuration.
- The office that decides the Plan also holds the Requests office role (`requests.office`), as for the Document Registry and IP: the
  approver record role on a request ends with the task, and the office keeps reading the benchmarking requests afterwards.
