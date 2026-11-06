using System.Security.Claims;

namespace Strategy.Platform.Reporting;

/// <summary>A short record list for dashboards (Top-10 risks, delayed projects, upcoming milestones…), always live and scoped.</summary>
public sealed record ReportingListDefinition(string Code, string SystemCode, string LabelAr, string LabelEn, string Permission);

/// <remarks><c>UnitIds</c>: Restrict to records of these units (already the units the viewer chose; scope is applied by the module anyway).</remarks>
public sealed record ReportingListQuery(DateOnly From, DateOnly To, DateOnly Today, IReadOnlyCollection<Guid>? UnitIds, int Take);

/// <summary>One row. <paramref name="Tone"/>: neutral, info, success, warning or danger. <paramref name="Link"/>: SPA deep link.</summary>
public sealed record ReportingListItem(
    string ResourceType,
    Guid Id,
    string? Number,
    string? TitleAr,
    string? TitleEn,
    string? StatusCode,
    string Tone,
    decimal? Value,
    DateOnly? Date,
    Guid? UnitId,
    string Link,
    string? DetailAr = null,
    string? DetailEn = null);

public interface IReportingListSource
{
    string SystemCode { get; }

    IReadOnlyList<ReportingListDefinition> Lists { get; }

    /// <summary>Only records <paramref name="user"/> may read (the module's reader), ordered by relevance, at most <c>Take</c>.</summary>
    Task<IReadOnlyList<ReportingListItem>> GetAsync(string code, ReportingListQuery query, ClaimsPrincipal user, CancellationToken cancellationToken);
}
