using Microsoft.AspNetCore.Builder;

namespace Strategy.Platform.Http.Access;

/// <summary>
/// Endpoint metadata: the endpoint honours the <c>Idempotency-Key</c> header (CLAUDE.md §11, ADR 0026). A repeated request with the same key
/// replays the first response instead of running again.
/// </summary>
public sealed record IdempotentEndpoint;

public static class IdempotencyEndpointExtensions
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";

    /// <summary>Opts the endpoint into idempotency keys (submit / approve / reject / return endpoints).</summary>
    public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new IdempotentEndpoint());
}
