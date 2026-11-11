using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;

namespace Strategy.Modules.Cap.Reporting;

/// <summary>Dashboard lists of the CAP: overdue remarks (live, the CAP reader's scope).</summary>
internal sealed class CapListSource(CapReader reader, IClock clock) : IReportingListSource
{
    public const string OverdueRemarks = "cap.overdue-remarks";

    private static readonly string[] FinalStatuses = [.. CapRemarkStatuses.Final];

    public string SystemCode => CapPermissions.SystemCode;

    public IReadOnlyList<ReportingListDefinition> Lists { get; } =
        [new(OverdueRemarks, CapPermissions.SystemCode, "الملاحظات المتأخرة", "Overdue remarks", CapPermissions.Read)];

    public async Task<IReadOnlyList<ReportingListItem>> GetAsync(string code, ReportingListQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (code != OverdueRemarks)
        {
            return [];
        }

        var now = clock.UtcNow;
        var remarks = (await reader.RemarksAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(r => !FinalStatuses.Contains(r.Status) && r.Status != CapRemarkStatuses.Verification && r.DueAt < now);
        if (query.UnitIds is { Count: > 0 } units)
        {
            remarks = remarks.Where(r => units.Contains(r.DepartmentUnitId));
        }

        var rows = await remarks.OrderBy(r => r.DueAt).ThenBy(r => r.Number).Take(Math.Clamp(query.Take, 1, 200)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new ReportingListItem(Remark.EntityType, r.Id, r.Number, CapNotifier.Short(r.TextAr ?? r.TextEn), CapNotifier.Short(r.TextEn ?? r.TextAr),
            r.Status, "danger", Math.Max(0, query.Today.DayNumber - r.DueOn.DayNumber), r.DueOn, r.DepartmentUnitId, CapService.Link(r))).ToList();
    }
}

/// <summary>A remark row for the report builder (the scoped remark joined with its source type).</summary>
internal sealed class CapRemarkRow
{
    public Guid Id { get; init; }

    public Guid SourceId { get; init; }

    public Guid? LinkedId { get; init; }

    public string Number { get; init; } = string.Empty;

    public string? TextAr { get; init; }

