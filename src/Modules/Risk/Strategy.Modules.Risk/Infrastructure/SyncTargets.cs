using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Integration;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Risk.Infrastructure;

/// <summary>
/// Risk record types the standby sync may write (SPEC-ADDENDUM §G3, ADR 0202): enterprise risk, control and KRI. Only the listed properties
/// are written. Scores and bands are never copied: the hooks re-rate the risk with the installation's framework (scoring method, bands,
/// control effectiveness, appetite — the Salesforce additive score is not kept, the risk has no source-score field) and keep the action-owner
/// / control-owner record roles in line, as the module's own write paths do. No assessment row, approval or integration event is produced.
/// </summary>
internal static class RiskSyncTargets
{
    /// <summary>The KPI card target type (declared by the KPI module; referenced by id through its external reference, never by a foreign key).</summary>
    private const string KpiCardType = "kpi.card";

    private static readonly IReadOnlyList<string> ChildStatusCodes = [ControlStatuses.Active, ControlStatuses.Retired];

    public static IServiceCollection AddRiskSyncTargets(this IServiceCollection services)
    {
        const string System = RiskPermissions.SystemCode;
        services.AddSyncTarget(SyncTarget.Entity<RiskDbContext, RiskRecord>(RiskRecord.EntityType, System, "خطر مؤسسي", "Enterprise risk")
            .Text("number", nameof(RiskRecord.Number), required: true)
            .WholeNumber("registerYear", nameof(RiskRecord.RegisterYear))
            .Text("titleAr", nameof(RiskRecord.TitleAr)).Text("titleEn", nameof(RiskRecord.TitleEn))
            .Text("cause", nameof(RiskRecord.Cause)).Text("eventText", nameof(RiskRecord.EventText)).Text("consequence", nameof(RiskRecord.Consequence))
            .Lookup("determinedBy", nameof(RiskRecord.DeterminedByCode), RiskPermissions.DeterminedByLookup)
            .Date("identifiedOn", nameof(RiskRecord.IdentifiedOn), required: true).Date("exposureStart", nameof(RiskRecord.ExposureStart))
            .Choice("riskType", nameof(RiskRecord.RiskType), RiskTypes.All)
            .Choice("status", nameof(RiskRecord.Status), RiskStatuses.All, required: true)
            .Bool("emerging", nameof(RiskRecord.Emerging)).Bool("opportunity", nameof(RiskRecord.Opportunity))
            .Unit("unitId", nameof(RiskRecord.UnitId), required: true)
            .Person("ownerEmployeeId", nameof(RiskRecord.OwnerEmployeeId)).Person("coordinatorEmployeeId", nameof(RiskRecord.CoordinatorEmployeeId))
            .Person("followUpEmployeeId", nameof(RiskRecord.FollowUpEmployeeId))
            .Text("processes", nameof(RiskRecord.Processes))
            .WholeNumber("inherentLikelihood", nameof(RiskRecord.InherentLikelihood), required: true)
            .WholeNumber("inherentImpact", nameof(RiskRecord.InherentImpact), required: true)
            .Text("inherentImpactArea", nameof(RiskRecord.InherentImpactArea))
            .WholeNumber("residualLikelihood", nameof(RiskRecord.ResidualLikelihood)).WholeNumber("residualImpact", nameof(RiskRecord.ResidualImpact))
            .WholeNumber("targetLikelihood", nameof(RiskRecord.TargetLikelihood)).WholeNumber("targetImpact", nameof(RiskRecord.TargetImpact))
            .Date("targetDate", nameof(RiskRecord.TargetDate))
            .Lookup("response", nameof(RiskRecord.ResponseCode), RiskPermissions.ResponseLookup)
            .Bool("needsFollowUp", nameof(RiskRecord.NeedsFollowUp))
            .Lookup("closureReason", nameof(RiskRecord.ClosureReasonCode), RiskPermissions.ClosureReasonLookup)
            .Text("closureNote", nameof(RiskRecord.ClosureNote)).Text("rejectionReason", nameof(RiskRecord.RejectionReason))
            .Date("lastReviewOn", nameof(RiskRecord.LastReviewOn))
            .Default("status", RiskStatuses.Active)
            .After<RiskSyncHook>());

        services.AddSyncTarget(SyncTarget.Entity<RiskDbContext, Control>(Control.EntityType, System, "ضابط رقابي", "Risk control")
            .Reference("riskId", nameof(Control.RiskId), RiskRecord.EntityType, required: true)
            .Text("nameAr", nameof(Control.NameAr)).Text("nameEn", nameof(Control.NameEn)).Text("description", nameof(Control.Description))
            .Lookup("type", nameof(Control.TypeCode), RiskPermissions.ControlTypeLookup)
            .Lookup("nature", nameof(Control.NatureCode), RiskPermissions.ControlNatureLookup)
            .Lookup("frequency", nameof(Control.FrequencyCode), RiskPermissions.ControlFrequencyLookup)
            .Person("ownerEmployeeId", nameof(Control.OwnerEmployeeId))
            .Text("effectiveness", nameof(Control.EffectivenessCode)) // a code of the configurable framework (checked by the hook)
            .Date("lastAssessedOn", nameof(Control.LastAssessedOn)).Text("evidence", nameof(Control.Evidence))
            .Choice("status", nameof(Control.Status), ChildStatusCodes)
            .Default("status", ControlStatuses.Active)
            .After<RiskControlSyncHook>());

        services.AddSyncTarget(SyncTarget.Entity<RiskDbContext, Kri>(Kri.EntityType, System, "مؤشر مخاطر رئيسي", "Key risk indicator")
            .Reference("riskId", nameof(Kri.RiskId), RiskRecord.EntityType, required: true)
            .Reference("kpiCardId", nameof(Kri.KpiCardId), KpiCardType, required: true)
            .Choice("basis", nameof(Kri.Basis), KriBases.All)
            .Choice("direction", nameof(Kri.Direction), KriDirections.All)
            .Number("warningThreshold", nameof(Kri.WarningThreshold))
            .Number("breachThreshold", nameof(Kri.BreachThreshold), required: true)
            .Choice("status", nameof(Kri.Status), ChildStatusCodes)
            .Default("status", ControlStatuses.Active)
            .After<RiskKriSyncHook>());

        return services;
    }
}

