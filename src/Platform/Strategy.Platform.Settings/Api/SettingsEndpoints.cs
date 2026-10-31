using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings.Application;

namespace Strategy.Platform.Settings.Api;

internal static class SettingsEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/public", (SettingsAdministration a, CancellationToken ct) => a.PublicAsync(ct)).WithName("GetPublicSettings").AsPublic();
        group.MapGet("/translations", (SettingsAdministration a, CancellationToken ct) => a.TranslationsAsync(ct)).WithName("GetTranslationOverrides").AsPublic();
        group.MapPut("/translations", async (TranslationInput body, SettingsAdministration a, CancellationToken ct) =>
                (await a.SetTranslationAsync(body.Language, body.Namespace, body.Key, body.Value, ct)).ToOk())
            .WithName("SetTranslationOverride").RequirePermission(SettingsPermissions.TranslationManage);

        group.MapGet("/regional", (SettingsAdministration a, CancellationToken ct) => a.GetAsync<RegionalSettings>(ct)).WithName("GetRegionalSettings")
            .RequirePermission(SettingsPermissions.GeneralManage);
        group.MapPut("/regional", async (VersionedInput<RegionalSettings> body, SettingsAdministration a, CancellationToken ct) =>
                (await a.SaveAsync(body.Value, body.Version, SettingsAdministration.ValidateRegional, ct)).ToOk())
            .WithName("UpdateRegionalSettings").RequirePermission(SettingsPermissions.GeneralManage);
        group.MapGet("/branding", (SettingsAdministration a, CancellationToken ct) => a.GetAsync<BrandingSettings>(ct)).WithName("GetBrandingSettings")
            .RequirePermission(SettingsPermissions.GeneralManage);
        group.MapPut("/branding", async (VersionedInput<BrandingSettings> body, SettingsAdministration a, CancellationToken ct) =>
                (await a.SaveAsync(body.Value, body.Version, SettingsAdministration.ValidateBranding, ct)).ToOk())
            .WithName("UpdateBrandingSettings").RequirePermission(SettingsPermissions.GeneralManage);

        var logo = group.MapGroup("/branding/logo");
        logo.MapGet("/{kind}", GetLogoAsync).WithName("GetBrandingLogo").AsPublic().Produces(StatusCodes.Status200OK, contentType: "image/png");
        logo.MapPut("/{kind}", PutLogoAsync).WithName("PutBrandingLogo").RequirePermission(SettingsPermissions.GeneralManage).DisableAntiforgery();
        logo.MapDelete("/{kind}", async Task<Results<NoContent, ProblemHttpResult>> (string kind, SettingsAdministration a, CancellationToken ct) =>
                ToNoContent(await a.RemoveLogoAsync(kind, ct)))
            .WithName("DeleteBrandingLogo").RequirePermission(SettingsPermissions.GeneralManage);

        var lookups = group.MapGroup("/lookups");
        lookups.MapGet("/types", (SettingsAdministration a, CancellationToken ct) => a.ListLookupTypesAsync(ct)).WithName("ListLookupTypes").RequireSignedIn();
        lookups.MapPost("/types", async (LookupTypeInput body, SettingsAdministration a, CancellationToken ct) =>
                (await a.CreateLookupTypeAsync(body.Code, body.SystemCode, body.NameAr, body.NameEn, body.ParentTypeCode, ct)).ToOk())
            .WithName("CreateLookupType").RequirePermission(SettingsPermissions.LookupManage);
        lookups.MapGet("/{type}/values", async (string type, SettingsAdministration a, CancellationToken ct) => (await a.ListValuesAsync(type, ct)).ToOk())
            .WithName("ListLookupValues").RequireSignedIn();
        lookups.MapPost("/{type}/values", async (string type, LookupValueInput body, SettingsAdministration a, CancellationToken ct) =>
                (await a.CreateValueAsync(type, body, ct)).ToOk())
            .WithName("CreateLookupValue").RequirePermission(SettingsPermissions.LookupManage);
        lookups.MapPut("/values/{id:guid}", async (Guid id, UpdateLookupValueInput body, SettingsAdministration a, CancellationToken ct) =>
                (await a.UpdateValueAsync(id, new LookupValueInput(null, body.LabelAr, body.LabelEn, body.Sort, body.Active, body.ParentValueId, body.Metadata), body.Version, ct)).ToOk())
            .WithName("UpdateLookupValue").RequirePermission(SettingsPermissions.LookupManage);

        var numbering = group.MapGroup("/numbering");
        numbering.MapGet("/", (SettingsAdministration a, CancellationToken ct) => a.ListNumberingAsync(ct)).WithName("ListNumberingFormats")
            .RequirePermission(SettingsPermissions.NumberingManage);
        numbering.MapPut("/{key}", async (string key, NumberingInput body, SettingsAdministration a, CancellationToken ct) =>
                (await a.UpdateNumberingAsync(key, body.Pattern, body.Version, ct)).ToOk())
            .WithName("UpdateNumberingFormat").RequirePermission(SettingsPermissions.NumberingManage);
        numbering.MapPost("/preview", (PreviewInput body, SettingsAdministration a) => a.Preview(body.Pattern).ToOk())
            .WithName("PreviewNumberingFormat").RequirePermission(SettingsPermissions.NumberingManage);

        var features = group.MapGroup("/features");
        features.MapGet("/", (SettingsAdministration a, CancellationToken ct) => a.ListFeaturesAsync(ct)).WithName("ListFeatureFlags")
            .RequirePermission(SettingsPermissions.FeatureManage);
        features.MapGet("/enabled", EnabledFeaturesAsync).WithName("ListEnabledFeatures").RequireSignedIn();
        features.MapPut("/{code}", async (string code, FeatureInput body, SettingsAdministration a, CancellationToken ct) =>
                (await a.SetFeatureAsync(code, body.Enabled, body.Version, ct)).ToOk())
            .WithName("SetFeatureFlag").RequirePermission(SettingsPermissions.FeatureManage);
    }

    private static async Task<Ok<IReadOnlyList<string>>> EnabledFeaturesAsync(SettingsAdministration a, CancellationToken ct) =>
        TypedResults.Ok<IReadOnlyList<string>>((await a.ListFeaturesAsync(ct)).Where(f => f.Enabled).Select(f => f.Code).ToList());

    private static async Task<Results<FileContentHttpResult, NotFound>> GetLogoAsync(string kind, SettingsAdministration a, HttpContext context, CancellationToken ct)
    {
        if (await a.GetLogoAsync(kind, ct) is not { } asset)
        {
            return TypedResults.NotFound();
        }

        context.Response.Headers.CacheControl = "public, max-age=300";
        return TypedResults.File(asset.Content, asset.ContentType, lastModified: asset.UpdatedAt);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> PutLogoAsync(string kind, IFormFile file, SettingsAdministration a, CancellationToken ct)
    {
        if (file.Length > SettingsAdministration.MaxLogoBytes)
        {
            return ToNoContent(await a.SetLogoAsync(kind, new byte[SettingsAdministration.MaxLogoBytes + 1], ct));
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return ToNoContent(await a.SetLogoAsync(kind, buffer.ToArray(), ct));
    }

    private static Results<NoContent, ProblemHttpResult> ToNoContent(Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
}

internal sealed record VersionedInput<T>(T Value, uint Version);

internal sealed record TranslationInput(string? Language, string? Namespace, string? Key, string? Value);

internal sealed record LookupTypeInput(string? Code, string? SystemCode, string? NameAr, string? NameEn, string? ParentTypeCode);

internal sealed record UpdateLookupValueInput(string? LabelAr, string? LabelEn, int? Sort, bool? Active, Guid? ParentValueId, string? Metadata, uint Version);

internal sealed record NumberingInput(string? Pattern, uint Version);

internal sealed record PreviewInput(string? Pattern);

internal sealed record FeatureInput(bool Enabled, uint Version);
