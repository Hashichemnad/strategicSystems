using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Modules.Tasks.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Calendar;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio.Application;

public sealed record MilestoneDto(
    Guid Id,
    string Code,
    Guid? ParentId,
    bool IsHeader,
    bool HighLevel,
    string? TitleAr,
    string? TitleEn,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    decimal Weight,
    decimal MaxAchieved,
    decimal? PendingAchieve,
    decimal CompletionRate,
    DateOnly? ActualStart,
    DateOnly? ActualEnd,
    DateOnly? PendingActualStart,
    DateOnly? PendingActualEnd,
    decimal? Cost,
    string Status,
    string UpdateState,
    bool Delayed,
    string? Causality,
    string? CorrectiveActions,
    string? EvidenceNote,
    int EvidenceFiles,
    bool IsDeliverable,
    string? MainDeliverable,
    string? AcceptanceCriteria,
    bool? QualityMet,
    bool IsInnovative,
    string? InnovativeReason,
    int Sort,
    uint Version,
    bool DeliveredInSprints = false,
    string? QualityStatus = null,
    string? QualityComment = null,
    DateTimeOffset? QualityAt = null,
    Guid? VendorId = null,
    string? PendingSource = null);

public sealed record MilestonePlanDto(
    IReadOnlyList<MilestoneDto> Milestones, decimal DetailedWeight, decimal HighLevelWeight, decimal Performance, decimal PlannedProgress, string ProgressStatus);

public sealed record MilestoneInput(MilestoneValues Values, uint? Version = null);

public sealed record MilestoneUpdateInput(MilestoneProgressValues Values, uint Version);

public sealed record MilestoneProgressResult(MilestoneDto Milestone, IReadOnlyList<string> Warnings);

public sealed record MonthlyUpdateDto(
    Guid Id, string Period, int Sequence, string Status, DateTimeOffset OpenedAt, DateTimeOffset? DueAt, string OpenedBy, DateTimeOffset? SubmittedAt, DateTimeOffset? ApprovedAt,
    bool Overdue, string? SubmitComment, string Items, string Sprints = "[]", string? NarrativeAr = null, string? NarrativeEn = null);

public sealed record NarrativeInput(string? NarrativeAr, string? NarrativeEn);

public sealed record MonthlyViewDto(MonthlyUpdateDto? Current, IReadOnlyList<MonthlyUpdateDto> History, bool CanUpdate, bool CanSubmit, bool CanOpen, int PendingMilestones);

