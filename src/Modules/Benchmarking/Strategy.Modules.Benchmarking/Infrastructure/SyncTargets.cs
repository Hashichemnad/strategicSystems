using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Benchmarking.Application;
using Strategy.Modules.Benchmarking.Contracts;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Platform.Integration;

namespace Strategy.Modules.Benchmarking.Infrastructure;

/// <summary>
/// Benchmarking record types the standby sync may write (SPEC-ADDENDUM §G3, ADR 0202): the benchmark study header. Only the listed properties
/// are written; a synced study arrives at its Salesforce stage and status without Request Framework request, PMO approvals, CAP hand-off or
/// notifications.
/// </summary>
internal static class BenchSyncTargets
{
    public static IServiceCollection AddBenchSyncTargets(this IServiceCollection services)
    {
        services.AddSyncTarget(SyncTarget.Entity<BenchDbContext, Study>(BenchLinkTypes.Study, BenchLinkTypes.SystemCode, "دراسة مقارنة معيارية", "Benchmark study")
            .Text("number", nameof(Study.Number), required: true)
            .Text("requestNumber", nameof(Study.RequestNumber))
            .Text("titleAr", nameof(Study.TitleAr)).Text("titleEn", nameof(Study.TitleEn))
            .Unit("responsibleUnitId", nameof(Study.ResponsibleUnitId), required: true)
            .Unit("sectionUnitId", nameof(Study.SectionUnitId))
            .Person("responsibleEmployeeId", nameof(Study.ResponsibleEmployeeId))
            .Person("requesterEmployeeId", nameof(Study.RequesterEmployeeId))
            .WholeNumber("targetYear", nameof(Study.TargetYear), required: true)
            .WholeNumber("targetQuarter", nameof(Study.TargetQuarter), required: true)
            .Lookup("comparisonType", nameof(Study.ComparisonTypeCode), BenchPermissions.ComparisonTypeLookup)
            .Lookup("purpose", nameof(Study.PurposeCode), BenchPermissions.PurposeLookup)
            .Text("scope", nameof(Study.Scope))
            .Choice("stage", nameof(Study.Stage), StudyStages.All, required: true)
            .Choice("status", nameof(Study.Status), StudyStatuses.All, required: true)
            .Timestamp("planApprovedAt", nameof(Study.PlanApprovedAt))
            .Timestamp("analysisApprovedAt", nameof(Study.AnalysisApprovedAt))
            .Timestamp("closedAt", nameof(Study.ClosedAt))
            .Date("impactDueOn", nameof(Study.ImpactDueOn))
            .Text("cancelReason", nameof(Study.CancelReason))
            .Default("stage", StudyStages.Plan)
            .Default("status", StudyStatuses.Active)
            .After<StudySyncHook>());

        return services;
    }
}

/// <summary>
/// Derived data of a synced study: a synced study has no Request Framework request (the Plan stage ran in Salesforce), so its unique
/// request reference is set to the study's own id — never the id of a real request — and the coordinator record role is granted through
/// the module's own role sync (ADR 0126), so scope-based access works as for local studies.
/// </summary>
internal sealed class StudySyncHook(BenchRoles roles) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (BenchDbContext)transactionContext;
        var study = await db.Studies.FirstAsync(s => s.Id == recordId, cancellationToken).ConfigureAwait(false);
        if (study.RequestId == Guid.Empty)
        {
            db.Entry(study).Property(nameof(Study.RequestId)).CurrentValue = study.Id;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await roles.SyncAsync(study, cancellationToken).ConfigureAwait(false);
    }
}
