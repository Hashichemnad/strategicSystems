using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Scheduling;

/// <summary>A message for the outbox: handler type, JSON-serialisable payload, optional delay and expiry.</summary>
public sealed record OutboxEnvelope(string Type, object Payload, DateTimeOffset? AvailableAt = null, DateTimeOffset? ExpiresAt = null);

/// <summary>
/// Writes outbox messages. With a <see cref="DbContext"/> that has an open transaction the message is inserted in that transaction, so it
/// exists exactly when the business change commits; otherwise it is committed at once on its own.
/// </summary>
public interface IOutbox
{
    Task EnqueueAsync(DbContext context, OutboxEnvelope message, CancellationToken cancellationToken = default);

    Task EnqueueAsync(OutboxEnvelope message, CancellationToken cancellationToken = default);
}

/// <summary>Processes one outbox message type. Throwing means "retry later".</summary>
public interface IOutboxHandler
{
    string Type { get; }

    Task HandleAsync(string payload, CancellationToken cancellationToken);
}

/// <summary>Wakes the outbox worker so new messages go out within a second instead of the next poll.</summary>
public sealed class OutboxSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // already signalled
            }
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _signal.Dispose();
}

/// <summary>JSON options of outbox payloads (web defaults), shared by writers and handlers.</summary>
public static class OutboxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

internal sealed class Outbox(NpgsqlDataSource dataSource, IClock clock, IRequestContext context, OutboxSignal signal) : IOutbox
{
    public async Task EnqueueAsync(DbContext dbContext, OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        if (dbContext.Database.CurrentTransaction is { } transaction)
        {
            await InsertAsync(dbContext.Database.GetDbConnection(), transaction.GetDbTransaction(), message, cancellationToken).ConfigureAwait(false);
            signal.Notify(); // the worker retries on its next poll if the caller has not committed yet
            return;
        }

        await EnqueueAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnqueueAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await InsertAsync(connection, null, message, cancellationToken).ConfigureAwait(false);
        signal.Notify();
    }

    private async Task InsertAsync(DbConnection connection, DbTransaction? transaction, OutboxEnvelope message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var now = clock.UtcNow;
        await using var command = new NpgsqlCommand(
            """
            insert into scheduling.outbox_message (id, type, payload, created_at, available_at, attempts, expires_at, correlation_id)
            values (@id, @type, @payload, @now, @available, 0, @expires, @correlation)
            """, (NpgsqlConnection)connection, (NpgsqlTransaction?)transaction);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = Guid.CreateVersion7() });
        command.Parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Text) { Value = message.Type });
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(message.Payload, OutboxJson.Options) });
        command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });
        command.Parameters.Add(new NpgsqlParameter("available", NpgsqlDbType.TimestampTz) { Value = message.AvailableAt ?? now });
        command.Parameters.Add(new NpgsqlParameter("expires", NpgsqlDbType.TimestampTz) { Value = (object?)message.ExpiresAt ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("correlation", NpgsqlDbType.Text) { Value = context.CorrelationId });
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
