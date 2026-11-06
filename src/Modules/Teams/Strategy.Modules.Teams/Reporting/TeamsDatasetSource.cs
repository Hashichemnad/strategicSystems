using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;

namespace Strategy.Modules.Teams.Reporting;

/// <summary>A team membership with its team's identity, for the report builder (row id = the team: memberships open on the team page).</summary>
internal sealed class TeamMemberReportRow
{
    public Guid MemberId { get; init; }

    public Guid TeamId { get; init; }

    public string? TeamNumber { get; init; }

    public string? TeamNameAr { get; init; }

    public string? TeamNameEn { get; init; }

    public string TeamType { get; init; } = string.Empty;

    public string TeamStatus { get; init; } = string.Empty;

    public Guid? UnitId { get; init; }

    public Guid? EmployeeId { get; init; }

    public string? ExternalName { get; init; }

    public string? ExternalOrganisation { get; init; }

    public string RoleCode { get; init; } = string.Empty;

    public decimal? AllocationPercent { get; init; }

    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// Report-builder objects of Teams (ADR 0144): teams and team members, on the reader's scoped teams. Evaluation scores and rewards are not
/// offered (they need the results permission team by team, ADR 0042). External members' contact details are not exposed.
/// </summary>
internal sealed class TeamsDatasetSource(TeamsDbContext db, TeamReader reader) : IReportingDatasetSource
{
    public const string TeamsDataset = "teams.teams";
    public const string MembersDataset = "teams.members";

