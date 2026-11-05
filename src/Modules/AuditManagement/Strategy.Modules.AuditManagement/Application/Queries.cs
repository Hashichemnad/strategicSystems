using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.DocumentRegistry.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Modules.AuditManagement.Application;

public sealed record AuditOptionDto(string Code, string? LabelAr, string? LabelEn);

public sealed record AuditOptionsDto(
    IReadOnlyDictionary<string, IReadOnlyList<AuditOptionDto>> Lookups, bool IsOffice, bool CanDashboard, bool CanExport, bool CanSettings, bool LifecycleEnabled,
    bool CanLifecycle, bool CanWorkPapers, bool FollowUpReview, string CapObservations, bool CapAvailable, bool RiskAvailable, bool DocumentsAvailable);

public sealed record AuditReportPermissionsDto(bool Edit, bool Issue, bool Cancel, bool AddFinding);

public sealed record AuditReportListItemDto(
    Guid Id, string Number, string? TitleAr, string? TitleEn, string TypeCode, string? AuditorBody, DateOnly? ReportDate, string Status, AuditPersonDto? LeadAuditor,
    int Findings, int OpenFindings, int LateFindings, int ClosedFindings);

public sealed record AuditReportDto(
    Guid Id, string Number, AuditReportValues Values, IReadOnlyList<AuditUnitDto> AuditedUnits, IReadOnlyList<AuditPersonDto> Distribution, AuditPersonDto? LeadAuditor,
    string Status, DateTimeOffset? IssuedAt, DateTimeOffset? ClosedAt, string? CancelReason, Guid? EngagementId, int Findings, int OpenFindings, int ClosedFindings,
    AuditReportPermissionsDto Can, uint Version);

public sealed record AuditFindingPermissionsDto(bool Edit, bool Cancel, bool AddRecommendation, bool LinkRisk, bool SendToCap, bool FollowUp);

public sealed record AuditFindingListItemDto(
    Guid Id, string Number, Guid ReportId, string ReportNumber, string ReportTypeCode, string? TitleAr, string? TitleEn, string TypeCode, string Priority, string Status,
    decimal Completion, AuditUnitDto? ResponsibleUnit, AuditUnitDto? Department, DateOnly FindingDate, int Year, DateOnly? ExpectedClosingOn, DateOnly? ActualClosedOn,
    bool Repeat, int Recommendations, int OpenRecommendations, DateOnly? NextDueOn, DateOnly? NextFollowUpOn, string? LastComment, AuditPersonDto? Auditor);

public sealed record AuditFindingRiskDto(Guid Id, Guid RiskId, string? RiskNumber, string? RiskTitleAr, string? RiskTitleEn, string? RiskStatus, string? ResidualBand,
    Guid? ControlId, string? ControlNameAr, string? ControlNameEn, string? ControlEffectiveness, bool Flagged, string? ControlLoweredTo, bool Visible, string? Link);

public sealed record AuditFindingRefDto(Guid Id, string Number, string? TitleAr, string? TitleEn, string Status);

public sealed record AuditCapRemarkDto(Guid RemarkId, string Number, string? Status, string? Stage, string Link);

public sealed record AuditFindingDto(
    Guid Id, string Number, Guid ReportId, string ReportNumber, string? ReportTitleAr, string? ReportTitleEn, string ReportTypeCode, string ReportStatus, FindingValues Values,
    AuditUnitDto? ResponsibleUnit, AuditUnitDto? Department, AuditPersonDto? Auditor, AuditFindingRefDto? RepeatOf, DocumentBadge? CriteriaDocument, string Status,
    decimal Completion, DateOnly? ActualClosedOn, string? CancelReason, AuditCapRemarkDto? CapRemark, IReadOnlyList<AuditFindingRiskDto> Risks,
    AuditFindingPermissionsDto Can, uint Version);

public sealed record AuditFileDto(Guid Id, string Name, string ContentType, long SizeBytes);

public sealed record AuditFollowUpDto(
    Guid Id, string Number, Guid RecommendationId, DateOnly FollowUpOn, DateOnly? AchievedOn, decimal Pct, string? ActionsImplemented, string? EvidenceText,
    DateOnly? NextFollowUpOn, string? Comment, AuditPersonDto? RecordedBy, string ReviewStatus, string? ReviewNote, bool Applied, DateTimeOffset CreatedAt,
    IReadOnlyList<AuditFileDto> Files);

