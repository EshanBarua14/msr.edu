using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Data;

namespace MsrEdu.Api.Auth;

/// <summary>
/// The session cookie carries only the user id. Roles are loaded from the database on
/// each request, so granting or revoking a role takes effect immediately.
/// </summary>
public sealed class RoleClaimsTransformation(AppDbContext db) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity
            || identity.HasClaim(c => c.Type == ClaimTypes.Role)
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return principal;

        var roles = await db.UserRoles.Where(r => r.UserId == userId).Select(r => r.RoleKey).ToListAsync();
        if (roles.Count == 0) return new ClaimsPrincipal(new ClaimsIdentity()); // user deleted: treat as signed out
        var clone = principal.Clone();
        var cloneIdentity = (ClaimsIdentity)clone.Identity!;
        foreach (var role in roles) cloneIdentity.AddClaim(new Claim(ClaimTypes.Role, role));
        return clone;
    }
}
