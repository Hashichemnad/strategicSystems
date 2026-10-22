using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Kernel;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.UnitTests.Kernel;

public class DispatcherTests
{
    [Fact]
    public async Task Sends_request_to_its_handler()
    {
        await using var provider = BuildProvider();
        var dispatcher = provider.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.SendAsync(new Echo("hello"));

        result.Value.ShouldBe("hello");
    }

    [Fact]
    public async Task Validation_failure_short_circuits_before_the_handler()
    {
        await using var provider = BuildProvider();
        var dispatcher = provider.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.SendAsync(new Echo(""));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields!.Single().ShouldSatisfyAllConditions(
            f => f.Field.ShouldBe("text"),
            f => f.MessageKey.ShouldBe("validation.notEmpty"));
        EchoHandler.Calls.ShouldNotContain(string.Empty);
    }

    [Fact]
    public async Task Behaviours_run_in_registration_order_outermost_first()
    {
        var trace = new List<string>();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(trace);
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(FirstBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(SecondBehavior<,>));
        services.AddScoped<IRequestHandler<Traced, string>>(_ => new TracingHandler(trace));
        await using var provider = services.BuildServiceProvider();

        await provider.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new Traced());

        trace.ShouldBe(["first:before", "second:before", "handler", "second:after", "first:after"]);
    }

    [Fact]
    public async Task Missing_handler_is_a_programming_error()
    {
        await using var provider = BuildProvider();
        var dispatcher = provider.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();

        await Should.ThrowAsync<InvalidOperationException>(() => dispatcher.SendAsync(new Unhandled()));
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddPlatformKernel();
        services.AddRequestHandlersFrom(typeof(DispatcherTests).Assembly);
        return services.BuildServiceProvider();
    }

    public sealed record Echo(string Text) : IQuery<string>;

    public sealed record Unhandled : ICommand<Unit>;

    /// <summary>Handled only by the manually registered <see cref="TracingHandler"/>, never by the assembly scan.</summary>
    public sealed record Traced : IQuery<string>;

    public sealed class EchoValidator : AbstractValidator<Echo>
    {
        public EchoValidator() => RuleFor(x => x.Text).NotEmpty();
    }

    public sealed class EchoHandler : IQueryHandler<Echo, string>
    {
        public static readonly List<string> Calls = [];

        public Task<Result<string>> HandleAsync(Echo request, CancellationToken cancellationToken)
        {
            Calls.Add(request.Text);
            return Task.FromResult<Result<string>>(request.Text);
        }
    }

    private sealed class TracingHandler(List<string> trace) : IRequestHandler<Traced, string>
    {
        public Task<Result<string>> HandleAsync(Traced request, CancellationToken cancellationToken)
        {
            trace.Add("handler");
            return Task.FromResult<Result<string>>("traced");
        }
    }

    private sealed class FirstBehavior<TRequest, TResult>(List<string> trace) : IPipelineBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult>
    {
        public async Task<Result<TResult>> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        {
            trace.Add("first:before");
            var result = await next();
            trace.Add("first:after");
            return result;
        }
    }

    private sealed class SecondBehavior<TRequest, TResult>(List<string> trace) : IPipelineBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult>
    {
        public async Task<Result<TResult>> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        {
            trace.Add("second:before");
            var result = await next();
            trace.Add("second:after");
            return result;
        }
    }
}
