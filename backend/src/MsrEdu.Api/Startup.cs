using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Auth;
using MsrEdu.Api.Data;
using MsrEdu.Api.Endpoints;
using MsrEdu.Api.Services;

namespace MsrEdu.Api;

public static class Startup
{
    public const string CsrfHeader = "X-MSR-CSRF";

    public static WebApplicationBuilder AddMsrEdu(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        var connection = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not set (see backend/README).");

        services.AddHttpContextAccessor();
        services.AddMsrRateLimits(config);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o
            .UseNpgsql(connection, AppDbContext.MapEnums)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));
        services.AddScoped<AccessService>();
        services.AddScoped<EntitlementService>();
        services.AddScoped<QuizService>();
        services.Configure<PaymentOptions>(config.GetSection("Payments"));
        // The sandbox fakes successful payments, so it can never run in Production.
        if (config.GetValue<bool>("Payments:Sandbox:Enabled") && !builder.Environment.IsProduction())
            services.AddSingleton<IPaymentGateway, SandboxGateway>();
        services.AddSingleton<PaymentGateways>();
        services.AddScoped<PaymentService>();
        services.Configure<DriveOptions>(config.GetSection("GoogleDrive"));
        services.AddSingleton<IDriveClient, GoogleDriveClient>();
        services.AddScoped<DriveSyncService>();
        services.Configure<AiOptions>(config.GetSection("Ai"));
        services.AddSingleton<IQuestionGenerator, ClaudeQuestionGenerator>();
        services.AddScoped<QuestionDraftService>();
        services.AddScoped<UserProvisioning>();
        services.AddScoped<IClaimsTransformation, RoleClaimsTransformation>();

        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower)));
        services.AddProblemDetails();

        // ------------------------------------------------------------- authentication
        services.Configure<MsrAuthOptions>(config.GetSection(MsrAuthOptions.Section));
        var authOptions = config.GetSection(MsrAuthOptions.Section).Get<MsrAuthOptions>() ?? new();
        if (authOptions.EnableDevLogin && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException("Auth:EnableDevLogin is only allowed in the Development environment.");

        var auth = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "msr.session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
                o.SlidingExpiration = true;
                // An API answers with status codes, never login-page redirects.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            });

        var googleId = config["Authentication:Google:ClientId"];
        var googleSecret = config["Authentication:Google:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecret))
        {
            auth.AddGoogle(o =>
            {
                o.ClientId = googleId;
                o.ClientSecret = googleSecret;
                o.CallbackPath = "/api/auth/google/callback";
                o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.Scope.Add("profile");
                o.Scope.Add("email");
                o.ClaimActions.MapJsonKey("urn:google:picture", "picture");
                o.Events.OnCreatingTicket = async ctx =>
                {
                    var p = ctx.Principal!;
                    var subject = p.FindFirstValue(ClaimTypes.NameIdentifier)!;
                    var email = p.FindFirstValue(ClaimTypes.Email)
                        ?? throw new InvalidOperationException("Google did not return an email address.");
                    var provisioning = ctx.HttpContext.RequestServices.GetRequiredService<UserProvisioning>();
                    var user = await provisioning.UpsertGoogleUserAsync(subject, email, p.FindFirstValue(ClaimTypes.Name), p.FindFirstValue("urn:google:picture"));
                    // Replace Google's identity with our own user id; nothing else is stored in the cookie.
                    ctx.Principal = UserProvisioning.PrincipalFor(user, ctx.Scheme.Name);
                };
                o.Events.OnRemoteFailure = ctx =>
                {
                    var cancelled = ctx.Request.Query["error"] == "access_denied";
                    ctx.Response.Redirect($"/login?notice={(cancelled ? "cancelled" : "failed")}");
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Staff, p => p.RequireRole(RoleKeys.Staff))
            .AddPolicy(Policies.Content, p => p.RequireRole(RoleKeys.OwnerAdmin, RoleKeys.ContentManager))
            .AddPolicy(Policies.Finance, p => p.RequireRole(RoleKeys.OwnerAdmin, RoleKeys.FinanceManager))
            .AddPolicy(Policies.Support, p => p.RequireRole(RoleKeys.OwnerAdmin, RoleKeys.SupportStaff, RoleKeys.FinanceManager))
            .AddPolicy(Policies.Owner, p => p.RequireRole(RoleKeys.OwnerAdmin));

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        return builder;
    }

    public static WebApplication UseMsrEdu(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseSecurityHeaders(app.Environment.IsProduction());
        app.UseStatusCodePages();

        // CSRF: state-changing API calls must carry a custom header, which browsers
        // only send cross-site after a CORS preflight (and no CORS is enabled).
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api")
                && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)
                && !HttpMethods.IsOptions(ctx.Request.Method)
                && !ctx.Request.Path.StartsWithSegments("/api/webhooks")
                && !ctx.Request.Headers.ContainsKey(CsrfHeader))
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsJsonAsync(new { error = $"missing {CsrfHeader} header" });
                return;
            }
            await next();
        });

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
        app.MapAuthEndpoints();
        app.MapCatalogEndpoints();
        app.MapLearningEndpoints();
        app.MapAdminEndpoints();
        app.MapAdminContentEndpoints();
        app.MapResourceEndpoints();
        app.MapPaymentEndpoints();
        app.MapFinanceEndpoints();
        app.MapOpsEndpoints();
        app.Map("/api/{**rest}", () => Results.NotFound());

        // Production: serve the built React app (frontend/dist copied to wwwroot) from the same origin.
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
        return app;
    }
}
