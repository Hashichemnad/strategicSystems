using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting.Excel;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;
using Strategy.Platform.Settings.Application;

namespace Strategy.Modules.Cap.Application;

public sealed record CapCountDto(string Code, int Count);

public sealed record CapUnitCountDto(CapUnitDto? Unit, int Remarks, int Completed, int Open, int Overdue, int LateActions, decimal ClosureRate);

public sealed record CapDashboardDto(
    int Remarks,
    int OpenRemarks,
    int CompletedRemarks,
    int OverdueRemarks,
    int Actions,
    int LateActions,
    int CompletedActions,
    int BudgetActions,
    int OpenSuggestions,
    IReadOnlyList<CapCountDto> BySourceType,
    IReadOnlyList<CapCountDto> ByStatus,
    IReadOnlyList<CapCountDto> ByPriority,
    IReadOnlyList<CapCountDto> ActionsByStatus,
    IReadOnlyList<CapCountDto> Ageing,
    IReadOnlyList<CapUnitCountDto> ByDepartment,
    IReadOnlyList<CapActionListItemDto> BudgetDependent,
    int Effective,
    int NotEffective,
    int AwaitingVerification);

public sealed record CapExportedFile(byte[] Content, string ContentType, string FileName);

public sealed record GetCapDashboard(Guid? UnitId, int? Year) : IQuery<CapDashboardDto>;

public sealed record ExportCapReport(string Kind, string Format, Guid? UnitId, Guid? SourceId, string? SourceTypeCode, int? Year, string? Language) : IQuery<CapExportedFile>;

public sealed record GetCapSettings : IQuery<SettingsDocument<CapSettings>>;

public sealed record SaveCapSettings(CapSettings Settings, uint Version) : ICommand<SettingsDocument<CapSettings>>;

