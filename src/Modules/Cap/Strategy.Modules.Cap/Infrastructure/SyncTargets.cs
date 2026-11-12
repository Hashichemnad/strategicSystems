using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Platform.Integration;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Modules.Cap.Infrastructure;

/// <summary>
/// CAP record types the standby sync may write (SPEC-ADDENDUM §G3, ADR 0202): remarks source, remark and corrective action. Only the listed
/// properties are written; synced records arrive in their Salesforce state (status, validation status, achievement) without starting the
/// validation / completion approvals, raising <c>CapRemarkStatusChanged</c> / <c>CapActionCompleted</c> or notifying anyone.
/// </summary>
internal static class CapSyncTargets
{
    /// <summary>Project / initiative target of Portfolio (an action may already be part of one, SPEC §9.3 C).</summary>
    private const string ProjectTarget = "portfolio.project";

    /// <summary>Program request target of Portfolio (budget-needing actions, SPEC §9.4 C2.4).</summary>
    private const string RequestTarget = "portfolio.request";

    private static readonly IReadOnlyList<string> SourceStatuses = ["active", "closed"];
    private static readonly IReadOnlyList<string> PmoDecisions = ["confirmed", "sent-back"];

    public static IServiceCollection AddCapSyncTargets(this IServiceCollection services)
    {
        const string System = CapPermissions.SystemCode;
        services.AddSyncTarget(SyncTarget.Entity<CapDbContext, CapSource>(CapLinkTypes.Source, System, "مصدر ملاحظات", "Remarks source")
            .Text("number", nameof(CapSource.Number), required: true)
            .Text("titleAr", nameof(CapSource.TitleAr)).Text("titleEn", nameof(CapSource.TitleEn))
            .Lookup("sourceType", nameof(CapSource.SourceTypeCode), CapPermissions.SourceTypeLookup)
            .Text("description", nameof(CapSource.Description))
            .Date("receivedOn", nameof(CapSource.ReceivedOn), required: true)
            .Person("ownerEmployeeId", nameof(CapSource.OwnerEmployeeId))
            .Unit("unitId", nameof(CapSource.UnitId))
            .Choice("status", nameof(CapSource.Status), SourceStatuses)
            .Default("sourceType", CapSourceTypes.Other)
            .Default("status", "active"));

        services.AddSyncTarget(SyncTarget.Entity<CapDbContext, Remark>(CapLinkTypes.Remark, System, "ملاحظة", "Remark")
            .Text("number", nameof(Remark.Number), required: true)
            .Reference("sourceId", nameof(Remark.SourceId), CapLinkTypes.Source, required: true)
            .Text("textAr", nameof(Remark.TextAr)).Text("textEn", nameof(Remark.TextEn))
            .Lookup("criterion", nameof(Remark.CriterionCode), CapPermissions.CriterionLookup)
            .Lookup("type", nameof(Remark.TypeCode), CapPermissions.RemarkTypeLookup)
            .Lookup("priority", nameof(Remark.Priority), CapPermissions.PriorityLookup)
            .Unit("departmentUnitId", nameof(Remark.DepartmentUnitId), required: true)
            .Unit("sectionUnitId", nameof(Remark.SectionUnitId))
            .Person("ownerEmployeeId", nameof(Remark.OwnerEmployeeId))
            .Date("assignedOn", nameof(Remark.AssignedOn), required: true)
            .Date("dueOn", nameof(Remark.DueOn), required: true)
            .Timestamp("dueAt", nameof(Remark.DueAt), required: true)
            .Timestamp("respondedAt", nameof(Remark.RespondedAt))
            .Choice("status", nameof(Remark.Status), CapRemarkStatuses.All, required: true)
            .Text("rejectionReason", nameof(Remark.RejectionReason))
            .Choice("pmoDecision", nameof(Remark.PmoDecision), PmoDecisions).Text("pmoDecisionNote", nameof(Remark.PmoDecisionNote))
            .Choice("rootCauseMethod", nameof(Remark.RootCauseMethod), RootCauseMethods.All).Text("rootCauseText", nameof(Remark.RootCauseText))
            .Timestamp("completedAt", nameof(Remark.CompletedAt))
            .Date("verificationDueOn", nameof(Remark.VerificationDueOn))
            .Text("cancelReason", nameof(Remark.CancelReason))
            .Default("type", CapRemarkTypes.Observation)
            .Default("priority", CapPriorities.Medium)
            .Default("status", CapRemarkStatuses.Assigned)
            .After<RemarkSyncHook>());

        services.AddSyncTarget(SyncTarget.Entity<CapDbContext, CapAction>(CapLinkTypes.Action, System, "إجراء تصحيحي", "Corrective action")
            .Text("number", nameof(CapAction.Number), required: true)
            .Reference("homeRemarkId", nameof(CapAction.HomeRemarkId), CapLinkTypes.Remark, required: true)
            .Text("detailsAr", nameof(CapAction.DetailsAr)).Text("detailsEn", nameof(CapAction.DetailsEn))
            .Lookup("type", nameof(CapAction.TypeCode), CapPermissions.ActionTypeLookup)
            .Bool("budgetRequired", nameof(CapAction.BudgetRequired)).Bool("budgetAvailable", nameof(CapAction.BudgetAvailable))
            .Number("estimatedCost", nameof(CapAction.EstimatedCost))
            .Text("relatedProcess", nameof(CapAction.RelatedProcess))
            .Person("assignedEmployeeId", nameof(CapAction.AssignedEmployeeId))
            .Date("plannedStart", nameof(CapAction.PlannedStart)).Date("plannedEnd", nameof(CapAction.PlannedEnd))
            .Date("revisedEnd", nameof(CapAction.RevisedEnd))
            .Choice("validationStatus", nameof(CapAction.ValidationStatus), ValidationStatuses.All, required: true)
            .Text("validationNote", nameof(CapAction.ValidationNote))
            .Reference("projectId", nameof(CapAction.ProjectId), ProjectTarget)
            .Reference("programRequestId", nameof(CapAction.ProgramRequestId), RequestTarget)
            .Date("actualStart", nameof(CapAction.ActualStart)).Date("actualEnd", nameof(CapAction.ActualEnd))
            .Ratio("achievedPct", nameof(CapAction.AchievedPct))
            .Choice("status", nameof(CapAction.Status), ActionStatuses.All, required: true)
            .Text("completionNote", nameof(CapAction.CompletionNote))
            .Timestamp("completedAt", nameof(CapAction.CompletedAt))
            .Text("cancelReason", nameof(CapAction.CancelReason))
            .Default("validationStatus", ValidationStatuses.Draft)
            .Default("status", ActionStatuses.NotStarted)
            .After<ActionSyncHook>());

        return services;
    }
}

