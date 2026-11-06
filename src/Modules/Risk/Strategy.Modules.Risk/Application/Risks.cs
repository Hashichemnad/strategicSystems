using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Risk.Application;

public sealed record RiskListItemDto(
    Guid Id, string Number, string? TitleAr, string? TitleEn, string Status, RiskUnitDto? Unit, RiskPersonDto? Owner, IReadOnlyList<string> CategoryCodes,
    decimal InherentScore, string InherentBand, decimal ResidualScore, string ResidualBand, decimal? TargetScore, string? TargetBand, string ResidualStatus,
    bool? WithinAppetite, bool Top10, int? Top10Rank, bool Escalated, DateOnly? NextReviewOn, bool ReviewOverdue, bool ReassessmentRequired, string? PendingProcess,
    bool Emerging, bool Opportunity, int RegisterYear);

public sealed record RiskPermissionsDto(
    bool Edit, bool Submit, bool Recall, bool Office, bool RequestClosure, bool Reopen, bool Discard, bool CommitteeDecide, bool ManageChildren, bool StartReview,
    bool LogEvent);

public sealed record RiskDto(
    Guid Id,
    string Number,
    int RegisterYear,
    RiskValues Values,
    string Status,
    RiskUnitDto? Unit,
    RiskPersonDto? Owner,
    RiskPersonDto? Coordinator,
    RiskPersonDto? FollowUp,
    decimal InherentScore,
    string InherentBand,
    string? ControlEffectiveness,
    string? ControlEffectivenessOverride,
    decimal ResidualScore,
    string ResidualBand,
    string ResidualStatus,
    string? ResidualStatusOverride,
    string? ResidualStatusJustification,
    decimal? TargetScore,
    string? TargetBand,
    bool? WithinAppetite,
    decimal? Appetite,
    bool Top10,
    int? Top10Rank,
    bool Escalated,
    string? EscalationReason,
    DateOnly? LastReviewOn,
    DateOnly? NextReviewOn,
    bool ReviewOverdue,
    bool ReassessmentRequired,
    string? ReassessmentReason,
    string? PendingProcess,
    bool Returned,
    string? ReturnComment,
    string? ClosureReasonCode,
    string? ClosureNote,
    DateTimeOffset? ClosedAt,
    string? RejectionReason,
    int FrameworkVersionNo,
    bool OnCurrentFramework,
    RiskPermissionsDto Can,
    uint Version);

public sealed record RiskAssessmentDto(
    Guid Id, string Kind, int InherentLikelihood, int InherentImpact, decimal InherentScore, string InherentBand, int? ResidualLikelihood, int? ResidualImpact,
    decimal ResidualScore, string ResidualBand, string ResidualStatus, decimal? TargetScore, string? TargetBand, string? ControlEffectiveness, string Status,
    int FrameworkVersionNo, string? Comment, DateTimeOffset At, RiskPersonDto? By);

public sealed record CommitteeDecisionDto(Guid Id, string DecisionCode, string? Note, DateOnly DecidedOn, DateTimeOffset CreatedAt);

public sealed record RiskOptionDto(string Code, string? LabelAr, string? LabelEn);

public sealed record RiskOptionsDto(
    IReadOnlyDictionary<string, IReadOnlyList<RiskOptionDto>> Lookups, IReadOnlyList<RiskCategoryDto> Categories, IReadOnlyList<RiskOptionDto> Objectives,
    FrameworkVersionDto Framework, IReadOnlyList<MatrixCell> Matrix, bool OpportunitiesEnabled, bool CanCreate, bool CanOffice, bool CanManageFramework,
    bool CanReadProjectRisks, bool CanExport, bool CanDashboard, int TopCount, bool IncludeProjectRisksByDefault);

public sealed record OfficeInput(string? ControlEffectivenessOverride, string? ResidualStatusOverride, string? Justification, bool Top10, int? Top10Rank, uint Version);

public sealed record ListRisks(
    PageRequest Page, string? View, string? Search, Guid? UnitId, string? Category, string? Status, string? ResidualBand, string? ResidualStatus, int? Year)
    : IQuery<PagedResult<RiskListItemDto>>;

public sealed record GetRisk(Guid Id) : IQuery<RiskDto>;

public sealed record GetRiskOptions : IQuery<RiskOptionsDto>;

public sealed record ListRiskAssessments(Guid Id) : IQuery<IReadOnlyList<RiskAssessmentDto>>;

public sealed record ListCommitteeDecisions(Guid Id) : IQuery<IReadOnlyList<CommitteeDecisionDto>>;

public sealed record CreateRisk(RiskValues Values) : ICommand<RiskDto>;

public sealed record UpdateRisk(Guid Id, RiskValues Values, uint Version) : ICommand<RiskDto>;

public sealed record SubmitRisk(Guid Id, string? Comment) : ICommand<RiskDto>;

public sealed record RecallRisk(Guid Id) : ICommand<RiskDto>;

public sealed record OfficeUpdateRisk(Guid Id, OfficeInput Input) : ICommand<RiskDto>;

public sealed record RequestRiskClosure(Guid Id, string ReasonCode, string? Note) : ICommand<RiskDto>;

public sealed record ReopenRisk(Guid Id, string Reason) : ICommand<RiskDto>;

