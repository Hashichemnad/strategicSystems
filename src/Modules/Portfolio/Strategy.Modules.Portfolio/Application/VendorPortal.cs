using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Modules.Portfolio.Application;

// ---------------------------------------------------------------- Vendor DTOs (ADR 0113): dedicated records, explicit mapping, no internal fields.

public sealed record VendorPortalMeDto(
    Guid VendorUserId, string Email, string? Name, Guid VendorId, string VendorNumber, string? VendorNameAr, string? VendorNameEn, IReadOnlyList<VendorProjectSummaryDto> Projects);

public sealed record VendorProjectSummaryDto(
    Guid Id, string Number, string? NameAr, string? NameEn, string Stage, string Status, string Role, string UpdateMode, DateOnly? AccessUntil, int OpenActions);

public sealed record VendorContactDto(string? NameAr, string? NameEn, string? Email);

public sealed record VendorProjectDto(
    Guid Id,
    string Number,
    string? NameAr,
    string? NameEn,
    string? Scope,
    string? Description,
    string Stage,
    string Status,
    string Methodology,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    VendorContactDto? ProjectManager,
    VendorContactDto? Owner,
    decimal? Progress,
    decimal? Budget,
    string? ApprovalStatus,
    string Role,
    string UpdateMode,
    DateOnly? AccessUntil,
    bool CanProposePlan,
    string? OpenWindowPeriod,
    DateTimeOffset? WindowDueAt,
    IReadOnlyList<string> Permissions);

public sealed record VendorMilestoneDto(
    Guid Id, string Code, string? PhaseTitleAr, string? PhaseTitleEn, string? TitleAr, string? TitleEn, DateOnly? PlannedStart, DateOnly? PlannedEnd, decimal? Weight,
    decimal ApprovedAchieve, DateOnly? ActualStart, DateOnly? ActualEnd, string Status, bool Delayed, decimal? Cost, bool IsDeliverable, string? MainDeliverable);

public sealed record VendorSubmissionItemDto(
    Guid? Id, Guid MilestoneId, string Code, string? TitleAr, string? TitleEn, decimal? Weight, decimal ApprovedAchieve, DateOnly? ApprovedActualStart, DateOnly? PlannedEnd,
    bool Delayed, VendorProgressValues? Values, string ReviewStatus, VendorProgressValues? Final, IReadOnlyList<DiffEntry> Diff, string? PmComment,
    IReadOnlyList<VendorFileDto> Evidence, uint? Version);

public sealed record VendorSubmissionDto(
    Guid? Id, string? Period, string? WindowStatus, DateTimeOffset? DueAt, string Status, DateTimeOffset? SubmittedAt, string? SharedOutcome, string? SharedNote,
    IReadOnlyList<VendorSubmissionItemDto> Items, bool CanEdit, bool CanSubmit, string UpdateMode, uint? Version);

public sealed record VendorSubmissionHistoryDto(Guid Id, string Period, string Status, DateTimeOffset? SubmittedAt, string? SharedOutcome, string? SharedNote, int Items);

public sealed record VendorProposalDto(
    Guid Id, int Sequence, string Status, string Kind, IReadOnlyList<ProposalItem> Items, IReadOnlyList<ProposalItem>? AcceptedItems, IReadOnlyList<ProposalDiffEntry> Diff,
    string? VendorNote, string? PmComment, DateTimeOffset? SubmittedAt, DateTimeOffset? DecidedAt, uint Version);

public sealed record VendorProposalsDto(IReadOnlyList<VendorProposalDto> Proposals, bool CanPropose, string Kind, decimal AvailableWeight);

public sealed record VendorDeliverableDto(
    Guid Id, Guid? MilestoneId, string Title, string? Description, string Status, string? PmComment, IReadOnlyList<VendorFileDto> Files, DateTimeOffset CreatedAt);

public sealed record VendorItemDto(
    Guid Id, string Kind, string Title, string? Description, string? Impact, int? Probability, int? ImpactLevel, Guid? MilestoneId, string Status, string? PmComment,
    DateTimeOffset CreatedAt);

public sealed record VendorMeetingDto(Guid Id, DateTimeOffset MeetingAt, string? Title, string? Notes, DateTimeOffset? ConfirmedAt);

public sealed record VendorProposalInput(IReadOnlyList<ProposalItem> Items, string? Note, uint? Version = null);

public sealed record VendorProgressInput(VendorProgressValues Values, uint? Version = null);

public sealed record VendorSubmitInput(uint Version);

