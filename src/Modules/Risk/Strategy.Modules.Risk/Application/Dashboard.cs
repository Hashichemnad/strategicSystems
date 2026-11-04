using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting.Excel;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Risk.Application;

public sealed record RiskFilter(Guid? UnitId, string? Category, string? ResidualStatus, int? Year, bool? IncludeProjectRisks);

public sealed record RiskCountDto(string Code, int Count);

public sealed record HeatCellDto(int Likelihood, int Impact, decimal Score, string Band, int Enterprise, int Project);

public sealed record ActionBriefDto(Guid Id, string Number, string? TitleAr, string? TitleEn, Guid RiskId, string RiskNumber, DateOnly? PlannedEnd, decimal CompletionPct, RiskPersonDto? Owner);

public sealed record KriBreachDto(Guid KriId, Guid RiskId, string RiskNumber, string? RiskTitleAr, string? RiskTitleEn, string CardCode, string? CardNameAr, string? CardNameEn,
    decimal? Value, decimal Threshold, string Direction, string? Period, DateTimeOffset? At);

public sealed record MovementDto(int Improved, int Worsened, int Unchanged, IReadOnlyList<RiskListItemDto> WorsenedRisks);

public sealed record RiskDashboardDto(
    int Total, int Active, int UnderReview, int Draft, int Closed, int ProjectRisks, IReadOnlyList<RiskCountDto> ByCategory, IReadOnlyList<RiskCountDto> ByUnit,
    IReadOnlyList<RiskCountDto> ByResidualBand, IReadOnlyList<RiskCountDto> ByType, IReadOnlyList<RiskCountDto> ByResidualStatus, IReadOnlyList<HeatCellDto> Inherent,
    IReadOnlyList<HeatCellDto> Residual, IReadOnlyList<RiskListItemDto> OutsideAppetite, int OutsideAppetiteCount, IReadOnlyList<RiskListItemDto> Top10,
    IReadOnlyList<ActionBriefDto> OverdueActions, int OverdueActionsCount, IReadOnlyList<RiskListItemDto> OverdueReviews, int OverdueReviewsCount, MovementDto Movement,
    IReadOnlyList<KriBreachDto> KriBreaches, IReadOnlyList<RatingBand> Bands, IReadOnlyDictionary<Guid, RiskUnitDto> Units);

public sealed record ProjectRiskRowDto(
    Guid Id, Guid ProjectId, string ProjectNumber, string? ProjectNameAr, string? ProjectNameEn, string? Title, string? CategoryCode, int Probability, int Impact,
    int? ControlEvaluation, decimal InherentScore, string InherentBand, decimal ResidualScore, string ResidualBand, string? Response, decimal? ImplementationPct,
    RiskPersonDto? Owner, string Link);

public sealed record HeatCellRisksDto(IReadOnlyList<RiskListItemDto> Risks, IReadOnlyList<ProjectRiskRowDto> ProjectRisks);

public sealed record MechanismDto(FrameworkVersionDto Framework, IReadOnlyList<MatrixCell> Matrix, IReadOnlyList<RiskCategoryDto> Categories);

public sealed record RiskExportedFile(byte[] Content, string ContentType, string FileName);

public sealed record GetRiskDashboard(RiskFilter Filter) : IQuery<RiskDashboardDto>;

public sealed record GetHeatCellRisks(string Kind, int Likelihood, int Impact, RiskFilter Filter) : IQuery<HeatCellRisksDto>;

public sealed record ListProjectRisks(Guid? ProjectId, string? Band, string? Search) : IQuery<IReadOnlyList<ProjectRiskRowDto>>;

public sealed record GetRiskMechanism : IQuery<MechanismDto>;

public sealed record ExportRiskReport(string Kind, string Format, Guid? UnitId, string? Language) : IQuery<RiskExportedFile>;

