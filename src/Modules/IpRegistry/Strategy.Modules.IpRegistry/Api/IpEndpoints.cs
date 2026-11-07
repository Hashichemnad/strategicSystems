using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.IpRegistry.Ai;
using Strategy.Modules.IpRegistry.Application;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Integration;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Modules.IpRegistry.Api;

/// <summary><c>/api/v1/ip</c> (docs/design/requests.md §6). 404 outside the IP visibility; office actions 403 for others.</summary>
internal static class IpEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/records", async (int? page, int? pageSize, string? propertyType, string? protectionType, string? status, Guid? unitId, int? year, int? expiringWithin,
                    bool? mine, string? search, IpService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListAsync(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalised(), propertyType, protectionType, status, unitId,
                    year, expiringWithin, mine == true, search, ct)))
            .WithName("ListIpRecords").RequirePermission(IpPermissions.Read);
        group.MapByExternal("/records", IpLinks.Definition.ResourceType, IpPermissions.Read, "IpRecord", r => r.Id,
            (id, http) => http.RequestServices.GetRequiredService<IpService>().GetAsync(id, http.RequestAborted));
        group.MapGet("/dashboard", async (DateOnly? from, DateOnly? to, IpService s, CancellationToken ct) => TypedResults.Ok(await s.DashboardAsync(ct, from, to)))
            .WithName("GetIpDashboard").RequirePermission(IpPermissions.DashboardRead);

        var records = group.MapGroup("/records/{id:guid}");
        records.MapGet("/", async (Guid id, IpService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToOk())
            .WithName("GetIpRecord").RequirePermission(IpPermissions.Read);
        records.MapPost("/filing", async (Guid id, FilingInput b, IpService s, CancellationToken ct) => (await s.FileAsync(id, b, ct)).ToOk())
            .WithName("RecordIpFiling").RequirePermission(IpPermissions.Read);
        records.MapPost("/register-internally", async (Guid id, InternalInput b, IpService s, CancellationToken ct) => (await s.RegisterInternallyAsync(id, b, ct)).ToOk())
            .WithName("RegisterIpInternally").RequirePermission(IpPermissions.Read);
        records.MapPost("/registration", async (Guid id, RegistrationInput b, IpService s, CancellationToken ct) => (await s.RegisterAsync(id, b, ct)).ToOk())
            .WithName("RecordIpRegistration").RequirePermission(IpPermissions.Read);
        records.MapPost("/moet-rejection", async (Guid id, MoetRejectionInput b, IpService s, CancellationToken ct) => (await s.MoetRejectionAsync(id, b, ct)).ToOk())
            .WithName("RecordIpMoetRejection").RequirePermission(IpPermissions.Read);
        records.MapPost("/close", async (Guid id, InternalInput b, IpService s, CancellationToken ct) => (await s.CloseAsync(id, b, ct)).ToOk())
            .WithName("CloseIpRecord").RequirePermission(IpPermissions.Read);
        records.MapPost("/renewal", async (Guid id, RenewalInput b, IpService s, CancellationToken ct) => (await s.RenewAsync(id, b, ct)).ToOk())
            .WithName("RenewIpRecord").RequirePermission(IpPermissions.Read);
        records.MapPut("/contributors", async (Guid id, ContributorsInput b, IpService s, CancellationToken ct) => (await s.SetContributorsAsync(id, b, ct)).ToOk())
            .WithName("SetIpContributors").RequirePermission(IpPermissions.Manage);
        records.MapPut("/moet-description", async (Guid id, MoetDescriptionInput b, IpService s, CancellationToken ct) => (await s.SetMoetDescriptionAsync(id, b, ct)).ToOk())
            .WithName("SetIpMoetDescription").RequirePermission(IpPermissions.Read);
        records.MapGet("/contributors/{contributorId:guid}/emirates-id", async (Guid id, Guid contributorId, IpService s, CancellationToken ct) =>
                (await s.RevealAsync(id, contributorId, ct)).ToOk())
            .WithName("RevealContributorEmiratesId").RequirePermission(IpPermissions.Sensitive);
        records.MapGet("/moet-form", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (Guid id, IpService s, HttpContext http, CancellationToken ct) =>
            {
                var result = await s.MoetFormAsync(id, ct);
                http.Response.Headers.CacheControl = "no-store";
                return result.IsSuccess ? TypedResults.File(result.Value.Content, "application/pdf", result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("GetIpMoetForm").RequirePermission(IpPermissions.Sensitive);
        records.MapGet("/certificate", async Task<Results<FileStreamHttpResult, ProblemHttpResult>> (Guid id, IpService s, CancellationToken ct) =>
            {
                var result = await s.CertificateAsync(id, ct);
                return result.IsSuccess ? TypedResults.File(result.Value.Stream, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("DownloadIpCertificate").RequirePermission(IpPermissions.Read);
        group.MapGroup("/records").MapRecordHistory(IpRecord.EntityType, IpPermissions.Read, "GetIpRecord", async (rid, context) =>
            (await context.RequestServices.GetRequiredService<IpService>().GetAsync(rid, context.RequestAborted)).IsSuccess);

        var ai = group.MapGroup("/ai");
        ai.MapPost("/suggest", async (IpSuggestInput b, IpAiService s, CancellationToken ct) => (await s.SuggestAsync(b, ct)).ToOk())
            .WithName("SuggestIpTypes").RequirePermission(IpPermissions.AiSuggest);
        ai.MapPost("/similar", async (IpSuggestInput b, Guid? excludeId, IpAiService s, CancellationToken ct) => (await s.SimilarityAsync(b, excludeId, ct)).ToOk())
            .WithName("FindSimilarIp").RequirePermission(IpPermissions.AiSimilarity);
        ai.MapPost("/records/{id:guid}/moet-description", async (Guid id, IpAiService s, CancellationToken ct) => (await s.MoetDescriptionAsync(id, ct)).ToOk())
            .WithName("DraftIpMoetDescription").RequirePermission(IpPermissions.AiMoet);

        var settings = group.MapGroup("/settings");
        settings.MapGet("/", async (IpService s, CancellationToken ct) => TypedResults.Ok(await s.SettingsAsync(ct)))
            .WithName("GetIpSettings").RequirePermission(IpPermissions.SettingsManage);
        settings.MapPut("/", async (IpSettingsInput b, IpService s, CancellationToken ct) => (await s.SaveSettingsAsync(b, ct)).ToOk())
            .WithName("SaveIpSettings").RequirePermission(IpPermissions.SettingsManage);
    }
}
