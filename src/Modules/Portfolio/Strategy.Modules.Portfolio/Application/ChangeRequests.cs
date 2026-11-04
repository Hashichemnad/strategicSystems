using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio.Application;

public sealed record ChangeLogEntry(string Field, string? Old, string? New);

public sealed record ChangeRequestDto(
    Guid Id,
    Guid ProjectId,
    string Number,
    string Type,
    IReadOnlyList<string> Areas,
    string? Reason,
    string? ImpactOfNonResponse,
    string? ProposedNameAr,
    string? ProposedNameEn,
    string? ProposedDescription,
    string? ProposedScope,
    DateOnly? ProposedStart,
    DateOnly? ProposedEnd,
    decimal? ProposedBudget,
    bool CostAffected,
    PortfolioPersonDto? NewPm,
    PortfolioPersonDto? NewOwner,
    Guid? MergeIntoProjectId,
    string? MergeIntoProjectNumber,
    DateOnly? RequestedDate,
    string Status,
    PortfolioPersonDto? RequestedBy,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ImplementedAt,
    IReadOnlyList<ChangeLogEntry> Applied,
    string? CancelReason,
    bool CanEdit,
    bool CanApply,
    DateTimeOffset CreatedAt,
    uint Version,
    string? ProposedMethodology = null,
    bool CostsHidden = false);

public sealed record ChangeRequestInput(ChangeRequestValues Values, uint? Version = null);

/// <summary>What the PM confirms in "Apply changes" (pre-filled from the request; only the fields of the type are used).</summary>
public sealed record ChangeApplyValues(
    string? NameAr = null,
    string? NameEn = null,
    string? Description = null,
    string? Scope = null,
    DateOnly? PlannedStart = null,
    DateOnly? PlannedEnd = null,
    decimal? Budget = null,
    Guid? NewPmEmployeeId = null,
    Guid? NewOwnerEmployeeId = null,
    string? Methodology = null,
    bool Rebaseline = false);

public sealed record ChangeApplyInput(ChangeApplyValues Values, uint Version);

