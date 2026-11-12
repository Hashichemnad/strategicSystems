using System.Security.Claims;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;

namespace Strategy.Platform.Integration.Feeds;

/// <summary>A record type as one caller may read it: only the fields and relationships allowed for that caller.</summary>
public sealed record VisibleRecordType(
    ReportableRecordType Definition,
    IReportableRecordSource Source,
    IReadOnlyList<RecordFieldDefinition> Fields,
    IReadOnlyList<RecordRelationship> Relationships)
{
    public string Code => Definition.Code;

    public string EntitySet => Definition.EntitySet;

    public string SystemCode => Definition.SystemCode;

    /// <summary>OData entity type name: the last segment of the code in Pascal case (<c>portfolio.project</c> → <c>Project</c>).</summary>
    public string TypeName => TypeNameOf(Definition.Code);

    /// <summary>Implicit fields first (<c>id</c>, <c>modifiedAt</c>), then the declared ones the caller may read.</summary>
    public IEnumerable<RecordFieldDefinition> AllFields => new[] { RecordFields.IdField, RecordFields.ModifiedAtField }.Concat(Fields);

    public RecordFieldDefinition? Field(string code) => AllFields.FirstOrDefault(f => f.Code == code);

    public static string TypeNameOf(string code)
    {
        var last = code[(code.LastIndexOf('.') + 1)..];
        return string.Concat(last.Split('-', '_').Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}

/// <summary>
/// Every reportable record type of the installation (ADR 0190) and what a caller may read of them: system enabled, the caller's read
/// permission, the integration client's allowed systems / record types, field permissions; relationships only to readable types of the
/// same system.
/// </summary>
public sealed class FeedCatalog(IEnumerable<IReportableRecordSource> sources, IAccessService access, ISystemState systems)
{
    private readonly IReadOnlyList<(IReportableRecordSource Source, ReportableRecordType Type)> _all =
        [.. sources.SelectMany(s => s.RecordTypes.Select(t => (s, t)))];

    public IReadOnlyList<ReportableRecordType> AllTypes => [.. _all.Select(a => a.Type)];

    public IReadOnlyList<string> Systems => [.. _all.Select(a => a.Type.SystemCode).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    public ReportableRecordType? Find(string code) => _all.FirstOrDefault(a => a.Type.Code == code).Type;

    /// <summary>
    /// Record types of <paramref name="systemCode"/> (null = every system) the caller may read. <paramref name="allowedRecordTypes"/>:
    /// the integration client's list (empty = all).
    /// </summary>
    public async Task<IReadOnlyList<VisibleRecordType>> VisibleAsync(
        ClaimsPrincipal user, string? systemCode, IReadOnlyCollection<string> allowedRecordTypes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(allowedRecordTypes);
        var integration = user.IntegrationScopeOf();
        var candidates = new List<(IReportableRecordSource Source, ReportableRecordType Type)>();
        foreach (var (source, type) in _all.Where(a => systemCode is null || a.Type.SystemCode == systemCode))
        {
            if ((integration is not null && !integration.Systems.Contains(type.SystemCode))
                || (allowedRecordTypes.Count > 0 && !allowedRecordTypes.Contains(type.Code))
                || !await systems.IsEnabledAsync(type.SystemCode, cancellationToken).ConfigureAwait(false)
                || !await access.CanAsync(user, type.ReadPermission, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            candidates.Add((source, type));
        }

        var permissionCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        var result = new List<VisibleRecordType>();
        foreach (var (source, type) in candidates)
        {
            var fields = new List<RecordFieldDefinition>();
            foreach (var field in type.Fields)
            {
                if (field.Permission is null)
                {
                    fields.Add(field);
                    continue;
                }

                if (!permissionCache.TryGetValue(field.Permission, out var allowed))
                {
                    allowed = permissionCache[field.Permission] = await access.CanAsync(user, field.Permission, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                if (allowed)
                {
                    fields.Add(field);
                }
            }

            result.Add(new VisibleRecordType(type, source, fields, []));
        }

        // Relationships: to readable types of the same system, joined on readable fields.
        return result.Select(v => v with
        {
            Relationships = v.Definition.Relationships
                .Where(r => result.FirstOrDefault(t => t.Code == r.TargetType) is { } target && target.SystemCode == v.SystemCode
                    && v.Field(r.SourceField) is not null && target.Field(r.TargetField) is not null)
                .ToList(),
        }).ToList();
    }
}
