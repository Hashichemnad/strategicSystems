using Strategy.Modules.Teams.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Teams.Application;

/// <summary>Permission, record-role, numbering and lookup codes (docs/design/teams.md §6).</summary>
public static class TeamPermissions
{
    public const string SystemCode = "teams";
    public const string ResourceType = Team.EntityType;

    public const string Read = "teams.team.read";
    public const string Request = "teams.team.request";
    public const string Submit = "teams.team.submit";
    public const string Manage = "teams.team.manage";
    public const string Operate = "teams.team.operate";
    public const string Minutes = "teams.meeting.minutes";
    public const string MembershipRead = "teams.membership.read";
    public const string EvaluationOpen = "teams.evaluation.open";
    public const string Evaluate = "teams.evaluation.evaluate";
    public const string Results = "teams.evaluation.results";
    public const string RewardManage = "teams.reward.manage";
    public const string DashboardRead = "teams.dashboard.read";
    public const string Export = "teams.report.export";
    public const string SettingsManage = "teams.settings.manage";

    public const string AiFormation = "teams.ai.formation";
    public const string AiMembers = "teams.ai.members";
    public const string AiEvaluation = "teams.ai.evaluation";
    public const string AiMinutes = "teams.ai.minutes";

    public const string RequesterRole = "requester";
    public const string LeaderRole = "leader";
    public const string MemberRole = "member";
    public const string SecretaryRole = "secretary";
    public const string ResultsViewerRole = "results_viewer";
    public const string EvaluatorRole = "evaluator";
    public const string LeaderEvaluatorRole = "leader_evaluator";
    public const string ApproverRole = "approver";
    public const string EvaluationApproverRole = "evaluation_approver";
    public const string RewardApproverRole = "reward_approver";
    public const string RecommendationOwnerRole = "recommendation_owner";

    /// <summary>Approval-engine role key of HR (SPEC §4.3 "evaluation approval: HR").</summary>
    public const string AdministratorRoleKey = "teams.administrator";

    public const string TeamNumbering = "teams.team";
    public const string MeetingNumbering = "teams.meeting";
    public const string RecommendationNumbering = "teams.recommendation";
    public const string EvaluationNumbering = "teams.evaluation";
    public const string MemoNumbering = "teams.memo";
    public const string CertificateNumbering = "teams.certificate";

    public const string ApprovedByLookup = "teams.approved-by";

    public const string MembershipResource = "teams.membership";
}

/// <summary>Data scopes of a team (ADR 0015): own = leader (employee) or requester (user), unit = owning unit.</summary>
public static class TeamScope
{
    public static readonly ScopeMap<Team> Map = new(
        TeamPermissions.ResourceType, x => x.Id, ownerEmployee: x => x.LeaderEmployeeId, ownerUser: x => x.RequestedByUserId, unit: x => x.OwningUnitId);
}

