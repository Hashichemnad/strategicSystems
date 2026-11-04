# 0050 — Proposal Assessment versions and run-time choices for AI forms

- **Status:** Accepted by the owner (Phase 8, before the pull request)
- **Context:** SPEC §1.15 A: the smart request fills the form from one or more documents (strategic objective "matched to the configured
  list") and produces a Proposal Assessment saved on the request, visible to the creator and all approvers, regenerable with previous
  versions kept. Phase 5's AI forms declare their choices in code; strategic objectives are a lookup (configuration).

## Decision
- **`IAiFormChoiceSource`** (AI.Contracts, additive): a module returns, per form, field → `(codes, hint)` at run time. The extraction
  service merges them into the form before building the prompt and before coercing values (only active codes are accepted). Portfolio
  supplies the active strategic objectives, sources, program types and delivery options.
- **Several documents:** each file is extracted separately through the shared endpoint (one usage-log entry and audit event per file); the
  browser merges the proposals (highest confidence wins). Nothing is saved before the user confirms (Phase 5 review rule).
- **Proposal Assessment:** AI feature `portfolio-assessment` (own switch and permission). Generated only for a saved request; input = the
  request's values (gateway data controls / masking) + the text of its attached documents as untrusted data + configured objectives and
  criteria as trusted values. Output validated against a schema; unknown codes dropped. **Similar requests / projects are computed by the
  server** (trigram similarity within the caller's scope), never by the model. Each generation is a new immutable row
  (`proposal_assessment.version_no`); reading follows the request's read access (creator, approvers, PM Office, department viewers).
  Generating needs edit rights on a draft or the PM Office manage permission.

## Consequences
Other modules can declare configuration-driven AI choices the same way. Assessments are history, never edited.