public sealed record EscalateRisk(Guid Id, bool Escalated, string? Note) : ICommand<RiskDto>;

public sealed record RecordCommitteeDecision(Guid Id, string DecisionCode, string? Note, DateOnly? DecidedOn) : ICommand<CommitteeDecisionDto>;

public sealed record DiscardRisk(Guid Id) : ICommand<bool>;

/// <summary>Validation of risk values (design §3.1) — keys AR / EN, field paths for the wizard.</summary>
internal sealed class RiskRules(ILookupService lookups, RiskDbContext db, FrameworkStore frameworks)
{
    public async Task<List<FieldError>> ValidateAsync(RiskValues v, RiskSettings settings, CancellationToken ct)
    {
        if (v is null)
        {
            return [new FieldError("values", "validation.required", "validation.required")];
        }

        var errors = new List<FieldError>();
        void Add(string field, string key, Dictionary<string, object?>? p = null) => errors.Add(new FieldError(field, key, key, p));
        if (string.IsNullOrWhiteSpace(v.TitleAr) && string.IsNullOrWhiteSpace(v.TitleEn))
        {
            Add("titleEn", "validation.atLeastOneLanguage");
        }

        foreach (var (field, value, max) in new[]
                 {
                     ("titleAr", v.TitleAr, RiskRecord.TitleMaxLength), ("titleEn", v.TitleEn, RiskRecord.TitleMaxLength), ("cause", v.Cause, RiskRecord.TextMaxLength),
                     ("eventText", v.EventText, RiskRecord.TextMaxLength), ("consequence", v.Consequence, RiskRecord.TextMaxLength),
                     ("processes", v.Processes, RiskRecord.TextMaxLength),
                 })
        {
            if (value is { Length: var l } && l > max)
            {
                Add(field, "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = max });
            }
        }

        foreach (var (field, value) in new[] { ("cause", v.Cause), ("eventText", v.EventText), ("consequence", v.Consequence) })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Add(field, "validation.required");
            }
        }

        if (!RiskTypes.All.Contains(v.RiskType))
        {
            Add("riskType", "validation.invalid");
        }

        if (v.CategoryCodes is not { Count: > 0 })
        {
            Add("categoryCodes", "validation.required");
        }
        else
        {
            var active = await db.Categories.AsNoTracking().Where(c => c.Active).Select(c => c.Code).ToListAsync(ct).ConfigureAwait(false);
            if (v.CategoryCodes.Any(c => !active.Contains(c)))
            {
                Add("categoryCodes", "validation.invalid");
            }
        }

        if (v.UnitId == Guid.Empty)
        {
            Add("unitId", "validation.required");
        }

        if (v.OwnerEmployeeId is null)
        {
            Add("ownerEmployeeId", "validation.required");
        }

        if (v.DeterminedByCode is { } d && !await lookups.IsActiveCodeAsync(RiskPermissions.DeterminedByLookup, d, ct).ConfigureAwait(false))
        {
            Add("determinedByCode", "validation.invalid");
        }

        if (v.ResponseCode is { } r)
        {
            var opportunity = r is "exploit" or "enhance";
            if (!await lookups.IsActiveCodeAsync(RiskPermissions.ResponseLookup, r, ct).ConfigureAwait(false) || (opportunity && !settings.OpportunitiesEnabled))
            {
                Add("responseCode", "validation.invalid");
            }
        }

        if (v.Opportunity && !settings.OpportunitiesEnabled)
        {
            Add("opportunity", "risk.errors.opportunitiesDisabled");
        }

        var scorer = await frameworks.ScorerAsync(ct).ConfigureAwait(false);
        void Rating(string field, int? value, bool likelihood, bool required)
        {
            if (value is null)
            {
                if (required)
                {
                    Add(field, "validation.required");
                }

                return;
            }

            if (likelihood ? !scorer.IsLikelihood(value.Value) : !scorer.IsImpact(value.Value))
            {
                Add(field, "validation.invalid");
            }
        }

        Rating("inherentLikelihood", v.InherentLikelihood, true, true);
        Rating("inherentImpact", v.InherentImpact, false, true);
        Rating("residualLikelihood", v.ResidualLikelihood, true, false);
        Rating("residualImpact", v.ResidualImpact, false, false);
        Rating("targetLikelihood", v.TargetLikelihood, true, false);
        Rating("targetImpact", v.TargetImpact, false, false);
        if ((v.ResidualLikelihood is null) != (v.ResidualImpact is null))
        {
            Add("residualImpact", "risk.errors.pairRequired");
        }

        if ((v.TargetLikelihood is null) != (v.TargetImpact is null))
        {
            Add("targetImpact", "risk.errors.pairRequired");
        }

        if (v.InherentImpactArea is { } area && scorer.Content.ImpactAreas.All(a => a.Code != area))
        {
            Add("inherentImpactArea", "validation.invalid");
        }

        if (v.ExposureStart is { } start && start < v.IdentifiedOn.AddYears(-50))
        {
            Add("exposureStart", "validation.invalid");
        }

        return errors;
    }
}

