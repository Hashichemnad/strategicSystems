using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Teams.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Benchmarking.Application;

public sealed record BenchStudyPermissionsDto(
    bool EditSearch, bool SubmitSearch, bool Outreach, bool EditVisits, bool SendToAnalysis, bool EditAnalysis, bool SubmitAnalysis, bool RequestClosure, bool Cancel,
    bool LinkTeam, bool Impact, bool Office);

public sealed record BenchTopicDto(Guid Id, string Title, BenchPersonDto? Owner, string? Description, int Sort);

public sealed record BenchTeamDto(Guid Id, string? Number, string? NameAr, string? NameEn, string Status);

public sealed record BenchStudyListItemDto(
    Guid Id, string Number, string? TitleAr, string? TitleEn, BenchUnitDto? ResponsibleUnit, BenchPersonDto? ResponsibleEmployee, int TargetYear, int TargetQuarter,
    string? ComparisonTypeCode, string Stage, string Status, string? PendingProcess, bool Returned, bool AnalysisApproved);

public sealed record BenchStudyDto(
    Guid Id,
    string Number,
    Guid RequestId,
    string? RequestNumber,
    string? TitleAr,
    string? TitleEn,
    BenchUnitDto? ResponsibleUnit,
    BenchUnitDto? Section,
    BenchPersonDto? ResponsibleEmployee,
    BenchPersonDto? Requester,
    int TargetYear,
    int TargetQuarter,
    string? ComparisonTypeCode,
    string? PurposeCode,
    string? Scope,
    string Stage,
    string Status,
    string? PendingProcess,
    bool Returned,
    string? ReturnComment,
    bool SearchSubmitted,
    DateOnly? SearchNoticeOn,
    DateTimeOffset? SearchNotifiedAt,
    BenchTeamDto? Team,
    Guid? CapSourceId,
    DateTimeOffset? PlanApprovedAt,
    DateTimeOffset? AnalysisApprovedAt,
    DateTimeOffset? ClosedAt,
    DateOnly? ImpactDueOn,
    string? CancelReason,
    IReadOnlyList<BenchTopicDto> Topics,
    BenchStudyPermissionsDto Can,
    uint Version);

public sealed record BenchOutreachDto(Guid Id, DateOnly On, string Channel, string? Summary, string Response, DateTimeOffset CreatedAt);

public sealed record BenchCandidateDto(
    Guid Id, Guid ComparatorId, string? NameAr, string? NameEn, string? Country, string Scope, string? SectorCode, bool Selected, string? Reason, string? AvailableInfo,
    string? Contact, bool CodeOfConductAck, int? Rating, string? RatingNote, IReadOnlyList<BenchOutreachDto> Outreach);

public sealed record BenchVisitDto(
    Guid Id, Guid ComparatorId, string? ComparatorNameAr, string? ComparatorNameEn, DateOnly? AgreedOn, string Method, IReadOnlyList<BenchPersonDto> Participants, Guid? TeamId,
    string? Questionnaire, DateOnly? QuestionnaireSentOn, string? Notes);

public sealed record BenchPracticeDto(Guid Id, Guid? TopicId, string Subject, string? OurPractice, string? TheirPractice, string Category, bool Applicable, bool AddToLibrary, int Sort);

public sealed record BenchIndicatorDto(
    Guid Id, Guid? KpiCardId, string? KpiCardCode, string Indicator, decimal? OurValue, decimal? TheirValue, string? Period, string? Source, bool UpdateKpiBenchmark, int Sort);

public sealed record BenchPlanItemDto(
    Guid Id, Guid? TopicId, string? TitleAr, string? TitleEn, string? Description, BenchPersonDto? Owner, BenchUnitDto? Unit, DateOnly? DueOn, string Priority,
    string? ActionTypeCode, bool BudgetRequired, Guid? RemarkId, int Sort);

public sealed record BenchAnalysisDto(IReadOnlyList<BenchPracticeDto> Practices, IReadOnlyList<BenchIndicatorDto> Indicators, IReadOnlyList<BenchPlanItemDto> PlanItems);

public sealed record BenchImpactDto(Guid Id, DateOnly? DueOn, bool Implemented, bool KpisMoved, string? Notes, DateTimeOffset ReviewedAt);

public sealed record BenchPlanRowDto(BenchStudyListItemDto Study, BenchUnitDto? Unit);

public sealed record BenchCirculationDto(int Year, DateTimeOffset CirculatedAt, int Recipients, int Studies);

public sealed record BenchPlanDto(int Year, IReadOnlyList<BenchStudyListItemDto> Studies, IReadOnlyList<BenchCirculationDto> Circulations, bool CanCirculate);

public sealed record BenchOptionDto(string Code, string? LabelAr, string? LabelEn);

public sealed record BenchOptionsDto(
    IReadOnlyDictionary<string, IReadOnlyList<BenchOptionDto>> Lookups, bool CanOffice, bool CanDashboard, bool CanManageComparators, bool CanManagePractices, bool CanSettings,
    bool CanCreate, string RequestTypeCode);

public sealed record BenchCandidateInput(Guid? ComparatorId, ComparatorValues? NewComparator, bool Selected, string? Reason, string? AvailableInfo, string? Contact,
    bool CodeOfConductAck);

public sealed record BenchVisitInput(Guid? Id, Guid ComparatorId, DateOnly? AgreedOn, string Method, IReadOnlyList<Guid>? ParticipantIds, Guid? TeamId, string? Questionnaire,
    DateOnly? QuestionnaireSentOn, string? Notes);

public sealed record BenchPracticeInput(Guid? Id, Guid? TopicId, string? Subject, string? OurPractice, string? TheirPractice, string Category, bool Applicable, bool AddToLibrary);

public sealed record BenchIndicatorInput(Guid? Id, Guid? KpiCardId, string? Indicator, decimal? OurValue, decimal? TheirValue, string? Period, string? Source, bool UpdateKpiBenchmark);

public sealed record BenchPlanItemInput(
    Guid? Id, Guid? TopicId, string? TitleAr, string? TitleEn, string? Description, Guid? OwnerEmployeeId, Guid? UnitId, DateOnly? DueOn, string? Priority, string? ActionTypeCode,
    bool BudgetRequired);

public sealed record BenchAnalysisInput(IReadOnlyList<BenchPracticeInput> Practices, IReadOnlyList<BenchIndicatorInput> Indicators, IReadOnlyList<BenchPlanItemInput> PlanItems);

