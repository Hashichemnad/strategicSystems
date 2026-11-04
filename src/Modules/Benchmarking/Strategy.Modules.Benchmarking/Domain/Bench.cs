using System.Text.Json;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Benchmarking.Domain;

/// <summary>The four stages (SPEC §8.2) and the closed stage.</summary>
public static class StudyStages
{
    public const string Plan = "plan";
    public const string Search = "search";
    public const string Acquire = "acquire";
    public const string Analysis = "analysis";
    public const string Completed = "completed";

    public static readonly IReadOnlyList<string> All = [Plan, Search, Acquire, Analysis, Completed];
}

public static class StudyStatuses
{
    public const string Active = "active";
    public const string Cancelled = "cancelled";
    public const string Completed = "completed";

    public static readonly IReadOnlyList<string> All = [Active, Cancelled, Completed];
}

/// <summary>Stage actions (docs/design/benchmarking.md §2).</summary>
public static class StudyActions
{
    public const string PlanApproved = "plan-approved";
    public const string SubmitSearch = "submit-search";
    public const string SearchApproved = "search-approved";
    public const string SearchReturned = "search-returned";
    public const string SendToAnalysis = "send-to-analysis";
    public const string SubmitAnalysis = "submit-analysis";
    public const string AnalysisApproved = "analysis-approved";
    public const string AnalysisReturned = "analysis-returned";
    public const string RequestClosure = "request-closure";
    public const string ClosureApproved = "closure-approved";
    public const string ClosureRefused = "closure-refused";

    public static readonly IReadOnlyList<string> All =
    [
        PlanApproved, SubmitSearch, SearchApproved, SearchReturned, SendToAnalysis, SubmitAnalysis, AnalysisApproved, AnalysisReturned, RequestClosure, ClosureApproved,
        ClosureRefused,
    ];
}

/// <summary>The study stage machine (explicit table, unit-tested). Key: stage, whether the analysis is approved, action.</summary>
public static class StudyLifecycle
{
    private static readonly Dictionary<(string Stage, bool AnalysisApproved, string Action), string> Table = new()
    {
        [(StudyStages.Plan, false, StudyActions.PlanApproved)] = StudyStages.Search,
        [(StudyStages.Search, false, StudyActions.SubmitSearch)] = StudyStages.Search,
        [(StudyStages.Search, false, StudyActions.SearchApproved)] = StudyStages.Acquire,
        [(StudyStages.Search, false, StudyActions.SearchReturned)] = StudyStages.Search,
        [(StudyStages.Acquire, false, StudyActions.SendToAnalysis)] = StudyStages.Analysis,
        [(StudyStages.Analysis, false, StudyActions.SubmitAnalysis)] = StudyStages.Analysis,
        [(StudyStages.Analysis, false, StudyActions.AnalysisApproved)] = StudyStages.Analysis,
        [(StudyStages.Analysis, false, StudyActions.AnalysisReturned)] = StudyStages.Analysis,
        [(StudyStages.Analysis, true, StudyActions.RequestClosure)] = StudyStages.Analysis,
        [(StudyStages.Analysis, true, StudyActions.ClosureApproved)] = StudyStages.Completed,
        [(StudyStages.Analysis, true, StudyActions.ClosureRefused)] = StudyStages.Analysis,
    };

    public static string? Next(string stage, bool analysisApproved, string action) =>
        Table.TryGetValue((stage, analysisApproved, action), out var to) ? to : null;

    public static bool Allows(string stage, bool analysisApproved, string action) => Table.ContainsKey((stage, analysisApproved, action));
}

/// <summary>Quarters and the pre-quarter notice date (ADR 0127). Pure.</summary>
public static class Quarters
{
    public static DateOnly Start(int year, int quarter) => new(year, ((quarter - 1) * 3) + 1, 1);

    public static DateOnly End(int year, int quarter) => Start(year, quarter).AddMonths(3).AddDays(-1);

    public static int Of(DateOnly date) => ((date.Month - 1) / 3) + 1;
}

public static class ComparatorScopes
{
    public const string Local = "local";
    public const string Regional = "regional";
    public const string International = "international";