/// <summary>Loads and maps risks for the API (design §12).</summary>
internal sealed class RiskViews(RiskDbContext db, RiskReader reader, FrameworkStore frameworks, RiskRater rater, IRequestContext context, IClock clock, IApprovalEngine engine)
{
    public async Task<RiskDto> ToDtoAsync(RiskRecord r, CancellationToken ct)
    {
        var user = context.User;
        var people = await reader.PeopleAsync([r.OwnerEmployeeId, r.CoordinatorEmployeeId, r.FollowUpEmployeeId], ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync([r.UnitId], ct).ConfigureAwait(false);
        var current = await frameworks.CurrentAsync(ct).ConfigureAwait(false);
        var (version, _) = await frameworks.ForAsync(r.FrameworkVersionId, ct).ConfigureAwait(false);
        var appetite = await rater.AppetiteAsync(r.CategoryCodes, ct).ConfigureAwait(false);
        var can = await PermissionsAsync(r, user, ct).ConfigureAwait(false);
        return new RiskDto(r.Id, r.Number, r.RegisterYear, r.Values(), r.Status, RiskReader.Unit(r.UnitId, units), RiskReader.Person(r.OwnerEmployeeId, people),
            RiskReader.Person(r.CoordinatorEmployeeId, people), RiskReader.Person(r.FollowUpEmployeeId, people), r.InherentScore, r.InherentBand, r.ControlEffectivenessCode,
            r.ControlEffectivenessOverride, r.ResidualScore, r.ResidualBand, r.ResidualStatus, r.ResidualStatusOverride, r.ResidualStatusJustification, r.TargetScore,
            r.TargetBand, r.WithinAppetite, appetite, r.Top10, r.Top10Rank, r.Escalated, r.EscalationReason, r.LastReviewOn, r.NextReviewOn, ReviewOverdue(r),
            r.ReassessmentRequired, r.ReassessmentReason, r.PendingProcess, r.Returned, r.ReturnComment, r.ClosureReasonCode, r.ClosureNote, r.ClosedAt, r.RejectionReason,
            version.VersionNo, version.Id == current.Id, can, r.Version);
    }

    public bool ReviewOverdue(RiskRecord r) => r.Status == RiskStatuses.Active && r.NextReviewDueAt is { } due && due < clock.UtcNow;

    public async Task<RiskPermissionsDto> PermissionsAsync(RiskRecord r, ClaimsPrincipal user, CancellationToken ct)
    {
        async Task<bool> Can(string permission) => await reader.CanAsync(user, permission, r, ct).ConfigureAwait(false);
        var office = await Can(RiskPermissions.Office).ConfigureAwait(false);
        var edit = await Can(RiskPermissions.Edit).ConfigureAwait(false);
        var submit = await Can(RiskPermissions.Submit).ConfigureAwait(false);
        var editable = RiskCommands.Editable(r, edit, office);
        var pending = r.PendingProcess is not null;
        var discard = r.Status == RiskStatuses.Draft && !r.Returned && edit
            && await engine.GetLatestAsync(RiskProcesses.Review, r.Id, ct).ConfigureAwait(false) is null;
        return new RiskPermissionsDto(
            editable,
            submit && r.Status == RiskStatuses.Draft,
            submit && r.PendingProcess is not null && r.Status is RiskStatuses.UnderReview or RiskStatuses.Active,
            office,
            r.Status == RiskStatuses.Active && !pending && await Can(RiskPermissions.CloseRequest).ConfigureAwait(false),
            office && r.Status is RiskStatuses.Closed or RiskStatuses.Rejected,
            discard,
            r.Status == RiskStatuses.Active && await Can(RiskPermissions.CommitteeDecide).ConfigureAwait(false),
            r.IsOpen && (edit || office),
            r.Status == RiskStatuses.Active && !pending && (submit || office),
            r.Status == RiskStatuses.Active && await Can(RiskPermissions.EventManage).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<RiskListItemDto>> ListItemsAsync(IReadOnlyList<RiskRecord> risks, CancellationToken ct)
    {
        var people = await reader.PeopleAsync(risks.Select(r => r.OwnerEmployeeId), ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(risks.Select(r => (Guid?)r.UnitId), ct).ConfigureAwait(false);
        return risks.Select(r => new RiskListItemDto(r.Id, r.Number, r.TitleAr, r.TitleEn, r.Status, RiskReader.Unit(r.UnitId, units),
            RiskReader.Person(r.OwnerEmployeeId, people), r.CategoryCodes, r.InherentScore, r.InherentBand, r.ResidualScore, r.ResidualBand, r.TargetScore, r.TargetBand,
            r.ResidualStatus, r.WithinAppetite, r.Top10, r.Top10Rank, r.Escalated, r.NextReviewOn, ReviewOverdue(r), r.ReassessmentRequired, r.PendingProcess, r.Emerging,
            r.Opportunity, r.RegisterYear)).ToList();
    }

    public async Task<Dictionary<Guid, RiskPersonDto>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct) => await reader.PeopleAsync(ids, ct).ConfigureAwait(false);

    public IQueryable<Assessment> Assessments(Guid riskId) => db.Assessments.AsNoTracking().Where(a => a.RiskId == riskId);
}

internal sealed class RiskQueries(
    RiskDbContext db, RiskReader reader, RiskViews views, FrameworkStore frameworks, ILookupService lookups, IRequestContext context, IClock clock,
    IServiceProvider services)
    : IQueryHandler<ListRisks, PagedResult<RiskListItemDto>>, IQueryHandler<GetRisk, RiskDto>, IQueryHandler<GetRiskOptions, RiskOptionsDto>,
        IQueryHandler<ListRiskAssessments, IReadOnlyList<RiskAssessmentDto>>, IQueryHandler<ListCommitteeDecisions, IReadOnlyList<CommitteeDecisionDto>>
{
    public static readonly IReadOnlyList<string> Views = ["all", "mine", "my-unit", "top10", "outside-appetite", "overdue-review", "under-review", "escalated", "reassessment"];

    public async Task<Result<PagedResult<RiskListItemDto>>> HandleAsync(ListRisks query, CancellationToken cancellationToken)
    {
        var source = await FilterAsync(query, cancellationToken).ConfigureAwait(false);
        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = query.Page.Normalised();
        source = query.View == "top10" ? source.OrderBy(r => r.Top10Rank ?? int.MaxValue).ThenByDescending(r => r.ResidualScore).ThenBy(r => r.Number)
            : (page.Sort ?? "-residualScore") switch
            {
                "number" => source.OrderBy(r => r.Number),
                "-number" => source.OrderByDescending(r => r.Number),
                "residualScore" => source.OrderBy(r => r.ResidualScore).ThenBy(r => r.Number),
                "inherentScore" => source.OrderBy(r => r.InherentScore).ThenBy(r => r.Number),
                "-inherentScore" => source.OrderByDescending(r => r.InherentScore).ThenBy(r => r.Number),
                "nextReview" => source.OrderBy(r => r.NextReviewOn).ThenBy(r => r.Number),
                _ => source.OrderByDescending(r => r.ResidualScore).ThenByDescending(r => r.InherentScore).ThenBy(r => r.Number),
            };
        var rows = await source.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<RiskListItemDto>(await views.ListItemsAsync(rows, cancellationToken).ConfigureAwait(false), page.Page, page.PageSize, total);
    }

    public async Task<IQueryable<RiskRecord>> FilterAsync(ListRisks query, CancellationToken ct)
    {
        var user = context.User;
        var source = await reader.RisksAsync(user, ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        switch (query.View)
        {
            case "mine":
                var me = user.EmployeeIdOf() ?? Guid.Empty;
                source = source.Where(r => r.OwnerEmployeeId == me || r.CoordinatorEmployeeId == me || r.FollowUpEmployeeId == me);
                break;
            case "my-unit":
                if (user.EmployeeIdOf() is { } employee && await reader.EmployeeAsync(employee, ct).ConfigureAwait(false) is { UnitId: { } unit })
                {
                    var units = await reader.UnitAndBelowAsync(unit, ct).ConfigureAwait(false);
                    source = source.Where(r => units.Contains(r.UnitId));
                }
                else
                {
                    source = source.Where(r => false);
                }

                break;
            case "top10":
                source = source.Where(r => r.Status == RiskStatuses.Active);
                var ranked = await source.OrderByDescending(r => r.Top10).ThenBy(r => r.Top10Rank ?? int.MaxValue).ThenByDescending(r => r.ResidualScore)
                    .ThenByDescending(r => r.InherentScore).ThenBy(r => r.Number).Select(r => r.Id).Take(settings.TopCount).ToListAsync(ct).ConfigureAwait(false);
                source = source.Where(r => ranked.Contains(r.Id));
                break;
            case "outside-appetite":
                source = source.Where(r => r.WithinAppetite == false && r.Status == RiskStatuses.Active);
                break;
            case "overdue-review":
                source = source.Where(r => r.Status == RiskStatuses.Active && r.NextReviewDueAt != null && r.NextReviewDueAt < now);
                break;
            case "under-review":
                source = source.Where(r => r.Status == RiskStatuses.UnderReview || r.PendingProcess != null);
                break;
            case "escalated":
                source = source.Where(r => r.Escalated && r.Status == RiskStatuses.Active);
                break;
            case "reassessment":
                source = source.Where(r => r.ReassessmentRequired && r.Status == RiskStatuses.Active);
                break;
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like.Pattern(query.Search);
            source = source.Where(r => EF.Functions.ILike(r.Number, pattern, "\\") || EF.Functions.ILike(r.TitleAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(r.TitleEn ?? string.Empty, pattern, "\\") || EF.Functions.ILike(r.EventText ?? string.Empty, pattern, "\\"));
        }

        if (query.UnitId is { } unitId)
        {
            var units = await reader.UnitAndBelowAsync(unitId, ct).ConfigureAwait(false);
            source = source.Where(r => units.Contains(r.UnitId));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var json = $"[\"{query.Category.Replace("\"", string.Empty, StringComparison.Ordinal)}\"]";
            source = source.Where(r => EF.Functions.JsonContains(r.CategoryCodesJson, json));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(r => r.Status == query.Status);
        }
        else if (query.View is null or "all" or "mine" or "my-unit")
        {
            source = source.Where(r => r.Status != RiskStatuses.Rejected);
        }

        if (!string.IsNullOrWhiteSpace(query.ResidualBand))
        {
            source = source.Where(r => r.ResidualBand == query.ResidualBand);
        }

        if (!string.IsNullOrWhiteSpace(query.ResidualStatus))
        {
            source = source.Where(r => r.ResidualStatus == query.ResidualStatus);
        }

        if (query.Year is { } year)
        {
            source = source.Where(r => r.RegisterYear == year);
        }

        return source;
    }

    public async Task<Result<RiskDto>> HandleAsync(GetRisk query, CancellationToken cancellationToken) =>
        await reader.RiskAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is { } risk
            ? await views.ToDtoAsync(risk, cancellationToken).ConfigureAwait(false)
            : Error.NotFound();

    public async Task<Result<RiskOptionsDto>> HandleAsync(GetRiskOptions query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var lists = new Dictionary<string, IReadOnlyList<RiskOptionDto>>();
        foreach (var type in RiskAccessCatalogue.Lookups)
        {
            var values = await lookups.GetValuesAsync(type.Code, cancellationToken: cancellationToken).ConfigureAwait(false);
            lists[type.Code] = values.Where(v => settings.OpportunitiesEnabled || type.Code != RiskPermissions.ResponseLookup || v.Code is not ("exploit" or "enhance"))
                .Select(v => new RiskOptionDto(v.Code, v.LabelAr, v.LabelEn)).ToList();
        }

        var categories = await db.Categories.AsNoTracking().Where(c => c.Active).OrderBy(c => c.Sort).ThenBy(c => c.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RiskOptionDto> objectives = [];
        if (services.GetService(typeof(Kpi.Contracts.IKpiStrategyCatalog)) is Kpi.Contracts.IKpiStrategyCatalog strategy)
        {
            objectives = (await strategy.ListObjectivesAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
                .Select(o => new RiskOptionDto(o.Code, o.NameAr, o.NameEn)).ToList();
        }

        var framework = await frameworks.CurrentAsync(cancellationToken).ConfigureAwait(false);
        var scorer = await frameworks.ScorerAsync(cancellationToken).ConfigureAwait(false);
        async Task<bool> Can(string permission) => await reader.CanAsync(user, permission, cancellationToken).ConfigureAwait(false);
        return new RiskOptionsDto(lists, categories.Select(FrameworkQueries.ToDto).ToList(), objectives, FrameworkQueries.ToDto(framework), scorer.Matrix(),
            settings.OpportunitiesEnabled, await Can(RiskPermissions.Create).ConfigureAwait(false), await Can(RiskPermissions.Office).ConfigureAwait(false),
            await Can(RiskPermissions.FrameworkManage).ConfigureAwait(false), await Can(RiskPermissions.ProjectRisksRead).ConfigureAwait(false),
            await Can(RiskPermissions.Export).ConfigureAwait(false), await Can(RiskPermissions.DashboardRead).ConfigureAwait(false), settings.TopCount,
            settings.IncludeProjectRisksByDefault);
    }

    public async Task<Result<IReadOnlyList<RiskAssessmentDto>>> HandleAsync(ListRiskAssessments query, CancellationToken cancellationToken)
    {
        if (await reader.RiskAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        var rows = await views.Assessments(query.Id).OrderBy(a => a.At).ToListAsync(cancellationToken).ConfigureAwait(false);
        var users = rows.Select(a => a.By).Where(b => b is not null).Distinct().ToList();
        var byUser = new Dictionary<Guid, RiskPersonDto>();
        if (users.Count > 0 && services.GetService(typeof(IUserDirectory)) is IUserDirectory directory)
        {
            var refs = await directory.GetManyAsync(users.Select(u => u!.Value).ToList(), cancellationToken).ConfigureAwait(false);
            var people = await views.PeopleAsync(refs.Values.Select(u => u.EmployeeId), cancellationToken).ConfigureAwait(false);
            foreach (var (id, u) in refs)
            {
                byUser[id] = u.EmployeeId is { } e && people.GetValueOrDefault(e) is { } p ? p with { Id = id } : new RiskPersonDto(id, u.UserName, u.UserName);
            }
        }

        return rows.Select(a => new RiskAssessmentDto(a.Id, a.Kind, a.InherentLikelihood, a.InherentImpact, a.InherentScore, a.InherentBand, a.ResidualLikelihood,
            a.ResidualImpact, a.ResidualScore, a.ResidualBand, a.ResidualStatus, a.TargetScore, a.TargetBand, a.ControlEffectiveness, a.Status, a.FrameworkVersionNo,
            a.Comment, a.At, a.By is { } b ? byUser.GetValueOrDefault(b) : null)).ToList();
    }

    public async Task<Result<IReadOnlyList<CommitteeDecisionDto>>> HandleAsync(ListCommitteeDecisions query, CancellationToken cancellationToken)
    {
        if (await reader.RiskAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        return await db.Decisions.AsNoTracking().Where(d => d.RiskId == query.Id).OrderByDescending(d => d.DecidedOn)
            .Select(d => new CommitteeDecisionDto(d.Id, d.DecisionCode, d.Note, d.DecidedOn, d.CreatedAt)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>R1 / R4 / R5 commands (design §5). Approval decisions themselves go through the workflow API; hooks move the risk.</summary>
internal sealed class RiskCommands(
    RiskDbContext db, RiskReader reader, RiskRules rules, RiskRater rater, RiskViews views, FrameworkStore frameworks, RiskRoleSync roles, INumberingService numbering,
    IApprovalEngine engine, ILookupService lookups, RiskNotifier notifier, IRequestContext context, IClock clock)
    : ICommandHandler<CreateRisk, RiskDto>, ICommandHandler<UpdateRisk, RiskDto>, ICommandHandler<SubmitRisk, RiskDto>, ICommandHandler<RecallRisk, RiskDto>,
        ICommandHandler<OfficeUpdateRisk, RiskDto>, ICommandHandler<RequestRiskClosure, RiskDto>, ICommandHandler<ReopenRisk, RiskDto>,
        ICommandHandler<EscalateRisk, RiskDto>, ICommandHandler<RecordCommitteeDecision, CommitteeDecisionDto>, ICommandHandler<DiscardRisk, bool>
{
    /// <summary>Draft: editors; under review: the Risk Office (it adjusts before approving); active: editors and the Risk Office.</summary>
    public static bool Editable(RiskRecord r, bool edit, bool office) => r.Status switch
    {
        RiskStatuses.Draft => edit || office,
        RiskStatuses.UnderReview => office,
        RiskStatuses.Active => (edit && r.PendingProcess is null) || office,
        _ => false,
    };

    public async Task<Result<RiskDto>> HandleAsync(CreateRisk command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var v = command.Values;
        if (!await reader.CanInUnitAsync(user, RiskPermissions.Create, v.UnitId, v.OwnerEmployeeId, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var errors = await rules.ValidateAsync(v, settings, cancellationToken).ConfigureAwait(false);
        var unit = v.UnitId == Guid.Empty ? null : await reader.UnitAsync(v.UnitId, cancellationToken).ConfigureAwait(false);
        if (unit is null && v.UnitId != Guid.Empty)
        {
            errors.Add(new FieldError("unitId", "validation.invalid", "validation.invalid"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, RiskPermissions.RiskNumbering, new NumberingContext(v.IdentifiedOn, unit!.Code), cancellationToken).ConfigureAwait(false);
        var risk = RiskRecord.Create(number, v.IdentifiedOn.Year, v);
        db.Risks.Add(risk);
        await rater.RateAsync(risk, settings, clock.UtcNow, cancellationToken, moveToCurrent: true, controlRatings: []).ConfigureAwait(false);
        var current = await frameworks.CurrentAsync(cancellationToken).ConfigureAwait(false);
        db.Assessments.Add(Assessment.Of(risk, AssessmentKinds.Registration, current.VersionNo, null, clock.UtcNow, user.UserId()));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(risk, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RiskDto>> HandleAsync(UpdateRisk command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var risk = await reader.RiskAsync(command.Id, user, cancellationToken, tracked: true).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        var office = await reader.CanAsync(user, RiskPermissions.Office, risk, cancellationToken).ConfigureAwait(false);
        if (!Editable(risk, await reader.CanAsync(user, RiskPermissions.Edit, risk, cancellationToken).ConfigureAwait(false), office))
        {
            return risk.IsOpen ? Error.Forbidden() : Error.Rule("risk.notEditable", "risk.errors.notEditable");
        }

        var v = command.Values;
        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var errors = await rules.ValidateAsync(v, settings, cancellationToken).ConfigureAwait(false);
        if (v.UnitId != risk.UnitId)
        {
            // Moving a risk to another register: the user must be able to create risks there, and the number stays (it is an identifier).
            if (!await reader.CanInUnitAsync(user, RiskPermissions.Create, v.UnitId, v.OwnerEmployeeId, cancellationToken).ConfigureAwait(false))
            {
                errors.Add(new FieldError("unitId", "risk.errors.unitNotAllowed", "risk.errors.unitNotAllowed"));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(risk).Property(r => r.Version).OriginalValue = command.Version;
        var before = (risk.InherentBand, risk.ResidualBand, risk.TargetBand);
        risk.Edit(v);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await rater.RateAsync(risk, settings, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (risk.Status == RiskStatuses.Active && before != (risk.InherentBand, risk.ResidualBand, risk.TargetBand))
        {
            var (version, _) = await frameworks.ForAsync(risk.FrameworkVersionId, cancellationToken).ConfigureAwait(false);
            db.Assessments.Add(Assessment.Of(risk, AssessmentKinds.Reassessment, version.VersionNo, null, clock.UtcNow, user.UserId()));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(risk, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RiskDto>> HandleAsync(SubmitRisk command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var risk = await reader.RiskAsync(command.Id, user, cancellationToken, tracked: true).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(user, RiskPermissions.Submit, risk, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var errors = await rules.ValidateAsync(risk.Values(), settings, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var applied = risk.Apply(RiskActions.Submit, clock.UtcNow, user.UserId());
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        risk.SetPending(RiskProcesses.Review);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var submitted = await engine.SubmitAsync(Submission(RiskProcesses.Review, risk, command.Comment), db, cancellationToken).ConfigureAwait(false);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(risk, cancellationToken).ConfigureAwait(false);
    }

    public static ApprovalSubmission Submission(string process, RiskRecord risk, string? comment) => new(
        process,
        new ApprovalSubject(RiskRecord.EntityType, risk.Id, risk.Number, risk.TitleAr, risk.TitleEn, risk.OwnerEmployeeId, null, risk.UnitId),
        new Dictionary<string, object?>
        {
            ["inherentBand"] = risk.InherentBand,
            ["residualBand"] = risk.ResidualBand,
            ["residualScore"] = risk.ResidualScore,
            ["withinAppetite"] = risk.WithinAppetite ?? true,
            ["top10"] = risk.Top10,
        },
        comment);

    public async Task<Result<RiskDto>> HandleAsync(RecallRisk command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var risk = await reader.RiskAsync(command.Id, user, cancellationToken, tracked: true).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(user, RiskPermissions.Submit, risk, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (risk.PendingProcess is not { } process)
        {
            return Error.Rule("risk.nothingPending", "risk.errors.nothingPending");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var cancelled = await engine.CancelAsync(process, risk.Id, "recalled", db, cancellationToken).ConfigureAwait(false);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await db.Entry(risk).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(risk, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RiskDto>> HandleAsync(OfficeUpdateRisk command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var risk = await reader.RiskAsync(command.Id, user, cancellationToken, tracked: true).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(user, RiskPermissions.Office, risk, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var input = command.Input;
        var scorer = await frameworks.ScorerAsync(cancellationToken).ConfigureAwait(false);
        var errors = new List<FieldError>();
        if (input.ControlEffectivenessOverride is { } c && scorer.Content.ControlEffectiveness.All(e => e.Code != c))
        {
            errors.Add(new FieldError("controlEffectivenessOverride", "validation.invalid", "validation.invalid"));
        }

        if (input.ResidualStatusOverride is { } s)
        {
            if (!ResidualStatuses.All.Contains(s))
            {
                errors.Add(new FieldError("residualStatusOverride", "validation.invalid", "validation.invalid"));
            }

            if (string.IsNullOrWhiteSpace(input.Justification))
            {
                errors.Add(new FieldError("justification", "validation.required", "validation.required"));
            }
        }

        if (input.Top10Rank is < 1 or > 100)
        {
            errors.Add(new FieldError("top10Rank", "validation.invalid", "validation.invalid"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(risk).Property(r => r.Version).OriginalValue = input.Version;
        risk.OfficeUpdate(input.ControlEffectivenessOverride, input.ResidualStatusOverride, input.Justification, input.Top10, input.Top10Rank);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await rater.RateAsync(risk, await reader.SettingsAsync(cancellationToken).ConfigureAwait(false), clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(risk, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RiskDto>> HandleAsync(RequestRiskClosure command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var risk = await reader.RiskAsync(command.Id, user, cancellationToken, tracked: true).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(user, RiskPermissions.CloseRequest, risk, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!await lookups.IsActiveCodeAsync(RiskPermissions.ClosureReasonLookup, command.ReasonCode, cancellationToken).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("reasonCode", "validation.invalid", "validation.invalid")]);
        }

        if (risk.PendingProcess is not null)
        {
            return Error.Rule("risk.alreadyPending", "risk.errors.alreadyPending");
        }

        var applied = risk.Apply(RiskActions.RequestClosure, clock.UtcNow, user.UserId(), command.Note, command.ReasonCode);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        risk.SetPending(RiskProcesses.Closure);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var submitted = await engine.SubmitAsync(Submission(RiskProcesses.Closure, risk, command.Note), db, cancellationToken).ConfigureAwait(false);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(risk, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RiskDto>> HandleAsync(ReopenRisk command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var risk = await reader.RiskAsync(command.Id, user, cancellationToken, tracked: true).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(user, RiskPermissions.Office, risk, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.required", "validation.required")]);
        }

        var wasClosed = risk.Status == RiskStatuses.Closed;
        var applied = risk.Apply(RiskActions.Reopen, clock.UtcNow, user.UserId(), command.Reason);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        await rater.RateAsync(risk, settings, clock.UtcNow, cancellationToken, moveToCurrent: true).ConfigureAwait(false);
        var current = await frameworks.CurrentAsync(cancellationToken).ConfigureAwait(false);
        if (wasClosed)
        {
            risk.FlagReassessment(ReassessmentReasons.Framework);
        }

        db.Assessments.Add(Assessment.Of(risk, AssessmentKinds.Reopen, current.VersionNo, command.Reason.Trim(), clock.UtcNow, user.UserId()));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(risk, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RiskDto>> HandleAsync(EscalateRisk command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var risk = await reader.RiskAsync(command.Id, user, cancellationToken, tracked: true).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(user, RiskPermissions.Office, risk, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (risk.Status != RiskStatuses.Active)
        {
            return Error.Rule("risk.notActive", "risk.errors.notActive");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (command.Escalated)
        {
            risk.Escalate(EscalationReasons.Manual, clock.UtcNow);
            await notifier.SendAsync(RiskNotifications.Escalated, [risk.OwnerEmployeeId], RiskNotifier.Values(risk, text: command.Note), $"/risk/risks/{risk.Id:D}", db,
                cancellationToken, [RiskPermissions.RiskCommitteeRoleKey]).ConfigureAwait(false);
        }
        else
        {
            risk.ClearEscalation();
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(risk, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<CommitteeDecisionDto>> HandleAsync(RecordCommitteeDecision command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var risk = await reader.RiskAsync(command.Id, user, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(user, RiskPermissions.CommitteeDecide, risk, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!await lookups.IsActiveCodeAsync(RiskPermissions.CommitteeDecisionLookup, command.DecisionCode, cancellationToken).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("decisionCode", "validation.invalid", "validation.invalid")]);
        }

        var decision = CommitteeDecision.Create(risk.Id, command.DecisionCode, command.Note?.Trim(), command.DecidedOn ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));
        db.Decisions.Add(decision);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CommitteeDecisionDto(decision.Id, decision.DecisionCode, decision.Note, decision.DecidedOn, decision.CreatedAt);
    }

    public async Task<Result<bool>> HandleAsync(DiscardRisk command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var risk = await reader.RiskAsync(command.Id, user, cancellationToken, tracked: true).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        if (!(await views.PermissionsAsync(risk, user, cancellationToken).ConfigureAwait(false)).Discard)
        {
            return risk.Status == RiskStatuses.Draft ? Error.Forbidden() : Error.Rule("risk.notDiscardable", "risk.errors.notDiscardable");
        }

        // Hard delete of a draft never submitted (CLAUDE.md §6): an explicit, audited action; children go with it.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.Controls.RemoveRange(await db.Controls.Where(c => c.RiskId == risk.Id).ToListAsync(cancellationToken).ConfigureAwait(false));
        var actions = await db.Actions.Where(a => a.RiskId == risk.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var actionIds = actions.Select(a => a.Id).ToList();
        db.ActionUpdates.RemoveRange(await db.ActionUpdates.Where(u => actionIds.Contains(u.ActionId)).ToListAsync(cancellationToken).ConfigureAwait(false));
        db.Actions.RemoveRange(actions);
        db.Kris.RemoveRange(await db.Kris.Where(k => k.RiskId == risk.Id).ToListAsync(cancellationToken).ConfigureAwait(false));
        db.Assessments.RemoveRange(await db.Assessments.Where(a => a.RiskId == risk.Id).ToListAsync(cancellationToken).ConfigureAwait(false));
        db.EventRisks.RemoveRange(await db.EventRisks.Where(e => e.RiskId == risk.Id).ToListAsync(cancellationToken).ConfigureAwait(false));
        db.Risks.Remove(risk);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await roles.RevokeAllAsync(risk.Id, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}

/// <summary>Keeps the action-owner / control-owner record roles on a risk in line with its children (CLAUDE.md §7: granted by the module).</summary>
internal sealed class RiskRoleSync(RiskDbContext db, IRecordRoleService recordRoles)
{
    public async Task SyncAsync(Guid riskId, CancellationToken ct)
    {
        var actionOwners = await db.Actions.AsNoTracking().Where(a => a.RiskId == riskId && a.Status != ActionStatuses.Cancelled && a.OwnerEmployeeId != null)
            .Select(a => a.OwnerEmployeeId!.Value).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var controlOwners = await db.Controls.AsNoTracking().Where(c => c.RiskId == riskId && c.Status == ControlStatuses.Active && c.OwnerEmployeeId != null)
            .Select(c => c.OwnerEmployeeId!.Value).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var holders = await recordRoles.GetHoldersAsync(RiskRecord.EntityType, riskId, ct).ConfigureAwait(false);
        await SyncRoleAsync(riskId, RiskPermissions.ActionOwnerRole, actionOwners, holders, ct).ConfigureAwait(false);
        await SyncRoleAsync(riskId, RiskPermissions.ControlOwnerRole, controlOwners, holders, ct).ConfigureAwait(false);
    }

    public async Task RevokeAllAsync(Guid riskId, CancellationToken ct)
    {
        foreach (var holder in await recordRoles.GetHoldersAsync(RiskRecord.EntityType, riskId, ct).ConfigureAwait(false))
        {
            if (holder.RoleCode is RiskPermissions.ActionOwnerRole or RiskPermissions.ControlOwnerRole)
            {
                await recordRoles.RevokeInTransactionAsync(db, RiskRecord.EntityType, riskId, holder.PrincipalKind, holder.PrincipalId, holder.RoleCode, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task SyncRoleAsync(Guid riskId, string role, IReadOnlyList<Guid> wanted, IReadOnlyList<RecordRoleHolder> holders, CancellationToken ct)
    {
        var current = holders.Where(h => h.RoleCode == role && h.PrincipalKind == RecordPrincipalKinds.Employee).Select(h => h.PrincipalId).ToHashSet();
        foreach (var employee in wanted.Where(e => !current.Contains(e)))
        {
            await recordRoles.GrantInTransactionAsync(db, RiskRecord.EntityType, riskId, RecordPrincipalKinds.Employee, employee, role, ct).ConfigureAwait(false);
        }

        foreach (var employee in current.Where(e => !wanted.Contains(e)))
        {
            await recordRoles.RevokeInTransactionAsync(db, RiskRecord.EntityType, riskId, RecordPrincipalKinds.Employee, employee, role, ct).ConfigureAwait(false);
        }
    }
}

internal static class Like
{
    public static string Pattern(string text) => "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
