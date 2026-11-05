using System.Security.Claims;
using Strategy.Platform.Reporting;

namespace Strategy.Platform.Workflow.Application;

/// <summary>My Work card "Pending approvals" across all systems (SPEC §0.4 level 1, ADR 0149): overdue first, delegated ones included.</summary>
internal sealed class ApprovalsMyWorkCard(ApprovalQueries queries) : IMyWorkCardSource
{
    public string SystemCode => WorkflowPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var work = await queries.MyWorkAsync(cancellationToken).ConfigureAwait(false);
        if (work.Approvals.Count == 0)
        {
            return [];
        }

        var items = work.Approvals.OrderByDescending(a => a.Overdue).ThenBy(a => a.DueAt ?? DateTimeOffset.MaxValue).Take(5)
            .Select(a => new MyWorkCardItem(a.ResourceType, a.ResourceId, a.Number, a.TitleAr, a.TitleEn, "reporting.myWork.items.approve", a.Link,
                a.DueAt is { } due ? DateOnly.FromDateTime(due.UtcDateTime) : null, a.Overdue))
            .ToList();
        return
        [
            new MyWorkCard("workflow.approvals", SystemCode, "myWork.cards.approvals",
            [
                new MyWorkCount("pending", work.Counts.PendingApprovals, "info"),
                new MyWorkCount("overdue", work.Counts.Overdue, work.Counts.Overdue > 0 ? "danger" : "neutral"),
                new MyWorkCount("delegated", work.Counts.Delegated),
            ], items, "/", 0),
        ];
    }
}
