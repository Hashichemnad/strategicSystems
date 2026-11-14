# 0213 — Upgrading the shipped mapping pack on installations that did not change it

- **Status:** Proposed (release 1.0.1) — awaiting owner acceptance
- **Context:** The ADoF Salesforce pack is imported once, when the database is seeded (ADR 0203), and never overwritten afterwards so an
  administrator's changes are kept. Release 1.0.1 corrects three of its objects (`user` options, `contact` matching, `project-risk`
  probability / impact). Installations of 1.0.0 would otherwise keep the faulty mappings until someone re-imports the pack by hand.

## Decision
- The shipped pack's version goes from 1 to 2. Next to it, `Sync/Packs/upgrades/adof-salesforce-v1.json` holds the **1.0.0 content of the
  objects that version 2 changes** (embedded as `sync-pack-upgrade/…`).
- At start-up, the seeder upgrades an installed pack whose version is older than the shipped one and for which a snapshot of that version
  exists: each snapshot object is replaced by the shipped object **only while the installed object still equals the snapshot** (JSON
  compared semantically: source object, kind, target, filter, watermark, external-ID template, fields, defaults, options). Replaced objects
  lose their confirmation and are disabled until *Validate* passes again, like an import. Objects an administrator changed are kept and named
  in a server-log warning; the pack version is set to the shipped one either way. Entity changes are audited by the interceptor.
- The guide tells administrators to validate and enable `user`, `contact` and `project-risk` after the upgrade, or to apply the fixes by hand
  / re-import the shipped pack when they had changed them.

## Consequences
- Upgrades never overwrite an administrator's work; untouched installations get the fixes without manual steps beyond re-validation.
- Each later pack change that should reach existing installations adds a snapshot of the objects it changes (`adof-salesforce-v{n}.json`).
