# 0115 — Vendor access expiry, dormant accounts, notifications and rating

- **Status:** Accepted by the owner (Phase 11)

## Decision
- `access_until` per assignment: from the linked contract's end date by default (`expiryRule = contract-end`), else entered by the PM;
  `endAtClosure` (default on) ends access when the project is closed, completed or cancelled. Record-role validity makes expiry immediate;
  the hourly job `portfolio.wasel-access` marks rows expired / ended and revokes sessions.
- Dormant vendor accounts (no sign-in for `dormantDays`, default 90; 0 = off) are disabled by the same job; PM Office reactivates.
- Vendors receive **e-mail** notifications only (bilingual, their language first); the in-app bell and SignalR stay internal.
- Rating: timeliness, quality, responsiveness, reporting (1–5), overall = average (2 dp). Closure ratings set the project's Impact
  "vendor satisfaction" when it is still empty (rounded average); the PM may change it.

## Consequences
No vendor keeps access past the contract or project without an explicit new assignment.
