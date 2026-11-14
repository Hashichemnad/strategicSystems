# 0218 — One Salesforce record → a Request Framework request and its registry record

- **Status:** Proposed (release 1.0.2) — awaiting owner acceptance
- **Context:** In Salesforce, `BMS_Process__c`, `Central_Registry_of_Documents__c` and `Intellectual_Property_Registry__c` are each a single
  record. That record carries both the request (approval status, request status, stage, requester, cancel reason) and the register entry
  (study, document, IP record). In this system the request is a Request Framework request (SPEC §5) and the register entry is a record of
  Benchmarking, the Document Registry or the IP Registry, linked through `RequestId` and `SubjectType` / `SubjectId`. Before this release the
  Requests module had no sync target.

## Decision
- **New sync target `requests.request`** in the Requests module, written by a custom writer:
  - It sets type, stage, status, outcome, requester, unit, dates and `source = sync`.
  - It does not run type handlers, approvals or notifications (ADR 0202).
  - Request numbers are labels (ADR 0220).
- **Pack:** the source object is mapped twice with the `{Id}` template.
  1. `<system>-request` → `requests.request`, at a lower load order.
  2. `<system>-<record>` → the registry target, whose `requestId` is a `reference` to `requests.request` on the same `Id`.

  A hook of the registry target sets the request's `SubjectType` / `SubjectId` in the same transaction.
- **Approval history:** `ProcessInstance` source history attaches to the request.
- **Files:** files go to the registry record. The mapping option `"fileOwner": true` names the owner. When several records share one
  Salesforce Id, the owner choice is deterministic, which replaces the arbitrary first match of 1.0.1.

## Consequences
Request pages show the Salesforce approval history and status. Registry pages show their documents and link to the request, as for records
created here.
