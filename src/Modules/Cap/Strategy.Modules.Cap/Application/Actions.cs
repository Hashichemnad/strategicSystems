using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Cap.Application;

public sealed record ActionPermissionsDto(bool Edit, bool Progress, bool RequestCompletion, bool RequestExtension, bool Cancel, bool LinkProject, bool Validate, bool Unlink);

public sealed record ActionRemarkRefDto(Guid Id, string Number, bool Home);

public sealed record ActionDto(
    Guid Id,
    string Number,
    Guid HomeRemarkId,
    ActionValues Values,
    string ValidationStatus,
    string? ValidationNote,
    string Status,
    decimal AchievedPct,
    DateOnly? ActualStart,
    DateOnly? ActualEnd,
    DateOnly? RevisedEnd,
    DateOnly? EffectiveEnd,
    CapPersonDto? Assignee,
    IReadOnlyList<ActionRemarkRefDto> Remarks,
    Guid? ProjectId,
    PortfolioRecordBadge? Project,
    Guid? ProgramRequestId,
    string? ProgramRequestNumber,
    PortfolioRecordBadge? ProgramRequest,
    string? PendingProcess,
    string? CompletionNote,
    DateTimeOffset? CompletedAt,
    string? CancelReason,
    int EvidenceCount,
    IReadOnlyList<string> QualityFlags,
    ActionPermissionsDto Can,
    uint Version);

public sealed record ActionListItemDto(
    Guid Id, string Number, string? DetailsAr, string? DetailsEn, Guid HomeRemarkId, string HomeRemarkNumber, string ValidationStatus, string Status, decimal AchievedPct,
    DateOnly? EffectiveEnd, CapPersonDto? Assignee, bool BudgetRequired, bool? BudgetAvailable, string? ProgramRequestNumber, Guid? ProgramRequestId, string? PendingProcess,
    uint Version);

public sealed record AchievementUpdateDto(Guid Id, decimal Pct, string? Note, DateOnly? ActualStart, DateOnly? ActualEnd, DateTimeOffset At, Guid? By, bool Draft);

public sealed record ExtensionDto(Guid Id, DateOnly? PreviousEnd, DateOnly RequestedEnd, string Justification, string Status, DateTimeOffset? DecidedAt, string? DecisionNote,
    DateTimeOffset CreatedAt);

public sealed record ValidationViewDto(RemarkDto Remark, IReadOnlyList<ActionDto> Actions);

public sealed record ActionBody(ActionValues Values, uint? Version);

public sealed record ProgressInput(decimal Pct, string? Note, DateOnly? ActualStart, DateOnly? ActualEnd, uint Version, bool FromMcp = false);

public sealed record ValidateInput(string Decision, string? Comment, Guid? ProjectId);

public sealed record ExtensionInput(DateOnly RequestedEnd, string? Justification);

public sealed record ListRemarkActions(Guid RemarkId) : IQuery<IReadOnlyList<ActionDto>>;

public sealed record ListActions(PageRequest Page, string? View, string? Search, string? Status, Guid? UnitId) : IQuery<PagedResult<ActionListItemDto>>;

public sealed record SearchLinkableActions(Guid RemarkId, string? Text) : IQuery<IReadOnlyList<ActionListItemDto>>;

public sealed record GetAction(Guid Id) : IQuery<ActionDto>;

public sealed record GetValidationView(Guid RemarkId) : IQuery<ValidationViewDto>;

public sealed record ListAchievementUpdates(Guid Id) : IQuery<IReadOnlyList<AchievementUpdateDto>>;

public sealed record ListExtensions(Guid Id) : IQuery<IReadOnlyList<ExtensionDto>>;

public sealed record CreateAction(Guid RemarkId, ActionValues Values) : ICommand<ActionDto>;

public sealed record UpdateAction(Guid Id, ActionValues Values, uint Version) : ICommand<ActionDto>;

public sealed record LinkAction(Guid RemarkId, Guid ActionId) : ICommand<ActionDto>;

