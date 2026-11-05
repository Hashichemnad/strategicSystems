using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Risk.Reporting;

/// <summary>
/// My Work card "my risks and treatment actions" (docs/design/reporting.md §6): the risks the user owns or coordinates that need a review,
/// reassessment or correction, their overdue actions and KRI breaches. Items are the existing My Work items (<see cref="RiskMyWork"/>).
/// </summary>
internal sealed class RiskMyWorkCards(RiskDbContext db, IAccessService access, IClock clock, RiskDeadlines deadlines) : IMyWorkCardSource
{
    public const string Code = "risk.my-risks";

    public string SystemCode => RiskPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } me)
        {
            return [];
        }

        var now = clock.UtcNow;
        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        var soon = today.AddDays(30);
        var mine = (await access.ApplyScopeAsync(db.Risks.AsNoTracking(), user, RiskPermissions.Read, RiskScope.Risk, cancellationToken).ConfigureAwait(false))
            .Where(r => r.OwnerEmployeeId == me || r.CoordinatorEmployeeId == me);
        var reviewable = mine.Where(r => r.Status == RiskStatuses.Active && r.PendingProcess == null);
        var reviewsOverdue = await reviewable.CountAsync(r => !r.ReassessmentRequired && r.NextReviewDueAt != null && r.NextReviewDueAt < now, cancellationToken)
            .ConfigureAwait(false);
        var reviewsDue = await reviewable.CountAsync(r => !r.ReassessmentRequired && r.NextReviewOn != null && r.NextReviewOn <= soon
            && !(r.NextReviewDueAt != null && r.NextReviewDueAt < now), cancellationToken).ConfigureAwait(false);
        var reassessment = await reviewable.CountAsync(r => r.ReassessmentRequired, cancellationToken).ConfigureAwait(false);
        var returned = await mine.CountAsync(r => r.Status == RiskStatuses.Draft && r.Returned, cancellationToken).ConfigureAwait(false);
        var actionsOverdue = await db.Actions.AsNoTracking().CountAsync(a => a.OwnerEmployeeId == me
            && (a.Status == ActionStatuses.NotStarted || a.Status == ActionStatuses.InProgress) && a.PlannedEnd != null && a.PlannedEnd < today, cancellationToken)
            .ConfigureAwait(false);
        var activeMine = mine.Where(r => r.Status == RiskStatuses.Active).Select(r => r.Id);
        var kriBreaches = await db.Kris.AsNoTracking().CountAsync(k => activeMine.Contains(k.RiskId) && k.Status == ControlStatuses.Active && k.LastLevel == KriLevels.Breach,
            cancellationToken).ConfigureAwait(false);
        if (reviewsOverdue + reviewsDue + reassessment + returned + actionsOverdue + kriBreaches == 0)
        {
            return [];
        }

        var items = await new RiskMyWork(db, access, clock, deadlines).GetItemsAsync(user, cancellationToken).ConfigureAwait(false);
        var top = items.OrderBy(i => i.DueDate ?? DateOnly.MaxValue).Take(5)
            .Select(i => new MyWorkCardItem(i.ResourceType, i.ResourceId, i.Number, i.TitleAr, i.TitleEn, i.MessageKey, i.Link, i.DueDate,
                i.MessageKey is "risk.myWork.reviewOverdue" or "risk.myWork.actionOverdue"))
            .ToList();
        return
        [
            new MyWorkCard(Code, SystemCode, "myWork.cards.myRisks",
            [
                new MyWorkCount("myWork.counts.reviewsOverdue", reviewsOverdue, reviewsOverdue > 0 ? "danger" : "neutral", "/risk/risks?view=overdue-review"),
                new MyWorkCount("myWork.counts.reviewsDue", reviewsDue, reviewsDue > 0 ? "warning" : "neutral", "/risk/risks?view=mine"),
                new MyWorkCount("myWork.counts.reassessment", reassessment, reassessment > 0 ? "warning" : "neutral", "/risk/risks?view=reassessment"),
                new MyWorkCount("myWork.counts.returned", returned, returned > 0 ? "warning" : "neutral", "/risk/risks?view=mine"),
                new MyWorkCount("myWork.counts.actionsOverdue", actionsOverdue, actionsOverdue > 0 ? "danger" : "neutral", "/risk/my-actions"),
                new MyWorkCount("myWork.counts.kriBreaches", kriBreaches, kriBreaches > 0 ? "danger" : "neutral", "/risk/risks?view=mine"),
            ],
            top, "/risk/risks?view=mine", 40),
        ];
    }
}