public sealed record VendorItemInput(VendorItemValues Values);

public sealed record VendorVersionInput(uint Version);

/// <summary>
/// The vendor's side of Wasel (SPEC §1.18, docs/design/portfolio.md §50–§53). Every call resolves the vendor scope through <see cref="WaselContext"/>; all writes
/// are audited with channel <c>wasel</c> by the request context of <c>/api/wasel</c>.
/// </summary>
internal sealed class VendorPortalService(
    PortfolioDbContext db,
    WaselContext wasel,
    RequestReader reader,
    IFileService files,
    IEmployeeDirectory employees,
    IUserDirectoryLookup users,
    WaselNotifier notifier,
    WaselManagementService management)
{
    public async Task<Result<VendorPortalMeDto>> MeAsync(CancellationToken ct)
    {
        var caller = await wasel.CallerAsync(ct, markActive: true).ConfigureAwait(false);
        if (caller is null)
        {
            return Error.NotFound();
        }

        var projects = await wasel.ProjectsAsync(caller, ct).ConfigureAwait(false);
        var ids = projects.Select(p => p.Project.Id).ToList();
        var returnedItems = await db.VendorSubmissions.AsNoTracking()
            .Where(s => ids.Contains(s.ProjectId) && s.VendorId == caller.Vendor.Id && s.Status == SubmissionStatuses.Returned).Select(s => s.ProjectId).ToListAsync(ct)
            .ConfigureAwait(false);
        var returnedProposals = await db.PlanProposals.AsNoTracking()
            .Where(p => ids.Contains(p.ProjectId) && p.VendorId == caller.Vendor.Id && p.Status == ProposalStatuses.Returned).Select(p => p.ProjectId).ToListAsync(ct)
            .ConfigureAwait(false);
        return new VendorPortalMeDto(caller.User.Id, caller.User.Email, caller.User.Name, caller.Vendor.Id, caller.Vendor.Number, caller.Vendor.NameAr, caller.Vendor.NameEn,
            projects.Select(p => new VendorProjectSummaryDto(p.Project.Id, p.Project.Number, p.Project.NameAr, p.Project.NameEn, p.Project.Stage, p.Project.Status, p.Member.Role,
                p.Project.MilestoneUpdateMode, p.Assignment.AccessUntil,
                returnedItems.Count(x => x == p.Project.Id) + returnedProposals.Count(x => x == p.Project.Id))).ToList());
    }

    public async Task<Result<VendorProjectDto>> ProjectAsync(Guid projectId, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.ProjectRead, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var s = scope.Value;
        var p = s.Project;
        var visible = (await reader.SettingsAsync(ct).ConfigureAwait(false)).Wasel.VisibleFields;
        var window = await OpenWindowAsync(p.Id, ct).ConfigureAwait(false);
        return new VendorProjectDto(
            p.Id, p.Number, p.NameAr, p.NameEn, p.ScopeOfWork, visible.Description ? p.Description : null, p.Stage, p.Status, p.Methodology, p.PlannedStart, p.PlannedEnd,
            await ContactAsync(p.PmEmployeeId, ct).ConfigureAwait(false), visible.Owner ? await ContactAsync(p.OwnerEmployeeId, ct).ConfigureAwait(false) : null,
            visible.Progress ? p.Performance : null, visible.Budget ? p.Budget : null,
            visible.ApprovalStatus ? (p.ApprovalProcess is null ? null : p.ApprovalResult ?? "in-approval") : null,
            s.Member.Role, p.MilestoneUpdateMode, s.Assignment.AccessUntil, AgileService.CanPlan(p), window?.Period, window?.DueAt,
            s.IsLead ? WaselPermissions.LeadPermissions : WaselPermissions.ContributorPermissions);
    }

    public async Task<Result<IReadOnlyList<VendorMilestoneDto>>> MilestonesAsync(Guid projectId, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.ProjectRead, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var visible = (await reader.SettingsAsync(ct).ConfigureAwait(false)).Wasel.VisibleFields;
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var vendorId = scope.Value.Vendor.Id;
        var rows = await db.Milestones.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.VendorId == vendorId && !m.IsHeader && !m.HighLevel && m.Status != MilestoneStatuses.Cancelled)
            .OrderBy(m => m.Sort).ThenBy(m => m.Code).ToListAsync(ct).ConfigureAwait(false);
        var parentIds = rows.Where(r => r.ParentId != null).Select(r => r.ParentId!.Value).Distinct().ToList();
        var parents = await db.Milestones.AsNoTracking().Where(m => parentIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);
        return rows.Select(m =>
        {
            var parent = m.ParentId is { } pid ? parents.GetValueOrDefault(pid) : null;
            return new VendorMilestoneDto(m.Id, m.Code, parent?.TitleAr, parent?.TitleEn, m.TitleAr, m.TitleEn, m.PlannedStart, m.PlannedEnd,
                visible.MilestoneWeight ? m.Weight : null, m.MaxAchieved, m.ActualStart, m.ActualEnd, m.Status,
                MilestoneRules.IsDelayed(m.MaxAchieved, m.Weight, m.PlannedEnd, today), visible.MilestoneCost ? m.Cost : null, m.IsDeliverable, m.MainDeliverable);
        }).ToList();
    }

    // ---------------------------------------------------------------- plan proposals

    public async Task<Result<VendorProposalsDto>> ProposalsAsync(Guid projectId, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.ProjectRead, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var s = scope.Value;
        var rows = await db.PlanProposals.AsNoTracking().Where(p => p.ProjectId == projectId && p.VendorId == s.Vendor.Id).OrderByDescending(p => p.Sequence)
            .ToListAsync(ct).ConfigureAwait(false);
        var kind = KindOf(s.Project);
        return new VendorProposalsDto(rows.Select(ProposalDto).ToList(), AgileService.CanPlan(s.Project), kind,
            await management.RemainingWeightAsync(s.Project, kind, ct).ConfigureAwait(false));
    }

    public async Task<Result<VendorProposalsDto>> SaveProposalAsync(Guid projectId, Guid? proposalId, VendorProposalInput input, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.PlanPrepare, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var s = scope.Value;
        if (!AgileService.CanPlan(s.Project))
        {
            return ProjectService.NotInStage(s.Project);
        }

        if (input.Items is null || input.Items.Count > 300 || input.Note is { Length: > 4000 })
        {
            return Error.Validation([new FieldError("items", "validation.maximumLength", "validation.maximumLength")]);
        }

        PlanProposal? proposal;
        if (proposalId is { } id)
        {
            proposal = await db.PlanProposals.FirstOrDefaultAsync(p => p.Id == id && p.ProjectId == projectId && p.VendorId == s.Vendor.Id, ct).ConfigureAwait(false);
            if (proposal is null)
            {
                return Error.NotFound();
            }

            if (input.Version is { } version)
            {
                db.Entry(proposal).Property(x => x.Version).OriginalValue = version;
            }

            var moved = proposal.Move(ProposalActions.Edit, wasel.Now, wasel.UserId);
            if (moved.IsFailure)
            {
                return moved.Error;
            }
        }
        else
        {
            if (await db.PlanProposals.AnyAsync(p => p.ProjectId == projectId && p.VendorId == s.Vendor.Id
                    && (p.Status == ProposalStatuses.Draft || p.Status == ProposalStatuses.Submitted || p.Status == ProposalStatuses.Returned), ct).ConfigureAwait(false))
            {
                return Error.Rule("portfolio.wasel.proposalOpen", "portfolio.errors.proposalOpen");
            }

            var next = (await db.PlanProposals.Where(p => p.ProjectId == projectId && p.VendorId == s.Vendor.Id).MaxAsync(p => (int?)p.Sequence, ct).ConfigureAwait(false) ?? 0) + 1;
            proposal = PlanProposal.Create(projectId, s.Vendor.Id, next, KindOf(s.Project));
            db.PlanProposals.Add(proposal);
        }

        proposal.SetItems(ProposalRules.Serialize(input.Items), input.Note);
        if (await TrySaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        return await ProposalsAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Result<VendorProposalsDto>> MoveProposalAsync(Guid projectId, Guid proposalId, string action, uint version, CancellationToken ct)
    {
        var permission = action == ProposalActions.Submit ? WaselPermissions.PlanSubmit : WaselPermissions.PlanPrepare;
        var scope = await wasel.ProjectAsync(projectId, permission, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var s = scope.Value;
        var proposal = await db.PlanProposals.FirstOrDefaultAsync(p => p.Id == proposalId && p.ProjectId == projectId && p.VendorId == s.Vendor.Id, ct).ConfigureAwait(false);
        if (proposal is null)
        {
            return Error.NotFound();
        }

        if (action == ProposalActions.Submit)
        {
            if (!AgileService.CanPlan(s.Project))
            {
                return ProjectService.NotInStage(s.Project);
            }

            var errors = ProposalRules.Validate(ProposalRules.Parse(proposal.Items), proposal.Kind,
                await management.RemainingWeightAsync(s.Project, proposal.Kind, ct).ConfigureAwait(false), s.Project.PlannedStart, s.Project.PlannedEnd);
            if (errors.Count > 0)
            {
                return Error.Validation(errors);
            }
        }

        db.Entry(proposal).Property(x => x.Version).OriginalValue = version;
        var moved = proposal.Move(action, wasel.Now, wasel.UserId);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        if (await TrySaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        if (action == ProposalActions.Submit)
        {
            await notifier.ToPmAsync(PortfolioNotifications.WaselProposalReceived, s.Project, ct, null, VendorName(s)).ConfigureAwait(false);
        }

        return await ProposalsAsync(projectId, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- monthly submission (§52)

    public async Task<Result<VendorSubmissionDto>> SubmissionAsync(Guid projectId, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.ProjectRead, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        return await BuildSubmissionAsync(scope.Value, ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<VendorSubmissionHistoryDto>>> HistoryAsync(Guid projectId, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.ProjectRead, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var vendorId = scope.Value.Vendor.Id;
        return await db.VendorSubmissions.AsNoTracking().Where(s => s.ProjectId == projectId && s.VendorId == vendorId).OrderByDescending(s => s.Period)
            .ThenByDescending(s => s.CreatedAt)
            .Select(s => new VendorSubmissionHistoryDto(s.Id, s.Period, s.Status, s.SubmittedAt, s.SharedOutcome, s.SharedNote,
                db.VendorSubmissionItems.Count(i => i.SubmissionId == s.Id))).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<Result<VendorSubmissionDto>> SaveItemAsync(Guid projectId, Guid milestoneId, VendorProgressInput input, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.UpdatePrepare, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var s = scope.Value;
        var milestone = await wasel.AssignedMilestoneAsync(s, milestoneId, ct).ConfigureAwait(false);
        if (milestone is null)
        {
            return Error.NotFound();
        }

        var (submission, error) = await EditableSubmissionAsync(s, ct).ConfigureAwait(false);
        if (submission is null)
        {
            return error!;
        }

        var v = input.Values;
        var errors = new List<FieldError>();
        if (v is null || v.Achieve < 0 || v.Achieve > milestone.Weight || decimal.Round(v.Achieve, 4) != v.Achieve)
        {
            errors.Add(new FieldError("achieve", "portfolio.errors.achieveAboveWeight", "portfolio.errors.achieveAboveWeight"));
        }

        if (v is not null && new[] { v.ProgressNote, v.Causality, v.CorrectiveActions }.Any(t => t is { Length: > MilestoneRules.TextMaxLength }))
        {
            errors.Add(new FieldError("progressNote", "validation.maximumLength", "validation.maximumLength"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var item = await db.VendorSubmissionItems.FirstOrDefaultAsync(i => i.SubmissionId == submission.Id && i.MilestoneId == milestoneId, ct).ConfigureAwait(false);
        if (item is null)
        {
            item = VendorSubmissionItem.Create(submission, milestoneId);
            db.VendorSubmissionItems.Add(item);
        }
        else
        {
            if (item.Locked)
            {
                return Error.Rule("portfolio.wasel.itemLocked", "portfolio.errors.vendorItemLocked");
            }

            if (input.Version is { } version)
            {
                db.Entry(item).Property(x => x.Version).OriginalValue = version;
            }
        }

        item.SetValues(v!);
        if (await TrySaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        return await BuildSubmissionAsync(s, ct).ConfigureAwait(false);
    }

    public async Task<Result<VendorSubmissionDto>> UploadEvidenceAsync(Guid projectId, Guid milestoneId, string fileName, Stream content, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.UpdatePrepare, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var s = scope.Value;
        if (await wasel.AssignedMilestoneAsync(s, milestoneId, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        var (submission, error) = await EditableSubmissionAsync(s, ct).ConfigureAwait(false);
        if (submission is null)
        {
            return error!;
        }

        var uploaded = await files.UploadAsync(new FileUpload(WaselFilePurposes.VendorEvidence.Key, milestoneId.ToString("D"), fileName, content,
            $"wasel:{s.Vendor.Number}"), ct).ConfigureAwait(false);
        if (uploaded.IsFailure)
        {
            return uploaded.Error;
        }

        return await BuildSubmissionAsync(s, ct).ConfigureAwait(false);
    }

    public async Task<Result<VendorSubmissionDto>> SubmitAsync(Guid projectId, VendorSubmitInput input, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.UpdateSubmit, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var s = scope.Value;
        var (submission, error) = await EditableSubmissionAsync(s, ct).ConfigureAwait(false);
        if (submission is null)
        {
            return error!;
        }

        var items = await db.VendorSubmissionItems.Where(i => i.SubmissionId == submission.Id).ToListAsync(ct).ConfigureAwait(false);
        var open = items.Where(i => !i.Locked).ToList();
        if (open.Count == 0)
        {
            return Error.Rule("portfolio.wasel.nothingToSubmit", "portfolio.errors.vendorNothingToSubmit");
        }

        var ids = open.Select(i => i.MilestoneId).ToList();
        var milestones = await db.Milestones.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var errors = new List<FieldError>();
        foreach (var item in open)
        {
            var m = milestones[item.MilestoneId];
            var evidence = await EvidenceCountAsync(m.Id, ct).ConfigureAwait(false);
            var values = new MilestoneProgressValues(item.Achieve, item.ActualStart, item.ActualEnd, item.Causality, item.CorrectiveActions, item.ProgressNote);
            errors.AddRange(MilestoneRules.ValidateProgress(values, m.Weight, m.MaxAchieved, m.PlannedEnd, today, evidence).Select(e => e with { Field = $"{m.Code}.{e.Field}" }));
            item.ResetForResubmit();
            item.SnapshotPrevious(JsonSerializer.Serialize(new PreviousValuesDto(m.MaxAchieved, m.ActualStart, m.ActualEnd), ProposalRules.Json));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(submission).Property(x => x.Version).OriginalValue = input.Version;
        var moved = submission.Move(SubmissionActions.Submit, wasel.Now, wasel.UserId);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        if (await TrySaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        await notifier.ToPmAsync(PortfolioNotifications.WaselSubmissionReceived, s.Project, ct, submission.Period, VendorName(s)).ConfigureAwait(false);
        return await BuildSubmissionAsync(s, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- deliverables, proposed items, meetings, comments

    public async Task<Result<IReadOnlyList<VendorDeliverableDto>>> DeliverablesAsync(Guid projectId, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.ProjectRead, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var vendorId = scope.Value.Vendor.Id;
        var rows = await db.VendorDeliverables.AsNoTracking().Where(d => d.ProjectId == projectId && d.VendorId == vendorId).OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        var result = new List<VendorDeliverableDto>();
        foreach (var d in rows)
        {
            result.Add(new VendorDeliverableDto(d.Id, d.MilestoneId, d.Title, d.Description, d.Status, d.PmComment,
                await management.FilesAsync(VendorDeliverable.EntityType, d.Id, WaselFilePurposes.Deliverable.Key, ct).ConfigureAwait(false), d.CreatedAt));
        }

        return result;
    }

    public async Task<Result<IReadOnlyList<VendorDeliverableDto>>> UploadDeliverableAsync(
        Guid projectId, Guid? milestoneId, string? title, string? description, string fileName, Stream content, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.DeliverableUpload, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var s = scope.Value;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 300 || description is { Length: > 4000 })
        {
            return Error.Validation([new FieldError("title", "validation.notEmpty", "validation.notEmpty")]);
        }

        if (milestoneId is { } mid && await wasel.AssignedMilestoneAsync(s, mid, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        if (s.Project.Status != ProjectStatuses.Active)
        {
            return ProjectService.NotInStage(s.Project);
        }

        var deliverable = VendorDeliverable.Create(projectId, s.Vendor.Id, milestoneId, title, description);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        db.VendorDeliverables.Add(deliverable);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var uploaded = await files.UploadAsync(new FileUpload(WaselFilePurposes.Deliverable.Key, deliverable.Id.ToString("D"), fileName, content, $"wasel:{s.Vendor.Number}"), ct)
            .ConfigureAwait(false);
        if (uploaded.IsFailure)
        {
            return uploaded.Error;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        await notifier.ToPmAsync(PortfolioNotifications.WaselDeliverableReceived, s.Project, ct, title, VendorName(s)).ConfigureAwait(false);
        return await DeliverablesAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<VendorItemDto>>> ItemsAsync(Guid projectId, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.ProjectRead, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var vendorId = scope.Value.Vendor.Id;
        return await db.VendorItems.AsNoTracking().Where(i => i.ProjectId == projectId && i.VendorId == vendorId).OrderByDescending(i => i.CreatedAt)
            .Select(i => new VendorItemDto(i.Id, i.Kind, i.Title, i.Description, i.Impact, i.Probability, i.ImpactLevel, i.MilestoneId, i.Status, i.PmComment, i.CreatedAt))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<VendorItemDto>>> ProposeItemAsync(Guid projectId, VendorItemInput input, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.ItemPropose, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var s = scope.Value;
        var v = input.Values;
        var errors = new List<FieldError>();
        if (v is null || !VendorItemKinds.All.Contains(v.Kind))
        {
            errors.Add(new FieldError("kind", "validation.unknownCode", "validation.unknownCode"));
        }

        if (v is null || string.IsNullOrWhiteSpace(v.Title) || v.Title.Length > 300)
        {
            errors.Add(new FieldError("title", "validation.notEmpty", "validation.notEmpty"));
        }

        if (v is not null && (v.Description is { Length: > 4000 } || v.Impact is { Length: > 4000 }))
        {
            errors.Add(new FieldError("description", "validation.maximumLength", "validation.maximumLength"));
        }

        if (v is not null && (v.Probability is < 1 or > 5 || v.ImpactLevel is < 1 or > 5))
        {
            errors.Add(new FieldError("probability", "validation.inclusiveBetween", "validation.inclusiveBetween"));
        }

        if (v is { Kind: VendorItemKinds.Risk } && (v.Probability is null || v.ImpactLevel is null))
        {
            errors.Add(new FieldError("probability", "validation.notEmpty", "validation.notEmpty"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        if (v!.MilestoneId is { } mid && await wasel.AssignedMilestoneAsync(s, mid, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        db.VendorItems.Add(VendorItem.Create(projectId, s.Vendor.Id, v));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.ToPmAsync(PortfolioNotifications.WaselItemProposed, s.Project, ct, v.Title, VendorName(s)).ConfigureAwait(false);
        return await ItemsAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<VendorMeetingDto>>> MeetingsAsync(Guid projectId, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.ProjectRead, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var vendorId = scope.Value.Vendor.Id;
        return await db.Meetings.AsNoTracking().Where(m => m.ProjectId == projectId && m.VendorId == vendorId && m.Status == ItemStatuses.Active)
            .OrderByDescending(m => m.MeetingAt).Select(m => new VendorMeetingDto(m.Id, m.MeetingAt, m.Title, m.Notes, m.VendorConfirmedAt)).ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<VendorMeetingDto>>> ConfirmMeetingAsync(Guid projectId, Guid meetingId, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.MeetingConfirm, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var vendorId = scope.Value.Vendor.Id;
        var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == meetingId && m.ProjectId == projectId && m.VendorId == vendorId, ct).ConfigureAwait(false);
        if (meeting is null)
        {
            return Error.NotFound();
        }

        meeting.ConfirmByVendor(wasel.UserId!.Value, wasel.Now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await MeetingsAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<CommentDto>>> CommentsAsync(Guid projectId, string? subjectType, Guid? subjectId, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.ProjectRead, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var vendorId = scope.Value.Vendor.Id;
        var query = db.VendorComments.AsNoTracking().Where(c => c.ProjectId == projectId && c.VendorId == vendorId);
        if (subjectType is not null)
        {
            query = query.Where(c => c.SubjectType == subjectType);
        }

        if (subjectId is { } sid)
        {
            query = query.Where(c => c.SubjectId == sid);
        }

        return await query.OrderBy(c => c.CreatedAt).Take(500)
            .Select(c => new CommentDto(c.Id, c.SubjectType, c.SubjectId, c.AuthorKind, c.AuthorName, c.Body, c.CreatedAt)).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<CommentDto>>> AddCommentAsync(Guid projectId, CommentInput input, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.CommentWrite, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        if (WaselComments.Invalid(input) is { } invalid)
        {
            return invalid;
        }

        var s = scope.Value;
        var owner = await WaselComments.SubjectVendorAsync(db, projectId, input.SubjectType, input.SubjectId, null, ct).ConfigureAwait(false);
        if (owner != s.Vendor.Id)
        {
            return Error.NotFound(); // another vendor's or an internal subject
        }

        db.VendorComments.Add(VendorComment.Create(projectId, s.Vendor.Id, input.SubjectType, input.SubjectId, wasel.UserId!.Value, "vendor", s.User.Name ?? s.User.Email,
            input.Body));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.ToPmAsync(PortfolioNotifications.WaselComment, s.Project, ct, WaselManagementService.Truncate(input.Body), VendorName(s)).ConfigureAwait(false);
        return await CommentsAsync(projectId, input.SubjectType, input.SubjectId, ct).ConfigureAwait(false);
    }

    /// <summary>Downloads a file the vendor may see: its own evidence on an assigned milestone or one of its deliverables.</summary>
    public async Task<Result<FileContent>> FileAsync(Guid projectId, Guid fileId, CancellationToken ct)
    {
        var scope = await wasel.ProjectAsync(projectId, WaselPermissions.ProjectRead, ct).ConfigureAwait(false);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var s = scope.Value;
        var file = await files.FindAsync(fileId, ct).ConfigureAwait(false);
        if (file is null || !Guid.TryParse(file.OwnerId, out var ownerId))
        {
            return Error.NotFound();
        }

        var allowed = file.Purpose switch
        {
            var p when p == WaselFilePurposes.VendorEvidence.Key => await wasel.AssignedMilestoneAsync(s, ownerId, ct).ConfigureAwait(false) is not null,
            var p when p == WaselFilePurposes.Deliverable.Key => await db.VendorDeliverables.AnyAsync(d => d.Id == ownerId && d.ProjectId == projectId && d.VendorId == s.Vendor.Id,
                ct).ConfigureAwait(false),
            _ => false,
        };
        return allowed ? await files.OpenAsync(fileId, cancellationToken: ct).ConfigureAwait(false) : Error.NotFound();
    }

    // ---------------------------------------------------------------- helpers

    private async Task<VendorSubmissionDto> BuildSubmissionAsync(VendorScope s, CancellationToken ct)
    {
        var window = await OpenWindowAsync(s.Project.Id, ct).ConfigureAwait(false);
        var vendorMode = s.Project.MilestoneUpdateMode == UpdateModes.VendorThenPm;
        var submission = window is null
            ? null
            : await db.VendorSubmissions.AsNoTracking().FirstOrDefaultAsync(x => x.MonthlyUpdateId == window.Id && x.VendorId == s.Vendor.Id, ct).ConfigureAwait(false);
        var items = submission is null
            ? []
            : await db.VendorSubmissionItems.AsNoTracking().Where(i => i.SubmissionId == submission.Id).ToListAsync(ct).ConfigureAwait(false);
        var visible = (await reader.SettingsAsync(ct).ConfigureAwait(false)).Wasel.VisibleFields;
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var vendorId = s.Vendor.Id;
        var milestones = await db.Milestones.AsNoTracking()
            .Where(m => m.ProjectId == s.Project.Id && m.VendorId == vendorId && !m.IsHeader && !m.HighLevel && m.Status != MilestoneStatuses.Cancelled)
            .OrderBy(m => m.Sort).ThenBy(m => m.Code).ToListAsync(ct).ConfigureAwait(false);
        var rows = new List<VendorSubmissionItemDto>();
        foreach (var m in milestones)
        {
            var item = items.FirstOrDefault(i => i.MilestoneId == m.Id);
            VendorProgressValues? final = item?.FinalAchieve is { } fa
                ? new VendorProgressValues(fa, item.FinalActualStart, item.FinalActualEnd, item.ProgressNote, item.FinalCausality, item.FinalCorrectiveActions)
                : null;
            rows.Add(new VendorSubmissionItemDto(item?.Id, m.Id, m.Code, m.TitleAr, m.TitleEn, visible.MilestoneWeight ? m.Weight : null, m.MaxAchieved, m.ActualStart,
                m.PlannedEnd, MilestoneRules.IsDelayed(item?.Achieve ?? m.MaxAchieved, m.Weight, m.PlannedEnd, today), item?.Values,
                item?.ReviewStatus ?? "not-started", final, WaselManagementService.ParseDiff(item?.Diff), item?.PmComment,
                await management.FilesAsync(PortfolioPermissions.MilestoneResource, m.Id, WaselFilePurposes.VendorEvidence.Key, ct).ConfigureAwait(false), item?.Version));
        }

        var status = submission?.Status ?? SubmissionStatuses.Draft;
        var editable = vendorMode && window is not null && MonthlyStatuses.AcceptsUpdates(window.Status) && SubmissionStatuses.Editable(status)
            && s.Project.Status == ProjectStatuses.Active;
        var permissions = s.IsLead ? WaselPermissions.LeadPermissions : WaselPermissions.ContributorPermissions;
        return new VendorSubmissionDto(submission?.Id, window?.Period, window?.Status, window?.DueAt, status, submission?.SubmittedAt, submission?.SharedOutcome,
            submission?.SharedNote, rows, editable && permissions.Contains(WaselPermissions.UpdatePrepare),
            editable && permissions.Contains(WaselPermissions.UpdateSubmit) && items.Any(i => !i.Locked), s.Project.MilestoneUpdateMode, submission?.Version);
    }

    private async Task<(VendorSubmission? Submission, Error? Error)> EditableSubmissionAsync(VendorScope s, CancellationToken ct)
    {
        if (s.Project.MilestoneUpdateMode != UpdateModes.VendorThenPm)
        {
            return (null, Error.Rule("portfolio.wasel.pmOnly", "portfolio.errors.vendorPmOnlyMode"));
        }

        var window = await OpenWindowAsync(s.Project.Id, ct).ConfigureAwait(false);
        if (s.Project.Status != ProjectStatuses.Active || s.Project.SubState != SubStates.Execution || window is null || !MonthlyStatuses.AcceptsUpdates(window.Status))
        {
            return (null, Error.Rule("portfolio.monthly.closed", "portfolio.errors.windowClosed"));
        }

        var submission = await db.VendorSubmissions.FirstOrDefaultAsync(x => x.MonthlyUpdateId == window.Id && x.VendorId == s.Vendor.Id, ct).ConfigureAwait(false);
        if (submission is null)
        {
            submission = VendorSubmission.Create(s.Project.Id, s.Vendor.Id, window.Id, window.Period);
            db.VendorSubmissions.Add(submission);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return SubmissionStatuses.Editable(submission.Status)
            ? (submission, null)
            : (null, Error.Rule("portfolio.wasel.submitted", "portfolio.errors.vendorSubmissionLocked"));
    }

    private Task<MonthlyUpdate?> OpenWindowAsync(Guid projectId, CancellationToken ct) =>
        db.MonthlyUpdates.AsNoTracking().Where(m => m.ProjectId == projectId && m.Status != MonthlyStatuses.Approved).OrderByDescending(m => m.Period)
            .ThenByDescending(m => m.Sequence).FirstOrDefaultAsync(ct);

    private async Task<int> EvidenceCountAsync(Guid milestoneId, CancellationToken ct) =>
        (await files.ListAsync(PortfolioPermissions.MilestoneResource, milestoneId.ToString("D"), null, ct).ConfigureAwait(false))
        .Count(f => f.Purpose == ProjectFilePurposes.Evidence.Key || f.Purpose == WaselFilePurposes.VendorEvidence.Key);

    private async Task<VendorContactDto?> ContactAsync(Guid? employeeId, CancellationToken ct)
    {
        if (employeeId is not { } id || await employees.GetAsync(id, ct).ConfigureAwait(false) is not { } employee)
        {
            return null;
        }

        return new VendorContactDto(employee.NameAr, employee.NameEn, employee.Email ?? await users.EmailOfEmployeeAsync(id, ct).ConfigureAwait(false));
    }

    private static string KindOf(Project project) => project.Methodology == Methodologies.Agile ? Methodologies.Agile : Methodologies.Waterfall;

    private static string VendorName(VendorScope s) => s.Vendor.NameEn ?? s.Vendor.NameAr ?? s.Vendor.Number;

    private static VendorProposalDto ProposalDto(PlanProposal p) =>
        new(p.Id, p.Sequence, p.Status, p.Kind, ProposalRules.Parse(p.Items), p.AcceptedItems is null ? null : ProposalRules.Parse(p.AcceptedItems),
            p.Diff is null ? [] : JsonSerializer.Deserialize<List<ProposalDiffEntry>>(p.Diff, ProposalRules.Json) ?? [], p.VendorNote,
            p.Status is ProposalStatuses.Returned or ProposalStatuses.Accepted ? p.PmComment : null, p.SubmittedAt, p.DecidedAt, p.Version);

    private async Task<Error?> TrySaveAsync(CancellationToken ct)
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

/// <summary>The PM contact's e-mail when the employee record has none (the linked account's address).</summary>
internal interface IUserDirectoryLookup
{
    Task<string?> EmailOfEmployeeAsync(Guid employeeId, CancellationToken ct);
}

internal sealed class UserDirectoryLookup(Strategy.Platform.Access.IUserDirectory users, Strategy.Platform.Notifications.INotificationRecipientDirectory recipients) : IUserDirectoryLookup
{
    public async Task<string?> EmailOfEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        var linked = await users.GetByEmployeesAsync([employeeId], ct).ConfigureAwait(false);
        if (linked.Count == 0)
        {
            return null;
        }

        var found = await recipients.ResolveAsync([linked[0].Id], ct).ConfigureAwait(false);
        return found.Count > 0 ? found[0].Email : null;
    }
}
