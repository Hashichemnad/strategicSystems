# 0217 — The shipped-pack upgrade also adds and retires objects; snapshots chain

- **Status:** Proposed (release 1.0.2) — awaiting owner acceptance
- **Context:** ADR 0213 replaces an installed object only while it still equals the snapshot of the release that shipped it. Pack 3 (release
  1.0.2) adds objects, such as fraud scenarios, FAA findings and the new sync targets. It also renames the objects whose Salesforce names were
  invented. Upgrades from pack 1 must still work.

## Decision
- **Snapshots:** `upgrades/adof-salesforce-v2.json` holds the version-2 content of every object, because pack 3 changes nearly all of them.
- **Chaining:** snapshot `vN` holds version-N content of the objects that version N+1 changed. The content an installation of version V
  received for an object is its first appearance in `vV`, `vV+1`, …. So pack 1 installs upgrade straight to 3.
- **Upgrade (`UpgradeShippedPackAsync`):**
  - Objects still equal to their snapshot are replaced, as in ADR 0213.
  - Changed objects are kept and logged.
  - **New object keys are inserted.**
  - Shipped objects that the new pack drops are **disabled** while they still equal the snapshot.
  - The pack's lookup values are provisioned (ADR 0216).
  - Every inserted or replaced object is unconfirmed and disabled until *Validate* passes against the live describe.
- Object keys are kept where the meaning is the same, so watermarks, exceptions and references stay with them. For example `risk` now reads
  `Org_RiskFactor__c` instead of the invented `RMS_Risk__c`. A changed source object restarts that object's watermark with a full load.

## Consequences
After `upgrade.sh` the administrator validates and enables the new and replaced objects, in load order: departments, people, then the rest.
The guide lists them.
