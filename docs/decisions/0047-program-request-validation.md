# 0047 — Program request validation: what applies when, and the "other" convention

- **Status:** Proposed (Phase 8)
- **Context:** SPEC §1.4 A lists the request's validation rules (Year 1–4 costs = budget, date windows, 4-digit years, Operation or Service,
  previous program name, "Other" requires text, 2 decimals). The SPEC also asks for save-as-draft wizards (§0.3), so a half-filled request
  must be storable. The picklists are configuration (CLAUDE.md rule 2), but "Other requires text" needs to know which value is "Other".

## Decision
- **Format rules on every save** (draft or not): lengths, amounts ≥ 0 with at most two decimals, 4-digit return / reduction years, share
  0–1, scores 1–5 for active criteria, active lookup codes, "other" text, end ≥ start. A draft can never hold a malformed value.
- **Completeness and business rules at submission** (and resubmission): required fields, the six screening answers, all priority
  scores, Year 1–4 = Estimated Budget exactly (when budget is required), start within the current year and the next N years, end ≤ current
  year + N (N = 3, Settings → Portfolio), value and year when a return / reduction is expected, Operation or Service, previous program name
  for continuous programs, the cost path complete when budget is required. All failures are returned together (field paths + keys).
- The current year is the installation's local year (`IClock` + Settings → Regional time zone).
- **"Other" convention:** the lookup value with the stable code **`other`** (seeded in `portfolio.suggestion-source` and
  `portfolio.delivery-option`) requires its free-text companion. Administrators may relabel it but should not delete it; other codes never
  require text.

## Consequences
The wizard can save at every step; the server is the single authority at submission. The rules live in one pure class (`RequestRules`)
with table tests.
