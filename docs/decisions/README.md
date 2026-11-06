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
| [0090](0090-methodologies-and-agile-model.md) | Delivery methodologies and the agile data model | Accepted by the owner |
| [0091](0091-agile-progress-and-approval-modes.md) | Agile progress formula; sprint / monthly roll-up approval modes; hybrid | Accepted by the owner |
| [0092](0092-standards-feature-switches.md) | Feature switches for the standards enhancements | Accepted by the owner |
| [0093](0093-rag-health.md) | Project health (RAG): calculation and override | Accepted by the owner |
| [0094](0094-baselines-and-earned-value.md) | Baselines, re-baseline through a change request, variance, earned value | Accepted by the owner |
| [0095](0095-standards-data-model.md) | RAID, RACI, communication plan, benefits, quality, payments, checklists, templates, portfolios | Accepted by the owner |
| [0096](0096-gantt-and-echarts.md) | In-house SVG Gantt, exports, Apache ECharts | Accepted by the owner |
| [0097](0097-monthly-status-report-pdf.md) | Bilingual monthly status report PDF | Accepted by the owner |
| [0098](0098-cost-visibility.md) | Cost / budget visibility for team members | Accepted by the owner |
| [0099](0099-phase-10-ai-and-mcp.md) | Phase 10 AI assists and MCP tools | Accepted by the owner |
| [0100](0100-risk-framework-versions-and-scoring.md) | Risk framework versions, shared scoring engine, band gaps, recalculation | Accepted by the owner |
| [0101](0101-portfolio-project-risks-on-the-shared-framework.md) | Portfolio project risks scored by the shared risk framework | Accepted by the owner |
| [0102](0102-risk-lifecycle-and-approvals.md) | Risk lifecycle, one pending process, approvals R1 / R3 / R5 | Accepted by the owner |
| [0103](0103-project-risks-tab-live-read.md) | Project Risks tab: live, permission-filtered read; separate dashboard category | Accepted by the owner |
| [0104](0104-kris-and-kpi-results.md) | Key risk indicators react to approved KPI results | Accepted by the owner |
| [0105](0105-risk-screens-charts-reports.md) | Risk screens: CSS / SVG heat-maps and charts; reports through the platform renderers | Accepted by the owner |
| [0106](0106-risk-ai-and-mcp.md) | Risk AI features and MCP tools | Accepted by the owner |
| [0107](0107-risk-office-role-and-system-ownership.md) | Risk Office is a role; owning unit configurable | Accepted by the owner |
| [0110](0110-wasel-vendor-principal-and-scheme.md) | Wasel: vendor principal and a separate authentication scheme | Accepted by the owner |
| [0111](0111-vendor-authorization-through-record-roles.md) | Vendor authorization through IAccessService and record roles | Accepted by the owner |
| [0112](0112-vendor-register-shared-supplier-master.md) | Vendor register as the shared supplier master | Accepted by the owner |
| [0113](0113-vendor-visible-fields-and-dtos.md) | Vendor-visible fields and separate vendor DTOs | Accepted by the owner |
| [0114](0114-vendor-proposals-submissions-and-monthly.md) | Plan proposals, vendor submissions, PM review with diff, monthly approval | Accepted by the owner |
| [0115](0115-vendor-access-expiry-notifications-rating.md) | Vendor access expiry, dormant accounts, notifications, rating | Accepted by the owner |
| [0116](0116-capacity-view-and-portfolio-review.md) | Capacity view and quarterly portfolio review | Accepted by the owner |
| [0117](0117-wasel-ai-gantt-font-sprint-notification.md) | Wasel AI, bundled Gantt export font, sprint review notification | Accepted by the owner |
| [0120](0120-cap-data-model-and-numbering.md) | CAP data model, many-to-many actions and numbering | Proposed |
| [0121](0121-cap-lifecycle-and-approvals.md) | CAP lifecycle: owner response, per-action PMO validation, completion and extension | Proposed |
| [0122](0122-cap-due-dates-late-and-reminders.md) | CAP due dates by priority in working time, late actions, escalation, monthly reminders | Proposed |
| [0123](0123-cap-source-system-contract.md) | CAP source-system contract, live links, KPI and Risk integrations | Proposed |
| [0124](0124-cap-budget-routing-to-portfolio.md) | Budget routing: draft Program Request from a CAP action | Proposed |
| [0125](0125-cap-access-model.md) | CAP access: PM Office role, record roles, unit scope | Proposed |
| [0126](0126-benchmarking-on-the-request-framework.md) | Benchmarking: Plan on the Request Framework, stages and gates, access | Proposed |
| [0127](0127-benchmarking-plan-and-pre-quarter-notice.md) | Annual benchmarking plan and the pre-quarter search notification | Proposed |
| [0128](0128-benchmarking-to-cap-closure-kpi-library.md) | Analysis → CAP, closure rule, KPI benchmark update, library, impact follow-up | Proposed |
| [0129](0129-cap-and-benchmarking-ai-and-mcp.md) | CAP and Benchmarking AI features and MCP tools | Proposed |
| [0130](0130-record-roles-granted-to-units.md) | Record roles granted to a unit (audit department scope) | Accepted |
| [0131](0131-audit-data-model-numbering-and-closure.md) | Audit data model, numbering and closure rules | Accepted |
| [0132](0132-audit-office-only-write-model.md) | Audit-Office-only write model and read-only department users | Accepted |
| [0133](0133-audit-follow-ups-late-and-reminders.md) | Audit follow-ups, late status, reminders, optional review | Accepted |
| [0134](0134-audit-excel-import-and-ai-extraction.md) | Excel import of findings and AI report-to-findings extraction | Accepted |
| [0135](0135-audit-integrations-risk-docreg-cap-kpi-teams.md) | Audit integrations: Risk controls, Document Registry, CAP, KPI, Teams | Accepted |
| [0136](0136-audit-layer-2-feature-switch.md) | Audit Layer 2 (lifecycle) behind a feature switch | Accepted |
| [0137](0137-audit-ai-mcp-and-reports.md) | Audit AI features, MCP tools, reports and committee pack | Accepted |
| [0140](0140-reporting-read-model-through-contracts.md) | Reporting read model: module metric sources through Contracts, facts in `reporting` | Accepted |
| [0141](0141-scope-correct-cached-aggregates.md) | Scope-correct cached aggregates: unit partitions, additive components, live fallback | Accepted |
| [0142](0142-periods-snapshots-and-refresh.md) | Month-grain periods, comparison, snapshots and refresh triggers | Accepted |
| [0143](0143-widget-catalogue-and-dashboard-builder.md) | Widget catalogue, dashboard definitions, role defaults, builder security | Accepted |
| [0144](0144-report-builder-datasets-and-field-security.md) | Report builder: module datasets, field permissions, run-time scope | Accepted |
| [0145](0145-scheduled-delivery-as-the-recipient.md) | Scheduled delivery built as the recipient, in their language; email attachments | Accepted |
| [0146](0146-executive-dashboard-and-department-scorecard.md) | Executive dashboard and department comparison scorecard | Accepted |
| [0147](0147-ai-executive-brief.md) | AI executive brief | Accepted |
| [0148](0148-echarts-single-chart-library.md) | Apache ECharts as the single chart library; shared dashboard UI kit | Accepted |
| [0149](0149-my-work-cards-and-layout.md) | My Work cards from every system and the per-user layout | Accepted |
| [0150](0150-setup-wizard-and-setup-token.md) | First-run setup wizard and the one-time setup token | Proposed |
| [0152](0152-version-and-release-notes.md) | Product version and release notes in the application | Proposed |