    public string? TextEn { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Priority { get; init; } = string.Empty;

    public string TypeCode { get; init; } = string.Empty;

    public string? CriterionCode { get; init; }

    public string SourceTypeCode { get; init; } = string.Empty;

    public string SourceNumber { get; init; } = string.Empty;

    public Guid DepartmentUnitId { get; init; }

    public Guid? SectionUnitId { get; init; }

    public Guid? OwnerEmployeeId { get; init; }

    public DateOnly AssignedOn { get; init; }

    public DateOnly DueOn { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public bool Overdue { get; init; }
}

/// <summary>A corrective action row (the readable action joined with its home remark).</summary>
internal sealed class CapActionRow
{
    public Guid Id { get; init; }

    public Guid HomeRemarkId { get; init; }

    public string Number { get; init; } = string.Empty;

    public string? DetailsAr { get; init; }

    public string? DetailsEn { get; init; }

    public string? TypeCode { get; init; }

    public string Status { get; init; } = string.Empty;

    public string ValidationStatus { get; init; } = string.Empty;

    public Guid? AssignedEmployeeId { get; init; }

    public Guid DepartmentUnitId { get; init; }

    public string RemarkNumber { get; init; } = string.Empty;

    public string Priority { get; init; } = string.Empty;

    public DateOnly? PlannedStart { get; init; }

    public DateOnly? PlannedEnd { get; init; }

    public DateOnly? EndDate { get; init; }

    public DateOnly? ActualEnd { get; init; }

    public decimal AchievedPct { get; init; }

    public bool BudgetRequired { get; init; }

    public bool? BudgetAvailable { get; init; }

    public decimal? EstimatedCost { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>Report-builder objects of the CAP (ADR 0144): remarks and corrective actions, on the CAP reader's scoped queries.</summary>
internal sealed class CapDatasetSource(CapDbContext db, CapReader reader, IClock clock) : IReportingDatasetSource
{
    public const string RemarksDataset = "cap.remarks";
    public const string ActionsDataset = "cap.actions";
    public const string SourcesDataset = "cap.sources";

    private static readonly DatasetQuery<CapSource> SourceQuery = new DatasetQuery<CapSource>(s => s.Id)
        .Field("number", s => s.Number)
        .Field("titleAr", s => s.TitleAr)
        .Field("titleEn", s => s.TitleEn)
        .Field("sourceType", s => s.SourceTypeCode)
        .Field("receivedOn", s => s.ReceivedOn)
        .Field("owner", s => s.OwnerEmployeeId)
        .Field("unit", s => s.UnitId)
        .Field("origin", s => s.Origin)
        .Field("status", s => s.Status)
        .Field("description", s => s.Description)
        .Field("id", s => s.Id);

    private static readonly string[] FinalStatuses = [.. CapRemarkStatuses.Final];

    private static readonly DatasetQuery<CapRemarkRow> RemarkQuery = new DatasetQuery<CapRemarkRow>(r => r.Id)
        .Field("number", r => r.Number)
        .Field("textAr", r => r.TextAr)
        .Field("textEn", r => r.TextEn)
        .Field("status", r => r.Status)
        .Field("priority", r => r.Priority)
        .Field("type", r => r.TypeCode)
        .Field("criterion", r => r.CriterionCode)
        .Field("sourceType", r => r.SourceTypeCode)
        .Field("sourceNumber", r => r.SourceNumber)
        .Field("department", r => r.DepartmentUnitId)
        .Field("section", r => r.SectionUnitId)
        .Field("owner", r => r.OwnerEmployeeId)
        .Field("assignedOn", r => r.AssignedOn)
        .Field("dueOn", r => r.DueOn)
        .Field("completedAt", r => r.CompletedAt)
        .Field("overdue", r => r.Overdue)
        .Field("id", r => r.Id)
        .Field("sourceId", r => r.SourceId)
        .Field("linkedId", r => r.LinkedId);

    private static readonly DatasetQuery<CapActionRow> ActionQuery = new DatasetQuery<CapActionRow>(a => a.Id)
        .Field("number", a => a.Number)
        .Field("detailsAr", a => a.DetailsAr)
        .Field("detailsEn", a => a.DetailsEn)
        .Field("type", a => a.TypeCode)
        .Field("status", a => a.Status)
        .Field("validationStatus", a => a.ValidationStatus)
        .Field("assignee", a => a.AssignedEmployeeId)
        .Field("department", a => a.DepartmentUnitId)
        .Field("remarkNumber", a => a.RemarkNumber)
        .Field("priority", a => a.Priority)
        .Field("plannedStart", a => a.PlannedStart)
        .Field("plannedEnd", a => a.PlannedEnd)
        .Field("endDate", a => a.EndDate)
        .Field("actualEnd", a => a.ActualEnd)
        .Field("achieved", a => a.AchievedPct)
        .Field("budgetRequired", a => a.BudgetRequired)
        .Field("budgetAvailable", a => a.BudgetAvailable)
        .Field("estimatedCost", a => a.EstimatedCost)
        .Field("completedAt", a => a.CompletedAt)
        .Field("id", a => a.Id)
        .Field("homeRemarkId", a => a.HomeRemarkId);

    public string SystemCode => CapPermissions.SystemCode;

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new(RemarksDataset, CapPermissions.SystemCode, "ملاحظات خطة العمل التصحيحية", "CAP remarks", CapPermissions.Read,
        [
            new("number", "رقم الملاحظة", "Remark number", DatasetFieldType.Text),
            new("textAr", "نص الملاحظة (عربي)", "Remark (Arabic)", DatasetFieldType.Text),
            new("textEn", "نص الملاحظة (إنجليزي)", "Remark (English)", DatasetFieldType.Text),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("priority", "الأولوية", "Priority", DatasetFieldType.Lookup, Groupable: true, LookupType: CapPermissions.PriorityLookup),
            new("type", "نوع الملاحظة", "Remark type", DatasetFieldType.Lookup, Groupable: true, LookupType: CapPermissions.RemarkTypeLookup),
            new("criterion", "المعيار", "Criterion", DatasetFieldType.Lookup, Groupable: true, LookupType: CapPermissions.CriterionLookup),
            new("sourceType", "نوع المصدر", "Source type", DatasetFieldType.Lookup, Groupable: true, LookupType: CapPermissions.SourceTypeLookup),
            new("sourceNumber", "رقم المصدر", "Source number", DatasetFieldType.Text, Groupable: true),
            new("department", "الإدارة المعنية", "Department", DatasetFieldType.Unit, Groupable: true),
            new("section", "القسم", "Section", DatasetFieldType.Unit, Groupable: true),
            new("owner", "مالك الملاحظة", "Remark owner", DatasetFieldType.Employee, Groupable: true),
            new("assignedOn", "تاريخ الإسناد", "Assigned on", DatasetFieldType.Date),
            new("dueOn", "تاريخ الاستحقاق", "Due date", DatasetFieldType.Date),
            new("completedAt", "تاريخ الاكتمال", "Completed at", DatasetFieldType.DateTime),
            new("overdue", "متأخرة", "Overdue", DatasetFieldType.Bool, Groupable: true),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
            new("sourceId", "المصدر", "Source", DatasetFieldType.Reference, Sortable: false),
            new("linkedId", "السجل المرتبط", "Linked record", DatasetFieldType.Reference, Sortable: false),
        ], "/cap/remarks/{id}") { KeyField = "id" },
        new(ActionsDataset, CapPermissions.SystemCode, "الإجراءات التصحيحية", "CAP corrective actions", CapPermissions.Read,
        [
            new("number", "رقم الإجراء", "Action number", DatasetFieldType.Text),
            new("detailsAr", "تفاصيل الإجراء (عربي)", "Action (Arabic)", DatasetFieldType.Text),
            new("detailsEn", "تفاصيل الإجراء (إنجليزي)", "Action (English)", DatasetFieldType.Text),
            new("type", "نوع الإجراء", "Action type", DatasetFieldType.Lookup, Groupable: true, LookupType: CapPermissions.ActionTypeLookup),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("validationStatus", "حالة الاعتماد", "Validation status", DatasetFieldType.Lookup, Groupable: true),
            new("assignee", "المسؤول", "Assigned to", DatasetFieldType.Employee, Groupable: true),
            new("department", "الإدارة المعنية", "Department", DatasetFieldType.Unit, Groupable: true),
            new("remarkNumber", "رقم الملاحظة", "Remark number", DatasetFieldType.Text, Groupable: true),
            new("priority", "أولوية الملاحظة", "Remark priority", DatasetFieldType.Lookup, Groupable: true, LookupType: CapPermissions.PriorityLookup),
            new("plannedStart", "تاريخ البدء المخطط", "Planned start", DatasetFieldType.Date),
            new("plannedEnd", "تاريخ الانتهاء المخطط", "Planned end", DatasetFieldType.Date),
            new("endDate", "تاريخ الانتهاء المعتمد", "End date in force", DatasetFieldType.Date),
            new("actualEnd", "تاريخ الانتهاء الفعلي", "Actual end", DatasetFieldType.Date),
            new("achieved", "نسبة الإنجاز", "Achieved", DatasetFieldType.Ratio),
            new("budgetRequired", "يتطلب ميزانية", "Budget required", DatasetFieldType.Bool, Groupable: true),
            new("budgetAvailable", "الميزانية متوفرة", "Budget available", DatasetFieldType.Bool, Groupable: true),
            new("estimatedCost", "التكلفة التقديرية", "Estimated cost", DatasetFieldType.Money),
            new("completedAt", "تاريخ الاكتمال", "Completed at", DatasetFieldType.DateTime),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
            new("homeRemarkId", "الملاحظة", "Remark", DatasetFieldType.Reference, Sortable: false),
        ], "/cap/actions/{id}") { KeyField = "id" },
        new(SourcesDataset, CapPermissions.SystemCode, "مصادر خطة العمل التصحيحية", "CAP sources", CapPermissions.Read,
        [
            new("number", "رقم المصدر", "Source number", DatasetFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", DatasetFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", DatasetFieldType.Text),
            new("sourceType", "نوع المصدر", "Source type", DatasetFieldType.Lookup, Groupable: true, LookupType: CapPermissions.SourceTypeLookup),
            new("receivedOn", "تاريخ الاستلام", "Received on", DatasetFieldType.Date),
            new("owner", "المسؤول", "Owner", DatasetFieldType.Employee, Groupable: true),
            new("unit", "الوحدة", "Unit", DatasetFieldType.Unit, Groupable: true),
            new("origin", "المنشأ", "Origin", DatasetFieldType.Lookup, Groupable: true),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("description", "الوصف", "Description", DatasetFieldType.Text, Filterable: false, Sortable: false),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
        ], "/cap/sources/{id}") { KeyField = "id" },
    ];

    public IReadOnlyList<DatasetRelationship> Relationships { get; } =
    [
        new("cap.source-remarks", SourcesDataset, "id", RemarksDataset, "sourceId", "الملاحظات", "Remarks"),
        new("cap.remark-actions", RemarksDataset, "id", ActionsDataset, "homeRemarkId", "الإجراءات التصحيحية", "Corrective actions"),
        new("cap.remark-tasks", RemarksDataset, "id", "tasks.tasks", "linkedId", "المهام", "Tasks"),
    ];

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        switch (query.Dataset)
        {
            case RemarksDataset:
                {
                    var now = clock.UtcNow;
                    var rows = (await reader.RemarksAsync(user, cancellationToken).ConfigureAwait(false))
                        .Join(db.Sources.AsNoTracking(), r => r.SourceId, s => s.Id, (r, s) => new CapRemarkRow
                        {
                            Id = r.Id,
                            SourceId = r.SourceId,
                            LinkedId = r.LinkedId,
                            Number = r.Number,
                            TextAr = r.TextAr,
                            TextEn = r.TextEn,
                            Status = r.Status,
                            Priority = r.Priority,
                            TypeCode = r.TypeCode,
                            CriterionCode = r.CriterionCode,
                            SourceTypeCode = s.SourceTypeCode,
                            SourceNumber = s.Number,
                            DepartmentUnitId = r.DepartmentUnitId,
                            SectionUnitId = r.SectionUnitId,
                            OwnerEmployeeId = r.OwnerEmployeeId,
                            AssignedOn = r.AssignedOn,
                            DueOn = r.DueOn,
                            CompletedAt = r.CompletedAt,
                            Overdue = !FinalStatuses.Contains(r.Status) && r.Status != CapRemarkStatuses.Verification && r.DueAt < now,
                        });
                    return await RemarkQuery.RunAsync(rows, query, cancellationToken).ConfigureAwait(false);
                }

            case ActionsDataset:
                {
                    var rows = (await reader.ActionsAsync(user, cancellationToken).ConfigureAwait(false))
                        .Join(db.Remarks.AsNoTracking(), a => a.HomeRemarkId, r => r.Id, (a, r) => new CapActionRow
                        {
                            Id = a.Id,
                            HomeRemarkId = a.HomeRemarkId,
                            Number = a.Number,
                            DetailsAr = a.DetailsAr,
                            DetailsEn = a.DetailsEn,
                            TypeCode = a.TypeCode,
                            Status = a.Status,
                            ValidationStatus = a.ValidationStatus,
                            AssignedEmployeeId = a.AssignedEmployeeId,
                            DepartmentUnitId = r.DepartmentUnitId,
                            RemarkNumber = r.Number,
                            Priority = r.Priority,
                            PlannedStart = a.PlannedStart,
                            PlannedEnd = a.PlannedEnd,
                            EndDate = a.RevisedEnd ?? a.PlannedEnd,
                            ActualEnd = a.ActualEnd,
                            AchievedPct = a.AchievedPct,
                            BudgetRequired = a.BudgetRequired,
                            BudgetAvailable = a.BudgetAvailable,
                            EstimatedCost = a.EstimatedCost,
                            CompletedAt = a.CompletedAt,
                        });
                    return await ActionQuery.RunAsync(rows, query, cancellationToken).ConfigureAwait(false);
                }

            case SourcesDataset:
                return await SourceQuery.RunAsync(await reader.SourcesAsync(user, cancellationToken).ConfigureAwait(false), query, cancellationToken).ConfigureAwait(false);

            default:
                throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }
    }
}

/// <summary>
/// My Work card "My CAP remarks and actions" (ADR 0149): remarks to respond to, approved actions due this month or late, and for the PM
/// Office the validation / confirmation queue. Same queries as the CAP My Work items.
/// </summary>
internal sealed class CapMyWorkCards(CapDbContext db, CapReader reader, CapDeadlines deadlines) : IMyWorkCardSource
{
    public const string CardCode = "capRemarksActions";

    public string SystemCode => CapPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var remarks = await reader.RemarksAsync(user, cancellationToken).ConfigureAwait(false);
        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        var counts = new List<MyWorkCount>();
        var items = new List<MyWorkCardItem>();
        if (user.EmployeeIdOf() is { } me)
        {
            var toRespond = CapMyWork.ToRespond(remarks, me);
            var respond = await toRespond.CountAsync(cancellationToken).ConfigureAwait(false);
            var monthEnd = new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1);
            var actions = CapMyWork.ToUpdate(db.Actions.AsNoTracking(), me, monthEnd);
            var late = await actions.CountAsync(a => a.Status == ActionStatuses.Late, cancellationToken).ConfigureAwait(false);
            var due = await actions.CountAsync(a => a.Status != ActionStatuses.Late, cancellationToken).ConfigureAwait(false);
            Add(counts, "capRemarksToRespond", respond, "warning", "/cap/remarks?view=mine");
            Add(counts, "capActionsDue", due, "info", "/cap/my-actions?view=mine");
            Add(counts, "capActionsLate", late, "danger", "/cap/my-actions?view=late");
            var lateItems = await actions.Where(a => a.Status == ActionStatuses.Late).OrderBy(a => a.RevisedEnd ?? a.PlannedEnd).Take(5).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var remarkItems = await toRespond.OrderBy(r => r.DueOn).Take(5).ToListAsync(cancellationToken).ConfigureAwait(false);
            var dueItems = await actions.Where(a => a.Status != ActionStatuses.Late).OrderBy(a => a.RevisedEnd ?? a.PlannedEnd).Take(5).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            items.AddRange(lateItems.Select(a => Item(a, "cap.myWork.actionLate", today)));
            items.AddRange(remarkItems.Select(r => Item(r, r.Status switch
            {
                CapRemarkStatuses.Assigned => "cap.myWork.toAccept",
                CapRemarkStatuses.Correction => "cap.myWork.toCorrect",
                _ => "cap.myWork.toPlan",
            }, CapService.Link(r), today)));
            items.AddRange(dueItems.Select(a => Item(a, "cap.myWork.actionToUpdate", today)));
        }

        if (await reader.CanAllAsync(user, CapPermissions.Office, cancellationToken).ConfigureAwait(false))
        {
            var pending = CapMyWork.OfficePending(remarks);
            var validate = await pending.CountAsync(r => r.Status == CapRemarkStatuses.Validation, cancellationToken).ConfigureAwait(false);
            var confirm = await pending.CountAsync(r => r.Status == CapRemarkStatuses.RejectionPending, cancellationToken).ConfigureAwait(false);
            Add(counts, "capActionsToValidate", validate, "warning", "/cap/my-actions?view=to-validate");
            Add(counts, "capRejectionsToConfirm", confirm, "warning", "/cap/remarks?view=to-confirm");
            var queue = await pending.OrderBy(r => r.UpdatedAt).Take(5).ToListAsync(cancellationToken).ConfigureAwait(false);
            items.AddRange(queue.Select(r => r.Status == CapRemarkStatuses.Validation
                ? Item(r, "cap.myWork.toValidate", $"/cap/remarks/{r.Id:D}/validate", today)
                : Item(r, "cap.myWork.toConfirm", CapService.Link(r), today)));
        }

        if (counts.Count == 0)
        {
            return [];
        }

        return
        [
            new MyWorkCard(CardCode, CapPermissions.SystemCode, $"myWork.cards.{CardCode}", counts, items.Take(5).ToList(), "/cap/remarks?view=mine", 50),
        ];
    }

    private static void Add(List<MyWorkCount> counts, string key, int value, string tone, string link)
    {
        if (value > 0)
        {
            counts.Add(new MyWorkCount(key, value, tone, link));
        }
    }

    private static MyWorkCardItem Item(Remark r, string messageKey, string link, DateOnly today) =>
        new(Remark.EntityType, r.Id, r.Number, CapNotifier.Short(r.TextAr ?? r.TextEn), CapNotifier.Short(r.TextEn ?? r.TextAr), messageKey, link, r.DueOn, r.DueOn < today);

    private static MyWorkCardItem Item(CapAction a, string messageKey, DateOnly today) =>
        new(CapAction.EntityType, a.Id, a.Number, CapNotifier.Short(a.DetailsAr ?? a.DetailsEn), CapNotifier.Short(a.DetailsEn ?? a.DetailsAr), messageKey,
            CapService.Link(a), a.EffectiveEnd, a.Status == ActionStatuses.Late || a.EffectiveEnd < today);
}
