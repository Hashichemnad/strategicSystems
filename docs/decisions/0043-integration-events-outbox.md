# 0043 — Integration events through the outbox; linked-record closure

- **Status:** Accepted (Phase 7)
- **Context:** CLAUDE.md §5: cross-module reactions use integration events via the outbox (e.g. `ProjectClosureApproved` → Teams opens the
  project-team evaluation). ADR 0020 built the outbox (one handler per message type) but no publish / subscribe layer. Portfolio (which owns
  project closure) arrives in Phase 9; SPEC §4.3 also asks that closing "the linked record in another system" opens the evaluation.

## Decision
- Kernel contracts (`Strategy.Platform.Kernel.Messaging`): `IIntegrationEvent` (marker), `IIntegrationEventPublisher.PublishAsync(event,
  transactionContext?)`, `IIntegrationEventHandler<TEvent>`. Registration `services.AddIntegrationEventHandler<TEvent, THandler>()`
  (Scheduling).
- The publisher writes **one outbox message per subscriber** (type `integration:<handler type>`), in the publisher's transaction when a
  DbContext with an open transaction is passed (ADR 0023). A failing subscriber retries alone with the outbox back-off; handlers must be
  idempotent. Handlers run as the system principal, channel `job` (as other outbox work).
- A generic event **`LinkedRecordClosed(ResourceType, ResourceId, Reason, ClosedAt)`** lives in `Strategy.Platform.Links.Contracts`: any
  system publishes it when a record others link to reaches its closing point. Portfolio will publish it with reason `closure-approved` when
  project closure is approved (its `ProjectClosureApproved`, Phase 9); CAP, Audit… do the same. Teams subscribes and opens the completion
  evaluation of the linked active teams whose type triggers on `linked-record-closed`.
- Until Portfolio exists, tests use a **fake publisher** and a fake `portfolio.project` linkable type registered in the test host.

## Consequences
Subscribers never depend on the publisher's module; publishers do not know subscribers. Phase 9 adds one `PublishAsync` call.
