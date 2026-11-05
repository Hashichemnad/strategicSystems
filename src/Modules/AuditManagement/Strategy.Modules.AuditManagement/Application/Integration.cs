using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Persistence;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.AuditManagement.Application;

/// <summary>
/// Approval decisions (inside the engine's transaction, ADR 0023): the optional follow-up review applies or discards the pending follow-up
/// (ADR 0133); the annual plan approval moves the plan (ADR 0136).
/// </summary>
internal sealed class AuditApprovalHook(AuditMgmtDbContext db, AuditService service, AuditNotifier notifier, IClock clock) : IApprovalHook
{
    public IReadOnlyCollection<string> ProcessKeys { get; } = [AuditPermissions.FollowUpReviewProcess, AuditPermissions.PlanProcess];

    public async Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken)
    {
        if (approvalEvent.Kind is not (ApprovalEventKinds.Approved or ApprovalEventKinds.Rejected or ApprovalEventKinds.Returned or ApprovalEventKinds.Cancelled))
        {
            return Result.Success();
        }

        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        if (approvalEvent.ProcessKey == AuditPermissions.PlanProcess)
        {
            var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
            if (plan is null)
            {
                return Error.NotFound();
            }

            plan.Decided(approvalEvent.Kind switch
            {
                ApprovalEventKinds.Approved => PlanStatuses.Approved,
                ApprovalEventKinds.Cancelled => PlanStatuses.Draft,
                _ => PlanStatuses.Returned,
            }, approvalEvent.Comment, clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }

        var recommendation = await db.Recommendations.FirstOrDefaultAsync(r => r.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
        if (recommendation is null)
        {
            return Error.NotFound();
        }

        var followUp = await db.FollowUps.Where(u => u.RecommendationId == recommendation.Id && u.ReviewStatus == ReviewStatuses.Pending)
            .OrderByDescending(u => u.CreatedAt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (followUp is null)
        {
            return Result.Success();
        }

        var finding = await db.Findings.FirstAsync(f => f.Id == recommendation.FindingId, cancellationToken).ConfigureAwait(false);
        if (approvalEvent.Kind == ApprovalEventKinds.Approved)
        {
            followUp.Reviewed(ReviewStatuses.Approved, approvalEvent.Comment);
            if (recommendation.IsOpen)
            {
                await service.ApplyFollowUpAsync(followUp, recommendation, finding, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            followUp.Reviewed(ReviewStatuses.Returned, approvalEvent.Comment);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (approvalEvent.Kind != ApprovalEventKinds.Cancelled)
        {
            var label = approvalEvent.Kind == ApprovalEventKinds.Approved ? "Approved / معتمدة" : "Returned / معادة";
            await notifier.SendAsync(AuditNotifications.FollowUpReviewed, [followUp.RecordedByEmployeeId],
                AuditNotifier.Values(followUp.Number, recommendation.ActionAr, recommendation.ActionEn,
                    text: string.IsNullOrWhiteSpace(approvalEvent.Comment) ? label : $"{label} — {approvalEvent.Comment}"), AuditService.Link(finding), db, cancellationToken)
                .ConfigureAwait(false);
        }

        return Result.Success();
    }
}

/// <summary>"Next action required + who" on the finding page.</summary>
internal sealed class FindingCurrentStatus(AuditReader reader, AuditMgmtDbContext db, IRequestContext context) : ICurrentStatusProvider
{
    public string ResourceType => Finding.EntityType;

    public async Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var finding = await reader.FindingAsync(resourceId, context.User, cancellationToken).ConfigureAwait(false);
        if (finding is null)
        {
            return null;
        }

        var office = await reader.IsOfficeAsync(context.User, cancellationToken).ConfigureAwait(false);
        PersonName[] auditOffice = [new PersonName(null, "مكتب التدقيق", "Audit Office")];
        var recs = await db.Recommendations.AsNoTracking().Where(r => r.FindingId == finding.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var next = recs.Where(r => r.IsOpen && r.NextFollowUpOn != null).Select(r => r.NextFollowUpOn).Min();
        var p = new Dictionary<string, BilingualText>
        {
            ["date"] = new(AuditDeadlines.Date(next), AuditDeadlines.Date(next)),
            ["pct"] = new($"{Math.Round(finding.Completion * 100, 0)}", $"{Math.Round(finding.Completion * 100, 0)}"),
        };
        CurrentStatusAction[] actions = office && finding.IsOpen ? [new CurrentStatusAction("record-follow-up", "auditmgmt.actions.recordFollowUp")] : [];
        return finding.Status switch
        {
            AuditFindingStatuses.Closed => new CurrentStatus("auditmgmt.currentStatus.closed", p, "success", null, [], [], finding.Status),
            AuditFindingStatuses.RiskAccepted => new CurrentStatus("auditmgmt.currentStatus.riskAccepted", p, "neutral", null, [], [], finding.Status),
            AuditFindingStatuses.Cancelled => new CurrentStatus("auditmgmt.currentStatus.cancelled", p, "neutral", null, [], [], finding.Status),
            _ when !recs.Any(r => r.Status != RecommendationStatuses.Cancelled) => new CurrentStatus("auditmgmt.currentStatus.noRecommendations", p, "warning",
                "auditmgmt.currentStatus.office", auditOffice, office ? [new CurrentStatusAction("add-recommendation", "auditmgmt.actions.addRecommendation")] : [],
                finding.Status),
            AuditFindingStatuses.Late => new CurrentStatus("auditmgmt.currentStatus.late", p, "danger", "auditmgmt.currentStatus.office", auditOffice, actions, finding.Status),
            _ => new CurrentStatus(next is null ? "auditmgmt.currentStatus.open" : "auditmgmt.currentStatus.nextFollowUp", p, "info", "auditmgmt.currentStatus.office",
                auditOffice, actions, finding.Status),
        };
    }
}

/// <summary>My Work (SPEC §0.4) for auditors: follow-ups due, late recommendations of their findings.</summary>
internal sealed class AuditMyWork(AuditMgmtDbContext db, AuditReader reader, AuditDeadlines deadlines) : IMyWorkSource
{
    public string SystemCode => AuditPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } me || !await reader.IsOfficeAsync(user, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        var limit = today.AddDays(Math.Max(settings.FollowUpReminderDays, 7));
        var mine = db.Findings.Where(f => f.AuditorEmployeeId == me && f.Status != AuditFindingStatuses.Cancelled);
        var rows = await db.Recommendations.AsNoTracking()
            .Where(r => (r.Status == RecommendationStatuses.NotStarted || r.Status == RecommendationStatuses.Continuing || r.Status == RecommendationStatuses.Late)
                && ((r.NextFollowUpOn != null && r.NextFollowUpOn <= limit) || r.Status == RecommendationStatuses.Late))
            .Join(mine, r => r.FindingId, f => f.Id, (r, f) => new { R = r, F = f })
            .OrderBy(x => x.R.NextFollowUpOn ?? x.R.DueOn).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(x => new MyWorkItem(AuditPermissions.SystemCode, Finding.EntityType, x.F.Id, x.R.Number, AuditNotifier.Short(x.R.ActionAr ?? x.R.ActionEn),
            AuditNotifier.Short(x.R.ActionEn ?? x.R.ActionAr), x.R.Status == RecommendationStatuses.Late ? "auditmgmt.myWork.late" : "auditmgmt.myWork.followUpDue",
            $"{AuditService.Link(x.F)}?recommendation={x.R.Id:D}", null, x.R.NextFollowUpOn ?? x.R.EffectiveDue)).ToList();
    }
}

internal sealed class FindingLinkTargets(AuditReader reader) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        Finding.EntityType, AuditPermissions.SystemCode, "ملاحظة تدقيق", "Audit finding", [LinkCapabilities.TaskAble], AuditPermissions.Read,
        "/audit-management/findings/{id}", AuditPermissions.Office);

    public string ResourceType => Finding.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var rows = await (await reader.FindingsAsync(user, cancellationToken).ConfigureAwait(false)).Where(f => ids.Contains(f.Id)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(f => f.Id, f => new LinkTarget(f.Id, f.Number, f.TitleAr ?? f.TitleEn, f.TitleEn ?? f.TitleAr, f.Status, null, null, Tone(f.Status),
            AuditFindingStatuses.IsFinal(f.Status)));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.FindingAsync(id, user, cancellationToken).ConfigureAwait(false) is not null
        && (permission == AuditPermissions.Read || await reader.IsOfficeAsync(user, cancellationToken).ConfigureAwait(false));

    public static string Tone(string status) => status switch
    {
        AuditFindingStatuses.Closed => "success",
        AuditFindingStatuses.Late => "danger",
        AuditFindingStatuses.RiskAccepted or AuditFindingStatuses.Cancelled => "neutral",
        _ => "info",
    };
}

internal sealed class ReportLinkTargets(AuditReader reader) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        AuditReport.EntityType, AuditPermissions.SystemCode, "تقرير تدقيق", "Audit report", [], AuditPermissions.Read, "/audit-management/reports/{id}", AuditPermissions.Office);

    public string ResourceType => AuditReport.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var rows = await (await reader.ReportsAsync(user, cancellationToken).ConfigureAwait(false)).Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(r => r.Id, r => new LinkTarget(r.Id, r.Number, r.TitleAr ?? r.TitleEn, r.TitleEn ?? r.TitleAr, r.Status, null, null,
            r.Status == ReportStatuses.Closed ? "success" : "info", !r.IsOpen));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.ReportAsync(id, user, cancellationToken).ConfigureAwait(false) is not null
        && (permission == AuditPermissions.Read || await reader.IsOfficeAsync(user, cancellationToken).ConfigureAwait(false));
}

/// <summary>Report file: read with the report, written by the Audit Office.</summary>
internal sealed class ReportFiles(AuditReader reader) : IFileOwnerAccess
{
    public string OwnerType => AuditReport.EntityType;

    public string AttachmentPurpose => AuditFilePurposes.ReportFile.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.ReportAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        await CanReadAsync(user, ownerId, cancellationToken).ConfigureAwait(false) && await reader.IsOfficeAsync(user, cancellationToken).ConfigureAwait(false);
}

/// <summary>Evidence of a follow-up: readable with its finding (department users see it), written by the Audit Office only.</summary>
internal sealed class FollowUpFiles(AuditReader reader, AuditMgmtDbContext db) : IFileOwnerAccess
{
    public string OwnerType => FollowUp.EntityType;

    public string AttachmentPurpose => AuditFilePurposes.Evidence.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(ownerId, out var id))
        {
            return false;
        }

        var recommendationId = await db.FollowUps.AsNoTracking().Where(u => u.Id == id).Select(u => (Guid?)u.RecommendationId).FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return recommendationId is { } r && await reader.RecommendationAsync(r, user, cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        await CanReadAsync(user, ownerId, cancellationToken).ConfigureAwait(false) && await reader.IsOfficeAsync(user, cancellationToken).ConfigureAwait(false);
}

/// <summary>Working-paper files: Audit Office only (SPEC §10.6), Layer 2 switch on.</summary>
internal sealed class WorkPaperFiles(AuditReader reader, AuditMgmtDbContext db, Platform.Settings.IFeatureFlags features) : IFileOwnerAccess
{
    public string OwnerType => WorkPaper.EntityType;

    public string AttachmentPurpose => AuditFilePurposes.WorkPaper.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await features.IsEnabledAsync(AuditPermissions.LifecycleFeature, cancellationToken).ConfigureAwait(false)
        && await reader.CanAsync(user, AuditPermissions.WorkPapers, cancellationToken).ConfigureAwait(false)
        && await db.WorkPapers.AsNoTracking().AnyAsync(w => w.Id == id, cancellationToken).ConfigureAwait(false);

    public Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) => CanReadAsync(user, ownerId, cancellationToken);
}

