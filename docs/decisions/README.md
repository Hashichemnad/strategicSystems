# Architecture decision records

One file per decision, `NNNN-title.md`, numbered in order. Status: Proposed · Accepted · Superseded by NNNN.
Write an ADR for every choice the SPEC or CLAUDE.md leaves open (CLAUDE.md §2 rule 9) and list it in the phase summary.

| # | Decision | Status |
|---|---|---|
| [0001](0001-minimal-apis.md) | Endpoint style: Minimal APIs grouped per module | Accepted |
| [0002](0002-tanstack-router.md) | Front-end router: TanStack Router | Accepted |
| [0003](0003-in-house-dispatcher.md) | In-house command / query dispatcher | Accepted |
| [0004](0004-openapi-ts-client.md) | OpenAPI → TypeScript client pipeline | Accepted |
| [0005](0005-phase-0-access-placeholder.md) | Phase 0 access placeholder that fails closed | Superseded by 0009 (Phase 1) |
| [0006](0006-module-pattern.md) | Module pattern, persistence and extra platform projects | Accepted |
| [0007](0007-licence-notes.md) | Licence notes: ClamAV sidecar and dev-only tooling licences | Accepted |
| [0008](0008-test-and-tooling-versions.md) | Test runner and front-end tool versions | Accepted |
| [0009](0009-interim-system-admin-access.md) | Interim access: signed-in users and a system-administrator flag | Superseded by 0016 (Phase 2) |
| [0010](0010-platform-components-and-identity-schema.md) | Platform components with endpoints, and the `identity` schema | Accepted |
| [0011](0011-runtime-identity-providers.md) | Runtime-configured identity providers and account linking | Accepted |
| [0012](0012-data-protection-and-secrets.md) | Data Protection key storage and secret encryption | Accepted |
| [0013](0013-org-history-model.md) | Organisation history: effective-dated versions and a materialised path | Accepted |
| [0014](0014-interim-placements-phase-1.md) | Interim placements until later platform services exist | Closed (grade and settings screens in Phase 2; photo, auth events, e-mail and directory sync in Phase 3) |
| [0015](0015-data-scope-semantics.md) | Data scope semantics and how scopes reach the database | Accepted |
| [0016](0016-roles-replace-interim-admin.md) | Roles replace the interim system-administrator flag | Accepted |
| [0017](0017-settings-storage-and-numbering.md) | Settings storage, numbering counters and interim branding assets | Accepted (interim logo table closed in Phase 3, ADR 0019) |
| [0018](0018-audit-trail-hash-chain.md) | Audit trail: storage, hash chain, append-only database role | Accepted |
| [0019](0019-files-service.md) | Files service: purposes, storage, checks and owner access | Accepted |
| [0020](0020-notifications-outbox-and-jobs.md) | Notifications, outbox and scheduled jobs | Accepted |
| [0021](0021-pdf-rendering.md) | PDF rendering through Gotenberg with embedded Arabic fonts | Accepted |
| [0022](0022-expression-sandbox.md) | Shared sandboxed expression service (in-house) | Accepted |
| [0023](0023-shared-transaction-across-contexts.md) | One transaction across a module's DbContext and the approval engine | Accepted |
| [0024](0024-approval-process-versions.md) | Approval processes: code-registered keys, database-versioned steps | Accepted |
| [0025](0025-approver-rules-and-no-approver.md) | Approver rule semantics, self-approval and steps without an approver | Accepted |
| [0026](0026-idempotency-keys.md) | Idempotency keys on submit and approval endpoints | Accepted |
| [0027](0027-business-hours-and-holidays.md) | Business hours, holidays and working-time SLAs | Accepted |
| [0028](0028-ai-provider-registry-and-settings.md) | AI provider registry, feature routing, per-role access and limits | Accepted |
| [0029](0029-prompts-output-validation-and-injection-defences.md) | Prompt templates, output validation, prompt-injection defences and masking | Accepted |
| [0030](0030-document-text-extraction-and-similar-records.md) | Document text extraction and similar records without embeddings | Accepted |
| [0031](0031-tool-registry.md) | Tool registry shared by the MCP server and "Ask the system" | Accepted |
| [0032](0032-mcp-server-and-oauth.md) | MCP server, OAuth 2.1 with OpenIddict, dynamic client registration and consent | Accepted |
| [0033](0033-ocr-for-scanned-documents.md) | Text recognition (OCR) for scanned PDFs and images with Tesseract | Accepted (one licence point for owner confirmation) |
| [0034](0034-links-registry.md) | Links registry: linkable types, permission-aware resolution and link storage | Accepted |
| [0035](0035-task-approval-by-creator.md) | Task approval by the creator is a task-level decision, not an approval-engine process | Accepted |
| [0036](0036-task-workflow-templates.md) | Task workflow templates, status codes, and Tasks as an always-on core system | Accepted |
| [0037](0037-task-deadlines-and-reminders.md) | Task due dates, reminders and escalation in working time | Accepted |
| [0038](0038-task-dependencies-and-recurrence.md) | Task dependencies and recurrence | Accepted |
| [0039](0039-confidential-tasks.md) | Confidential tasks and one visibility rule for every read path | Accepted |
| [0040](0040-reassign-open-tasks-of-an-employee.md) | Reassigning the open tasks of an employee who leaves or moves | Accepted |
| [0041](0041-teams-configuration-and-versioning.md) | Teams configuration: module-owned tables, versioned templates and reward matrices | Accepted by the owner |
| [0042](0042-evaluation-results-visibility.md) | Who sees team evaluation results | Accepted by the owner |
| [0043](0043-integration-events-outbox.md) | Integration events through the outbox; linked-record closure | Accepted by the owner |
| [0044](0044-formation-memo-certificates-org-head.md) | Formation memo, certificates and the "head of the organisation" approver | Accepted by the owner |
| [0045](0045-suggested-levels-and-minutes-delay.md) | Suggested criterion levels and minutes delay | Accepted by the owner |
| [0046](0046-team-lifecycle.md) | Team lifecycle | Accepted by the owner |
| [0047](0047-program-request-validation.md) | Program request validation: what applies when, and the "other" convention | Proposed |
| [0048](0048-request-approval-chain-and-approved-budget.md) | Request approval chain, status per step and the Approved Budget | Proposed |
| [0049](0049-project-auto-creation-and-numbering.md) | Automatic Project / Initiative creation and `PROJ-…` numbering | Proposed |
| [0050](0050-proposal-assessment-and-dynamic-ai-choices.md) | Proposal Assessment versions and run-time choices for AI forms | Proposed |
| [0051](0051-administration-quotation-hand-off.md) | Administration Dept quotation hand-off | Proposed |
