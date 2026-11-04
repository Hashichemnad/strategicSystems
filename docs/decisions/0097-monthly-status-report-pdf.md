# 0097 — Auto-generated bilingual monthly status report (PDF)

- **Status:** Proposed (Phase 10)
- **Context:** SPEC §1.12: "auto-generated bilingual project status report (PDF) from the approved monthly update"; §1.15 B: status
  report writer drafts it.

## Decision
- Generated **on demand** (`GET /projects/{id}/status-report?period&lang`) from the approved monthly update's snapshot through Gotenberg,
  so it is always reproducible and needs no storage; Arabic (RTL) and English (LTR) layouts, both titles in the header band (ADR 0021).
- The narrative is a pair of fields on the monthly update (`narrative_ar`, `narrative_en`) typed by the PM or accepted from the AI
  status report writer while the window is open; the PDF prints it as approved.
- Cost figures are omitted for callers who may not see costs (ADR 0098). Downloads are audited as exports.

## Consequences
No background job or file store needed; old reports remain identical because they read the approval snapshot.
