using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio.Application;

public sealed record ProjectRefDto(Guid Id, string Number, string Classification);

/// <summary>What the current user may do on the request now (drives the record page; the server checks again on every call).</summary>
public sealed record RequestCapabilities(bool Edit, bool Submit, bool Cancel, bool Manage, bool Quotations, bool SetApprovedBudget, bool Assess);

public sealed record RequestDto(
    Guid Id,
    string Number,
    string Status,
    string? ApprovalStatus,
    string? ApprovalStepCode,
    string? ApprovalStepNameAr,
    string? ApprovalStepNameEn,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? LastActionAt,
    string? CancelReason,
    DateTimeOffset? CancelledAt,
    PersonDto? Requester,
    string Source,
    RequestValues Values,
    UnitDto? Unit,
    UnitDto? SectionUnit,
    string Classification,
    string? ClassificationOverride,
    string? OverrideReason,
    string EffectiveClassification,
    decimal? EvaluationScore,
    string? PriorityBand,
    decimal? YearCostsTotal,
    int? TimeFrameMonths,
    string? ProcurementStatus,
    bool CostEstimationUploaded,
    bool ProjectScopeUploaded,
    DateTimeOffset? QuotationsHandedOffAt,
    DateTimeOffset? QuotationsUploadedAt,
    decimal? ApprovedBudget,
    ProjectRefDto? Project,
    int AssessmentCount,
    RequestCapabilities Can,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version);

public sealed record RequestListItemDto(
    Guid Id,
    string Number,
    string? NameAr,
    string? NameEn,
    string Status,
    string? ApprovalStatus,
    string? ApprovalStepNameAr,
    string? ApprovalStepNameEn,
    string EffectiveClassification,
    decimal? EvaluationScore,
    string? PriorityBand,
    bool? RequiresBudget,
    decimal? EstimatedBudget,
    UnitDto? Unit,
    PersonDto? Requester,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset UpdatedAt,
    string? ProjectNumber);

public sealed record CreateRequest(RequestValues Values, string? Source = null) : ICommand<RequestDto>;

public sealed record UpdateRequest(Guid Id, RequestValues Values, uint Version) : ICommand<RequestDto>;

public sealed record SubmitRequest(Guid Id, uint Version, string? Comment) : ICommand<RequestDto>;

public sealed record CancelRequest(Guid Id, string? Reason, uint Version) : ICommand<RequestDto>;

public sealed record OverrideClassification(Guid Id, string? Value, string? Reason, uint Version) : ICommand<RequestDto>;

public sealed record SetApprovedBudget(Guid Id, decimal Amount, uint Version) : ICommand<RequestDto>;

public sealed record GetRequest(Guid Id) : IQuery<RequestDto>;

public sealed record ListRequests(
    PageRequest Page, string? View = null, string? Status = null, string? Classification = null, string? PriorityBand = null, Guid? UnitId = null,
    string? Search = null) : IQuery<PagedResult<RequestListItemDto>>;

public static class RequestViews
{
    public const string All = "all";
    public const string Mine = "mine";
    public const string InApproval = "in-approval";
    public const string Returned = "returned";
}