public sealed record UnlinkAction(Guid RemarkId, Guid ActionId) : ICommand<bool>;

public sealed record ValidateAction(Guid Id, ValidateInput Input) : ICommand<ActionDto>;

public sealed record LinkActionProject(Guid Id, Guid? ProjectId) : ICommand<ActionDto>;

public sealed record UpdateAchievement(Guid Id, ProgressInput Input) : ICommand<ActionDto>;

public sealed record ConfirmAchievementDraft(Guid Id, Guid UpdateId) : ICommand<ActionDto>;

public sealed record RequestActionCompletion(Guid Id, string? Note) : ICommand<ActionDto>;

public sealed record RequestActionExtension(Guid Id, ExtensionInput Input) : ICommand<ActionDto>;

public sealed record CancelAction(Guid Id, string? Reason) : ICommand<ActionDto>;

/// <summary>Maps actions for the API (badges of linked Portfolio records, evidence count, quality flags, what the caller may do).</summary>
internal sealed class ActionViews(CapDbContext db, CapReader reader, IFileService files, CapDeadlines deadlines, IRequestContext context, IServiceProvider services)
{
    public async Task<ActionDto> ToDtoAsync(CapAction a, CancellationToken ct, Guid? remarkContext = null)
    {
        var user = context.User;
        var people = await reader.PeopleAsync([a.AssignedEmployeeId], ct).ConfigureAwait(false);
        var links = await db.RemarkActions.AsNoTracking().Where(l => l.ActionId == a.Id).Join(db.Remarks.AsNoTracking(), l => l.RemarkId, r => r.Id, (l, r) => r)
            .OrderBy(r => r.Number).ToListAsync(ct).ConfigureAwait(false);
        var portfolio = services.GetService(typeof(IPortfolioDirectory)) as IPortfolioDirectory;
        var project = a.ProjectId is { } p && portfolio is not null ? await portfolio.GetProjectAsync(p, user, ct).ConfigureAwait(false) : null;
        var request = a.ProgramRequestId is { } r && portfolio is not null ? await portfolio.GetRequestAsync(r, user, ct).ConfigureAwait(false) : null;
        var evidence = (await files.ListAsync(CapAction.EntityType, a.Id.ToString("D"), CapFilePurposes.Evidence.Key, ct).ConfigureAwait(false)).Count;
        var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
        var home = links.FirstOrDefault(l => l.Id == a.HomeRemarkId);
        var can = await PermissionsAsync(a, links, user, ct, remarkContext).ConfigureAwait(false);
        var flags = can.Validate || a.ValidationStatus is ValidationStatuses.Draft or ValidationStatuses.Returned or ValidationStatuses.Pending
            ? ActionQuality.Flags(a.Values(), today, home?.DueOn)
            : [];
        return new ActionDto(a.Id, a.Number, a.HomeRemarkId, a.Values(), a.ValidationStatus, a.ValidationNote, a.Status, a.AchievedPct, a.ActualStart, a.ActualEnd,
            a.RevisedEnd, a.EffectiveEnd, CapReader.Person(a.AssignedEmployeeId, people), links.Select(l => new ActionRemarkRefDto(l.Id, l.Number, l.Id == a.HomeRemarkId)).ToList(),
            a.ProjectId, project, a.ProgramRequestId, a.ProgramRequestNumber, request, a.PendingProcess, a.CompletionNote, a.CompletedAt, a.CancelReason, evidence, flags, can,
            a.Version);
    }

