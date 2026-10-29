using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MsrEdu.Api.Services;

/// <summary>Requests per minute for the endpoints people or bots could hammer. Configurable under "RateLimits".</summary>
public class RateLimitOptions
{
    public int CheckoutPerMinute { get; set; } = 10;
    public int WebhooksPerMinute { get; set; } = 300;
    public int SignInPerMinute { get; set; } = 20;
    public int SandboxPerMinute { get; set; } = 20;
}

public static class Protection
{
    /// <summary>
    /// One global limiter keyed by endpoint group and caller (the signed-in user, else the client IP).
    /// Everything else is unlimited. Over the limit returns 429 with Retry-After.
    /// </summary>
    public static IServiceCollection AddMsrRateLimits(this IServiceCollection services, IConfiguration config)
    {
        var limits = config.GetSection("RateLimits").Get<RateLimitOptions>() ?? new RateLimitOptions();
        return services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "60";
                await ctx.HttpContext.Response.WriteAsJsonAsync(new { title = "Too many requests. Wait a minute and try again.", status = 429 }, ct);
            };
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var (group, limit) = Classify(http.Request, limits);
                if (group is null) return RateLimitPartition.GetNoLimiter("none");
                var caller = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter($"{group}:{caller}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                });
            });
        });
    }

    private static (string? Group, int Limit) Classify(HttpRequest req, RateLimitOptions limits)
    {
        var path = req.Path;
        var post = HttpMethods.IsPost(req.Method);
        if (post && path.StartsWithSegments("/api/checkout")) return ("checkout", limits.CheckoutPerMinute);
        if (path.StartsWithSegments("/api/webhooks")) return ("webhook", limits.WebhooksPerMinute);
        if (path.StartsWithSegments("/api/auth/dev-login") || path.StartsWithSegments("/api/auth/google")) return ("signin", limits.SignInPerMinute);
        if (post && path.StartsWithSegments("/api/sandbox")) return ("sandbox", limits.SandboxPerMinute);
        return (null, 0);
    }

    /// <summary>
    /// Browser hardening for every response. The CSP applies to the built React app served by the API;
    /// it allows Google Fonts, https images and video, and the YouTube and Drive players lessons embed.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool production) => app.Use(async (ctx, next) =>
    {
        var h = ctx.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        if (!ctx.Request.Path.StartsWithSegments("/api"))
            h.ContentSecurityPolicy = string.Join("; ",
                "default-src 'self'",
                "script-src 'self'",
                "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
                "font-src 'self' https://fonts.gstatic.com",
                "img-src 'self' data: https:",
                "media-src 'self' https:",
                "frame-src https://www.youtube-nocookie.com https://drive.google.com",
                "connect-src 'self'",
                "form-action 'self' https://accounts.google.com",
                "base-uri 'self'",
                "object-src 'none'",
                "frame-ancestors 'none'");
        if (production) h.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
        await next();
    });
}