/// <summary>Builds <see cref="RequestDto"/> with names, units, the project and the caller's capabilities.</summary>
internal sealed class RequestQueries(PortfolioDbContext db, RequestReader reader, IRequestContext context, IApprovalEngine engine)
{
    public async Task<Result<RequestDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var request = await reader.FindAsync(id, context.User, ct).ConfigureAwait(false);
        return request is null ? Error.NotFound() : await ToDtoAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<RequestDto> ToDtoAsync(ProgramRequest r, CancellationToken ct)
    {
        var user = context.User;
        var people = await reader.PeopleAsync([r.RequesterEmployeeId], ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync([r.UnitId, r.SectionUnitId], ct).ConfigureAwait(false);
        var project = r.ProjectId is { } projectId
            ? await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => new ProjectRefDto(p.Id, p.Number, p.Classification)).FirstOrDefaultAsync(ct)
                .ConfigureAwait(false)
            : null;
        var assessments = await db.Assessments.CountAsync(a => a.RequestId == r.Id, ct).ConfigureAwait(false);
        var draft = r.Status == RequestStatuses.Draft;
        var edit = draft && await reader.CanAsync(user, PortfolioPermissions.RequestEdit, r, ct).ConfigureAwait(false);
        var manage = !RequestStatuses.IsFinal(r.Status) && await reader.CanAsync(user, PortfolioPermissions.RequestManage, r, ct).ConfigureAwait(false);
        var quotations = draft && r.ProcurementStatus == ProcurementStatuses.WaitingQuotations
            && await reader.CanAsync(user, PortfolioPermissions.RequestQuotations, r, ct).ConfigureAwait(false);
        var setBudget = r.Status == RequestStatuses.InApproval && await IsBudgetApproverAsync(r, ct).ConfigureAwait(false);
        var values = r.ToValues();
        return new RequestDto(
            r.Id, r.Number, r.Status, r.ApprovalStatus, r.ApprovalStepCode, r.ApprovalStepNameAr, r.ApprovalStepNameEn, r.SubmittedAt, r.LastActionAt, r.CancelReason,
            r.CancelledAt, r.RequesterEmployeeId is { } e ? people.GetValueOrDefault(e) : null, r.Source, values,
            r.UnitId is { } u ? units.GetValueOrDefault(u) : null, r.SectionUnitId is { } s ? units.GetValueOrDefault(s) : null,
            r.Classification, r.ClassificationOverride, r.OverrideReason, r.EffectiveClassification, r.EvaluationScore, r.PriorityBand, values.YearCostsTotal,
            RequestRules.TimeFrameMonths(r.ExpectedStartDate, r.ExpectedEndDate), r.ProcurementStatus, r.CostEstimationUploaded, r.ProjectScopeUploaded,
            r.QuotationsHandedOffAt, r.QuotationsUploadedAt, r.ApprovedBudget, project, assessments,
            new RequestCapabilities(edit, edit, draft ? edit || manage : manage, manage, quotations, setBudget, edit || manage),
            r.CreatedAt, r.UpdatedAt, r.Version);
    }

    /// <summary>True when the caller (or a delegate) is an approver of the step in progress and that step is the budget step (ADR 0048).</summary>
    public async Task<bool> IsBudgetApproverAsync(ProgramRequest r, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        if (r.ApprovalStepCode != settings.BudgetStepCode)
        {
            return false;
        }

        var view = await engine.GetStatusViewAsync(RequestApprovalProcess.Key, r.Id, context.User, ct).ConfigureAwait(false);
        return view?.MyTaskId is not null && view.Instance.CurrentStepCode == settings.BudgetStepCode;
    }

    public async Task<IReadOnlyList<RequestListItemDto>> ItemsAsync(IReadOnlyList<ProgramRequest> requests, CancellationToken ct)
    {
        var people = await reader.PeopleAsync(requests.Select(r => r.RequesterEmployeeId), ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(requests.Select(r => r.UnitId), ct).ConfigureAwait(false);
        var projectIds = requests.Where(r => r.ProjectId != null).Select(r => r.ProjectId!.Value).ToList();
        var projects = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, ct).ConfigureAwait(false);
        return requests.Select(r => new RequestListItemDto(
            r.Id, r.Number, r.NameAr, r.NameEn, r.Status, r.ApprovalStatus, r.ApprovalStepNameAr, r.ApprovalStepNameEn, r.EffectiveClassification, r.EvaluationScore,
            r.PriorityBand, r.RequiresBudget, r.EstimatedBudget, r.UnitId is { } u ? units.GetValueOrDefault(u) : null,
            r.RequesterEmployeeId is { } e ? people.GetValueOrDefault(e) : null, r.SubmittedAt, r.UpdatedAt,
            r.ProjectId is { } p ? projects.GetValueOrDefault(p) : null)).ToList();
    }
}

