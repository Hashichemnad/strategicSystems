using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Benchmarking.Application;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Modules.Cap.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;

namespace Strategy.Modules.Benchmarking.Reporting;

/// <summary>Metric codes of Benchmarking (docs/design/reporting.md §2.5) and their dimension values.</summary>
public static class BenchMetrics
{
    public const string Studies = "bench.studies";
    public const string Actions = "bench.actions";
    public const string PracticesAdopted = "bench.practices-adopted";

    public const string Quarter = "quarter";
    public const string Year = "year";

    public static string QuarterCode(int quarter) => "q" + quarter.ToString(CultureInfo.InvariantCulture);

    /// <summary>CAP action status as a dimension value (catalogue codes, as <c>cap.actions</c>).</summary>
    public static string ActionStatusCode(string status) => status switch
    {
        "not_started" => "not-started",
        "in_progress" => "in-progress",
        _ => status,
    };

    /// <summary>The actions a study counts (as the CAP progress counts them): distinct, not cancelled, not rejected at validation.</summary>
    public static IEnumerable<CapActionProgress> Counted(CapSourceProgress progress) =>
        progress.Remarks.SelectMany(r => r.Actions).DistinctBy(a => a.ActionId).Where(a => a.Status != "cancelled" && a.ValidationStatus != "rejected");
}

/// <summary>
/// Benchmarking metrics. Live path: the studies the user reads (the Benchmarking reader, as the dashboard); unit = the study's responsible
/// unit (the unit column of <c>bench.study.read</c>). Development-plan actions are read from the CAP through its contract, per approved study.
/// </summary>
internal sealed class BenchMetricSource(BenchDbContext db, BenchReader reader, IServiceProvider services) : IReportingMetricSource
{
    public string SystemCode => BenchPermissions.SystemCode;

