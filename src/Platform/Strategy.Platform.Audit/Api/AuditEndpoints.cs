using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Audit.Application;
using Strategy.Platform.Audit.Infrastructure;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Reporting.Pdf;

namespace Strategy.Platform.Audit.Api;

internal static class AuditEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/entries", SearchAsync).WithName("SearchAuditEntries").RequirePermission(AuditPermissions.LogView);
        group.MapGet("/entries/export", ExportAsync).WithName("ExportAuditEntries").RequirePermission(AuditPermissions.LogExport)
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream");
        group.MapGet("/entity-types", EntityTypesAsync).WithName("ListAuditEntityTypes").RequirePermission(AuditPermissions.LogView);
        group.MapGet("/verification", VerificationAsync).WithName("GetAuditVerification").RequirePermission(AuditPermissions.ChainVerify);
        group.MapPost("/verification", VerifyAsync).WithName("VerifyAuditChain").RequirePermission(AuditPermissions.ChainVerify);
    }

    private static AuditFilter Filter(
        string? text, Guid? userId, string? system, string? entityType, string? recordId, string? action, string? channel, string? kind,
        DateTimeOffset? from, DateTimeOffset? to, string? field) =>
        new(text, userId, system, entityType, recordId, action, channel, kind, from, to, field);

    private static async Task<Ok<PagedResult<AuditEntryDto>>> SearchAsync(
        int? page, int? pageSize, string? text, Guid? userId, string? system, string? entityType, string? recordId, string? action, string? channel,
        string? kind, DateTimeOffset? from, DateTimeOffset? to, string? field, IAuditQuery query, CancellationToken ct) =>
        TypedResults.Ok(await query.SearchAsync(Filter(text, userId, system, entityType, recordId, action, channel, kind, from, to, field),
            new PageRequest(page ?? 1, pageSize ?? 50), ct));

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportAsync(
        string? format, string? lang, string? text, Guid? userId, string? system, string? entityType, string? recordId, string? action,
        string? channel, string? kind, DateTimeOffset? from, DateTimeOffset? to, string? field, AuditExport export, CancellationToken ct)
    {
        var chosen = format == "pdf" ? "pdf" : "xlsx";
        try
        {
            var (content, contentType, fileName) = await export.ExportAsync(
                Filter(text, userId, system, entityType, recordId, action, channel, kind, from, to, field), chosen, lang == "en" ? "en" : "ar", ct);
            return TypedResults.File(content, contentType, fileName);
        }
        catch (PdfRenderingException exception)
        {
            return Error.Rule("reporting.pdfUnavailable", exception.MessageKey).ToProblem();
        }
    }

    private static async Task<Ok<IReadOnlyList<string>>> EntityTypesAsync(AuditDbContext db, CancellationToken ct) =>
        TypedResults.Ok((IReadOnlyList<string>)await db.Entries.AsNoTracking().Where(e => e.EntityType != null).Select(e => e.EntityType!).Distinct()
            .OrderBy(e => e).Take(500).ToListAsync(ct));

    private static async Task<Ok<VerificationStatusDto>> VerificationAsync(AuditDbContext db, CancellationToken ct)
    {
        var runs = await db.VerificationRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).Take(20).Select(r => VerificationRunDto.From(r)).ToListAsync(ct);
        var checkpoint = await db.Checkpoints.AsNoTracking().FirstOrDefaultAsync(ct);
        var total = await db.Entries.MaxAsync(e => (long?)e.Seq, ct) ?? 0;
        return TypedResults.Ok(new VerificationStatusDto(total, checkpoint?.Seq, checkpoint?.VerifiedAt, runs));
    }

    private static async Task<Ok<VerificationRunDto>> VerifyAsync(VerifyInput body, AuditVerifier verifier, CancellationToken ct) =>
        TypedResults.Ok(VerificationRunDto.From(await verifier.VerifyAsync(
            body.Mode == VerificationModes.Incremental ? VerificationModes.Incremental : VerificationModes.Full, ct)));
}

internal sealed record VerifyInput(string? Mode);

internal sealed record VerificationStatusDto(long TotalEntries, long? CheckpointSeq, DateTimeOffset? CheckpointAt, IReadOnlyList<VerificationRunDto> Runs);

internal sealed record VerificationRunDto(
    Guid Id, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Mode, long FromSeq, long EntriesChecked, long LastSeq, bool Ok,
    long? BrokenSeq, string? MessageKey, string Channel)
{
    public static VerificationRunDto From(Domain.VerificationRun r) =>
        new(r.Id, r.StartedAt, r.FinishedAt, r.Mode, r.FromSeq, r.EntriesChecked, r.LastSeq, r.Ok, r.BrokenSeq, r.MessageKey, r.Channel);
}
