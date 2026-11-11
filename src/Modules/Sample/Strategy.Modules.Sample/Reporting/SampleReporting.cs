using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Application;
using Strategy.Modules.Sample.Domain;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;

namespace Strategy.Modules.Sample.Reporting;

/// <summary>
/// The template module's reporting sources (Phase 17, docs/design/reporting.md §2.5): the pattern every module follows — one visible query
/// (all records for the cache, the user's scoped records for the live path), facts grouped in SQL by unit, partition and dimensions.
/// Amounts are a restricted partition (<see cref="ReportingPartitions.Costs"/>) readable in full by item editors only.
/// </summary>
internal sealed class SampleReporting(SampleDbContext db, IAccessService access) : IReportingMetricSource, IReportingListSource, IReportingDatasetSource
{
    public const string Items = "sample.items";
    public const string Amount = "sample.amount";
    public const string Created = "sample.created";
    public const string ApprovedShare = "sample.approved-share";

    private static readonly Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>> Labels = new()
    {
        ["status"] = new Dictionary<string, DimensionLabel>
        {
            [SampleItemStatus.Active] = new("نشط", "Active"),
            [SampleItemStatus.Inactive] = new("غير نشط", "Inactive"),
        },
        ["approval"] = new Dictionary<string, DimensionLabel>
        {
            [SampleApprovalStatus.Draft] = new("مسودة", "Draft"),
            [SampleApprovalStatus.Pending] = new("قيد الموافقة", "Pending"),
            [SampleApprovalStatus.Approved] = new("معتمد", "Approved"),
            [SampleApprovalStatus.Rejected] = new("مرفوض", "Rejected"),
            [SampleApprovalStatus.Returned] = new("معاد للتصحيح", "Returned"),
        },
    };

    public string SystemCode => "sample";

    public IReadOnlyList<MetricDefinition> Metrics { get; } =
    [
        new(Items, "sample", "البنود", "Items", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count, SamplePermissions.Read, ["status", "approval"],
            DrillLink: "/sample", DimensionLabels: Labels),
        new(Amount, "sample", "قيمة البنود", "Item amounts", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Money, SamplePermissions.Read, ["approval"],
            DimensionLabels: Labels),
        new(Created, "sample", "البنود المنشأة", "Items created", MetricKind.Flow, MetricAggregation.Sum, MetricValueKind.Count, SamplePermissions.Read, ["approval"],
            HigherIsBetter: true, DimensionLabels: Labels),
        new(ApprovedShare, "sample", "نسبة البنود المعتمدة", "Share of approved items", MetricKind.Stock, MetricAggregation.Ratio, MetricValueKind.Ratio,
            SamplePermissions.Read, [], HigherIsBetter: true),
    ];

