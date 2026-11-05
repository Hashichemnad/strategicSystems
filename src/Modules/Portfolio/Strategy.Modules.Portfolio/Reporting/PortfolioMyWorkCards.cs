using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Portfolio.Reporting;

/// <summary>
/// My Work cards of Portfolio (docs/design/reporting.md §6): "my projects and milestones due" for the PM / owner of active projects and
/// "Wasel submissions to review" for PMs of projects with vendors. The same records the module's My Work items (<c>ProjectMyWork</c>) use.
/// </summary>
internal sealed class PortfolioMyWorkCards(PortfolioDbContext db, RequestReader reader) : IMyWorkCardSource
{
    public const string ProjectsCard = "portfolio-projects";
    public const string WaselCard = "portfolio-wasel";

    /// <summary>Milestones ending within this many days count as "due".</summary>
    public const int DueWithinDays = 30;

    public string SystemCode => PortfolioPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } me)
        {
            return [];
        }

        var cards = new List<MyWorkCard>();
        var today = await reader.TodayAsync(cancellationToken).ConfigureAwait(false);
        var mine = await db.Projects.AsNoTracking()
            .Where(p => p.Status == ProjectStatuses.Active && (p.PmEmployeeId == me || p.OwnerEmployeeId == me))
            .Select(p => new { p.Id, p.Number, p.NameAr, p.NameEn, Pm = p.PmEmployeeId == me })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (mine.Count > 0)
        {
            var ids = mine.Select(p => p.Id).ToList();
            var until = today.AddDays(DueWithinDays);
            var milestones = await db.Milestones.AsNoTracking()
                .Where(m => ids.Contains(m.ProjectId) && !m.IsHeader && !m.HighLevel && m.Status != MilestoneStatuses.Cancelled && m.Status != MilestoneStatuses.Completed
                    && m.PlannedEnd != null && m.PlannedEnd <= until)
                .OrderBy(m => m.PlannedEnd).ThenBy(m => m.Code)
                .Select(m => new { m.Id, m.ProjectId, m.Code, m.TitleAr, m.TitleEn, PlannedEnd = m.PlannedEnd!.Value })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var monthly = await db.MonthlyUpdates.AsNoTracking()
                .Where(u => ids.Contains(u.ProjectId) && (u.Status == MonthlyStatuses.Open || u.Status == MonthlyStatuses.Returned))
                .Select(u => u.ProjectId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
            var overdue = milestones.Count(m => m.PlannedEnd < today);
            var numbers = mine.ToDictionary(p => p.Id, p => p.Number);
            var items = milestones.Take(5).Select(m => new MyWorkCardItem(PortfolioPermissions.MilestoneResource, m.Id, $"{numbers[m.ProjectId]} · {m.Code}",
                m.TitleAr ?? m.TitleEn, m.TitleEn ?? m.TitleAr, m.PlannedEnd < today ? "portfolio.myWork.milestoneOverdue" : "portfolio.myWork.milestoneDue",
                $"/portfolio/projects/{m.ProjectId:D}?tab=milestones", m.PlannedEnd, m.PlannedEnd < today)).ToList();
            foreach (var p in mine.Where(p => monthly.Contains(p.Id) && p.Pm).Take(5 - items.Count))
            {
                items.Add(new MyWorkCardItem(PortfolioPermissions.ProjectResource, p.Id, p.Number, p.NameAr, p.NameEn, "portfolio.myWork.monthlyDue",
                    $"/portfolio/projects/{p.Id:D}"));
            }

            cards.Add(new MyWorkCard(ProjectsCard, SystemCode, "myWork.cards.portfolio-projects",
            [
                new MyWorkCount("projects", mine.Count, "info", "/portfolio/projects?view=mine"),
                new MyWorkCount("milestonesDue", milestones.Count - overdue, milestones.Count > overdue ? "warning" : "neutral"),
                new MyWorkCount("milestonesOverdue", overdue, overdue > 0 ? "danger" : "neutral"),
                new MyWorkCount("monthlyDue", mine.Count(p => p.Pm && monthly.Contains(p.Id)), "warning"),
            ], items, "/portfolio/projects?view=mine", 40));
        }

        // Vendor submissions and plan proposals waiting for the PM's review (SPEC §1.18).
        var managed = mine.Where(p => p.Pm).ToDictionary(p => p.Id);
        if (managed.Count > 0)
        {
            var ids = managed.Keys.ToList();
            var submissions = await db.VendorSubmissions.AsNoTracking().Where(s => ids.Contains(s.ProjectId) && s.Status == SubmissionStatuses.Submitted)
                .OrderBy(s => s.SubmittedAt).Select(s => new { s.Id, s.ProjectId, s.Period, s.SubmittedAt }).ToListAsync(cancellationToken).ConfigureAwait(false);
            var proposals = await db.PlanProposals.AsNoTracking().Where(s => ids.Contains(s.ProjectId) && s.Status == ProposalStatuses.Submitted)
                .OrderBy(s => s.SubmittedAt).Select(s => new { s.Id, s.ProjectId, s.Sequence, s.SubmittedAt }).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (submissions.Count + proposals.Count > 0)
            {
                var items = submissions.Select(s => new MyWorkCardItem(VendorSubmission.EntityType, s.Id, $"{managed[s.ProjectId].Number} · {s.Period}",
                        managed[s.ProjectId].NameAr, managed[s.ProjectId].NameEn, "portfolio.myWork.waselSubmission", $"/portfolio/projects/{s.ProjectId:D}?tab=vendors"))
                    .Concat(proposals.Select(s => new MyWorkCardItem(PlanProposal.EntityType, s.Id, managed[s.ProjectId].Number, managed[s.ProjectId].NameAr,
                        managed[s.ProjectId].NameEn, "portfolio.myWork.waselProposal", $"/portfolio/projects/{s.ProjectId:D}?tab=vendors")))
                    .Take(5).ToList();
                cards.Add(new MyWorkCard(WaselCard, SystemCode, "myWork.cards.portfolio-wasel",
                [
                    new MyWorkCount("submissions", submissions.Count, submissions.Count > 0 ? "warning" : "neutral"),
                    new MyWorkCount("proposals", proposals.Count, proposals.Count > 0 ? "warning" : "neutral"),
                ], items, "/portfolio/projects?view=mine", 41));
            }
        }

        return cards;
    }
}
