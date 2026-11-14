using System.Text.Json;
using System.Text.Json.Serialization;

namespace Strategy.Platform.Integration.Sync;

public static class FieldMappingKinds
{
    public const string Direct = "direct";
    public const string Picklist = "picklist";
    public const string RecordType = "recordType";
    public const string Reference = "reference";
    public const string Person = "person";
    public const string Constant = "constant";

    public static readonly IReadOnlyList<string> All = [Direct, Picklist, RecordType, Reference, Person, Constant];
}

/// <summary>One field mapping of an object mapping (ADR 0203, docs/design/salesforce-sync.md §3).</summary>
public sealed record FieldMapping
{
    /// <summary>Source field API name; one relationship hop allowed (<c>RecordType.DeveloperName</c>). Empty for constants.</summary>
    public string? Source { get; init; }

    /// <summary>Target field code of the sync target.</summary>
    public string Target { get; init; } = string.Empty;

    public string Kind { get; init; } = FieldMappingKinds.Direct;

    /// <summary>Value map (picklist / record type); key <c>*</c> = fallback.</summary>
    public Dictionary<string, string>? Values { get; init; }

    public string? Transform { get; init; }

    /// <summary>Reference mappings: the target type the Salesforce Id points at.</summary>
    public string? ReferenceType { get; init; }

    /// <summary>Constant value.</summary>
    public string? Value { get; init; }

    public bool Required { get; init; }
}

/// <summary>Match order and source fields of a people mapping (ADR 0204).</summary>
public sealed record PeopleOptions
{
    public List<string> MatchOrder { get; init; } = [MatchedBy.Email, MatchedBy.EmployeeNo, MatchedBy.FederationId];

    public string? EmailField { get; init; } = "Email";

    /// <summary>No default (ADR 0212): <c>EmployeeNumber</c> exists on User but not on Contact; a match key without a field is skipped.</summary>
    public string? EmployeeNoField { get; init; }

    /// <summary>No default (ADR 0212): <c>FederationIdentifier</c> exists on User but not on Contact.</summary>
    public string? FederationIdField { get; init; }

    public string? NameField { get; init; } = "Name";
}

/// <summary>Options of approval / field-history / files mappings (ADR 0208).</summary>
public sealed record HistoryOptions
{
    /// <summary>Field history: the source object whose history this is (e.g. <c>PMS_Project__c</c> for <c>PMS_Project__History</c>).</summary>
    public string? ParentObject { get; init; }
}

public static class ScheduleKinds
{
    public const string Interval = "interval";
    public const string Daily = "daily";
    public const string Weekly = "weekly";

    public static readonly IReadOnlyList<string> All = [Interval, Daily, Weekly];
}

/// <summary>Schedule of a connection (§4): every N hours (anchored at <c>At</c>), daily at a time, or weekly on days at a time.</summary>
public sealed record SyncScheduleSettings
{
    public string Kind { get; init; } = ScheduleKinds.Interval;

    public int EveryHours { get; init; } = 4;

    /// <summary>"HH:mm" in the installation time zone.</summary>
    public string At { get; init; } = "00:00";

    /// <summary>ISO days (Monday = 1 … Sunday = 7).</summary>
    public List<int> Days { get; init; } = [1, 2, 3, 4, 5];

    public bool BusinessHoursOnly { get; init; }
}

/// <summary>
/// API limits of the source as last read: from <c>/limits</c>, or from the <c>Sforce-Limit-Info</c> header of normal calls when <c>/limits</c>
/// is refused (ADR 0210; then the Bulk job counts are unknown). <see cref="Source"/> is null on values stored by 1.0.0 (= <c>limits</c>).
/// </summary>
public sealed record ApiLimits(int? DailyApiMax, int? DailyApiRemaining, int? BulkJobsMax, int? BulkJobsRemaining, DateTimeOffset At, string? Source = null)
{
    /// <summary>True when fewer than <paramref name="reservePercent"/> % of the daily API requests remain (the run stops; ADR 0206).</summary>
    public bool IsBelowReserve(int reservePercent) =>
        DailyApiMax is > 0 && DailyApiRemaining is { } remaining
        && remaining * 100L < DailyApiMax.Value * (long)Math.Clamp(reservePercent, 0, 90);
}

public static class ApiLimitSources
{
    public const string Limits = "limits";
    public const string Header = "header";
}

/// <summary>Counters of one object in one run.</summary>
public sealed class ObjectRunStats
{
    public int Read { get; set; }

    public int Created { get; set; }

    public int Updated { get; set; }

    public int Unchanged { get; set; }

    public int Deleted { get; set; }

    public int Failed { get; set; }

    public int Conflicts { get; set; }

    public int Skipped { get; set; }

    /// <summary><c>bulk</c> or <c>rest</c>.</summary>
    public string? Api { get; set; }

    public bool Completed { get; set; }

    /// <summary>The object was read from scratch (first load or full run): reconciliation applies (release 1.0.2, fix 6).</summary>
    public bool FullLoad { get; set; }

    /// <summary>Salesforce COUNT() versus synced records plus open exceptions after a full load; null for incremental runs.</summary>
    public ReconciliationResult? Reconciliation { get; set; }
}

/// <summary>
/// Per object after a full load (release 1.0.2, fix 6): <c>Source</c> = Salesforce <c>SELECT COUNT()</c> of live rows (the mapping's filter, not
/// deleted); <c>Synced</c> = records of the target type held with status "synced"; <c>OpenExceptions</c> = open exceptions of the mappings of that
/// target; <c>Difference</c> = Source − Synced − OpenExceptions (0 = every Salesforce row is either here or waiting in the queue). Mappings that
/// share a target type (e.g. two Salesforce objects → controls) are reconciled together and named in <c>Mappings</c>.
/// </summary>
public sealed record ReconciliationResult(int Source, int Synced, int OpenExceptions, int Difference, IReadOnlyList<string> Mappings);

internal static class SyncJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Read<T>(string? json)
        where T : new() =>
        string.IsNullOrWhiteSpace(json) ? new T() : JsonSerializer.Deserialize<T>(json, Options) ?? new T();
}