public sealed record BenchOutreachInput(Guid StudyEntityId, DateOnly On, string Channel, string? Summary, string Response);

public sealed record BenchRatingInput(Guid StudyEntityId, int? Rating, string? Note);

public sealed record BenchImpactInput(bool Implemented, bool KpisMoved, string? Notes);

public sealed record ListStudies(PageRequest Page, string? View, string? Search, int? Year, int? Quarter, string? Stage, string? Status, Guid? UnitId)
    : IQuery<PagedResult<BenchStudyListItemDto>>;

public sealed record GetStudy(Guid Id) : IQuery<BenchStudyDto>;

public sealed record GetStudySearch(Guid Id) : IQuery<IReadOnlyList<BenchCandidateDto>>;

public sealed record GetStudyVisits(Guid Id) : IQuery<IReadOnlyList<BenchVisitDto>>;

public sealed record GetStudyAnalysis(Guid Id) : IQuery<BenchAnalysisDto>;

public sealed record GetStudyActions(Guid Id) : IQuery<CapSourceProgress?>;

public sealed record GetStudyImpact(Guid Id) : IQuery<IReadOnlyList<BenchImpactDto>>;

public sealed record GetBenchPlan(int Year) : IQuery<BenchPlanDto>;

public sealed record GetBenchOptions : IQuery<BenchOptionsDto>;

public sealed record SaveStudySearch(Guid Id, IReadOnlyList<BenchCandidateInput> Candidates) : ICommand<IReadOnlyList<BenchCandidateDto>>;

public sealed record SubmitStudySearch(Guid Id, string? Comment) : ICommand<BenchStudyDto>;

public sealed record AddStudyOutreach(Guid Id, BenchOutreachInput Input) : ICommand<IReadOnlyList<BenchCandidateDto>>;

public sealed record RateStudyComparator(Guid Id, BenchRatingInput Input) : ICommand<IReadOnlyList<BenchCandidateDto>>;

public sealed record SaveStudyVisits(Guid Id, IReadOnlyList<BenchVisitInput> Visits) : ICommand<IReadOnlyList<BenchVisitDto>>;

public sealed record SendStudyToAnalysis(Guid Id) : ICommand<BenchStudyDto>;

public sealed record SaveStudyAnalysis(Guid Id, BenchAnalysisInput Input) : ICommand<BenchAnalysisDto>;

public sealed record SubmitStudyAnalysis(Guid Id, string? Comment) : ICommand<BenchStudyDto>;

public sealed record RequestStudyClosure(Guid Id, string? Comment) : ICommand<BenchStudyDto>;

public sealed record CancelStudy(Guid Id, string? Reason) : ICommand<BenchStudyDto>;

public sealed record LinkStudyTeam(Guid Id, Guid? TeamId) : ICommand<BenchStudyDto>;

public sealed record RecordStudyImpact(Guid Id, BenchImpactInput Input) : ICommand<IReadOnlyList<BenchImpactDto>>;

public sealed record CirculateBenchPlan(int Year) : ICommand<BenchPlanDto>;