/// <summary>Checks lookup codes against active values and computes classification / priority (every save).</summary>
internal sealed class RequestEvaluator(RequestReader reader, ILookupService lookups)
{
    public async Task<List<Platform.Kernel.Results.FieldError>> LookupErrorsAsync(RequestValues v, CancellationToken ct)
    {
        var errors = new List<Platform.Kernel.Results.FieldError>();
        foreach (var (field, type, code) in new[]
                 {
                     ("strategicObjectiveCode", PortfolioPermissions.ObjectiveLookup, v.StrategicObjectiveCode),
                     ("suggestionSourceCode", PortfolioPermissions.SourceLookup, v.SuggestionSourceCode),
                     ("programTypeCode", PortfolioPermissions.ProgramTypeLookup, v.ProgramTypeCode),
                     ("mainOptionCode", PortfolioPermissions.DeliveryOptionLookup, v.MainOptionCode),
                     ("altOptionCode", PortfolioPermissions.DeliveryOptionLookup, v.AltOptionCode),
                 })
        {
            if (!string.IsNullOrWhiteSpace(code) && !await lookups.IsActiveCodeAsync(type, code.Trim(), ct).ConfigureAwait(false))
            {
                errors.Add(new Platform.Kernel.Results.FieldError(field, "validation.unknownCode", "validation.unknownCode"));
            }
        }

        return errors;
    }

    public async Task<(string Classification, decimal? Score, string? Band)> ComputeAsync(RequestValues v, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var score = PriorityCalculator.Score(v.PriorityScores, settings.ActiveCriteria);
        return (ClassificationCalculator.Classify(v, settings.ThresholdAmount), score, PriorityCalculator.Band(score, settings.PriorityBands)?.Code);
    }
}

