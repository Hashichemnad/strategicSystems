using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using Strategy.Platform.Audit.Domain;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Audit.Application;

/// <summary>
/// Appends rows to <c>audit.entry</c> on the caller's connection and transaction. A transaction-scoped advisory lock makes the chain
/// strictly serial: the next <c>seq</c> is the last one plus one (no gaps, so a deleted row is visible) and every row's
/// <c>prev_hash</c> is the hash of the row before it.
/// </summary>
internal sealed class AuditChainWriter(IRequestContext context, IClock clock) : IAuditSink
{
    /// <summary>Advisory lock key of the audit chain ("AUDT").</summary>
    public const long ChainLockKey = 0x41554454;

    private const int RowsPerStatement = 1000;

    private static readonly string[] Columns =
    [
        "seq", "id", "occurred_at", "kind", "action", "system_code", "entity_type", "record_id", "parent_type", "parent_id", "field",
        "old_value", "new_value", "change_set_id", "user_id", "user_name", "on_behalf_of_id", "on_behalf_of_name", "channel",
        "correlation_id", "ip_address", "user_agent", "reason", "success", "data", "prev_hash", "hash",
    ];

    public async Task AppendAsync(DbConnection connection, DbTransaction transaction, IReadOnlyList<AuditRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return;
        }

        var npgsql = (NpgsqlConnection)connection;
        var tx = (NpgsqlTransaction)transaction;
        await using (var lockCommand = new NpgsqlCommand($"select pg_advisory_xact_lock({ChainLockKey})", npgsql, tx))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        long seq = 0;
        var prev = AuditCanonical.Genesis;
        await using (var last = new NpgsqlCommand("select seq, hash from audit.entry order by seq desc limit 1", npgsql, tx))
        await using (var reader = await last.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                seq = reader.GetInt64(0);
                prev = (byte[])reader[1];
            }
        }

        var now = clock.UtcNow;
        var entries = new List<AuditEntry>(records.Count);
        foreach (var record in records)
        {
            var entry = ToEntry(record, ++seq, now);
            AuditCanonical.Seal(entry, prev);
            prev = entry.Hash;
            entries.Add(entry);
        }

        foreach (var chunk in entries.Chunk(RowsPerStatement))
        {
            await InsertAsync(npgsql, tx, chunk, cancellationToken).ConfigureAwait(false);
        }
    }

    private AuditEntry ToEntry(AuditRecord r, long seq, DateTimeOffset now)
    {
        // A carried-over record (channel given) keeps its own actor; otherwise the signed-in caller, else the named subject (sign-in attempts).
        var legacy = r.Channel is not null;
        var userId = legacy ? r.ActorId : context.UserId ?? r.ActorId;
        var userName = legacy ? r.ActorName : context.UserId is not null ? context.User.Identity?.Name : r.ActorName;
        return new AuditEntry
        {
            Seq = seq,
            Id = Guid.CreateVersion7(),
            OccurredAt = r.OccurredAt ?? now,
            Kind = r.Kind,
            Action = r.Action,
            SystemCode = r.SystemCode,
            EntityType = r.EntityType,
            RecordId = r.RecordId,
            ParentType = r.ParentType,
            ParentId = r.ParentId,
            Field = r.Field,
            OldValue = r.OldValue,
            NewValue = r.NewValue,
            ChangeSetId = r.ChangeSetId,
            UserId = userId,
            UserName = Cut(userName, 256),
            OnBehalfOfId = legacy ? null : context.OnBehalfOfId,
            OnBehalfOfName = legacy ? null : Cut(context.OnBehalfOfName, 256),
            Channel = r.Channel ?? context.Channel,
            CorrelationId = Cut(r.CorrelationId ?? context.CorrelationId, 64),
            IpAddress = Cut(r.IpAddress ?? context.IpAddress, 64),
            UserAgent = Cut(r.UserAgent ?? context.UserAgent, 512),
            Reason = Cut(r.Reason, 1000),
            Success = r.Success,
            Data = r.Data,
        };
    }

    private static async Task InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AuditEntry[] rows, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand { Connection = connection, Transaction = transaction };
        var sql = new System.Text.StringBuilder("insert into audit.entry (").AppendJoin(", ", Columns).Append(") values ");
        for (var i = 0; i < rows.Length; i++)
        {
            var e = rows[i];
            sql.Append(i == 0 ? "(" : ", (");
            object?[] values =
            [
                e.Seq, e.Id, e.OccurredAt, e.Kind, e.Action, e.SystemCode, e.EntityType, e.RecordId, e.ParentType, e.ParentId, e.Field,
                e.OldValue, e.NewValue, e.ChangeSetId, e.UserId, e.UserName, e.OnBehalfOfId, e.OnBehalfOfName, e.Channel,
                e.CorrelationId, e.IpAddress, e.UserAgent, e.Reason, e.Success, e.Data, e.PrevHash, e.Hash,
            ];
            for (var c = 0; c < values.Length; c++)
            {
                var name = $"p{i}_{c}";
                sql.Append(c == 0 ? "@" : ", @").Append(name);
                command.Parameters.Add(Parameter(name, values[c], Columns[c]));
            }

            sql.Append(')');
        }

        command.CommandText = sql.ToString();
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static NpgsqlParameter Parameter(string name, object? value, string column) => column switch
    {
        "seq" => new NpgsqlParameter(name, NpgsqlDbType.Bigint) { Value = value },
        "id" or "change_set_id" or "user_id" or "on_behalf_of_id" => new NpgsqlParameter(name, NpgsqlDbType.Uuid) { Value = value ?? DBNull.Value },
        "occurred_at" => new NpgsqlParameter(name, NpgsqlDbType.TimestampTz) { Value = value },
        "success" => new NpgsqlParameter(name, NpgsqlDbType.Boolean) { Value = value ?? DBNull.Value },
        "prev_hash" or "hash" => new NpgsqlParameter(name, NpgsqlDbType.Bytea) { Value = value },
        _ => new NpgsqlParameter(name, NpgsqlDbType.Text) { Value = value ?? DBNull.Value },
    };

    private static string? Cut(string? value, int max) => value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}
