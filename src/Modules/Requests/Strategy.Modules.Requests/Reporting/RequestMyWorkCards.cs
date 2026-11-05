using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Requests.Application;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Requests.Reporting;

/// <summary>
/// My Work card "my requests" (docs/design/reporting.md §6): the caller's own requests of every type — documents, IP, benchmarks and custom
/// types — the same set as My Requests (<see cref="RequestQueries.MineAsync"/>): returned ones first, then those in review / in progress, then drafts.
/// </summary>
internal sealed class RequestMyWorkCards(RequestsDbContext db) : IMyWorkCardSource
{
    public const string Code = "requests.my-requests";

    public string SystemCode => RequestPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userId = user.UserId();
        var employee = user.EmployeeIdOf();
        if (userId is null && employee is null)
        {
            return [];
        }

        var mine = db.Requests.AsNoTracking().Where(r => (userId != null && r.RequesterUserId == userId) || (employee != null && r.RequesterEmployeeId == employee));
        var counts = await mine.Where(r => r.Status == RequestStatuses.Draft || r.Status == RequestStatuses.Returned || r.Status == RequestStatuses.InReview
                || r.Status == RequestStatuses.InProgress)
            .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken).ConfigureAwait(false);
        int Count(string status) => counts.GetValueOrDefault(status);
        var returned = Count(RequestStatuses.Returned);
        var inReview = Count(RequestStatuses.InReview);
        var inProgress = Count(RequestStatuses.InProgress);
        var drafts = Count(RequestStatuses.Draft);
        if (returned + inReview + inProgress + drafts == 0)
        {
            return [];
        }

        var items = await mine.Where(r => r.Status == RequestStatuses.Returned || r.Status == RequestStatuses.InReview || r.Status == RequestStatuses.InProgress
                || r.Status == RequestStatuses.Draft)
            .OrderBy(r => r.Status == RequestStatuses.Returned ? 0 : r.Status == RequestStatuses.Draft ? 2 : 1).ThenByDescending(r => r.UpdatedAt).Take(5)
            .Select(r => new { r.Id, r.Number, r.TitleAr, r.TitleEn, r.Status })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            new MyWorkCard(Code, SystemCode, "myWork.cards.myRequests",
            [
                new MyWorkCount("myWork.counts.returned", returned, returned > 0 ? "warning" : "neutral", "/requests/mine"),
                new MyWorkCount("myWork.counts.inReview", inReview, "info", "/requests/mine"),
                new MyWorkCount("myWork.counts.inProgress", inProgress, "info", "/requests/mine"),
                new MyWorkCount("myWork.counts.drafts", drafts, "neutral", "/requests/mine"),
            ],
            items.Select(r => new MyWorkCardItem(Domain.Request.EntityType, r.Id, r.Number, r.TitleAr, r.TitleEn, "requests.status." + r.Status, $"/requests/{r.Id:D}"))
                .ToList(),
            "/requests/mine", 80),
        ];
    }
}
