using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;

namespace MsrEdu.Api.Auth;

/// <summary>Creates or updates the local user for a Google sign-in.</summary>
public class UserProvisioning(AppDbContext db, IOptions<MsrAuthOptions> options, TimeProvider clock)
{
    /// <summary>
    /// Two first-time sign-ins for the same person can race (two tabs); the loser hits the unique email index.
    /// Retrying once finds the row the winner created.
    /// </summary>
    public async Task<User> UpsertGoogleUserAsync(string subject, string email, string? name, string? avatar, CancellationToken ct = default)
    {
        try
        {
            return await UpsertOnceAsync(subject, email, name, avatar, ct);
        }
        catch (DbUpdateException e) when (IsUniqueViolation(e))
        {
            db.ChangeTracker.Clear();
            return await UpsertOnceAsync(subject, email, name, avatar, ct);
        }
    }

    public static bool IsUniqueViolation(DbUpdateException e) => e.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private async Task<User> UpsertOnceAsync(string subject, string email, string? name, string? avatar, CancellationToken ct)
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
