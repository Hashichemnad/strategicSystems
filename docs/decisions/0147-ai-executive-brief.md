# 0147 — AI executive brief

- **Status:** Proposed (Phase 17)
- **Context:** SPEC §0.4: bilingual narrative of the period (improved, needs attention, recommended decisions) with links to evidence;
  CLAUDE.md §12: AI drafts, structured output, untrusted text as data, data controls.

## Decision
- Feature `reporting-executive-brief`, permission `reporting.brief.generate`. Input: the viewer's executive payload for the period and the
  previous one, plus evidence candidates referenced by opaque ids (`ref-n` mapped server-side to records). Record titles are passed as
  delimited data; Settings data controls and masking apply.
- Output schema `{improved[], attention[], decisions[]}` of `{textAr, textEn, evidence[]}`; unknown ids are dropped. Shown as an
  AI-generated draft; the user may edit and save it as the period brief (`reporting.brief`, audited) or include it in a scheduled email.
- AI off or failing → the section is hidden; the dashboard never waits for it.

## Consequences
The brief can only cite records the viewer may see; every call is in `ai.usage_log` and the audit trail (channel `ai`).