public sealed record AuditLogDto(Guid Id, string Kind, DateOnly? PreviousDueOn, DateOnly? NewDueOn, string Justification, string? Reference, AuditPersonDto? RecordedBy,
    DateTimeOffset At);

public sealed record AuditRecommendationDto(
    Guid Id, string Number, Guid FindingId, RecommendationValues Values, AuditPersonDto? Contact, string Status, decimal LatestPct, DateOnly EffectiveDue,
    DateOnly? RevisedDueOn, string? RevisedReason, DateOnly? CompletedOn, DateOnly? LastFollowUpOn, DateOnly? NextFollowUpOn, string? LastComment,
    string? RiskAcceptanceJustification, string? RiskAcceptanceReference, DateOnly? RiskAcceptedOn, string? CancelReason, bool PendingReview,
    IReadOnlyList<AuditFollowUpDto> FollowUps, IReadOnlyList<AuditLogDto> Log, uint Version);

public sealed record ListAuditReports(PageRequest Page, string? Search, string? TypeCode, string? Status, int? Year) : IQuery<PagedResult<AuditReportListItemDto>>;

public sealed record GetAuditReport(Guid Id) : IQuery<AuditReportDto>;

public sealed record ListAuditFindings(
    PageRequest Page, string? View, string? Search, Guid? ReportId, string? Status, string? Priority, Guid? UnitId, int? Year, string? ReportTypeCode)
    : IQuery<PagedResult<AuditFindingListItemDto>>;

public sealed record GetAuditFinding(Guid Id) : IQuery<AuditFindingDto>;

public sealed record ListAuditRecommendations(Guid FindingId) : IQuery<IReadOnlyList<AuditRecommendationDto>>;

public sealed record GetAuditRecommendation(Guid Id) : IQuery<AuditRecommendationDto>;

public sealed record GetAuditOptions : IQuery<AuditOptionsDto>;

public sealed record ListSimilarFindings(Guid Id) : IQuery<IReadOnlyList<AuditSimilarFindingDto>>;

public sealed record AuditSimilarFindingDto(Guid Id, string Number, string? TitleAr, string? TitleEn, AuditUnitDto? Unit, int Year, string Status, double Score);