    public async Task<ActionPermissionsDto> PermissionsAsync(CapAction a, IReadOnlyList<Remark> remarks, ClaimsPrincipal user, CancellationToken ct, Guid? remarkContext = null)
    {
        var me = user.EmployeeIdOf();
        var home = remarks.FirstOrDefault(r => r.Id == a.HomeRemarkId) ?? (remarks.Count > 0 ? remarks[0] : null);
        var office = false;
        var respond = false;
        foreach (var remark in remarks)
        {
            office |= await reader.CanAsync(user, CapPermissions.Office, remark, ct).ConfigureAwait(false);
            respond |= remark.OwnerEmployeeId == me && me is not null && await reader.CanAsync(user, CapPermissions.Respond, remark, ct).ConfigureAwait(false);
        }

        var homeRespond = home is not null && home.OwnerEmployeeId == me && me is not null
            && await reader.CanAsync(user, CapPermissions.Respond, home, ct).ConfigureAwait(false);
        var assignee = me is not null && a.AssignedEmployeeId == me
            && (remarks.Count == 0 || await AnyCanAsync(remarks, user, CapPermissions.ActionUpdate, ct).ConfigureAwait(false));
        var homeOpen = home is { IsOpen: true } && home.Status != CapRemarkStatuses.Validation;
        var approved = a.ValidationStatus == ValidationStatuses.Approved && a.IsOpen && a.PendingProcess is null;
        var context = remarkContext is { } rc ? remarks.FirstOrDefault(r => r.Id == rc) : null;
        var unlink = context is not null && context.Id != a.HomeRemarkId && context.OwnerEmployeeId == me
            && context.Status is CapRemarkStatuses.Planning or CapRemarkStatuses.Correction;
        return new ActionPermissionsDto(
            (homeRespond || office) && a.IsEditable && homeOpen,
            (assignee || office) && approved,
            (assignee || office) && approved && a.AchievedPct >= 1m,
            (assignee || respond || office) && approved,
            (office && a.IsOpen) || (homeRespond && a.IsEditable),
            office && a.IsOpen,
            office && a.PendingProcess == CapProcesses.Validation,
            unlink);
    }

    public async Task<IReadOnlyList<ActionListItemDto>> ListItemsAsync(IReadOnlyList<CapAction> actions, CancellationToken ct)
    {
        var people = await reader.PeopleAsync(actions.Select(a => a.AssignedEmployeeId), ct).ConfigureAwait(false);
        var homeIds = actions.Select(a => a.HomeRemarkId).Distinct().ToList();
        var homes = await db.Remarks.AsNoTracking().Where(r => homeIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, ct).ConfigureAwait(false);
        return actions.Select(a => new ActionListItemDto(a.Id, a.Number, a.DetailsAr, a.DetailsEn, a.HomeRemarkId, homes.GetValueOrDefault(a.HomeRemarkId) ?? string.Empty,
            a.ValidationStatus, a.Status, a.AchievedPct, a.EffectiveEnd, CapReader.Person(a.AssignedEmployeeId, people), a.BudgetRequired, a.BudgetAvailable,
            a.ProgramRequestNumber, a.ProgramRequestId, a.PendingProcess, a.Version)).ToList();
    }

