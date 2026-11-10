using Microsoft.EntityFrameworkCore;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Risk.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Modules.AuditManagement.Application;

public sealed record AuditReportBody(AuditReportValues Values, uint Version);

public sealed record AuditFindingBody(FindingValues Values, uint Version);

/// <summary>PUT by external ID (ADR 0165): <c>ReportId</c> and <c>Recommendations</c> are used when the finding is created, ignored on update.</summary>
public sealed record AuditFindingUpsertInput(Guid ReportId, FindingValues Values, IReadOnlyList<RecommendationValues>? Recommendations);

public sealed record AuditRecommendationBody(RecommendationValues Values, uint Version);

public sealed record AuditReasonInput(string? Reason);

public sealed record AuditReviseInput(DateOnly NewDueOn, string? Reason);

public sealed record AuditRiskAcceptInput(string? Justification, string? Reference);

public sealed record AuditRiskLinkInput(Guid RiskId, Guid? ControlId, bool FlagForReassessment, bool LowerControl);

public sealed record AuditBatchInput(IReadOnlyList<NewFindingInput> Findings);

public sealed record CreateAuditReport(AuditReportValues Values) : ICommand<AuditReportDto>;

public sealed record UpdateAuditReport(Guid Id, AuditReportValues Values, uint Version) : ICommand<AuditReportDto>;

public sealed record IssueAuditReport(Guid Id) : ICommand<AuditReportDto>;

public sealed record CancelAuditReport(Guid Id, string? Reason) : ICommand<AuditReportDto>;

public sealed record CreateAuditFinding(Guid ReportId, NewFindingInput Input) : ICommand<AuditFindingDto>;

public sealed record CreateAuditFindingsBatch(Guid ReportId, IReadOnlyList<NewFindingInput> Findings) : ICommand<IReadOnlyList<AuditFindingListItemDto>>;

public sealed record UpdateAuditFinding(Guid Id, FindingValues Values, uint Version) : ICommand<AuditFindingDto>;

public sealed record CancelAuditFinding(Guid Id, string? Reason) : ICommand<AuditFindingDto>;

public sealed record LinkAuditFindingRisk(Guid Id, AuditRiskLinkInput Input) : ICommand<AuditFindingDto>;

public sealed record UnlinkAuditFindingRisk(Guid Id, Guid LinkId) : ICommand<AuditFindingDto>;

public sealed record SendAuditFindingToCap(Guid Id) : ICommand<AuditFindingDto>;

public sealed record CreateAuditRecommendation(Guid FindingId, RecommendationValues Values) : ICommand<AuditRecommendationDto>;

public sealed record UpdateAuditRecommendation(Guid Id, RecommendationValues Values, uint Version) : ICommand<AuditRecommendationDto>;

public sealed record ReviseAuditRecommendation(Guid Id, DateOnly NewDueOn, string? Reason) : ICommand<AuditRecommendationDto>;

public sealed record AcceptAuditRecommendationRisk(Guid Id, string? Justification, string? Reference) : ICommand<AuditRecommendationDto>;

public sealed record ReopenAuditRecommendation(Guid Id, string? Reason) : ICommand<AuditRecommendationDto>;

public sealed record CancelAuditRecommendation(Guid Id, string? Reason) : ICommand<AuditRecommendationDto>;

public sealed record RecordAuditFollowUp(Guid RecommendationId, FollowUpValues Values) : ICommand<AuditRecommendationDto>;