/// <summary>Catalogue of the core Teams system (always on, like Tasks — ADR 0041).</summary>
internal sealed class TeamsAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(TeamPermissions.SystemCode, SystemKind.Platform, "إدارة الفرق واللجان", "Teams & committees", 65);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(TeamPermissions.Read, "عرض الفرق واللجان", "View teams and committees"),
        new(TeamPermissions.Request, "طلب تشكيل فريق", "Request a team", Scopable: false),
        new(TeamPermissions.Submit, "تعديل طلب التشكيل وتقديمه", "Edit and submit a formation request"),
        new(TeamPermissions.Manage, "إدارة الفرق (الأعضاء، الإغلاق، الربط، المذكرة)", "Manage teams (members, closure, links, memo)"),
        new(TeamPermissions.Operate, "تشغيل الفريق (الاجتماعات، التوصيات، التقارير)", "Run the team (meetings, recommendations, reports)"),
        new(TeamPermissions.Minutes, "تحرير محاضر الاجتماعات والحضور", "Write meeting minutes and attendance"),
        new(TeamPermissions.MembershipRead, "عرض عضويات الموظفين في الفرق", "View employees' team memberships"),
        new(TeamPermissions.EvaluationOpen, "فتح دورات التقييم وإلغاؤها", "Open and cancel evaluation cycles"),
        new(TeamPermissions.Evaluate, "تقييم الفريق وأعضائه", "Evaluate the team and its members"),
        new(TeamPermissions.Results, "عرض نتائج التقييم والمكافآت", "View evaluation results and rewards"),
        new(TeamPermissions.RewardManage, "إدارة المكافآت والشهادات", "Manage rewards and certificates"),
        new(TeamPermissions.DashboardRead, "عرض لوحة الفرق", "View the teams dashboard", Scopable: false),
        new(TeamPermissions.Export, "تصدير تقارير الفرق", "Export team reports", Scopable: false),
        new(TeamPermissions.SettingsManage, "إدارة إعدادات الفرق", "Manage team settings", Scopable: false),
        new(TeamPermissions.AiFormation, "مساعد التشكيل بالذكاء الاصطناعي", "AI formation assistant", Scopable: false),
        new(TeamPermissions.AiMembers, "اقتراح الأعضاء", "Member suggestions", Scopable: false),
        new(TeamPermissions.AiEvaluation, "مساعد التقييم بالذكاء الاصطناعي", "AI evaluation assistant", Scopable: false),
        new(TeamPermissions.AiMinutes, "كاتب المحاضر بالذكاء الاصطناعي", "AI minutes writer", Scopable: false),
        ResponsibilityPermissions.Definition(TeamPermissions.SystemCode),
    ];

    /// <summary>Members page lists (ADR 0166, docs/design/members-responsibilities.md §1).</summary>
    public SystemMemberRoles MemberRoles => new(["administrator"], ["employee"], ["department-manager", "viewer"]);

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("employee", "موظف (الفرق)", "Employee (teams)",
            [new(TeamPermissions.Read, DataScope.Own), new(TeamPermissions.Request), new(TeamPermissions.MembershipRead, DataScope.Own),
                new(TeamPermissions.AiFormation), new(TeamPermissions.AiMembers), new(TeamPermissions.AiEvaluation), new(TeamPermissions.AiMinutes)],
            AssignToEveryone: true),
        new("department-manager", "مدير الإدارة (الفرق)", "Department manager (teams)",
            [new(TeamPermissions.Read, DataScope.UnitAndBelow), new(TeamPermissions.MembershipRead, DataScope.UnitAndBelow), new(TeamPermissions.DashboardRead),
                new(TeamPermissions.Export)]),
        new("viewer", "مستعرض / القيادة (الفرق)", "Viewer / leadership (teams)",
            [new(TeamPermissions.Read, DataScope.All), new(TeamPermissions.MembershipRead, DataScope.All), new(TeamPermissions.DashboardRead),
                new(TeamPermissions.Export)]),
        new("administrator", "مسؤول الفرق (الموارد البشرية)", "Teams administrator (HR)",
            [new(TeamPermissions.Read, DataScope.All), new(TeamPermissions.Request), new(TeamPermissions.Submit, DataScope.All), new(TeamPermissions.Manage, DataScope.All),
                new(TeamPermissions.Operate, DataScope.All), new(TeamPermissions.MembershipRead, DataScope.All), new(TeamPermissions.EvaluationOpen, DataScope.All),
                new(TeamPermissions.Results, DataScope.All), new(TeamPermissions.RewardManage, DataScope.All), new(TeamPermissions.DashboardRead),
                new(TeamPermissions.Export), new(TeamPermissions.SettingsManage), new(TeamPermissions.AiFormation), new(TeamPermissions.AiMembers),
                new(TeamPermissions.AiEvaluation), new(TeamPermissions.AiMinutes), new(ResponsibilityPermissions.Manage(TeamPermissions.SystemCode), DataScope.All)],
            RequiresMfa: true),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles =>
    [
        R(TeamPermissions.RequesterRole, "طالب التشكيل", "Formation requester", TeamPermissions.Read, TeamPermissions.Submit),
        R(TeamPermissions.LeaderRole, "قائد الفريق", "Team leader", TeamPermissions.Read, TeamPermissions.Operate, TeamPermissions.Minutes),
        R(TeamPermissions.MemberRole, "عضو الفريق", "Team member", TeamPermissions.Read),
        R(TeamPermissions.SecretaryRole, "أمين سر الفريق", "Team secretary", TeamPermissions.Read, TeamPermissions.Minutes),
        R(TeamPermissions.ResultsViewerRole, "مطلع على نتائج التقييم", "Evaluation results viewer", TeamPermissions.Read, TeamPermissions.Results),
        R(TeamPermissions.EvaluatorRole, "مقيّم الفريق", "Team evaluator", TeamPermissions.Read, TeamPermissions.Evaluate, TeamPermissions.Results),
        R(TeamPermissions.LeaderEvaluatorRole, "مقيّم القائد", "Leader's evaluator", TeamPermissions.Read, TeamPermissions.Evaluate, TeamPermissions.Results),
        R(TeamPermissions.ApproverRole, "معتمد التشكيل", "Formation approver", TeamPermissions.Read),
        R(TeamPermissions.EvaluationApproverRole, "معتمد التقييم", "Evaluation approver", TeamPermissions.Read, TeamPermissions.Results),
        R(TeamPermissions.RewardApproverRole, "معتمد المكافآت", "Reward approver", TeamPermissions.Read, TeamPermissions.Results),
        R(TeamPermissions.RecommendationOwnerRole, "مسؤول توصية", "Recommendation owner", TeamPermissions.Read),
    ];

    private static RecordRoleDefinition R(string code, string ar, string en, params string[] permissions) =>
        new(TeamPermissions.ResourceType, code, ar, en, permissions);

    public static readonly NumberingDefinition TeamNumbering = new(TeamPermissions.TeamNumbering, TeamPermissions.SystemCode, "رقم الفريق", "Team code", "S-{SEQ:4}");

    public static readonly NumberingDefinition MeetingNumbering = new(
        TeamPermissions.MeetingNumbering, TeamPermissions.SystemCode, "رقم الاجتماع", "Meeting number", "MTG-{SEQ:5}");

    public static readonly NumberingDefinition RecommendationNumbering = new(
        TeamPermissions.RecommendationNumbering, TeamPermissions.SystemCode, "رقم التوصية", "Recommendation number", "REC-{SEQ:5}");

    public static readonly NumberingDefinition EvaluationNumbering = new(
        TeamPermissions.EvaluationNumbering, TeamPermissions.SystemCode, "رقم دورة التقييم", "Evaluation number", "EVAL-{YYYY}-{SEQ:4}");

    public static readonly NumberingDefinition MemoNumbering = new(
        TeamPermissions.MemoNumbering, TeamPermissions.SystemCode, "رقم قرار التشكيل", "Formation memo number", "MEMO-{YYYY}-{SEQ:4}");

    public static readonly NumberingDefinition CertificateNumbering = new(
        TeamPermissions.CertificateNumbering, TeamPermissions.SystemCode, "رقم شهادة التقدير", "Certificate number", "CERT-{YYYY}-{SEQ:4}");

    public static readonly LookupTypeDefinition ApprovedBy = new(TeamPermissions.ApprovedByLookup, TeamPermissions.SystemCode, "جهة اعتماد التشكيل", "Approved by",
    [
        new("general-manager", "المدير العام", "General Manager"),
        new("president", "الرئيس", "President"),
    ]);
}