/// <summary>Create / edit drafts, submit, cancel, classification override and the approved budget (docs/design/portfolio.md §5–§6).</summary>
internal sealed class RequestHandlers(
    PortfolioDbContext db,
    RequestReader reader,
    RequestQueries queries,
    RequestEvaluator evaluator,
    RequestFileFlags flags,
    PortfolioNotifier notifier,
    IRecordRoleService recordRoles,
    IRequestContext context,
    INumberingService numbering,
    IApprovalEngine engine,
    IClock clock)
    : ICommandHandler<CreateRequest, RequestDto>,
        ICommandHandler<UpdateRequest, RequestDto>,
        ICommandHandler<SubmitRequest, RequestDto>,
        ICommandHandler<CancelRequest, RequestDto>,
        ICommandHandler<OverrideClassification, RequestDto>,
        ICommandHandler<SetApprovedBudget, RequestDto>,
        IQueryHandler<GetRequest, RequestDto>,
        IQueryHandler<ListRequests, PagedResult<RequestListItemDto>>
{
    public async Task<Result<RequestDto>> HandleAsync(CreateRequest command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var values = command.Values ?? new RequestValues();
        if (values.UnitId is null)
        {
            values = values with { UnitId = await reader.UnitOfAsync(user.EmployeeIdOf(), cancellationToken).ConfigureAwait(false) };
        }

        var errors = RequestRules.ValidateFormat(values, await reader.RuleContextAsync(null, cancellationToken).ConfigureAwait(false));
        errors.AddRange(await evaluator.LookupErrorsAsync(values, cancellationToken).ConfigureAwait(false));
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, PortfolioPermissions.RequestNumbering, new NumberingContext(clock.Today), cancellationToken).ConfigureAwait(false);
        var request = ProgramRequest.Create(number, user.EmployeeIdOf(), user.UserId(), command.Source ?? "manual");
        var (classification, score, band) = await evaluator.ComputeAsync(values, cancellationToken).ConfigureAwait(false);
        var edited = request.Edit(values, classification, score, band);
        if (edited.IsFailure)
        {
            return edited.Error;
        }

        db.Requests.Add(request);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (user.EmployeeIdOf() is { } employee)
        {
            await recordRoles.GrantInTransactionAsync(db, PortfolioPermissions.RequestResource, request.Id, RecordPrincipalKinds.Employee, employee,
                PortfolioPermissions.RequesterRole, cancellationToken).ConfigureAwait(false);
        }
        else if (user.UserId() is { } userId)
        {
            await recordRoles.GrantInTransactionAsync(db, PortfolioPermissions.RequestResource, request.Id, RecordPrincipalKinds.User, userId,
                PortfolioPermissions.RequesterRole, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await queries.ToDtoAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RequestDto>> HandleAsync(UpdateRequest command, CancellationToken cancellationToken)
    {
        var request = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, PortfolioPermissions.RequestEdit, request, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var values = command.Values ?? new RequestValues();
        var errors = RequestRules.ValidateFormat(values, await reader.RuleContextAsync(request, cancellationToken).ConfigureAwait(false));
        errors.AddRange(await evaluator.LookupErrorsAsync(values, cancellationToken).ConfigureAwait(false));
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(request).Property(x => x.Version).OriginalValue = command.Version;
        var (classification, score, band) = await evaluator.ComputeAsync(values, cancellationToken).ConfigureAwait(false);
        var edited = request.Edit(values, classification, score, band);
        if (edited.IsFailure)
        {
            return edited.Error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await queries.ToDtoAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RequestDto>> HandleAsync(SubmitRequest command, CancellationToken cancellationToken)
    {
        var request = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, PortfolioPermissions.RequestEdit, request, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!RequestLifecycle.Allows(request.Status, RequestActions.Submit))
        {
            return RequestLifecycle.Next(request.Status, RequestActions.Submit).Error!;
        }

        // Values may have been saved under older settings: recompute, then apply every submission rule (ADR 0047).
        await flags.RefreshAsync(request, cancellationToken).ConfigureAwait(false);
        var values = request.ToValues();
        var errors = RequestRules.ValidateForSubmission(values, await reader.RuleContextAsync(request, cancellationToken).ConfigureAwait(false));
        errors.AddRange(await evaluator.LookupErrorsAsync(values, cancellationToken).ConfigureAwait(false));
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        db.Entry(request).Property(x => x.Version).OriginalValue = command.Version;
        var (classification, score, band) = await evaluator.ComputeAsync(values, cancellationToken).ConfigureAwait(false);
        request.Edit(values, classification, score, band);
        var submitted = request.Submit(clock.UtcNow);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        var started = await engine.SubmitAsync(
            new ApprovalSubmission(
                RequestApprovalProcess.Key,
                new ApprovalSubject(PortfolioPermissions.RequestResource, request.Id, request.Number, request.NameAr, request.NameEn, request.RequesterEmployeeId,
                    request.RequesterUserId, request.UnitId),
                new Dictionary<string, object?>
                {
                    [RequestApprovalProcess.RequiresBudget] = request.RequiresBudget == true,
                    [RequestApprovalProcess.EstimatedBudget] = request.RequiresBudget == true ? request.EstimatedBudget ?? 0 : 0m,
                    [RequestApprovalProcess.Threshold] = settings.ThresholdAmount,
                    [RequestApprovalProcess.Classification] = request.EffectiveClassification,
                    [RequestApprovalProcess.EvaluationScore] = Math.Round((request.EvaluationScore ?? 0) * 100, 2),
                },
                command.Comment),
            db,
            cancellationToken).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await db.Entry(request).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return await queries.ToDtoAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RequestDto>> HandleAsync(CancelRequest command, CancellationToken cancellationToken)
    {
        var request = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        // The requester cancels their own draft; the PM Office cancels any draft or request in approval (W1.8, W8).
        var manage = await reader.CanAsync(context.User, PortfolioPermissions.RequestManage, request, cancellationToken).ConfigureAwait(false);
        var ownDraft = request.Status == RequestStatuses.Draft
            && await reader.CanAsync(context.User, PortfolioPermissions.RequestEdit, request, cancellationToken).ConfigureAwait(false);
        if (!manage && !ownDraft)
        {
            return Error.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > RequestRules.TextMaxLength)
        {
            return Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]);
        }

        db.Entry(request).Property(x => x.Version).OriginalValue = command.Version;
        var wasInApproval = request.Status == RequestStatuses.InApproval;
        var cancelled = request.Cancel(command.Reason, clock.UtcNow);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        if (wasInApproval)
        {
            var stopped = await engine.CancelAsync(RequestApprovalProcess.Key, request.Id, command.Reason.Trim(), db, cancellationToken).ConfigureAwait(false);
            if (stopped.IsFailure)
            {
                return stopped.Error;
            }
        }

        if (request.RequesterUserId is { } requester && requester != context.User.UserId())
        {
            await notifier.SendAsync(PortfolioNotifications.RequestCancelled, request, [requester], command.Reason.Trim(), db, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await queries.ToDtoAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RequestDto>> HandleAsync(OverrideClassification command, CancellationToken cancellationToken)
    {
        var request = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, PortfolioPermissions.RequestManage, request, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var value = string.IsNullOrWhiteSpace(command.Value) ? null : command.Value.Trim();
        if (value is not null && !Classifications.All.Contains(value))
        {
            return Error.Validation([new FieldError("value", "validation.unknownCode", "validation.unknownCode")]);
        }

        if (value is not null && (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > RequestRules.TextMaxLength))
        {
            return Error.Validation([new FieldError("reason", "portfolio.errors.overrideReasonRequired", "portfolio.errors.overrideReasonRequired")]);
        }

        db.Entry(request).Property(x => x.Version).OriginalValue = command.Version;
        var applied = request.OverrideClassification(value, command.Reason, context.User.UserId(), clock.UtcNow);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await queries.ToDtoAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RequestDto>> HandleAsync(SetApprovedBudget command, CancellationToken cancellationToken)
    {
        var request = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        if (!await queries.IsBudgetApproverAsync(request, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (command.Amount < 0 || decimal.Round(command.Amount, 2) != command.Amount)
        {
            return Error.Validation([new FieldError("amount", "portfolio.errors.twoDecimals", "portfolio.errors.twoDecimals")]);
        }

        db.Entry(request).Property(x => x.Version).OriginalValue = command.Version;
        var applied = request.SetApprovedBudget(command.Amount);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await queries.ToDtoAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task<Result<RequestDto>> HandleAsync(GetRequest query, CancellationToken cancellationToken) => queries.GetAsync(query.Id, cancellationToken);

    public async Task<Result<PagedResult<RequestListItemDto>>> HandleAsync(ListRequests query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var page = query.Page.Normalised();
        var source = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var me = user.EmployeeIdOf();
        var myUser = user.UserId();
        switch (query.View ?? RequestViews.All)
        {
            case RequestViews.All:
                break;
            case RequestViews.Mine:
                source = source.Where(r => (me != null && r.RequesterEmployeeId == me) || (myUser != null && r.RequesterUserId == myUser));
                break;
            case RequestViews.InApproval:
                source = source.Where(r => r.Status == RequestStatuses.InApproval);
                break;
            case RequestViews.Returned:
                source = source.Where(r => r.Status == RequestStatuses.Draft && r.ApprovalStatus == RequestApprovalResults.Returned);
                break;
            default:
                return Error.Validation([new FieldError("view", "validation.unknownCode", "validation.unknownCode")]);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(r => r.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Classification))
        {
            source = source.Where(r => (r.ClassificationOverride ?? r.Classification) == query.Classification);
        }

        if (!string.IsNullOrWhiteSpace(query.PriorityBand))
        {
            source = source.Where(r => r.PriorityBand == query.PriorityBand);
        }

        if (query.UnitId is { } unit)
        {
            source = source.Where(r => r.UnitId == unit);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like.Pattern(query.Search);
            source = source.Where(r => EF.Functions.ILike(r.NameAr ?? string.Empty, pattern, "\\") || EF.Functions.ILike(r.NameEn ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(r.Number, pattern, "\\"));
        }

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        source = page.SortOrder switch
        {
            ("number", false) => source.OrderBy(r => r.Number),
            ("number", true) => source.OrderByDescending(r => r.Number),
            ("name", false) => source.OrderBy(r => r.NameEn).ThenBy(r => r.NameAr),
            ("name", true) => source.OrderByDescending(r => r.NameEn).ThenByDescending(r => r.NameAr),
            ("score", false) => source.OrderBy(r => r.EvaluationScore),
            ("score", true) => source.OrderByDescending(r => r.EvaluationScore),
            ("budget", false) => source.OrderBy(r => r.EstimatedBudget),
            ("budget", true) => source.OrderByDescending(r => r.EstimatedBudget),
            ("submittedAt", false) => source.OrderBy(r => r.SubmittedAt),
            ("submittedAt", true) => source.OrderByDescending(r => r.SubmittedAt),
            _ => source.OrderByDescending(r => r.UpdatedAt),
        };
        var requests = await source.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<RequestListItemDto>(await queries.ItemsAsync(requests, cancellationToken).ConfigureAwait(false), page.Page, page.PageSize, total);
    }
}

internal static class Like
{
    public static string Pattern(string text) =>
        "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