/// <summary>
/// Audit Office commands (ADR 0132): every handler refuses anyone without <c>auditmgmt.office.manage</c> (403) and loads the record through the
/// reader (404 outside the scope).
/// </summary>
internal sealed class AuditCommands(
    AuditMgmtDbContext db, AuditReader reader, AuditViews views, AuditService service, AuditNotifier notifier, AuditDeadlines deadlines, IRequestContext context,
    IClock clock, IIntegrationEventPublisher events, IServiceProvider services)
    : ICommandHandler<CreateAuditReport, AuditReportDto>, ICommandHandler<UpdateAuditReport, AuditReportDto>, ICommandHandler<IssueAuditReport, AuditReportDto>,
        ICommandHandler<CancelAuditReport, AuditReportDto>, ICommandHandler<CreateAuditFinding, AuditFindingDto>,
        ICommandHandler<CreateAuditFindingsBatch, IReadOnlyList<AuditFindingListItemDto>>, ICommandHandler<UpdateAuditFinding, AuditFindingDto>,
        ICommandHandler<CancelAuditFinding, AuditFindingDto>, ICommandHandler<LinkAuditFindingRisk, AuditFindingDto>, ICommandHandler<UnlinkAuditFindingRisk, AuditFindingDto>,
        ICommandHandler<SendAuditFindingToCap, AuditFindingDto>, ICommandHandler<CreateAuditRecommendation, AuditRecommendationDto>,
        ICommandHandler<UpdateAuditRecommendation, AuditRecommendationDto>, ICommandHandler<ReviseAuditRecommendation, AuditRecommendationDto>,
        ICommandHandler<AcceptAuditRecommendationRisk, AuditRecommendationDto>, ICommandHandler<ReopenAuditRecommendation, AuditRecommendationDto>,
        ICommandHandler<CancelAuditRecommendation, AuditRecommendationDto>, ICommandHandler<RecordAuditFollowUp, AuditRecommendationDto>
{
    // ---- Reports ----

    public async Task<Result<AuditReportDto>> HandleAsync(CreateAuditReport command, CancellationToken cancellationToken)
    {
        if (!await reader.IsOfficeAsync(context.User, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var errors = await service.ValidateReportAsync(command.Values, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var report = await service.CreateReportAsync(command.Values, null, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(report, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditReportDto>> HandleAsync(UpdateAuditReport command, CancellationToken cancellationToken)
    {
        var (report, error) = await ReportAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return error!;
        }

        if (!report.IsOpen)
        {
            return Error.Rule("auditmgmt.notEditable", "auditmgmt.errors.notEditable");
        }

        var errors = await service.ValidateReportAsync(command.Values, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(report).Property(r => r.Version).OriginalValue = command.Version;
        report.Edit(command.Values);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(report, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditReportDto>> HandleAsync(IssueAuditReport command, CancellationToken cancellationToken)
    {
        var (report, error) = await ReportAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return error!;
        }

        var issued = report.Issue(clock.UtcNow);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var findings = await db.Findings.Where(f => f.ReportId == report.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        await service.RecomputeReportAsync(report.Id, cancellationToken).ConfigureAwait(false);
        await notifier.SendAsync(AuditNotifications.ReportIssued, [.. report.DistributionEmployeeIds.Select(i => (Guid?)i), report.LeadAuditorEmployeeId],
            AuditNotifier.Values(report.Number, report.TitleAr, report.TitleEn, count: findings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            AuditService.Link(report), db, cancellationToken).ConfigureAwait(false);
        foreach (var finding in findings)
        {
            await service.NotifyDepartmentAsync(finding, AuditNotifications.FindingCreated, null, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(report, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditReportDto>> HandleAsync(CancelAuditReport command, CancellationToken cancellationToken)
    {
        var (report, error) = await ReportAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return error!;
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.required", "validation.required")]);
        }

        var cancelled = report.Cancel(command.Reason);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(report, cancellationToken).ConfigureAwait(false);
    }

    // ---- Findings ----

    public async Task<Result<AuditFindingDto>> HandleAsync(CreateAuditFinding command, CancellationToken cancellationToken)
    {
        var created = await CreateManyAsync(command.ReportId, [command.Input], cancellationToken).ConfigureAwait(false);
        return created.IsFailure ? created.Error : await views.ToDtoAsync(created.Value[0], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>AI extraction confirmation (ADR 0134): the rows the Audit Office kept, validated and created like the form, all or nothing.</summary>
    public async Task<Result<IReadOnlyList<AuditFindingListItemDto>>> HandleAsync(CreateAuditFindingsBatch command, CancellationToken cancellationToken)
    {
        var created = await CreateManyAsync(command.ReportId, command.Findings, cancellationToken).ConfigureAwait(false);
        return created.IsFailure ? created.Error : Result.Success(await views.ListItemsAsync(created.Value, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<List<Finding>>> CreateManyAsync(Guid reportId, IReadOnlyList<NewFindingInput> inputs, CancellationToken ct)
    {
        var (report, error) = await ReportAsync(reportId, ct).ConfigureAwait(false);
        if (report is null)
        {
            return error!;
        }

        if (!report.IsOpen)
        {
            return Error.Rule("auditmgmt.reportClosed", "auditmgmt.errors.reportClosed");
        }

        if (inputs.Count is 0 or > 200)
        {
            return Error.Validation([new FieldError("findings", "validation.notEmpty", "validation.notEmpty")]);
        }

        var errors = new List<FieldError>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var prefix = inputs.Count == 1 ? string.Empty : $"findings[{i}].";
            errors.AddRange(await service.ValidateFindingAsync(inputs[i].Finding, prefix, ct).ConfigureAwait(false));
            var recs = inputs[i].Recommendations ?? [];
            for (var j = 0; j < recs.Count; j++)
            {
                errors.AddRange(await service.ValidateRecommendationAsync(recs[j], $"{prefix}recommendations[{j}].", ct).ConfigureAwait(false));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var created = new List<Finding>();
        foreach (var input in inputs)
        {
            created.Add(await service.CreateFindingAsync(report, input, ct).ConfigureAwait(false));
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return created;
    }

    public async Task<Result<AuditFindingDto>> HandleAsync(UpdateAuditFinding command, CancellationToken cancellationToken)
    {
        var (finding, error) = await FindingAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (finding is null)
        {
            return error!;
        }

        if (finding.Status == AuditFindingStatuses.Cancelled)
        {
            return Error.Rule("auditmgmt.notEditable", "auditmgmt.errors.notEditable");
        }

        var errors = await service.ValidateFindingAsync(command.Values, string.Empty, cancellationToken).ConfigureAwait(false);
        if (command.Values.RepeatOfFindingId == finding.Id)
        {
            errors.Add(new FieldError("repeatOfFindingId", "validation.invalid", "validation.invalid"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        db.Entry(finding).Property(f => f.Version).OriginalValue = command.Version;
        var department = finding.ResponsibleUnitId == command.Values.ResponsibleUnitId
            ? finding.DepartmentUnitId
            : await reader.DepartmentOfAsync(command.Values.ResponsibleUnitId, settings.DepartmentLevelCode, cancellationToken).ConfigureAwait(false);
        finding.Edit(command.Values, department);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await service.SyncDepartmentGrantAsync(finding, settings, cancellationToken).ConfigureAwait(false);
        await service.RecomputeAsync(finding, cancellationToken, updateText: "Details updated / تحديث التفاصيل").ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(finding, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditFindingDto>> HandleAsync(CancelAuditFinding command, CancellationToken cancellationToken)
    {
        var (finding, error) = await FindingAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (finding is null)
        {
            return error!;
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.required", "validation.required")]);
        }

        var cancelled = finding.Cancel(command.Reason);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var me = context.User.EmployeeIdOf();
        foreach (var r in await db.Recommendations.Where(r => r.FindingId == finding.Id).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (r.IsOpen && r.Cancel(command.Reason).IsSuccess)
            {
                db.Logs.Add(RecommendationLog.Create(r.Id, LogKinds.Cancellation, null, null, command.Reason, null, me, clock.UtcNow));
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await events.PublishAsync(new AuditFindingClosed(finding.Id, finding.Number, finding.Status, finding.ResponsibleUnitId, clock.UtcNow), db, cancellationToken)
            .ConfigureAwait(false);
        await service.RecomputeReportAsync(finding.ReportId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(finding, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditFindingDto>> HandleAsync(LinkAuditFindingRisk command, CancellationToken cancellationToken)
    {
        var (finding, error) = await FindingAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (finding is null)
        {
            return error!;
        }

        if (services.GetService(typeof(IRiskDirectory)) is not IRiskDirectory risks)
        {
            return Error.Rule("auditmgmt.riskUnavailable", "auditmgmt.errors.riskUnavailable");
        }

        var input = command.Input;
        var risk = await risks.GetRiskAsync(input.RiskId, context.User, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.Validation([new FieldError("riskId", "validation.invalid", "validation.invalid")]);
        }

        if (input.ControlId is { } controlId && (await risks.ListControlsAsync(risk.Id, context.User, cancellationToken).ConfigureAwait(false)).All(c => c.Id != controlId))
        {
            return Error.Validation([new FieldError("controlId", "validation.invalid", "validation.invalid")]);
        }

        var link = await db.FindingRisks.FirstOrDefaultAsync(l => l.FindingId == finding.Id && l.RiskId == risk.Id && l.ControlId == input.ControlId, cancellationToken)
            .ConfigureAwait(false);
        if (link is null)
        {
            link = FindingRisk.Create(finding.Id, risk.Id, risk.Number, input.ControlId);
            db.FindingRisks.Add(link);
        }
        else
        {
            link.Restore();
        }

        if (input.FlagForReassessment && await risks.FlagForReassessmentAsync(risk.Id, "audit-finding", cancellationToken).ConfigureAwait(false))
        {
            link.Flagged();
        }

        if (input.LowerControl && input.ControlId is { } lower
            && await risks.LowerControlEffectivenessAsync(lower, "audit-finding", cancellationToken).ConfigureAwait(false) is { } level)
        {
            link.Lowered(level);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await events.PublishAsync(new AuditFindingLinkedToRisk(finding.Id, finding.Number, risk.Id, input.ControlId, clock.UtcNow), db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(finding, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditFindingDto>> HandleAsync(UnlinkAuditFindingRisk command, CancellationToken cancellationToken)
    {
        var (finding, error) = await FindingAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (finding is null)
        {
            return error!;
        }

        var link = await db.FindingRisks.FirstOrDefaultAsync(l => l.Id == command.LinkId && l.FindingId == finding.Id, cancellationToken).ConfigureAwait(false);
        if (link is null)
        {
            return Error.NotFound();
        }

        link.Remove();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(finding, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>An improvement observation (never a formal finding) to CAP as a remark (ADR 0135).</summary>
    public async Task<Result<AuditFindingDto>> HandleAsync(SendAuditFindingToCap command, CancellationToken cancellationToken)
    {
        var (finding, error) = await FindingAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (finding is null)
        {
            return error!;
        }

        if (!(await views.ToDtoAsync(finding, cancellationToken).ConfigureAwait(false)).Can.SendToCap
            || services.GetService(typeof(ICapRemarkSources)) is not ICapRemarkSources cap)
        {
            return Error.Rule("auditmgmt.capNotAllowed", "auditmgmt.errors.capNotAllowed");
        }

        var report = await db.Reports.FirstAsync(r => r.Id == finding.ReportId, cancellationToken).ConfigureAwait(false);
        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var source = await cap.EnsureSourceAsync(new CapSourceRequest(AuditReport.EntityType, report.Id,
            report.TypeCode == ReportTypes.ExternalAudit ? CapSourceTypes.ExternalAudit : CapSourceTypes.InternalAudit, report.TitleAr, report.TitleEn,
            $"{report.Number} — {report.AuditorBody}".Trim(' ', '—'), report.ReportDate ?? today, report.LeadAuditorEmployeeId, null), db, cancellationToken).ConfigureAwait(false);
        report.LinkCapSource(source.SourceId);
        var priority = CapPriorities.All.Contains(finding.Priority) ? finding.Priority : CapPriorities.Medium;
        var section = finding.ResponsibleUnitId == finding.DepartmentUnitId ? (Guid?)null : finding.ResponsibleUnitId;
        var remark = await cap.AddRemarkAsync(source.SourceId, new CapRemarkRequest(finding.Id.ToString("D"),
            $"{finding.TitleAr}\n{finding.Condition}".Trim(), $"{finding.TitleEn}\n{finding.Condition}".Trim(), CapRemarkTypes.Improvement, priority, null,
            finding.DepartmentUnitId, section, null, today, Finding.EntityType, finding.Id), db, cancellationToken).ConfigureAwait(false);
        if (remark.IsFailure)
        {
            return remark.Error;
        }

        finding.LinkCapRemark(remark.Value.RemarkId, remark.Value.Number);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(finding, cancellationToken).ConfigureAwait(false);
    }

    // ---- Recommendations ----

    public async Task<Result<AuditRecommendationDto>> HandleAsync(CreateAuditRecommendation command, CancellationToken cancellationToken)
    {
        var (finding, error) = await FindingAsync(command.FindingId, cancellationToken).ConfigureAwait(false);
        if (finding is null)
        {
            return error!;
        }

        if (finding.Status == AuditFindingStatuses.Cancelled)
        {
            return Error.Rule("auditmgmt.notEditable", "auditmgmt.errors.notEditable");
        }

        var errors = await service.ValidateRecommendationAsync(command.Values, string.Empty, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var recommendation = await service.AddRecommendationAsync(finding, command.Values, cancellationToken).ConfigureAwait(false);
        await service.RecomputeAsync(finding, cancellationToken, updateText: "New recommendation / توصية جديدة").ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await DtoAsync(recommendation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditRecommendationDto>> HandleAsync(UpdateAuditRecommendation command, CancellationToken cancellationToken)
    {
        var (recommendation, finding, error) = await RecommendationAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (recommendation is null)
        {
            return error!;
        }

        if (recommendation.Status == RecommendationStatuses.Cancelled)
        {
            return Error.Rule("auditmgmt.notEditable", "auditmgmt.errors.notEditable");
        }

        var errors = await service.ValidateRecommendationAsync(command.Values, string.Empty, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(recommendation).Property(r => r.Version).OriginalValue = command.Version;
        if (command.Values.DueOn != recommendation.DueOn
            && (recommendation.RevisedDueOn is not null || recommendation.LastFollowUpOn is not null
                || await db.FollowUps.AnyAsync(u => u.RecommendationId == recommendation.Id, cancellationToken).ConfigureAwait(false)))
        {
            // Once followed up, a new date is a revision: the original is kept and the change logged with its reason (SPEC §10.1).
            return Error.Validation([new FieldError("dueOn", "auditmgmt.errors.useRevision", "auditmgmt.errors.useRevision")]);
        }

        recommendation.Edit(command.Values);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await service.RecomputeAsync(finding!, cancellationToken, notify: false).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await DtoAsync(recommendation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditRecommendationDto>> HandleAsync(ReviseAuditRecommendation command, CancellationToken cancellationToken)
    {
        var (recommendation, finding, error) = await RecommendationAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (recommendation is null)
        {
            return error!;
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.required", "validation.required")]);
        }

        if (command.NewDueOn == default)
        {
            return Error.Validation([new FieldError("newDueOn", "validation.required", "validation.required")]);
        }

        var previous = recommendation.EffectiveDue;
        var revised = recommendation.Revise(command.NewDueOn, command.Reason);
        return revised.IsFailure
            ? revised.Error
            : await LogAndRecomputeAsync(recommendation, finding!, RecommendationLog.Create(recommendation.Id, LogKinds.DueRevision, previous, command.NewDueOn, command.Reason,
                null, context.User.EmployeeIdOf(), clock.UtcNow), "Due date revised / تعديل تاريخ التنفيذ", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditRecommendationDto>> HandleAsync(AcceptAuditRecommendationRisk command, CancellationToken cancellationToken)
    {
        var (recommendation, finding, error) = await RecommendationAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (recommendation is null)
        {
            return error!;
        }

        if (string.IsNullOrWhiteSpace(command.Justification))
        {
            return Error.Validation([new FieldError("justification", "validation.required", "validation.required")]);
        }

        var accepted = recommendation.AcceptRisk(command.Justification, command.Reference, await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false));
        return accepted.IsFailure
            ? accepted.Error
            : await LogAndRecomputeAsync(recommendation, finding!, RecommendationLog.Create(recommendation.Id, LogKinds.RiskAcceptance, null, null, command.Justification,
                command.Reference, context.User.EmployeeIdOf(), clock.UtcNow), "Risk accepted by management / قبول المخاطر من الإدارة", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditRecommendationDto>> HandleAsync(ReopenAuditRecommendation command, CancellationToken cancellationToken)
    {
        var (recommendation, finding, error) = await RecommendationAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (recommendation is null)
        {
            return error!;
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.required", "validation.required")]);
        }

        var reopened = recommendation.Reopen();
        return reopened.IsFailure
            ? reopened.Error
            : await LogAndRecomputeAsync(recommendation, finding!, RecommendationLog.Create(recommendation.Id, LogKinds.Reopen, null, null, command.Reason, null,
                context.User.EmployeeIdOf(), clock.UtcNow), "Reopened / إعادة فتح", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditRecommendationDto>> HandleAsync(CancelAuditRecommendation command, CancellationToken cancellationToken)
    {
        var (recommendation, finding, error) = await RecommendationAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (recommendation is null)
        {
            return error!;
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.required", "validation.required")]);
        }

        var cancelled = recommendation.Cancel(command.Reason);
        return cancelled.IsFailure
            ? cancelled.Error
            : await LogAndRecomputeAsync(recommendation, finding!, RecommendationLog.Create(recommendation.Id, LogKinds.Cancellation, null, null, command.Reason, null,
                context.User.EmployeeIdOf(), clock.UtcNow), "Recommendation cancelled / إلغاء توصية", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AuditRecommendationDto>> HandleAsync(RecordAuditFollowUp command, CancellationToken cancellationToken)
    {
        var (recommendation, finding, error) = await RecommendationAsync(command.RecommendationId, cancellationToken).ConfigureAwait(false);
        if (recommendation is null)
        {
            return error!;
        }

        if (!recommendation.IsOpen || finding!.Status == AuditFindingStatuses.Cancelled)
        {
            return Error.Rule("auditmgmt.notOpen", "auditmgmt.errors.recommendationClosed");
        }

        if (await db.FollowUps.AnyAsync(u => u.RecommendationId == recommendation.Id && u.ReviewStatus == ReviewStatuses.Pending, cancellationToken).ConfigureAwait(false))
        {
            return Error.Rule("auditmgmt.reviewPending", "auditmgmt.errors.reviewPending");
        }

        var errors = FollowUp.Validate(command.Values, await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false));
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var recorded = await service.RecordFollowUpAsync(recommendation, finding, command.Values, context.User.EmployeeIdOf(), cancellationToken).ConfigureAwait(false);
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await DtoAsync(recommendation, cancellationToken).ConfigureAwait(false);
    }

    // ---- Helpers ----

    private async Task<AuditRecommendationDto> DtoAsync(Recommendation recommendation, CancellationToken ct) =>
        (await views.RecommendationsAsync([recommendation], ct).ConfigureAwait(false))[0];

    private async Task<Result<AuditRecommendationDto>> LogAndRecomputeAsync(Recommendation recommendation, Finding finding, RecommendationLog log, string text, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        db.Logs.Add(log);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await service.RecomputeAsync(finding, ct, updateText: text).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await DtoAsync(recommendation, ct).ConfigureAwait(false);
    }

    private async Task<(AuditReport? Report, Error? Error)> ReportAsync(Guid id, CancellationToken ct)
    {
        if (!await reader.IsOfficeAsync(context.User, ct).ConfigureAwait(false))
        {
            return (null, await reader.ReportAsync(id, context.User, ct).ConfigureAwait(false) is null ? Error.NotFound() : Error.Forbidden());
        }

        var report = await reader.ReportAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        return report is null ? (null, Error.NotFound()) : (report, null);
    }

    private async Task<(Finding? Finding, Error? Error)> FindingAsync(Guid id, CancellationToken ct)
    {
        var finding = await reader.FindingAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (finding is null)
        {
            return (null, Error.NotFound());
        }

        return await reader.IsOfficeAsync(context.User, ct).ConfigureAwait(false) ? (finding, null) : (null, Error.Forbidden());
    }

    private async Task<(Recommendation? Recommendation, Finding? Finding, Error? Error)> RecommendationAsync(Guid id, CancellationToken ct)
    {
        var recommendation = await reader.RecommendationAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (recommendation is null)
        {
            return (null, null, Error.NotFound());
        }

        if (!await reader.IsOfficeAsync(context.User, ct).ConfigureAwait(false))
        {
            return (null, null, Error.Forbidden());
        }

        var finding = await db.Findings.FirstAsync(f => f.Id == recommendation.FindingId, ct).ConfigureAwait(false);
        return (recommendation, finding, null);
    }
}
