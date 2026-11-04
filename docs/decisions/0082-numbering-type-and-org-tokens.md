# 0082 — Numbering tokens `{TYPE}` and `{ORG}`; Document ID and version numbers

- **Status:** Accepted by the owner (Phase 14, before the pull request)
- **Context:** SPEC §6.2.3: Document ID default `{ORG}-{UNITCODE}-{TYPECODE}-{YYYY}-{NN}` (e.g. `DOF-LG-LEG-2026-01`); SPEC §5.1: each request
  type has a numbering format. The numbering service (ADR 0017) knows `{YYYY} {YY} {MM} {UNIT} {SEQ:n}` only.

## Decision
- Two new tokens, values supplied by the caller in `NumberingContext` (additive optional parameters): `{TYPE}` (a type code: the document
  type's numbering code, the request type's prefix) and `{ORG}` (organisation code from the module's settings, default `ORG`). As for
  `{UNIT}`, a saved pattern may use them only when the default pattern does. The counter partition is still the rendered prefix, so each
  type / unit / year has its own sequence.
- Formats: `requests.request` `{TYPE}-{YYYY}-{SEQ:4}`, `docreg.document` `{ORG}-{UNIT}-{TYPE}-{YYYY}-{SEQ:2}`, `ip.record`
  `IP-Registry-{SEQ:4}`, `ip.internal` `IP-INT-{YYYY}-{SEQ:4}`.
- Document **version** is `major.minor`: the first approved version is 1.0; a new version request gives the next major (2.0); the office's
  correction republish (no new review) gives the next minor. A new version keeps the Document ID. Numbers are generated on approval inside
  the approval transaction.

## Consequences
Unit codes are the Organisation module's unit codes. Changing a document type's code later does not renumber existing documents.