/// <summary>Pure rules of the risk sync hooks (unit-tested).</summary>
internal static class RiskSyncRules
{
    /// <summary>
    /// Re-rates a synced risk with the framework (the source score is ignored): inherent / residual / target scores and bands, residual status,
    /// appetite and the escalation rule (R4). True when a score or band changed.
    /// </summary>
    public static bool ReRate(RiskRecord risk, RiskScorer scorer, Guid versionId, IReadOnlyList<string?> controlRatings, decimal? appetite, RiskSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(risk);
        ArgumentNullException.ThrowIfNull(scorer);
        var changed = risk.ApplyRatings(RiskRater.Compute(risk, scorer, versionId, controlRatings, appetite));
        RiskRater.ApplyEscalation(risk, settings, scorer, now);
        return changed;
    }

    /// <summary>A control effectiveness code the framework knows, else null (unrated) — Salesforce values without a mapping never count.</summary>
    public static string? KnownEffectiveness(RiskScorer scorer, string? code)
    {
        ArgumentNullException.ThrowIfNull(scorer);
        return code is not null && scorer.Factor(code) is not null ? code : null;
    }
}

/// <summary>Re-rates a synced risk (current framework for a new one, its own version otherwise) and the derived record roles, in the sync transaction.</summary>
internal sealed class RiskSyncRating(ISettingsStore settings, IClock clock, IIntegrationEventPublisher events, IRecordRoleService recordRoles)
{
    public async Task ReRateAsync(RiskDbContext db, Guid riskId, bool moveToCurrent, CancellationToken ct)
    {
        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId, ct).ConfigureAwait(false);
        if (risk is null)
        {
            return;
        }

