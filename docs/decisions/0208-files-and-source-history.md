# 0208 — Salesforce files into the Files service; approval and field history as read-only source history

- **Status:** Proposed (Phase 23) — awaiting owner acceptance
- **Context:** §G3: `ContentVersion` / `ContentDocumentLink` / `Attachment` (files), `ProcessInstance` / `ProcessInstanceStep` (approval
  history, read-only), `*__History` (field history, read-only "source history" tab).

## Decision
- Files: links queried per batch of synced record Ids (`LinkedEntityId IN (…)`), latest `ContentVersion.VersionData` uploaded through
  `IFileService` on the owner type's attachment purpose (allow-list, magic number, size, optional scan as for any upload); reference
  `files.file` → `ContentDocumentId` (or Attachment Id); new Salesforce versions add file versions. Refused files → `fileRejected` exception.
- Approval steps and field-history rows go to `integration.source_history` (unique per source row), shown read-only in the record's
  External references section ("Source history"), never turned into live approval instances or audit entries of the record.

## Consequences
History from Salesforce stays separate from this system's own History tab (which shows the audited changes made here, including the sync's).
