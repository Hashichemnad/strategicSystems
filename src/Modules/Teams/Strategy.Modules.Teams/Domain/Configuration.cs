using System.Text.Json;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Modules.Teams.Domain;

/// <summary>Fixed logic codes (labels in the translation files).</summary>
public static class Permanence
{
    public const string Permanent = "permanent";
    public const string Temporary = "temporary";
    public const string Either = "either";

    public static readonly IReadOnlyList<string> All = [Permanent, Temporary, Either];
}

public static class EvaluationTriggers
{
    public const string None = "none";

    /// <summary>Permanent team → annual; temporary team → end date.</summary>
    public const string Auto = "auto";
    public const string Annual = "annual";
    public const string EndDate = "end-date";
    public const string LinkedRecordClosed = "linked-record-closed";
    public const string Manual = "manual";

    public static readonly IReadOnlyList<string> Configurable = [None, Auto, Annual, EndDate, LinkedRecordClosed];

    /// <summary>The trigger that applies to one team of a type.</summary>
    public static string Effective(string typeTrigger, bool permanent) =>
        typeTrigger == Auto ? (permanent ? Annual : EndDate) : typeTrigger;
}

public static class EvaluatorRules
{
    public const string Leader = "leader";
    public const string Sponsor = "sponsor";
    public const string Administrator = "administrator";
    public const string Employee = "employee";
    public const string MemberRole = "member-role";

    /// <summary>Leader evaluator only: the same evaluator as the team.</summary>
    public const string Same = "same";

    public static readonly IReadOnlyList<string> All = [Leader, Sponsor, Administrator, Employee, MemberRole];

    public static readonly IReadOnlyList<string> ForLeader = [Same, Leader, Sponsor, Administrator, Employee, MemberRole];
}

public static class Cadences
{
    public const string Monthly = "monthly";
    public const string Quarterly = "quarterly";
    public const string Biannually = "biannually";
    public const string Annually = "annually";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [Monthly, Quarterly, Biannually, Annually, Other];

    /// <summary>Months per reporting period; null for <see cref="Other"/> (no periodic report).</summary>
    public static int? MonthsOf(string? cadence) => cadence switch
    {
        Monthly => 1,
        Quarterly => 3,
        Biannually => 6,
        Annually => 12,
        _ => null,
    };
}

public static class Workloads
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";

    public static readonly IReadOnlyList<string> All = [High, Medium, Low];
}

public static class VersionStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Retired = "retired";
}

/// <summary>A team type (SPEC §4.3), Settings → Teams.</summary>
[Audited(EntityType)]
public sealed class TeamType : AuditableEntity
{
    public const string EntityType = "teams.team_type";

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string Permanence { get; private set; } = Domain.Permanence.Either;

    public bool RequiresApproval { get; private set; } = true;

    public bool RequiresMemo { get; private set; } = true;

    /// <summary>Linkable record types teams of this type may link to; empty = any team-able type.</summary>
    public string[] LinkableTypes { get; private set; } = [];

    public string DefaultCadence { get; private set; } = Cadences.Quarterly;

    public string EvaluationTrigger { get; private set; } = EvaluationTriggers.Auto;

    public int AnnualMonth { get; private set; } = 1;

    public int AnnualDay { get; private set; } = 1;

    public string EvaluatorRule { get; private set; } = EvaluatorRules.Leader;

    public Guid? EvaluatorEmployeeId { get; private set; }

    public string? EvaluatorMemberRole { get; private set; }

    public string LeaderEvaluatorRule { get; private set; } = EvaluatorRules.Same;

    public Guid? LeaderEvaluatorEmployeeId { get; private set; }

    public string? LeaderEvaluatorMemberRole { get; private set; }

    public int Sort { get; private set; }

    public bool Active { get; private set; } = true;

    public static TeamType Create(string code) => new() { Code = code };

    public void Update(TeamTypeValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        NameAr = v.NameAr.Trim();
        NameEn = v.NameEn.Trim();
        Permanence = v.Permanence;
        RequiresApproval = v.RequiresApproval;
        RequiresMemo = v.RequiresMemo;
        LinkableTypes = [.. v.LinkableTypes.Distinct(StringComparer.Ordinal)];
        DefaultCadence = v.DefaultCadence;
        EvaluationTrigger = v.EvaluationTrigger;
        AnnualMonth = v.AnnualMonth;
        AnnualDay = v.AnnualDay;
        EvaluatorRule = v.EvaluatorRule;
        EvaluatorEmployeeId = v.EvaluatorRule == EvaluatorRules.Employee ? v.EvaluatorEmployeeId : null;
        EvaluatorMemberRole = v.EvaluatorRule == EvaluatorRules.MemberRole ? v.EvaluatorMemberRole : null;
        LeaderEvaluatorRule = v.LeaderEvaluatorRule;
        LeaderEvaluatorEmployeeId = v.LeaderEvaluatorRule == EvaluatorRules.Employee ? v.LeaderEvaluatorEmployeeId : null;
        LeaderEvaluatorMemberRole = v.LeaderEvaluatorRule == EvaluatorRules.MemberRole ? v.LeaderEvaluatorMemberRole : null;
        Sort = v.Sort;
        Active = v.Active;
    }
}

public sealed record TeamTypeValues(
    string NameAr,
    string NameEn,
    string Permanence,
    bool RequiresApproval,
    bool RequiresMemo,
    IReadOnlyList<string> LinkableTypes,
    string DefaultCadence,
    string EvaluationTrigger,
    int AnnualMonth,
    int AnnualDay,
    string EvaluatorRule,
    Guid? EvaluatorEmployeeId,
    string? EvaluatorMemberRole,
    string LeaderEvaluatorRule,
    Guid? LeaderEvaluatorEmployeeId,
    string? LeaderEvaluatorMemberRole,
    int Sort,
    bool Active);

