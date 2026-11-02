# 0018 — Audit trail: storage, hash chain, append-only database role

- **Status:** Accepted (Phase 3)
- **Context:** CLAUDE.md §8 and SPEC §0.2 require every create / update / delete on every record to be logged with field-level old → new
  values, plus security, file, export and job events; the log must be append-only and tamper-evident ("nobody, including administrators,
  can edit or delete audit entries"), with a History tab per record, a global Audit Log, export and "view record as of date".

## Decision
- **One table, one chain:** `audit.entry`, one row per changed field (update) or one snapshot row (create / delete, JSON of all audited
  fields), one row per explicit event. Columns: see `docs/design/audit-files-notifications-jobs.md` §2.1.
- **Interceptor in Persistence:** `AuditTrailInterceptor` (registered on every module DbContext) collects audited entries in
  `SavingChanges` — types that are `IAuditable`, or carry `[Audited]`; `[NotAudited]` / `[AuditMasked]` / `[AuditParent]` steer it — and
  writes the rows in `SavedChanges` **on the same connection and transaction**. Without a caller transaction it opens one before EF saves
  and commits after the audit rows: data and audit commit together or not at all (a refused save leaves no rows; tested).
- **Hash chain:** `hash = SHA-256(prev_hash ‖ canonical_json(row))`, genesis = 32 zero bytes. Canonical JSON: fixed property order, UTF-8,
  no whitespace, UTC timestamps with microseconds, nulls written. `data` is stored as **text**, not jsonb, so the stored bytes are the hashed
  bytes. `seq` is assigned under a transaction-scoped advisory lock as last + 1, so the chain is strictly serial and has **no gaps**: a
  deleted row shows as a missing `seq`.
- **Serial writes:** the advisory lock is held until the business transaction ends. Audited writes are business operations of low
  frequency; serialising them is the price of a single verifiable chain. If throughput ever matters, partitioned chains per system can be
  added without changing the read side.
- **Verification:** walks the chain from genesis (full) or from the last checkpoint (incremental), checking consecutive `seq`, every
  `prev_hash` and every hash; `audit.checkpoint` keeps the last verified `(seq, hash)` so a cut-off tail is reported as truncation.
  Daily Quartz job + "Verify now"; a failure alerts the holders of `audit.chain.verify` (in-app and e-mail).
- **Append-only at the database:**
  - triggers refuse UPDATE, DELETE (row) and TRUNCATE (statement) on `audit.entry` for every role; `REVOKE UPDATE, DELETE, TRUNCATE … FROM PUBLIC`;
  - **two roles** for installs: migrations run as the owner (`ConnectionStrings:StrategyMigrations`, used by `--migrate`), the app as a runtime
    role (`ConnectionStrings:Strategy`); after every migration the `RuntimeRoleGrants` seeder gives the runtime role read / write on the
    module schemas and only `SELECT, INSERT` on append-only tables. `deploy/postgres/init-roles.sh` creates the runtime role on a new volume.
    Existing installations move to the two roles with `--setup-db-roles` (deploy/README.md). The runtime role cannot disable the trigger (not the owner) nor change rows (no privilege) — tested. Single-role installs (development,
    older installs) are still protected by the trigger; only a superuser switching it off could change rows, and verification detects that.
- **Older records carried over:** Phase 1 `identity.auth_event` rows are staged by the identity migration in `audit.legacy_import` (the
  audit schema migrates first) and chained by the audit seeder, oldest first, with channel `import` and their original time, user and IP.
  `/identity/admin/auth-events` now reads the audit trail.
- **Values:** invariant formatting; jsonb columns compacted with sorted keys before comparing (PostgreSQL re-formats jsonb); secrets,
  hashes and keys masked as `***` (by attribute or by name); per-sign-in counters `[NotAudited]`.
- **Read side:** owners expose `GET …/{id}/audit` and `…/audit/as-of` after their own record check (404 outside scope); `/history` stays
  the organisation's effective-dated history. Global search / export at `/api/v1/audit` (permissions `audit.log.view`, `audit.log.export`,
  `audit.chain.verify`; roles Audit administrator — a platform administrator role — and Auditor).

## Consequences
- Retention is a setting with default "keep forever" (SPEC). Purging is not offered: the application role cannot delete audit rows; an
  archive-and-purge procedure run by the owner is a later, explicit operation (known gap).
- An attacker with superuser rights could rewrite the whole chain consistently; anchoring checkpoints outside the database (e.g. periodic
  export of the latest hash) is a possible hardening step, not part of Phase 3.
