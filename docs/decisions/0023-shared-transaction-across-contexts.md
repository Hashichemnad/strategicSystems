# 0023 — One transaction across a module's DbContext and the approval engine

- **Status:** Accepted (Phase 4)
- **Context:** Each component owns a DbContext and schema (ADR 0006), all on the same PostgreSQL database and data source. A submission
  changes the module's record and creates the approval instance; an approval changes the instance and the module's record (hook). Both
  must commit together, with their audit rows (ADR 0018) and notifications (outbox, ADR 0020).

## Decision
- `Strategy.Platform.Persistence` adds `DbContext.JoinTransactionAsync(object transactionContext)`: when the given object is a DbContext with an
  open transaction, the caller's context switches to that context's connection (`SetDbConnection(…, contextOwnsConnection: false)`) and
  enlists in the transaction (`UseTransactionAsync`). The audit interceptor then writes on that connection and transaction.
- Contracts pass the transaction as `object? transactionContext` (as `INotificationService` already does), so Contracts stay free of EF.
- **Submit:** the module begins the transaction, the engine joins it. **Act:** the engine begins it, the module hook joins it.
  **Role transfer:** Access passes its DbContext to `IRoleTransferHook`; the Workflow hook joins it and reads the uncommitted assignments.
- Hooks run synchronously in that transaction (not through the outbox): the record's state must move with the approval, and a failing
  hook must undo the approval. Integration events between business modules still use the outbox.

## Consequences
A context can join only before it has an open connection of its own in the current scope; the engine and hooks join first thing.
No foreign keys across schemas are introduced; only the transaction is shared.
