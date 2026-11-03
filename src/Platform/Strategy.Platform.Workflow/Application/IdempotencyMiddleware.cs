using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Workflow.Domain;
using Strategy.Platform.Workflow.Infrastructure;

namespace Strategy.Platform.Workflow.Application;

/// <summary>
/// <c>Idempotency-Key</c> handling for endpoints marked <see cref="IdempotentEndpoint"/> (CLAUDE.md §11, ADR 0026): the first request runs and
/// its successful response is kept for 24 hours; a repeat with the same key and request replays it without running the handler again.
/// </summary>
internal sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public const int MaxKeyLength = 100;
    public const int MaxStoredBody = 64 * 1024;

    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopes, IClock clock)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotentEndpoint>() is null
            || !context.Request.Headers.TryGetValue(IdempotencyEndpointExtensions.HeaderName, out var header)
            || context.User.UserId() is not { } userId)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var key = header.ToString().Trim();
        if (key.Length is 0 or > MaxKeyLength || key.Any(char.IsControl))
        {
            await WriteAsync(context, Error.Validation([new FieldError(IdempotencyEndpointExtensions.HeaderName, "idempotency.invalidKey", "errors.idempotencyInvalidKey")]))
                .ConfigureAwait(false);
            return;
        }

        var fingerprint = await FingerprintAsync(context.Request).ConfigureAwait(false);
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var existing = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, context.RequestAborted)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                await ReplayOrRefuseAsync(context, existing, fingerprint).ConfigureAwait(false);
                return;
            }

            db.IdempotencyRecords.Add(new IdempotencyRecord { UserId = userId, Key = key, Fingerprint = fingerprint, CreatedAt = clock.UtcNow });
            try
            {
                await db.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await WriteAsync(context, Error.Conflict("idempotency.inProgress", "errors.idempotencyInProgress")).ConfigureAwait(false);
                return;
            }
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        var stored = false;
        try
        {
            await next(context).ConfigureAwait(false);
            if (context.Response.StatusCode is >= 200 and < 300 && buffer.Length <= MaxStoredBody)
            {
                await CompleteAsync(scopes, userId, key, context.Response.StatusCode, context.Response.ContentType, buffer.ToArray()).ConfigureAwait(false);
                stored = true;
            }
        }
        finally
        {
            context.Response.Body = original;
            if (!stored)
            {
                // Failures are not remembered: the client may correct the request and retry with the same key.
                await RemoveAsync(scopes, userId, key).ConfigureAwait(false);
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(original, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task ReplayOrRefuseAsync(HttpContext context, IdempotencyRecord existing, string fingerprint)
    {
        if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            await WriteAsync(context, Error.Rule("idempotency.keyReused", "errors.idempotencyKeyReused")).ConfigureAwait(false);
            return;
        }

        if (!existing.Completed)
        {
            await WriteAsync(context, Error.Conflict("idempotency.inProgress", "errors.idempotencyInProgress")).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = existing.StatusCode;
        if (existing.ContentType is { } contentType)
        {
            context.Response.ContentType = contentType;
        }

        context.Response.Headers[IdempotencyEndpointExtensions.ReplayedHeaderName] = "true";
        if (existing.Body is { Length: > 0 } body)
        {
            await context.Response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
        }
    }

    private static async Task<string> FingerprintAsync(HttpRequest request)
    {
        request.EnableBuffering();
        using var memory = new MemoryStream();
        await request.Body.CopyToAsync(memory, request.HttpContext.RequestAborted).ConfigureAwait(false);
        request.Body.Position = 0;
        var head = Encoding.UTF8.GetBytes($"{request.Method}\n{request.Path}{request.QueryString}\n");
        return Convert.ToHexStringLower(SHA256.HashData([.. head, .. memory.ToArray()]));
    }

    private static async Task CompleteAsync(IServiceScopeFactory scopes, Guid userId, string key, int status, string? contentType, byte[] body)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var record = await db.IdempotencyRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        record.Completed = true;
        record.StatusCode = status;
        record.ContentType = contentType;
        record.Body = body;
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task RemoveAsync(IServiceScopeFactory scopes, Guid userId, string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        await db.IdempotencyRecords.Where(r => r.UserId == userId && r.Key == key && !r.Completed).ExecuteDeleteAsync().ConfigureAwait(false);
    }

    private static Task WriteAsync(HttpContext context, Error error) => error.ToProblem().ExecuteAsync(context);
}
