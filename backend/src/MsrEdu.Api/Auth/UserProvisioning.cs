using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;

namespace MsrEdu.Api.Auth;

/// <summary>Creates or updates the local user for a Google sign-in.</summary>
public class UserProvisioning(AppDbContext db, IOptions<MsrAuthOptions> options, TimeProvider clock)
{
    public async Task<User> UpsertGoogleUserAsync(string subject, string email, string? name, string? avatar, CancellationToken ct = default)
    {
        email = email.Trim().ToLowerInvariant();
        var user = await db.Users.Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.GoogleSubject == subject || u.Email == email, ct);
        if (user is null)
        {
            user = new User { Email = email };
            user.Roles.Add(new UserRole { RoleKey = RoleKeys.Learner });
            db.Users.Add(user);
        }
        user.GoogleSubject = subject;
        user.Email = email;
        user.FullName = name ?? user.FullName;
        user.AvatarUrl = avatar ?? user.AvatarUrl;
        user.LastLoginAt = clock.GetUtcNow();

        if (options.Value.OwnerEmails.Any(e => string.Equals(e.Trim(), email, StringComparison.OrdinalIgnoreCase))
            && user.Roles.All(r => r.RoleKey != RoleKeys.OwnerAdmin))
            user.Roles.Add(new UserRole { RoleKey = RoleKeys.OwnerAdmin });

        await db.SaveChangesAsync(ct);
        return user;
    }

    public static ClaimsPrincipal PrincipalFor(User user, string scheme) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
            ],
            scheme));
}
