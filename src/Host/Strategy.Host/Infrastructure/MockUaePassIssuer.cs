using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
using Strategy.Platform.Http.Access;

namespace Strategy.Host.Infrastructure;

/// <summary>
/// A stand-in for UAE Pass (OAuth 2.0 authorization code + userinfo) for demonstrations, E2E and integration tests (ADR 0154). The person types the
/// e-mail and user type UAE Pass would return. Mapped only in the Development environment with <c>Development:MockUaePass=true</c>, or started on
/// its own Kestrel port by the integration tests; never part of a production pipeline.
/// </summary>
public static class MockUaePassIssuer
{
    public const string BasePath = "/dev/mock-uaepass";
    public const string ClientId = "mock-uaepass-client";
    public const string ClientSecret = "mock-uaepass-secret";

    private static readonly ConcurrentDictionary<string, (string Email, string UserType, DateTimeOffset Expires)> Codes = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, (string Email, string UserType, DateTimeOffset Expires)> Tokens = new(StringComparer.Ordinal);

    public static void MapMockUaePassIssuer(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).ExcludeFromDescription();
        group.MapGet("/authorize", AuthorizePage).AsPublic();
        group.MapPost("/authorize", Approve).AsPublic().DisableAntiforgery();
        group.MapPost("/token", Token).AsPublic().DisableAntiforgery();
        group.MapGet("/userinfo", UserInfo).AsPublic();
    }

    /// <summary>The authorization, token and userinfo endpoints for a provider whose issuer runs at <paramref name="origin"/>.</summary>
    public static (string Authorize, string Token, string UserInfo) Endpoints(string origin)
    {
        var root = origin.TrimEnd('/') + BasePath;
        return ($"{root}/authorize", $"{root}/token", $"{root}/userinfo");
    }

    /// <summary>Integration tests: the issuer alone on a free loopback port (the OAuth back channel needs a real socket).</summary>
    public static async Task<WebApplication> StartStandaloneAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapMockUaePassIssuer();
        await app.StartAsync().ConfigureAwait(false);
        return app;
    }

    private static ContentHttpResult AuthorizePage(string? redirect_uri, string? state)
    {
        var html = $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><title>UAE Pass (test)</title></head>
            <body>
            <h1>UAE Pass — test issuer / الهوية الرقمية — بيئة تجريبية</h1>
            <p>Development only. Enter the identity UAE Pass would return. / للتطوير فقط.</p>
            <form method="post" action="{{BasePath}}/authorize">
              <input type="hidden" name="redirect_uri" value="{{WebUtility.HtmlEncode(redirect_uri)}}">
              <input type="hidden" name="state" value="{{WebUtility.HtmlEncode(state)}}">
              <p><label for="email">E-mail / البريد الإلكتروني</label> <input id="email" name="email" type="email" required></p>
              <p><label for="userType">User type / نوع الحساب</label>
                <select id="userType" name="userType"><option>SOP2</option><option>SOP3</option><option>SOP1</option></select></p>
              <p><button type="submit">Sign in / تسجيل الدخول</button></p>
            </form>
            </body></html>
            """;
        return TypedResults.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> Approve(HttpRequest request)
    {
        var form = await request.ReadFormAsync(request.HttpContext.RequestAborted).ConfigureAwait(false);
        var redirect = form["redirect_uri"].ToString();

        // Only back to an application callback path (no open redirect, even in development).
        if (!Uri.TryCreate(redirect, UriKind.Absolute, out var target) || !target.AbsolutePath.StartsWith("/signin-ext/", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(form["email"]))
        {
            return TypedResults.BadRequest();
        }

        var code = NewValue();
        Codes[code] = (form["email"].ToString().Trim(), form["userType"].ToString() is { Length: > 0 } type ? type : "SOP2", DateTimeOffset.UtcNow.AddMinutes(5));
        var separator = redirect.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return TypedResults.Redirect($"{redirect}{separator}code={code}&state={Uri.EscapeDataString(form["state"].ToString())}");
    }

    private static async Task<IResult> Token(HttpRequest request)
    {
        var form = await request.ReadFormAsync(request.HttpContext.RequestAborted).ConfigureAwait(false);
        if (!Codes.TryRemove(form["code"].ToString(), out var grant) || grant.Expires < DateTimeOffset.UtcNow)
        {
            return TypedResults.BadRequest(new { error = "invalid_grant" });
        }

        var token = NewValue();
        Tokens[token] = grant;
        return TypedResults.Ok(new { access_token = token, token_type = "Bearer", expires_in = 300 });
    }

    private static IResult UserInfo(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || !Tokens.TryGetValue(header["Bearer ".Length..], out var grant)
            || grant.Expires < DateTimeOffset.UtcNow)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sub"] = "uaepass:" + grant.Email.ToLowerInvariant(),
            ["email"] = grant.Email,
            ["userType"] = grant.UserType,
            ["fullnameEN"] = grant.Email,
        });
    }

    private static string NewValue() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
