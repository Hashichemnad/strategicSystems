using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Portfolio.Reporting;

/// <summary>Dashboard lists (docs/design/reporting.md §2.5): delayed projects and upcoming milestones, always on the caller's readable projects.</summary>
internal sealed class PortfolioListSource(PortfolioDbContext db, RequestReader reader) : IReportingListSource
{
    public const string DelayedProjects = "portfolio.delayed-projects";
    public const string UpcomingMilestones = "portfolio.upcoming-milestones";

    /// <summary>The look-ahead of upcoming milestones when the period ends sooner (the module dashboard's 60 days).</summary>
    public const int UpcomingDays = 60;

    public string SystemCode => PortfolioPermissions.SystemCode;

    public IReadOnlyList<ReportingListDefinition> Lists { get; } =
    [
        new(DelayedProjects, PortfolioPermissions.SystemCode, "المشاريع المتأخرة", "Delayed projects", PortfolioPermissions.ProjectRead),
        new(UpcomingMilestones, PortfolioPermissions.SystemCode, "المعالم القادمة", "Upcoming milestones", PortfolioPermissions.ProjectRead),
    ];

    public async Task<IReadOnlyList<ReportingListItem>> GetAsync(string code, ReportingListQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var projects = (await reader.VisibleProjectsAsync(user, cancellationToken).ConfigureAwait(false)).Where(p => p.Status == ProjectStatuses.Active);
        if (query.UnitIds is { Count: > 0 } units)
        {
            projects = projects.Where(p => p.UnitId != null && units.Contains(p.UnitId.Value));
        }

        var take = Math.Clamp(query.Take, 1, 200);
        switch (code)
        {
            case DelayedProjects:
                {
                    var rows = await projects.Where(p => p.ProgressStatus == ProgressCalculator.Delayed)
                        .OrderBy(p => p.Performance - p.PlannedProgress).ThenBy(p => p.Number).Take(take)
                        .Select(p => new { p.Id, p.Number, p.NameAr, p.NameEn, p.ProgressStatus, p.Performance, p.PlannedProgress, p.PlannedEnd, p.UnitId })
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    return rows.Select(p => new ReportingListItem(PortfolioPermissions.ProjectResource, p.Id, p.Number, p.NameAr, p.NameEn, p.ProgressStatus, "danger",
                        p.Performance, p.PlannedEnd, p.UnitId, $"/portfolio/projects/{p.Id:D}",
                        string.Create(CultureInfo.InvariantCulture, $"الإنجاز {p.Performance:P0} مقابل المخطط {p.PlannedProgress:P0}"),
                        string.Create(CultureInfo.InvariantCulture, $"Progress {p.Performance:P0} vs planned {p.PlannedProgress:P0}"))).ToList();
                }

            case UpcomingMilestones:
                {
                    var today = query.Today;
                    var until = query.To > today.AddDays(UpcomingDays) ? query.To : today.AddDays(UpcomingDays);
                    var rows = await (from m in db.Milestones.AsNoTracking()
                                      join p in projects on m.ProjectId equals p.Id
                                      where !m.IsHeader && !m.HighLevel && m.Status != MilestoneStatuses.Cancelled && m.Status != MilestoneStatuses.Completed
                                            && m.PlannedEnd >= today && m.PlannedEnd <= until
                                      orderby m.PlannedEnd, p.Number, m.Code
                                      select new
                                      {
                                          m.Id,
                                          ProjectId = p.Id,
                                          p.Number,
                                          m.Code,
                                          m.TitleAr,
                                          m.TitleEn,
                                          m.Status,
                                          m.MaxAchieved,
                                          m.Weight,
                                          PlannedEnd = m.PlannedEnd!.Value,
                                          p.UnitId,
                                          ProjectAr = p.NameAr,
                                          ProjectEn = p.NameEn,
                                      })
                        .Take(take).ToListAsync(cancellationToken).ConfigureAwait(false);
                    return rows.Select(m => new ReportingListItem(PortfolioPermissions.MilestoneResource, m.Id, $"{m.Number} · {m.Code}", m.TitleAr ?? m.TitleEn,
                        m.TitleEn ?? m.TitleAr, m.Status, m.PlannedEnd <= today.AddDays(7) ? "warning" : "info", ProgressCalculator.CompletionRate(m.MaxAchieved, m.Weight),
                        m.PlannedEnd, m.UnitId, $"/portfolio/projects/{m.ProjectId:D}?tab=milestones", m.ProjectAr ?? m.ProjectEn, m.ProjectEn ?? m.ProjectAr)).ToList();
                }

            default:
                return [];
        }
    }
}
