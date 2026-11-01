using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Scheduling.Application;

/// <summary>
/// Delivers outbox messages one at a time: each is claimed with <c>FOR UPDATE SKIP LOCKED</c> (safe with several app instances), handled,
/// and marked processed in the same transaction. A failure schedules a retry with back-off; after <see cref="MaxAttempts"/> it is dead.
/// </summary>
public sealed partial class OutboxDispatcher(
    NpgsqlDataSource dataSource, IServiceScopeFactory scopes, IClock clock, ILogger<OutboxDispatcher> logger)
{
    public const int MaxAttempts = 10;

    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2),
    ];

    /// <summary>Delay before attempt <paramref name="attempts"/> + 1: 30 s, 2 min, 10 min, 30 min, 2 h, then every 6 h.</summary>
    public static TimeSpan DelayAfter(int attempts) => attempts >= 1 && attempts <= Backoff.Length ? Backoff[attempts - 1] : TimeSpan.FromHours(6);

    /// <summary>Handles every message that is due now; returns how many were handled (delivered, retried or given up).</summary>
    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        while (await DispatchOneAsync(cancellationToken).ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }

    private async Task<bool> DispatchOneAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        Guid id;
        string type, payload;
        int attempts;
        DateTimeOffset? expires;
        string? correlation;
        await using (var claim = new NpgsqlCommand(
            """
            select id, type, payload::text, attempts, expires_at, correlation_id from scheduling.outbox_message
            where processed_at is null and dead_at is null and available_at <= @now
            order by available_at, id limit 1 for update skip locked
            """, connection, transaction))
        {
            claim.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });
            await using var reader = await claim.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return false;
            }

            id = reader.GetGuid(0);
            type = reader.GetString(1);
            payload = reader.GetString(2);
            attempts = reader.GetInt32(3);
            expires = reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4);
            correlation = reader.IsDBNull(5) ? null : reader.GetString(5);
        }

        string sql;
        string? error = null;
        if (expires is { } expiry && expiry < now)
        {
            sql = "update scheduling.outbox_message set dead_at = @now, last_error = 'expired' where id = @id";
        }
        else
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(
                    correlation is { Length: >= 8 } ? correlation : Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
                var handler = scope.ServiceProvider.GetServices<IOutboxHandler>().FirstOrDefault(h => h.Type == type)
                    ?? throw new InvalidOperationException($"No outbox handler for '{type}'.");
                await handler.HandleAsync(payload, ct).ConfigureAwait(false);
                sql = "update scheduling.outbox_message set processed_at = @now, attempts = attempts + 1, last_error = null where id = @id";
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                error = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
                var dead = attempts + 1 >= MaxAttempts;
                sql = dead
                    ? "update scheduling.outbox_message set attempts = attempts + 1, last_error = @error, dead_at = @now where id = @id"
                    : "update scheduling.outbox_message set attempts = attempts + 1, last_error = @error, available_at = @retry where id = @id";
                LogFailed(logger, exception, type, id, attempts + 1);
            }
        }

        await using (var update = new NpgsqlCommand(sql, connection, transaction))
        {
            update.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
            update.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });
            update.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Text) { Value = (object?)error ?? DBNull.Value });
            update.Parameters.Add(new NpgsqlParameter("retry", NpgsqlDbType.TimestampTz) { Value = now + DelayAfter(attempts + 1) });
            await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {Type} {Id} failed (attempt {Attempt})")]
    private static partial void LogFailed(ILogger logger, Exception exception, string type, Guid id, int attempt);
}

/// <summary>Runs the dispatcher when signalled, and every few seconds anyway.</summary>
internal sealed partial class OutboxWorker(OutboxDispatcher dispatcher, OutboxSignal signal, ILogger<OutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await dispatcher.DispatchDueAsync(stoppingToken).ConfigureAwait(false);
                await signal.WaitAsync(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or TimeoutException)
            {
                LogLoopFailed(logger, exception);
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox worker loop failed; retrying")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception);
}
