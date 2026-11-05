using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting.Excel;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;
using Strategy.Platform.Settings.Application;

namespace Strategy.Modules.AuditManagement.Application;

public sealed record AuditCountDto(string Code, int Count);

public sealed record AuditUnitCountDto(AuditUnitDto? Unit, int Findings, int Open, int Late, int Closed, int RiskAccepted, decimal ClosureRate);

public sealed record AuditTrendDto(string Month, int Opened, int Closed);

public sealed record AuditDashboardDto(
    int Findings, int Open, int Late, int Closed, int RiskAccepted, int Cancelled, int Repeat, int OverdueRecommendations, decimal ClosureRate, int Reports,
    IReadOnlyList<AuditCountDto> ByStatus, IReadOnlyList<AuditCountDto> ByPriority, IReadOnlyList<AuditCountDto> ByYear, IReadOnlyList<AuditCountDto> ByReportType,
    IReadOnlyList<AuditUnitCountDto> ByDepartment, IReadOnlyList<AuditTrendDto> Trend, IReadOnlyList<AuditCountDto> Ageing, IReadOnlyList<AuditFindingListItemDto> Overdue,
    IReadOnlyList<AuditFindingListItemDto> RepeatFindings, IReadOnlyList<AuditFindingListItemDto> RiskAcceptedFindings);

public sealed record AuditDueItemDto(Guid RecommendationId, string Number, string? ActionAr, string? ActionEn, Guid FindingId, string FindingNumber, DateOnly DueOn,
    decimal LatestPct, string Status);

public sealed record AuditDepartmentViewDto(
    IReadOnlyList<AuditUnitDto> Units, int Audits, int AuditsClosed, int AuditsActive, int Findings, int ActiveFindings, int LateFindings, int ClosedFindings,
    IReadOnlyList<AuditFindingListItemDto> Active, IReadOnlyList<AuditDueItemDto> DueSoon, string Scope);

public sealed record AuditCalendarItemDto(
    DateOnly Date, string Kind, Guid RecommendationId, string RecommendationNumber, string? ActionAr, string? ActionEn, Guid FindingId, string FindingNumber, string Priority,
    AuditUnitDto? Unit, AuditPersonDto? Auditor, decimal LatestPct, string Status, bool Overdue);

public sealed record AuditExportInput(string Format, string? Language, Guid? ReportId, Guid? UnitId, int? Year, string? SummaryAr, string? SummaryEn);

public sealed record AuditExportedFile(byte[] Content, string ContentType, string FileName);

/// <summary><paramref name="From"/> / <paramref name="To"/>: whole months (inclusive) of the finding date (reporting level 2).</summary>
public sealed record AuditDashboardFilter(Guid? UnitId, int? Year, string? Priority, string? ReportTypeCode, Guid? ReportId, DateOnly? From = null, DateOnly? To = null);

public sealed record GetAuditDashboard(AuditDashboardFilter Filter) : IQuery<AuditDashboardDto>;

public sealed record GetAuditDepartmentView : IQuery<AuditDepartmentViewDto>;

public sealed record GetAuditCalendar(DateOnly? From, DateOnly? To, Guid? AuditorEmployeeId) : IQuery<IReadOnlyList<AuditCalendarItemDto>>;

public sealed record ExportAuditReport(string Kind, AuditExportInput Input) : IQuery<AuditExportedFile>;

public sealed record GetAuditSettings : IQuery<SettingsDocument<AuditMgmtSettings>>;

public sealed record SaveAuditSettings(AuditMgmtSettings Settings, uint Version) : ICommand<SettingsDocument<AuditMgmtSettings>>;