    public IReadOnlyList<MetricDefinition> Metrics { get; } =
    [
        new(BenchMetrics.Studies, BenchPermissions.SystemCode, "دراسات المقارنة المعيارية", "Benchmarking studies", MetricKind.Stock, MetricAggregation.Sum,
            MetricValueKind.Count, BenchPermissions.Read, [ReportingDimensions.Stage, BenchMetrics.Quarter, ReportingDimensions.Status, BenchMetrics.Year],
            HigherIsBetter: null, DrillLink: "/bench/studies?stage={dim:stage}&year={dim:year}",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Stage] = BenchReportingLabels.Stages,
                [BenchMetrics.Quarter] = BenchReportingLabels.Quarters,
                [ReportingDimensions.Status] = BenchReportingLabels.Statuses,
            }),
        new(BenchMetrics.Actions, BenchPermissions.SystemCode, "إجراءات خطط التطوير", "Development-plan actions", MetricKind.Stock, MetricAggregation.Sum,
            MetricValueKind.Count, BenchPermissions.Read, [ReportingDimensions.Status], HigherIsBetter: null, DrillLink: "/bench",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>> { [ReportingDimensions.Status] = BenchReportingLabels.ActionStatuses }),
        new(BenchMetrics.PracticesAdopted, BenchPermissions.SystemCode, "الممارسات المعتمدة في المكتبة", "Practices adopted", MetricKind.Flow, MetricAggregation.Sum,
            MetricValueKind.Count, BenchPermissions.Read, [], HigherIsBetter: true, DrillLink: "/bench/practices"),
    ];

    public async Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(computation);
        var facts = new List<MetricFact>();
        var studies = computation.User is { } user ? await reader.StudiesAsync(user, cancellationToken).ConfigureAwait(false) : db.Studies.AsNoTracking();
        var month = computation.CurrentMonth;
        if (computation.CoversCurrentMonth && computation.Wants(BenchMetrics.Studies))
        {
            var rows = await studies.GroupBy(s => new { s.ResponsibleUnitId, s.Stage, s.Status, s.TargetYear, s.TargetQuarter })
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(rows.Select(r => new MetricFact(BenchMetrics.Studies, month, r.Key.ResponsibleUnitId, ReportingPartitions.Public, new Dictionary<string, string>
            {
                [ReportingDimensions.Stage] = r.Key.Stage,
                [BenchMetrics.Quarter] = BenchMetrics.QuarterCode(r.Key.TargetQuarter),
                [ReportingDimensions.Status] = r.Key.Status,
                [BenchMetrics.Year] = r.Key.TargetYear.ToString(CultureInfo.InvariantCulture),
            }, r.Count)));
        }

        if (computation.CoversCurrentMonth && computation.Wants(BenchMetrics.Actions) && services.GetService(typeof(ICapRemarkSources)) is ICapRemarkSources cap)
        {
            var approved = await studies.Where(s => s.AnalysisApprovedAt != null).Select(s => new { s.Id, s.ResponsibleUnitId }).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var counts = new Dictionary<(Guid Unit, string Status), int>();
            foreach (var study in approved)
            {
                if (await cap.GetProgressAsync(Study.EntityType, study.Id, cancellationToken).ConfigureAwait(false) is not { } progress)
                {
                    continue;
                }

                foreach (var action in BenchMetrics.Counted(progress))
                {
                    var key = (study.ResponsibleUnitId, BenchMetrics.ActionStatusCode(action.Status));
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }

            facts.AddRange(counts.Select(c => new MetricFact(BenchMetrics.Actions, month, c.Key.Unit, ReportingPartitions.Public,
                new Dictionary<string, string> { [ReportingDimensions.Status] = c.Key.Status }, c.Value)));
        }

        if (computation.Wants(BenchMetrics.PracticesAdopted))
        {
            var from = new DateTimeOffset(computation.FromMonth.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var to = new DateTimeOffset(computation.ToMonth.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var practices = await db.BestPractices.AsNoTracking().Where(b => b.StudyId != null && b.CreatedAt >= from && b.CreatedAt < to)
                .Join(studies, b => b.StudyId, s => s.Id, (b, s) => new { s.ResponsibleUnitId, b.CreatedAt })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(practices.GroupBy(p => (p.ResponsibleUnitId, Month: MetricComputation.MonthOf(p.CreatedAt)))
                .Where(g => computation.InRange(g.Key.Month))
                .Select(g => new MetricFact(BenchMetrics.PracticesAdopted, g.Key.Month, g.Key.ResponsibleUnitId, ReportingPartitions.Public,
                    new Dictionary<string, string>(), g.Count())));
        }

        return facts;
    }

    public Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);
}

/// <summary>A study row for the report builder.</summary>
internal sealed class BenchStudyRow
{
    public Guid Id { get; init; }

    public string Number { get; init; } = string.Empty;

    public string? TitleAr { get; init; }

    public string? TitleEn { get; init; }

    public string Stage { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int TargetYear { get; init; }

    public int TargetQuarter { get; init; }

    public Guid ResponsibleUnitId { get; init; }

    public Guid? SectionUnitId { get; init; }

    public Guid? ResponsibleEmployeeId { get; init; }

    public Guid? RequesterEmployeeId { get; init; }

    public string? ComparisonTypeCode { get; init; }

    public string? PurposeCode { get; init; }

    public string? RequestNumber { get; init; }

    public DateTimeOffset? PlanApprovedAt { get; init; }

    public DateTimeOffset? AnalysisApprovedAt { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }

    public DateOnly? ImpactDueOn { get; init; }
}

/// <summary>Report-builder object: benchmarking studies, on the Benchmarking reader's scoped query.</summary>
internal sealed class BenchDatasetSource(BenchReader reader) : IReportingDatasetSource
{
    public const string StudiesDataset = "bench.studies";

