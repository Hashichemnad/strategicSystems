using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Kernel.Messaging;

/// <summary>In-house dispatcher (ADR 0003). Resolves the handler and behaviours from the current DI scope.</summary>
public sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Invokers = new();

    public Task<Result<TResult>> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invoker = (RequestInvoker<TResult>)Invokers.GetOrAdd(
            request.GetType(),
            static (requestType, resultType) => Activator.CreateInstance(
                typeof(RequestInvoker<,>).MakeGenericType(requestType, resultType))!,
            typeof(TResult));

        return invoker.InvokeAsync(request, serviceProvider, cancellationToken);
    }

    private abstract class RequestInvoker<TResult>
    {
        public abstract Task<Result<TResult>> InvokeAsync(
            IRequest<TResult> request, IServiceProvider services, CancellationToken cancellationToken);
    }

    private sealed class RequestInvoker<TRequest, TResult> : RequestInvoker<TResult>
        where TRequest : IRequest<TResult>
    {
        public override Task<Result<TResult>> InvokeAsync(
            IRequest<TResult> request, IServiceProvider services, CancellationToken cancellationToken)
        {
            var typed = (TRequest)request;
            var handler = services.GetRequiredService<IRequestHandler<TRequest, TResult>>();
            RequestHandlerDelegate<TResult> next = () => handler.HandleAsync(typed, cancellationToken);

            // Wrap from the innermost behaviour outwards so the first registered runs first.
            foreach (var behaviour in services.GetServices<IPipelineBehavior<TRequest, TResult>>().Reverse())
            {
                var inner = next;
                next = () => behaviour.HandleAsync(typed, inner, cancellationToken);
            }

            return next();
        }
    }
}