        var frameworks = new FrameworkStore(db);
        var (version, scorer) = moveToCurrent || risk.FrameworkVersionId == Guid.Empty
            ? (await frameworks.CurrentAsync(ct).ConfigureAwait(false), await frameworks.ScorerAsync(ct).ConfigureAwait(false))
            : await frameworks.ForAsync(risk.FrameworkVersionId, ct).ConfigureAwait(false);
        var rater = new RiskRater(db, frameworks, events);
        var controls = await db.Controls.AsNoTracking().Where(c => c.RiskId == risk.Id && c.Status == ControlStatuses.Active).Select(c => c.EffectivenessCode)
            .ToListAsync(ct).ConfigureAwait(false);
        var appetite = await rater.AppetiteAsync(risk.CategoryCodes, ct).ConfigureAwait(false);
        RiskSyncRules.ReRate(risk, scorer, version.Id, controls, appetite, await settings.GetAsync<RiskSettings>(ct).ConfigureAwait(false), clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await new RiskRoleSync(db, recordRoles).SyncAsync(risk.Id, ct).ConfigureAwait(false);
    }

    public static async Task<RiskScorer> ScorerAsync(RiskDbContext db, CancellationToken ct) => await new FrameworkStore(db).ScorerAsync(ct).ConfigureAwait(false);
}

/// <summary>
/// After a synced risk: register year from the identification date (new risks), re-rating by the framework and the action / control owner
/// record roles. The risk owner, coordinator and unit scopes work from the record's own fields (<see cref="RiskScope"/>), so no owner record
/// role is needed.
/// </summary>
internal sealed class RiskSyncHook(RiskSyncRating rating) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        var db = (RiskDbContext)transactionContext;
        var risk = await db.Risks.FirstAsync(r => r.Id == recordId, cancellationToken).ConfigureAwait(false);
        if (risk.RegisterYear == 0)
        {
            db.Entry(risk).Property(r => r.RegisterYear).CurrentValue = risk.IdentifiedOn.Year;
        }

        await rating.ReRateAsync(db, recordId, moveToCurrent: created, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>After a synced control: an effectiveness code the framework does not know is cleared, then its risk is re-rated (residual) and the control-owner roles synced.</summary>
internal sealed class RiskControlSyncHook(RiskSyncRating rating) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (RiskDbContext)transactionContext;
        var control = await db.Controls.FirstAsync(c => c.Id == recordId, cancellationToken).ConfigureAwait(false);
        var scorer = await RiskSyncRating.ScorerAsync(db, cancellationToken).ConfigureAwait(false);
        var known = RiskSyncRules.KnownEffectiveness(scorer, control.EffectivenessCode);
        if (known != control.EffectivenessCode)
        {
            db.Entry(control).Property(c => c.EffectivenessCode).CurrentValue = known;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await rating.ReRateAsync(db, control.RiskId, moveToCurrent: false, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>After a synced KRI: the KPI card code / name snapshot (as the module's own KRI form stores it) through the KPI contract.</summary>
internal sealed class RiskKriSyncHook(IServiceProvider services, IRequestContext context) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (RiskDbContext)transactionContext;
        var kri = await db.Kris.FirstAsync(k => k.Id == recordId, cancellationToken).ConfigureAwait(false);
        if (services.GetService(typeof(IKpiCatalog)) is not IKpiCatalog catalog
            || await catalog.GetAsync(kri.KpiCardId, context.User, cancellationToken).ConfigureAwait(false) is not { } card)
        {
            return;
        }

        var entry = db.Entry(kri);
        entry.Property(k => k.CardCode).CurrentValue = card.Code;
        entry.Property(k => k.CardNameAr).CurrentValue = card.NameAr;
        entry.Property(k => k.CardNameEn).CurrentValue = card.NameEn;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