/// <summary>Dashboard (SPEC §9.6) and bilingual reports (register by source, departmental plan, overdue) — PDF / Excel, audited.</summary>
internal sealed class CapDashboardQueries(
    CapDbContext db, CapReader reader, ActionViews actionViews, CapDeadlines deadlines, IRequestContext context, IClock clock, ISettingsStore settings, IPdfRenderer pdf,
    IAuditLog audit, ILookupService lookups, ISettingsEditor editor)
    : IQueryHandler<GetCapDashboard, CapDashboardDto>, IQueryHandler<ExportCapReport, CapExportedFile>, IQueryHandler<GetCapSettings, SettingsDocument<CapSettings>>,
        ICommandHandler<SaveCapSettings, SettingsDocument<CapSettings>>
{
    private static readonly string[] AgeingBuckets = ["0-30", "31-60", "61-90", "90+"];

    public async Task<Result<CapDashboardDto>> HandleAsync(GetCapDashboard query, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, CapPermissions.DashboardRead, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var remarksQuery = await FilteredAsync(query.UnitId, query.Year, cancellationToken).ConfigureAwait(false);
        var remarks = await remarksQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = remarks.Select(r => r.Id).ToList();
        var sourceIds = remarks.Select(r => r.SourceId).Distinct().ToList();
        var sources = await db.Sources.AsNoTracking().Where(s => sourceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.SourceTypeCode, cancellationToken)
            .ConfigureAwait(false);
        var links = await db.RemarkActions.AsNoTracking().Where(l => ids.Contains(l.RemarkId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var actionIds = links.Select(l => l.ActionId).Distinct().ToList();
        var actions = await db.Actions.AsNoTracking().Where(a => actionIds.Contains(a.Id) && a.Status != ActionStatuses.Cancelled).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        bool Overdue(Remark r) => r.IsOpen && r.Status != CapRemarkStatuses.Verification && r.DueAt < now;
        var open = remarks.Where(r => r.IsOpen).ToList();
        var ageing = AgeingBuckets.ToDictionary(b => b, _ => 0);
        foreach (var r in open)
        {
            var days = today.DayNumber - r.AssignedOn.DayNumber;
            ageing[days <= 30 ? "0-30" : days <= 60 ? "31-60" : days <= 90 ? "61-90" : "90+"]++;
        }

        var actionsByRemark = links.GroupBy(l => l.RemarkId).ToDictionary(g => g.Key, g => g.Select(l => l.ActionId).ToHashSet());
        var units = await reader.UnitsAsync(remarks.Select(r => (Guid?)r.DepartmentUnitId), cancellationToken).ConfigureAwait(false);
        var byDepartment = remarks.GroupBy(r => r.DepartmentUnitId).Select(g =>
        {
            var completed = g.Count(r => r.Status == CapRemarkStatuses.Completed);
            var closable = g.Count(r => r.Status is not (CapRemarkStatuses.Rejected or CapRemarkStatuses.Cancelled));
            var actionSet = g.SelectMany(r => actionsByRemark.GetValueOrDefault(r.Id) ?? []).ToHashSet();
            return new CapUnitCountDto(CapReader.Unit(g.Key, units), g.Count(), completed, g.Count(r => r.IsOpen), g.Count(Overdue),
                actions.Count(a => actionSet.Contains(a.Id) && a.Status == ActionStatuses.Late), closable == 0 ? 0 : Math.Round((decimal)completed / closable, 4));
        }).OrderByDescending(u => u.Remarks).ToList();
        var budget = actions.Where(a => a.BudgetRequired).OrderBy(a => a.Number).Take(50).ToList();
        var checks = await db.Checks.AsNoTracking().Where(c => ids.Contains(c.RemarkId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var openSuggestions = await reader.CanAllAsync(context.User, CapPermissions.Office, cancellationToken).ConfigureAwait(false)
            ? await db.Suggestions.CountAsync(s => s.Status == SuggestionStatuses.Open, cancellationToken).ConfigureAwait(false)
            : 0;
        static List<CapCountDto> Count<T>(IEnumerable<T> items, Func<T, string> key) =>
            items.GroupBy(key).Select(g => new CapCountDto(g.Key, g.Count())).OrderByDescending(c => c.Count).ToList();
        return new CapDashboardDto(remarks.Count, open.Count, remarks.Count(r => r.Status == CapRemarkStatuses.Completed), remarks.Count(Overdue), actions.Count,
            actions.Count(a => a.Status == ActionStatuses.Late), actions.Count(a => a.Status == ActionStatuses.Completed), actions.Count(a => a.BudgetRequired), openSuggestions,
            Count(remarks, r => sources.GetValueOrDefault(r.SourceId) ?? CapSourceTypes.Other), Count(remarks, r => r.Status), Count(remarks, r => r.Priority),
            Count(actions.Where(a => a.ValidationStatus == ValidationStatuses.Approved), a => a.Status), ageing.Select(kv => new CapCountDto(kv.Key, kv.Value)).ToList(),
            byDepartment, await actionViews.ListItemsAsync(budget, cancellationToken).ConfigureAwait(false), checks.Count(c => c.Effective), checks.Count(c => !c.Effective),
            remarks.Count(r => r.Status == CapRemarkStatuses.Verification));
    }

    public async Task<Result<CapExportedFile>> HandleAsync(ExportCapReport query, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, CapPermissions.Export, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (query.Format is not ("pdf" or "xlsx") || query.Kind is not ("register" or "departmental" or "overdue"))
        {
            return Error.Validation([new FieldError("kind", "validation.invalid", "validation.invalid")]);
        }

        var arabic = query.Language != "en";
        var remarksQuery = await FilteredAsync(query.UnitId, query.Year, cancellationToken).ConfigureAwait(false);
        if (query.SourceId is { } sourceId)
        {
            remarksQuery = remarksQuery.Where(r => r.SourceId == sourceId);
        }

        if (!string.IsNullOrWhiteSpace(query.SourceTypeCode))
        {
            var typed = db.Sources.Where(s => s.SourceTypeCode == query.SourceTypeCode).Select(s => s.Id);
            remarksQuery = remarksQuery.Where(r => typed.Contains(r.SourceId));
        }

        var remarks = await remarksQuery.OrderBy(r => r.Number).Take(3000).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = remarks.Select(r => r.Id).ToList();
        var links = await db.RemarkActions.AsNoTracking().Where(l => ids.Contains(l.RemarkId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var actionIds = links.Select(l => l.ActionId).Distinct().ToList();
        var actions = await db.Actions.AsNoTracking().Where(a => actionIds.Contains(a.Id) && a.Status != ActionStatuses.Cancelled).ToDictionaryAsync(a => a.Id, cancellationToken)
            .ConfigureAwait(false);
        var people = await reader.PeopleAsync(remarks.Select(r => r.OwnerEmployeeId).Concat(actions.Values.Select(a => a.AssignedEmployeeId)), cancellationToken)
            .ConfigureAwait(false);
        var units = await reader.UnitsAsync(remarks.Select(r => (Guid?)r.DepartmentUnitId), cancellationToken).ConfigureAwait(false);
        var statuses = Labels(arabic);
        var priorities = (await lookups.GetValuesAsync(CapPermissions.PriorityLookup, true, cancellationToken).ConfigureAwait(false))
            .ToDictionary(v => v.Code, v => (arabic ? v.LabelAr ?? v.LabelEn : v.LabelEn ?? v.LabelAr) ?? v.Code);
        string Text(string? ar, string? en) => (arabic ? ar ?? en : en ?? ar) ?? string.Empty;
        string Person(Guid? id) => id is { } i && people.GetValueOrDefault(i) is { } p ? Text(p.NameAr, p.NameEn) : string.Empty;
        string Unit(Guid id) => units.GetValueOrDefault(id) is { } u ? Text(u.NameAr, u.NameEn) : string.Empty;
        string Pct(decimal v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
        var now = clock.UtcNow;
        var rows = new List<string?[]>();
        foreach (var r in remarks)
        {
            var remarkActions = links.Where(l => l.RemarkId == r.Id).Select(l => actions.GetValueOrDefault(l.ActionId)).Where(a => a is not null).Select(a => a!).ToList();
            if (query.Kind == "overdue" && !(r.IsOpen && r.DueAt < now) && !remarkActions.Any(a => a.Status == ActionStatuses.Late))
            {
                continue;
            }

            if (remarkActions.Count == 0)
            {
                rows.Add([r.Number, Text(r.TextAr, r.TextEn), Unit(r.DepartmentUnitId), priorities.GetValueOrDefault(r.Priority) ?? r.Priority, statuses.GetValueOrDefault(r.Status),
                    CapDeadlines.Date(r.DueOn), null, null, null, null, null, null]);
                continue;
            }

            foreach (var a in remarkActions.Where(a => query.Kind != "overdue" || a.Status == ActionStatuses.Late || r.DueAt < now))
            {
                rows.Add([r.Number, Text(r.TextAr, r.TextEn), Unit(r.DepartmentUnitId), priorities.GetValueOrDefault(r.Priority) ?? r.Priority, statuses.GetValueOrDefault(r.Status),
                    CapDeadlines.Date(r.DueOn), a.Number, Text(a.DetailsAr, a.DetailsEn), Person(a.AssignedEmployeeId), CapDeadlines.Date(a.EffectiveEnd), Pct(a.AchievedPct),
                    statuses.GetValueOrDefault(a.Status) ?? a.Status]);
            }
        }

        List<(string Ar, string En)> headers =
        [
            ("الملاحظة", "Remark"), ("نص الملاحظة", "Observation"), ("الإدارة", "Department"), ("الأولوية", "Priority"), ("حالة الملاحظة", "Remark status"),
            ("تاريخ الاستحقاق", "Due date"), ("الإجراء", "Action"), ("تفاصيل الإجراء", "Action details"), ("المسؤول", "Assigned to"), ("تاريخ الانتهاء", "End date"),
            ("الإنجاز", "Achieved"), ("حالة الإجراء", "Action status"),
        ];
        var (titleAr, titleEn) = query.Kind switch
        {
            "departmental" => ("خطة العمل التصحيحية للإدارة", "Departmental action plan"),
            "overdue" => ("تقرير الملاحظات والإجراءات المتأخرة", "Overdue remarks and actions"),
            _ => ("سجل خطة العمل حسب المصدر", "Action plan register by source"),
        };
        CapExportedFile file;
        if (query.Format == "pdf")
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
            document.Table(headers.Select(h => new ReportColumn(h.Ar, h.En)).ToList(), rows);
            file = new CapExportedFile(await pdf.RenderAsync(document.ToHtml(), cancellationToken).ConfigureAwait(false), "application/pdf", $"cap-{query.Kind}.pdf");
        }
        else
        {
            file = new CapExportedFile(ExcelExport.Build(arabic ? titleAr : titleEn, headers.Select(h => arabic ? h.Ar : h.En).ToList(),
                rows.Select(r => (IReadOnlyList<object?>)r.Cast<object?>().ToList()), arabic), ExcelExport.ContentType, $"cap-{query.Kind}.xlsx");
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.Export, true, CapPermissions.SystemCode, Remark.EntityType, null,
            new { kind = query.Kind, format = query.Format, unitId = query.UnitId, sourceId = query.SourceId, rows = rows.Count }), cancellationToken).ConfigureAwait(false);
        return file;
    }

    public async Task<Result<SettingsDocument<CapSettings>>> HandleAsync(GetCapSettings query, CancellationToken cancellationToken) =>
        !await reader.CanAsync(context.User, CapPermissions.SettingsManage, cancellationToken).ConfigureAwait(false)
            ? Error.Forbidden()
            : await editor.GetDocumentAsync<CapSettings>(cancellationToken).ConfigureAwait(false);

    public async Task<Result<SettingsDocument<CapSettings>>> HandleAsync(SaveCapSettings command, CancellationToken cancellationToken) =>
        !await reader.CanAsync(context.User, CapPermissions.SettingsManage, cancellationToken).ConfigureAwait(false)
            ? Error.Forbidden()
            : await editor.SaveAsync(command.Settings, command.Version, CapSettings.Validate, cancellationToken).ConfigureAwait(false);

    private async Task<IQueryable<Remark>> FilteredAsync(Guid? unitId, int? year, CancellationToken ct)
    {
        var source = await reader.RemarksAsync(context.User, ct).ConfigureAwait(false);
        if (unitId is { } unit)
        {
            var units = await reader.UnitAndBelowAsync(unit, ct).ConfigureAwait(false);
            source = source.Where(r => units.Contains(r.DepartmentUnitId));
        }

        if (year is { } y)
        {
            var from = new DateOnly(y, 1, 1);
            var to = new DateOnly(y, 12, 31);
            source = source.Where(r => r.AssignedOn >= from && r.AssignedOn <= to);
        }

        return source;
    }

    private static Dictionary<string, string> Labels(bool arabic) => new()
    {
        [CapRemarkStatuses.Assigned] = arabic ? "بانتظار القبول" : "Waiting acceptance",
        [CapRemarkStatuses.RejectionPending] = arabic ? "رفض بانتظار التأكيد" : "Rejection to confirm",
        [CapRemarkStatuses.Rejected] = arabic ? "مرفوضة" : "Rejected",
        [CapRemarkStatuses.Planning] = arabic ? "تخطيط الإجراءات" : "Planning actions",
        [CapRemarkStatuses.Validation] = arabic ? "بانتظار اعتماد المكتب" : "Waiting PM Office",
        [CapRemarkStatuses.Correction] = arabic ? "للتصحيح" : "To correct",
        [CapRemarkStatuses.Implementation] = arabic ? "قيد التنفيذ" : "Implementation",
        [CapRemarkStatuses.Verification] = arabic ? "التحقق من الفعالية" : "Verification",
        [CapRemarkStatuses.Completed] = arabic ? "مكتملة" : "Completed",
        [CapRemarkStatuses.Cancelled] = arabic ? "ملغاة" : "Cancelled",
        [ActionStatuses.NotStarted] = arabic ? "لم يبدأ" : "Not started",
        [ActionStatuses.InProgress] = arabic ? "قيد التنفيذ" : "In progress",
        [ActionStatuses.Late] = arabic ? "متأخر" : "Late",
    };
}