/// <summary>A member role (SPEC §4.3), Settings → Teams.</summary>
[Audited(EntityType)]
public sealed class MemberRole : AuditableEntity
{
    public const string EntityType = "teams.member_role";

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public int Sort { get; private set; }

    public bool IsLeader { get; private set; }

    /// <summary>Members holding this role see evaluation results (ADR 0042). Default: leader roles only.</summary>
    public bool CanViewResults { get; private set; }

    public bool CountsTowardQuorum { get; private set; } = true;

    /// <summary>Writes minutes and attendance.</summary>
    public bool IsSecretary { get; private set; }

    public bool Active { get; private set; } = true;

    public static MemberRole Create(string code) => new() { Code = code };

    public void Update(string nameAr, string nameEn, int sort, bool isLeader, bool canViewResults, bool countsTowardQuorum, bool isSecretary, bool active)
    {
        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        Sort = sort;
        IsLeader = isLeader;
        CanViewResults = canViewResults;
        CountsTowardQuorum = countsTowardQuorum;
        IsSecretary = isSecretary;
        Active = active;
    }
}

public static class CriterionDataSources
{
    public const string RecommendationsClosure = "recommendations-closure";
    public const string MinutesDelay = "minutes-delay";

    public static readonly IReadOnlyList<string> All = [RecommendationsClosure, MinutesDelay];
}

/// <summary>One level of a team criterion. <c>Min</c> / <c>Max</c>: data range for a data-backed criterion (Min inclusive, Max inclusive; null = open).</summary>
public sealed record CriterionLevel(string Code, string NameAr, string NameEn, decimal Score, decimal? Min = null, decimal? Max = null);

public sealed record TeamCriterion(string Code, string NameAr, string NameEn, decimal Weight, IReadOnlyList<CriterionLevel> Levels, string? DataSource = null);

public sealed record MemberQuestion(string Code, string TextAr, string TextEn);

public sealed record GradeBand(string Code, string NameAr, string NameEn, int Min, int Max);

/// <summary>The content of an evaluation template version (stored as JSON).</summary>
public sealed record TemplateContent(
    IReadOnlyList<TeamCriterion> Criteria, IReadOnlyList<MemberQuestion> Questions, IReadOnlyList<GradeBand> GradeBands, decimal EvidenceThreshold)
{
    public const int AnswerMin = 1;
    public const int AnswerMax = 5;

    public int MinPoints => Questions.Count * AnswerMin;

    public int MaxPoints => Questions.Count * AnswerMax;
}

/// <summary>An evaluation template version (ADR 0041): immutable once published.</summary>
[Audited(EntityType)]
public sealed class EvaluationTemplate : AuditableEntity
{
    public const string EntityType = "teams.evaluation_template";

    public int Year { get; private set; }

    public int VersionNo { get; private set; }

    public string Status { get; private set; } = VersionStatuses.Draft;

    public string Content { get; private set; } = "{}";

    public string? Notes { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public TemplateContent Read() => JsonSerializer.Deserialize<TemplateContent>(Content, TeamJson.Options)!;

    public static EvaluationTemplate Create(int year, int versionNo, TemplateContent content, string? notes) =>
        new() { Year = year, VersionNo = versionNo, Content = JsonSerializer.Serialize(content, TeamJson.Options), Notes = notes };

    public bool Edit(TemplateContent content, string? notes)
    {
        if (Status != VersionStatuses.Draft)
        {
            return false;
        }

        Content = JsonSerializer.Serialize(content, TeamJson.Options);
        Notes = notes;
        return true;
    }

    public void Publish(DateTimeOffset at)
    {
        Status = VersionStatuses.Published;
        PublishedAt = at;
    }

    public void Retire() => Status = VersionStatuses.Retired;
}

/// <summary>One cell of the reward matrix: grade × workload × member role → amount.</summary>
public sealed record RewardCell(string Grade, string Workload, string Role, decimal Amount);

public sealed record RewardMatrixContent(IReadOnlyList<RewardCell> Cells, IReadOnlyList<string> CertificateGrades, decimal AnnualBudget, string Currency);

/// <summary>A reward matrix version for a year (ADR 0041). Seeded empty (Q-T5).</summary>
[Audited(EntityType)]
public sealed class RewardMatrix : AuditableEntity
{
    public const string EntityType = "teams.reward_matrix";

    public int Year { get; private set; }

    public int VersionNo { get; private set; }

    public string Status { get; private set; } = VersionStatuses.Draft;

    public string Content { get; private set; } = "{}";

    public DateTimeOffset? PublishedAt { get; private set; }

    public RewardMatrixContent Read() => JsonSerializer.Deserialize<RewardMatrixContent>(Content, TeamJson.Options)!;

    public static RewardMatrix Create(int year, int versionNo, RewardMatrixContent content) =>
        new() { Year = year, VersionNo = versionNo, Content = JsonSerializer.Serialize(content, TeamJson.Options) };

    public bool Edit(RewardMatrixContent content)
    {
        if (Status != VersionStatuses.Draft)
        {
            return false;
        }

        Content = JsonSerializer.Serialize(content, TeamJson.Options);
        return true;
    }

    public void Publish(DateTimeOffset at)
    {
        Status = VersionStatuses.Published;
        PublishedAt = at;
    }

    public void Retire() => Status = VersionStatuses.Retired;
}

/// <summary>JSON options of the module's jsonb columns.</summary>
public static class TeamJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