/// <summary>Dashboard, heat-maps, Top-10, movement, KRI breaches, Project Risks (live, ADR 0103), mechanism page and reports (design §12).</summary>
internal sealed class DashboardHandlers(
    RiskDbContext db, RiskReader reader, RiskViews views, FrameworkStore frameworks, RiskDeadlines deadlines, IRequestContext context, IClock clock,
    ISettingsStore settings, IPdfRenderer pdf, IAuditLog audit, ILookupService lookups, IServiceProvider services)
    : IQueryHandler<GetRiskDashboard, RiskDashboardDto>, IQueryHandler<GetHeatCellRisks, HeatCellRisksDto>, IQueryHandler<ListProjectRisks, IReadOnlyList<ProjectRiskRowDto>>,
        IQueryHandler<GetRiskMechanism, MechanismDto>, IQueryHandler<ExportRiskReport, RiskExportedFile>
{
    private async Task<IQueryable<RiskRecord>> FilteredAsync(RiskFilter f, CancellationToken ct)
    {
        var source = (await reader.RisksAsync(context.User, ct).ConfigureAwait(false)).Where(r => r.Status != RiskStatuses.Rejected);
        if (f.UnitId is { } unitId)
        {
            var units = await reader.UnitAndBelowAsync(unitId, ct).ConfigureAwait(false);
            source = source.Where(r => units.Contains(r.UnitId));
        }

        if (!string.IsNullOrWhiteSpace(f.Category) && f.Category != RiskPermissions.ProjectCategory)
        {
            var json = $"[\"{f.Category.Replace("\"", string.Empty, StringComparison.Ordinal)}\"]";
            source = source.Where(r => EF.Functions.JsonContains(r.CategoryCodesJson, json));
        }

        if (!string.IsNullOrWhiteSpace(f.ResidualStatus))
        {
            source = source.Where(r => r.ResidualStatus == f.ResidualStatus);
        }

        if (f.Year is { } year)
        {
            source = source.Where(r => r.RegisterYear == year);
        }

        return source;
    }

    private async Task<IReadOnlyList<ProjectRiskSummary>> ProjectRisksAsync(RiskFilter f, CancellationToken ct)
    {
        var include = f.IncludeProjectRisks ?? (await reader.SettingsAsync(ct).ConfigureAwait(false)).IncludeProjectRisksByDefault;
        if (!include || (!string.IsNullOrWhiteSpace(f.Category) && f.Category != RiskPermissions.ProjectCategory) || f.UnitId is not null || f.ResidualStatus is not null
            || services.GetService(typeof(IProjectRiskDirectory)) is not IProjectRiskDirectory directory
            || !await reader.CanAsync(context.User, RiskPermissions.ProjectRisksRead, ct).ConfigureAwait(false))
        {
            return [];
        }

        return await directory.ListAsync(context.User, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<Result<RiskDashboardDto>> HandleAsync(GetRiskDashboard query, CancellationToken cancellationToken)
    {
        var f = query.Filter;
        var onlyProjects = f.Category == RiskPermissions.ProjectCategory;
        var risks = onlyProjects ? [] : await (await FilteredAsync(f, cancellationToken).ConfigureAwait(false)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var projects = await ProjectRisksAsync(f, cancellationToken).ConfigureAwait(false);
        var scorer = await frameworks.ScorerAsync(cancellationToken).ConfigureAwait(false);
        var settingsValue = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var open = risks.Where(r => r.Status != RiskStatuses.Closed).ToList();
        var active = risks.Where(r => r.Status == RiskStatuses.Active).ToList();
        List<RiskCountDto> Count(IEnumerable<string> keys) => keys.GroupBy(k => k).Select(g => new RiskCountDto(g.Key, g.Count())).OrderByDescending(c => c.Count).ToList();

        var byCategory = Count(open.SelectMany(r => r.CategoryCodes.Take(1)));
        if (projects.Count > 0)
        {
            byCategory.Add(new RiskCountDto(RiskPermissions.ProjectCategory, projects.Count));
        }

        var byBand = Count(open.Select(r => r.ResidualBand).Concat(projects.Select(p => p.ResidualBand)));
        var unitIds = open.Select(r => r.UnitId).Distinct().ToList();
        var units = await reader.UnitsAsync(unitIds.Select(u => (Guid?)u), cancellationToken).ConfigureAwait(false);
        var heatInherent = Heat(scorer, open.Select(r => (r.InherentLikelihood, r.InherentImpact)), projects.Select(p => (p.Probability, p.Impact)));
        var heatResidual = Heat(scorer, open.Select(r => (r.ResidualLikelihood ?? r.InherentLikelihood, r.ResidualImpact ?? r.InherentImpact)), []);
        var outside = active.Where(r => r.WithinAppetite == false).OrderByDescending(r => r.ResidualScore).ToList();
        var top = Top10.Rank(active, settingsValue.TopCount);
        var now = clock.UtcNow;
        var overdueReviews = active.Where(r => r.NextReviewDueAt is { } due && due < now).OrderBy(r => r.NextReviewOn).ToList();
        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        var riskIds = open.Select(r => r.Id).ToList();
        var actions = await db.Actions.AsNoTracking()
            .Where(a => riskIds.Contains(a.RiskId) && (a.Status == ActionStatuses.NotStarted || a.Status == ActionStatuses.InProgress) && a.PlannedEnd != null && a.PlannedEnd < today)
            .OrderBy(a => a.PlannedEnd).ToListAsync(cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync(actions.Select(a => a.OwnerEmployeeId), cancellationToken).ConfigureAwait(false);
        var byId = risks.ToDictionary(r => r.Id);
        var overdueActions = actions.Take(20).Select(a => new ActionBriefDto(a.Id, a.Number, a.TitleAr, a.TitleEn, a.RiskId, byId[a.RiskId].Number, a.PlannedEnd, a.CompletionPct,
            RiskReader.Person(a.OwnerEmployeeId, people))).ToList();
        var movement = await MovementAsync(active, today, cancellationToken).ConfigureAwait(false);
        var activeIds = active.Select(r => r.Id).ToList();
        var breaches = await db.Kris.AsNoTracking().Where(k => activeIds.Contains(k.RiskId) && k.Status == ControlStatuses.Active && k.LastLevel == KriLevels.Breach)
            .OrderByDescending(k => k.LastAt).Take(20).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new RiskDashboardDto(
            open.Count + projects.Count, active.Count, risks.Count(r => r.Status == RiskStatuses.UnderReview), risks.Count(r => r.Status == RiskStatuses.Draft),
            risks.Count(r => r.Status == RiskStatuses.Closed), projects.Count, byCategory, Count(open.Select(r => r.UnitId.ToString("D"))), byBand,
            Count(open.Select(r => r.RiskType)), Count(open.Select(r => r.ResidualStatus)), heatInherent, heatResidual,
            await views.ListItemsAsync(outside.Take(10).ToList(), cancellationToken).ConfigureAwait(false), outside.Count,
            await views.ListItemsAsync(top, cancellationToken).ConfigureAwait(false), overdueActions, actions.Count,
            await views.ListItemsAsync(overdueReviews.Take(10).ToList(), cancellationToken).ConfigureAwait(false), overdueReviews.Count, movement,
            breaches.Select(k => new KriBreachDto(k.Id, k.RiskId, byId[k.RiskId].Number, byId[k.RiskId].TitleAr, byId[k.RiskId].TitleEn, k.CardCode, k.CardNameAr, k.CardNameEn,
                k.LastValue, k.BreachThreshold, k.Direction, k.LastPeriod, k.LastAt)).ToList(),
            scorer.Content.Bands, units);
    }

    private static List<HeatCellDto> Heat(RiskScorer scorer, IEnumerable<(int L, int I)> enterprise, IEnumerable<(int L, int I)> project)
    {
        var e = enterprise.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var p = project.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        return scorer.Matrix().Select(c => new HeatCellDto(c.Likelihood, c.Impact, c.Score, c.Band, e.GetValueOrDefault((c.Likelihood, c.Impact)),
            p.GetValueOrDefault((c.Likelihood, c.Impact)))).ToList();
    }

    /// <summary>Improved / worsened since the start of the quarter: current residual score vs the last snapshot before it (SPEC §3.4 E).</summary>
    private async Task<MovementDto> MovementAsync(IReadOnlyList<RiskRecord> active, DateOnly today, CancellationToken ct)
    {
        var quarterStart = new DateOnly(today.Year, ((today.Month - 1) / 3 * 3) + 1, 1);
        var since = new DateTimeOffset(quarterStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var ids = active.Select(r => r.Id).ToList();
        var before = await db.Assessments.AsNoTracking().Where(a => ids.Contains(a.RiskId) && a.At < since).GroupBy(a => a.RiskId)
            .Select(g => g.OrderByDescending(a => a.At).Select(a => new { a.RiskId, a.ResidualScore }).First()).ToListAsync(ct).ConfigureAwait(false);
        var baseline = before.ToDictionary(x => x.RiskId, x => x.ResidualScore);
        int improved = 0, worsened = 0, unchanged = 0;
        var worse = new List<RiskRecord>();
        foreach (var r in active)
        {
            if (!baseline.TryGetValue(r.Id, out var score) || score == r.ResidualScore)
            {
                unchanged++;
            }
            else if (r.ResidualScore < score)
            {
                improved++;
            }
            else
            {
                worsened++;
                worse.Add(r);
            }
        }

        return new MovementDto(improved, worsened, unchanged, await views.ListItemsAsync(worse.Take(10).ToList(), ct).ConfigureAwait(false));
    }

    public async Task<Result<HeatCellRisksDto>> HandleAsync(GetHeatCellRisks query, CancellationToken cancellationToken)
    {
        var f = query.Filter;
        var residual = query.Kind == "residual";
        List<RiskRecord> risks = [];
        if (f.Category != RiskPermissions.ProjectCategory)
        {
            var source = (await FilteredAsync(f, cancellationToken).ConfigureAwait(false)).Where(r => r.Status != RiskStatuses.Closed);
            source = residual
                ? source.Where(r => (r.ResidualLikelihood ?? r.InherentLikelihood) == query.Likelihood && (r.ResidualImpact ?? r.InherentImpact) == query.Impact)
                : source.Where(r => r.InherentLikelihood == query.Likelihood && r.InherentImpact == query.Impact);
            risks = await source.OrderByDescending(r => r.ResidualScore).ThenBy(r => r.Number).Take(200).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        var projects = residual ? [] : (await ProjectRisksAsync(f, cancellationToken).ConfigureAwait(false))
            .Where(p => p.Probability == query.Likelihood && p.Impact == query.Impact).ToList();
        return new HeatCellRisksDto(await views.ListItemsAsync(risks, cancellationToken).ConfigureAwait(false),
            await ProjectRowsAsync(projects, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<ProjectRiskRowDto>>> HandleAsync(ListProjectRisks query, CancellationToken cancellationToken)
    {
        if (services.GetService(typeof(IProjectRiskDirectory)) is not IProjectRiskDirectory directory)
        {
            return Array.Empty<ProjectRiskRowDto>();
        }

        var rows = await directory.ListAsync(context.User, query.ProjectId, string.IsNullOrWhiteSpace(query.Band) ? null : query.Band, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var text = query.Search.Trim();
            rows = rows.Where(r => (r.Title ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase) || r.ProjectNumber.Contains(text, StringComparison.OrdinalIgnoreCase)
                || (r.ProjectNameAr ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase)
                || (r.ProjectNameEn ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return Result.Success(await ProjectRowsAsync(rows, cancellationToken).ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<ProjectRiskRowDto>> ProjectRowsAsync(IReadOnlyList<ProjectRiskSummary> rows, CancellationToken ct)
    {
        var people = await reader.PeopleAsync(rows.Select(r => r.OwnerEmployeeId), ct).ConfigureAwait(false);
        return rows.Select(r => new ProjectRiskRowDto(r.Id, r.ProjectId, r.ProjectNumber, r.ProjectNameAr, r.ProjectNameEn, r.Title, r.CategoryCode, r.Probability, r.Impact,
            r.ControlEvaluation, r.InherentScore, r.InherentBand, r.ResidualScore, r.ResidualBand, r.Response, r.ImplementationPct, RiskReader.Person(r.OwnerEmployeeId, people),
            r.Link)).ToList();
    }

    public async Task<Result<MechanismDto>> HandleAsync(GetRiskMechanism query, CancellationToken cancellationToken)
    {
        var framework = await frameworks.CurrentAsync(cancellationToken).ConfigureAwait(false);
        var scorer = await frameworks.ScorerAsync(cancellationToken).ConfigureAwait(false);
        var categories = await db.Categories.AsNoTracking().Where(c => c.Active).OrderBy(c => c.Sort).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new MechanismDto(FrameworkQueries.ToDto(framework), scorer.Matrix(), categories.Select(FrameworkQueries.ToDto).ToList());
    }

    public async Task<Result<RiskExportedFile>> HandleAsync(ExportRiskReport query, CancellationToken cancellationToken)
    {
        if (query.Format is not ("pdf" or "xlsx") || query.Kind is not ("register" or "top10" or "unit-profile" or "action-tracker" or "mechanism"))
        {
            return Error.Validation([new FieldError("kind", "validation.invalid", "validation.invalid")]);
        }

        var arabic = query.Language != "en";
        var branding = await settings.GetAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);
        var scorer = await frameworks.ScorerAsync(cancellationToken).ConfigureAwait(false);
        string Band(string? code) => code is null ? string.Empty : scorer.Content.Bands.FirstOrDefault(b => b.Code == code) is { } b ? (arabic ? b.LabelAr ?? b.LabelEn : b.LabelEn ?? b.LabelAr) ?? code : code;
        if (query.Kind == "mechanism")
        {
            if (query.Format != "pdf")
            {
                return Error.Validation([new FieldError("format", "validation.invalid", "validation.invalid")]);
            }

            var html = MechanismHtml.Build(scorer.Content, (await frameworks.CurrentAsync(cancellationToken).ConfigureAwait(false)).VersionNo, branding, arabic);
            var bytes = await pdf.RenderAsync(html, cancellationToken).ConfigureAwait(false);
            await AuditAsync(query, 1, cancellationToken).ConfigureAwait(false);
            return new RiskExportedFile(bytes, "application/pdf", "risk-assessment-mechanism.pdf");
        }

        var filter = new RiskFilter(query.UnitId, null, null, null, false);
        var source = (await FilteredAsync(filter, cancellationToken).ConfigureAwait(false)).Where(r => r.Status != RiskStatuses.Closed && r.Status != RiskStatuses.Draft);
        var risks = await source.OrderByDescending(r => r.ResidualScore).ThenBy(r => r.Number).Take(2000).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (query.Kind == "top10")
        {
            risks = Top10.Rank(risks.Where(r => r.Status == RiskStatuses.Active).ToList(), (await reader.SettingsAsync(cancellationToken).ConfigureAwait(false)).TopCount);
        }

        var units = await reader.UnitsAsync(risks.Select(r => (Guid?)r.UnitId), cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync(risks.Select(r => r.OwnerEmployeeId), cancellationToken).ConfigureAwait(false);
        string Unit(Guid id) => units.GetValueOrDefault(id) is { } u ? (arabic ? u.NameAr : u.NameEn) ?? u.Code ?? string.Empty : string.Empty;
        string Person(Guid? id) => id is { } i && people.GetValueOrDefault(i) is { } p ? (arabic ? p.NameAr : p.NameEn) ?? string.Empty : string.Empty;
        string Title(string? ar, string? en) => (arabic ? ar ?? en : en ?? ar) ?? string.Empty;
        string N(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        var responses = (await lookups.GetValuesAsync(RiskPermissions.ResponseLookup, true, cancellationToken).ConfigureAwait(false))
            .ToDictionary(v => v.Code, v => (arabic ? v.LabelAr ?? v.LabelEn : v.LabelEn ?? v.LabelAr) ?? v.Code);
        string Response(string? code) => code is null ? string.Empty : responses.GetValueOrDefault(code) ?? code;
        List<(string Ar, string En)> headers;
        List<string?[]> rows;
        var paragraphs = new List<(string Ar, string En)>();
        switch (query.Kind)
        {
            case "action-tracker":
                {
                    var ids = risks.Select(r => r.Id).ToList();
                    var byRisk = risks.ToDictionary(r => r.Id);
                    var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
                    var actions = await db.Actions.AsNoTracking().Where(a => ids.Contains(a.RiskId) && a.Status != ActionStatuses.Cancelled).OrderBy(a => a.PlannedEnd)
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    var owners = await reader.PeopleAsync(actions.Select(a => a.OwnerEmployeeId), cancellationToken).ConfigureAwait(false);
                    headers = [("الإجراء", "Action"), ("الخطر", "Risk"), ("العنوان", "Title"), ("المسؤول", "Owner"), ("تاريخ الانتهاء المخطط", "Planned end"),
                        ("نسبة الإنجاز", "Completion"), ("الحالة", "Status")];
                    rows = actions.Select(a => new string?[]
                    {
                        a.Number, byRisk[a.RiskId].Number, Title(a.TitleAr, a.TitleEn),
                        a.OwnerEmployeeId is { } o && owners.GetValueOrDefault(o) is { } p ? (arabic ? p.NameAr : p.NameEn) : null, RiskDeadlines.Date(a.PlannedEnd),
                        (a.CompletionPct * 100).ToString("0", CultureInfo.InvariantCulture) + " %",
                        a.IsOverdue(today) ? (arabic ? "متأخر" : "Overdue") : StatusLabel(a.Status, arabic),
                    }).ToList();
                    break;
                }

            case "unit-profile":
                {
                    headers = [("الوحدة", "Unit"), ("عدد المخاطر", "Risks"), ("حرجة", "Critical"), ("مرتفعة", "High"), ("متوسطة", "Medium"), ("منخفضة", "Low"),
                        ("خارج الرغبة", "Outside appetite"), ("مراجعات متأخرة", "Overdue reviews")];
                    var now = clock.UtcNow;
                    rows = risks.GroupBy(r => r.UnitId).Select(g => new string?[]
                    {
                        Unit(g.Key), g.Count().ToString(CultureInfo.InvariantCulture), g.Count(r => r.ResidualBand == "critical").ToString(CultureInfo.InvariantCulture),
                        g.Count(r => r.ResidualBand == "high").ToString(CultureInfo.InvariantCulture), g.Count(r => r.ResidualBand == "medium").ToString(CultureInfo.InvariantCulture),
                        g.Count(r => r.ResidualBand == "low").ToString(CultureInfo.InvariantCulture), g.Count(r => r.WithinAppetite == false).ToString(CultureInfo.InvariantCulture),
                        g.Count(r => r.Status == RiskStatuses.Active && r.NextReviewDueAt is { } d && d < now).ToString(CultureInfo.InvariantCulture),
                    }).ToList();
                    break;
                }

            default:
                headers = [("الرقم", "Number"), ("الخطر", "Risk"), ("الوحدة", "Unit"), ("المالك", "Owner"), ("الكامن", "Inherent"), ("المتبقي", "Residual"),
                    ("المستهدف", "Target"), ("الاستجابة", "Response"), ("المراجعة القادمة", "Next review")];
                rows = risks.Select(r => new string?[]
                {
                    r.Number, Title(r.TitleAr, r.TitleEn), Unit(r.UnitId), Person(r.OwnerEmployeeId), $"{N(r.InherentScore)} · {Band(r.InherentBand)}",
                    $"{N(r.ResidualScore)} · {Band(r.ResidualBand)}", r.TargetScore is { } t ? $"{N(t)} · {Band(r.TargetBand)}" : null, Response(r.ResponseCode),
                    RiskDeadlines.Date(r.NextReviewOn),
                }).ToList();
                if (query.Kind == "top10")
                {
                    paragraphs.Add(("أهم المخاطر المؤسسية مرتبة حسب درجة الخطر المتبقي والترتيب الذي حدده مكتب المخاطر.",
                        "Top institutional risks ranked by residual score and the Risk Office's ranking."));
                }

                break;
        }

        var (titleAr, titleEn) = query.Kind switch
        {
            "top10" => ("أهم المخاطر المؤسسية", "Top institutional risks"),
            "unit-profile" => ("ملف المخاطر حسب الوحدة", "Risk profile by unit"),
            "action-tracker" => ("متابعة إجراءات المعالجة", "Treatment action tracker"),
            _ => ("سجل المخاطر", "Risk register"),
        };
        RiskExportedFile file;
        if (query.Format == "pdf")
        {
            var document = new ReportDocument
            {
                TitleAr = titleAr,
                TitleEn = titleEn,
                OrganisationAr = branding.OrganisationNameAr,
                OrganisationEn = branding.OrganisationNameEn,
                PrimaryColor = branding.PrimaryColor,
                Language = arabic ? "ar" : "en",
                GeneratedAt = clock.UtcNow,
            };
            foreach (var (ar, en) in paragraphs)
            {
                document.Paragraph(ar, en);
            }

            document.Table(headers.Select(h => new ReportColumn(h.Ar, h.En)).ToList(), rows);
            file = new RiskExportedFile(await pdf.RenderAsync(document.ToHtml(), cancellationToken).ConfigureAwait(false), "application/pdf", $"risk-{query.Kind}.pdf");
        }
        else
        {
            file = new RiskExportedFile(ExcelExport.Build(arabic ? titleAr : titleEn, headers.Select(h => arabic ? h.Ar : h.En).ToList(),
                rows.Select(r => (IReadOnlyList<object?>)r.Cast<object?>().ToList()), arabic), ExcelExport.ContentType, $"risk-{query.Kind}.xlsx");
        }

        await AuditAsync(query, rows.Count, cancellationToken).ConfigureAwait(false);
        return file;
    }

    private Task AuditAsync(ExportRiskReport query, int count, CancellationToken ct) =>
        audit.WriteAsync(new AuditEvent(AuditActions.Export, true, RiskPermissions.SystemCode, RiskRecord.EntityType, null,
            new { kind = query.Kind, format = query.Format, unitId = query.UnitId, rows = count }), ct);

    private static string StatusLabel(string status, bool arabic) => status switch
    {
        ActionStatuses.NotStarted => arabic ? "لم يبدأ" : "Not started",
        ActionStatuses.InProgress => arabic ? "قيد التنفيذ" : "In progress",
        ActionStatuses.Completed => arabic ? "مكتمل" : "Completed",
        _ => arabic ? "ملغى" : "Cancelled",
    };
}

/// <summary>Top-10 ranking (design §5.4): flagged first by manual rank, then residual score, inherent score, number. Pure.</summary>
public static class Top10
{
    public static List<RiskRecord> Rank(IEnumerable<RiskRecord> active, int count) =>
        active.OrderByDescending(r => r.Top10).ThenBy(r => r.Top10Rank ?? int.MaxValue).ThenByDescending(r => r.ResidualScore).ThenByDescending(r => r.InherentScore)
            .ThenBy(r => r.Number, StringComparer.Ordinal).Take(count).ToList();
}

/// <summary>The generated, printable Risk Assessment Mechanism (SPEC §3.3): scales with criteria, matrix, bands, review frequency — bilingual.</summary>
internal static class MechanismHtml
{
    public static string Build(FrameworkContent c, int versionNo, BrandingSettings branding, bool arabic)
    {
        var scorer = new RiskScorer(c);
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        string L(string? ar, string? en) => E(arabic ? ar ?? en : en ?? ar);
        var color = System.Text.RegularExpressions.Regex.IsMatch(branding.PrimaryColor, "^#[0-9a-fA-F]{6}$") ? branding.PrimaryColor : "#1f3a68";
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(arabic ? "ar" : "en").Append("\" dir=\"").Append(arabic ? "rtl" : "ltr").Append("\"><head><meta charset=\"utf-8\"><style>")
            .Append(PdfFonts.FontFaceCss()).Append("@page{size:A4;margin:14mm 12mm}body{font-family:'").Append(PdfFonts.Family).Append("',sans-serif;font-size:9.5pt;color:#1b1f24}")
            .Append("h1{color:").Append(color).Append(";font-size:16pt;margin:0}h2{font-size:12pt;margin:14px 0 6px;color:").Append(color).Append('}')
            .Append("table{border-collapse:collapse;width:100%}td,th{border:1px solid #c8ccd1;padding:4px 6px;vertical-align:top}th{background:#f1f3f5}")
            .Append(".cell{text-align:center;font-weight:600;color:#111}.muted{color:#57606a;font-size:8.5pt}</style></head><body>");
        html.Append("<h1>").Append(E(arabic ? "آلية تقييم المخاطر" : "Risk Assessment Mechanism")).Append("</h1><div class=\"muted\">")
            .Append(L(branding.OrganisationNameAr, branding.OrganisationNameEn)).Append(" · ").Append(E(arabic ? "إصدار الإطار" : "Framework version")).Append(' ')
            .Append(versionNo.ToString(CultureInfo.InvariantCulture)).Append("</div>");
        var method = c.ScoringMethod switch
        {
            ScoringMethods.Additive => arabic ? "الاحتمالية + الأثر" : "Likelihood + Impact",
            ScoringMethods.Custom => (arabic ? "معادلة مخصصة: " : "Custom formula: ") + c.Formula,
            _ => arabic ? "الاحتمالية × الأثر" : "Likelihood × Impact",
        };
        html.Append("<h2>").Append(E(arabic ? "طريقة احتساب الدرجة" : "Scoring method")).Append("</h2><p>").Append(E(method)).Append("</p>");
        html.Append("<h2>").Append(E(arabic ? "مقياس الاحتمالية" : "Likelihood scale")).Append("</h2><table><tr><th>#</th><th>")
            .Append(E(arabic ? "المستوى" : "Level")).Append("</th><th>").Append(E(arabic ? "الوصف" : "Description")).Append("</th></tr>");
        foreach (var l in c.Likelihood.OrderBy(x => x.Value))
        {
            html.Append("<tr><td>").Append(l.Value).Append("</td><td>").Append(L(l.LabelAr, l.LabelEn)).Append("</td><td>").Append(L(l.DescriptionAr, l.DescriptionEn)).Append("</td></tr>");
        }

        html.Append("</table><h2>").Append(E(arabic ? "مقياس الأثر ومعاييره حسب مجال الأثر" : "Impact scale and criteria by impact area")).Append("</h2><table><tr><th>#</th><th>")
            .Append(E(arabic ? "المستوى" : "Level")).Append("</th>");
        foreach (var area in c.ImpactAreas)
        {
            html.Append("<th>").Append(L(area.LabelAr, area.LabelEn)).Append("</th>");
        }

        html.Append("</tr>");
        foreach (var level in c.Impact.OrderBy(x => x.Value))
        {
            html.Append("<tr><td>").Append(level.Value).Append("</td><td>").Append(L(level.LabelAr, level.LabelEn)).Append("</td>");
            foreach (var area in c.ImpactAreas)
            {
                var criterion = level.Criteria?.GetValueOrDefault(area.Code);
                html.Append("<td>").Append(L(criterion?.Ar, criterion?.En)).Append("</td>");
            }

            html.Append("</tr>");
        }

        html.Append("</table><h2>").Append(E(arabic ? "مصفوفة المخاطر" : "Risk matrix")).Append("</h2><table><tr><th>")
            .Append(E(arabic ? "الاحتمالية \\ الأثر" : "Likelihood \\ Impact")).Append("</th>");
        foreach (var i in c.Impact.OrderBy(x => x.Value))
        {
            html.Append("<th>").Append(i.Value).Append(' ').Append(L(i.LabelAr, i.LabelEn)).Append("</th>");
        }

        html.Append("</tr>");
        foreach (var l in c.Likelihood.OrderByDescending(x => x.Value))
        {
            html.Append("<tr><th>").Append(l.Value).Append(' ').Append(L(l.LabelAr, l.LabelEn)).Append("</th>");
            foreach (var i in c.Impact.OrderBy(x => x.Value))
            {
                var score = scorer.Score(l.Value, i.Value, 0);
                var band = scorer.Band(score);
                html.Append("<td class=\"cell\" style=\"background:").Append(band.Color).Append("\">").Append(score.ToString("0.##", CultureInfo.InvariantCulture)).Append("</td>");
            }

            html.Append("</tr>");
        }

        html.Append("</table><h2>").Append(E(arabic ? "نطاقات التصنيف ودورية المراجعة" : "Rating bands and review frequency")).Append("</h2><table><tr><th>")
            .Append(E(arabic ? "النطاق" : "Band")).Append("</th><th>").Append(E(arabic ? "الدرجات" : "Scores")).Append("</th><th>")
            .Append(E(arabic ? "حالة الخطر المتبقي" : "Residual status")).Append("</th><th>").Append(E(arabic ? "المراجعة كل (شهر)" : "Review every (months)")).Append("</th></tr>");
        foreach (var b in c.Bands)
        {
            html.Append("<tr><td style=\"border-inline-start:8px solid ").Append(b.Color).Append("\">").Append(L(b.LabelAr, b.LabelEn)).Append("</td><td>")
                .Append(b.Min.ToString("0.##", CultureInfo.InvariantCulture)).Append(" – ").Append(b.Max.ToString("0.##", CultureInfo.InvariantCulture)).Append("</td><td>")
                .Append(E(ResidualLabel(b.ResidualStatus, arabic))).Append("</td><td>").Append(scorer.ReviewMonths(b.Code)).Append("</td></tr>");
        }

        html.Append("</table><h2>").Append(E(arabic ? "مقياس فعالية الضوابط" : "Control effectiveness scale")).Append("</h2><table>");
        foreach (var e in c.ControlEffectiveness.OrderByDescending(x => x.Factor))
        {
            html.Append("<tr><td>").Append(L(e.LabelAr, e.LabelEn)).Append("</td><td>C = ").Append(e.Factor.ToString("0.##", CultureInfo.InvariantCulture)).Append("</td></tr>");
        }

        html.Append("</table></body></html>");
        return html.ToString();
    }

    public static string ResidualLabel(string status, bool arabic) => status switch
    {
        ResidualStatuses.NoIssue => arabic ? "لا توجد مشكلة" : "No issue",
        ResidualStatuses.ContinuousReview => arabic ? "مراجعة مستمرة" : "Continuous review",
        _ => arabic ? "إدارة فعالة" : "Active management",
    };
}
