using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace Strategy.Host.Infrastructure;

/// <summary>
/// Serves the built SPA (Vite output). Root: <c>Spa:RootPath</c>, default <c>wwwroot</c>. Hashed assets are cached
/// for a year; <c>index.html</c> is never cached. Unknown <c>/api</c>, <c>/mcp</c>, <c>/health</c>, <c>/openapi</c> paths
/// stay 404 instead of returning the SPA.
/// </summary>
internal static class SpaHosting
{
    public static void UseSpaStaticFiles(this WebApplication app)
    {
        if (GetFileProvider(app) is not { } files)
        {
            return;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers[HeaderNames.CacheControl] =
                    context.Context.Request.Path.StartsWithSegments("/assets")
                        ? "public, max-age=31536000, immutable"
                        : "no-cache";
            },
        });
    }

    public static void MapSpaFallback(this WebApplication app)
    {
        if (GetFileProvider(app) is not { } files)
        {
            return;
        }

        app.MapFallbackToFile(
            "{*path:regex(^(?!(api|mcp|health|openapi)(/|$)).*$)}",
            "index.html",
            new StaticFileOptions
            {
                FileProvider = files,
                OnPrepareResponse = context => context.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache",
            });
    }

    private static PhysicalFileProvider? GetFileProvider(WebApplication app)
    {
        var configured = app.Configuration["Spa:RootPath"];
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(app.Environment.ContentRootPath, "wwwroot")
            : Path.Combine(app.Environment.ContentRootPath, configured));
        return File.Exists(Path.Combine(root, "index.html")) ? new PhysicalFileProvider(root) : null;
    }
}
