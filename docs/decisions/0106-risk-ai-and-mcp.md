# 0106 — Risk AI features and MCP tools

- **Status:** Proposed (Phase 13) — awaiting owner acceptance
- **Context:** SPEC §3.9; CLAUDE.md §12 (drafts only, untrusted text as data) and §13 (read tools on, write tools off, no approve tool).

## Decision
- Model features: `risk.identify` (description, uploaded document or a project's charter text → suggested risks with cause / event /
  consequence, category, L / I and reasoning), `risk.quality` (deterministic vague-statement flags always; the model proposes a rewrite),
  `risk.suggest` (controls and actions, with up to five similar register risks the user may read as context), `risk.review-assistant`
  (facts since the last review assembled by code; the model drafts the commentary), `risk.report` (bilingual quarterly report and Top-10
  briefing from aggregates the user may see). Output JSON validated; nothing saved until the user accepts.
- `risk.duplicates` uses **no model**: trigram similarity over titles and the event text across all units; the caller sees full details
  only for risks in their read scope, other matches only as a count (no leak of other units' registers).
- MCP: `risk_register_search`, `risk_details`, `my_risk_actions`, `risk_heatmap_summary`, `risk_top10`, `risks_outside_appetite` (read, on);
  `update_risk_action_progress_draft` (write, off: records a progress note marked as from MCP; the owner confirms the percentage in the UI).

## Consequences
With AI off every screen works; the AI buttons are hidden.