    private static readonly DatasetQuery<BenchStudyRow> StudyQuery = new DatasetQuery<BenchStudyRow>(s => s.Id)
        .Field("number", s => s.Number)
        .Field("titleAr", s => s.TitleAr)
        .Field("titleEn", s => s.TitleEn)
        .Field("stage", s => s.Stage)
        .Field("status", s => s.Status)
        .Field("targetYear", s => s.TargetYear)
        .Field("targetQuarter", s => s.TargetQuarter)
        .Field("unit", s => s.ResponsibleUnitId)
        .Field("section", s => s.SectionUnitId)
        .Field("coordinator", s => s.ResponsibleEmployeeId)
        .Field("requester", s => s.RequesterEmployeeId)
        .Field("comparisonType", s => s.ComparisonTypeCode)
        .Field("purpose", s => s.PurposeCode)
        .Field("requestNumber", s => s.RequestNumber)
        .Field("planApprovedAt", s => s.PlanApprovedAt)
        .Field("analysisApprovedAt", s => s.AnalysisApprovedAt)
        .Field("closedAt", s => s.ClosedAt)
        .Field("impactDueOn", s => s.ImpactDueOn);

    public string SystemCode => BenchPermissions.SystemCode;

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new(StudiesDataset, BenchPermissions.SystemCode, "دراسات المقارنة المعيارية", "Benchmarking studies", BenchPermissions.Read,
        [
            new("number", "رقم الدراسة", "Study number", DatasetFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", DatasetFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", DatasetFieldType.Text),
            new("stage", "المرحلة", "Stage", DatasetFieldType.Lookup, Groupable: true),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("targetYear", "السنة المستهدفة", "Target year", DatasetFieldType.Number, Groupable: true),
            new("targetQuarter", "الربع المستهدف", "Target quarter", DatasetFieldType.Number, Groupable: true),
            new("unit", "الجهة المسؤولة", "Responsible unit", DatasetFieldType.Unit, Groupable: true),
            new("section", "القسم", "Section", DatasetFieldType.Unit, Groupable: true),
            new("coordinator", "منسق الدراسة", "Coordinator", DatasetFieldType.Employee, Groupable: true),
            new("requester", "مقدم الطلب", "Requester", DatasetFieldType.Employee, Groupable: true),
            new("comparisonType", "نوع المقارنة", "Comparison type", DatasetFieldType.Lookup, Groupable: true, LookupType: BenchPermissions.ComparisonTypeLookup),
            new("purpose", "الغرض من المقارنة", "Purpose", DatasetFieldType.Lookup, Groupable: true, LookupType: BenchPermissions.PurposeLookup),
            new("requestNumber", "رقم الطلب", "Request number", DatasetFieldType.Text),
            new("planApprovedAt", "تاريخ اعتماد الخطة", "Plan approved at", DatasetFieldType.DateTime),
            new("analysisApprovedAt", "تاريخ اعتماد التحليل", "Analysis approved at", DatasetFieldType.DateTime),
            new("closedAt", "تاريخ الإغلاق", "Closed at", DatasetFieldType.DateTime),
            new("impactDueOn", "موعد قياس الأثر", "Impact review due", DatasetFieldType.Date),
        ], "/bench/studies/{id}"),
    ];

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Dataset != StudiesDataset)
        {
            throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }

        var rows = (await reader.StudiesAsync(user, cancellationToken).ConfigureAwait(false)).Select(s => new BenchStudyRow
        {
            Id = s.Id,
            Number = s.Number,
            TitleAr = s.TitleAr,
            TitleEn = s.TitleEn,
            Stage = s.Stage,
            Status = s.Status,
            TargetYear = s.TargetYear,
            TargetQuarter = s.TargetQuarter,
            ResponsibleUnitId = s.ResponsibleUnitId,
            SectionUnitId = s.SectionUnitId,
            ResponsibleEmployeeId = s.ResponsibleEmployeeId,
            RequesterEmployeeId = s.RequesterEmployeeId,
            ComparisonTypeCode = s.ComparisonTypeCode,
            PurposeCode = s.PurposeCode,
            RequestNumber = s.RequestNumber,
            PlanApprovedAt = s.PlanApprovedAt,
            AnalysisApprovedAt = s.AnalysisApprovedAt,
            ClosedAt = s.ClosedAt,
            ImpactDueOn = s.ImpactDueOn,
        });
        return await StudyQuery.RunAsync(rows, query, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// My Work card "Benchmarking studies awaiting me" (ADR 0149): the coordinator's stage forms and the requester's returned plans — the same
/// query as the Benchmarking My Work items.
/// </summary>
internal sealed class BenchMyWorkCards(BenchReader reader, BenchCalendar calendar) : IMyWorkCardSource
{
    public const string CardCode = "benchStudies";

    public string SystemCode => BenchPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } me)
        {
            return [];
        }

        var awaiting = BenchMyWork.AwaitingMe(await reader.StudiesAsync(user, cancellationToken).ConfigureAwait(false), me);
        var returned = await awaiting.CountAsync(s => s.Stage == StudyStages.Plan, cancellationToken).ConfigureAwait(false);
        var forms = await awaiting.CountAsync(s => s.Stage != StudyStages.Plan, cancellationToken).ConfigureAwait(false);
        if (returned + forms == 0)
        {
            return [];
        }

        var counts = new List<MyWorkCount>();
        if (forms > 0)
        {
            counts.Add(new MyWorkCount("benchStudiesToAct", forms, "warning", "/bench/studies?view=mine"));
        }

        if (returned > 0)
        {
            counts.Add(new MyWorkCount("benchPlansReturned", returned, "danger", "/requests"));
        }

        var studies = await awaiting.OrderBy(s => s.TargetYear).ThenBy(s => s.TargetQuarter).ThenBy(s => s.Number).Take(5).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var today = await calendar.TodayAsync(cancellationToken).ConfigureAwait(false);
        var items = studies.Select(s =>
        {
            var due = Quarters.Start(s.TargetYear, Math.Clamp(s.TargetQuarter, 1, 4));
            return new MyWorkCardItem(Study.EntityType, s.Id, s.Number, s.TitleAr ?? s.TitleEn, s.TitleEn ?? s.TitleAr, BenchMyWork.MessageKey(s), BenchMyWork.Link(s), due,
                due < today);
        }).ToList();
        return [new MyWorkCard(CardCode, BenchPermissions.SystemCode, $"myWork.cards.{CardCode}", counts, items, "/bench/studies?view=mine", 90)];
    }
}