/// <summary>
/// Grants the record roles of a synced remark (ADR 0125) — remark owner, assignees of its actions, source-system viewers — through the
/// module's own role sync, so scope-based access works as for local remarks.
/// </summary>
internal sealed class RemarkSyncHook(CapService service) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (CapDbContext)transactionContext;
        var remark = await db.Remarks.FirstAsync(r => r.Id == recordId, cancellationToken).ConfigureAwait(false);
        await service.SyncRolesAsync(remark, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Derived data of a synced action: the remark ↔ action link of its home remark (SPEC §9.3 C), the home remark's action sequence (so a
/// local action added after failover does not reuse a synced <c>{RemarkNo}-{n}</c> number) and the remark's assignee record role.
/// </summary>
internal sealed class ActionSyncHook(CapService service, IClock clock) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (CapDbContext)transactionContext;
        var action = await db.Actions.FirstAsync(a => a.Id == recordId, cancellationToken).ConfigureAwait(false);
        var remark = await db.Remarks.FirstOrDefaultAsync(r => r.Id == action.HomeRemarkId, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return;
        }

        if (!await db.RemarkActions.AnyAsync(l => l.RemarkId == remark.Id && l.ActionId == action.Id, cancellationToken).ConfigureAwait(false))
        {
            db.RemarkActions.Add(RemarkAction.Create(remark.Id, action.Id, clock.UtcNow, null));
        }

        var prefix = remark.Number + "-";
        if (action.Number.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(action.Number.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var seq) && seq > remark.ActionSeq)
        {
            db.Entry(remark).Property(nameof(Remark.ActionSeq)).CurrentValue = seq;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await service.SyncRolesForActionAsync(action.Id, cancellationToken).ConfigureAwait(false);
    }
}
