using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Integration.Sync;

/// <summary>Standby state of a record as its page shows it (badges; ADR 0206 / 0207).</summary>
public sealed record RecordSyncStateDto(
    bool ReadOnly, string? SourceSystem, string? SourceNameAr, string? SourceNameEn, bool DeletedInSource, bool ResubmitRequired, int SourceHistoryCount);

public sealed record SourceHistoryEntryDto(
    Guid Id, string Kind, DateTimeOffset OccurredAt, string? ActorName, string? ProcessName, string? ProcessInstanceId, string? ProcessStatus, string? StepStatus,
    string? Comments, string? Field, string? OldValue, string? NewValue);

/// <summary><c>/api/v1/integration/sync/…</c> (docs/design/salesforce-sync.md §12, §13). Every endpoint checks its permission on the server.</summary>
internal static class SyncEndpoints
{
    private static Results<NoContent, ProblemHttpResult> NoContentOr(Result result) => result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();

    public static void Map(RouteGroupBuilder group)
    {
        var sync = group.MapGroup("/sync");
        const string View = IntegrationPermissions.SyncView;
        const string Manage = IntegrationPermissions.SyncManage;
        const string Failover = IntegrationPermissions.SyncFailover;

        sync.MapGet("/connections", async (SyncAdministration a, CancellationToken ct) => TypedResults.Ok(await a.ListAsync(ct)))
            .WithName("ListSyncConnections").RequirePermission(View);
        sync.MapGet("/connections/{id:guid}", async (Guid id, SyncAdministration a, CancellationToken ct) => (await a.GetAsync(id, ct)).ToOk())
            .WithName("GetSyncConnection").RequirePermission(View);
        sync.MapPut("/connections/{id:guid}", async (Guid id, SyncConnectionInput body, SyncAdministration a, CancellationToken ct) => (await a.UpdateAsync(id, body, ct)).ToOk())
            .WithName("UpdateSyncConnection").RequirePermission(Manage);
        sync.MapPut("/connections/{id:guid}/key", async (Guid id, SyncKeyInput body, SyncAdministration a, CancellationToken ct) => (await a.SetKeyAsync(id, body, ct)).ToOk())
            .WithName("SetSyncConnectionKey").RequirePermission(Manage);
        sync.MapPost("/connections/{id:guid}/test", async (Guid id, SyncAdministration a, CancellationToken ct) => (await a.TestAsync(id, ct)).ToOk())
            .WithName("TestSyncConnection").RequirePermission(Manage);
        sync.MapPost("/connections/{id:guid}/activate", async (Guid id, SyncAdministration a, CancellationToken ct) => (await a.SetActiveAsync(id, true, ct)).ToOk())
            .WithName("ActivateSyncConnection").RequirePermission(Manage);
        sync.MapPost("/connections/{id:guid}/deactivate", async (Guid id, SyncAdministration a, CancellationToken ct) => (await a.SetActiveAsync(id, false, ct)).ToOk())
            .WithName("DeactivateSyncConnection").RequirePermission(Manage);
        sync.MapGet("/connections/{id:guid}/schedule", async (Guid id, SyncAdministration a, CancellationToken ct) => (await a.ScheduleAsync(id, ct)).ToOk())
            .WithName("GetSyncSchedule").RequirePermission(View);
        sync.MapPut("/connections/{id:guid}/schedule", async (Guid id, SyncScheduleInput body, SyncAdministration a, CancellationToken ct) =>
                (await a.SaveScheduleAsync(id, body, ct)).ToOk())
            .WithName("SaveSyncSchedule").RequirePermission(Manage);
        sync.MapPost("/connections/{id:guid}/sync-now", async (Guid id, SyncNowInput body, SyncAdministration a, CancellationToken ct) => (await a.SyncNowAsync(id, body, ct)).ToOk())
            .WithName("SyncNow").RequirePermission(Manage);
        sync.MapGet("/connections/{id:guid}/status", async (Guid id, SyncAdministration a, CancellationToken ct) => (await a.StatusAsync(id, ct)).ToOk())
            .WithName("GetSyncStatus").RequirePermission(View);
        sync.MapGet("/connections/{id:guid}/runs", async (Guid id, int? page, int? pageSize, SyncAdministration a, CancellationToken ct) =>
                (await a.RunsAsync(id, new PageRequest(page ?? 1, Math.Clamp(pageSize ?? 25, 1, PageRequest.MaxPageSize)), ct)).ToOk())
            .WithName("ListSyncRuns").RequirePermission(View);
        sync.MapGet("/connections/{id:guid}/runs/{runId:guid}", async (Guid id, Guid runId, SyncAdministration a, CancellationToken ct) => (await a.RunAsync(id, runId, ct)).ToOk())
            .WithName("GetSyncRun").RequirePermission(View);

        // Live describe (mapping editor).
        sync.MapGet("/connections/{id:guid}/source/objects", async (Guid id, SyncAdministration a, CancellationToken ct) => (await a.SourceObjectsAsync(id, ct)).ToOk())
            .WithName("ListSourceObjects").RequirePermission(Manage);
        sync.MapGet("/connections/{id:guid}/source/objects/{name}", async (Guid id, string name, SyncAdministration a, CancellationToken ct) =>
                (await a.DescribeAsync(id, name, ct)).ToOk())
            .WithName("DescribeSourceObject").RequirePermission(Manage);

        // Mapping packs and the editor.
        sync.MapGet("/targets", (SyncAdministration a) => TypedResults.Ok(a.Targets())).WithName("ListSyncTargets").RequirePermission(View);
        sync.MapGet("/packs", async (SyncAdministration a, CancellationToken ct) => TypedResults.Ok(await a.PacksAsync(ct))).WithName("ListMappingPacks").RequirePermission(View);
        sync.MapGet("/packs/{packId:guid}/mappings", async (Guid packId, SyncAdministration a, CancellationToken ct) => (await a.MappingsAsync(packId, ct)).ToOk())
            .WithName("ListObjectMappings").RequirePermission(View);
        sync.MapGet("/packs/{packId:guid}/export", async (Guid packId, SyncAdministration a, CancellationToken ct) => (await a.ExportPackAsync(packId, ct)).ToOk())
            .WithName("ExportMappingPack").RequirePermission(Manage);
        sync.MapPost("/packs/import", async (MappingPackDocument body, SyncAdministration a, CancellationToken ct) => (await a.ImportPackAsync(body, ct)).ToOk())
            .WithName("ImportMappingPack").RequirePermission(Manage);
        sync.MapGet("/mappings/{mappingId:guid}", async (Guid mappingId, SyncAdministration a, CancellationToken ct) => (await a.MappingAsync(mappingId, ct)).ToOk())
            .WithName("GetObjectMapping").RequirePermission(View);
        sync.MapPut("/mappings/{mappingId:guid}", async (Guid mappingId, ObjectMappingInput body, SyncAdministration a, CancellationToken ct) =>
                (await a.UpdateMappingAsync(mappingId, body, ct)).ToOk())
            .WithName("UpdateObjectMapping").RequirePermission(Manage);
        sync.MapPost("/mappings/{mappingId:guid}/enabled", async (Guid mappingId, ObjectMappingEnableInput body, SyncAdministration a, CancellationToken ct) =>
                (await a.SetMappingEnabledAsync(mappingId, body, ct)).ToOk())
            .WithName("SetObjectMappingEnabled").RequirePermission(Manage);
        sync.MapPost("/connections/{id:guid}/mappings/{mappingId:guid}/validate", async (Guid id, Guid mappingId, SyncAdministration a, CancellationToken ct) =>
                (await a.ValidateMappingAsync(id, mappingId, ct)).ToOk())
            .WithName("ValidateObjectMapping").RequirePermission(Manage);

        // Exceptions queue.
        sync.MapGet("/connections/{id:guid}/exceptions", async (Guid id, string? status, string? reason, string? mapping, string? search, int? page, int? pageSize,
                SyncAdministration a, CancellationToken ct) =>
                TypedResults.Ok(await a.ExceptionsAsync(id, status, reason, mapping, search, new PageRequest(page ?? 1, Math.Clamp(pageSize ?? 25, 1, PageRequest.MaxPageSize)), ct)))
            .WithName("ListSyncExceptions").RequirePermission(View);
        sync.MapGet("/exceptions/{exceptionId:guid}", async (Guid exceptionId, SyncAdministration a, CancellationToken ct) => (await a.ExceptionAsync(exceptionId, ct)).ToOk())
            .WithName("GetSyncException").RequirePermission(View);
        sync.MapPost("/exceptions/{exceptionId:guid}/reprocess", async (Guid exceptionId, SyncAdministration a, CancellationToken ct) =>
                (await a.ReprocessOneAsync(exceptionId, ct)).ToOk())
            .WithName("ReprocessSyncException").RequirePermission(Manage);
        sync.MapPost("/exceptions/{exceptionId:guid}/ignore", async (Guid exceptionId, SyncAdministration a, CancellationToken ct) => (await a.IgnoreAsync(exceptionId, ct)).ToOk())
            .WithName("IgnoreSyncException").RequirePermission(Manage);
        sync.MapPost("/exceptions/{exceptionId:guid}/map-value", async (Guid exceptionId, MapValueInput body, SyncAdministration a, CancellationToken ct) =>
                (await a.MapValueAsync(exceptionId, body, ct)).ToOk())
            .WithName("MapSyncExceptionValue").RequirePermission(Manage);
        sync.MapPost("/exceptions/{exceptionId:guid}/map-person", async (Guid exceptionId, MapPersonInput body, SyncAdministration a,
                Organisation.Contracts.IEmployeeDirectory employees, CancellationToken ct) => (await a.MapPersonAsync(exceptionId, body, employees, ct)).ToOk())
            .WithName("MapSyncExceptionPerson").RequirePermission(Manage);
        sync.MapPost("/connections/{id:guid}/exceptions/reprocess", async (Guid id, ReprocessInput body, SyncAdministration a, CancellationToken ct) =>
                (await a.ReprocessManyAsync(id, body, ct)).ToOk())
            .WithName("ReprocessSyncExceptions").RequirePermission(Manage);

        // Failover.
        sync.MapGet("/step-up/methods", async (SyncFailover f, CancellationToken ct) => TypedResults.Ok(await f.StepUpMethodsAsync(ct)))
            .WithName("GetStepUpMethods").RequirePermission(Failover);
        sync.MapPost("/step-up/email-code", async (SyncFailover f, CancellationToken ct) => NoContentOr(await f.SendStepUpCodeAsync(ct)))
            .WithName("SendStepUpEmailCode").RequirePermission(Failover);
        sync.MapPost("/connections/{id:guid}/make-primary", async (Guid id, MakePrimaryInput body, SyncFailover f, CancellationToken ct) => (await f.MakePrimaryAsync(id, body, ct)).ToOk())
            .WithName("MakeThisSystemPrimary").RequirePermission(Failover);
        sync.MapPost("/connections/{id:guid}/return-to-standby", async (Guid id, ReturnToStandbyInput body, SyncFailover f, CancellationToken ct) =>
                (await f.ReturnToStandbyAsync(id, body, ct)).ToOk())
            .WithName("ReturnToStandby").RequirePermission(Failover);
        sync.MapPost("/connections/{id:guid}/close-outage", async (Guid id, SyncFailover f, CancellationToken ct) => (await f.CloseOutageAsync(id, ct)).ToOk())
            .WithName("CloseOutage").RequirePermission(Failover);
        sync.MapGet("/connections/{id:guid}/outages", async (Guid id, int? page, int? pageSize, SyncFailover f, CancellationToken ct) =>
                TypedResults.Ok(await f.OutagesAsync(id, new PageRequest(page ?? 1, Math.Clamp(pageSize ?? 25, 1, PageRequest.MaxPageSize)), ct)))
            .WithName("ListOutages").RequirePermission(Failover);
        sync.MapGet("/outages/{outageId:guid}/report", async (Guid outageId, SyncFailover f, CancellationToken ct) => (await f.ReportAsync(outageId, ct)).ToOk())
            .WithName("GetOutageReport").RequirePermission(Failover);
        sync.MapPost("/outages/{outageId:guid}/acknowledge", async (Guid outageId, SyncFailover f, CancellationToken ct) => NoContentOr(await f.AcknowledgeAsync(outageId, ct)))
            .WithName("AcknowledgeOutageReport").RequirePermission(Failover);
        sync.MapGet("/outages/{outageId:guid}/report.xlsx", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (Guid outageId, string? lang, SyncFailover f, CancellationToken ct) =>
            {
                var file = await f.ExportExcelAsync(outageId, lang == "ar" ? "ar" : "en", ct);
                return file.IsSuccess ? TypedResults.File(file.Value, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"outage-changes-{outageId:N}.xlsx")
                    : file.Error!.ToProblem();
            })
            .WithName("ExportOutageReportExcel").RequirePermission(Failover);
        sync.MapGet("/outages/{outageId:guid}/report.pdf", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (Guid outageId, string? lang, SyncFailover f, CancellationToken ct) =>
            {
                var file = await f.ExportPdfAsync(outageId, lang == "ar" ? "ar" : "en", ct);
                return file.IsSuccess ? TypedResults.File(file.Value, "application/pdf", $"outage-changes-{outageId:N}.pdf") : file.Error!.ToProblem();
            })
            .WithName("ExportOutageReportPdf").RequirePermission(Failover);
        sync.MapPost("/outages/{outageId:guid}/records/{type}/{recordId:guid}/re-entered", async (Guid outageId, string type, Guid recordId, ReEnteredInput body, SyncFailover f,
                CancellationToken ct) => NoContentOr(await f.SetReEnteredAsync(outageId, recordId, type, body, ct)))
            .WithName("SetOutageRecordReEntered").RequirePermission(Failover);
        sync.MapGet("/outages/{outageId:guid}/conflicts", async (Guid outageId, SyncFailover f, CancellationToken ct) => (await f.ConflictsAsync(outageId, ct)).ToOk())
            .WithName("ListOutageConflicts").RequirePermission(Failover);
        sync.MapPost("/outages/{outageId:guid}/conflicts/{recordId:guid}/resolve", async (Guid outageId, Guid recordId, ResolveConflictInput body, SyncFailover f,
                CancellationToken ct) => (await f.ResolveConflictAsync(outageId, recordId, body, ct)).ToOk())
            .WithName("ResolveOutageConflict").RequirePermission(Failover);

        // Record pages: standby badges and the read-only source history (same 404 rule as the references).
        group.MapGet("/references/{type}/{id:guid}/sync-state", SyncStateAsync).WithName("GetRecordSyncState").RequireSignedIn();
        group.MapGet("/references/{type}/{id:guid}/source-history", SourceHistoryAsync).WithName("ListRecordSourceHistory").RequireSignedIn();
    }

