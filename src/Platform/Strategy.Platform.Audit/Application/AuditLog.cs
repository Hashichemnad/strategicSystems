using Microsoft.Extensions.Logging;
using Npgsql;

namespace Strategy.Platform.Audit.Application;

/// <summary>Explicit events (sign-in, download, export…) in their own transaction, plus a structured log line.</summary>
internal sealed partial class AuditLog(NpgsqlDataSource dataSource, IAuditSink sink, ILogger<AuditLog> logger) : IAuditLog
{
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var record = new AuditRecord(AuditKinds.Event, auditEvent.Action, auditEvent.SystemCode, auditEvent.EntityType, auditEvent.RecordId,
            Success: auditEvent.Success, Data: AuditCanonical.DataJson(auditEvent.Data), Reason: auditEvent.Reason)
        {
            ActorId = auditEvent.ActorId,
            ActorName = auditEvent.ActorName,
        };

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await sink.AppendAsync(connection, transaction, [record], cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        LogEvent(logger, auditEvent.Action, auditEvent.Success, auditEvent.EntityType, auditEvent.RecordId);
    }

    [LoggerMessage(EventId = 4200, Level = LogLevel.Information,
        Message = "Audit event {AuditAction} success={AuditSuccess} entity={AuditEntityType} record={AuditRecordId}")]
    private static partial void LogEvent(ILogger logger, string auditAction, bool auditSuccess, string? auditEntityType, string? auditRecordId);
}