    private async Task<bool> AnyCanAsync(IReadOnlyList<Remark> remarks, ClaimsPrincipal user, string permission, CancellationToken ct)
    {
        foreach (var remark in remarks)
        {
            if (await reader.CanAsync(user, permission, remark, ct).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class ActionQueries(CapDbContext db, CapReader reader, CapViews views, ActionViews actionViews, IRequestContext context)
    : IQueryHandler<ListRemarkActions, IReadOnlyList<ActionDto>>, IQueryHandler<ListActions, PagedResult<ActionListItemDto>>,
        IQueryHandler<SearchLinkableActions, IReadOnlyList<ActionListItemDto>>, IQueryHandler<GetAction, ActionDto>, IQueryHandler<GetValidationView, ValidationViewDto>,
        IQueryHandler<ListAchievementUpdates, IReadOnlyList<AchievementUpdateDto>>, IQueryHandler<ListExtensions, IReadOnlyList<ExtensionDto>>
{
    public async Task<Result<IReadOnlyList<ActionDto>>> HandleAsync(ListRemarkActions query, CancellationToken cancellationToken)
    {
        if (await reader.RemarkAsync(query.RemarkId, context.User, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        var ids = db.RemarkActions.Where(l => l.RemarkId == query.RemarkId).Select(l => l.ActionId);
        var actions = await db.Actions.AsNoTracking().Where(a => ids.Contains(a.Id)).OrderBy(a => a.Number).ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ActionDto>();
        foreach (var a in actions)
        {
            result.Add(await actionViews.ToDtoAsync(a, cancellationToken, query.RemarkId).ConfigureAwait(false));
        }

        return result;
    }

    public static readonly IReadOnlyList<string> Views = ["mine", "all", "to-validate", "late", "budget", "pending-completion", "open"];

    public async Task<Result<PagedResult<ActionListItemDto>>> HandleAsync(ListActions query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var source = await reader.ActionsAsync(user, cancellationToken).ConfigureAwait(false);
        var me = user.EmployeeIdOf() ?? Guid.Empty;
        source = query.View switch
        {
            "mine" or null => source.Where(a => a.AssignedEmployeeId == me && a.ValidationStatus == ValidationStatuses.Approved),
            "to-validate" => source.Where(a => a.PendingProcess == CapProcesses.Validation),
            "late" => source.Where(a => a.Status == ActionStatuses.Late),
            "budget" => source.Where(a => a.BudgetRequired),
            "pending-completion" => source.Where(a => a.PendingProcess == CapProcesses.Completion || a.PendingProcess == CapProcesses.Extension),
            "open" => source.Where(a => a.Status != ActionStatuses.Completed && a.Status != ActionStatuses.Cancelled),
            _ => source,
        };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like.Pattern(query.Search);
            source = source.Where(a => EF.Functions.ILike(a.Number, pattern, "\\") || EF.Functions.ILike(a.DetailsAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(a.DetailsEn ?? string.Empty, pattern, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(a => a.Status == query.Status);
        }

        if (query.UnitId is { } unitId)
        {
            var units = await reader.UnitAndBelowAsync(unitId, cancellationToken).ConfigureAwait(false);
            var remarks = db.Remarks.Where(r => units.Contains(r.DepartmentUnitId)).Select(r => r.Id);
            source = source.Where(a => remarks.Contains(a.HomeRemarkId));
        }

        var page = query.Page.Normalised();
        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await source.OrderBy(a => a.RevisedEnd ?? a.PlannedEnd).ThenBy(a => a.Number).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<ActionListItemDto>(await actionViews.ListItemsAsync(rows, cancellationToken).ConfigureAwait(false), page.Page, page.PageSize, total);
    }

    /// <summary>Open actions of other remarks the user may see, to link instead of duplicating (SPEC §9.3 C).</summary>
    public async Task<Result<IReadOnlyList<ActionListItemDto>>> HandleAsync(SearchLinkableActions query, CancellationToken cancellationToken)
    {
        if (await reader.RemarkAsync(query.RemarkId, context.User, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        var linked = db.RemarkActions.Where(l => l.RemarkId == query.RemarkId).Select(l => l.ActionId);
        var source = (await reader.ActionsAsync(context.User, cancellationToken).ConfigureAwait(false))
            .Where(a => !linked.Contains(a.Id) && a.Status != ActionStatuses.Cancelled && a.Status != ActionStatuses.Completed
                && a.ValidationStatus != ValidationStatuses.Rejected);
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var pattern = Like.Pattern(query.Text);
            source = source.Where(a => EF.Functions.ILike(a.Number, pattern, "\\") || EF.Functions.ILike(a.DetailsAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(a.DetailsEn ?? string.Empty, pattern, "\\"));
        }

        var rows = await source.OrderBy(a => a.Number).Take(30).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<ActionListItemDto>>.Success(await actionViews.ListItemsAsync(rows, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<ActionDto>> HandleAsync(GetAction query, CancellationToken cancellationToken) =>
        await reader.ActionAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is { } a
            ? await actionViews.ToDtoAsync(a, cancellationToken).ConfigureAwait(false)
            : Error.NotFound();

    public async Task<Result<ValidationViewDto>> HandleAsync(GetValidationView query, CancellationToken cancellationToken)
    {
        var remark = await reader.RemarkAsync(query.RemarkId, context.User, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, CapPermissions.Office, remark, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var ids = db.RemarkActions.Where(l => l.RemarkId == remark.Id).Select(l => l.ActionId);
        var actions = await db.Actions.AsNoTracking().Where(a => ids.Contains(a.Id)).OrderBy(a => a.Number).ToListAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<ActionDto>();
        foreach (var a in actions)
        {
            list.Add(await actionViews.ToDtoAsync(a, cancellationToken, remark.Id).ConfigureAwait(false));
        }

        return new ValidationViewDto(await views.ToDtoAsync(remark, cancellationToken).ConfigureAwait(false), list);
    }

    public async Task<Result<IReadOnlyList<AchievementUpdateDto>>> HandleAsync(ListAchievementUpdates query, CancellationToken cancellationToken)
    {
        if (await reader.ActionAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        return await db.Updates.AsNoTracking().Where(u => u.ActionId == query.Id).OrderByDescending(u => u.At)
            .Select(u => new AchievementUpdateDto(u.Id, u.Pct, u.Note, u.ActualStart, u.ActualEnd, u.At, u.By, u.Draft)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<ExtensionDto>>> HandleAsync(ListExtensions query, CancellationToken cancellationToken)
    {
        if (await reader.ActionAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        return await db.Extensions.AsNoTracking().Where(e => e.ActionId == query.Id).OrderByDescending(e => e.CreatedAt)
            .Select(e => new ExtensionDto(e.Id, e.PreviousEnd, e.RequestedEnd, e.Justification, e.Status, e.DecidedAt, e.DecisionNote, e.CreatedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>C2 / C3 commands on actions (docs/design/cap.md §5).</summary>
internal sealed class ActionCommands(
    CapDbContext db, CapReader reader, ActionViews views, CapService service, CapApprovals approvals, CapDeadlines deadlines, ILookupService lookups,
    IFileService files, IApprovalEngine engine, CapViews remarkViews, IRequestContext context, IClock clock, IServiceProvider services)
    : ICommandHandler<CreateAction, ActionDto>, ICommandHandler<UpdateAction, ActionDto>, ICommandHandler<LinkAction, ActionDto>, ICommandHandler<UnlinkAction, bool>,
        ICommandHandler<ValidateAction, ActionDto>, ICommandHandler<LinkActionProject, ActionDto>, ICommandHandler<UpdateAchievement, ActionDto>,
        ICommandHandler<ConfirmAchievementDraft, ActionDto>, ICommandHandler<RequestActionCompletion, ActionDto>, ICommandHandler<RequestActionExtension, ActionDto>,
        ICommandHandler<CancelAction, ActionDto>
{
    public async Task<Result<ActionDto>> HandleAsync(CreateAction command, CancellationToken cancellationToken)
    {
        var remark = await reader.RemarkAsync(command.RemarkId, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (remark is null)
        {
            return Error.NotFound();
        }

        if (!(await RemarkPermissionsAsync(remark, cancellationToken).ConfigureAwait(false)).ManageActions)
        {
            return remark.IsOpen ? Error.Forbidden() : Error.Rule("cap.notEditable", "cap.errors.notEditable");
        }

        var errors = await CapService.ValidateActionAsync(command.Values, lookups, reader, false, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var action = await service.NewActionAsync(remark, command.Values, null, cancellationToken).ConfigureAwait(false);
        if (remark.Status == CapRemarkStatuses.Implementation)
        {
            // A new action re-opens planning (docs/design/cap.md §4.1).
            remark.Apply(RemarkActions.ToPlanning, clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await service.PublishAsync(remark, null, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(action, cancellationToken, remark.Id).ConfigureAwait(false);
    }

    public async Task<Result<ActionDto>> HandleAsync(UpdateAction command, CancellationToken cancellationToken)
    {
        var (action, remarks) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Error.NotFound();
        }

        if (!(await views.PermissionsAsync(action, remarks, context.User, cancellationToken).ConfigureAwait(false)).Edit)
        {
            return action.IsOpen ? Error.Forbidden() : Error.Rule("cap.notEditable", "cap.errors.notEditable");
        }

        var errors = await CapService.ValidateActionAsync(command.Values, lookups, reader, false, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(action).Property(a => a.Version).OriginalValue = command.Version;
        var assigneeChanged = action.AssignedEmployeeId != command.Values.AssignedEmployeeId;
        action.Edit(command.Values);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (assigneeChanged)
        {
            await service.SyncRolesForActionAsync(action.Id, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(action, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ActionDto>> HandleAsync(LinkAction command, CancellationToken cancellationToken)
    {
        var remark = await reader.RemarkAsync(command.RemarkId, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (remark is null)
        {
            return Error.NotFound();
        }

        if (!(await RemarkPermissionsAsync(remark, cancellationToken).ConfigureAwait(false)).ManageActions)
        {
            return remark.IsOpen ? Error.Forbidden() : Error.Rule("cap.notEditable", "cap.errors.notEditable");
        }

        var action = await reader.ActionAsync(command.ActionId, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (action is null)
        {
            return Error.Validation([new FieldError("actionId", "validation.invalid", "validation.invalid")]);
        }

        if (action.Status is ActionStatuses.Cancelled or ActionStatuses.Completed || action.ValidationStatus == ValidationStatuses.Rejected)
        {
            return Error.Rule("cap.actionNotLinkable", "cap.errors.actionNotLinkable");
        }

        if (await db.RemarkActions.AnyAsync(l => l.RemarkId == remark.Id && l.ActionId == action.Id, cancellationToken).ConfigureAwait(false))
        {
            return await views.ToDtoAsync(action, cancellationToken, remark.Id).ConfigureAwait(false);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.RemarkActions.Add(RemarkAction.Create(remark.Id, action.Id, clock.UtcNow, context.User.UserId()));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await service.SyncRolesAsync(remark, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(action, cancellationToken, remark.Id).ConfigureAwait(false);
    }

    public async Task<Result<bool>> HandleAsync(UnlinkAction command, CancellationToken cancellationToken)
    {
        var remark = await reader.RemarkAsync(command.RemarkId, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (remark is null)
        {
            return Error.NotFound();
        }

        var (action, remarks) = await LoadAsync(command.ActionId, cancellationToken).ConfigureAwait(false);
        var link = await db.RemarkActions.FirstOrDefaultAsync(l => l.RemarkId == remark.Id && l.ActionId == command.ActionId, cancellationToken).ConfigureAwait(false);
        if (action is null || link is null)
        {
            return Error.NotFound();
        }

        if (!(await views.PermissionsAsync(action, remarks, context.User, cancellationToken, remark.Id).ConfigureAwait(false)).Unlink)
        {
            return Error.Forbidden();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.RemarkActions.Remove(link);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await service.SyncRolesAsync(remark, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>PMO decision on one action (Validate Actions screen) through the engine's own checks (ADR 0121).</summary>
    public async Task<Result<ActionDto>> HandleAsync(ValidateAction command, CancellationToken cancellationToken)
    {
        var (action, remarks) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Error.NotFound();
        }

        if (!(await views.PermissionsAsync(action, remarks, context.User, cancellationToken).ConfigureAwait(false)).Validate)
        {
            return Error.Forbidden();
        }

        var decision = command.Input.Decision;
        if (decision is not (ApprovalActions.Approve or ApprovalActions.Reject or ApprovalActions.Return))
        {
            return Error.Validation([new FieldError("decision", "validation.invalid", "validation.invalid")]);
        }

        if (decision != ApprovalActions.Approve && string.IsNullOrWhiteSpace(command.Input.Comment))
        {
            return Error.Validation([new FieldError("comment", "validation.required", "validation.required")]);
        }

        if (command.Input.ProjectId != action.ProjectId)
        {
            var linked = await LinkProjectAsync(action, command.Input.ProjectId, cancellationToken).ConfigureAwait(false);
            if (linked.IsFailure)
            {
                return linked.Error;
            }
        }

        var decided = await engine.DecideAsync(CapProcesses.Validation, action.Id, decision, command.Input.Comment, cancellationToken).ConfigureAwait(false);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        await db.Entry(action).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(action, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ActionDto>> HandleAsync(LinkActionProject command, CancellationToken cancellationToken)
    {
        var (action, remarks) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Error.NotFound();
        }

        if (!(await views.PermissionsAsync(action, remarks, context.User, cancellationToken).ConfigureAwait(false)).LinkProject)
        {
            return Error.Forbidden();
        }

        var linked = await LinkProjectAsync(action, command.ProjectId, cancellationToken).ConfigureAwait(false);
        return linked.IsFailure ? linked.Error : await views.ToDtoAsync(action, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ActionDto>> HandleAsync(UpdateAchievement command, CancellationToken cancellationToken)
    {
        var (action, remarks) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Error.NotFound();
        }

        if (!(await views.PermissionsAsync(action, remarks, context.User, cancellationToken).ConfigureAwait(false)).Progress)
        {
            return action.IsOpen ? Error.Forbidden() : Error.Rule("cap.notEditable", "cap.errors.notEditable");
        }

        var input = command.Input;
        var errors = new List<FieldError>();
        if (input.Pct is < 0 or > 1)
        {
            errors.Add(new FieldError("pct", "validation.inclusiveBetween", "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = 0, ["to"] = 100 }));
        }

        if (input.Note is { Length: > CapSource.TextMaxLength })
        {
            errors.Add(new FieldError("note", "validation.maximumLength", "validation.maximumLength"));
        }

        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        if (input.ActualStart is { } s && s > today)
        {
            errors.Add(new FieldError("actualStart", "cap.errors.dateInFuture", "cap.errors.dateInFuture"));
        }

        if (input.ActualEnd is { } e && (e > today || (input.ActualStart is { } st && e < st)))
        {
            errors.Add(new FieldError("actualEnd", "validation.invalid", "validation.invalid"));
        }

        if (input.Pct < 1m && input.ActualEnd is not null)
        {
            errors.Add(new FieldError("actualEnd", "cap.errors.endOnlyAtCompletion", "cap.errors.endOnlyAtCompletion"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(action).Property(a => a.Version).OriginalValue = input.Version;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.Updates.Add(AchievementUpdate.Create(action.Id, input.Pct, input.Note, input.ActualStart, input.ActualEnd, clock.UtcNow, context.User.UserId(), input.FromMcp));
        if (!input.FromMcp)
        {
            action.Progress(input.Pct, input.ActualStart, input.ActualEnd, today);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(action, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ActionDto>> HandleAsync(ConfirmAchievementDraft command, CancellationToken cancellationToken)
    {
        var (action, remarks) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        var update = await db.Updates.FirstOrDefaultAsync(u => u.Id == command.UpdateId && u.ActionId == command.Id && u.Draft, cancellationToken).ConfigureAwait(false);
        if (action is null || update is null)
        {
            return Error.NotFound();
        }

        if (!(await views.PermissionsAsync(action, remarks, context.User, cancellationToken).ConfigureAwait(false)).Progress)
        {
            return Error.Forbidden();
        }

        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        update.Confirm(clock.UtcNow);
        action.Progress(update.Pct, update.ActualStart, update.ActualEnd, today);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(action, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ActionDto>> HandleAsync(RequestActionCompletion command, CancellationToken cancellationToken)
    {
        var (action, remarks) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Error.NotFound();
        }

        if (!(await views.PermissionsAsync(action, remarks, context.User, cancellationToken).ConfigureAwait(false)).RequestCompletion)
        {
            return action.AchievedPct < 1m && action.IsOpen ? Error.Rule("cap.notComplete", "cap.errors.notComplete") : Error.Forbidden();
        }

        if ((await files.ListAsync(CapAction.EntityType, action.Id.ToString("D"), CapFilePurposes.Evidence.Key, cancellationToken).ConfigureAwait(false)).Count == 0)
        {
            return Error.Rule("cap.evidenceRequired", "cap.errors.evidenceRequired");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        action.RequestCompletion(command.Note);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var submitted = await approvals.SubmitAsync(CapProcesses.Completion, action, remarks.FirstOrDefault(r => r.Id == action.HomeRemarkId) ?? remarks[0], command.Note, db,
            cancellationToken).ConfigureAwait(false);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(action, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ActionDto>> HandleAsync(RequestActionExtension command, CancellationToken cancellationToken)
    {
        var (action, remarks) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Error.NotFound();
        }

        if (!(await views.PermissionsAsync(action, remarks, context.User, cancellationToken).ConfigureAwait(false)).RequestExtension)
        {
            return action.IsOpen ? Error.Forbidden() : Error.Rule("cap.notEditable", "cap.errors.notEditable");
        }

        var input = command.Input;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.Justification))
        {
            errors.Add(new FieldError("justification", "validation.required", "validation.required"));
        }
        else if (input.Justification.Length > CapSource.TextMaxLength)
        {
            errors.Add(new FieldError("justification", "validation.maximumLength", "validation.maximumLength"));
        }

        if (action.EffectiveEnd is { } end && input.RequestedEnd <= end)
        {
            errors.Add(new FieldError("requestedEnd", "cap.errors.extensionNotLater", "cap.errors.extensionNotLater"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.Extensions.Add(ExtensionRequest.Create(action.Id, action.EffectiveEnd, input.RequestedEnd, input.Justification!));
        action.SetPending(CapProcesses.Extension);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var submitted = await approvals.SubmitAsync(CapProcesses.Extension, action, remarks.FirstOrDefault(r => r.Id == action.HomeRemarkId) ?? remarks[0],
            input.Justification, db, cancellationToken).ConfigureAwait(false);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(action, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ActionDto>> HandleAsync(CancelAction command, CancellationToken cancellationToken)
    {
        var (action, remarks) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return Error.NotFound();
        }

        if (!(await views.PermissionsAsync(action, remarks, context.User, cancellationToken).ConfigureAwait(false)).Cancel)
        {
            return action.IsOpen ? Error.Forbidden() : Error.Rule("cap.notEditable", "cap.errors.notEditable");
        }

        if (action.ValidationStatus != ValidationStatuses.Draft && string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.required", "validation.required")]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (action.PendingProcess is { } process)
        {
            await approvals.CancelAsync(process, action.Id, "action-cancelled", db, cancellationToken).ConfigureAwait(false);
        }

        action.Cancel(command.Reason);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var remark in await service.RemarksOfAsync(action.Id, cancellationToken).ConfigureAwait(false))
        {
            await service.RecomputeAsync(remark, cancellationToken).ConfigureAwait(false);
            await service.SyncRolesAsync(remark, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(action, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> LinkProjectAsync(CapAction action, Guid? projectId, CancellationToken ct)
    {
        if (projectId is { } id)
        {
            var portfolio = services.GetService(typeof(IPortfolioDirectory)) as IPortfolioDirectory;
            if (portfolio is null || await portfolio.GetProjectAsync(id, context.User, ct).ConfigureAwait(false) is null)
            {
                return Error.Validation([new FieldError("projectId", "validation.invalid", "validation.invalid")]);
            }
        }

        action.LinkProject(projectId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<(CapAction? Action, List<Remark> Remarks)> LoadAsync(Guid id, CancellationToken ct)
    {
        var action = await reader.ActionAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (action is null)
        {
            return (null, []);
        }

        var remarks = await service.RemarksOfAsync(action.Id, ct).ConfigureAwait(false);
        return (action, remarks);
    }

    private async Task<RemarkPermissionsDto> RemarkPermissionsAsync(Remark remark, CancellationToken ct) =>
        await remarkViews.PermissionsAsync(remark, context.User, ct).ConfigureAwait(false);
}
