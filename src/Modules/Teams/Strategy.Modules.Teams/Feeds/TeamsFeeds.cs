using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.Modules.Teams.Feeds;

/// <summary>
/// Reportable record types of Teams (ADR 0190): teams / committees and their members, on the reader's scoped teams (<see cref="TeamReader"/>),
/// as the report builder. Evaluation scores, rewards and external members' contact details are not exposed.
/// </summary>
internal sealed class TeamsFeeds(TeamsDbContext db, TeamReader reader) : IReportableRecordSource
{
    public const string TeamType = Team.EntityType;
    public const string MemberType = TeamMember.EntityType;

    private static readonly RecordTypeQuery<Team> Teams = new RecordTypeQuery<Team>(t => t.Id, t => t.UpdatedAt)
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
        .Field("closedAt", t => t.ClosedAt)
        .Deleted(t => t.Status == TeamStatuses.Cancelled);

    private static readonly RecordTypeQuery<TeamMember> Members = new RecordTypeQuery<TeamMember>(m => m.Id, m => m.UpdatedAt)
        .Field("teamId", m => m.TeamId)
        .Field("employee", m => m.EmployeeId)
        .Field("externalName", m => m.ExternalName)
        .Field("externalOrganisation", m => m.ExternalOrganisation)
        .Field("role", m => m.RoleCode)
        .Field("allocation", m => m.AllocationPercent)
        .Field("fromDate", m => m.FromDate)
        .Field("toDate", m => m.ToDate)
        .Field("status", m => m.Status);

    public string SystemCode => TeamPermissions.SystemCode;

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(TeamType, TeamPermissions.SystemCode, "Teams", "الفرق واللجان", "Teams and committees", TeamPermissions.Read,
        [
            new("number", "الرمز", "Code", RecordFieldType.Text),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", RecordFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", RecordFieldType.Text),
            new("type", "النوع", "Type", RecordFieldType.Text),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices:
            [
                new(TeamStatuses.Draft, "مسودة", "Draft"), new(TeamStatuses.PendingApproval, "بانتظار الاعتماد", "Pending approval"),
                new(TeamStatuses.Active, "نشط", "Active"), new(TeamStatuses.Inactive, "غير نشط", "Inactive"),
                new(TeamStatuses.PendingEvaluation, "بانتظار التقييم", "Pending evaluation"),
                new(TeamStatuses.EvaluationWaitingApproval, "التقييم بانتظار الاعتماد", "Evaluation awaiting approval"),
                new(TeamStatuses.Closed, "مغلق", "Closed"), new(TeamStatuses.Replaced, "مستبدَل", "Replaced"), new(TeamStatuses.Cancelled, "ملغى", "Cancelled"),
            ]),
            new("permanent", "دائم", "Permanent", RecordFieldType.Bool),
            new("unit", "الوحدة المالكة", "Owning unit", RecordFieldType.Unit),
            new("leader", "القائد", "Leader", RecordFieldType.Employee),
            new("sponsor", "الراعي", "Sponsor", RecordFieldType.Employee),
            new("cadence", "دورية الاجتماعات", "Meeting cadence", RecordFieldType.Lookup, Choices:
            [
                new(Cadences.Monthly, "شهري", "Monthly"), new(Cadences.Quarterly, "ربع سنوي", "Quarterly"), new(Cadences.Biannually, "نصف سنوي", "Twice a year"),
                new(Cadences.Annually, "سنوي", "Annually"), new(Cadences.Other, "أخرى", "Other"),
            ]),
            new("workload", "عبء العمل", "Workload", RecordFieldType.Lookup, Choices:
            [
                new(Workloads.High, "عالٍ", "High"), new(Workloads.Medium, "متوسط", "Medium"), new(Workloads.Low, "منخفض", "Low"),
            ]),
            new("startDate", "تاريخ البدء", "Start date", RecordFieldType.Date),
            new("endDate", "تاريخ الانتهاء", "End date", RecordFieldType.Date),
            new("activatedAt", "تاريخ التفعيل", "Activated on", RecordFieldType.DateTime),
            new("closedAt", "تاريخ الإغلاق", "Closed on", RecordFieldType.DateTime),
        ],
        [
            new("members", "الأعضاء", "Members", MemberType, RecordFields.Id, "teamId", Many: true),
        ], "/teams/{id}"),
        new(MemberType, TeamPermissions.SystemCode, "TeamMembers", "أعضاء الفرق", "Team members", TeamPermissions.Read,
        [
            new("teamId", "الفريق", "Team", RecordFieldType.Id),
            new("employee", "الموظف", "Employee", RecordFieldType.Employee),
            new("externalName", "العضو الخارجي", "External member", RecordFieldType.Text),
            new("externalOrganisation", "جهة العضو الخارجي", "External organisation", RecordFieldType.Text),
            new("role", "الدور", "Role", RecordFieldType.Text),
            new("allocation", "نسبة التفرغ", "Allocation", RecordFieldType.Number),
            new("fromDate", "من", "From", RecordFieldType.Date),
            new("toDate", "إلى", "To", RecordFieldType.Date),
            new("status", "حالة العضوية", "Membership status", RecordFieldType.Lookup, Choices:
            [
                new(MemberStatuses.Active, "نشط", "Active"), new(MemberStatuses.Left, "غادر", "Left"), new(MemberStatuses.Replaced, "مستبدَل", "Replaced"),
            ]),
        ],
        [
            new("team", "الفريق", "Team", TeamType, "teamId", RecordFields.Id, Many: false),
        ], "/teams/{id}?tab=members"),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var teams = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        switch (query.RecordType)
        {
            case TeamType:
                return await Teams.RunAsync(teams, query, cancellationToken).ConfigureAwait(false);
            case MemberType:
                {
                    var source = db.Members.AsNoTracking().Where(m => teams.Any(t => t.Id == m.TeamId));
                    return await Members.RunAsync(source, query, cancellationToken).ConfigureAwait(false);
                }

            default:
                throw new ArgumentException($"Unknown record type '{query.RecordType}'.", nameof(query));
        }
    }
}