/// <summary>AR / EN labels of fixed dimension values.</summary>
internal static class BenchReportingLabels
{
    public static readonly IReadOnlyDictionary<string, DimensionLabel> Stages = new Dictionary<string, DimensionLabel>
    {
        [StudyStages.Plan] = new("التخطيط", "Plan"),
        [StudyStages.Search] = new("البحث", "Search"),
        [StudyStages.Acquire] = new("الاكتساب", "Acquire"),
        [StudyStages.Analysis] = new("التحليل", "Analysis"),
        [StudyStages.Completed] = new("مكتملة", "Completed"),
    };

    public static readonly IReadOnlyDictionary<string, DimensionLabel> Statuses = new Dictionary<string, DimensionLabel>
    {
        [StudyStatuses.Active] = new("نشطة", "Active"),
        [StudyStatuses.Completed] = new("مكتملة", "Completed"),
        [StudyStatuses.Cancelled] = new("ملغاة", "Cancelled"),
    };

    public static readonly IReadOnlyDictionary<string, DimensionLabel> Quarters = new Dictionary<string, DimensionLabel>
    {
        ["q1"] = new("الربع الأول", "Q1"),
        ["q2"] = new("الربع الثاني", "Q2"),
        ["q3"] = new("الربع الثالث", "Q3"),
        ["q4"] = new("الربع الرابع", "Q4"),
    };

    public static readonly IReadOnlyDictionary<string, DimensionLabel> ActionStatuses = new Dictionary<string, DimensionLabel>
    {
        ["not-started"] = new("لم يبدأ", "Not started"),
        ["in-progress"] = new("قيد التنفيذ", "In progress"),
        ["late"] = new("متأخر", "Late"),
        ["completed"] = new("منجز", "Completed"),
    };
}