/// <summary>Dashboard, department view, follow-up calendar, bilingual reports (SPEC §10.3 C) and settings.</summary>
internal sealed class AuditDashboardQueries(
    AuditMgmtDbContext db, AuditReader reader, AuditViews views, AuditService service, AuditDeadlines deadlines, IRequestContext context, IClock clock,
    ISettingsStore settings, IPdfRenderer pdf, IAuditLog audit, ILookupService lookups, ISettingsEditor editor)
    : IQueryHandler<GetAuditDashboard, AuditDashboardDto>, IQueryHandler<GetAuditDepartmentView, AuditDepartmentViewDto>,
        IQueryHandler<GetAuditCalendar, IReadOnlyList<AuditCalendarItemDto>>, IQueryHandler<ExportAuditReport, AuditExportedFile>,
        IQueryHandler<GetAuditSettings, SettingsDocument<AuditMgmtSettings>>, ICommandHandler<SaveAuditSettings, SettingsDocument<AuditMgmtSettings>>
{
    private static readonly string[] AgeingBuckets = ["0-90", "91-180", "181-365", "365+"];

    public static readonly IReadOnlyList<string> ExportKinds = ["register", "department", "revenue", "committee"];

    public async Task<Result<AuditDashboardDto>> HandleAsync(GetAuditDashboard query, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, AuditPermissions.DashboardRead, cancellationToken).ConfigureAwait(false)
            && !await reader.CanAsync(context.User, AuditPermissions.AiSummary, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (query.Filter.From is { } from && query.Filter.To is { } to && from > to)
        {
            return Error.Validation([new FieldError("to", "validation.invalid", "validation.invalid")]);
        }

        return await BuildAsync(query.Filter, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<AuditDashboardDto> BuildAsync(AuditDashboardFilter filter, CancellationToken ct)
    {
        var findings = await (await FilteredAsync(filter, ct).ConfigureAwait(false)).ToListAsync(ct).ConfigureAwait(false);
        var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
        var ids = findings.Select(f => f.Id).ToList();
        var reportIds = findings.Select(f => f.ReportId).Distinct().ToList();
        var reports = await db.Reports.AsNoTracking().Where(r => reportIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct).ConfigureAwait(false);
        var overdueRecs = await db.Recommendations.AsNoTracking().CountAsync(r => ids.Contains(r.FindingId) && r.Status == RecommendationStatuses.Late, ct)
            .ConfigureAwait(false);
        int Count(string status) => findings.Count(f => f.Status == status);
        decimal Rate(IReadOnlyCollection<Finding> set)
        {
            var closable = set.Count(f => f.Status != AuditFindingStatuses.Cancelled);
            return closable == 0 ? 0m : Math.Round((decimal)set.Count(f => f.Status is AuditFindingStatuses.Closed or AuditFindingStatuses.RiskAccepted) / closable, 4);
        }

        var departments = findings.GroupBy(f => f.DepartmentUnitId).ToList();
        var units = await reader.UnitsAsync(departments.Select(g => (Guid?)g.Key), ct).ConfigureAwait(false);
        var byDepartment = departments.Select(g => new AuditUnitCountDto(AuditReader.Unit(g.Key, units), g.Count(),
                g.Count(f => f.Status == AuditFindingStatuses.Open), g.Count(f => f.Status == AuditFindingStatuses.Late), g.Count(f => f.Status == AuditFindingStatuses.Closed),
                g.Count(f => f.Status == AuditFindingStatuses.RiskAccepted), Rate(g.ToList())))
            .OrderByDescending(u => u.Late).ThenByDescending(u => u.Open).ToList();
        var trend = new List<AuditTrendDto>();
        for (var i = 11; i >= 0; i--)
        {
            var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
            var end = month.AddMonths(1);
            trend.Add(new AuditTrendDto(month.ToString("yyyy-MM", CultureInfo.InvariantCulture), findings.Count(f => f.FindingDate >= month && f.FindingDate < end),
                findings.Count(f => f.ActualClosedOn >= month && f.ActualClosedOn < end)));
        }

        var ageing = AgeingBuckets.ToDictionary(b => b, _ => 0);
        foreach (var f in findings.Where(f => f.IsOpen))
        {
            var days = today.DayNumber - f.FindingDate.DayNumber;
            ageing[days <= 90 ? "0-90" : days <= 180 ? "91-180" : days <= 365 ? "181-365" : "365+"]++;
        }

        var priorities = (await lookups.GetValuesAsync(AuditPermissions.PriorityLookup, true, ct).ConfigureAwait(false)).Select(v => v.Code).ToList();
        var overdue = findings.Where(f => f.Status == AuditFindingStatuses.Late)
            .OrderByDescending(f => priorities.IndexOf(f.Priority)).ThenBy(f => f.FindingDate).Take(10).ToList();
        return new AuditDashboardDto(findings.Count, Count(AuditFindingStatuses.Open), Count(AuditFindingStatuses.Late), Count(AuditFindingStatuses.Closed),
            Count(AuditFindingStatuses.RiskAccepted), Count(AuditFindingStatuses.Cancelled), findings.Count(f => f.Repeat), overdueRecs, Rate(findings), reports.Count,
            Group(findings, f => f.Status), Group(findings, f => f.Priority), Group(findings, f => f.Year.ToString(CultureInfo.InvariantCulture)),
            Group(findings, f => reports[f.ReportId].TypeCode), byDepartment, trend, AgeingBuckets.Select(b => new AuditCountDto(b, ageing[b])).ToList(),
            await views.ListItemsAsync(overdue, ct).ConfigureAwait(false),
            await views.ListItemsAsync(findings.Where(f => f.Repeat).OrderByDescending(f => f.FindingDate).Take(10).ToList(), ct).ConfigureAwait(false),
            await views.ListItemsAsync(findings.Where(f => f.Status == AuditFindingStatuses.RiskAccepted).OrderByDescending(f => f.ActualClosedOn).Take(10).ToList(), ct)
                .ConfigureAwait(false));
    }

    /// <summary>Read-only department view (replaces the Salesforce portal dashboard): the caller's own scope only.</summary>
    public async Task<Result<AuditDepartmentViewDto>> HandleAsync(GetAuditDepartmentView query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var findings = await (await reader.FindingsAsync(user, cancellationToken).ConfigureAwait(false)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var reportIds = findings.Select(f => f.ReportId).Distinct().ToList();
        var reportStatuses = await db.Reports.AsNoTracking().Where(r => reportIds.Contains(r.Id) && r.Status != ReportStatuses.Cancelled).Select(r => r.Status)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        var activeIds = findings.Where(f => f.IsOpen).Select(f => f.Id).ToList();
        var limit = today.AddDays(30);
        var dueSoon = await db.Recommendations.AsNoTracking()
            .Where(r => activeIds.Contains(r.FindingId) && (r.Status == RecommendationStatuses.NotStarted || r.Status == RecommendationStatuses.Continuing
                || r.Status == RecommendationStatuses.Late) && (r.RevisedDueOn ?? r.DueOn) <= limit)
            .OrderBy(r => r.RevisedDueOn ?? r.DueOn).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        var numbers = findings.ToDictionary(f => f.Id, f => f.Number);
        var units = new List<AuditUnitDto>();
        if (user.EmployeeIdOf() is { } me && await reader.EmployeeAsync(me, cancellationToken).ConfigureAwait(false) is { } employee)
        {
            units.AddRange((await reader.UnitsAsync([employee.UnitId], cancellationToken).ConfigureAwait(false)).Values);
        }

        var settingsValue = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        return new AuditDepartmentViewDto(units, reportStatuses.Count, reportStatuses.Count(s => s == ReportStatuses.Closed), reportStatuses.Count(s => s != ReportStatuses.Closed),
            findings.Count, findings.Count(f => f.IsOpen), findings.Count(f => f.Status == AuditFindingStatuses.Late),
            findings.Count(f => f.Status is AuditFindingStatuses.Closed or AuditFindingStatuses.RiskAccepted),
            await views.ListItemsAsync(findings.Where(f => f.IsOpen).OrderByDescending(f => f.Status == AuditFindingStatuses.Late).ThenBy(f => f.FindingDate).Take(100).ToList(),
                cancellationToken).ConfigureAwait(false),
            dueSoon.Select(r => new AuditDueItemDto(r.Id, r.Number, r.ActionAr, r.ActionEn, r.FindingId, numbers[r.FindingId], r.EffectiveDue, r.LatestPct, r.Status)).ToList(),
            settingsValue.DepartmentScope);
    }

    /// <summary>Follow-up calendar (Audit Office): next follow-up dates and due dates of open recommendations, by auditor.</summary>
    public async Task<Result<IReadOnlyList<AuditCalendarItemDto>>> HandleAsync(GetAuditCalendar query, CancellationToken cancellationToken)
    {
        if (!await reader.IsOfficeAsync(context.User, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        var from = query.From ?? new DateOnly(today.Year, today.Month, 1);
        var to = query.To ?? from.AddMonths(1).AddDays(-1);
        if (to < from || to.DayNumber - from.DayNumber > 400)
        {
            return Error.Validation([new FieldError("to", "validation.invalid", "validation.invalid")]);
        }

        var findings = db.Findings.AsNoTracking().Where(f => f.Status != AuditFindingStatuses.Cancelled);
        if (query.AuditorEmployeeId is { } auditor)
        {
            findings = findings.Where(f => f.AuditorEmployeeId == auditor);
        }

        var rows = await db.Recommendations.AsNoTracking()
            .Where(r => r.Status == RecommendationStatuses.NotStarted || r.Status == RecommendationStatuses.Continuing || r.Status == RecommendationStatuses.Late)
            .Join(findings, r => r.FindingId, f => f.Id, (r, f) => new { R = r, F = f })
            .Where(x => (x.R.NextFollowUpOn != null && x.R.NextFollowUpOn <= to) || ((x.R.RevisedDueOn ?? x.R.DueOn) >= from && (x.R.RevisedDueOn ?? x.R.DueOn) <= to)
                || (x.R.NextFollowUpOn == null && x.R.Status == RecommendationStatuses.Late))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var units = await reader.UnitsAsync(rows.Select(x => (Guid?)x.F.ResponsibleUnitId), cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync(rows.Select(x => x.F.AuditorEmployeeId), cancellationToken).ConfigureAwait(false);
        var items = new List<AuditCalendarItemDto>();
        foreach (var x in rows)
        {
            AuditCalendarItemDto Item(DateOnly date, string kind) => new(date, kind, x.R.Id, x.R.Number, x.R.ActionAr, x.R.ActionEn, x.F.Id, x.F.Number, x.F.Priority,
                AuditReader.Unit(x.F.ResponsibleUnitId, units), AuditReader.Person(x.F.AuditorEmployeeId, people), x.R.LatestPct, x.R.Status, date < today);
            if (x.R.NextFollowUpOn is { } next && next <= to && (next >= from || next < today))
            {
                items.Add(Item(next, "follow-up"));
            }

            if (x.R.EffectiveDue >= from && x.R.EffectiveDue <= to)
            {
                items.Add(Item(x.R.EffectiveDue, "due"));
            }
        }

        return Result.Success<IReadOnlyList<AuditCalendarItemDto>>(items.OrderBy(i => i.Date).ThenBy(i => i.FindingNumber).ToList());
    }

    public async Task<Result<AuditExportedFile>> HandleAsync(ExportAuditReport query, CancellationToken cancellationToken)
    {
        var input = query.Input;
        if (!ExportKinds.Contains(query.Kind) || input.Format is not ("pdf" or "xlsx"))
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, AuditPermissions.Export, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var arabic = input.Language != "en";
        string Text(string? ar, string? en) => (arabic ? ar ?? en : en ?? ar) ?? string.Empty;
        var filter = new AuditDashboardFilter(input.UnitId, input.Year, null, query.Kind == "revenue" ? ReportTypes.RevenueAudit : null, input.ReportId);
        if (query.Kind == "register" && input.ReportId is null)
        {
            return Error.Validation([new FieldError("reportId", "validation.required", "validation.required")]);
        }

        var findings = await (await FilteredAsync(filter, cancellationToken).ConfigureAwait(false)).OrderBy(f => f.Number).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = findings.Select(f => f.Id).ToList();
        var recs = await db.Recommendations.AsNoTracking().Where(r => ids.Contains(r.FindingId)).OrderBy(r => r.Number).ToListAsync(cancellationToken).ConfigureAwait(false);
        var recIds = recs.Select(r => r.Id).ToList();
        var followUps = query.Kind == "revenue"
            ? await db.FollowUps.AsNoTracking().Where(u => recIds.Contains(u.RecommendationId) && u.Applied).OrderBy(u => u.FollowUpOn).ToListAsync(cancellationToken)
                .ConfigureAwait(false)
            : [];
        var units = await reader.UnitsAsync(findings.Select(f => (Guid?)f.ResponsibleUnitId), cancellationToken).ConfigureAwait(false);
        var labels = Labels(arabic);
        var priorityLabels = (await lookups.GetValuesAsync(AuditPermissions.PriorityLookup, true, cancellationToken).ConfigureAwait(false))
            .ToDictionary(v => v.Code, v => Text(v.LabelAr, v.LabelEn));
        string Unit(Guid id) => units.GetValueOrDefault(id) is { } u ? Text(u.NameAr, u.NameEn) : string.Empty;
        string Pct(decimal v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
        string Status(string s) => labels.GetValueOrDefault(s) ?? s;
        string Priority(string p) => priorityLabels.GetValueOrDefault(p) ?? p;

        var (titleAr, titleEn) = query.Kind switch
        {
            "department" => ("تقرير متابعة الإدارة", "Department follow-up report"),
            "revenue" => ("التقرير الكامل لتدقيق الإيرادات", "Revenue audit full report"),
            "committee" => ("حزمة لجنة التدقيق", "Audit committee pack"),
            _ => ("سجل ملاحظات تقرير التدقيق", "Findings register"),
        };
        if (input.ReportId is { } reportId && await db.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken).ConfigureAwait(false) is { } report)
        {
            titleAr += $" — {report.Number}";
            titleEn += $" — {report.Number}";
        }

        List<(string Ar, string En)> headers =
        [
            ("الملاحظة", "Finding"), ("العنوان", "Title"), ("الإدارة", "Department"), ("الأولوية", "Priority"), ("حالة الملاحظة", "Finding status"), ("الإنجاز", "Completion"),
            ("التوصية", "Recommendation"), ("نص التوصية", "Recommended action"), ("تاريخ التنفيذ", "Due date"), ("حالة التوصية", "Status"), ("آخر تعليق", "Audit Office comment"),
        ];
        var rows = new List<string?[]>();
        foreach (var f in findings)
        {
            var mine = recs.Where(r => r.FindingId == f.Id).ToList();
            if (mine.Count == 0)
            {
                rows.Add([f.Number, Text(f.TitleAr, f.TitleEn), Unit(f.ResponsibleUnitId), Priority(f.Priority), Status(f.Status), Pct(f.Completion), null, null, null, null, null]);
            }

            foreach (var r in mine)
            {
                rows.Add([f.Number, Text(f.TitleAr, f.TitleEn), Unit(f.ResponsibleUnitId), Priority(f.Priority), Status(f.Status), Pct(f.Completion), r.Number,
                    Text(r.ActionAr, r.ActionEn), AuditDeadlines.Date(r.EffectiveDue), $"{Status(r.Status)} ({Pct(r.LatestPct)})", r.LastComment]);
                if (query.Kind == "revenue")
                {
                    foreach (var u in followUps.Where(u => u.RecommendationId == r.Id))
                    {
                        rows.Add([null, null, null, null, null, null, u.Number, u.ActionsImplemented, AuditDeadlines.Date(u.FollowUpOn), Pct(u.Pct), u.Comment]);
                    }
                }
            }
        }

        AuditExportedFile file;
        if (input.Format == "pdf")
        {
            var branding = await settings.GetAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);
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
            if (query.Kind == "committee")
            {
                await CommitteePackAsync(document, filter, input, arabic, Text, Status, Priority, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                document.Table(headers.Select(h => new ReportColumn(h.Ar, h.En)).ToList(), rows);
            }

            file = new AuditExportedFile(await pdf.RenderAsync(document.ToHtml(), cancellationToken).ConfigureAwait(false), "application/pdf", $"audit-{query.Kind}.pdf");
        }
        else
        {
            file = new AuditExportedFile(ExcelExport.Build(arabic ? titleAr : titleEn, headers.Select(h => arabic ? h.Ar : h.En).ToList(),
                rows.Select(r => (IReadOnlyList<object?>)r.Cast<object?>().ToList()), arabic), ExcelExport.ContentType, $"audit-{query.Kind}.xlsx");
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.Export, true, AuditPermissions.SystemCode, Finding.EntityType, null,
            new { kind = query.Kind, format = input.Format, unitId = input.UnitId, reportId = input.ReportId, year = input.Year, rows = rows.Count }), cancellationToken)
            .ConfigureAwait(false);
        return file;
    }

    /// <summary>Audit committee pack (ADR 0137): figures, by department, overdue high / critical findings, risk acceptances, accepted AI summary.</summary>
    private async Task CommitteePackAsync(ReportDocument document, AuditDashboardFilter filter, AuditExportInput input, bool arabic, Func<string?, string?, string> text,
        Func<string, string> status, Func<string, string> priority, CancellationToken ct)
    {
        var d = await BuildAsync(filter, ct).ConfigureAwait(false);
        string Pct(decimal v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
        document.Paragraph(
            $"عدد الملاحظات {d.Findings} — مفتوحة {d.Open}، متأخرة {d.Late}، مغلقة {d.Closed}، مقبولة المخاطر {d.RiskAccepted}. نسبة الإغلاق {Pct(d.ClosureRate)}. ملاحظات متكررة {d.Repeat}.",
            $"Findings {d.Findings} — open {d.Open}, late {d.Late}, closed {d.Closed}, risk accepted {d.RiskAccepted}. Closure rate {Pct(d.ClosureRate)}. Repeat findings {d.Repeat}.");
        if (!string.IsNullOrWhiteSpace(input.SummaryAr) || !string.IsNullOrWhiteSpace(input.SummaryEn))
        {
            document.Paragraph(input.SummaryAr ?? input.SummaryEn, input.SummaryEn ?? input.SummaryAr);
        }

        document.Paragraph("الملاحظات حسب الإدارة", "Findings by department");
        document.Table(
            [new("الإدارة", "Department"), new("الملاحظات", "Findings"), new("مفتوحة", "Open"), new("متأخرة", "Late"), new("مغلقة", "Closed"), new("نسبة الإغلاق", "Closure rate")],
            d.ByDepartment.Select(u => new string?[]
            {
                text(u.Unit?.NameAr, u.Unit?.NameEn), u.Findings.ToString(CultureInfo.InvariantCulture), u.Open.ToString(CultureInfo.InvariantCulture),
                u.Late.ToString(CultureInfo.InvariantCulture), u.Closed.ToString(CultureInfo.InvariantCulture), Pct(u.ClosureRate),
            }));
        var high = d.Overdue.Where(f => f.Priority is AuditPriorities.High or AuditPriorities.Critical).ToList();
        document.Paragraph("الملاحظات المتأخرة عالية الخطورة", "Overdue high / critical findings");
        document.Table(
            [new("الملاحظة", "Finding"), new("العنوان", "Title"), new("الإدارة", "Department"), new("الأولوية", "Priority"), new("الإنجاز", "Completion"), new("الاستحقاق", "Due")],
            high.Select(f => new string?[]
            {
                f.Number, text(f.TitleAr, f.TitleEn), text(f.ResponsibleUnit?.NameAr, f.ResponsibleUnit?.NameEn), priority(f.Priority), Pct(f.Completion),
                AuditDeadlines.Date(f.NextDueOn),
            }));
        document.Paragraph("الملاحظات المقبولة المخاطر", "Risk-accepted findings");
        document.Table(
            [new("الملاحظة", "Finding"), new("العنوان", "Title"), new("الإدارة", "Department"), new("الحالة", "Status")],
            d.RiskAcceptedFindings.Select(f => new string?[] { f.Number, text(f.TitleAr, f.TitleEn), text(f.ResponsibleUnit?.NameAr, f.ResponsibleUnit?.NameEn), status(f.Status) }));
        _ = arabic;
    }

    public async Task<Result<SettingsDocument<AuditMgmtSettings>>> HandleAsync(GetAuditSettings query, CancellationToken cancellationToken) =>
        !await reader.CanAsync(context.User, AuditPermissions.SettingsManage, cancellationToken).ConfigureAwait(false)
            ? Error.Forbidden()
            : await editor.GetDocumentAsync<AuditMgmtSettings>(cancellationToken).ConfigureAwait(false);

    /// <summary>Saves; a changed department scope / level re-syncs every finding's department and grant (ADR 0130).</summary>
    public async Task<Result<SettingsDocument<AuditMgmtSettings>>> HandleAsync(SaveAuditSettings command, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, AuditPermissions.SettingsManage, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (command.Settings is null)
        {
            return Error.Validation([new FieldError("settings", "validation.required", "validation.required")]);
        }

        var before = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var saved = await editor.SaveAsync(command.Settings, command.Version, AuditMgmtSettings.Validate, cancellationToken).ConfigureAwait(false);
        if (saved.IsSuccess && (before.DepartmentScope != command.Settings.DepartmentScope || before.DepartmentLevelCode != command.Settings.DepartmentLevelCode))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await service.ResyncDepartmentsAsync(command.Settings, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return saved;
    }

    private async Task<IQueryable<Finding>> FilteredAsync(AuditDashboardFilter filter, CancellationToken ct)
    {
        var source = await reader.FindingsAsync(context.User, ct).ConfigureAwait(false);
        if (filter.UnitId is { } unit)
        {
            var units = await reader.UnitAndBelowAsync(unit, ct).ConfigureAwait(false);
            source = source.Where(f => units.Contains(f.ResponsibleUnitId));
        }

        if (filter.Year is { } year)
        {
            source = source.Where(f => f.Year == year);
        }

        if (filter.From is { } fromDate)
        {
            var from = new DateOnly(fromDate.Year, fromDate.Month, 1);
            source = source.Where(f => f.FindingDate >= from);
        }

        if (filter.To is { } toDate)
        {
            var to = new DateOnly(toDate.Year, toDate.Month, 1).AddMonths(1).AddDays(-1);
            source = source.Where(f => f.FindingDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Priority))
        {
            source = source.Where(f => f.Priority == filter.Priority);
        }

        if (filter.ReportId is { } reportId)
        {
            source = source.Where(f => f.ReportId == reportId);
        }

        if (!string.IsNullOrWhiteSpace(filter.ReportTypeCode))
        {
            var typed = db.Reports.Where(r => r.TypeCode == filter.ReportTypeCode).Select(r => r.Id);
            source = source.Where(f => typed.Contains(f.ReportId));
        }

        return source;
    }

    private static List<AuditCountDto> Group(IEnumerable<Finding> findings, Func<Finding, string> key) =>
        findings.GroupBy(key).Select(g => new AuditCountDto(g.Key, g.Count())).OrderBy(c => c.Code, StringComparer.Ordinal).ToList();

    private static Dictionary<string, string> Labels(bool arabic) => new()
    {
        [AuditFindingStatuses.Open] = arabic ? "مفتوحة" : "Open",
        [AuditFindingStatuses.Late] = arabic ? "متأخرة" : "Late",
        [AuditFindingStatuses.Closed] = arabic ? "مغلقة" : "Closed",
        [AuditFindingStatuses.RiskAccepted] = arabic ? "قبول المخاطر" : "Risk accepted",
        [AuditFindingStatuses.Cancelled] = arabic ? "ملغاة" : "Cancelled",
        [RecommendationStatuses.NotStarted] = arabic ? "لم يبدأ" : "Not started",
        [RecommendationStatuses.Continuing] = arabic ? "مستمر" : "Continuing",
        [RecommendationStatuses.Completed] = arabic ? "مكتمل" : "Completed",
    };
}
