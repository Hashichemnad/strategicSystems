# 0017 — Settings storage, numbering counters and interim branding assets

- **Status:** Accepted (Phase 2)
- **Context:** CLAUDE.md §6 requires numbers to be generated in the same transaction as the record, with configurable formats. Settings must take effect without a redeploy. The Files service arrives in Phase 3.

## Decision
- **Typed settings:**
  - one `jsonb` document per area in `settings.setting`, read into typed records with defaults;
  - cached and invalidated on save;
  - optimistic concurrency on `xmin` (409).
- **Numbering:**
  - **Partition:** the counter is partitioned by the *rendered prefix* of the pattern. The yearly or per-unit reset follows from the tokens, and no separate reset setting can disagree with the pattern.
  - **Same transaction:** the increment is an `INSERT … ON CONFLICT DO UPDATE … RETURNING` executed on the **caller's** DbContext connection, so it joins the caller's transaction.
  - **Infrastructure table:** `settings.numbering_counter` is infrastructure, not a business table, so the "no raw SQL on business tables" rule does not apply. Callers open a transaction around "next number + save".
- **Branding logos:** stored in `settings.branding_asset` (bytea, PNG/JPEG/WebP by magic number, ≤ 512 KB) until the Files service (Phase 3). They move then, like employee photos (ADR 0014). SVG is refused, because it can carry script.
- **Translation overrides:**
  - stored per language / namespace / key;
  - served publicly, because labels are not sensitive and the sign-in page needs them;
  - merged on the client over the bundled resources. Server message keys are translated on the client, so one mechanism covers both.
- **Module enable / disable:** in `access.system.enabled`, because `IAccessService` must refuse a disabled system's permissions. The configuration key `Modules:<Name>:Enabled` only seeds the first value.

## Consequences
Branding logos and the employee photo both move to Files in Phase 3.
