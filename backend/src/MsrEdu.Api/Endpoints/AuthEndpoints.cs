using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MsrEdu.Api.Auth;
using MsrEdu.Api.Data;

namespace MsrEdu.Api.Endpoints;

public static class AuthEndpoints
{
    public record MeResponse(Guid Id, string Email, string? FullName, string? AvatarUrl, IReadOnlyCollection<string> Roles);
    public record DevLoginRequest(string Email, string[] Roles);

    /// <summary>Only same-site relative paths, to prevent open redirects.</summary>
    public static string SafeReturnPath(string? raw) =>
        !string.IsNullOrEmpty(raw) && raw.StartsWith('/') && !raw.StartsWith("//") && !raw.StartsWith("/\\") ? raw : "/";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapGet("/providers", async (IAuthenticationSchemeProvider schemes, IOptions<MsrAuthOptions> opts) => new
        {
            google = await schemes.GetSchemeAsync("Google") is not null,
            devLogin = opts.Value.EnableDevLogin,
        });

        g.MapGet("/google/login", async (string? returnUrl, IAuthenticationSchemeProvider schemes) =>
        {
            if (await schemes.GetSchemeAsync("Google") is null)
                return Results.Problem("Google sign-in is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var next = SafeReturnPath(returnUrl);
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = $"/auth/callback?next={Uri.EscapeDataString(next)}" },
                ["Google"]);
        });

        g.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        g.MapGet("/me", async (ICurrentUser current, AppDbContext db) =>
        {
            if (!current.IsAuthenticated) return Results.Unauthorized();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == current.Id);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new MeResponse(user.Id, user.Email, user.FullName, user.AvatarUrl, current.Roles));
        });

        // Development only (guarded at startup): sign in as any email with chosen roles, no Google needed.
        g.MapPost("/dev-login", async (DevLoginRequest req, HttpContext ctx, AppDbContext db, IOptions<MsrAuthOptions> opts) =>
        {
            if (!opts.Value.EnableDevLogin) return Results.NotFound();
            var email = req.Email.Trim().ToLowerInvariant();
            var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Email == email);
            if (user is null)
            {
                user = new Data.Entities.User { Email = email, FullName = email.Split('@')[0] };
                db.Users.Add(user);
            }
            var wanted = req.Roles.Append(RoleKeys.Learner).Intersect([.. RoleKeys.Staff, RoleKeys.Learner]).ToHashSet();
            user.Roles.RemoveAll(r => !wanted.Contains(r.RoleKey) && r.RoleKey != RoleKeys.OwnerAdmin);
            foreach (var role in wanted.Where(w => user.Roles.All(r => r.RoleKey != w)))
                user.Roles.Add(new Data.Entities.UserRole { RoleKey = role });
            await db.SaveChangesAsync();
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                UserProvisioning.PrincipalFor(user, CookieAuthenticationDefaults.AuthenticationScheme));
            return Results.NoContent();
        });
    }
}
