using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Application;
using Strategy.Modules.Sample.Domain;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Modules.Sample.Reporting;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.Modules.Sample.Feeds;

/// <summary>Reportable record types of the template module (ADR 0190): analytics feeds and extracts read sample items through here.</summary>
internal sealed class SampleFeeds(SampleDbContext db, IAccessService access) : IReportableRecordSource
{
    public const string ItemType = SampleItem.EntityType;

    private static readonly RecordTypeQuery<SampleItem> Items = new RecordTypeQuery<SampleItem>(i => i.Id, i => i.UpdatedAt)
        .Field("number", i => i.Number)
        .Field("nameAr", i => i.NameAr)
        .Field("nameEn", i => i.NameEn)
        .Field("status", i => i.Status)
        .Field("approvalStatus", i => i.ApprovalStatus)
        .Field("unit", i => i.UnitId)
        .Field("owner", i => i.OwnerEmployeeId)
        .Field("amount", i => i.Amount)
        .Field("createdAt", i => i.CreatedAt)
        .Deleted(i => i.Status == SampleItemStatus.Inactive);

    public string SystemCode => "sample";

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(ItemType, "sample", "Items", "عناصر تجريبية", "Sample items", SamplePermissions.Read,
        [
            new("number", "الرقم", "Number", RecordFieldType.Text),
            new("nameAr", "الاسم بالعربية", "Name (Arabic)", RecordFieldType.Text),
            new("nameEn", "الاسم بالإنجليزية", "Name (English)", RecordFieldType.Text),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: SampleChoices.Statuses),
            new("approvalStatus", "حالة الموافقة", "Approval status", RecordFieldType.Lookup, Choices: SampleChoices.ApprovalStatuses),
            new("unit", "الوحدة", "Unit", RecordFieldType.Unit),
            new("owner", "المالك", "Owner", RecordFieldType.Employee),
            new("amount", "القيمة", "Amount", RecordFieldType.Money, Permission: SamplePermissions.Update),
            new("createdAt", "تاريخ الإنشاء", "Created", RecordFieldType.DateTime),
        ], [], "/sample/items/{id}"),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.RecordType != ItemType)
        {
            throw new ArgumentException($"Unknown record type '{query.RecordType}'.", nameof(query));
        }

        var scoped = await access.ApplyScopeAsync(db.Items.AsNoTracking(), user, SamplePermissions.Read, SampleScope.Map, cancellationToken).ConfigureAwait(false);
        return await Items.RunAsync(scoped, query, cancellationToken).ConfigureAwait(false);
    }
}
