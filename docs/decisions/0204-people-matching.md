# 0204 — Salesforce users and contacts are matched to employees

- **Status:** Proposed (Phase 23) — awaiting owner acceptance
- **Context:** §G2 "user → employee by e-mail / employee number / Federation ID". PMS people fields point at `Contact` (project manager, risk
  owner) and approver fields at `User`; an employee has one external reference per source and type (ADR 0164), so both cannot be
  `org.employee` references.

## Decision
- `User` and `Contact` are **people** object mappings that fill `integration.sync_person` (source, object, Salesforce Id → employee, matched by
  `email` · `employeeNo` · `federationId` · `manual`), in the configured order of match keys. People mappings do not create employees by default
  (employees are HR master data, Organisation import); `createUnmatchedEmployees` (off) creates them through the employee administration.
- `person` field mappings resolve through `sync_person`; an unknown or unmatched person → exception `unknownUser`; *Map person* sets a manual
  match and re-processes. The employee also gets an `org.employee` reference to the matched **User** (so global search finds it by Salesforce Id).

## Consequences
Matching is explainable (matched-by column) and correctable without touching Salesforce.