/// <summary>Maps studies for the API and works out what the caller may do (docs/design/benchmarking.md §7).</summary>
internal sealed class BenchViews(BenchDbContext db, BenchReader reader, BenchCalendar calendar, IRequestContext context, IServiceProvider services)
{
    public async Task<BenchStudyDto> ToDtoAsync(Study s, CancellationToken ct)
    {
        var user = context.User;
        var topics = await db.Topics.AsNoTracking().Where(t => t.StudyId == s.Id && !t.Removed).OrderBy(t => t.Sort).ToListAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync([s.ResponsibleEmployeeId, s.RequesterEmployeeId, .. topics.Select(t => t.OwnerEmployeeId)], ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync([s.ResponsibleUnitId, s.SectionUnitId], ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        DateOnly? notice = s.TargetYear > 0
            ? (await calendar.SearchNoticeAsync(s.ResponsibleUnitId, s.TargetYear, s.TargetQuarter, settings.SearchNoticeDays, settings.CountWorkingDays, ct).ConfigureAwait(false)).Day
            : null;
        BenchTeamDto? team = null;
        if (s.TeamId is { } teamId && services.GetService(typeof(ITeamsDirectory)) is ITeamsDirectory teams
            && await teams.GetTeamAsync(teamId, user, ct).ConfigureAwait(false) is { } t)
        {
            team = new BenchTeamDto(t.Id, t.Number, t.NameAr, t.NameEn, t.Status);
        }

        return new BenchStudyDto(s.Id, s.Number, s.RequestId, s.RequestNumber, s.TitleAr, s.TitleEn, BenchReader.Unit(s.ResponsibleUnitId, units),
            BenchReader.Unit(s.SectionUnitId, units), BenchReader.Person(s.ResponsibleEmployeeId, people), BenchReader.Person(s.RequesterEmployeeId, people), s.TargetYear,
            s.TargetQuarter, s.ComparisonTypeCode, s.PurposeCode, s.Scope, s.Stage, s.Status, s.PendingProcess, s.Returned, s.ReturnComment, s.SearchSubmitted, notice,
            s.SearchNotifiedAt, team, s.CapSourceId, s.PlanApprovedAt, s.AnalysisApprovedAt, s.ClosedAt, s.ImpactDueOn, s.CancelReason,
            topics.Select(x => new BenchTopicDto(x.Id, x.Title, BenchReader.Person(x.OwnerEmployeeId, people), x.Description, x.Sort)).ToList(),
            await PermissionsAsync(s, user, ct).ConfigureAwait(false), s.Version);
    }

    public async Task<BenchStudyPermissionsDto> PermissionsAsync(Study s, ClaimsPrincipal user, CancellationToken ct)
    {
        var office = await reader.CanAsync(user, BenchPermissions.Office, s, ct).ConfigureAwait(false);
        var edit = office || await reader.CanAsync(user, BenchPermissions.Edit, s, ct).ConfigureAwait(false);
        var active = s.IsActive;
        var free = active && s.PendingProcess is null;
        var search = free && s.Stage == StudyStages.Search;
        var analysis = free && s.Stage == StudyStages.Analysis && !s.AnalysisApproved;
        return new BenchStudyPermissionsDto(
            edit && search,
            edit && search,
            edit && active && s.Stage is StudyStages.Acquire or StudyStages.Analysis,
            edit && active && (s.Stage == StudyStages.Acquire || analysis),
            edit && free && s.Stage == StudyStages.Acquire,
            edit && analysis,
            edit && analysis,
            edit && free && s.Stage == StudyStages.Analysis && s.AnalysisApproved,
            office && active,
            edit && active,
            (office || edit) && s.Status == StudyStatuses.Completed,
            office);
    }

    public async Task<IReadOnlyList<BenchStudyListItemDto>> ListItemsAsync(IReadOnlyList<Study> studies, CancellationToken ct)
    {
        var people = await reader.PeopleAsync(studies.Select(s => s.ResponsibleEmployeeId), ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(studies.Select(s => (Guid?)s.ResponsibleUnitId), ct).ConfigureAwait(false);
        return studies.Select(s => new BenchStudyListItemDto(s.Id, s.Number, s.TitleAr, s.TitleEn, BenchReader.Unit(s.ResponsibleUnitId, units),
            BenchReader.Person(s.ResponsibleEmployeeId, people), s.TargetYear, s.TargetQuarter, s.ComparisonTypeCode, s.Stage, s.Status, s.PendingProcess, s.Returned,
            s.AnalysisApproved)).ToList();
    }

    public async Task<BenchPlanDto> PlanAsync(int year, CancellationToken ct)
    {
        var studies = await db.Studies.AsNoTracking().Where(s => s.TargetYear == year && s.PlanApprovedAt != null && s.Status != StudyStatuses.Cancelled)
            .OrderBy(s => s.TargetQuarter).ThenBy(s => s.Number).ToListAsync(ct).ConfigureAwait(false);
        var circulations = await db.Circulations.AsNoTracking().Where(c => c.Year == year).OrderByDescending(c => c.CirculatedAt)
            .Select(c => new BenchCirculationDto(c.Year, c.CirculatedAt, c.Recipients, c.Studies)).ToListAsync(ct).ConfigureAwait(false);
        return new BenchPlanDto(year, await ListItemsAsync(studies, ct).ConfigureAwait(false), circulations,
            await reader.CanAllAsync(context.User, BenchPermissions.Office, ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<BenchCandidateDto>> CandidatesAsync(Guid studyId, CancellationToken ct)
    {
        var rows = await db.StudyEntities.AsNoTracking().Where(e => e.StudyId == studyId && !e.Removed)
            .Join(db.Comparators.AsNoTracking(), e => e.ComparatorId, c => c.Id, (e, c) => new { e, c }).ToListAsync(ct).ConfigureAwait(false);
        var ids = rows.Select(r => r.e.Id).ToList();
        var outreach = await db.Outreach.AsNoTracking().Where(o => ids.Contains(o.StudyEntityId)).OrderBy(o => o.On).ThenBy(o => o.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return rows.OrderByDescending(r => r.e.Selected).ThenBy(r => r.c.NameEn ?? r.c.NameAr).Select(r => new BenchCandidateDto(r.e.Id, r.c.Id, r.c.NameAr, r.c.NameEn, r.c.Country,
            r.c.Scope, r.c.SectorCode, r.e.Selected, r.e.Reason, r.e.AvailableInfo, r.e.Contact, r.e.CodeOfConductAck, r.e.Rating, r.e.RatingNote,
            outreach.Where(o => o.StudyEntityId == r.e.Id).Select(o => new BenchOutreachDto(o.Id, o.On, o.Channel, o.Summary, o.Response, o.CreatedAt)).ToList())).ToList();
    }

    public async Task<IReadOnlyList<BenchVisitDto>> VisitsAsync(Guid studyId, CancellationToken ct)
    {
        var visits = await db.Visits.AsNoTracking().Where(v => v.StudyId == studyId && !v.Removed).OrderBy(v => v.AgreedOn).ToListAsync(ct).ConfigureAwait(false);
        var comparatorIds = visits.Select(v => v.ComparatorId).Distinct().ToList();
        var comparators = await db.Comparators.AsNoTracking().Where(c => comparatorIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(visits.SelectMany(v => v.ParticipantIds).Select(p => (Guid?)p), ct).ConfigureAwait(false);
        return visits.Select(v => new BenchVisitDto(v.Id, v.ComparatorId, comparators.GetValueOrDefault(v.ComparatorId)?.NameAr, comparators.GetValueOrDefault(v.ComparatorId)?.NameEn,
            v.AgreedOn, v.Method, v.ParticipantIds.Select(p => BenchReader.Person(p, people)!).ToList(), v.TeamId, v.Questionnaire, v.QuestionnaireSentOn, v.Notes)).ToList();
    }

    public async Task<BenchAnalysisDto> AnalysisAsync(Guid studyId, CancellationToken ct)
    {
        var practices = await db.Practices.AsNoTracking().Where(p => p.StudyId == studyId && !p.Removed).OrderBy(p => p.Sort).ToListAsync(ct).ConfigureAwait(false);
        var indicators = await db.Indicators.AsNoTracking().Where(p => p.StudyId == studyId && !p.Removed).OrderBy(p => p.Sort).ToListAsync(ct).ConfigureAwait(false);
        var items = await db.PlanItems.AsNoTracking().Where(p => p.StudyId == studyId && !p.Removed).OrderBy(p => p.Sort).ToListAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(items.Select(i => i.OwnerEmployeeId), ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(items.Select(i => i.UnitId), ct).ConfigureAwait(false);
        return new BenchAnalysisDto(
            practices.Select(p => new BenchPracticeDto(p.Id, p.TopicId, p.Subject, p.OurPractice, p.TheirPractice, p.Category, p.Applicable, p.AddToLibrary, p.Sort)).ToList(),
            indicators.Select(i => new BenchIndicatorDto(i.Id, i.KpiCardId, i.KpiCardCode, i.Indicator, i.OurValue, i.TheirValue, i.Period, i.SourceText, i.UpdateKpiBenchmark,
                i.Sort)).ToList(),
            items.Select(i => new BenchPlanItemDto(i.Id, i.TopicId, i.TitleAr, i.TitleEn, i.Description, BenchReader.Person(i.OwnerEmployeeId, people),
                BenchReader.Unit(i.UnitId, units), i.DueOn, i.Priority, i.ActionTypeCode, i.BudgetRequired, i.RemarkId, i.Sort)).ToList());
    }
}

internal sealed class StudyQueries(BenchDbContext db, BenchReader reader, BenchViews views, ILookupService lookups, IRequestContext context, IServiceProvider services)
    : IQueryHandler<ListStudies, PagedResult<BenchStudyListItemDto>>, IQueryHandler<GetStudy, BenchStudyDto>, IQueryHandler<GetStudySearch, IReadOnlyList<BenchCandidateDto>>,
        IQueryHandler<GetStudyVisits, IReadOnlyList<BenchVisitDto>>, IQueryHandler<GetStudyAnalysis, BenchAnalysisDto>, IQueryHandler<GetStudyActions, CapSourceProgress?>,
        IQueryHandler<GetStudyImpact, IReadOnlyList<BenchImpactDto>>, IQueryHandler<GetBenchPlan, BenchPlanDto>, IQueryHandler<GetBenchOptions, BenchOptionsDto>
{
    public async Task<Result<PagedResult<BenchStudyListItemDto>>> HandleAsync(ListStudies query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var source = await reader.StudiesAsync(user, cancellationToken).ConfigureAwait(false);
        var me = user.EmployeeIdOf() ?? Guid.Empty;
        switch (query.View)
        {
            case "mine":
                var topicStudies = db.Topics.Where(t => t.OwnerEmployeeId == me && !t.Removed).Select(t => t.StudyId);
                source = source.Where(s => s.ResponsibleEmployeeId == me || s.RequesterEmployeeId == me || topicStudies.Contains(s.Id));
                break;
            case "my-unit":
                if (await reader.EmployeeAsync(me, cancellationToken).ConfigureAwait(false) is { UnitId: var unit })
                {
                    var units = await reader.UnitAndBelowAsync(unit, cancellationToken).ConfigureAwait(false);
                    source = source.Where(s => units.Contains(s.ResponsibleUnitId));
                }
                else
                {
                    source = source.Where(_ => false);
                }

                break;
            case "pending":
                source = source.Where(s => s.PendingProcess != null && s.Status == StudyStatuses.Active);
                break;
            case "active":
                source = source.Where(s => s.Status == StudyStatuses.Active);
                break;
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like.Pattern(query.Search);
            source = source.Where(s => EF.Functions.ILike(s.Number, pattern, "\\") || EF.Functions.ILike(s.TitleAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(s.TitleEn ?? string.Empty, pattern, "\\"));
        }

        if (query.Year is { } year)
        {
            source = source.Where(s => s.TargetYear == year);
        }

        if (query.Quarter is { } quarter)
        {
            source = source.Where(s => s.TargetQuarter == quarter);
        }

        if (!string.IsNullOrWhiteSpace(query.Stage))
        {
            source = source.Where(s => s.Stage == query.Stage);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(s => s.Status == query.Status);
        }

        if (query.UnitId is { } unitId)
        {
            var units = await reader.UnitAndBelowAsync(unitId, cancellationToken).ConfigureAwait(false);
            source = source.Where(s => units.Contains(s.ResponsibleUnitId));
        }

        var page = query.Page.Normalised();
        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await source.OrderByDescending(s => s.TargetYear).ThenBy(s => s.TargetQuarter).ThenBy(s => s.Number).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<BenchStudyListItemDto>(await views.ListItemsAsync(rows, cancellationToken).ConfigureAwait(false), page.Page, page.PageSize, total);
    }

    public async Task<Result<BenchStudyDto>> HandleAsync(GetStudy query, CancellationToken cancellationToken) =>
        await reader.StudyAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is { } s
            ? await views.ToDtoAsync(s, cancellationToken).ConfigureAwait(false)
            : Error.NotFound();

    public async Task<Result<IReadOnlyList<BenchCandidateDto>>> HandleAsync(GetStudySearch query, CancellationToken cancellationToken) =>
        await reader.StudyAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is not null
            ? Result<IReadOnlyList<BenchCandidateDto>>.Success(await views.CandidatesAsync(query.Id, cancellationToken).ConfigureAwait(false))
            : Error.NotFound();

    public async Task<Result<IReadOnlyList<BenchVisitDto>>> HandleAsync(GetStudyVisits query, CancellationToken cancellationToken) =>
        await reader.StudyAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is not null
            ? Result<IReadOnlyList<BenchVisitDto>>.Success(await views.VisitsAsync(query.Id, cancellationToken).ConfigureAwait(false))
            : Error.NotFound();

    public async Task<Result<BenchAnalysisDto>> HandleAsync(GetStudyAnalysis query, CancellationToken cancellationToken) =>
        await reader.StudyAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is not null
            ? await views.AnalysisAsync(query.Id, cancellationToken).ConfigureAwait(false)
            : Error.NotFound();

    /// <summary>The linked CAP actions, live and read-only (SPEC §8.2.4); null before the analysis is approved or when CAP is off.</summary>
    public async Task<Result<CapSourceProgress?>> HandleAsync(GetStudyActions query, CancellationToken cancellationToken)
    {
        if (await reader.StudyAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        return services.GetService(typeof(ICapRemarkSources)) is ICapRemarkSources cap
            ? await cap.GetProgressAsync(Study.EntityType, query.Id, cancellationToken).ConfigureAwait(false)
            : (CapSourceProgress?)null;
    }

    public async Task<Result<IReadOnlyList<BenchImpactDto>>> HandleAsync(GetStudyImpact query, CancellationToken cancellationToken)
    {
        if (await reader.StudyAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        return await db.ImpactReviews.AsNoTracking().Where(r => r.StudyId == query.Id).OrderByDescending(r => r.ReviewedAt)
            .Select(r => new BenchImpactDto(r.Id, r.DueOn, r.Implemented, r.KpisMoved, r.Notes, r.ReviewedAt)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The annual plan (SPEC §8.2.1): approved studies of the year, circulated to all units — not scope-filtered (bench.plan.read).</summary>
    public async Task<Result<BenchPlanDto>> HandleAsync(GetBenchPlan query, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, BenchPermissions.PlanRead, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        return await views.PlanAsync(query.Year, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<BenchOptionsDto>> HandleAsync(GetBenchOptions query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var lists = new Dictionary<string, IReadOnlyList<BenchOptionDto>>();
        foreach (var type in BenchAccessCatalogue.Lookups)
        {
            var values = await lookups.GetValuesAsync(type.Code, cancellationToken: cancellationToken).ConfigureAwait(false);
            lists[type.Code] = values.Select(v => new BenchOptionDto(v.Code, v.LabelAr, v.LabelEn)).ToList();
        }

        return new BenchOptionsDto(lists, await reader.CanAllAsync(user, BenchPermissions.Office, cancellationToken).ConfigureAwait(false),
            await reader.CanAsync(user, BenchPermissions.DashboardRead, cancellationToken).ConfigureAwait(false),
            await reader.CanAsync(user, BenchPermissions.ComparatorManage, cancellationToken).ConfigureAwait(false),
            await reader.CanAsync(user, BenchPermissions.PracticeManage, cancellationToken).ConfigureAwait(false),
            await reader.CanAsync(user, BenchPermissions.SettingsManage, cancellationToken).ConfigureAwait(false), true, BenchRequestTypes.Request);
    }
}

/// <summary>Stage forms and gates (docs/design/benchmarking.md §2): search, outreach, visits, analysis, closure, cancellation, team, impact, plan circulation.</summary>
internal sealed class StudyCommands(
    BenchDbContext db, BenchReader reader, BenchViews views, BenchRoles roles, BenchNotifier notifier, IApprovalEngine engine,
    IRequestContext context, IClock clock, IOrgDirectory org, IServiceProvider services)
    : ICommandHandler<SaveStudySearch, IReadOnlyList<BenchCandidateDto>>, ICommandHandler<SubmitStudySearch, BenchStudyDto>,
        ICommandHandler<AddStudyOutreach, IReadOnlyList<BenchCandidateDto>>, ICommandHandler<RateStudyComparator, IReadOnlyList<BenchCandidateDto>>,
        ICommandHandler<SaveStudyVisits, IReadOnlyList<BenchVisitDto>>, ICommandHandler<SendStudyToAnalysis, BenchStudyDto>, ICommandHandler<SaveStudyAnalysis, BenchAnalysisDto>,
        ICommandHandler<SubmitStudyAnalysis, BenchStudyDto>, ICommandHandler<RequestStudyClosure, BenchStudyDto>, ICommandHandler<CancelStudy, BenchStudyDto>,
        ICommandHandler<LinkStudyTeam, BenchStudyDto>, ICommandHandler<RecordStudyImpact, IReadOnlyList<BenchImpactDto>>, ICommandHandler<CirculateBenchPlan, BenchPlanDto>
{
    public async Task<Result<IReadOnlyList<BenchCandidateDto>>> HandleAsync(SaveStudySearch command, CancellationToken cancellationToken)
    {
        var (study, can, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return error!;
        }

        if (!can!.EditSearch)
        {
            return Forbidden(study);
        }

        var errors = new List<FieldError>();
        var candidates = command.Candidates ?? [];
        if (candidates.Count > 30)
        {
            errors.Add(new FieldError("candidates", "validation.maximumCount", "validation.maximumCount"));
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c.ComparatorId is null && c.NewComparator is null)
            {
                errors.Add(new FieldError($"candidates[{i}].comparatorId", "validation.required", "validation.required"));
            }

            if (c.NewComparator is { } n)
            {
                errors.AddRange(LibraryRules.Comparator(n).Select(e => e with { Field = $"candidates[{i}].newComparator.{e.Field}" }));
            }

            if (c.ComparatorId is { } id && !await db.Comparators.AnyAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false))
            {
                errors.Add(new FieldError($"candidates[{i}].comparatorId", "validation.invalid", "validation.invalid"));
            }

            if ((c.Reason?.Length ?? 0) > Study.TextMaxLength || (c.AvailableInfo?.Length ?? 0) > Study.TextMaxLength || (c.Contact?.Length ?? 0) > Study.TextMaxLength)
            {
                errors.Add(new FieldError($"candidates[{i}].reason", "validation.maximumLength", "validation.maximumLength"));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.StudyEntities.Where(e => e.StudyId == study.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var kept = new HashSet<Guid>();
        foreach (var c in candidates)
        {
            var comparatorId = c.ComparatorId;
            if (comparatorId is null)
            {
                var created = Comparator.Create(c.NewComparator!);
                db.Comparators.Add(created);
                comparatorId = created.Id;
            }

            var entity = existing.FirstOrDefault(e => e.ComparatorId == comparatorId);
            if (entity is null)
            {
                entity = StudyEntity.Create(study.Id, comparatorId.Value);
                db.StudyEntities.Add(entity);
                existing.Add(entity);
            }

            entity.Update(c.Selected, c.Reason, c.AvailableInfo, c.Contact, c.CodeOfConductAck);
            kept.Add(entity.Id);
        }

        // The search form is a draft until it is approved: removed candidates are deleted (audited).
        foreach (var removed in existing.Where(e => !kept.Contains(e.Id)))
        {
            removed.Remove();
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<BenchCandidateDto>>.Success(await views.CandidatesAsync(study.Id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<BenchStudyDto>> HandleAsync(SubmitStudySearch command, CancellationToken cancellationToken)
    {
        var (study, can, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return error!;
        }

        if (!can!.SubmitSearch)
        {
            return Forbidden(study);
        }

        var selected = await db.StudyEntities.AsNoTracking().Where(e => e.StudyId == study.Id && e.Selected && !e.Removed).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (selected.Count == 0)
        {
            return Error.Rule("bench.noCandidate", "bench.errors.noCandidate");
        }

        if (selected.Any(e => string.IsNullOrWhiteSpace(e.Reason)))
        {
            return Error.Validation([new FieldError("candidates", "bench.errors.reasonRequired", "bench.errors.reasonRequired")]);
        }

        return await SubmitAsync(study, StudyActions.SubmitSearch, BenchProcesses.Search, command.Comment, "search form / نموذج البحث", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<BenchCandidateDto>>> HandleAsync(AddStudyOutreach command, CancellationToken cancellationToken)
    {
        var (study, can, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return error!;
        }

        if (!can!.Outreach)
        {
            return Forbidden(study);
        }

        var input = command.Input;
        var entity = await db.StudyEntities.AsNoTracking().FirstOrDefaultAsync(e => e.Id == input.StudyEntityId && e.StudyId == study.Id && e.Selected && !e.Removed, cancellationToken)
            .ConfigureAwait(false);
        var errors = new List<FieldError>();
        if (entity is null)
        {
            errors.Add(new FieldError("studyEntityId", "validation.invalid", "validation.invalid"));
        }

        if (!OutreachChannels.All.Contains(input.Channel))
        {
            errors.Add(new FieldError("channel", "validation.invalid", "validation.invalid"));
        }

        if (!OutreachChannels.Responses.Contains(input.Response))
        {
            errors.Add(new FieldError("response", "validation.invalid", "validation.invalid"));
        }

        if ((input.Summary?.Length ?? 0) > Study.TextMaxLength)
        {
            errors.Add(new FieldError("summary", "validation.maximumLength", "validation.maximumLength"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Outreach.Add(Outreach.Create(entity!.Id, input.On, input.Channel, input.Summary, input.Response));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<BenchCandidateDto>>.Success(await views.CandidatesAsync(study.Id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<BenchCandidateDto>>> HandleAsync(RateStudyComparator command, CancellationToken cancellationToken)
    {
        var (study, can, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return error!;
        }

        if (!can!.Outreach && !can.Impact)
        {
            return Forbidden(study);
        }

        var entity = await db.StudyEntities.FirstOrDefaultAsync(e => e.Id == command.Input.StudyEntityId && e.StudyId == study.Id, cancellationToken).ConfigureAwait(false);
        if (entity is null || command.Input.Rating is < 1 or > 5)
        {
            return Error.Validation([new FieldError("rating", "validation.invalid", "validation.invalid")]);
        }

        entity.Rate(command.Input.Rating, command.Input.Note);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<BenchCandidateDto>>.Success(await views.CandidatesAsync(study.Id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<BenchVisitDto>>> HandleAsync(SaveStudyVisits command, CancellationToken cancellationToken)
    {
        var (study, can, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return error!;
        }

        if (!can!.EditVisits)
        {
            return Forbidden(study);
        }

        var selected = await db.StudyEntities.AsNoTracking().Where(e => e.StudyId == study.Id && e.Selected && !e.Removed).Select(e => e.ComparatorId).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var errors = new List<FieldError>();
        var visits = command.Visits ?? [];
        for (var i = 0; i < visits.Count; i++)
        {
            var v = visits[i];
            if (!selected.Contains(v.ComparatorId))
            {
                errors.Add(new FieldError($"visits[{i}].comparatorId", "bench.errors.notSelected", "bench.errors.notSelected"));
            }

            if (!VisitMethods.All.Contains(v.Method))
            {
                errors.Add(new FieldError($"visits[{i}].method", "validation.invalid", "validation.invalid"));
            }

            if ((v.Questionnaire?.Length ?? 0) > 20000 || (v.Notes?.Length ?? 0) > 20000)
            {
                errors.Add(new FieldError($"visits[{i}].notes", "validation.maximumLength", "validation.maximumLength"));
            }

            if (v.TeamId is { } teamId && (services.GetService(typeof(ITeamsDirectory)) is not ITeamsDirectory teams
                || await teams.GetTeamAsync(teamId, context.User, cancellationToken).ConfigureAwait(false) is null))
            {
                errors.Add(new FieldError($"visits[{i}].teamId", "validation.invalid", "validation.invalid"));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.Visits.Where(v => v.StudyId == study.Id && !v.Removed).ToListAsync(cancellationToken).ConfigureAwait(false);
        var kept = new HashSet<Guid>();
        foreach (var v in visits)
        {
            var visit = v.Id is { } id ? existing.FirstOrDefault(x => x.Id == id) : null;
            if (visit is null)
            {
                visit = Visit.Create(study.Id);
                db.Visits.Add(visit);
            }

            visit.Update(v.ComparatorId, v.AgreedOn, v.Method, v.ParticipantIds ?? [], v.TeamId, v.Questionnaire, v.QuestionnaireSentOn, v.Notes);
            kept.Add(visit.Id);
        }

        foreach (var removed in existing.Where(x => !kept.Contains(x.Id)))
        {
            removed.Remove();
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await roles.SyncAsync(study, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<BenchVisitDto>>.Success(await views.VisitsAsync(study.Id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<BenchStudyDto>> HandleAsync(SendStudyToAnalysis command, CancellationToken cancellationToken)
    {
        var (study, can, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return error!;
        }

        if (!can!.SendToAnalysis)
        {
            return Forbidden(study);
        }

        if (!await db.Visits.AnyAsync(v => v.StudyId == study.Id && !v.Removed && v.AgreedOn != null, cancellationToken).ConfigureAwait(false))
        {
            return Error.Rule("bench.noVisit", "bench.errors.noVisit");
        }

        var applied = study.Apply(StudyActions.SendToAnalysis, clock.UtcNow);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(study, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<BenchAnalysisDto>> HandleAsync(SaveStudyAnalysis command, CancellationToken cancellationToken)
    {
        var (study, can, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return error!;
        }

        if (!can!.EditAnalysis)
        {
            return Forbidden(study);
        }

        var input = command.Input;
        var topics = await db.Topics.AsNoTracking().Where(t => t.StudyId == study.Id && !t.Removed).Select(t => t.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var errors = new List<FieldError>();
        var kpi = services.GetService(typeof(IKpiCatalog)) as IKpiCatalog;
        var cardCodes = new Dictionary<Guid, string>();
        for (var i = 0; i < (input.Practices ?? []).Count; i++)
        {
            var p = input.Practices![i];
            if (string.IsNullOrWhiteSpace(p.Subject))
            {
                errors.Add(new FieldError($"practices[{i}].subject", "validation.required", "validation.required"));
            }

            if (!PracticeCategories.All.Contains(p.Category) || (p.TopicId is { } t && !topics.Contains(t)))
            {
                errors.Add(new FieldError($"practices[{i}].category", "validation.invalid", "validation.invalid"));
            }

            if ((p.OurPractice?.Length ?? 0) > Study.TextMaxLength || (p.TheirPractice?.Length ?? 0) > Study.TextMaxLength || (p.Subject?.Length ?? 0) > Study.TitleMaxLength)
            {
                errors.Add(new FieldError($"practices[{i}].subject", "validation.maximumLength", "validation.maximumLength"));
            }
        }

        for (var i = 0; i < (input.Indicators ?? []).Count; i++)
        {
            var row = input.Indicators![i];
            if (string.IsNullOrWhiteSpace(row.Indicator) && row.KpiCardId is null)
            {
                errors.Add(new FieldError($"indicators[{i}].indicator", "validation.required", "validation.required"));
            }

            if (row.KpiCardId is { } cardId)
            {
                if (kpi is null || await kpi.GetAsync(cardId, context.User, cancellationToken).ConfigureAwait(false) is not { } card)
                {
                    errors.Add(new FieldError($"indicators[{i}].kpiCardId", "validation.invalid", "validation.invalid"));
                }
                else
                {
                    cardCodes[cardId] = card.Code;
                }
            }
        }

        for (var i = 0; i < (input.PlanItems ?? []).Count; i++)
        {
            var item = input.PlanItems![i];
            if (string.IsNullOrWhiteSpace(item.TitleAr) && string.IsNullOrWhiteSpace(item.TitleEn))
            {
                errors.Add(new FieldError($"planItems[{i}].titleEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
            }

            if (item.Priority is { } pr && !CapPriorities.All.Contains(pr))
            {
                errors.Add(new FieldError($"planItems[{i}].priority", "validation.invalid", "validation.invalid"));
            }

            if (item.ActionTypeCode is { } at && !CapActionTypes.All.Contains(at))
            {
                errors.Add(new FieldError($"planItems[{i}].actionTypeCode", "validation.invalid", "validation.invalid"));
            }

            if (item.TopicId is { } t && !topics.Contains(t))
            {
                errors.Add(new FieldError($"planItems[{i}].topicId", "validation.invalid", "validation.invalid"));
            }

            if (item.OwnerEmployeeId is { } o && await reader.EmployeeAsync(o, cancellationToken).ConfigureAwait(false) is null)
            {
                errors.Add(new FieldError($"planItems[{i}].ownerEmployeeId", "validation.invalid", "validation.invalid"));
            }

            if (item.UnitId is { } u && await reader.UnitAsync(u, cancellationToken).ConfigureAwait(false) is null)
            {
                errors.Add(new FieldError($"planItems[{i}].unitId", "validation.invalid", "validation.invalid"));
            }

            if ((item.Description?.Length ?? 0) > Study.TextMaxLength || (item.TitleAr?.Length ?? 0) > Study.TitleMaxLength || (item.TitleEn?.Length ?? 0) > Study.TitleMaxLength)
            {
                errors.Add(new FieldError($"planItems[{i}].description", "validation.maximumLength", "validation.maximumLength"));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var practices = await db.Practices.Where(p => p.StudyId == study.Id && !p.Removed).ToListAsync(cancellationToken).ConfigureAwait(false);
        var keep = new HashSet<Guid>();
        var sort = 0;
        foreach (var p in input.Practices ?? [])
        {
            var row = p.Id is { } id ? practices.FirstOrDefault(x => x.Id == id) : null;
            if (row is null)
            {
                row = PracticeRow.Create(study.Id);
                db.Practices.Add(row);
            }

            row.Update(p.TopicId, p.Subject!, p.OurPractice, p.TheirPractice, p.Category, p.Applicable, p.AddToLibrary, sort++);
            keep.Add(row.Id);
        }

        foreach (var removed in practices.Where(x => !keep.Contains(x.Id)))
        {
            removed.Remove();
        }

        var indicators = await db.Indicators.Where(p => p.StudyId == study.Id && !p.Removed).ToListAsync(cancellationToken).ConfigureAwait(false);
        keep.Clear();
        sort = 0;
        foreach (var r in input.Indicators ?? [])
        {
            var row = r.Id is { } id ? indicators.FirstOrDefault(x => x.Id == id) : null;
            if (row is null)
            {
                row = IndicatorRow.Create(study.Id);
                db.Indicators.Add(row);
            }

            var code = r.KpiCardId is { } c ? cardCodes.GetValueOrDefault(c) : null;
            row.Update(r.KpiCardId, code, string.IsNullOrWhiteSpace(r.Indicator) ? code ?? string.Empty : r.Indicator, r.OurValue, r.TheirValue, r.Period, r.Source,
                r.UpdateKpiBenchmark, sort++);
            keep.Add(row.Id);
        }

        foreach (var removed in indicators.Where(x => !keep.Contains(x.Id)))
        {
            removed.Remove();
        }

        var items = await db.PlanItems.Where(p => p.StudyId == study.Id && !p.Removed).ToListAsync(cancellationToken).ConfigureAwait(false);
        keep.Clear();
        sort = 0;
        foreach (var i in input.PlanItems ?? [])
        {
            var row = i.Id is { } id ? items.FirstOrDefault(x => x.Id == id) : null;
            if (row is null)
            {
                row = PlanItem.Create(study.Id);
                db.PlanItems.Add(row);
            }

            row.Update(i.TopicId, i.TitleAr, i.TitleEn, i.Description, i.OwnerEmployeeId, i.UnitId, i.DueOn, i.Priority ?? CapPriorities.Medium, i.ActionTypeCode,
                i.BudgetRequired, sort++);
            keep.Add(row.Id);
        }

        foreach (var removed in items.Where(x => !keep.Contains(x.Id)))
        {
            removed.Remove();
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.AnalysisAsync(study.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<BenchStudyDto>> HandleAsync(SubmitStudyAnalysis command, CancellationToken cancellationToken)
    {
        var (study, can, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return error!;
        }

        if (!can!.SubmitAnalysis)
        {
            return Forbidden(study);
        }

        var items = await db.PlanItems.AsNoTracking().Where(p => p.StudyId == study.Id && !p.Removed).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (items.Count == 0)
        {
            return Error.Rule("bench.noPlanItem", "bench.errors.noPlanItem");
        }

        var errors = new List<FieldError>();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].OwnerEmployeeId is null)
            {
                errors.Add(new FieldError($"planItems[{i}].ownerEmployeeId", "validation.required", "validation.required"));
            }

            if (items[i].DueOn is null)
            {
                errors.Add(new FieldError($"planItems[{i}].dueOn", "validation.required", "validation.required"));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        return await SubmitAsync(study, StudyActions.SubmitAnalysis, BenchProcesses.Analysis, command.Comment, "analysis / التحليل", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<BenchStudyDto>> HandleAsync(RequestStudyClosure command, CancellationToken cancellationToken)
    {
        var (study, can, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return error!;
        }

        if (!can!.RequestClosure)
        {
            return Forbidden(study);
        }

        if (!await BenchClosure.ReadyAsync(services, study.Id, cancellationToken).ConfigureAwait(false))
        {
            return Error.Rule("bench.actionsOpen", "bench.errors.actionsOpen");
        }

        return await SubmitAsync(study, StudyActions.RequestClosure, BenchProcesses.Closure, command.Comment, "closure / الإغلاق", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<BenchStudyDto>> HandleAsync(CancelStudy command, CancellationToken cancellationToken)
    {
        var (study, can, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return error!;
        }

        if (!can!.Cancel)
        {
            return Forbidden(study);
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.required", "validation.required")]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (study.PendingProcess is { } process && process != BenchProcesses.Plan)
        {
            await engine.CancelAsync(process, study.Id, "study-cancelled", db, cancellationToken).ConfigureAwait(false);
        }

        study.Cancel(command.Reason);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(study, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<BenchStudyDto>> HandleAsync(LinkStudyTeam command, CancellationToken cancellationToken)
    {
        var (study, can, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return error!;
        }

        if (!can!.LinkTeam)
        {
            return Forbidden(study);
        }

        if (command.TeamId is { } teamId && (services.GetService(typeof(ITeamsDirectory)) is not ITeamsDirectory teams
            || await teams.GetTeamAsync(teamId, context.User, cancellationToken).ConfigureAwait(false) is null))
        {
            return Error.Validation([new FieldError("teamId", "validation.invalid", "validation.invalid")]);
        }

        study.SetTeam(command.TeamId);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(study, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<BenchImpactDto>>> HandleAsync(RecordStudyImpact command, CancellationToken cancellationToken)
    {
        var (study, can, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return error!;
        }

        if (!can!.Impact)
        {
            return study.Status == StudyStatuses.Completed ? Error.Forbidden() : Error.Rule("bench.notCompleted", "bench.errors.notCompleted");
        }

        if ((command.Input.Notes?.Length ?? 0) > Study.TextMaxLength)
        {
            return Error.Validation([new FieldError("notes", "validation.maximumLength", "validation.maximumLength")]);
        }

        db.ImpactReviews.Add(ImpactReview.Create(study.Id, study.ImpactDueOn, command.Input.Implemented, command.Input.KpisMoved, command.Input.Notes, clock.UtcNow,
            context.User.UserId()));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await db.ImpactReviews.AsNoTracking().Where(r => r.StudyId == study.Id).OrderByDescending(r => r.ReviewedAt)
            .Select(r => new BenchImpactDto(r.Id, r.DueOn, r.Implemented, r.KpisMoved, r.Notes, r.ReviewedAt)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Circulates the annual plan to the heads of all units under the organisation and the coordinators (ADR 0127).</summary>
    public async Task<Result<BenchPlanDto>> HandleAsync(CirculateBenchPlan command, CancellationToken cancellationToken)
    {
        if (!await reader.CanAllAsync(context.User, BenchPermissions.Office, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var studies = await db.Studies.AsNoTracking().Where(s => s.TargetYear == command.Year && s.PlanApprovedAt != null && s.Status != StudyStatuses.Cancelled)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (studies.Count == 0)
        {
            return Error.Rule("bench.emptyPlan", "bench.errors.emptyPlan");
        }

        var heads = new HashSet<Guid>();
        foreach (var root in studies.Select(s => s.ResponsibleUnitId).Distinct())
        {
            var path = await reader.PathAsync(root, cancellationToken).ConfigureAwait(false);
            var top = path.Count > 0 ? path[0].Id : root;
            foreach (var unit in await reader.UnitAndBelowAsync(top, cancellationToken).ConfigureAwait(false))
            {
                if (await org.GetHeadOfUnitAsync(unit, null, cancellationToken).ConfigureAwait(false) is { } head)
                {
                    heads.Add(head);
                }
            }
        }

        var recipients = heads.Concat(studies.Where(s => s.ResponsibleEmployeeId is not null).Select(s => s.ResponsibleEmployeeId!.Value)).Distinct().ToList();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.Circulations.Add(PlanCirculation.Create(command.Year, clock.UtcNow, context.User.UserId(), recipients.Count, studies.Count));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var year = command.Year.ToString(CultureInfo.InvariantCulture);
        await notifier.SendAsync(BenchNotifications.PlanCirculated, recipients.Select(r => (Guid?)r), new Dictionary<string, string>
        {
            ["number"] = year, ["titleAr"] = string.Empty, ["titleEn"] = string.Empty, ["date"] = string.Empty, ["actor"] = string.Empty, ["text"] = year,
            ["count"] = studies.Count.ToString(CultureInfo.InvariantCulture),
        }, $"/bench/plan?year={year}", db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.PlanAsync(command.Year, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<BenchStudyDto>> SubmitAsync(Study study, string action, string process, string? comment, string what, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var applied = study.Apply(action, clock.UtcNow);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        study.SetPending(process);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var submitted = await engine.SubmitAsync(new ApprovalSubmission(process,
            new ApprovalSubject(Study.EntityType, study.Id, study.Number, study.TitleAr, study.TitleEn, study.ResponsibleEmployeeId, null, study.ResponsibleUnitId),
            new Dictionary<string, object?> { ["targetYear"] = study.TargetYear, ["comparisonType"] = study.ComparisonTypeCode ?? string.Empty }, comment), db, ct)
            .ConfigureAwait(false);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        var actor = context.User.EmployeeIdOf() is { } me && await reader.EmployeeAsync(me, ct).ConfigureAwait(false) is { } e ? e.NameAr ?? e.NameEn : null;
        await notifier.SendAsync(BenchNotifications.WaitingOffice, [], BenchNotifier.Values(study, actor: actor, text: what), BenchNotifier.Link(study), db, ct,
            [BenchPermissions.OfficeRoleKey]).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await views.ToDtoAsync(study, ct).ConfigureAwait(false);
    }

    private async Task<(Study? Study, BenchStudyPermissionsDto? Can, Error? Error)> LoadAsync(Guid id, CancellationToken ct)
    {
        var study = await reader.StudyAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (study is null)
        {
            return (null, null, Error.NotFound());
        }

        return (study, await views.PermissionsAsync(study, context.User, ct).ConfigureAwait(false), null);
    }

    private static Error Forbidden(Study study) => study.IsActive ? Error.Forbidden() : Error.Rule("bench.notActive", "bench.errors.notActive");
}

/// <summary>Closure rule (ADR 0128): every linked CAP action completed with evidence.</summary>
internal static class BenchClosure
{
    public static async Task<bool> ReadyAsync(IServiceProvider services, Guid studyId, CancellationToken ct) =>
        services.GetService(typeof(ICapRemarkSources)) is ICapRemarkSources cap
        && await cap.GetProgressAsync(Study.EntityType, studyId, ct).ConfigureAwait(false) is { AllActionsCompleted: true };
}

internal static class Like
{
    public static string Pattern(string text) => "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