    public IReadOnlyList<ReportingListDefinition> Lists { get; } =
        [new("sample.largest", "sample", "أكبر البنود قيمة", "Largest items", SamplePermissions.Read)];

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new("sample.items", "sample", "البنود", "Items", SamplePermissions.Read,
        [
            new("number", "الرقم", "Number", DatasetFieldType.Text),
            new("nameEn", "الاسم بالإنجليزية", "Name (English)", DatasetFieldType.Text),
            new("nameAr", "الاسم بالعربية", "Name (Arabic)", DatasetFieldType.Text),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("approval", "حالة الموافقة", "Approval status", DatasetFieldType.Lookup, Groupable: true),
            new("unit", "الوحدة", "Unit", DatasetFieldType.Unit, Groupable: true),
            new("amount", "القيمة", "Amount", DatasetFieldType.Money, Permission: SamplePermissions.Update),
            new("createdAt", "تاريخ الإنشاء", "Created", DatasetFieldType.DateTime),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
        ], "/sample/{id}", SamplePermissions.ResourceType) { KeyField = "id" },
    ];

    private async Task<IQueryable<SampleItem>> VisibleAsync(ClaimsPrincipal? user, CancellationToken ct) =>
        user is null ? db.Items.AsNoTracking() : await access.ApplyScopeAsync(db.Items.AsNoTracking(), user, SamplePermissions.Read, SampleScope.Map, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken)
    {
        var items = await VisibleAsync(computation.User, cancellationToken).ConfigureAwait(false);
        var facts = new List<MetricFact>();
        if (computation.CoversCurrentMonth && (computation.Wants(Items) || computation.Wants(Amount) || computation.Wants(ApprovedShare)))
        {
            var groups = await items.GroupBy(i => new { i.UnitId, i.Status, i.ApprovalStatus })
                .Select(g => new { g.Key.UnitId, g.Key.Status, g.Key.ApprovalStatus, Count = g.Count(), Amount = g.Sum(i => i.Amount ?? 0) })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var month = computation.CurrentMonth;
            foreach (var g in groups)
            {
                if (computation.Wants(Items))
                {
                    facts.Add(new MetricFact(Items, month, g.UnitId, ReportingPartitions.Public,
                        new Dictionary<string, string> { ["status"] = g.Status, ["approval"] = g.ApprovalStatus }, g.Count));
                }

                if (computation.Wants(Amount) && g.Amount != 0)
                {
                    facts.Add(new MetricFact(Amount, month, g.UnitId, ReportingPartitions.Costs, new Dictionary<string, string> { ["approval"] = g.ApprovalStatus }, g.Amount));
                }

                if (computation.Wants(ApprovedShare))
                {
                    facts.Add(new MetricFact(ApprovedShare, month, g.UnitId, ReportingPartitions.Public, new Dictionary<string, string>(),
                        g.ApprovalStatus == SampleApprovalStatus.Approved ? g.Count : 0, g.Count));
                }
            }
        }

        if (computation.Wants(Created))
        {
            var from = new DateTimeOffset(computation.FromMonth.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var to = new DateTimeOffset(computation.ToMonth.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var created = await items.Where(i => i.CreatedAt >= from && i.CreatedAt < to)
                .GroupBy(i => new { i.UnitId, i.ApprovalStatus, i.CreatedAt.Year, i.CreatedAt.Month })
                .Select(g => new { g.Key.UnitId, g.Key.ApprovalStatus, g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(created.Select(g => new MetricFact(Created, new DateOnly(g.Year, g.Month, 1), g.UnitId, ReportingPartitions.Public,
                new Dictionary<string, string> { ["approval"] = g.ApprovalStatus }, g.Count)));
        }

        return facts;
    }

    /// <summary>Editors read amounts (a stand-in for a cost-visibility rule).</summary>
    public async Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await access.CanAsync(user, SamplePermissions.Update, cancellationToken: cancellationToken).ConfigureAwait(false) ? [ReportingPartitions.Costs] : [];

    public async Task<IReadOnlyList<ReportingListItem>> GetAsync(string code, ReportingListQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var items = await VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        if (query.UnitIds is { } units)
        {
            items = items.Where(i => i.UnitId != null && units.Contains(i.UnitId.Value));
        }

        var rows = await items.OrderByDescending(i => i.Amount).ThenBy(i => i.Number).Take(query.Take).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(i => new ReportingListItem(SamplePermissions.ResourceType, i.Id, i.Number, i.NameAr, i.NameEn, i.ApprovalStatus, "neutral", i.Amount, null,
            i.UnitId, $"/sample/{i.Id:D}")).ToList();
    }

    private static readonly DatasetQuery<SampleItem> ItemQuery = new DatasetQuery<SampleItem>(i => i.Id)
        .Field("number", i => i.Number)
        .Field("nameEn", i => i.NameEn)
        .Field("nameAr", i => i.NameAr)
        .Field("status", i => i.Status)
        .Field("approval", i => i.ApprovalStatus)
        .Field("unit", i => i.UnitId)
        .Field("amount", i => i.Amount)
        .Field("createdAt", i => i.CreatedAt)
        .Field("id", i => i.Id);

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await ItemQuery.RunAsync(await VisibleAsync(user, cancellationToken).ConfigureAwait(false), query, cancellationToken).ConfigureAwait(false);
}