/// <summary>Global search and Ask (ADR 0030): findings filtered by the user's scope in the query — out-of-scope findings never appear.</summary>
internal sealed class FindingSearchSource(AuditReader reader) : IRecordSearchSource
{
    public string ResourceType => Finding.EntityType;

    public string SystemCode => AuditPermissions.SystemCode;

    public string ReadPermission => AuditPermissions.Read;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var pattern = Like.Pattern(text);
        var rows = await (await reader.FindingsAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(f => EF.Functions.ILike(f.Number, pattern, "\\") || EF.Functions.ILike(f.TitleAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(f.TitleEn ?? string.Empty, pattern, "\\") || EF.Functions.ILike(f.Condition ?? string.Empty, pattern, "\\"))
            .OrderBy(f => f.Number).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(f => Hit(f, 1)).ToList();
    }

    public async Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var hits = await (await reader.FindingsAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(f => f.Id != excludeId)
            .Select(f => new
            {
                Finding = f,
                Score = Math.Max(EF.Functions.TrigramsSimilarity(f.TitleEn ?? string.Empty, text), EF.Functions.TrigramsSimilarity(f.Condition ?? string.Empty, text)),
            })
            .Where(x => x.Score > 0.2).OrderByDescending(x => x.Score).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return hits.Select(h => Hit(h.Finding, h.Score)).ToList();
    }

    private static RecordSearchHit Hit(Finding f, double score) =>
        new(Finding.EntityType, f.Id, AuditPermissions.SystemCode, f.Number, AuditNotifier.Short(f.TitleAr ?? f.TitleEn), AuditNotifier.Short(f.TitleEn ?? f.TitleAr), f.Status,
            AuditService.Link(f), score);
}
