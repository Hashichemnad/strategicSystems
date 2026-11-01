using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Strategy.Platform.Audit.Domain;

namespace Strategy.Platform.Audit.Application;

/// <summary>
/// The canonical form of an entry and its hash (docs/decisions/0018): <c>hash = SHA-256(prev_hash ‖ canonical_json(row))</c>.
/// Canonical JSON = every column except the hashes, in a fixed order, UTF-8, no whitespace, timestamps in UTC with microseconds,
/// Guids lower-case, nulls written. Any change to a stored column changes the hash.
/// </summary>
public static class AuditCanonical
{
    public const int HashLength = 32;

    public static readonly byte[] Genesis = new byte[HashLength];

    /// <summary>PostgreSQL keeps microseconds; the value is truncated before hashing so the stored value hashes the same.</summary>
    public static DateTimeOffset Truncate(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % (TimeSpan.TicksPerMillisecond / 1000)));
    }

    public static byte[] Serialize(AuditEntry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            json.WriteStartObject();
            json.WriteNumber("seq", e.Seq);
            json.WriteString("id", e.Id.ToString("D"));
            json.WriteString("occurredAt", Truncate(e.OccurredAt).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture));
            json.WriteString("kind", e.Kind);
            json.WriteString("action", e.Action);
            Text(json, "systemCode", e.SystemCode);
            Text(json, "entityType", e.EntityType);
            Text(json, "recordId", e.RecordId);
            Text(json, "parentType", e.ParentType);
            Text(json, "parentId", e.ParentId);
            Text(json, "field", e.Field);
            Text(json, "oldValue", e.OldValue);
            Text(json, "newValue", e.NewValue);
            Text(json, "changeSetId", e.ChangeSetId?.ToString("D"));
            Text(json, "userId", e.UserId?.ToString("D"));
            Text(json, "userName", e.UserName);
            Text(json, "onBehalfOfId", e.OnBehalfOfId?.ToString("D"));
            Text(json, "onBehalfOfName", e.OnBehalfOfName);
            json.WriteString("channel", e.Channel);
            Text(json, "correlationId", e.CorrelationId);
            Text(json, "ipAddress", e.IpAddress);
            Text(json, "userAgent", e.UserAgent);
            Text(json, "reason", e.Reason);
            if (e.Success is { } success)
            {
                json.WriteBoolean("success", success);
            }
            else
            {
                json.WriteNull("success");
            }

            Text(json, "data", e.Data);
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static byte[] ComputeHash(byte[] prevHash, AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(prevHash);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(prevHash);
        sha.AppendData(Serialize(entry));
        return sha.GetHashAndReset();
    }

    /// <summary>Chains <paramref name="entry"/> onto <paramref name="prevHash"/>: sets both hash columns.</summary>
    public static void Seal(AuditEntry entry, byte[] prevHash)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.OccurredAt = Truncate(entry.OccurredAt);
        entry.PrevHash = prevHash;
        entry.Hash = ComputeHash(prevHash, entry);
    }

    /// <summary>Event data as compact JSON (property order as given by the caller's object).</summary>
    public static string? DataJson(object? data) => data switch
    {
        null => null,
        string s => s,
        _ => JsonSerializer.Serialize(data, DataOptions),
    };

    private static readonly JsonSerializerOptions DataOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static void Text(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null)
        {
            json.WriteNull(name);
        }
        else
        {
            json.WriteString(name, value);
        }
    }
}
