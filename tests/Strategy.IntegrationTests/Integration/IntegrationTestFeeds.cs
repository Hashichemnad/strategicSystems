using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Application;
using Strategy.Modules.Sample.Domain;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.IntegrationTests.Integration;

/// <summary>
/// Test-only record types over the real sample items (system <c>sample</c>) with relationships, so <c>$expand</c> is exercised on real,
/// scoped data: <c>TestItems</c> (read permission <c>sample.item.read</c>) → <c>unitItems</c> (same unit, same permission) and
/// <c>ownItems</c> (same unit, read with <c>sample.item.update</c> — a narrower scope for the same caller).
/// </summary>
internal sealed class IntegrationTestFeeds(SampleDbContext db, IAccessService access) : IReportableRecordSource
{
    public const string ItemType = "tests.sample-item";
    public const string OwnType = "tests.sample-own";

    private static readonly RecordTypeQuery<SampleItem> Items = new RecordTypeQuery<SampleItem>(i => i.Id, i => i.UpdatedAt)
        .Field("number", i => i.Number)
        .Field("nameEn", i => i.NameEn)
        .Field("status", i => i.Status)
        .Field("unit", i => i.UnitId)
        .Field("amount", i => i.Amount)
        .Deleted(i => i.Status == SampleItemStatus.Inactive);

    public string SystemCode => "sample";

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(ItemType, "sample", "TestItems", "عناصر", "Test items", SamplePermissions.Read,
        [
            new("number", "الرقم", "Number", RecordFieldType.Text),
            new("nameEn", "الاسم", "Name", RecordFieldType.Text),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: [new("active", "نشط", "Active"), new("inactive", "غير نشط", "Inactive")]),
            new("unit", "الوحدة", "Unit", RecordFieldType.Unit),
            new("amount", "القيمة", "Amount", RecordFieldType.Money),
        ],
        [
            new("unitItems", "عناصر الوحدة", "Items of the unit", ItemType, "unit", "unit", Many: true),
            new("ownItems", "عناصري", "Own items", OwnType, "unit", "unit", Many: true),
        ]),
        new(OwnType, "sample", "OwnItems", "عناصري", "Own items", SamplePermissions.Update,
        [
            new("nameEn", "الاسم", "Name", RecordFieldType.Text),
            new("unit", "الوحدة", "Unit", RecordFieldType.Unit),
        ], []),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var permission = query.RecordType == OwnType ? SamplePermissions.Update : SamplePermissions.Read;
        var scoped = await access.ApplyScopeAsync(db.Items.AsNoTracking(), user, permission, SampleScope.Map, cancellationToken);
        return await Items.RunAsync(scoped, query, cancellationToken);
    }
}

/// <summary>A test integration event about a sample item (webhooks).</summary>
public sealed record TestItemChanged(Guid ItemId, string Change, DateTimeOffset At) : IIntegrationEvent;