/// <summary>Change requests (W7, Q-P4, Q-P9, ADR 0073).</summary>
internal sealed class ChangeRequestService(
    PortfolioDbContext db,
    ProjectLoader loader,
    RequestReader reader,
    ProjectService projects,
    ProjectNotifier notifier,
    IApprovalEngine engine,
    IRecordRoleService recordRoles,
    IRequestContext context,
    IClock clock,
    BaselineService baselines,
    CostVisibility costs)
{
    private static readonly string[] AreaCodes = ["name", "description", "scope", "dates", "budget", "ownership", "plan", "methodology"];

    public async Task<Result<IReadOnlyList<ChangeRequestDto>>> ListAsync(Guid projectId, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        var rows = await db.ChangeRequests.AsNoTracking().Where(c => c.ProjectId == projectId).OrderByDescending(c => c.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return Result.Success(await ToDtosAsync(project, rows, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChangeRequestDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var (cr, project, error) = await LoadAsync(id, ct, tracked: false).ConfigureAwait(false);
        return cr is null ? error! : (await ToDtosAsync(project!, [cr], ct).ConfigureAwait(false))[0];
    }

    public async Task<Result<ChangeRequestDto>> CreateAsync(Guid projectId, ChangeRequestInput input, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectChange, project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!Live(project))
        {
            return ProjectService.NotInStage(project);
        }

        var errors = await ValidateAsync(input.Values, project, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var sequence = await db.ChangeRequests.CountAsync(c => c.ProjectId == projectId, ct).ConfigureAwait(false) + 1;
        var cr = ChangeRequest.Create(projectId, $"{project.Number}-CR-{sequence.ToString(CultureInfo.InvariantCulture)}", context.User.EmployeeIdOf(), input.Values,
            Areas(input.Values));
        db.ChangeRequests.Add(cr);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await ToDtosAsync(project, [cr], ct).ConfigureAwait(false))[0];
    }

    public async Task<Result<ChangeRequestDto>> UpdateAsync(Guid id, ChangeRequestInput input, CancellationToken ct)
    {
        var (cr, project, error) = await LoadAsync(id, ct).ConfigureAwait(false);
        if (cr is null)
        {
            return error!;
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectChange, project!, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!ChangeLifecycle.Allows(cr.Status, ChangeActions.Edit))
        {
            return ChangeLifecycle.Next(cr.Status, ChangeActions.Edit).Error!;
        }

        var errors = await ValidateAsync(input.Values, project!, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        if (input.Version is { } version)
        {
            db.Entry(cr).Property(x => x.Version).OriginalValue = version;
        }

        cr.Edit(input.Values, Areas(input.Values));
        return await SaveAndReturnAsync(cr, project!, ct).ConfigureAwait(false);
    }

    /// <summary>Submit: one change request under review per project, and no other project approval running (ADR 0070 / 0073).</summary>
    public async Task<Result<ChangeRequestDto>> SubmitAsync(Guid id, uint version, string? comment, CancellationToken ct)
    {
        var (cr, project, error) = await LoadAsync(id, ct).ConfigureAwait(false);
        if (cr is null)
        {
            return error!;
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectChange, project!, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var tracked = await db.Projects.FirstAsync(p => p.Id == cr.ProjectId, ct).ConfigureAwait(false);
        if (!Live(tracked))
        {
            return ProjectService.NotInStage(tracked);
        }

        if (tracked.ApprovalProcess is not null && tracked.ApprovalResult is null)
        {
            return ProjectService.ApprovalRunning(tracked.ApprovalProcess!);
        }

        var errors = await ValidateAsync(cr.ToValues(), tracked, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(cr).Property(x => x.Version).OriginalValue = version;
        var moved = cr.Move(ChangeActions.Submit, clock.UtcNow);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        tracked.EnterApproval(ProjectApprovalProcesses.ChangeRequest, null, null, null);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        var variables = ProjectService.Variables(tracked);
        variables[ProjectApprovalProcesses.ChangeTypeVariable] = cr.Type;
        variables[ProjectApprovalProcesses.CostAffectedVariable] = cr.CostAffected || (cr.ProposedBudget is { } b && b != (tracked.Budget ?? 0));
        variables[ProjectApprovalProcesses.CostChangeVariable] = cr.ProposedBudget is { } budget ? Math.Abs(budget - (tracked.Budget ?? 0)) : 0m;
        variables[ProjectApprovalProcesses.GmThresholdVariable] = settings.GmThreshold;
        var started = await engine.SubmitAsync(
            new ApprovalSubmission(ProjectApprovalProcesses.ChangeRequest, ProjectService.Subject(tracked), variables, comment ?? cr.Reason), db, ct).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        await notifier.ToEmployeesAsync(PortfolioNotifications.ChangeRequestSubmitted, tracked, [tracked.PmEmployeeId, tracked.OwnerEmployeeId], db, ct, cr.Number,
            context.User.Identity?.Name).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return (await ToDtosAsync(tracked, [cr], ct).ConfigureAwait(false))[0];
    }

    public async Task<Result<ChangeRequestDto>> CancelAsync(Guid id, ProjectReasonInput input, CancellationToken ct)
    {
        var (cr, project, error) = await LoadAsync(id, ct).ConfigureAwait(false);
        if (cr is null)
        {
            return error!;
        }

        var underReview = cr.Status == ChangeStatuses.UnderReview;
        var allowed = underReview
            ? await loader.CanAsync(context.User, PortfolioPermissions.ProjectManage, project!, ct).ConfigureAwait(false)
            : await loader.CanAsync(context.User, PortfolioPermissions.ProjectChange, project!, ct).ConfigureAwait(false);
        if (!allowed)
        {
            return Error.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > RequestRules.TextMaxLength)
        {
            return Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]);
        }

        db.Entry(cr).Property(x => x.Version).OriginalValue = input.Version;
        var moved = cr.Move(ChangeActions.Cancel, clock.UtcNow, input.Reason);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (underReview)
        {
            var tracked = await db.Projects.FirstAsync(p => p.Id == cr.ProjectId, ct).ConfigureAwait(false);
            tracked.DecideApproval(ProjectApprovalProcesses.ChangeRequest, ApprovalInstanceStatuses.Cancelled);
        }

        if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        if (underReview)
        {
            var cancelled = await engine.CancelAsync(ProjectApprovalProcesses.ChangeRequest, cr.ProjectId, input.Reason, db, ct).ConfigureAwait(false);
            if (cancelled.IsFailure && cancelled.Error.Kind != ErrorKind.NotFound)
            {
                return cancelled.Error;
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return (await ToDtosAsync(project!, [cr], ct).ConfigureAwait(false))[0];
    }

    /// <summary>
    /// "Apply changes" (Q-P9): only an approved request, only after the PM's confirmation with the values to apply; every change is logged.
    /// </summary>
    public async Task<Result<ChangeRequestDto>> ApplyAsync(Guid id, ChangeApplyInput input, CancellationToken ct)
    {
        var (cr, project, error) = await LoadAsync(id, ct).ConfigureAwait(false);
        if (cr is null)
        {
            return error!;
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project!, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!ChangeLifecycle.Allows(cr.Status, ChangeActions.Apply))
        {
            return ChangeLifecycle.Next(cr.Status, ChangeActions.Apply).Error!;
        }

        var tracked = await db.Projects.FirstAsync(p => p.Id == cr.ProjectId, ct).ConfigureAwait(false);
        if (!Live(tracked) && tracked.Status != ProjectStatuses.OnHold)
        {
            return ProjectService.NotInStage(tracked);
        }

        var v = input.Values;
        var errors = await ApplyErrorsAsync(cr, v, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(cr).Property(x => x.Version).OriginalValue = input.Version;
        var log = new List<ChangeLogEntry>();
        void Log(string field, object? old, object? @new)
        {
            var o = Text(old);
            var n = Text(@new);
            if (o != n)
            {
                log.Add(new ChangeLogEntry(field, o, n));
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        switch (cr.Type)
        {
            case ChangeTypes.Details:
            case ChangeTypes.Postponement:
                Log("nameAr", tracked.NameAr, v.NameAr ?? tracked.NameAr);
                Log("nameEn", tracked.NameEn, v.NameEn ?? tracked.NameEn);
                Log("description", tracked.Description, v.Description ?? tracked.Description);
                Log("scopeOfWork", tracked.ScopeOfWork, v.Scope ?? tracked.ScopeOfWork);
                Log("plannedStart", tracked.PlannedStart, v.PlannedStart ?? tracked.PlannedStart);
                Log("plannedEnd", tracked.PlannedEnd, v.PlannedEnd ?? tracked.PlannedEnd);
                Log("budget", tracked.Budget, v.Budget ?? tracked.Budget);
                if (cr.Type == ChangeTypes.Postponement)
                {
                    log.Add(new ChangeLogEntry("milestones", null, "re-plan by the PM"));
                }

                tracked.ApplyDetails(v.NameAr, v.NameEn, v.Description, v.Scope, v.PlannedStart, v.PlannedEnd, v.Budget);
                if ((v.Methodology ?? cr.ProposedMethodology) is { } methodology && Methodologies.All.Contains(methodology) && methodology != tracked.Methodology)
                {
                    Log("methodology", tracked.Methodology, methodology);
                    tracked.ChangeMethodology(methodology);
                    if (Methodologies.UsesSprints(methodology) && tracked.Board is null)
                    {
                        tracked.SetBoard(BoardConfig.Default);
                    }
                }

                break;
            case ChangeTypes.Ownership:
                if (v.NewPmEmployeeId is { } pm && pm != tracked.PmEmployeeId)
                {
                    Log("pm", tracked.PmEmployeeId, pm);
                    await MoveRoleAsync(tracked.Id, tracked.PmEmployeeId, pm, PortfolioPermissions.PmRole, ct).ConfigureAwait(false);
                    tracked.SetPm(pm, clock.UtcNow);
                }

                if (v.NewOwnerEmployeeId is { } owner && owner != tracked.OwnerEmployeeId)
                {
                    Log("owner", tracked.OwnerEmployeeId, owner);
                    await MoveRoleAsync(tracked.Id, tracked.OwnerEmployeeId, owner, PortfolioPermissions.OwnerRole, ct).ConfigureAwait(false);
                    tracked.SetOwner(owner);
                }

                break;
            case ChangeTypes.Cancellation:
            case ChangeTypes.Merge:
                Log("status", tracked.Status, ProjectStatuses.Cancelled);
                if (cr.Type == ChangeTypes.Merge)
                {
                    Log("mergedInto", null, cr.MergeIntoProjectId);
                    tracked.MergeInto(cr.MergeIntoProjectId!.Value);
                }

                break;
        }

        if (tracked.ReplanChangeRequestId == cr.Id)
        {
            tracked.OpenReplan(null);
            log.Add(new ChangeLogEntry("plan", "re-opened", "closed"));
        }

        if (v.Rebaseline && cr.Type is ChangeTypes.Details or ChangeTypes.Postponement)
        {
            var baseline = await baselines.TakeAsync(tracked, cr.Number, cr.Id, ct).ConfigureAwait(false); // ADR 0094: only here
            log.Add(new ChangeLogEntry("baseline", null, $"v{baseline.VersionNo}"));
        }

        cr.Move(ChangeActions.Apply, clock.UtcNow);
        cr.MarkApplied(JsonSerializer.Serialize(log, LogJson), clock.UtcNow);
        if (cr.Type is ChangeTypes.Cancellation or ChangeTypes.Merge)
        {
            var running = tracked.ApprovalProcess is { } process && tracked.ApprovalResult is null ? process : null;
            var cancelled = await projects.ApplyStatusAsync(tracked, ProjectStatusActions.Cancel, $"{cr.Number}: {cr.Reason}", running, transaction, ct).ConfigureAwait(false);
            if (cancelled.IsFailure)
            {
                return cancelled.Error;
            }
        }
        else
        {
            if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
            {
                return conflict;
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return (await ToDtosAsync(tracked, [cr], ct).ConfigureAwait(false))[0];
    }

    private async Task MoveRoleAsync(Guid projectId, Guid? from, Guid to, string role, CancellationToken ct)
    {
        if (from is { } old)
        {
            await recordRoles.RevokeInTransactionAsync(db, PortfolioPermissions.ProjectResource, projectId, RecordPrincipalKinds.Employee, old, role, ct).ConfigureAwait(false);
        }

        await recordRoles.GrantInTransactionAsync(db, PortfolioPermissions.ProjectResource, projectId, RecordPrincipalKinds.Employee, to, role, ct).ConfigureAwait(false);
    }

    private async Task<List<FieldError>> ValidateAsync(ChangeRequestValues v, Project project, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        void Add(string field, string key) => errors.Add(new FieldError(field, key, key));
        if (!ChangeTypes.All.Contains(v.Type))
        {
            Add("type", "validation.unknownCode");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(v.Reason))
        {
            Add("reason", "validation.notEmpty");
        }

        if ((v.Areas ?? []).Any(a => !AreaCodes.Contains(a)))
        {
            Add("areas", "validation.unknownCode");
        }

        foreach (var (field, text, max) in new[]
                 {
                     ("reason", v.Reason, RequestRules.LongTextMaxLength), ("impactOfNonResponse", v.ImpactOfNonResponse, RequestRules.LongTextMaxLength),
                     ("proposedNameAr", v.ProposedNameAr, RequestRules.NameMaxLength), ("proposedNameEn", v.ProposedNameEn, RequestRules.NameMaxLength),
                     ("proposedDescription", v.ProposedDescription, RequestRules.LongTextMaxLength), ("proposedScope", v.ProposedScope, RequestRules.LongTextMaxLength),
                 })
        {
            if (text is { } t && t.Length > max)
            {
                Add(field, "validation.maximumLength");
            }
        }

        if (v.ProposedStart is { } s && v.ProposedEnd is { } e && e < s)
        {
            Add("proposedEnd", "portfolio.errors.endBeforeStart");
        }

        if (v.ProposedBudget is { } b && (b < 0 || decimal.Round(b, 2) != b))
        {
            Add("proposedBudget", "portfolio.errors.twoDecimals");
        }

        switch (v.Type)
        {
            case ChangeTypes.Details:
                if (v.ProposedNameAr is null && v.ProposedNameEn is null && v.ProposedDescription is null && v.ProposedScope is null && v.ProposedStart is null
                    && v.ProposedEnd is null && v.ProposedBudget is null && v.ProposedMethodology is null && !(v.Areas ?? []).Contains("plan"))
                {
                    Add("areas", "portfolio.errors.changeNothing");
                }

                if (v.ProposedMethodology is { } m && (!Methodologies.All.Contains(m) || m == project.Methodology))
                {
                    Add("proposedMethodology", "validation.unknownCode");
                }

                break;
            case ChangeTypes.Postponement:
                if (v.ProposedEnd is null)
                {
                    Add("proposedEnd", "validation.notEmpty");
                }

                break;
            case ChangeTypes.Ownership:
                if (v.NewPmEmployeeId is null && v.NewOwnerEmployeeId is null)
                {
                    Add("newPmEmployeeId", "portfolio.errors.changeNothing");
                }

                foreach (var (field, person) in new[] { ("newPmEmployeeId", v.NewPmEmployeeId), ("newOwnerEmployeeId", v.NewOwnerEmployeeId) })
                {
                    if (person is { } p && (await reader.PeopleAsync([p], ct).ConfigureAwait(false)).Count == 0)
                    {
                        Add(field, "validation.unknownCode");
                    }
                }

                break;
            case ChangeTypes.Merge:
                if (v.MergeIntoProjectId is not { } target || target == project.Id
                    || await loader.FindAsync(target, context.User, ct).ConfigureAwait(false) is not { Status: ProjectStatuses.Active })
                {
                    Add("mergeIntoProjectId", "portfolio.errors.mergeTarget");
                }

                break;
        }

        return errors;
    }

    private async Task<List<FieldError>> ApplyErrorsAsync(ChangeRequest cr, ChangeApplyValues v, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (v.PlannedStart is { } s && v.PlannedEnd is { } e && e < s)
        {
            errors.Add(new FieldError("plannedEnd", "portfolio.errors.endBeforeStart", "portfolio.errors.endBeforeStart"));
        }

        if (v.Budget is { } b && (b < 0 || decimal.Round(b, 2) != b))
        {
            errors.Add(new FieldError("budget", "portfolio.errors.twoDecimals", "portfolio.errors.twoDecimals"));
        }

        if (v.Rebaseline)
        {
            var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
            if (!settings.Features.Baselines)
            {
                errors.Add(new FieldError("rebaseline", "portfolio.errors.featureOff", "portfolio.errors.featureOff"));
            }
            else if (cr.Type is not (ChangeTypes.Details or ChangeTypes.Postponement))
            {
                errors.Add(new FieldError("rebaseline", "portfolio.errors.rebaselineType", "portfolio.errors.rebaselineType"));
            }
            else if ((JsonSerializer.Deserialize<List<string>>(cr.Areas) ?? []).Contains("plan"))
            {
                var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == cr.ProjectId, ct).ConfigureAwait(false);
                var facts = await loader.FactsAsync(project, ct).ConfigureAwait(false);
                foreach (var item in ProjectGates.Planning(facts).Where(i => !i.Met && i.Code is "weights" or "epics" or "hybrid-epics"))
                {
                    errors.Add(new FieldError($"gate.{item.Code}", item.MessageKey, item.MessageKey, item.Params));
                }
            }
        }

        if (v.Methodology is { } methodology && !Methodologies.All.Contains(methodology))
        {
            errors.Add(new FieldError("methodology", "validation.unknownCode", "validation.unknownCode"));
        }

        if (cr.Type == ChangeTypes.Ownership)
        {
            if (v.NewPmEmployeeId is null && v.NewOwnerEmployeeId is null)
            {
                errors.Add(new FieldError("newPmEmployeeId", "portfolio.errors.changeNothing", "portfolio.errors.changeNothing"));
            }

            foreach (var (field, person) in new[] { ("newPmEmployeeId", v.NewPmEmployeeId), ("newOwnerEmployeeId", v.NewOwnerEmployeeId) })
            {
                if (person is { } p && (await reader.PeopleAsync([p], ct).ConfigureAwait(false)).Count == 0)
                {
                    errors.Add(new FieldError(field, "validation.unknownCode", "validation.unknownCode"));
                }
            }
        }

        return errors;
    }

    private static bool Live(Project p) => p.Status == ProjectStatuses.Active && p.SubState != SubStates.Done;

    private static string Areas(ChangeRequestValues v) => JsonSerializer.Serialize((v.Areas ?? []).Distinct().ToList());

    private static readonly JsonSerializerOptions LogJson = new(JsonSerializerDefaults.Web);

    private static string? Text(object? value) => value switch
    {
        null => null,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private async Task<(ChangeRequest? Cr, Project? Project, Error? Error)> LoadAsync(Guid id, CancellationToken ct, bool tracked = true)
    {
        var cr = tracked
            ? await db.ChangeRequests.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false)
            : await db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (cr is null)
        {
            return (null, null, Error.NotFound());
        }

        var project = await loader.FindAsync(cr.ProjectId, context.User, ct).ConfigureAwait(false);
        return project is null ? (null, null, Error.NotFound()) : (cr, project, null);
    }

    private async Task<Error?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }
    }

    private async Task<Result<ChangeRequestDto>> SaveAndReturnAsync(ChangeRequest cr, Project project, CancellationToken ct) =>
        await SaveAsync(ct).ConfigureAwait(false) is { } conflict ? conflict : (await ToDtosAsync(project, [cr], ct).ConfigureAwait(false))[0];

    private async Task<IReadOnlyList<ChangeRequestDto>> ToDtosAsync(Project project, IReadOnlyList<ChangeRequest> rows, CancellationToken ct)
    {
        var people = await reader.PeopleAsync(rows.SelectMany(r => new[] { r.NewPmEmployeeId, r.NewOwnerEmployeeId, r.RequestedByEmployeeId }), ct).ConfigureAwait(false);
        var targets = rows.Where(r => r.MergeIntoProjectId is not null).Select(r => r.MergeIntoProjectId!.Value).Distinct().ToList();
        var numbers = await db.Projects.AsNoTracking().Where(p => targets.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, ct).ConfigureAwait(false);
        var canChange = await loader.CanAsync(context.User, PortfolioPermissions.ProjectChange, project, ct).ConfigureAwait(false);
        var canEdit = await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false);
        var show = await costs.CanSeeAsync(context.User, project, ct).ConfigureAwait(false);
        return rows.Select(c => new ChangeRequestDto(
            c.Id, c.ProjectId, c.Number, c.Type, JsonSerializer.Deserialize<List<string>>(c.Areas) ?? [], c.Reason, c.ImpactOfNonResponse, c.ProposedNameAr,
            c.ProposedNameEn, c.ProposedDescription, c.ProposedScope, c.ProposedStart, c.ProposedEnd, show ? c.ProposedBudget : null, c.CostAffected,
            c.NewPmEmployeeId is { } pm ? people.GetValueOrDefault(pm) : null, c.NewOwnerEmployeeId is { } o ? people.GetValueOrDefault(o) : null, c.MergeIntoProjectId,
            c.MergeIntoProjectId is { } t ? numbers.GetValueOrDefault(t) : null, c.RequestedDate, c.Status,
            c.RequestedByEmployeeId is { } r ? people.GetValueOrDefault(r) : null, c.SubmittedAt, c.DecidedAt, c.ImplementedAt,
            (JsonSerializer.Deserialize<List<ChangeLogEntry>>(c.Applied, LogJson) ?? []).Where(e => show || e.Field != "budget").ToList(), c.CancelReason,
            canChange && c.Status == ChangeStatuses.Draft, canEdit && c.Status == ChangeStatuses.Approved, c.CreatedAt, c.Version, c.ProposedMethodology, !show)).ToList();
    }
}
