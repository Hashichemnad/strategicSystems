# 0207 — Failover: make primary, outage period, outage changes report, return to standby

- **Status:** Proposed (Phase 23) — awaiting owner acceptance
- **Context:** §G5 / G6: "Make this system primary" (confirmation, reason, MFA re-prompt) stops the sync, unlocks records, re-enables
  workflows / jobs / notifications and records an outage; open Salesforce approvals → "Resubmit required"; "Return to standby" needs the outage
  report exported / acknowledged, resumes the sync and lets the admin choose per changed record keep local / take Salesforce until the outage
  is closed; the report lists every change from the audit trail with old → new values and the Salesforce ID, exportable, with re-entered marks.

## Decision
- Connection **mode** state machine `disabled → standby ⇄ (primary → reconciling) → standby` (`SyncModeLifecycle`, transition table tested);
  `reconciling → primary` re-opens the same outage if Salesforce fails again before the outage is closed.
- **Step-up MFA**: `IStepUpVerifier` (Identity) verifies a TOTP or e-mail code of the signed-in user at the moment of the action; failures audited.
  Permission `integration.sync.failover` (administrator role, MFA required).
- **Resubmit required**: at failover, synced records whose Salesforce `ProcessInstance` is pending are flagged in `sync_outage_record`; the flag
  is cleared when an approval is submitted here or the outage closes.
- **Report** from the audit trail (channel ≠ `sync`) between outage start and return (or now), grouped by system and record, Salesforce ID from
  the origin reference; Excel (ClosedXML) and PDF (Gotenberg); export sets the acknowledgement; re-entered mark per record group.
- **Return to standby** marks synced records changed during the outage; while reconciling, newer Salesforce versions of those records are held
  as `pending_payload` (conflict) unless the admin chose *Take Salesforce*; *Keep local* keeps the local version until **Close outage** (allowed
  when no conflict is pending). After closing, Salesforce wins again.

## Consequences
No automatic merge: people decide per record, with the data side by side. Records created here during the outage are listed in the report for
manual creation in Salesforce; they stay local records (no reference) until Salesforce data arrives and an admin links them (REST by-external).
