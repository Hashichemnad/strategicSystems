using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Integration;

/// <summary>Kinds of sync-target fields (ADR 0202). The engine converts and checks source values into these before writing.</summary>
public static class SyncFieldKinds
{
    public const string Text = "text";
    public const string Date = "date";
    public const string DateTime = "datetime";
    public const string Number = "decimal";

    /// <summary>numeric(9,4) ratio, 1.0 = 100 % (CLAUDE.md §6).</summary>
    public const string Ratio = "ratio";
    public const string WholeNumber = "int";
    public const string Bool = "bool";

    /// <summary>One of a fixed list of codes defined in code (e.g. a stage).</summary>
    public const string Code = "code";

    /// <summary>An active code of a lookup type (<c>settings.lookup_value</c>).</summary>
    public const string Lookup = "lookup";

    /// <summary>A jsonb array of codes of a lookup type (multi-select).</summary>
    public const string Codes = "codes";

    /// <summary>An employee id (people matching, ADR 0204).</summary>
    public const string Person = "person";

    /// <summary>An organisation unit id.</summary>
    public const string Unit = "unit";

    /// <summary>The id of a record of another sync target (resolved through its external reference).</summary>
    public const string Reference = "reference";

    public static readonly IReadOnlyList<string> All = [Text, Date, DateTime, Number, Ratio, WholeNumber, Bool, Code, Lookup, Codes, Person, Unit, Reference];
}

/// <summary>
/// One writable field of a sync target. <c>Property</c> is the entity property (generic writer) or a key the custom writer understands.
/// <c>Choices</c>: the fixed codes of a <see cref="SyncFieldKinds.Code"/> field; <c>LookupType</c>: of a lookup / codes field;
/// <c>ReferenceType</c>: the target type of a reference. <c>NumberingFormat</c> (release 1.0.2, ADR 0220): the field is a display number —
/// a label of the synced record, never unique — and the numbering service's counter of that format is raised past the synced value, so a
/// number made here later never repeats a Salesforce label.
/// </summary>
public sealed record SyncTargetField(
    string Code,
    string Kind,
    string Property,
    bool Required = false,
    string? LookupType = null,
    string? ReferenceType = null,
    IReadOnlyList<string>? Choices = null,
    int? MaxLength = null,
    string? NumberingFormat = null);

/// <summary>
/// A record type the sync may write, declared by its owning module (ADR 0202). With <c>ContextType</c> / <c>EntityType</c> the generic entity
/// writer of Platform.Integration writes the declared properties through the module's own DbContext; otherwise the module registers an
/// <see cref="ISyncTargetWriter"/> for <c>Code</c>.
/// </summary>
public sealed class SyncTargetDefinition
{
    private readonly List<SyncTargetField> _fields = [];
    private readonly Dictionary<string, object?> _defaults = new(StringComparer.Ordinal);

    public SyncTargetDefinition(string code, string systemCode, string nameAr, string nameEn, Type? contextType = null, Type? entityType = null)
    {
        Code = code;
        SystemCode = systemCode;
        NameAr = nameAr;
        NameEn = nameEn;
        ContextType = contextType;
        EntityClrType = entityType;
    }

    /// <summary>Entity type code (the audit / links / external reference type, e.g. <c>portfolio.project</c>).</summary>
    public string Code { get; }

    public string SystemCode { get; }

    public string NameAr { get; }

    public string NameEn { get; }

    public Type? ContextType { get; }

    public Type? EntityClrType { get; }

    /// <summary>Type of an <see cref="ISyncTargetHook"/> run after each write in the same transaction (record roles and other derived data).</summary>
    public Type? HookType { get; private set; }

    public IReadOnlyList<SyncTargetField> Fields => _fields;

    /// <summary>Values set on a new record when the mapping gives none (target field code → value in the field's kind).</summary>
    public IReadOnlyDictionary<string, object?> Defaults => _defaults;

    public SyncTargetField? Field(string code) => _fields.FirstOrDefault(f => string.Equals(f.Code, code, StringComparison.Ordinal));

    public SyncTargetDefinition Add(SyncTargetField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!SyncFieldKinds.All.Contains(field.Kind))
        {
            throw new ArgumentException($"Unknown sync field kind '{field.Kind}'.", nameof(field));
        }

        if (Field(field.Code) is not null)
        {
            throw new ArgumentException($"Duplicate sync field '{field.Code}' on '{Code}'.", nameof(field));
        }

