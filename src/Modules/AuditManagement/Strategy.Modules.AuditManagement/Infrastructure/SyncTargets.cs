using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.DocumentRegistry.Contracts;
using Strategy.Platform.Integration;

namespace Strategy.Modules.AuditManagement.Infrastructure;

/// <summary>
/// Audit Management record types the standby sync may write (SPEC-ADDENDUM §G3, ADR 0202): audit report, finding and recommendation
/// (Layer 1). Only the listed properties are written; synced records arrive in their Salesforce state (statuses, completion, revised dates)
/// without follow-up reviews, <c>AuditFindingClosed</c> events or department notices.
/// </summary>
internal static class AuditSyncTargets
{
    public static IServiceCollection AddAuditSyncTargets(this IServiceCollection services)
    {
        const string System = AuditPermissions.SystemCode;
        services.AddSyncTarget(SyncTarget.Entity<AuditMgmtDbContext, AuditReport>(AuditLinkTypes.Report, System, "تقرير تدقيق", "Audit report")
            .Text("number", nameof(AuditReport.Number), required: true)
            .Text("titleAr", nameof(AuditReport.TitleAr)).Text("titleEn", nameof(AuditReport.TitleEn))
            .Lookup("type", nameof(AuditReport.TypeCode), AuditPermissions.ReportTypeLookup)
            .Text("auditorBody", nameof(AuditReport.AuditorBody))
            .Person("leadAuditorEmployeeId", nameof(AuditReport.LeadAuditorEmployeeId))
            .Date("periodFrom", nameof(AuditReport.PeriodFrom)).Date("periodTo", nameof(AuditReport.PeriodTo))
            .Date("reportDate", nameof(AuditReport.ReportDate))
            .Text("distributionNote", nameof(AuditReport.DistributionNote))
            .Text("summary", nameof(AuditReport.Summary))
            .Choice("status", nameof(AuditReport.Status), ReportStatuses.All, required: true)
            .Text("cancelReason", nameof(AuditReport.CancelReason))
            .Timestamp("issuedAt", nameof(AuditReport.IssuedAt)).Timestamp("closedAt", nameof(AuditReport.ClosedAt))
            .Default("type", ReportTypes.InternalAudit)
            .Default("status", ReportStatuses.Issued));

        services.AddSyncTarget(SyncTarget.Entity<AuditMgmtDbContext, Finding>(AuditLinkTypes.Finding, System, "ملاحظة تدقيق", "Audit finding")
            .Text("number", nameof(Finding.Number), required: true)
            .Reference("reportId", nameof(Finding.ReportId), AuditLinkTypes.Report, required: true)
            .Text("titleAr", nameof(Finding.TitleAr)).Text("titleEn", nameof(Finding.TitleEn))
            .Lookup("type", nameof(Finding.TypeCode), AuditPermissions.FindingTypeLookup)
            .Text("condition", nameof(Finding.Condition)).Text("criteria", nameof(Finding.Criteria))
            .Reference("criteriaDocumentId", nameof(Finding.CriteriaDocumentId), DocumentResource.Type)
            .Text("cause", nameof(Finding.Cause)).Text("effect", nameof(Finding.Effect))
            .Date("findingDate", nameof(Finding.FindingDate), required: true)
            .Unit("responsibleUnitId", nameof(Finding.ResponsibleUnitId), required: true)
            .Lookup("priority", nameof(Finding.Priority), AuditPermissions.PriorityLookup)
            .Bool("repeat", nameof(Finding.Repeat))
            .Reference("repeatOfFindingId", nameof(Finding.RepeatOfFindingId), AuditLinkTypes.Finding)
            .Date("expectedClosingOn", nameof(Finding.ExpectedClosingOn)).Date("actualClosedOn", nameof(Finding.ActualClosedOn))
            .Choice("status", nameof(Finding.Status), AuditFindingStatuses.All, required: true)
            .Ratio("completion", nameof(Finding.Completion))
            .Person("auditorEmployeeId", nameof(Finding.AuditorEmployeeId))
            .Text("cancelReason", nameof(Finding.CancelReason))
            .Default("type", FindingTypes.Finding)
            .Default("priority", AuditPriorities.Medium)
            .Default("status", AuditFindingStatuses.Open)
            .After<FindingSyncHook>());

        services.AddSyncTarget(SyncTarget.Entity<AuditMgmtDbContext, Recommendation>(Recommendation.EntityType, System, "توصية تدقيق", "Audit recommendation")
            .Text("number", nameof(Recommendation.Number), required: true)
            .Reference("findingId", nameof(Recommendation.FindingId), AuditLinkTypes.Finding, required: true)
            .Text("actionAr", nameof(Recommendation.ActionAr)).Text("actionEn", nameof(Recommendation.ActionEn))
            .Lookup("managementResponse", nameof(Recommendation.ManagementResponseCode), AuditPermissions.ResponseLookup)
            .Text("managementComment", nameof(Recommendation.ManagementComment))
            .Text("implementationPlan", nameof(Recommendation.ImplementationPlan))
            .Person("contactEmployeeId", nameof(Recommendation.ContactEmployeeId))
            .Date("dueOn", nameof(Recommendation.DueOn), required: true)
            .Date("revisedDueOn", nameof(Recommendation.RevisedDueOn)).Text("revisedReason", nameof(Recommendation.RevisedReason))
            .Choice("status", nameof(Recommendation.Status), RecommendationStatuses.All, required: true)
            .Ratio("latestPct", nameof(Recommendation.LatestPct))
            .Date("completedOn", nameof(Recommendation.CompletedOn))
            .Date("lastFollowUpOn", nameof(Recommendation.LastFollowUpOn)).Date("nextFollowUpOn", nameof(Recommendation.NextFollowUpOn))
            .Text("lastComment", nameof(Recommendation.LastComment))
            .Text("riskAcceptanceJustification", nameof(Recommendation.RiskAcceptanceJustification))
            .Text("riskAcceptanceReference", nameof(Recommendation.RiskAcceptanceReference))
            .Date("riskAcceptedOn", nameof(Recommendation.RiskAcceptedOn))
            .Text("notes", nameof(Recommendation.Notes))
            .Text("cancelReason", nameof(Recommendation.CancelReason))
            .Default("status", RecommendationStatuses.NotStarted));

        return services;
    }
}

/// <summary>
/// Derived data of a synced finding, as when the Audit Office records one: the computed year, the concerned department (configured
/// department level above the responsible unit) and its read-only record role for department users (ADR 0130).
/// </summary>
internal sealed class FindingSyncHook(AuditReader reader, AuditService service) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (AuditMgmtDbContext)transactionContext;
        var finding = await db.Findings.FirstAsync(f => f.Id == recordId, cancellationToken).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        if (finding.Year != finding.FindingDate.Year)
        {
            db.Entry(finding).Property(nameof(Finding.Year)).CurrentValue = finding.FindingDate.Year;
        }

        var department = await reader.DepartmentOfAsync(finding.ResponsibleUnitId, settings.DepartmentLevelCode, cancellationToken).ConfigureAwait(false);
        if (department != finding.DepartmentUnitId)
        {
            finding.SetDepartment(department);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await service.SyncDepartmentGrantAsync(finding, settings, cancellationToken).ConfigureAwait(false);
    }
}
