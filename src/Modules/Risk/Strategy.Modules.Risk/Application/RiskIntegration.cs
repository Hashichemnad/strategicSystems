using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Risk.Contracts;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;

namespace Strategy.Modules.Risk.Application;

/// <summary>
/// KRIs react to approved KPI results (ADR 0104): the reading is stored on every active KRI of the card; a breach flags the risk for
/// reassessment, notifies the owner, coordinator and Risk Office and publishes <c>RiskKriBreached</c>. Idempotent by result id.
/// </summary>
internal sealed class KpiResultApprovedHandler(IServiceScopeFactory scopes) : IIntegrationEventHandler<KpiResultApproved>
{
    public async Task HandleAsync(KpiResultApproved integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.NotApplicable)
        {
            return;
        }

        await using var scope = Jobs.RiskJobScope.Create(scopes);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<RiskDbContext>();
        var notifier = services.GetRequiredService<RiskNotifier>();
        var events = services.GetRequiredService<IIntegrationEventPublisher>();
        var settings = await services.GetRequiredService<RiskReader>().SettingsAsync(cancellationToken).ConfigureAwait(false);
        var clock = services.GetRequiredService<IClock>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var kris = await db.Kris.Where(k => k.KpiCardId == integrationEvent.CardId && k.Status == ControlStatuses.Active).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var kri in kris.Where(k => k.LastResultId != integrationEvent.ResultId))
        {
            var value = kri.Basis == KriBases.Performance ? integrationEvent.Capped : integrationEvent.Achieve;
            if (value is not { } v)
            {
                continue;
            }

            var level = KriEvaluator.Level(v, kri.Direction, kri.WarningThreshold, kri.BreachThreshold);
            kri.Record(integrationEvent.ResultId, v, level, $"{integrationEvent.PeriodCode.ToUpperInvariant()} {integrationEvent.Year}", clock.UtcNow);
            var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == kri.RiskId, cancellationToken).ConfigureAwait(false);
            if (risk is null || risk.Status != RiskStatuses.Active)
            {
                continue;
            }

            var label = $"{kri.CardCode} ({v.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            var link = $"/risk/risks/{risk.Id:D}?tab=kris";
            if (level == KriLevels.Breach)
            {
                risk.FlagReassessment(ReassessmentReasons.KriBreach);
                await notifier.SendAsync(RiskNotifications.KriBreach, [risk.OwnerEmployeeId, risk.CoordinatorEmployeeId], RiskNotifier.Values(risk, text: label), link, db,
                    cancellationToken, [RiskPermissions.RiskOfficeRoleKey]).ConfigureAwait(false);
                await events.PublishAsync(new RiskKriBreached(risk.Id, risk.Number, integrationEvent.CardId, integrationEvent.ResultId, v, kri.BreachThreshold, kri.Direction,
                    clock.UtcNow), db, cancellationToken).ConfigureAwait(false);
            }
            else if (level == KriLevels.Warning && settings.KriWarningNotifies)
            {
                await notifier.SendAsync(RiskNotifications.Reassessment, [risk.OwnerEmployeeId], RiskNotifier.Values(risk, text: label), link, db, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>The shared scoring service for Portfolio project risks (ADR 0101).</summary>
internal sealed class RiskScoringService(FrameworkStore store) : IRiskScoring
{
    public async Task<SharedRiskScore> ScoreProjectRiskAsync(int probability, int impact, int? controlEvaluation, CancellationToken cancellationToken = default)
    {
        var version = await store.CurrentAsync(cancellationToken).ConfigureAwait(false);
        var scorer = await store.ScorerAsync(cancellationToken).ConfigureAwait(false);
        var (inherent, inherentBand, residual, residualBand) = scorer.ScoreProject(probability, impact, controlEvaluation);
        return new SharedRiskScore(inherent, inherentBand.Code, residual, residualBand.Code, version.VersionNo);
    }
}

/// <summary>Risks and events for CAP / Audit (design §11).</summary>
internal sealed class RiskDirectory(RiskDbContext db, RiskReader reader, IServiceScopeFactory scopes) : IRiskDirectory
{
    public async Task<RiskSummary?> GetRiskAsync(Guid riskId, ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
        await reader.RiskAsync(riskId, user, cancellationToken).ConfigureAwait(false) is { } r ? Summary(r) : null;

    public async Task<IReadOnlyList<RiskSummary>> SearchAsync(string? text, ClaimsPrincipal user, int take = 20, CancellationToken cancellationToken = default)
    {
        var source = await reader.RisksAsync(user, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var pattern = Like.Pattern(text);
            source = source.Where(r => EF.Functions.ILike(r.Number, pattern, "\\") || EF.Functions.ILike(r.TitleAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(r.TitleEn ?? string.Empty, pattern, "\\"));
        }

        return (await source.OrderBy(r => r.Number).Take(Math.Clamp(take, 1, 100)).ToListAsync(cancellationToken).ConfigureAwait(false)).Select(Summary).ToList();
    }

    public async Task<RiskEventSummary?> GetEventAsync(Guid eventId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var visible = await reader.RisksAsync(user, cancellationToken).ConfigureAwait(false);
        var riskIds = await db.EventRisks.AsNoTracking().Where(l => l.EventId == eventId).Select(l => l.RiskId).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (riskIds.Count == 0 || !await visible.AnyAsync(r => riskIds.Contains(r.Id), cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var e = await db.Events.AsNoTracking().FirstAsync(x => x.Id == eventId, cancellationToken).ConfigureAwait(false);
        return new RiskEventSummary(e.Id, e.Number, e.OccurredOn, e.Description, e.FinancialImpact, e.NearMiss, e.UnitId, riskIds, e.CapRemarkId,
            $"/risk/risks/{riskIds[0]:D}?tab=events");
    }

    public async Task<bool> LinkCapRemarkAsync(Guid eventId, Guid remarkId, CancellationToken cancellationToken = default)
    {
        await using var scope = Jobs.RiskJobScope.Create(scopes);
        var scoped = scope.ServiceProvider.GetRequiredService<RiskDbContext>();
        var e = await scoped.Events.FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken).ConfigureAwait(false);
        if (e is null)
        {
            return false;
        }

        if (e.CapRemarkId != remarkId)
        {
            e.LinkCapRemark(remarkId);
            await scoped.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<bool> FlagForReassessmentAsync(Guid riskId, string reason, CancellationToken cancellationToken = default)
    {
        await using var scope = Jobs.RiskJobScope.Create(scopes);
        var scoped = scope.ServiceProvider.GetRequiredService<RiskDbContext>();
        var risk = await scoped.Risks.FirstOrDefaultAsync(r => r.Id == riskId, cancellationToken).ConfigureAwait(false);
        if (risk is null)
        {
            return false;
        }

        if (risk.Status == RiskStatuses.Active)
        {
            risk.FlagReassessment(ReassessmentReasons.All.Contains(reason) ? reason : ReassessmentReasons.AuditFinding);
            await scoped.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<IReadOnlyList<RiskControlSummary>> ListControlsAsync(Guid riskId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (await reader.RiskAsync(riskId, user, cancellationToken).ConfigureAwait(false) is null)
        {
            return [];
        }

        return await db.Controls.AsNoTracking().Where(c => c.RiskId == riskId && c.Status == ControlStatuses.Active).OrderBy(c => c.NameEn ?? c.NameAr)
            .Select(c => new RiskControlSummary(c.Id, c.RiskId, c.NameAr, c.NameEn, c.EffectivenessCode, c.Status)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> LowerControlEffectivenessAsync(Guid controlId, string reason, CancellationToken cancellationToken = default)
    {
        await using var scope = Jobs.RiskJobScope.Create(scopes);
        var services = scope.ServiceProvider;
        var scoped = services.GetRequiredService<RiskDbContext>();
        var control = await scoped.Controls.FirstOrDefaultAsync(c => c.Id == controlId && c.Status == ControlStatuses.Active, cancellationToken).ConfigureAwait(false);
        if (control is null)
        {
            return null;
        }

        var scorer = await services.GetRequiredService<FrameworkStore>().ScorerAsync(cancellationToken).ConfigureAwait(false);
        var levels = scorer.Content.ControlEffectiveness.OrderByDescending(e => e.Factor).Select(e => e.Code).ToList();
        var index = control.EffectivenessCode is { } current ? levels.IndexOf(current) : -1;
        var next = index < 0 ? levels.Count > 1 ? levels[1] : null : index + 1 < levels.Count ? levels[index + 1] : null;
        if (next is null)
        {
            return null;
        }

        var risk = await scoped.Risks.FirstAsync(r => r.Id == control.RiskId, cancellationToken).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(services.GetRequiredService<IClock>().UtcNow.UtcDateTime);
        await using var transaction = await scoped.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        control.Rate(next, today);
        if (risk.Status == RiskStatuses.Active)
        {
            risk.FlagReassessment(ReassessmentReasons.All.Contains(reason) ? reason : ReassessmentReasons.AuditFinding);
        }

        if (next == scorer.LowestEffectiveness)
        {
            await services.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(new RiskControlRatedIneffective(control.Id, risk.Id, risk.Number, control.NameAr,
                control.NameEn, risk.UnitId, control.OwnerEmployeeId, services.GetRequiredService<IClock>().UtcNow), scoped, cancellationToken).ConfigureAwait(false);
        }

        await scoped.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return next;
    }

    private static RiskSummary Summary(RiskRecord r) =>
        new(r.Id, r.Number, r.TitleAr, r.TitleEn, r.Status, r.UnitId, r.OwnerEmployeeId, r.ResidualScore, r.ResidualBand, $"/risk/risks/{r.Id:D}");
}

/// <summary>The KPI card lists the risks whose active KRIs use it (ADR 0104).</summary>
internal sealed class RiskKpiLinks(RiskDbContext db) : IKpiLinkedRecordSource
{
    public string ResourceType => RiskRecord.EntityType;

    public async Task<IReadOnlyList<Guid>> GetLinkedRecordIdsAsync(Guid cardId, CancellationToken cancellationToken) =>
        await db.Kris.AsNoTracking().Where(k => k.KpiCardId == cardId && k.Status == ControlStatuses.Active).Select(k => k.RiskId).Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>Risks are task-able and remark sources (SPEC §0.0, §3.8 CAP): badge = number, title, status.</summary>
internal sealed class RiskLinkTargets(RiskReader reader) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        RiskRecord.EntityType, RiskPermissions.SystemCode, "خطر", "Risk", [LinkCapabilities.TaskAble, LinkCapabilities.RemarkSource], RiskPermissions.Read,
        "/risk/risks/{id}", RiskPermissions.Edit);

    public string ResourceType => RiskRecord.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var risks = await (await reader.RisksAsync(user, cancellationToken).ConfigureAwait(false)).Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return risks.ToDictionary(r => r.Id, r => new LinkTarget(r.Id, r.Number, r.TitleAr, r.TitleEn, r.Status, null, null, Tone(r.Status),
            r.Status is RiskStatuses.Closed or RiskStatuses.Rejected));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.RiskAsync(id, user, cancellationToken).ConfigureAwait(false) is { } risk && await reader.CanAsync(user, permission, risk, cancellationToken).ConfigureAwait(false);

    public static string Tone(string status) => status switch
    {
        RiskStatuses.Active => "success",
        RiskStatuses.UnderReview => "info",
        RiskStatuses.Draft => "warning",
        _ => "neutral",
    };
}

/// <summary>Treatment actions are task-able: the work can be run in Task Management while the action keeps the risk view (design §5.2).</summary>
internal sealed class ActionLinkTargets(RiskDbContext db, RiskReader reader) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        TreatmentAction.EntityType, RiskPermissions.SystemCode, "إجراء معالجة خطر", "Risk treatment action", [LinkCapabilities.TaskAble], RiskPermissions.Read,
        "/risk/actions/{id}", RiskPermissions.ActionUpdate);

    public string ResourceType => TreatmentAction.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var visible = await reader.RisksAsync(user, cancellationToken).ConfigureAwait(false);
        var actions = await db.Actions.AsNoTracking().Where(a => ids.Contains(a.Id)).Join(visible, a => a.RiskId, r => r.Id, (a, r) => a).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return actions.ToDictionary(a => a.Id, a => new LinkTarget(a.Id, a.Number, a.TitleAr, a.TitleEn, a.Status, null, null,
            a.Status == ActionStatuses.Completed ? "success" : a.Status == ActionStatuses.Cancelled ? "neutral" : "info",
            a.Status is ActionStatuses.Completed or ActionStatuses.Cancelled));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var action = await db.Actions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);
        return action is not null && await reader.RiskAsync(action.RiskId, user, cancellationToken).ConfigureAwait(false) is { } risk
            && await reader.CanAsync(user, permission, risk, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Global search over risks (number, titles, event text), scope-filtered in the query.</summary>
internal sealed class RiskSearchSource(RiskReader reader) : IRecordSearchSource
{
    public string ResourceType => RiskRecord.EntityType;

    public string SystemCode => RiskPermissions.SystemCode;

    public string ReadPermission => RiskPermissions.Read;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var pattern = Like.Pattern(text);
        var risks = await (await reader.RisksAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(r => EF.Functions.ILike(r.Number, pattern, "\\") || EF.Functions.ILike(r.TitleAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(r.TitleEn ?? string.Empty, pattern, "\\") || EF.Functions.ILike(r.EventText ?? string.Empty, pattern, "\\"))
            .OrderBy(r => r.Number).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return risks.Select(r => Hit(r, 1)).ToList();
    }

    public async Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var hits = await (await reader.RisksAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(r => r.Id != excludeId && r.Status != RiskStatuses.Rejected)
            .Select(r => new
            {
                Risk = r,
                Score = Math.Max(Math.Max(EF.Functions.TrigramsSimilarity(r.TitleEn ?? string.Empty, text), EF.Functions.TrigramsSimilarity(r.TitleAr ?? string.Empty, text)),
                    EF.Functions.TrigramsSimilarity(r.EventText ?? string.Empty, text)),
            })
            .Where(x => x.Score > 0.2).OrderByDescending(x => x.Score).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return hits.Select(h => Hit(h.Risk, h.Score)).ToList();
    }

    private static RecordSearchHit Hit(RiskRecord r, double score) =>
        new(RiskRecord.EntityType, r.Id, RiskPermissions.SystemCode, r.Number, r.TitleAr, r.TitleEn, r.Status, $"/risk/risks/{r.Id:D}", score);
}

/// <summary>Risk documents: read with the risk, written by its editors.</summary>
internal sealed class RiskFiles(RiskReader reader, RiskViews views) : IFileOwnerAccess
{
    public string OwnerType => RiskRecord.EntityType;

    public string AttachmentPurpose => RiskFilePurposes.RiskDocument.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.RiskAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.RiskAsync(id, user, cancellationToken).ConfigureAwait(false) is { } risk
        && (await views.PermissionsAsync(risk, user, cancellationToken).ConfigureAwait(false)).ManageChildren;
}

/// <summary>Action evidence: read with the risk, written by the action owner or the risk's editors.</summary>
internal sealed class ActionFiles(RiskDbContext db, RiskReader reader, RiskViews views) : IFileOwnerAccess
{
    public string OwnerType => TreatmentAction.EntityType;

    public string AttachmentPurpose => RiskFilePurposes.ActionEvidence.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        await RiskOfAsync(ownerId, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken)
    {
        if (await RiskOfAsync(ownerId, user, cancellationToken).ConfigureAwait(false) is not ({ } risk, { } action) || !risk.IsOpen)
        {
            return false;
        }

        return (await views.PermissionsAsync(risk, user, cancellationToken).ConfigureAwait(false)).ManageChildren
            || (action.OwnerEmployeeId is { } owner && user.EmployeeIdOf() == owner
                && await reader.CanAsync(user, RiskPermissions.ActionUpdate, risk, cancellationToken).ConfigureAwait(false));
    }

    private async Task<(RiskRecord Risk, TreatmentAction Action)?> RiskOfAsync(string ownerId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Guid.TryParse(ownerId, out var id) || await db.Actions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false) is not { } action)
        {
            return null;
        }

        return await reader.RiskAsync(action.RiskId, user, ct).ConfigureAwait(false) is { } risk ? (risk, action) : null;
    }
}