/// <summary>Milestones (SPEC §1.4 C), the monthly window and its updates (W4, ADR 0071).</summary>
internal sealed class MilestoneService(
    PortfolioDbContext db,
    ProjectLoader loader,
    RequestReader reader,
    ProjectProgress progress,
    MonthlyWindows windows,
    IFileService files,
    IApprovalEngine engine,
    IServiceProvider services,
    IRequestContext context,
    IClock clock,
    AgileService agile,
    CostVisibility costs)
{
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    public async Task<Result<MilestonePlanDto>> ListAsync(Guid projectId, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        var rows = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId).OrderBy(m => m.Sort).ThenBy(m => m.Code).ToListAsync(ct).ConfigureAwait(false);
        var dtos = await ToDtosAsync(rows, ct).ConfigureAwait(false);
        var active = rows.Where(m => m.IsActive && !m.IsHeader).ToList();
        return new MilestonePlanDto(dtos, active.Where(m => !m.HighLevel).Sum(m => m.Weight), active.Where(m => m.HighLevel).Sum(m => m.Weight), project.Performance,
            project.PlannedProgress, project.ProgressStatus);
    }

    public async Task<Result<MilestoneDto>> CreateAsync(Guid projectId, MilestoneInput input, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (await PlanDeniedAsync(project, ct).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        var all = await db.Milestones.Where(m => m.ProjectId == projectId).ToListAsync(ct).ConfigureAwait(false);
        var errors = Validate(input.Values, null, all);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var milestone = Milestone.Create(projectId, NextCode(project.Number, input.Values.ParentId, all), input.Values);
        db.Milestones.Add(milestone);
        await progress.RecalculateAsync(await TrackedProjectAsync(projectId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await ToDtosAsync([milestone], ct).ConfigureAwait(false))[0];
    }

    public async Task<Result<MilestoneDto>> UpdateAsync(Guid milestoneId, MilestoneInput input, CancellationToken ct)
    {
        var (milestone, project, error) = await LoadAsync(milestoneId, ct).ConfigureAwait(false);
        if (milestone is null)
        {
            return error!;
        }

        if (await PlanDeniedAsync(project!, ct).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        var all = await db.Milestones.Where(m => m.ProjectId == milestone.ProjectId).ToListAsync(ct).ConfigureAwait(false);
        if (input.Values.IsHeader != milestone.IsHeader && (milestone.IsHeader ? all.Any(m => m.ParentId == milestone.Id && m.IsActive) : milestone.MaxAchieved > 0))
        {
            return Error.Validation([new FieldError("isHeader", "portfolio.errors.headerChange", "portfolio.errors.headerChange")]);
        }

        var errors = Validate(input.Values, milestone, all);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        if (input.Version is { } version)
        {
            db.Entry(milestone).Property(x => x.Version).OriginalValue = version;
        }

        milestone.Edit(input.Values);
        await progress.RecalculateAsync(await TrackedProjectAsync(milestone.ProjectId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        return (await ToDtosAsync([milestone], ct).ConfigureAwait(false))[0];
    }

    public async Task<Result<MilestoneDto>> CancelAsync(Guid milestoneId, DeactivateInput input, CancellationToken ct)
    {
        var (milestone, project, error) = await LoadAsync(milestoneId, ct).ConfigureAwait(false);
        if (milestone is null)
        {
            return error!;
        }

        if (await PlanDeniedAsync(project!, ct).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        if (milestone.IsHeader && await db.Milestones.AnyAsync(m => m.ParentId == milestone.Id && m.Status != MilestoneStatuses.Cancelled, ct).ConfigureAwait(false))
        {
            return Error.Rule("portfolio.milestone.children", "portfolio.errors.headerHasChildren");
        }

        db.Entry(milestone).Property(x => x.Version).OriginalValue = input.Version;
        var cancelled = milestone.Cancel(input.Reason);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await progress.RecalculateAsync(await TrackedProjectAsync(milestone.ProjectId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        return (await ToDtosAsync([milestone], ct).ConfigureAwait(false))[0];
    }

    /// <summary>The PM's monthly values for one detailed leaf milestone, while the window is open or returned (ADR 0071).</summary>
    public async Task<Result<MilestoneProgressResult>> RecordProgressAsync(Guid milestoneId, MilestoneUpdateInput input, CancellationToken ct)
    {
        var (milestone, project, error) = await LoadAsync(milestoneId, ct).ConfigureAwait(false);
        if (milestone is null)
        {
            return error!;
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project!, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var window = await CurrentWindowAsync(project!.Id, ct).ConfigureAwait(false);
        if (project.Status != ProjectStatuses.Active || project.SubState != SubStates.Execution || window is null || !MonthlyStatuses.AcceptsUpdates(window.Status))
        {
            return Error.Rule("portfolio.monthly.closed", "portfolio.errors.windowClosed");
        }

        if (milestone.IsHeader || milestone.HighLevel || !milestone.IsActive || milestone.UpdateState == UpdateStates.PendingApproval || milestone.DeliveredInSprints)
        {
            return Error.Rule("portfolio.milestone.notUpdatable", "portfolio.errors.milestoneNotUpdatable");
        }

        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var evidence = await EvidenceCountAsync(milestone.Id, ct).ConfigureAwait(false);
        var errors = MilestoneRules.ValidateProgress(input.Values, milestone.Weight, milestone.MaxAchieved, milestone.PlannedEnd, today, evidence);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(milestone).Property(x => x.Version).OriginalValue = input.Version;
        milestone.RecordProgress(input.Values);
        if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        var warnings = new List<string>();
        if (input.Values.Achieve == milestone.Weight && services.GetService<ITaskDirectory>() is { } tasks
            && (await tasks.CountLinkedAsync(PortfolioPermissions.MilestoneResource, milestone.Id, ct).ConfigureAwait(false)).Open > 0)
        {
            warnings.Add("portfolio.warnings.openTasks");
        }

        return new MilestoneProgressResult((await ToDtosAsync([milestone], ct).ConfigureAwait(false))[0], warnings);
    }

    public async Task<Result<MilestoneDto>> UploadEvidenceAsync(Guid milestoneId, string fileName, Stream content, string? comment, CancellationToken ct)
    {
        var (milestone, project, error) = await LoadAsync(milestoneId, ct).ConfigureAwait(false);
        if (milestone is null)
        {
            return error!;
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project!, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (project!.Status != ProjectStatuses.Active)
        {
            return ProjectService.NotInStage(project);
        }

        var uploaded = await files.UploadAsync(new FileUpload(ProjectFilePurposes.Evidence.Key, milestone.Id.ToString("D"), fileName, content, comment), ct).ConfigureAwait(false);
        if (uploaded.IsFailure)
        {
            return uploaded.Error;
        }

        return (await ToDtosAsync([milestone], ct).ConfigureAwait(false))[0];
    }

    public async Task<Result<IReadOnlyList<ProjectFileDto>>> EvidenceAsync(Guid milestoneId, CancellationToken ct)
    {
        var (milestone, _, error) = await LoadAsync(milestoneId, ct, tracked: false).ConfigureAwait(false);
        if (milestone is null)
        {
            return error!;
        }

        var list = await files.ListAsync(PortfolioPermissions.MilestoneResource, milestone.Id.ToString("D"), ProjectFilePurposes.Evidence.Key, ct).ConfigureAwait(false);
        return list.Select(f => new ProjectFileDto(f.Id, "evidence", f.Name, f.ContentType, f.SizeBytes, f.VersionNo, f.UpdatedAt)).ToList();
    }

    public async Task<Result<MonthlyViewDto>> MonthlyAsync(Guid projectId, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        var rows = await db.MonthlyUpdates.AsNoTracking().Where(m => m.ProjectId == projectId).OrderByDescending(m => m.Period).ThenByDescending(m => m.Sequence).ToListAsync(ct).ConfigureAwait(false);
        var current = rows.FirstOrDefault(r => r.Status != MonthlyStatuses.Approved);
        var canEdit = await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false);
        var executing = project.Status == ProjectStatuses.Active && project.SubState == SubStates.Execution;
        var pending = await db.Milestones.CountAsync(m => m.ProjectId == projectId && (m.UpdateState == UpdateStates.Draft || m.UpdateState == UpdateStates.Returned), ct)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        return new MonthlyViewDto(
            current is null ? null : Dto(current, now), rows.Where(r => r != current).Select(r => Dto(r, now)).ToList(),
            canEdit && executing && current is not null && MonthlyStatuses.AcceptsUpdates(current.Status),
            canEdit && executing && current is not null && MonthlyStatuses.AcceptsUpdates(current.Status)
                && !(project.ApprovalProcess is not null && project.ApprovalResult is null),
            executing && current is null && await loader.CanAsync(context.User, PortfolioPermissions.ProjectManage, project, ct).ConfigureAwait(false),
            pending);
    }

    /// <summary>The PM Office opens this month's window now (audited; the job does it on the 1st).</summary>
    public async Task<Result<MonthlyViewDto>> OpenNowAsync(Guid projectId, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectManage, project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (project.Status != ProjectStatuses.Active || project.SubState != SubStates.Execution)
        {
            return ProjectService.NotInStage(project);
        }

        if (await CurrentWindowAsync(projectId, ct).ConfigureAwait(false) is not null)
        {
            return Error.Rule("portfolio.monthly.open", "portfolio.errors.windowAlreadyOpen");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await windows.OpenAsync(project, clock.UtcNow, context.User.UserId()?.ToString("D") ?? "user", ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await MonthlyAsync(projectId, ct).ConfigureAwait(false);
    }

    /// <summary>Submit the monthly update for approval (Q-P1). Pending milestone updates go with it.</summary>
    public async Task<Result<MonthlyViewDto>> SubmitMonthlyAsync(Guid projectId, uint version, string? comment, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var window = await db.MonthlyUpdates.Where(m => m.ProjectId == projectId && m.Status != MonthlyStatuses.Approved).OrderByDescending(m => m.Period)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (project.Status != ProjectStatuses.Active || project.SubState != SubStates.Execution || window is null || !MonthlyStatuses.AcceptsUpdates(window.Status))
        {
            return Error.Rule("portfolio.monthly.closed", "portfolio.errors.windowClosed");
        }

        if (project.ApprovalProcess is not null && project.ApprovalResult is null)
        {
            return ProjectService.ApprovalRunning(project!.ApprovalProcess!);
        }

        // Wasel (ADR 0114): a vendor submission of this window waiting for the PM's review blocks the monthly submit.
        if (await db.VendorSubmissions.AnyAsync(v => v.MonthlyUpdateId == window.Id && v.Status == SubmissionStatuses.Submitted, ct).ConfigureAwait(false))
        {
            return Error.Rule("portfolio.wasel.submissionPending", "portfolio.errors.vendorSubmissionPending");
        }

        var vendorNames = await db.Vendors.AsNoTracking().Where(v => db.Milestones.Any(m => m.ProjectId == projectId && m.VendorId == v.Id))
            .ToDictionaryAsync(v => v.Id, v => (v.NameAr, v.NameEn), ct).ConfigureAwait(false);
        var vendorItemIds = await db.Milestones.Where(m => m.ProjectId == projectId && m.PendingVendorItemId != null).Select(m => m.PendingVendorItemId!.Value)
            .ToListAsync(ct).ConfigureAwait(false);
        var vendorItems = await db.VendorSubmissionItems.AsNoTracking().Where(i => vendorItemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);

        var sprintSnapshot = Methodologies.UsesSprints(project.Methodology) ? await agile.CarryIntoMonthlyAsync(project, window.Period, ct).ConfigureAwait(false) : "[]";
        var milestones = (await db.Milestones.Where(m => m.ProjectId == projectId && (m.UpdateState == UpdateStates.Draft || m.UpdateState == UpdateStates.Returned))
            .ToListAsync(ct).ConfigureAwait(false)).Concat(db.Milestones.Local.Where(m => m.ProjectId == projectId && m.UpdateState == UpdateStates.Draft))
            .DistinctBy(m => m.Id).ToList();
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var errors = new List<FieldError>();
        foreach (var m in milestones.Where(m => m.PendingAchieve is not null && !m.DeliveredInSprints))
        {
            var values = new MilestoneProgressValues(m.PendingAchieve!.Value, m.PendingActualStart, m.PendingActualEnd, m.Causality, m.CorrectiveActions, m.EvidenceNote);
            errors.AddRange(MilestoneRules.ValidateProgress(values, m.Weight, m.MaxAchieved, m.PlannedEnd, today, await EvidenceCountAsync(m.Id, ct).ConfigureAwait(false))
                .Select(e => e with { Field = $"{m.Code}.{e.Field}" }));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        foreach (var m in milestones)
        {
            m.SubmitUpdate();
        }

        var snapshot = JsonSerializer.Serialize(milestones.Select(m => new
        {
            m.Id,
            m.Code,
            m.TitleAr,
            m.TitleEn,
            m.Weight,
            Approved = m.MaxAchieved,
            Achieve = m.PendingAchieve,
            ActualStart = m.PendingActualStart,
            ActualEnd = m.PendingActualEnd,
            m.Causality,
            m.CorrectiveActions,
            m.EvidenceNote,
            Source = m.PendingSource ?? PendingSources.Pm,
            m.VendorId,
            VendorNameAr = m.VendorId is { } vid && vendorNames.TryGetValue(vid, out var vn) ? vn.NameAr : null,
            VendorNameEn = m.VendorId is { } vid2 && vendorNames.TryGetValue(vid2, out var vn2) ? vn2.NameEn : null,
            VendorValues = m.PendingVendorItemId is { } itemId && vendorItems.TryGetValue(itemId, out var item)
                ? new { item.Achieve, item.ActualStart, item.ActualEnd, item.ProgressNote, item.Causality, item.CorrectiveActions, Diff = item.Diff }
                : null,
        }), SnapshotJson);
        window.Submit(clock.UtcNow, snapshot, comment);
        window.SetSprints(sprintSnapshot);
        db.Entry(project).Property(x => x.Version).OriginalValue = version;
        project.EnterApproval(ProjectApprovalProcesses.MonthlyUpdate, null, null, null);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await SaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        var variables = ProjectService.Variables(project);
        variables[ProjectApprovalProcesses.PeriodVariable] = window.Period;
        variables[ProjectApprovalProcesses.PerformanceVariable] = decimal.Round(project.Performance * 100, 2);
        var started = await engine.SubmitAsync(
            new ApprovalSubmission(ProjectApprovalProcesses.MonthlyUpdate, ProjectService.Subject(project), variables, comment), db, ct).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await MonthlyAsync(projectId, ct).ConfigureAwait(false);
    }

    private Task<MonthlyUpdate?> CurrentWindowAsync(Guid projectId, CancellationToken ct) =>
        db.MonthlyUpdates.AsNoTracking().Where(m => m.ProjectId == projectId && m.Status != MonthlyStatuses.Approved).OrderByDescending(m => m.Period)
            .ThenByDescending(m => m.Sequence).FirstOrDefaultAsync(ct);

    private static MonthlyUpdateDto Dto(MonthlyUpdate m, DateTimeOffset now) =>
        new(m.Id, m.Period, m.Sequence, m.Status, m.OpenedAt, m.DueAt, m.OpenedBy, m.SubmittedAt, m.ApprovedAt,
            MonthlyStatuses.AcceptsUpdates(m.Status) && m.DueAt is { } due && now > due, m.SubmitComment, m.Items, m.Sprints, m.NarrativeAr, m.NarrativeEn);

    /// <summary>The month's narrative (status report, ADR 0097) — typed by the PM or accepted from the status report writer while the window is open.</summary>
    public async Task<Result<MonthlyViewDto>> SaveNarrativeAsync(Guid projectId, NarrativeInput input, CancellationToken ct)
    {
        var project = await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound();
        }

        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var window = await db.MonthlyUpdates.Where(m => m.ProjectId == projectId && m.Status != MonthlyStatuses.Approved).OrderByDescending(m => m.Period)
            .ThenByDescending(m => m.Sequence).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (window is null || !MonthlyStatuses.AcceptsUpdates(window.Status))
        {
            return Error.Rule("portfolio.monthly.closed", "portfolio.errors.windowClosed");
        }

        if (input.NarrativeAr is { Length: > RequestRules.LongTextMaxLength } || input.NarrativeEn is { Length: > RequestRules.LongTextMaxLength })
        {
            return Error.Validation([new FieldError("narrativeEn", "validation.maximumLength", "validation.maximumLength")]);
        }

        window.SetNarrative(input.NarrativeAr, input.NarrativeEn);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await MonthlyAsync(projectId, ct).ConfigureAwait(false);
    }

    private async Task<Error?> PlanDeniedAsync(Project project, CancellationToken ct)
    {
        if (!await loader.CanAsync(context.User, PortfolioPermissions.ProjectEdit, project, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        return AgileService.CanPlan(project) ? null : ProjectService.NotInStage(project);
    }

    private static List<FieldError> Validate(MilestoneValues v, Milestone? self, List<Milestone> all)
    {
        var errors = new List<FieldError>();
        void Add(string field, string key, IReadOnlyDictionary<string, object?>? p = null) => errors.Add(new FieldError(field, key, key, p));
        if (string.IsNullOrWhiteSpace(v.TitleAr) && string.IsNullOrWhiteSpace(v.TitleEn))
        {
            Add("titleEn", "validation.atLeastOneLanguage");
        }

        foreach (var (field, text, max) in new[]
                 {
                     ("titleAr", v.TitleAr, RequestRules.NameMaxLength), ("titleEn", v.TitleEn, RequestRules.NameMaxLength),
                     ("mainDeliverable", v.MainDeliverable, MilestoneRules.TextMaxLength), ("acceptanceCriteria", v.AcceptanceCriteria, MilestoneRules.TextMaxLength),
                     ("innovativeReason", v.InnovativeReason, MilestoneRules.TextMaxLength),
                 })
        {
            if (text is { } t && t.Length > max)
            {
                Add(field, "validation.maximumLength", new Dictionary<string, object?> { ["max"] = max });
            }
        }

        if (!v.IsHeader && (v.Weight <= 0 || v.Weight > 1 || decimal.Round(v.Weight, 4) != v.Weight))
        {
            Add("weight", "portfolio.errors.weightRange");
        }

        if (v.PlannedStart is { } s && v.PlannedEnd is { } e && e < s)
        {
            Add("plannedEnd", "portfolio.errors.endBeforeStart");
        }

        if (!v.IsHeader && (v.PlannedStart is null || v.PlannedEnd is null))
        {
            Add("plannedStart", "validation.notEmpty");
        }

        if (v.Cost is { } c && (c < 0 || decimal.Round(c, 2) != c))
        {
            Add("cost", "portfolio.errors.twoDecimals");
        }

        if (v.IsDeliverable && (string.IsNullOrWhiteSpace(v.MainDeliverable) || string.IsNullOrWhiteSpace(v.AcceptanceCriteria)))
        {
            Add("mainDeliverable", "portfolio.errors.deliverableFields");
        }

        if (v.IsInnovative && string.IsNullOrWhiteSpace(v.InnovativeReason))
        {
            Add("innovativeReason", "validation.notEmpty");
        }

        if (!v.IsHeader && v.ParentId is { } parentId)
        {
            var parent = all.FirstOrDefault(m => m.Id == parentId);
            if (parent is null || !parent.IsHeader || !parent.IsActive || parent.HighLevel != v.HighLevel)
            {
                Add("parentId", "portfolio.errors.parentInvalid");
            }
        }

        if (!v.IsHeader)
        {
            var total = all.Where(m => m.IsActive && !m.IsHeader && m.HighLevel == v.HighLevel && m.Id != self?.Id).Sum(m => m.Weight) + v.Weight;
            if (total > 1)
            {
                Add("weight", "portfolio.errors.weightsOver100", new Dictionary<string, object?> { ["total"] = ProjectGates.Percent(total) });
            }
        }

        if (self is not null && v.Weight < self.MaxAchieved)
        {
            Add("weight", "portfolio.errors.weightBelowAchieved");
        }

        return errors;
    }

    /// <summary><c>&lt;Number&gt;-n</c> for top-level rows, <c>&lt;Number&gt;-n.m</c> under a phase.</summary>
    private static string NextCode(string projectNumber, Guid? parentId, List<Milestone> all)
    {
        if (parentId is { } p && all.FirstOrDefault(m => m.Id == p) is { } parent)
        {
            var siblings = all.Count(m => m.ParentId == p);
            return $"{parent.Code}.{siblings + 1}";
        }

        var top = all.Count(m => m.ParentId is null);
        return $"{projectNumber}-{top + 1}";
    }

    private async Task<(Milestone? Milestone, Project? Project, Error? Error)> LoadAsync(Guid milestoneId, CancellationToken ct, bool tracked = true)
    {
        var milestone = tracked
            ? await db.Milestones.FirstOrDefaultAsync(m => m.Id == milestoneId, ct).ConfigureAwait(false)
            : await db.Milestones.AsNoTracking().FirstOrDefaultAsync(m => m.Id == milestoneId, ct).ConfigureAwait(false);
        if (milestone is null)
        {
            return (null, null, Error.NotFound());
        }

        var project = await loader.FindAsync(milestone.ProjectId, context.User, ct).ConfigureAwait(false);
        return project is null ? (null, null, Error.NotFound()) : (milestone, project, null);
    }

    private async Task<Project> TrackedProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.Projects.FirstAsync(p => p.Id == projectId, ct).ConfigureAwait(false);

    /// <summary>Evidence of the PM plus the vendor's evidence (Wasel, ADR 0114).</summary>
    private async Task<int> EvidenceCountAsync(Guid milestoneId, CancellationToken ct) =>
        (await files.ListAsync(PortfolioPermissions.MilestoneResource, milestoneId.ToString("D"), null, ct).ConfigureAwait(false))
            .Count(f => f.Purpose == ProjectFilePurposes.Evidence.Key || f.Purpose == WaselFilePurposes.VendorEvidence.Key);

    private async Task<IReadOnlyList<MilestoneDto>> ToDtosAsync(List<Milestone> rows, CancellationToken ct)
    {
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var projectId = rows.Count == 0 ? Guid.Empty : rows[0].ProjectId;
        var project = rows.Count == 0 ? null : await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct).ConfigureAwait(false);
        var showCosts = project is not null && await costs.CanSeeAsync(context.User, project, ct).ConfigureAwait(false);
        var ownerIds = rows.Select(r => r.Id.ToString("D")).ToList();
        var withEvidence = (await files.OwnersWithFilesAsync(ProjectFilePurposes.Evidence.Key, ownerIds, ct).ConfigureAwait(false))
            .Concat(await files.OwnersWithFilesAsync(WaselFilePurposes.VendorEvidence.Key, ownerIds, ct).ConfigureAwait(false)).ToHashSet();
        var result = new List<MilestoneDto>();
        foreach (var m in rows)
        {
            var evidence = withEvidence.Contains(m.Id.ToString("D")) ? await EvidenceCountAsync(m.Id, ct).ConfigureAwait(false) : 0;
            var current = m.PendingAchieve ?? m.MaxAchieved;
            result.Add(new MilestoneDto(
                m.Id, m.Code, m.ParentId, m.IsHeader, m.HighLevel, m.TitleAr, m.TitleEn, m.PlannedStart, m.PlannedEnd, m.Weight, m.MaxAchieved, m.PendingAchieve,
                ProgressCalculator.CompletionRate(m.MaxAchieved, m.Weight), m.ActualStart, m.ActualEnd, m.PendingActualStart, m.PendingActualEnd, showCosts ? m.Cost : null, m.Status,
                m.UpdateState, m.IsActive && !m.IsHeader && MilestoneRules.IsDelayed(current, m.Weight, m.PlannedEnd, today), m.Causality, m.CorrectiveActions,
                m.EvidenceNote, evidence, m.IsDeliverable, m.MainDeliverable, m.AcceptanceCriteria, m.QualityMet, m.IsInnovative, m.InnovativeReason, m.Sort, m.Version,
                m.DeliveredInSprints, m.QualityStatus, m.QualityComment, m.QualityAt, m.VendorId, m.PendingSource));
        }

        return result;
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
}

/// <summary>
/// The monthly window (ADR 0071): opens on the 1st at the first working moment of the project unit's business hours, due N working days later at
/// the end of working time; reminder R working days before; overdue notice once.
/// </summary>
internal sealed class MonthlyWindows(
    PortfolioDbContext db, RequestReader reader, ProjectNotifier notifier, IBusinessHoursCalculator calculator, WaselNotifier waselNotifier)
{
    private const int MaxDaysScanned = 400;

    /// <summary>Creates the window of the period containing <paramref name="at"/> (in the caller's transaction) and notifies the PM.</summary>
    public async Task<MonthlyUpdate> OpenAsync(Project project, DateTimeOffset at, string openedBy, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var unit = project.SectionUnitId ?? project.UnitId;
        var period = MonthlyUpdate.PeriodOf(await LocalDateAsync(unit, at, ct).ConfigureAwait(false));
        var due = await DueAtAsync(unit, at, settings.MonthlyDueWorkingDays, ct).ConfigureAwait(false);
        var sequence = (await db.MonthlyUpdates.Where(m => m.ProjectId == project.Id && m.Period == period).Select(m => (int?)m.Sequence).MaxAsync(ct).ConfigureAwait(false) ?? 0) + 1;
        var window = MonthlyUpdate.Open(project.Id, period, sequence, at, due, openedBy);
        db.MonthlyUpdates.Add(window);
        project.MarkUpdateOpened(at);
        foreach (var milestone in await db.Milestones.Where(m => m.ProjectId == project.Id && m.UpdateState == UpdateStates.Approved).ToListAsync(ct).ConfigureAwait(false))
        {
            if (milestone.MaxAchieved < milestone.Weight)
            {
                milestone.ReopenForUpdate();
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var dueText = await DueTextAsync(unit, due, ct).ConfigureAwait(false);
        await notifier.ToEmployeesAsync(PortfolioNotifications.MonthlyOpened, project, [project.PmEmployeeId], db, ct, period, due: dueText).ConfigureAwait(false);
        if (project.MilestoneUpdateMode == UpdateModes.VendorThenPm)
        {
            // Wasel (SPEC §1.18 review flow 1): vendor users with assigned milestones are told the window is open.
            var vendorIds = await db.Milestones.Where(m => m.ProjectId == project.Id && m.VendorId != null && m.Status != MilestoneStatuses.Cancelled)
                .Select(m => m.VendorId!.Value).Distinct().ToListAsync(ct).ConfigureAwait(false);
            foreach (var vendorId in vendorIds)
            {
                await waselNotifier.ToVendorAsync(PortfolioNotifications.WaselMonthlyOpened, project, vendorId, ct, period, due: dueText, transactionContext: db)
                    .ConfigureAwait(false);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return window;
    }

    /// <summary>The first working moment on or after the 1st of the month of <paramref name="today"/>.</summary>
    public async Task<DateTimeOffset> OpeningAsync(Guid? unitId, DateOnly today, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        var day = new DateOnly(today.Year, today.Month, 1);
        for (var i = 0; i < MaxDaysScanned && await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false) == TimeSpan.Zero; i++)
        {
            day = day.AddDays(1);
        }

        var midnight = Midnight(zone, day);
        return await calculator.AddWorkingTimeAsync(hours.Id, midnight, TimeSpan.FromMinutes(1), ct).ConfigureAwait(false) - TimeSpan.FromMinutes(1);
    }

    /// <summary>End of working time on the <paramref name="workingDays"/>-th working day counting the opening day as the first.</summary>
    public async Task<DateTimeOffset> DueAtAsync(Guid? unitId, DateTimeOffset opened, int workingDays, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(opened, zone).DateTime);
        var counted = 0;
        for (var i = 0; i < MaxDaysScanned; i++, day = day.AddDays(1))
        {
            var working = await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false);
            if (working > TimeSpan.Zero && ++counted >= Math.Max(1, workingDays))
            {
                return await calculator.AddWorkingTimeAsync(hours.Id, Midnight(zone, day), working, ct).ConfigureAwait(false);
            }
        }

        return opened.AddDays(workingDays);
    }

    /// <summary>The start of the working day <paramref name="days"/> working days before the due moment's day.</summary>
    public async Task<DateTimeOffset> ReminderAtAsync(Guid? unitId, DateTimeOffset due, int days, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(due, zone).DateTime);
        for (int remaining = days, i = 0; remaining > 0 && i < MaxDaysScanned; i++)
        {
            day = day.AddDays(-1);
            if (await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false) > TimeSpan.Zero)
            {
                remaining--;
            }
        }

        return Midnight(zone, day);
    }

    public async Task<DateOnly> LocalDateAsync(Guid? unitId, DateTimeOffset at, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Zone(hours.TimeZone)).DateTime);
    }

    public async Task<string> DueTextAsync(Guid? unitId, DateTimeOffset due, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        return TimeZoneInfo.ConvertTime(due, Zone(hours.TimeZone)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private Task<TimeSpan> WorkingOnAsync(Guid hoursId, TimeZoneInfo zone, DateOnly day, CancellationToken ct) =>
        calculator.WorkingTimeBetweenAsync(hoursId, Midnight(zone, day), Midnight(zone, day.AddDays(1)), ct);

    private static DateTimeOffset Midnight(TimeZoneInfo zone, DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
