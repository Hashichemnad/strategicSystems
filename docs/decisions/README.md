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
| [0047](0047-program-request-validation.md) | Program request validation: what applies when, and the "other" convention | Accepted by the owner |
| [0048](0048-request-approval-chain-and-approved-budget.md) | Request approval chain, status per step and the Approved Budget | Accepted by the owner |
| [0049](0049-project-auto-creation-and-numbering.md) | Automatic Project / Initiative creation and `PROJ-…` numbering | Accepted by the owner |
| [0050](0050-proposal-assessment-and-dynamic-ai-choices.md) | Proposal Assessment versions and run-time choices for AI forms | Accepted by the owner |
| [0051](0051-administration-quotation-hand-off.md) | Administration Dept quotation hand-off | Accepted by the owner |
| [0060](0060-kpi-period-windows.md) | KPI period windows, reminders and the late flag | Accepted by the owner |
| [0061](0061-kpi-result-lifecycle-and-values.md) | KPI result lifecycle, Not Applicable and value units | Accepted by the owner |
| [0062](0062-kpi-strategy-hierarchy-and-scorecards.md) | Strategy hierarchy, cascading and weighted scorecards | Accepted by the owner |
| [0063](0063-kpi-definition-change-control.md) | KPI definition change control | Accepted by the owner |
| [0064](0064-kpi-approval-checklist-and-bulk.md) | KPI verification checklist and bulk approval | Accepted by the owner |
| [0065](0065-kpi-contracts-and-integration-events.md) | KPI contracts for other systems and integration events | Accepted by the owner |
| [0070](0070-project-lifecycle-templates-and-gates.md) | Project lifecycle: stage templates, gates, one approval at a time, high-level vs detailed plan | Accepted by the owner |
| [0071](0071-monthly-cycle-and-progress.md) | Monthly update cycle, achievement lock and progress formulas | Accepted by the owner |
| [0072](0072-team-formation-and-project-access.md) | Team formation through Teams; project access from team membership; closure events | Accepted by the owner |
| [0073](0073-change-requests.md) | Change requests: conditional approvers, one under review per project, semi-automatic apply | Accepted by the owner |
| [0074](0074-project-risks-and-kpis.md) | Project risks behind a scoring interface; project KPIs and the KPI catalogue | Accepted by the owner |
| [0075](0075-strategic-objectives-from-kpi.md) | Strategic objectives come from the KPI strategy hierarchy | Accepted by the owner |
| [0076](0076-phase-9-ai-and-mcp.md) | Phase 9 AI and MCP scope | Accepted by the owner |
| [0080](0080-request-framework-types-forms-stages.md) | Request Framework: built-in and custom types, form schema, stages and handlers | Accepted by the owner |
| [0081](0081-dynamic-approval-processes-and-approve-forms.md) | Approval processes for request types and approve mini-forms | Accepted by the owner |
| [0082](0082-numbering-type-and-org-tokens.md) | Numbering tokens `{TYPE}` and `{ORG}`; Document ID and version numbers | Accepted by the owner |
| [0083](0083-document-sensitivity-clearance.md) | Document sensitivity clearance: one visibility rule for every read path | Accepted by the owner |
| [0084](0084-document-validity-and-expiry.md) | Document validity by type, reminders, confirmation and expiry | Accepted by the owner |
| [0085](0085-uncontrolled-copy-watermark.md) | "Uncontrolled when printed" watermark on downloads | Accepted by the owner |
| [0086](0086-emirates-id-encryption-and-masking.md) | Emirates ID: encryption, masking and who may see it | Accepted by the owner |
| [0087](0087-ip-from-documents-renewal-internal.md) | IP from published documents, expiry, renewal and internal-only protection | Accepted by the owner |
| [0088](0088-request-pdf-templates-and-embedding.md) | Request PDF templates and the embeddable submit page | Accepted by the owner |
| [0100](0100-risk-framework-versions-and-scoring.md) | Risk framework versions, shared scoring engine, band gaps, recalculation | Accepted by the owner |
| [0101](0101-portfolio-project-risks-on-the-shared-framework.md) | Portfolio project risks scored by the shared risk framework | Accepted by the owner |
| [0102](0102-risk-lifecycle-and-approvals.md) | Risk lifecycle, one pending process, approvals R1 / R3 / R5 | Accepted by the owner |
| [0103](0103-project-risks-tab-live-read.md) | Project Risks tab: live, permission-filtered read; separate dashboard category | Accepted by the owner |
| [0104](0104-kris-and-kpi-results.md) | Key risk indicators react to approved KPI results | Accepted by the owner |
| [0105](0105-risk-screens-charts-reports.md) | Risk screens: CSS / SVG heat-maps and charts; reports through the platform renderers | Accepted by the owner |
| [0106](0106-risk-ai-and-mcp.md) | Risk AI features and MCP tools | Accepted by the owner |
| [0107](0107-risk-office-role-and-system-ownership.md) | Risk Office is a role; owning unit configurable | Accepted by the owner |
