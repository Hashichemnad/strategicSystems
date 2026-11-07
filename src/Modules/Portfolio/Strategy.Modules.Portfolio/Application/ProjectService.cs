using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio.Application;

public sealed record ProjectPermissionsDto(
    bool CanEdit, bool CanContribute, bool CanChange, bool CanManage, bool CanLpo, bool CanTakeOwnership, bool CanEditCharter, bool CanEditContent,
    bool CanPlanMilestones,
    bool CanRecall);

public sealed record ProjectApprovalDto(string? Process, string? StepCode, string? StepNameAr, string? StepNameEn, string? Result);

public sealed record ProjectGatesDto(IReadOnlyList<GateItem> Charter, IReadOnlyList<GateItem> Planning, bool CharterMandatoryDone, bool PlanningMandatoryDone);

public sealed record ProjectCountsDto(
    int Kpis, int Risks, int Stakeholders, int Members, int PlanningItems, int Milestones, int Challenges, int Lessons, int Meetings, int Contracts, int ChangeRequests);

public sealed record ProjectFullDto(
    Guid Id,
    string Number,
    string Classification,
    string? NameAr,
    string? NameEn,
    string? Description,
    string? ScopeOfWork,
    string Stage,
    string SubState,
    string Status,
    StageTemplate Template,
    IReadOnlyList<string> StagePath,
    PortfolioUnitDto? Unit,
    PortfolioUnitDto? SectionUnit,
    string? StrategicObjectiveCode,
    string? ProgramTypeCode,
    IReadOnlyList<string> OperationalThemes,
    ProjectCharterValues Charter,
    decimal? Budget,
    decimal? Year1PlannedCost,
    decimal? Year2PlannedCost,
    decimal? Year3PlannedCost,
    decimal? Year4PlannedCost,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    PortfolioPersonDto? Owner,
    PortfolioPersonDto? Pm,
    PortfolioPersonDto? Requester,
    DateTimeOffset? OwnershipTakenAt,
    bool LpoUploaded,
    Guid? TeamId,
    DateTimeOffset? TeamFormedAt,
    ProjectApprovalDto Approval,
    decimal Performance,
    decimal PlannedProgress,
    string ProgressStatus,
    string? SummaryOfAchievement,
    string? MainDeliverables,
    decimal? CostPerformance,
    int? CommunitySatisfaction,
    int? EmployeeSatisfaction,
    int? VendorSatisfaction,
    decimal? PreviousOperationalCost,
    string? ImpactNotes,
    DateTimeOffset? UpdateOpenedAt,
    DateTimeOffset? MonthlyApprovedAt,
    string? CancelReason,
    string? HoldReason,
    Guid? MergedIntoProjectId,
    DateTimeOffset? CompletedAt,
    Guid RequestId,
    string? RequestNumber,
    ProjectGatesDto Gates,
    ProjectCountsDto Counts,
    ProjectPermissionsDto Permissions,
    DateTimeOffset CreatedAt,
    uint Version,
    ProjectDeliveryDto Delivery);

/// <summary>Phase 10 additions of the project record: methodology, grouping, re-plan, cost visibility (ADRs 0090, 0094, 0095, 0098).</summary>
public sealed record ProjectDeliveryDto(
    string Methodology, string ApprovalMode, int? SprintLengthDays, string? DefinitionOfDone, bool CanSetMethodology, Guid? PortfolioGroupId, Guid? ProgrammeId,
    Guid? ReplanChangeRequestId, bool CostsHidden);

public sealed record ProjectVersionInput(uint Version, string? Comment = null);

public sealed record ProjectReasonInput(string? Reason, uint Version);

public sealed record AssignPmInput(Guid EmployeeId, uint Version);

public sealed record CharterInput(ProjectCharterValues Values, uint Version);

public sealed record ClosureInput(string? SummaryOfAchievement, string? MainDeliverables, uint Version);

public sealed record ImpactInput(decimal? CostPerformance, int? CommunitySatisfaction, int? EmployeeSatisfaction, int? VendorSatisfaction, decimal? PreviousOperationalCost,
    string? ImpactNotes, uint Version);

public sealed record ProjectFileDto(Guid Id, string Kind, string Name, string ContentType, long SizeBytes, int VersionNo, DateTimeOffset UpdatedAt);

