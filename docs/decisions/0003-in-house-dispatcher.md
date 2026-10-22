# 0003 — In-house command / query dispatcher

- **Status:** Accepted (Phase 0)
- **Context:** MediatR v13+ has a commercial licence and is banned (CLAUDE.md §4.3). We still want one pipeline for every
  use case, so validation, logging and (later) access, audit reason and idempotency are not repeated in every handler.

## Decision
A small dispatcher in `Strategy.Platform.Kernel.Messaging`:

- `ICommand<TResult>` (changes state) and `IQuery<TResult>` (reads), both `IRequest<TResult>`.
- One handler per request: `ICommandHandler<TCommand,TResult>` / `IQueryHandler<TQuery,TResult>`, returning `Task<Result<TResult>>`.
- `IPipelineBehavior<TRequest,TResult>`, registered as open generics; the **first registered runs outermost**.
  Phase 0 pipeline: `LoggingBehavior` → `ValidationBehavior` (FluentValidation; failures become `Error.Validation` with message keys).
- `Dispatcher` resolves handler and behaviours from the current DI scope. It caches one typed invoker per request type, so there is no reflection per call.
- `AddRequestHandlersFrom(assembly)` registers a module's handlers and validators.

## Why
- About 100 lines of owned code, fully unit-tested (ordering, short-circuit, missing handler), with no licence risk.
- `Result<T>` instead of exceptions for expected failures (validation, not found, rule, conflict); exceptions stay for bugs.

## Consequences
- No notifications or broadcast. Cross-module reactions use integration events through the outbox (Phase 3).
- New cross-cutting concerns are added as behaviours, in registration order, in `AddPlatformKernel`.