    private static async Task<Results<Ok<RecordSyncStateDto>, ProblemHttpResult>> SyncStateAsync(
        string type, Guid id, ClaimsPrincipal user, IReferencedRecordResolver resolver, IntegrationDbContext db, SyncStandbyGuard guard, CancellationToken ct)
    {
        if (!user.IsInternal() || !(await resolver.ResolveAsync(type, [id], user, ct).ConfigureAwait(false)).ContainsKey(id))
        {
            return Error.NotFound().ToProblem();
        }

        var origin = await db.References.AsNoTracking().Where(r => r.RecordId == id && r.IsOrigin)
            .Join(db.SyncConnections.AsNoTracking(), r => r.SourceSystem, c => c.SourceSystem, (r, c) => new { r.SourceSystem, r.SyncStatus })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (origin is null)
        {
            return TypedResults.Ok(new RecordSyncStateDto(false, null, null, null, false, false, 0));
        }

        var source = await db.SourceSystems.AsNoTracking().FirstOrDefaultAsync(s => s.Code == origin.SourceSystem, ct).ConfigureAwait(false);
        var readOnly = (await guard.FindReadOnlyAsync([new(type, id)], ct).ConfigureAwait(false)).Count > 0;
        var resubmit = await db.OutageRecords.AsNoTracking().Join(db.Outages.AsNoTracking().Where(o => o.Status != OutageStatuses.Closed), r => r.OutageId, o => o.Id, (r, o) => r)
            .AnyAsync(r => r.RecordId == id && r.ResubmitRequired, ct).ConfigureAwait(false);
        var history = await db.SourceHistory.AsNoTracking().CountAsync(h => h.RecordId == id, ct).ConfigureAwait(false);
        return TypedResults.Ok(new RecordSyncStateDto(readOnly, origin.SourceSystem, source?.NameAr, source?.NameEn, origin.SyncStatus == SyncStatuses.DeletedInSource,
            resubmit, history));
    }

    private static async Task<Results<Ok<List<SourceHistoryEntryDto>>, ProblemHttpResult>> SourceHistoryAsync(
        string type, Guid id, ClaimsPrincipal user, IReferencedRecordResolver resolver, IntegrationDbContext db, CancellationToken ct)
    {
        if (!user.IsInternal() || !(await resolver.ResolveAsync(type, [id], user, ct).ConfigureAwait(false)).ContainsKey(id))
        {
            return Error.NotFound().ToProblem();
        }

        var rows = await db.SourceHistory.AsNoTracking().Where(h => h.RecordId == id).OrderByDescending(h => h.OccurredAt).Take(500).ToListAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(rows.Select(h => new SourceHistoryEntryDto(h.Id, h.Kind, h.OccurredAt, h.ActorName, h.ProcessName, h.ProcessInstanceId, h.ProcessStatus,
            h.StepStatus, h.Comments, h.Field, h.OldValue, h.NewValue)).ToList());
    }
}