/// <summary>Project lifecycle commands (docs/design/portfolio.md §16–§18). Every command: 404 outside read scope, 403 without the permission.</summary>
internal sealed class ProjectService(
    PortfolioDbContext db,
    ProjectLoader loader,
    RequestReader reader,
    ProjectNotifier notifier,
    IApprovalEngine engine,
    IRecordRoleService recordRoles,
    IFileService files,
    ILookupService lookups,
    StrategicObjectives objectives,
    IIntegrationEventPublisher events,
    IRequestContext context,
    IClock clock,
    AgileService agile,
    CostVisibility costs)
{
    public async Task<Result<ProjectFullDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var project = await loader.FindAsync(id, context.User, ct).ConfigureAwait(false);
        return project is null ? Error.NotFound() : await ToDtoAsync(project, ct).ConfigureAwait(false);
    }

    public async Task<Result<ProjectFullDto>> TakeOwnershipAsync(Guid id, uint version, CancellationToken ct)
    {
        var project = await loader.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (context.User.EmployeeIdOf() is not { } me)
        {
            return Error.Forbidden("portfolio.project.noEmployee");
        }

        if (!await loader.InSectionAsync(context.User, project, ct).ConfigureAwait(false)
            && !await loader.CanAsync(context.User, PortfolioPermissions.ProjectManage, project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        return await SetPmAsync(project, me, version, ct).ConfigureAwait(false);
    }

    public async Task<Result<ProjectFullDto>> AssignPmAsync(Guid id, Guid employeeId, uint version, CancellationToken ct)
    {
        var project = await loader.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectManage, project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if ((await reader.PeopleAsync([employeeId], ct).ConfigureAwait(false)).Count == 0)
        {
            return Error.Validation([new FieldError("employeeId", "validation.unknownCode", "validation.unknownCode")]);
        }

        return await SetPmAsync(project, employeeId, version, ct).ConfigureAwait(false);
    }

    private async Task<Result<ProjectFullDto>> SetPmAsync(Project project, Guid employeeId, uint version, CancellationToken ct)
    {
        var template = await loader.EnsureTemplateAsync(project, ct).ConfigureAwait(false);
        db.Entry(project).Property(x => x.Version).OriginalValue = version;
        var applied = project.Apply(ProjectActions.TakeOwnership, template, clock.UtcNow);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        project.SetPm(employeeId, clock.UtcNow);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        await recordRoles.GrantInTransactionAsync(db, PortfolioPermissions.ProjectResource, project.Id, RecordPrincipalKinds.Employee, employeeId, PortfolioPermissions.PmRole, ct)
            .ConfigureAwait(false);
        await notifier.ToEmployeesAsync(PortfolioNotifications.OwnershipTaken, project, [employeeId], db, ct, stage: ProjectNotifier.StageText(project.Stage))
            .ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await ToDtoAsync(project, ct).ConfigureAwait(false);
    }

    public async Task<Result<ProjectFullDto>> EditCharterAsync(Guid id, ProjectCharterValues values, uint version, CancellationToken ct)
    {
        var (project, error) = await EditableAsync(id, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (!ProjectLifecycle.CanEditCharter(project.Status, project.SubState))
        {
            return NotInStage(project);
        }

        var errors = await CharterErrorsAsync(values, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(project).Property(x => x.Version).OriginalValue = version;
        project.EditCharter(values, JsonSerializer.Serialize((values.OperationalThemes ?? []).Distinct().ToList()));
        return await SaveAndReturnAsync(project, ct).ConfigureAwait(false);
    }

    private async Task<List<FieldError>> CharterErrorsAsync(ProjectCharterValues v, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        void Add(string field, string key, IReadOnlyDictionary<string, object?>? p = null) => errors.Add(new FieldError(field, key, key, p));
        RequestRules.RichLength(errors, "description", v.Description);
        foreach (var (field, text, max) in new[]
                 {
                     ("nameAr", v.NameAr, RequestRules.NameMaxLength), ("nameEn", v.NameEn, RequestRules.NameMaxLength),
                     ("scopeOfWork", v.ScopeOfWork, RequestRules.LongTextMaxLength),
                     ("structuralChanges", v.StructuralChanges, RequestRules.LongTextMaxLength), ("resourcesHuman", v.ResourcesHuman, RequestRules.LongTextMaxLength),
                     ("resourcesFinancial", v.ResourcesFinancial, RequestRules.LongTextMaxLength), ("resourcesTechnical", v.ResourcesTechnical, RequestRules.LongTextMaxLength),
                     ("resourcesContract", v.ResourcesContract, RequestRules.LongTextMaxLength), ("resourcesOther", v.ResourcesOther, RequestRules.LongTextMaxLength),
                 })
        {
            if (text is { } t && t.Length > max)
            {
                Add(field, "validation.maximumLength", new Dictionary<string, object?> { ["max"] = max });
            }
        }

        foreach (var (field, amount) in new[]
                 {
                     ("year1ContractedCost", v.Year1ContractedCost), ("year2ContractedCost", v.Year2ContractedCost), ("year3ContractedCost", v.Year3ContractedCost),
                     ("year4ContractedCost", v.Year4ContractedCost), ("year1ActualCost", v.Year1ActualCost), ("year2ActualCost", v.Year2ActualCost),
                     ("year3ActualCost", v.Year3ActualCost), ("year4ActualCost", v.Year4ActualCost), ("contractedInitialCost", v.ContractedInitialCost),
                     ("annualOperationCost", v.AnnualOperationCost), ("annualMaintenanceCost", v.AnnualMaintenanceCost), ("annualLicenceCost", v.AnnualLicenceCost),
                     ("annualOtherCost", v.AnnualOtherCost),
                 })
        {
            if (amount is { } a && (a < 0 || decimal.Round(a, 2) != a))
            {
                Add(field, "portfolio.errors.twoDecimals");
            }
        }

        if (v.PlannedStart is { } s && v.PlannedEnd is { } e && e < s)
        {
            Add("plannedEnd", "portfolio.errors.endBeforeStart");
        }

        if (!string.IsNullOrWhiteSpace(v.StrategicObjectiveCode) && !await objectives.IsActiveAsync(v.StrategicObjectiveCode.Trim(), ct).ConfigureAwait(false))
        {
            Add("strategicObjectiveCode", "validation.unknownCode");
        }

        foreach (var theme in v.OperationalThemes ?? [])
        {
            if (!await lookups.IsActiveCodeAsync(PortfolioPermissions.ThemeLookup, theme, ct).ConfigureAwait(false))
            {
                Add("operationalThemes", "validation.unknownCode");
                break;
            }
        }

        return errors;
    }

    public async Task<Result<ProjectFullDto>> EditClosureAsync(Guid id, ClosureInput input, CancellationToken ct)
    {
        var (project, error) = await EditableAsync(id, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (project.SubState != SubStates.ClosureDraft)
        {
            return NotInStage(project);
        }

        if ((input.SummaryOfAchievement?.Length ?? 0) > RequestRules.LongTextMaxLength || (input.MainDeliverables?.Length ?? 0) > RequestRules.LongTextMaxLength)
        {
            return Error.Validation([new FieldError("summaryOfAchievement", "validation.maximumLength", "validation.maximumLength")]);
        }

        db.Entry(project).Property(x => x.Version).OriginalValue = input.Version;
        project.EditClosure(input.SummaryOfAchievement, input.MainDeliverables);
        return await SaveAndReturnAsync(project, ct).ConfigureAwait(false);
    }

    public async Task<Result<ProjectFullDto>> EditImpactAsync(Guid id, ImpactInput input, CancellationToken ct)
    {
        var (project, error) = await EditableAsync(id, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (project.SubState != SubStates.ImpactDraft)
        {
            return NotInStage(project);
        }

        var errors = new List<FieldError>();
        foreach (var (field, value) in new[] { ("communitySatisfaction", input.CommunitySatisfaction), ("employeeSatisfaction", input.EmployeeSatisfaction),
                     ("vendorSatisfaction", input.VendorSatisfaction) })
        {
            if (value is < 1 or > 5)
            {
                errors.Add(new FieldError(field, "validation.inclusiveBetween", "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = 1, ["to"] = 5 }));
            }
        }

        if (input.CostPerformance is < 0 or > 10)
        {
            errors.Add(new FieldError("costPerformance", "validation.inclusiveBetween", "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = 0, ["to"] = 10 }));
        }

        if (input.PreviousOperationalCost is { } c && (c < 0 || decimal.Round(c, 2) != c))
        {
            errors.Add(new FieldError("previousOperationalCost", "portfolio.errors.twoDecimals", "portfolio.errors.twoDecimals"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(project).Property(x => x.Version).OriginalValue = input.Version;
        project.EditImpact(input.CostPerformance, input.CommunitySatisfaction, input.EmployeeSatisfaction, input.VendorSatisfaction, input.PreviousOperationalCost,
            input.ImpactNotes);
        return await SaveAndReturnAsync(project, ct).ConfigureAwait(false);
    }

    /// <summary>Submit the charter, planning, closure or impact (gate → lifecycle → approval engine in one transaction).</summary>
    public async Task<Result<ProjectFullDto>> SubmitStageAsync(Guid id, string process, uint version, string? comment, CancellationToken ct)
    {
        var (project, error) = await EditableAsync(id, PortfolioPermissions.ProjectEdit, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var template = await loader.EnsureTemplateAsync(project, ct).ConfigureAwait(false);
        var action = process switch
        {
            ProjectApprovalProcesses.Charter => ProjectActions.SubmitCharter,
            ProjectApprovalProcesses.Planning => ProjectActions.SubmitPlanning,
            ProjectApprovalProcesses.Closure => ProjectActions.SubmitClosure,
            _ => ProjectActions.SubmitImpact,
        };
        if (!ProjectLifecycle.Allows(project.SubState, action) || project.Status != ProjectStatuses.Active)
        {
            return ProjectLifecycle.Next(project.Status, project.SubState, action, template).Error!;
        }

        var facts = await loader.FactsAsync(project, ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        Error? gate = process switch
        {
            ProjectApprovalProcesses.Charter => ProjectGates.Failure([.. ProjectGates.Charter(facts), .. ProjectExtrasService.GateChecklist(project, settings, Gates.Charter)]),
            ProjectApprovalProcesses.Planning => ProjectGates.Failure([.. ProjectGates.Planning(facts), .. ProjectExtrasService.GateChecklist(project, settings, Gates.Planning)]),
            ProjectApprovalProcesses.Closure => await ClosureGateAsync(project, settings, ct).ConfigureAwait(false),
            _ => await ImpactGateAsync(project, settings, ct).ConfigureAwait(false),
        };
        if (gate is not null)
        {
            return gate;
        }

        db.Entry(project).Property(x => x.Version).OriginalValue = version;
        project.Apply(action, template, clock.UtcNow);
        project.EnterApproval(process, null, null, null);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        var started = await engine.SubmitAsync(new ApprovalSubmission(process, Subject(project), Variables(project), comment), db, ct).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        await db.Entry(project).ReloadAsync(ct).ConfigureAwait(false);
        return await ToDtoAsync(project, ct).ConfigureAwait(false);
    }

    private async Task<Error?> ClosureGateAsync(Project project, PortfolioSettings settings, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        foreach (var item in ProjectExtrasService.GateChecklist(project, settings, Gates.Closure).Where(i => !i.Met))
        {
            errors.Add(new FieldError($"gate.{item.Code}", item.MessageKey, item.MessageKey, item.Params));
        }

        if (settings.Features.Quality && await db.Milestones.AnyAsync(
                m => m.ProjectId == project.Id && m.IsDeliverable && m.Status != MilestoneStatuses.Cancelled && m.QualityStatus != "accepted", ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("gate.quality", "portfolio.errors.gateQuality", "portfolio.errors.gateQuality"));
        }

        if (string.IsNullOrWhiteSpace(project.SummaryOfAchievement))
        {
            errors.Add(new FieldError("summaryOfAchievement", "validation.notEmpty", "validation.notEmpty"));
        }

        if (string.IsNullOrWhiteSpace(project.MainDeliverables))
        {
            errors.Add(new FieldError("mainDeliverables", "validation.notEmpty", "validation.notEmpty"));
        }

        if (!await db.Lessons.AnyAsync(l => l.ProjectId == project.Id && l.Status == ItemStatuses.Active, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("gate.lessons", "portfolio.errors.gateLessons", "portfolio.errors.gateLessons"));
        }

        return errors.Count == 0 ? null : Error.Validation(errors);
    }

    private async Task<Error?> ImpactGateAsync(Project project, PortfolioSettings settings, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (settings.Features.Benefits && await db.Benefits.AnyAsync(
                b => b.ProjectId == project.Id && b.ItemStatus == ItemStatuses.Active && b.Status == BenefitStatuses.Planned, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("gate.benefits", "portfolio.errors.gateBenefits", "portfolio.errors.gateBenefits"));
        }

        foreach (var (field, missing) in new[]
                 {
                     ("costPerformance", project.CostPerformance is null), ("communitySatisfaction", project.CommunitySatisfaction is null),
                     ("employeeSatisfaction", project.EmployeeSatisfaction is null), ("vendorSatisfaction", project.VendorSatisfaction is null),
                 })
        {
            if (missing)
            {
                errors.Add(new FieldError(field, "validation.notEmpty", "validation.notEmpty"));
            }
        }

        return errors.Count == 0 ? null : Error.Validation(errors);
    }

    public static ApprovalSubject Subject(Project p) =>
        new(PortfolioPermissions.ProjectResource, p.Id, p.Number, p.NameAr, p.NameEn, p.PmEmployeeId, null, p.SectionUnitId ?? p.UnitId);

    public static Dictionary<string, object?> Variables(Project p) => new()
    {
        [ProjectApprovalProcesses.ClassificationVariable] = p.Classification,
        [ProjectApprovalProcesses.BudgetVariable] = p.Budget ?? 0m,
    };

    /// <summary>Administration confirms the LPO (≥ 1 LPO file) → team formation or planning; PM Office notified (W2.4).</summary>
    public async Task<Result<ProjectFullDto>> LpoUploadedAsync(Guid id, uint version, CancellationToken ct)
    {
        var project = await loader.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectLpo, project, ct).ConfigureAwait(false)
            && !await loader.CanAsync(context.User, PortfolioPermissions.ProjectManage, project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var template = await loader.EnsureTemplateAsync(project, ct).ConfigureAwait(false);
        if ((await files.ListAsync(PortfolioPermissions.ProjectResource, project.Id.ToString("D"), ProjectFilePurposes.Lpo.Key, ct).ConfigureAwait(false)).Count == 0)
        {
            return Error.Validation([new FieldError("lpo", "portfolio.errors.lpoMissing", "portfolio.errors.lpoMissing")]);
        }

        db.Entry(project).Property(x => x.Version).OriginalValue = version;
        var applied = project.Apply(ProjectActions.LpoUploaded, template, clock.UtcNow);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        project.MarkLpoUploaded();
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        if (project.SubState == SubStates.TeamFormation)
        {
            await notifier.ToRoleAsync(PortfolioNotifications.LpoUploaded, project, PortfolioPermissions.PmOfficeRoleKey, db, ct).ConfigureAwait(false);
        }
        else
        {
            await notifier.ToEmployeesAsync(PortfolioNotifications.StageChanged, project, [project.PmEmployeeId], db, ct, stage: ProjectNotifier.StageText(project.Stage))
                .ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await ToDtoAsync(project, ct).ConfigureAwait(false);
    }

    /// <summary>Initiative at 100 %: the PM Office confirms → Completed (Q-P2, ADR 0070).</summary>
    public async Task<Result<ProjectFullDto>> ConfirmCompletionAsync(Guid id, uint version, CancellationToken ct)
    {
        var (project, error) = await EditableAsync(id, PortfolioPermissions.ProjectManage, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var template = await loader.EnsureTemplateAsync(project, ct).ConfigureAwait(false);
        db.Entry(project).Property(x => x.Version).OriginalValue = version;
        var applied = project.Apply(ProjectActions.ConfirmCompletion, template, clock.UtcNow);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        await events.PublishAsync(new LinkedRecordClosed(PortfolioPermissions.ProjectResource, project.Id, "completed", clock.UtcNow), db, ct).ConfigureAwait(false);
        await notifier.ToEmployeesAsync(PortfolioNotifications.StageChanged, project, [project.PmEmployeeId], db, ct, stage: ProjectNotifier.StageText(project.Stage))
            .ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await ToDtoAsync(project, ct).ConfigureAwait(false);
    }

    /// <summary>Hold / resume / cancel with a reason (W8). Hold is refused while an approval runs; cancel stops it.</summary>
    public async Task<Result<ProjectFullDto>> ChangeStatusAsync(Guid id, string action, string? reason, uint version, CancellationToken ct)
    {
        var project = await loader.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectManage, project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (action != ProjectStatusActions.Resume && string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]);
        }

        if ((reason?.Length ?? 0) > RequestRules.TextMaxLength)
        {
            return Error.Validation([new FieldError("reason", "validation.maximumLength", "validation.maximumLength")]);
        }

        var running = project.ApprovalProcess is { } process && project.ApprovalResult is null ? process : null;
        if (action == ProjectStatusActions.Hold && running is not null)
        {
            // The UI offers "Recall" from this error: recall the pending approval first, then hold.
            return ApprovalRunning(running);
        }

        db.Entry(project).Property(x => x.Version).OriginalValue = version;
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        return await ApplyStatusAsync(project, action, reason, running, transaction, ct).ConfigureAwait(false);
    }

    /// <summary>Refusal while an approval runs; <c>recall</c> tells the client to offer the Recall action.</summary>
    public static Error ApprovalRunning(string process) =>
        Error.Rule("portfolio.project.approvalRunning", "portfolio.errors.approvalInProgress",
            new Dictionary<string, object?> { ["process"] = process, ["action"] = "recall" });

    /// <summary>
    /// Can <paramref name="project"/>'s pending approval be recalled by the caller? The PM Office always; the PM (submitter of stage and monthly
    /// approvals); for a change request the PM or the person who raised it.
    /// </summary>
    public async Task<bool> CanRecallAsync(Project project, CancellationToken ct)
    {
        if (project.ApprovalProcess is not { } process || project.ApprovalResult is not null)
        {
            return false;
        }

        var user = context.User;
        if (await loader.CanAsync(user, PortfolioPermissions.ProjectManage, project, ct).ConfigureAwait(false)
            || await loader.CanAsync(user, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false))
        {
            return true;
        }

        return process == ProjectApprovalProcesses.ChangeRequest && user.EmployeeIdOf() is { } me
            && await db.ChangeRequests.AnyAsync(c => c.ProjectId == project.Id && c.Status == ChangeStatuses.UnderReview && c.RequestedByEmployeeId == me, ct)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Recall a pending approval with a reason (charter, planning, monthly update, closure, impact, change request): the instance is cancelled and
    /// the record goes back to where it was before submission, so it can be corrected or the project put on hold.
    /// </summary>
    public async Task<Result<ProjectFullDto>> RecallAsync(Guid id, string? reason, uint version, CancellationToken ct)
    {
        var project = await loader.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (project.ApprovalProcess is not { } process || project.ApprovalResult is not null)
        {
            return Error.Rule("portfolio.project.nothingToRecall", "portfolio.errors.nothingToRecall");
        }

        if (!await CanRecallAsync(project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]);
        }

        if (reason.Length > RequestRules.TextMaxLength)
        {
            return Error.Validation([new FieldError("reason", "validation.maximumLength", "validation.maximumLength")]);
        }

        var template = await loader.EnsureTemplateAsync(project, ct).ConfigureAwait(false);
        db.Entry(project).Property(x => x.Version).OriginalValue = version;
        switch (process)
        {
            case ProjectApprovalProcesses.MonthlyUpdate:
                {
                    var window = await db.MonthlyUpdates.Where(m => m.ProjectId == project.Id && m.Status == MonthlyStatuses.Submitted).FirstOrDefaultAsync(ct)
                        .ConfigureAwait(false);
                    window?.Return();
                    foreach (var m in await db.Milestones.Where(m => m.ProjectId == project.Id && m.UpdateState == UpdateStates.PendingApproval).ToListAsync(ct)
                                 .ConfigureAwait(false))
                    {
                        m.ReturnUpdate();
                    }

                    await agile.ReturnCarriedAsync(project.Id, clock.UtcNow, ct).ConfigureAwait(false);

                    break;
                }

            case ProjectApprovalProcesses.ChangeRequest:
                {
                    var cr = await db.ChangeRequests.FirstOrDefaultAsync(c => c.ProjectId == project.Id && c.Status == ChangeStatuses.UnderReview, ct).ConfigureAwait(false);
                    cr?.Move(ChangeActions.Return, clock.UtcNow);
                    break;
                }

            case ProjectApprovalProcesses.SprintReview:
                await agile.ReturnCarriedAsync(project.Id, clock.UtcNow, ct).ConfigureAwait(false);
                break;

            default:
                {
                    var applied = project.Apply(ProjectActions.ApprovalReturned, template, clock.UtcNow);
                    if (applied.IsFailure)
                    {
                        return applied.Error;
                    }

                    break;
                }
        }

        project.DecideApproval(process, ProjectApprovalResults.Recalled);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        var cancelled = await engine.CancelAsync(process, project.Id, reason.Trim(), db, ct).ConfigureAwait(false);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        await db.Entry(project).ReloadAsync(ct).ConfigureAwait(false);
        return await ToDtoAsync(project, ct).ConfigureAwait(false);
    }

    /// <summary>Shared by hold / cancel and the change-request apply (cancellation, merge). Commits the given transaction.</summary>
    public async Task<Result<ProjectFullDto>> ApplyStatusAsync(
        Project project, string action, string? reason, string? runningProcess, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction, CancellationToken ct)
    {
        var applied = project.ApplyStatus(action, reason?.Trim(), clock.UtcNow);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        if (action == ProjectStatusActions.Cancel && runningProcess is not null)
        {
            project.DecideApproval(runningProcess, ApprovalInstanceStatuses.Cancelled);
        }

        if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        if (action == ProjectStatusActions.Cancel)
        {
            if (runningProcess is not null)
            {
                var cancelled = await engine.CancelAsync(runningProcess, project.Id, reason ?? "cancelled", db, ct).ConfigureAwait(false);
                if (cancelled.IsFailure && cancelled.Error.Kind != ErrorKind.NotFound)
                {
                    return cancelled.Error;
                }
            }

            foreach (var cr in await db.ChangeRequests.Where(c => c.ProjectId == project.Id && (c.Status == ChangeStatuses.Draft || c.Status == ChangeStatuses.Approved))
                         .ToListAsync(ct).ConfigureAwait(false))
            {
                cr.Move(ChangeActions.Cancel, clock.UtcNow, reason);
            }

            await events.PublishAsync(new LinkedRecordClosed(PortfolioPermissions.ProjectResource, project.Id, "cancelled", clock.UtcNow), db, ct).ConfigureAwait(false);
        }

        var template = action switch
        {
            ProjectStatusActions.Hold => PortfolioNotifications.ProjectOnHold,
            ProjectStatusActions.Cancel => PortfolioNotifications.ProjectCancelled,
            _ => null,
        };
        if (template is not null)
        {
            await notifier.ToEmployeesAsync(template, project, [project.PmEmployeeId, project.OwnerEmployeeId], db, ct, reason, context.User.Identity?.Name)
                .ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return await ToDtoAsync(project, ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<ProjectFileDto>>> ListFilesAsync(Guid id, CancellationToken ct)
    {
        if (await loader.FindAsync(id, context.User, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        var all = await files.ListAsync(PortfolioPermissions.ProjectResource, id.ToString("D"), cancellationToken: ct).ConfigureAwait(false);
        return all.Select(f => new ProjectFileDto(f.Id, ProjectFilePurposes.KindOf(f.Purpose), f.Name, f.ContentType, f.SizeBytes, f.VersionNo, f.UpdatedAt)).ToList();
    }

    public async Task<Result<ProjectFileDto>> UploadFileAsync(Guid id, string kind, string fileName, Stream content, string? comment, CancellationToken ct)
    {
        var project = await loader.FindAsync(id, context.User, ct).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (ProjectFilePurposes.ByKind(kind) is not { } purpose)
        {
            return Error.Validation([new FieldError("kind", "validation.unknownCode", "validation.unknownCode")]);
        }

        var allowed = await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false)
            || (purpose == ProjectFilePurposes.Lpo && await loader.CanAsync(context.User, PortfolioPermissions.ProjectLpo, project, ct).ConfigureAwait(false));
        if (!allowed)
        {
            return Error.Forbidden();
        }

        if (project.Status != ProjectStatuses.Active)
        {
            return NotInStage(project);
        }

        var uploaded = await files.UploadAsync(new FileUpload(purpose.Key, project.Id.ToString("D"), fileName, content, comment), ct).ConfigureAwait(false);
        return uploaded.IsFailure
            ? uploaded.Error
            : new ProjectFileDto(uploaded.Value.Id, kind, uploaded.Value.Name, uploaded.Value.ContentType, uploaded.Value.SizeBytes, uploaded.Value.VersionNo,
                uploaded.Value.UpdatedAt);
    }

    /// <summary>Loads a tracked project the caller may act on with <paramref name="permission"/> (404 / 403).</summary>
    public async Task<(Project? Project, Error? Error)> EditableAsync(Guid id, string permission, CancellationToken ct)
    {
        var project = await loader.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return (null, Error.NotFound());
        }

        if (!await loader.CanAsync(context.User, permission, project, ct).ConfigureAwait(false))
        {
            return (null, Error.Forbidden());
        }

        return (project, null);
    }

    public static Error NotInStage(Project project) =>
        Error.Rule("portfolio.project.stage", "portfolio.errors.notAllowedInStage",
            new Dictionary<string, object?> { ["status"] = project.Status, ["stage"] = project.SubState });

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

    private async Task<Result<ProjectFullDto>> SaveAndReturnAsync(Project project, CancellationToken ct) =>
        await SaveAsync(ct).ConfigureAwait(false) is { } conflict ? conflict : await ToDtoAsync(project, ct).ConfigureAwait(false);

    public async Task<ProjectFullDto> ToDtoAsync(Project p, CancellationToken ct)
    {
        var user = context.User;
        var template = await loader.TemplateAsync(p, ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync([p.OwnerEmployeeId, p.RequesterEmployeeId, p.PmEmployeeId], ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync([p.UnitId, p.SectionUnitId], ct).ConfigureAwait(false);
        var requestNumber = await db.Requests.AsNoTracking().Where(r => r.Id == p.RequestId).Select(r => r.Number).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var facts = await loader.FactsAsync(p, ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<GateItem> charter = [.. ProjectGates.Charter(facts), .. ProjectExtrasService.GateChecklist(p, settings, Gates.Charter)];
        IReadOnlyList<GateItem> planning = [.. ProjectGates.Planning(facts), .. ProjectExtrasService.GateChecklist(p, settings, Gates.Planning)];
        var showCosts = await costs.CanSeeAsync(user, p, ct).ConfigureAwait(false);
        decimal? C(decimal? value) => showCosts ? value : null;
        var id = p.Id;
        var counts = new ProjectCountsDto(
            facts.ActiveKpis, facts.ActiveRisks, facts.ActiveStakeholders, facts.ProposedMembers,
            await db.PlanningItems.CountAsync(x => x.ProjectId == id && x.Status == ItemStatuses.Active, ct).ConfigureAwait(false),
            await db.Milestones.CountAsync(x => x.ProjectId == id && x.Status != MilestoneStatuses.Cancelled, ct).ConfigureAwait(false),
            await db.Challenges.CountAsync(x => x.ProjectId == id && x.Status == ItemStatuses.Active, ct).ConfigureAwait(false),
            await db.Lessons.CountAsync(x => x.ProjectId == id && x.Status == ItemStatuses.Active, ct).ConfigureAwait(false),
            await db.Meetings.CountAsync(x => x.ProjectId == id && x.Status == ItemStatuses.Active, ct).ConfigureAwait(false),
            await db.Contracts.CountAsync(x => x.ProjectId == id && x.Status == ItemStatuses.Active, ct).ConfigureAwait(false),
            await db.ChangeRequests.CountAsync(x => x.ProjectId == id, ct).ConfigureAwait(false));
        var canEdit = await loader.CanAsync(user, PortfolioPermissions.ProjectEdit, p, ct).ConfigureAwait(false);
        var canManage = await loader.CanAsync(user, PortfolioPermissions.ProjectManage, p, ct).ConfigureAwait(false);
        var permissions = new ProjectPermissionsDto(
            canEdit,
            await loader.CanAsync(user, PortfolioPermissions.ProjectContribute, p, ct).ConfigureAwait(false),
            await loader.CanAsync(user, PortfolioPermissions.ProjectChange, p, ct).ConfigureAwait(false),
            canManage,
            await loader.CanAsync(user, PortfolioPermissions.ProjectLpo, p, ct).ConfigureAwait(false),
            p.PmEmployeeId is null && p.SubState == SubStates.Ownership && p.Status == ProjectStatuses.Active && user.EmployeeIdOf() is not null
                && (canManage || await loader.InSectionAsync(user, p, ct).ConfigureAwait(false)),
            canEdit && ProjectLifecycle.CanEditCharter(p.Status, p.SubState),
            canEdit && ProjectLifecycle.CanEditContent(p.Status, p.SubState),
            canEdit && AgileService.CanPlan(p),
            await CanRecallAsync(p, ct).ConfigureAwait(false));
        var themes = JsonSerializer.Deserialize<List<string>>(p.OperationalThemes) ?? [];
        return new ProjectFullDto(
            p.Id, p.Number, p.Classification, p.NameAr, p.NameEn, p.Description, p.ScopeOfWork, p.Stage, p.SubState, p.Status, template, template.Path(),
            p.UnitId is { } u ? units.GetValueOrDefault(u) : null, p.SectionUnitId is { } s ? units.GetValueOrDefault(s) : null, p.StrategicObjectiveCode, p.ProgramTypeCode,
            themes,
            new ProjectCharterValues(p.NameAr, p.NameEn, p.Description, p.ScopeOfWork, p.StrategicObjectiveCode, themes, p.StructuralChanges, p.ResourcesHuman,
                p.ResourcesFinancial, p.ResourcesTechnical, p.ResourcesContract, p.ResourcesOther, p.PlannedStart, p.PlannedEnd, C(p.Year1ContractedCost),
                C(p.Year2ContractedCost), C(p.Year3ContractedCost), C(p.Year4ContractedCost), C(p.Year1ActualCost), C(p.Year2ActualCost), C(p.Year3ActualCost),
                C(p.Year4ActualCost), C(p.ContractedInitialCost), C(p.AnnualOperationCost), C(p.AnnualMaintenanceCost), C(p.AnnualLicenceCost), C(p.AnnualOtherCost),
                p.OperatingCostsFrom),
            C(p.Budget), C(p.Year1PlannedCost), C(p.Year2PlannedCost), C(p.Year3PlannedCost), C(p.Year4PlannedCost), p.PlannedStart, p.PlannedEnd,
            p.OwnerEmployeeId is { } o ? people.GetValueOrDefault(o) : null, p.PmEmployeeId is { } pm ? people.GetValueOrDefault(pm) : null,
            p.RequesterEmployeeId is { } q ? people.GetValueOrDefault(q) : null, p.OwnershipTakenAt, p.LpoUploaded, p.TeamId, p.TeamFormedAt,
            new ProjectApprovalDto(p.ApprovalProcess, p.ApprovalStepCode, p.ApprovalStepNameAr, p.ApprovalStepNameEn, p.ApprovalResult),
            p.Performance, p.PlannedProgress, p.ProgressStatus, p.SummaryOfAchievement, p.MainDeliverables, p.CostPerformance, p.CommunitySatisfaction, p.EmployeeSatisfaction,
            p.VendorSatisfaction, C(p.PreviousOperationalCost), p.ImpactNotes, p.UpdateOpenedAt, p.MonthlyApprovedAt, p.CancelReason, p.HoldReason, p.MergedIntoProjectId,
            p.CompletedAt, p.RequestId, requestNumber,
            new ProjectGatesDto(charter, planning, charter.All(i => i.Met), planning.All(i => i.Met)), counts, permissions, p.CreatedAt, p.Version,
            new ProjectDeliveryDto(p.Methodology, AgileApprovalModes.Effective(p.Methodology, p.AgileApprovalMode, settings.AgileApprovalMode), p.SprintLengthDays,
                p.DefinitionOfDone, canEdit && AgileService.CanSetMethodology(p, template), p.PortfolioGroupId, p.ProgrammeId, p.ReplanChangeRequestId, !showCosts));
    }
}
