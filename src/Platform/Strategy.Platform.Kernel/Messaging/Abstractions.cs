using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Kernel.Messaging;

/// <summary>Marker for anything the dispatcher can send. Use <see cref="ICommand{TResult}"/> or <see cref="IQuery{TResult}"/>.</summary>
public interface IRequest<TResult>;

/// <summary>Changes state. Named by intent (e.g. <c>SubmitCharter</c>).</summary>
public interface ICommand<TResult> : IRequest<TResult>;

/// <summary>Reads state; never changes it.</summary>
public interface IQuery<TResult> : IRequest<TResult>;

public interface IRequestHandler<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<Result<TResult>> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResult> : IRequestHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>;

public interface IQueryHandler<in TQuery, TResult> : IRequestHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>;

public delegate Task<Result<TResult>> RequestHandlerDelegate<TResult>();

/// <summary>
/// Cross-cutting step around every handler (logging, validation; later access, idempotency).
/// Registered as open generics; they run in registration order, outermost first.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<Result<TResult>> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken);
}

/// <summary>Sends a command or query to its single handler through the pipeline.</summary>
public interface IDispatcher
{
    Task<Result<TResult>> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);
}
