# 0214 — Every sync target can carry external references

- **Status:** Proposed (release 1.0.1) — awaiting owner acceptance
- **Context:** The sync writes each record together with its external reference (the Salesforce Id, ADR 0200 / 0202). The reference store
  accepts only types of the Links registry or registered referenceable types (ADR 0164). `portfolio.project-risk` is a sync target but
  neither, so every project risk failed with `integration.errors.unknownType` ("write failed"). The 1.0.1 tests of the risk picklists found it.

## Decision
- The external reference store treats every registered **sync target** code as referenceable.
- Visibility does not change: referenced records are still resolved only through the Links registry or a registered referenceable source;
  other types resolve to nothing (hidden), as before.
- An integration test fails when any registered sync target is not referenceable.

## Consequences
- Project risks (and any future child target outside the Links registry) sync with their Salesforce Id; the record page shows the reference
  where the record's own page lists references.
