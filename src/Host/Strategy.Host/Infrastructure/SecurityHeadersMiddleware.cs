namespace Strategy.Host.Infrastructure;

/// <summary>
/// Security headers on every response. The CSP allows only same-origin resources (no inline scripts) and forbids
/// framing; it can be overridden with <c>SecurityHeaders:ContentSecurityPolicy</c>. HSTS is added by <c>UseHsts</c>.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public const string DefaultContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self' data:; " +
        "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private readonly string _contentSecurityPolicy =
        configuration["SecurityHeaders:ContentSecurityPolicy"] is { Length: > 0 } csp ? csp : DefaultContentSecurityPolicy;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = _contentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        return next(context);
    }
}
