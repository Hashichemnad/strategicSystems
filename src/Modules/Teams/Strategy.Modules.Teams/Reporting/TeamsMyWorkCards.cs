using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Teams.Reporting;

/// <summary>
/// "My teams and pending evaluations" (docs/design/reporting.md §6): the operating teams the caller is an active member of, the evaluations
/// waiting for them as evaluator (the query of <c>TeamMyWork</c>) and the periodic reports due for the teams they lead.
/// </summary>
internal sealed class TeamsMyWorkCards(TeamsDbContext db, IClock clock) : IMyWorkCardSource
{
    public const string TeamsCard = "teams-teams";

    public string SystemCode => TeamPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } me)
        {
            return [];
        }

        var evaluations = await (from e in db.Evaluations.AsNoTracking()
                                 join t in db.Teams.AsNoTracking() on e.TeamId equals t.Id
                                 where (e.EvaluatorEmployeeId == me || e.LeaderEvaluatorEmployeeId == me)
                                       && (e.Status == EvaluationStatuses.Open || e.Status == EvaluationStatuses.Returned)
                                 orderby e.OpenedAt
                                 select new { e.Id, e.Number, e.Status, t.NameAr, t.NameEn, e.PeriodEnd })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var reports = await (from r in db.Reports.AsNoTracking()
                             join t in db.Teams.AsNoTracking() on r.TeamId equals t.Id
                             where t.LeaderEmployeeId == me && r.Status == ReportStatuses.Due && r.RemindAt <= now
                             orderby r.DueDate
                             select new { t.Id, t.Number, t.NameAr, t.NameEn, r.DueDate })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var teams = await (from m in db.Members.AsNoTracking()
                           join t in db.Teams.AsNoTracking() on m.TeamId equals t.Id
                           where m.EmployeeId == me && m.Status == MemberStatuses.Active
                                 && (t.Status == TeamStatuses.Active || t.Status == TeamStatuses.Inactive || t.Status == TeamStatuses.PendingEvaluation
                                     || t.Status == TeamStatuses.EvaluationWaitingApproval)
                           orderby t.Number
                           select new { t.Id, t.Number, t.NameAr, t.NameEn, Leader = t.LeaderEmployeeId == me })
            .Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        if (evaluations.Count + reports.Count + teams.Count == 0)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var items = evaluations.Select(e => new MyWorkCardItem(TeamEvaluation.EntityType, e.Id, e.Number, e.NameAr, e.NameEn,
                e.Status == EvaluationStatuses.Returned ? "teams.myWork.evaluationReturned" : "teams.myWork.evaluate", $"/teams/evaluations/{e.Id:D}"))
            .Concat(reports.Select(r => new MyWorkCardItem(TeamPermissions.ResourceType, r.Id, r.Number, r.NameAr, r.NameEn, "teams.myWork.reportDue",
                $"/teams/{r.Id:D}?tab=reports", r.DueDate, r.DueDate < today)))
            .Concat(teams.Select(t => new MyWorkCardItem(TeamPermissions.ResourceType, t.Id, t.Number, t.NameAr, t.NameEn,
                t.Leader ? "teams.myWork.leading" : "teams.myWork.member", $"/teams/{t.Id:D}")))
            .Take(5).ToList();
        return
        [
            new MyWorkCard(TeamsCard, SystemCode, "myWork.cards.teams-teams",
            [
                new MyWorkCount("teams", teams.Count, "info", "/teams?view=mine"),
                new MyWorkCount("evaluations", evaluations.Count, evaluations.Count > 0 ? "warning" : "neutral", "/teams?view=pending-evaluation"),
                new MyWorkCount("reportsDue", reports.Count, reports.Count > 0 ? "warning" : "neutral"),
            ], items, "/teams?view=mine", 60),
        ];
    }
}