    public static readonly IReadOnlyList<string> All = [Local, Regional, International];
}

public static class VisitMethods
{
    public const string SiteVisit = "site-visit";
    public const string Virtual = "virtual";
    public const string Questionnaire = "questionnaire";

    public static readonly IReadOnlyList<string> All = [SiteVisit, Virtual, Questionnaire];
}

public static class PracticeCategories
{
    public const string Compared = "compared";
    public const string NotApplicable = "not-applicable";
    public const string OutsideScope = "outside-scope";

    public static readonly IReadOnlyList<string> All = [Compared, NotApplicable, OutsideScope];
}

public static class OutreachChannels
{
    public static readonly IReadOnlyList<string> All = ["email", "phone", "meeting", "letter", "other"];

    public static readonly IReadOnlyList<string> Responses = ["pending", "accepted", "declined", "no-reply"];
}

/// <summary>A benchmarking study (SPEC §8.4, <c>BMS-NNNN</c>).</summary>
[Audited(EntityType)]
public sealed class Study : AuditableEntity
{
    public const string EntityType = "bench.study";
    public const int TitleMaxLength = 400;
    public const int TextMaxLength = 4000;

    private Study()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public Guid RequestId { get; private set; }

    public string? RequestNumber { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public Guid ResponsibleUnitId { get; private set; }

    public Guid? SectionUnitId { get; private set; }

    public Guid? ResponsibleEmployeeId { get; private set; }

    public Guid? RequesterEmployeeId { get; private set; }

    public int TargetYear { get; private set; }

    public int TargetQuarter { get; private set; }

    public string? ComparisonTypeCode { get; private set; }

    public string? PurposeCode { get; private set; }

    public string? Scope { get; private set; }

    public string Stage { get; private set; } = StudyStages.Plan;

    public string Status { get; private set; } = StudyStatuses.Active;

    public string? PendingProcess { get; private set; }

    public bool Returned { get; private set; }

    public string? ReturnComment { get; private set; }

    public bool SearchSubmitted { get; private set; }

    public DateTimeOffset? SearchNotifiedAt { get; private set; }

    public Guid? TeamId { get; private set; }

    public Guid? CapSourceId { get; private set; }

    public DateTimeOffset? PlanApprovedAt { get; private set; }

    public DateTimeOffset? AnalysisApprovedAt { get; private set; }

    public DateTimeOffset? ReadyNotifiedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public DateOnly? ImpactDueOn { get; private set; }

    public DateTimeOffset? ImpactNotifiedAt { get; private set; }

    public string? CancelReason { get; private set; }

    public bool AnalysisApproved => AnalysisApprovedAt is not null;

    public bool IsActive => Status == StudyStatuses.Active;

    public static Study Create(string number, Guid requestId, string? requestNumber) => new() { Number = number, RequestId = requestId, RequestNumber = requestNumber };

    public void Describe(StudyPlan p)
    {
        ArgumentNullException.ThrowIfNull(p);
        TitleAr = Clean(p.TitleAr);
        TitleEn = Clean(p.TitleEn);
        ResponsibleUnitId = p.ResponsibleUnitId;
        SectionUnitId = p.SectionUnitId;
        ResponsibleEmployeeId = p.ResponsibleEmployeeId;
        RequesterEmployeeId = p.RequesterEmployeeId;
        TargetYear = p.TargetYear;
        TargetQuarter = p.TargetQuarter;
        ComparisonTypeCode = Clean(p.ComparisonTypeCode);
        PurposeCode = Clean(p.PurposeCode);
        Scope = Clean(p.Scope);
    }

    public void SetPlanReturned(bool returned, string? comment)
    {
        Returned = returned;
        ReturnComment = returned ? Clean(comment) : null;
    }

    public void SetPending(string? process) => PendingProcess = process;

    public void SetTeam(Guid? teamId) => TeamId = teamId;

    public void LinkCapSource(Guid sourceId) => CapSourceId = sourceId;

    public void MarkSearchNotified(DateTimeOffset at) => SearchNotifiedAt = at;

    public void MarkReadyNotified(DateTimeOffset at) => ReadyNotifiedAt = at;

    public void MarkImpactNotified(DateTimeOffset at) => ImpactNotifiedAt = at;

    /// <summary>Applies a stage action (docs/design/benchmarking.md §2).</summary>
    public Result Apply(string action, DateTimeOffset at, string? comment = null, DateOnly? impactDue = null)
    {
        if (!IsActive || StudyLifecycle.Next(Stage, AnalysisApproved, action) is not { } next)
        {
            return Error.Rule("bench.invalidTransition", "bench.errors.invalidTransition",
                new Dictionary<string, object?> { ["stage"] = Stage, ["action"] = action });
        }

        switch (action)
        {
            case StudyActions.PlanApproved:
                PlanApprovedAt = at;
                Returned = false;
                ReturnComment = null;
                break;
            case StudyActions.SubmitSearch:
                SearchSubmitted = true;
                Returned = false;
                break;
            case StudyActions.SearchReturned:
            case StudyActions.AnalysisReturned:
                SearchSubmitted = action != StudyActions.SearchReturned && SearchSubmitted;
                Returned = true;
                ReturnComment = Clean(comment);
                break;
            case StudyActions.SubmitAnalysis:
                Returned = false;
                ReturnComment = null;
                break;
            case StudyActions.AnalysisApproved:
                AnalysisApprovedAt = at;
                break;
            case StudyActions.ClosureApproved:
                ClosedAt = at;
                Status = StudyStatuses.Completed;
                ImpactDueOn = impactDue;
                break;
        }

        Stage = next;
        return Result.Success();
    }

    public void Cancel(string? reason)
    {
        Status = StudyStatuses.Cancelled;
        CancelReason = Clean(reason);
        PendingProcess = null;
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The Plan values of a study (from the request form).</summary>
public sealed record StudyPlan(
    string? TitleAr, string? TitleEn, Guid ResponsibleUnitId, Guid? SectionUnitId, Guid? ResponsibleEmployeeId, Guid? RequesterEmployeeId, int TargetYear, int TargetQuarter,
    string? ComparisonTypeCode, string? PurposeCode, string? Scope);

/// <summary>A comparison topic (at least one per study, each with an owner).</summary>
[Audited(EntityType)]
public sealed class Topic : AuditableEntity
{
    public const string EntityType = "bench.topic";

    private Topic()
    {
    }

    public Guid StudyId { get; private set; }

    /// <summary>Removed from the stage form while it was a draft (no hard delete, CLAUDE.md §6).</summary>
    public bool Removed { get; private set; }

    public void Remove() => Removed = true;

    public string Title { get; private set; } = string.Empty;

    public Guid? OwnerEmployeeId { get; private set; }

    public string? Description { get; private set; }

    public int Sort { get; private set; }

    public static Topic Create(Guid studyId, string title, Guid? owner, string? description, int sort) =>
        new() { StudyId = studyId, Title = title.Trim(), OwnerEmployeeId = owner, Description = Study.Clean(description), Sort = sort };

    public void Update(string title, Guid? owner, string? description, int sort)
    {
        Removed = false;
        Title = title.Trim();
        OwnerEmployeeId = owner;
        Description = Study.Clean(description);
        Sort = sort;
    }
}

public sealed record ComparatorValues(
    string? NameAr, string? NameEn, string? SectorCode, string? Country, string Scope, string? Website, string? ContactName, string? ContactEmail, string? ContactPhone,
    string? Notes, bool Active);

/// <summary>A comparator entity (shared register, SPEC §8.3).</summary>
[Audited(EntityType)]
public sealed class Comparator : AuditableEntity
{
    public const string EntityType = "bench.comparator";

    private Comparator()
    {
    }

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? SectorCode { get; private set; }

    public string? Country { get; private set; }

    public string Scope { get; private set; } = ComparatorScopes.Local;

    public string? Website { get; private set; }

    public string? ContactName { get; private set; }

    public string? ContactEmail { get; private set; }

    public string? ContactPhone { get; private set; }

    public string? Notes { get; private set; }

    public bool Active { get; private set; } = true;

    public static Comparator Create(ComparatorValues values)
    {
        var c = new Comparator();
        c.Update(values);
        return c;
    }

    public void Update(ComparatorValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        NameAr = Study.Clean(v.NameAr);
        NameEn = Study.Clean(v.NameEn);
        SectorCode = Study.Clean(v.SectorCode);
        Country = Study.Clean(v.Country);
        Scope = v.Scope;
        Website = Study.Clean(v.Website);
        ContactName = Study.Clean(v.ContactName);
        ContactEmail = Study.Clean(v.ContactEmail);
        ContactPhone = Study.Clean(v.ContactPhone);
        Notes = Study.Clean(v.Notes);
        Active = v.Active;
    }

    public ComparatorValues Values() => new(NameAr, NameEn, SectorCode, Country, Scope, Website, ContactName, ContactEmail, ContactPhone, Notes, Active);
}

/// <summary>A candidate comparator of a study (search form, SPEC §8.2.2).</summary>
[Audited(EntityType)]
public sealed class StudyEntity : AuditableEntity
{
    public const string EntityType = "bench.study-entity";

    private StudyEntity()
    {
    }

    public Guid StudyId { get; private set; }

    /// <summary>Removed from the stage form while it was a draft (no hard delete, CLAUDE.md §6).</summary>
    public bool Removed { get; private set; }

    public void Remove() => Removed = true;

    public Guid ComparatorId { get; private set; }

    public bool Selected { get; private set; }

    public string? Reason { get; private set; }

    public string? AvailableInfo { get; private set; }

    public string? Contact { get; private set; }

    public bool CodeOfConductAck { get; private set; }

    public int? Rating { get; private set; }

    public string? RatingNote { get; private set; }

    public static StudyEntity Create(Guid studyId, Guid comparatorId) => new() { StudyId = studyId, ComparatorId = comparatorId };

    public void Update(bool selected, string? reason, string? availableInfo, string? contact, bool codeOfConduct)
    {
        Removed = false;
        Selected = selected;
        Reason = Study.Clean(reason);
        AvailableInfo = Study.Clean(availableInfo);
        Contact = Study.Clean(contact);
        CodeOfConductAck = codeOfConduct;
    }

    public void Rate(int? rating, string? note)
    {
        Rating = rating;
        RatingNote = Study.Clean(note);
    }
}

/// <summary>An outreach entry with a selected comparator (append-only log).</summary>
[Audited(EntityType)]
public sealed class Outreach : AuditableEntity
{
    public const string EntityType = "bench.outreach";

    private Outreach()
    {
    }

    public Guid StudyEntityId { get; private set; }

    public DateOnly On { get; private set; }

    public string Channel { get; private set; } = "email";

    public string? Summary { get; private set; }

    public string Response { get; private set; } = "pending";

    public static Outreach Create(Guid studyEntityId, DateOnly on, string channel, string? summary, string response) =>
        new() { StudyEntityId = studyEntityId, On = on, Channel = channel, Summary = Study.Clean(summary), Response = response };
}

/// <summary>Visit info (Acquire, SPEC §8.2.3).</summary>
[Audited(EntityType)]
public sealed class Visit : AuditableEntity
{
    public const string EntityType = "bench.visit";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Visit()
    {
    }

    public Guid StudyId { get; private set; }

    /// <summary>Removed from the stage form while it was a draft (no hard delete, CLAUDE.md §6).</summary>
    public bool Removed { get; private set; }

    public void Remove() => Removed = true;

    public Guid ComparatorId { get; private set; }

    public DateOnly? AgreedOn { get; private set; }

    public string Method { get; private set; } = VisitMethods.SiteVisit;

    public string ParticipantIdsJson { get; private set; } = "[]";

    public Guid? TeamId { get; private set; }

    public string? Questionnaire { get; private set; }

    public DateOnly? QuestionnaireSentOn { get; private set; }

    public string? Notes { get; private set; }

    public IReadOnlyList<Guid> ParticipantIds => JsonSerializer.Deserialize<List<Guid>>(ParticipantIdsJson, Json) ?? [];

    public static Visit Create(Guid studyId) => new() { StudyId = studyId };

    public void Update(Guid comparatorId, DateOnly? agreedOn, string method, IEnumerable<Guid> participants, Guid? teamId, string? questionnaire, DateOnly? sentOn,
        string? notes)
    {
        ComparatorId = comparatorId;
        AgreedOn = agreedOn;
        Method = method;
        ParticipantIdsJson = JsonSerializer.Serialize(participants.Distinct().ToList(), Json);
        TeamId = teamId;
        Questionnaire = Study.Clean(questionnaire);
        QuestionnaireSentOn = sentOn;
        Notes = Study.Clean(notes);
    }
}

/// <summary>A practice comparison row (Analysis, SPEC §8.4).</summary>
[Audited(EntityType)]
public sealed class PracticeRow : AuditableEntity
{
    public const string EntityType = "bench.practice-row";

    private PracticeRow()
    {
    }

    public Guid StudyId { get; private set; }

    /// <summary>Removed from the stage form while it was a draft (no hard delete, CLAUDE.md §6).</summary>
    public bool Removed { get; private set; }

    public void Remove() => Removed = true;

    public Guid? TopicId { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public string? OurPractice { get; private set; }

    public string? TheirPractice { get; private set; }

    public string Category { get; private set; } = PracticeCategories.Compared;

    public bool Applicable { get; private set; } = true;

    public bool AddToLibrary { get; private set; }

    public int Sort { get; private set; }

    public static PracticeRow Create(Guid studyId) => new() { StudyId = studyId };

    public void Update(Guid? topicId, string subject, string? ours, string? theirs, string category, bool applicable, bool addToLibrary, int sort)
    {
        TopicId = topicId;
        Subject = subject.Trim();
        OurPractice = Study.Clean(ours);
        TheirPractice = Study.Clean(theirs);
        Category = category;
        Applicable = applicable;
        AddToLibrary = addToLibrary;
        Sort = sort;
    }
}

/// <summary>An indicator comparison row (optionally linked to a KPI card).</summary>
[Audited(EntityType)]
public sealed class IndicatorRow : AuditableEntity
{
    public const string EntityType = "bench.indicator-row";

    private IndicatorRow()
    {
    }

    public Guid StudyId { get; private set; }

    /// <summary>Removed from the stage form while it was a draft (no hard delete, CLAUDE.md §6).</summary>
    public bool Removed { get; private set; }

    public void Remove() => Removed = true;

    public Guid? KpiCardId { get; private set; }

    public string? KpiCardCode { get; private set; }

    public string Indicator { get; private set; } = string.Empty;

    public decimal? OurValue { get; private set; }

    public decimal? TheirValue { get; private set; }

    public string? Period { get; private set; }

    public string? SourceText { get; private set; }

    public bool UpdateKpiBenchmark { get; private set; }

    public int Sort { get; private set; }

    public static IndicatorRow Create(Guid studyId) => new() { StudyId = studyId };

    public void Update(Guid? kpiCardId, string? kpiCardCode, string indicator, decimal? ours, decimal? theirs, string? period, string? source, bool updateKpi, int sort)
    {
        KpiCardId = kpiCardId;
        KpiCardCode = kpiCardCode;
        Indicator = indicator.Trim();
        OurValue = ours;
        TheirValue = theirs;
        Period = Study.Clean(period);
        SourceText = Study.Clean(source);
        UpdateKpiBenchmark = kpiCardId is not null && updateKpi;
        Sort = sort;
    }
}

/// <summary>A development-plan item (improvement action, at least one) — becomes a CAP remark with a proposed action (ADR 0128).</summary>
[Audited(EntityType)]
public sealed class PlanItem : AuditableEntity
{
    public const string EntityType = "bench.plan-item";

    private PlanItem()
    {
    }

    public Guid StudyId { get; private set; }

    /// <summary>Removed from the stage form while it was a draft (no hard delete, CLAUDE.md §6).</summary>
    public bool Removed { get; private set; }

    public void Remove() => Removed = true;

    public Guid? TopicId { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public string? Description { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public Guid? UnitId { get; private set; }

    public DateOnly? DueOn { get; private set; }

    public string Priority { get; private set; } = "medium";

    public string? ActionTypeCode { get; private set; }

    public bool BudgetRequired { get; private set; }

    public Guid? RemarkId { get; private set; }

    public int Sort { get; private set; }

    public static PlanItem Create(Guid studyId) => new() { StudyId = studyId };

    public void Update(Guid? topicId, string? titleAr, string? titleEn, string? description, Guid? owner, Guid? unitId, DateOnly? dueOn, string priority, string? actionType,
        bool budgetRequired, int sort)
    {
        TopicId = topicId;
        TitleAr = Study.Clean(titleAr);
        TitleEn = Study.Clean(titleEn);
        Description = Study.Clean(description);
        OwnerEmployeeId = owner;
        UnitId = unitId;
        DueOn = dueOn;
        Priority = priority;
        ActionTypeCode = Study.Clean(actionType);
        BudgetRequired = budgetRequired;
        Sort = sort;
    }

    public void LinkRemark(Guid remarkId) => RemarkId = remarkId;
}

/// <summary>A best-practice library entry (SPEC §8.3).</summary>
[Audited(EntityType)]
public sealed class BestPractice : AuditableEntity
{
    public const string EntityType = "bench.best-practice";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private BestPractice()
    {
    }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public string? Description { get; private set; }

    public string? Topic { get; private set; }

    public string TagsJson { get; private set; } = "[]";

    public string? Applicability { get; private set; }

    public string? SectorCode { get; private set; }

    public Guid? StudyId { get; private set; }

    public Guid? ComparatorId { get; private set; }

    public Guid? PracticeRowId { get; private set; }

    public string Origin { get; private set; } = "manual";

    public bool Active { get; private set; } = true;

    public IReadOnlyList<string> Tags => JsonSerializer.Deserialize<List<string>>(TagsJson, Json) ?? [];

    public static BestPractice Create(string origin, Guid? studyId, Guid? comparatorId, Guid? practiceRowId) =>
        new() { Origin = origin, StudyId = studyId, ComparatorId = comparatorId, PracticeRowId = practiceRowId };

    public void Update(string? titleAr, string? titleEn, string? description, string? topic, IEnumerable<string> tags, string? applicability, string? sector, bool active)
    {
        TitleAr = Study.Clean(titleAr);
        TitleEn = Study.Clean(titleEn);
        Description = Study.Clean(description);
        Topic = Study.Clean(topic);
        TagsJson = JsonSerializer.Serialize(tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().Take(20).ToList(), Json);
        Applicability = Study.Clean(applicability);
        SectorCode = Study.Clean(sector);
        Active = active;
    }
}

/// <summary>Impact follow-up after closure (SPEC §8.3, optional).</summary>
[Audited(EntityType)]
public sealed class ImpactReview : AuditableEntity
{
    public const string EntityType = "bench.impact-review";

    private ImpactReview()
    {
    }

    public Guid StudyId { get; private set; }

    public DateOnly? DueOn { get; private set; }

    public bool Implemented { get; private set; }

    public bool KpisMoved { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset ReviewedAt { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public static ImpactReview Create(Guid studyId, DateOnly? dueOn, bool implemented, bool kpisMoved, string? notes, DateTimeOffset at, Guid? by) =>
        new() { StudyId = studyId, DueOn = dueOn, Implemented = implemented, KpisMoved = kpisMoved, Notes = Study.Clean(notes), ReviewedAt = at, ReviewedBy = by };
}

/// <summary>A circulation of the annual plan to all units (SPEC §8.2.1).</summary>
[Audited(EntityType)]
public sealed class PlanCirculation : AuditableEntity
{
    public const string EntityType = "bench.plan-circulation";

    private PlanCirculation()
    {
    }

    public int Year { get; private set; }

    public DateTimeOffset CirculatedAt { get; private set; }

    public Guid? CirculatedBy { get; private set; }

    public int Recipients { get; private set; }

    public int Studies { get; private set; }

    public static PlanCirculation Create(int year, DateTimeOffset at, Guid? by, int recipients, int studies) =>
        new() { Year = year, CirculatedAt = at, CirculatedBy = by, Recipients = recipients, Studies = studies };
}