        _fields.Add(field);
        return this;
    }

    public SyncTargetDefinition Text(string code, string property, bool required = false, int? maxLength = null) =>
        Add(new(code, SyncFieldKinds.Text, property, required, MaxLength: maxLength));

    /// <summary>A display number (a label, not unique; ADR 0220) of the numbering format <paramref name="numberingFormat"/>.</summary>
    public SyncTargetDefinition DisplayNumber(string code, string property, string numberingFormat, bool required = false) =>
        Add(new(code, SyncFieldKinds.Text, property, required, NumberingFormat: numberingFormat));

    public SyncTargetDefinition Date(string code, string property, bool required = false) => Add(new(code, SyncFieldKinds.Date, property, required));

    public SyncTargetDefinition Timestamp(string code, string property, bool required = false) => Add(new(code, SyncFieldKinds.DateTime, property, required));

    public SyncTargetDefinition Number(string code, string property, bool required = false) => Add(new(code, SyncFieldKinds.Number, property, required));

    public SyncTargetDefinition Ratio(string code, string property, bool required = false) => Add(new(code, SyncFieldKinds.Ratio, property, required));

    public SyncTargetDefinition WholeNumber(string code, string property, bool required = false) => Add(new(code, SyncFieldKinds.WholeNumber, property, required));

    public SyncTargetDefinition Bool(string code, string property, bool required = false) => Add(new(code, SyncFieldKinds.Bool, property, required));

    public SyncTargetDefinition Choice(string code, string property, IReadOnlyList<string> choices, bool required = false) =>
        Add(new(code, SyncFieldKinds.Code, property, required, Choices: choices));

    public SyncTargetDefinition Lookup(string code, string property, string lookupType, bool required = false) =>
        Add(new(code, SyncFieldKinds.Lookup, property, required, LookupType: lookupType));

    public SyncTargetDefinition Codes(string code, string property, string lookupType) =>
        Add(new(code, SyncFieldKinds.Codes, property, LookupType: lookupType));

    public SyncTargetDefinition Person(string code, string property, bool required = false) => Add(new(code, SyncFieldKinds.Person, property, required));

    public SyncTargetDefinition Unit(string code, string property, bool required = false) => Add(new(code, SyncFieldKinds.Unit, property, required));

    public SyncTargetDefinition Reference(string code, string property, string referenceType, bool required = false) =>
        Add(new(code, SyncFieldKinds.Reference, property, required, ReferenceType: referenceType));

    public SyncTargetDefinition Default(string code, object? value)
    {
        _defaults[code] = value;
        return this;
    }

    public SyncTargetDefinition After<THook>()
        where THook : ISyncTargetHook
    {
        HookType = typeof(THook);
        return this;
    }
}

/// <summary>Builders of sync-target declarations.</summary>
public static class SyncTarget
{
    /// <summary>A target written by the generic entity writer through <typeparamref name="TContext"/> (a module DbContext).</summary>
    public static SyncTargetDefinition Entity<TContext, TEntity>(string code, string systemCode, string nameAr, string nameEn)
        where TContext : class
        where TEntity : class =>
        new(code, systemCode, nameAr, nameEn, typeof(TContext), typeof(TEntity));

    /// <summary>A target written by a module's own <see cref="ISyncTargetWriter"/>.</summary>
    public static SyncTargetDefinition Custom(string code, string systemCode, string nameAr, string nameEn) => new(code, systemCode, nameAr, nameEn);
}

/// <summary>
/// One write of a synced record. <c>Values</c> are converted to the field kinds (string, DateOnly, DateTimeOffset, decimal, int, bool, Guid,
/// string[] for codes); only fields present in the mapping are in it. <c>ExistingId</c>: the record of the external reference, null for a new record.
/// </summary>
public sealed record SyncWrite(
    string TargetType,
    Guid? ExistingId,
    IReadOnlyDictionary<string, object?> Values,
    string SourceSystem,
    string ExternalId);

/// <summary>
/// Writes a synced record for a target declared without an entity (custom invariants). The writer saves in a transaction of its own DbContext
/// and calls <c>linkInTransaction</c> with that context before committing, so the external reference commits with the record.
/// </summary>
public interface ISyncTargetWriter
{
    string TargetType { get; }

    Task<Result<Guid>> WriteAsync(SyncWrite write, Func<object, Guid, Task<Result>> linkInTransaction, CancellationToken cancellationToken);
}

/// <summary>Runs after a generic write in the same transaction (<c>transactionContext</c> = the module's DbContext).</summary>
public interface ISyncTargetHook
{
    Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken);
}

public static class SyncTargetServiceCollectionExtensions
{
    /// <summary>Declares a sync target (ADR 0202). Its hook type, when any, is registered as a scoped service.</summary>
    public static IServiceCollection AddSyncTarget(this IServiceCollection services, SyncTargetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        services.AddSingleton(definition);
        if (definition.HookType is { } hook)
        {
            services.AddScoped(hook);
        }

        return services;
    }

    /// <summary>Declares a custom-written sync target and its writer.</summary>
    public static IServiceCollection AddSyncTarget<TWriter>(this IServiceCollection services, SyncTargetDefinition definition)
        where TWriter : class, ISyncTargetWriter
    {
        services.AddSyncTarget(definition);
        services.AddScoped<ISyncTargetWriter, TWriter>();
        return services;
    }
}

/// <summary>Finds employees for people matching (implemented by Platform.Organisation, ADR 0204). Employees are HR master data: never created by the sync.</summary>
public interface IEmployeeMatcher
{
    /// <summary>Employee whose e-mail / employee number / Federation ID (<paramref name="key"/>: <c>email</c>, <c>employeeNo</c>, <c>federationId</c>) equals <paramref name="value"/>.</summary>
    Task<Guid?> FindAsync(string key, string value, CancellationToken cancellationToken);
}