/// <summary>Maps records for the API and works out what the caller may do (docs/design/audit.md §5).</summary>
internal sealed class AuditViews(AuditMgmtDbContext db, AuditReader reader, IFileService files, IRequestContext context, IServiceProvider services)
{
    public async Task<AuditReportDto> ToDtoAsync(AuditReport r, CancellationToken ct)
    {
        var user = context.User;
        var office = await reader.IsOfficeAsync(user, ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync([r.LeadAuditorEmployeeId, .. r.DistributionEmployeeIds.Select(i => (Guid?)i)], ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(r.AuditedUnitIds.Select(i => (Guid?)i), ct).ConfigureAwait(false);
        var visible = (await reader.FindingsAsync(user, ct).ConfigureAwait(false)).Where(f => f.ReportId == r.Id);
        var statuses = await visible.Select(f => f.Status).ToListAsync(ct).ConfigureAwait(false);
        return new AuditReportDto(r.Id, r.Number, r.Values(), r.AuditedUnitIds.Select(u => AuditReader.Unit(u, units)!).ToList(),
            r.DistributionEmployeeIds.Select(p => AuditReader.Person(p, people)!).ToList(), AuditReader.Person(r.LeadAuditorEmployeeId, people), r.Status, r.IssuedAt,
            r.ClosedAt, r.CancelReason, r.EngagementId, statuses.Count, statuses.Count(s => !AuditFindingStatuses.IsFinal(s)),
            statuses.Count(s => s is AuditFindingStatuses.Closed or AuditFindingStatuses.RiskAccepted),
            new AuditReportPermissionsDto(office && r.IsOpen, office && r.Status == ReportStatuses.Draft, office && r.IsOpen, office && r.IsOpen), r.Version);
    }

    public async Task<IReadOnlyList<AuditFindingListItemDto>> ListItemsAsync(IReadOnlyList<Finding> findings, CancellationToken ct)
    {
        var reportIds = findings.Select(f => f.ReportId).Distinct().ToList();
        var reports = await db.Reports.AsNoTracking().Where(r => reportIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct).ConfigureAwait(false);
        var ids = findings.Select(f => f.Id).ToList();
        var recs = await db.Recommendations.AsNoTracking().Where(r => ids.Contains(r.FindingId)).ToListAsync(ct).ConfigureAwait(false);
        var byFinding = recs.ToLookup(r => r.FindingId);
        var units = await reader.UnitsAsync(findings.SelectMany(f => new Guid?[] { f.ResponsibleUnitId, f.DepartmentUnitId }), ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(findings.Select(f => f.AuditorEmployeeId), ct).ConfigureAwait(false);
        return findings.Select(f =>
        {
            var mine = byFinding[f.Id].ToList();
            var open = mine.Where(r => r.IsOpen).ToList();
            var report = reports[f.ReportId];
            return new AuditFindingListItemDto(f.Id, f.Number, f.ReportId, report.Number, report.TypeCode, f.TitleAr, f.TitleEn, f.TypeCode, f.Priority, f.Status, f.Completion,
                AuditReader.Unit(f.ResponsibleUnitId, units), AuditReader.Unit(f.DepartmentUnitId, units), f.FindingDate, f.Year, f.ExpectedClosingOn, f.ActualClosedOn, f.Repeat,
                mine.Count(r => r.Status != RecommendationStatuses.Cancelled), open.Count, open.Count == 0 ? null : open.Min(r => r.EffectiveDue),
                open.Where(r => r.NextFollowUpOn != null).Select(r => r.NextFollowUpOn).Min(),
                mine.Where(r => r.LastComment != null).OrderByDescending(r => r.LastFollowUpOn).Select(r => r.LastComment).FirstOrDefault(),
                AuditReader.Person(f.AuditorEmployeeId, people));
        }).ToList();
    }

    public async Task<AuditFindingDto> ToDtoAsync(Finding f, CancellationToken ct)
    {
        var user = context.User;
        var office = await reader.IsOfficeAsync(user, ct).ConfigureAwait(false);
        var report = await db.Reports.AsNoTracking().FirstAsync(r => r.Id == f.ReportId, ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync([f.ResponsibleUnitId, f.DepartmentUnitId], ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync([f.AuditorEmployeeId], ct).ConfigureAwait(false);
        AuditFindingRefDto? repeatOf = null;
        if (f.RepeatOfFindingId is { } repeatId && await reader.FindingAsync(repeatId, user, ct).ConfigureAwait(false) is { } earlier)
        {
            repeatOf = new AuditFindingRefDto(earlier.Id, earlier.Number, earlier.TitleAr, earlier.TitleEn, earlier.Status);
        }

        DocumentBadge? document = null;
        if (f.CriteriaDocumentId is { } documentId && services.GetService(typeof(IDocumentDirectory)) is IDocumentDirectory documents)
        {
            document = await documents.GetAsync(documentId, user, ct).ConfigureAwait(false);
        }

        AuditCapRemarkDto? cap = null;
        if (f.CapRemarkId is { } remarkId)
        {
            string? status = null, stage = null;
            if (services.GetService(typeof(ICapRemarkSources)) is ICapRemarkSources sources
                && await sources.GetProgressAsync(AuditReport.EntityType, report.Id, ct).ConfigureAwait(false) is { } progress
                && progress.Remarks.FirstOrDefault(r => r.RemarkId == remarkId) is { } remark)
            {
                status = remark.Status;
                stage = remark.Stage;
            }

            cap = new AuditCapRemarkDto(remarkId, f.CapRemarkNumber ?? string.Empty, status, stage, $"/cap/remarks/{remarkId:D}");
        }

        var risks = await RisksAsync(f, user, ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var open = f.Status != AuditFindingStatuses.Cancelled;
        var can = new AuditFindingPermissionsDto(office && open, office && open, office && open, office && open,
            office && open && f.TypeCode == FindingTypes.Improvement && f.CapRemarkId is null && settings.CapObservations == CapObservationModes.Manual
                && services.GetService(typeof(ICapRemarkSources)) is not null && report.Status != ReportStatuses.Draft,
            office && open);
        return new AuditFindingDto(f.Id, f.Number, report.Id, report.Number, report.TitleAr, report.TitleEn, report.TypeCode, report.Status, f.Values(),
            AuditReader.Unit(f.ResponsibleUnitId, units), AuditReader.Unit(f.DepartmentUnitId, units), AuditReader.Person(f.AuditorEmployeeId, people), repeatOf, document,
            f.Status, f.Completion, f.ActualClosedOn, f.CancelReason, cap, risks, can, f.Version);
    }

    private async Task<IReadOnlyList<AuditFindingRiskDto>> RisksAsync(Finding f, ClaimsPrincipal user, CancellationToken ct)
    {
        var links = await db.FindingRisks.AsNoTracking().Where(l => l.FindingId == f.Id && l.Active).OrderBy(l => l.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        if (links.Count == 0)
        {
            return [];
        }

        var directory = services.GetService(typeof(Modules.Risk.Contracts.IRiskDirectory)) as Modules.Risk.Contracts.IRiskDirectory;
        var result = new List<AuditFindingRiskDto>();
        foreach (var l in links)
        {
            var risk = directory is null ? null : await directory.GetRiskAsync(l.RiskId, user, ct).ConfigureAwait(false);
            var control = risk is not null && l.ControlId is { } controlId && directory is not null
                ? (await directory.ListControlsAsync(l.RiskId, user, ct).ConfigureAwait(false)).FirstOrDefault(c => c.Id == controlId)
                : null;
            result.Add(new AuditFindingRiskDto(l.Id, l.RiskId, risk?.Number ?? l.RiskNumber, risk?.TitleAr, risk?.TitleEn, risk?.Status, risk?.ResidualBand, l.ControlId,
                control?.NameAr, control?.NameEn, control?.EffectivenessCode, l.FlaggedForReassessment, l.ControlLoweredTo, risk is not null, risk?.Link));
        }

        return result;
    }

    public async Task<IReadOnlyList<AuditRecommendationDto>> RecommendationsAsync(IReadOnlyList<Recommendation> recs, CancellationToken ct)
    {
        var ids = recs.Select(r => r.Id).ToList();
        var followUps = await db.FollowUps.AsNoTracking().Where(u => ids.Contains(u.RecommendationId)).OrderByDescending(u => u.FollowUpOn).ThenByDescending(u => u.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        var logs = await db.Logs.AsNoTracking().Where(l => ids.Contains(l.RecommendationId)).OrderByDescending(l => l.At).ToListAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(recs.Select(r => r.ContactEmployeeId).Concat(followUps.Select(u => u.RecordedByEmployeeId))
            .Concat(logs.Select(l => l.RecordedByEmployeeId)), ct).ConfigureAwait(false);
        var fileMap = new Dictionary<Guid, List<AuditFileDto>>();
        foreach (var u in followUps)
        {
            fileMap[u.Id] = (await files.ListAsync(FollowUp.EntityType, u.Id.ToString("D"), AuditFilePurposes.Evidence.Key, ct).ConfigureAwait(false))
                .Select(x => new AuditFileDto(x.Id, x.Name, x.ContentType, x.SizeBytes)).ToList();
        }

        return recs.Select(r => new AuditRecommendationDto(r.Id, r.Number, r.FindingId, r.Values(), AuditReader.Person(r.ContactEmployeeId, people), r.Status, r.LatestPct,
            r.EffectiveDue, r.RevisedDueOn, r.RevisedReason, r.CompletedOn, r.LastFollowUpOn, r.NextFollowUpOn, r.LastComment, r.RiskAcceptanceJustification,
            r.RiskAcceptanceReference, r.RiskAcceptedOn, r.CancelReason, followUps.Any(u => u.RecommendationId == r.Id && u.ReviewStatus == ReviewStatuses.Pending),
            followUps.Where(u => u.RecommendationId == r.Id).Select(u => new AuditFollowUpDto(u.Id, u.Number, u.RecommendationId, u.FollowUpOn, u.AchievedOn, u.Pct,
                u.ActionsImplemented, u.EvidenceText, u.NextFollowUpOn, u.Comment, AuditReader.Person(u.RecordedByEmployeeId, people), u.ReviewStatus, u.ReviewNote, u.Applied,
                u.CreatedAt, fileMap.GetValueOrDefault(u.Id) ?? [])).ToList(),
            logs.Where(l => l.RecommendationId == r.Id).Select(l => new AuditLogDto(l.Id, l.Kind, l.PreviousDueOn, l.NewDueOn, l.Justification, l.Reference,
                AuditReader.Person(l.RecordedByEmployeeId, people), l.At)).ToList(), r.Version)).ToList();
    }
}

internal sealed class AuditQueries(
    AuditMgmtDbContext db, AuditReader reader, AuditViews views, AuditDeadlines deadlines, ILookupService lookups, IFeatureFlags features, IRequestContext context,
    IServiceProvider services)
    : IQueryHandler<ListAuditReports, PagedResult<AuditReportListItemDto>>, IQueryHandler<GetAuditReport, AuditReportDto>,
        IQueryHandler<ListAuditFindings, PagedResult<AuditFindingListItemDto>>, IQueryHandler<GetAuditFinding, AuditFindingDto>,
        IQueryHandler<ListAuditRecommendations, IReadOnlyList<AuditRecommendationDto>>, IQueryHandler<GetAuditRecommendation, AuditRecommendationDto>,
        IQueryHandler<GetAuditOptions, AuditOptionsDto>, IQueryHandler<ListSimilarFindings, IReadOnlyList<AuditSimilarFindingDto>>
{
    public static readonly IReadOnlyList<string> Views = ["all", "open", "late", "closed", "risk-accepted", "mine", "my-unit", "repeat", "due-soon"];

    public async Task<Result<PagedResult<AuditReportListItemDto>>> HandleAsync(ListAuditReports query, CancellationToken cancellationToken)
    {
        var source = await reader.ReportsAsync(context.User, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like.Pattern(query.Search);
            source = source.Where(r => EF.Functions.ILike(r.Number, pattern, "\\") || EF.Functions.ILike(r.TitleAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(r.TitleEn ?? string.Empty, pattern, "\\") || EF.Functions.ILike(r.AuditorBody ?? string.Empty, pattern, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(query.TypeCode))
        {
            source = source.Where(r => r.TypeCode == query.TypeCode);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(r => r.Status == query.Status);
        }

        if (query.Year is { } year)
        {
            var from = new DateOnly(year, 1, 1);
            var to = new DateOnly(year, 12, 31);
            source = source.Where(r => r.ReportDate >= from && r.ReportDate <= to);
        }

        var page = query.Page.Normalised();
        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await source.OrderByDescending(r => r.ReportDate).ThenByDescending(r => r.Number).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = rows.Select(r => r.Id).ToList();
        var visible = (await reader.FindingsAsync(context.User, cancellationToken).ConfigureAwait(false)).Where(f => ids.Contains(f.ReportId));
        var counts = await visible.GroupBy(f => f.ReportId).Select(g => new
        {
            g.Key,
            Total = g.Count(),
            Late = g.Count(f => f.Status == AuditFindingStatuses.Late),
            Closed = g.Count(f => f.Status == AuditFindingStatuses.Closed || f.Status == AuditFindingStatuses.RiskAccepted),
            Open = g.Count(f => f.Status == AuditFindingStatuses.Open || f.Status == AuditFindingStatuses.Late),
        }).ToDictionaryAsync(x => x.Key, cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync(rows.Select(r => r.LeadAuditorEmployeeId), cancellationToken).ConfigureAwait(false);
        return new PagedResult<AuditReportListItemDto>(rows.Select(r => new AuditReportListItemDto(r.Id, r.Number, r.TitleAr, r.TitleEn, r.TypeCode, r.AuditorBody, r.ReportDate,
            r.Status, AuditReader.Person(r.LeadAuditorEmployeeId, people), counts.GetValueOrDefault(r.Id)?.Total ?? 0, counts.GetValueOrDefault(r.Id)?.Open ?? 0,
            counts.GetValueOrDefault(r.Id)?.Late ?? 0, counts.GetValueOrDefault(r.Id)?.Closed ?? 0)).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<Result<AuditReportDto>> HandleAsync(GetAuditReport query, CancellationToken cancellationToken) =>
        await reader.ReportAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is { } r
            ? await views.ToDtoAsync(r, cancellationToken).ConfigureAwait(false)
            : Error.NotFound();

    public async Task<Result<PagedResult<AuditFindingListItemDto>>> HandleAsync(ListAuditFindings query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var source = await reader.FindingsAsync(user, cancellationToken).ConfigureAwait(false);
        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        switch (query.View)
        {
            case "open":
                source = source.Where(f => f.Status == AuditFindingStatuses.Open || f.Status == AuditFindingStatuses.Late);
                break;
            case "late":
                source = source.Where(f => f.Status == AuditFindingStatuses.Late);
                break;
            case "closed":
                source = source.Where(f => f.Status == AuditFindingStatuses.Closed);
                break;
            case "risk-accepted":
                source = source.Where(f => f.Status == AuditFindingStatuses.RiskAccepted);
                break;
            case "repeat":
                source = source.Where(f => f.Repeat);
                break;
            case "mine":
                {
                    var me = user.EmployeeIdOf() ?? Guid.Empty;
                    source = source.Where(f => f.AuditorEmployeeId == me);
                    break;
                }

            case "my-unit":
                if (user.EmployeeIdOf() is { } employee && await reader.EmployeeAsync(employee, cancellationToken).ConfigureAwait(false) is { } person)
                {
                    var units = await reader.UnitAndBelowAsync(person.UnitId, cancellationToken).ConfigureAwait(false);
                    source = source.Where(f => units.Contains(f.ResponsibleUnitId) || units.Contains(f.DepartmentUnitId));
                }
                else
                {
                    source = source.Where(_ => false);
                }

                break;
            case "due-soon":
                {
                    var limit = today.AddDays(30);
                    var due = db.Recommendations.Where(r => (r.Status == RecommendationStatuses.NotStarted || r.Status == RecommendationStatuses.Continuing)
                        && (r.RevisedDueOn ?? r.DueOn) <= limit).Select(r => r.FindingId);
                    source = source.Where(f => due.Contains(f.Id));
                    break;
                }
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like.Pattern(query.Search);
            source = source.Where(f => EF.Functions.ILike(f.Number, pattern, "\\") || EF.Functions.ILike(f.TitleAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(f.TitleEn ?? string.Empty, pattern, "\\") || EF.Functions.ILike(f.Condition ?? string.Empty, pattern, "\\"));
        }

        if (query.ReportId is { } reportId)
        {
            source = source.Where(f => f.ReportId == reportId);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(f => f.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Priority))
        {
            source = source.Where(f => f.Priority == query.Priority);
        }

        if (query.Year is { } year)
        {
            source = source.Where(f => f.Year == year);
        }

        if (query.UnitId is { } unitId)
        {
            var units = await reader.UnitAndBelowAsync(unitId, cancellationToken).ConfigureAwait(false);
            source = source.Where(f => units.Contains(f.ResponsibleUnitId));
        }

        if (!string.IsNullOrWhiteSpace(query.ReportTypeCode))
        {
            var typed = db.Reports.Where(r => r.TypeCode == query.ReportTypeCode).Select(r => r.Id);
            source = source.Where(f => typed.Contains(f.ReportId));
        }

        var page = query.Page.Normalised();
        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var ordered = (page.Sort ?? "-number") switch
        {
            "number" => source.OrderBy(f => f.Number),
            "findingDate" => source.OrderBy(f => f.FindingDate).ThenBy(f => f.Number),
            "-findingDate" => source.OrderByDescending(f => f.FindingDate).ThenByDescending(f => f.Number),
            _ => source.OrderByDescending(f => f.Number),
        };
        var rows = await ordered.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<AuditFindingListItemDto>(await views.ListItemsAsync(rows, cancellationToken).ConfigureAwait(false), page.Page, page.PageSize, total);
    }

    public async Task<Result<AuditFindingDto>> HandleAsync(GetAuditFinding query, CancellationToken cancellationToken) =>
        await reader.FindingAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is { } f
            ? await views.ToDtoAsync(f, cancellationToken).ConfigureAwait(false)
            : Error.NotFound();

    public async Task<Result<IReadOnlyList<AuditRecommendationDto>>> HandleAsync(ListAuditRecommendations query, CancellationToken cancellationToken)
    {
        if (await reader.FindingAsync(query.FindingId, context.User, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        var recs = await db.Recommendations.AsNoTracking().Where(r => r.FindingId == query.FindingId).OrderBy(r => r.Number).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(await views.RecommendationsAsync(recs, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<AuditRecommendationDto>> HandleAsync(GetAuditRecommendation query, CancellationToken cancellationToken) =>
        await reader.RecommendationAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is { } r
            ? (await views.RecommendationsAsync([r], cancellationToken).ConfigureAwait(false))[0]
            : Error.NotFound();

    public async Task<Result<AuditOptionsDto>> HandleAsync(GetAuditOptions query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var lists = new Dictionary<string, IReadOnlyList<AuditOptionDto>>();
        foreach (var type in AuditAccessCatalogue.Lookups)
        {
            var values = await lookups.GetValuesAsync(type.Code, cancellationToken: cancellationToken).ConfigureAwait(false);
            lists[type.Code] = values.Select(v => new AuditOptionDto(v.Code, v.LabelAr, v.LabelEn)).ToList();
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var lifecycle = await features.IsEnabledAsync(AuditPermissions.LifecycleFeature, cancellationToken).ConfigureAwait(false);
        return new AuditOptionsDto(lists, await reader.IsOfficeAsync(user, cancellationToken).ConfigureAwait(false),
            await reader.CanAsync(user, AuditPermissions.DashboardRead, cancellationToken).ConfigureAwait(false),
            await reader.CanAsync(user, AuditPermissions.Export, cancellationToken).ConfigureAwait(false),
            await reader.CanAsync(user, AuditPermissions.SettingsManage, cancellationToken).ConfigureAwait(false), lifecycle,
            lifecycle && await reader.CanAsync(user, AuditPermissions.Lifecycle, cancellationToken).ConfigureAwait(false),
            lifecycle && await reader.CanAsync(user, AuditPermissions.WorkPapers, cancellationToken).ConfigureAwait(false), settings.FollowUpReview, settings.CapObservations,
            services.GetService(typeof(ICapRemarkSources)) is not null, services.GetService(typeof(Modules.Risk.Contracts.IRiskDirectory)) is not null,
            services.GetService(typeof(IDocumentDirectory)) is not null);
    }

    /// <summary>Repeat-finding detection without a model (ADR 0137): trigram similarity over visible findings, same or other departments.</summary>
    public async Task<Result<IReadOnlyList<AuditSimilarFindingDto>>> HandleAsync(ListSimilarFindings query, CancellationToken cancellationToken)
    {
        var finding = await reader.FindingAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false);
        if (finding is null)
        {
            return Error.NotFound();
        }

        var text = $"{finding.TitleEn ?? finding.TitleAr} {finding.Condition}".Trim();
        var hits = await SimilarAsync(text, finding.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success(hits);
    }

    public async Task<IReadOnlyList<AuditSimilarFindingDto>> SimilarAsync(string text, Guid? excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var visible = await reader.FindingsAsync(context.User, ct).ConfigureAwait(false);
        var hits = await visible.Where(f => f.Id != excludeId && f.Status != AuditFindingStatuses.Cancelled)
            .Select(f => new
            {
                Finding = f,
                Score = Math.Max(
                    Math.Max(EF.Functions.TrigramsSimilarity(f.TitleEn ?? string.Empty, text), EF.Functions.TrigramsSimilarity(f.TitleAr ?? string.Empty, text)),
                    EF.Functions.TrigramsSimilarity(f.Condition ?? string.Empty, text)),
            })
            .Where(x => x.Score > 0.2).OrderByDescending(x => x.Score).Take(5).ToListAsync(ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(hits.Select(h => (Guid?)h.Finding.ResponsibleUnitId), ct).ConfigureAwait(false);
        return hits.Select(h => new AuditSimilarFindingDto(h.Finding.Id, h.Finding.Number, h.Finding.TitleAr, h.Finding.TitleEn,
            AuditReader.Unit(h.Finding.ResponsibleUnitId, units), h.Finding.Year, h.Finding.Status, h.Score)).ToList();
    }
}