    public string SystemCode => TeamPermissions.SystemCode;

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new(TeamsDataset, TeamPermissions.SystemCode, "الفرق واللجان", "Teams and committees", TeamPermissions.Read,
        [
            new("number", "الرمز", "Code", DatasetFieldType.Text),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", DatasetFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", DatasetFieldType.Text),
            new("type", "النوع", "Type", DatasetFieldType.Lookup, Groupable: true),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("permanent", "دائم", "Permanent", DatasetFieldType.Bool, Groupable: true),
            new("unit", "الوحدة المالكة", "Owning unit", DatasetFieldType.Unit, Groupable: true),
            new("leader", "القائد", "Leader", DatasetFieldType.Employee, Groupable: true),
            new("sponsor", "الراعي", "Sponsor", DatasetFieldType.Employee, Groupable: true),
            new("cadence", "دورية الاجتماعات", "Meeting cadence", DatasetFieldType.Lookup, Groupable: true),
            new("workload", "عبء العمل", "Workload", DatasetFieldType.Lookup, Groupable: true),
            new("startDate", "تاريخ البدء", "Start date", DatasetFieldType.Date),
            new("endDate", "تاريخ الانتهاء", "End date", DatasetFieldType.Date),
            new("activatedAt", "تاريخ التفعيل", "Activated on", DatasetFieldType.DateTime),
            new("closedAt", "تاريخ الإغلاق", "Closed on", DatasetFieldType.DateTime),
        ], "/teams/{id}"),
        new(MembersDataset, TeamPermissions.SystemCode, "أعضاء الفرق", "Team members", TeamPermissions.Read,
        [
            new("teamNumber", "رمز الفريق", "Team code", DatasetFieldType.Text, Groupable: true),
            new("teamNameAr", "الفريق (عربي)", "Team (Arabic)", DatasetFieldType.Text),
            new("teamNameEn", "الفريق (إنجليزي)", "Team (English)", DatasetFieldType.Text),
            new("teamType", "نوع الفريق", "Team type", DatasetFieldType.Lookup, Groupable: true),
            new("teamStatus", "حالة الفريق", "Team status", DatasetFieldType.Lookup, Groupable: true),
            new("unit", "الوحدة المالكة", "Owning unit", DatasetFieldType.Unit, Groupable: true),
            new("employee", "الموظف", "Employee", DatasetFieldType.Employee, Groupable: true),
            new("externalName", "العضو الخارجي", "External member", DatasetFieldType.Text),
            new("externalOrganisation", "جهة العضو الخارجي", "External organisation", DatasetFieldType.Text, Groupable: true),
            new("role", "الدور", "Role", DatasetFieldType.Lookup, Groupable: true),
            new("allocation", "نسبة التفرغ", "Allocation", DatasetFieldType.Number),
            new("fromDate", "من", "From", DatasetFieldType.Date),
            new("toDate", "إلى", "To", DatasetFieldType.Date),
            new("status", "حالة العضوية", "Membership status", DatasetFieldType.Lookup, Groupable: true),
        ], "/teams/{id}?tab=members"),
    ];

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var teams = (await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false)).Where(t => t.Status != TeamStatuses.Cancelled);
        switch (query.Dataset)
        {
            case TeamsDataset:
                {
                    var dataset = new DatasetQuery<Team>(t => t.Id)
                        .Field("number", t => t.Number)
                        .Field("nameAr", t => t.NameAr)
                        .Field("nameEn", t => t.NameEn)
                        .Field("type", t => t.TypeCode)
                        .Field("status", t => t.Status)
                        .Field("permanent", t => t.Permanent)
                        .Field("unit", t => t.OwningUnitId)
                        .Field("leader", t => t.LeaderEmployeeId)
                        .Field("sponsor", t => t.SponsorEmployeeId)
                        .Field("cadence", t => t.Cadence)
                        .Field("workload", t => t.Workload)
                        .Field("startDate", t => t.StartDate)
                        .Field("endDate", t => t.EndDate)
                        .Field("activatedAt", t => t.ActivatedAt)
                        .Field("closedAt", t => t.ClosedAt);
                    return await dataset.RunAsync(teams, query, cancellationToken).ConfigureAwait(false);
                }

            case MembersDataset:
                {
                    var source = from m in db.Members.AsNoTracking()
                                 join t in teams on m.TeamId equals t.Id
                                 select new TeamMemberReportRow
                                 {
                                     MemberId = m.Id,
                                     TeamId = t.Id,
                                     TeamNumber = t.Number,
                                     TeamNameAr = t.NameAr,
                                     TeamNameEn = t.NameEn,
                                     TeamType = t.TypeCode,
                                     TeamStatus = t.Status,
                                     UnitId = t.OwningUnitId,
                                     EmployeeId = m.EmployeeId,
                                     ExternalName = m.ExternalName,
                                     ExternalOrganisation = m.ExternalOrganisation,
                                     RoleCode = m.RoleCode,
                                     AllocationPercent = m.AllocationPercent,
                                     FromDate = m.FromDate,
                                     ToDate = m.ToDate,
                                     Status = m.Status,
                                 };
                    var dataset = new DatasetQuery<TeamMemberReportRow>(m => m.TeamId)
                        .Field("teamNumber", m => m.TeamNumber)
                        .Field("teamNameAr", m => m.TeamNameAr)
                        .Field("teamNameEn", m => m.TeamNameEn)
                        .Field("teamType", m => m.TeamType)
                        .Field("teamStatus", m => m.TeamStatus)
                        .Field("unit", m => m.UnitId)
                        .Field("employee", m => m.EmployeeId)
                        .Field("externalName", m => m.ExternalName)
                        .Field("externalOrganisation", m => m.ExternalOrganisation)
                        .Field("role", m => m.RoleCode)
                        .Field("allocation", m => m.AllocationPercent)
                        .Field("fromDate", m => m.FromDate)
                        .Field("toDate", m => m.ToDate)
                        .Field("status", m => m.Status)
                        .Field("memberId", m => m.MemberId);

                    // Several members share a team id: page in a stable order unless the runner sorts.
                    var stable = query.Sort.Count > 0 || query.GroupBy.Count > 0
                        ? query
                        : query with { Sort = [new ReportSort("teamNumber", false), new ReportSort("memberId", false)] };
                    return await dataset.RunAsync(source, stable, cancellationToken).ConfigureAwait(false);
                }

            default:
                throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }
    }
}
