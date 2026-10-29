using System.Text.Json;

namespace MsrEdu.Api.Data.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Email { get; set; }
    /// <summary>Google account id (the OIDC "sub" claim).</summary>
    public string? GoogleSubject { get; set; }
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public List<UserRole> Roles { get; set; } = [];
}

public class Role
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
}

public class UserRole
{
    public Guid UserId { get; set; }
    public required string RoleKey { get; set; }
    public Guid? GrantedBy { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public User? User { get; set; }
}

/// <summary>Business policy lives here, not in code.</summary>
public class AppSetting
{
    public required string Key { get; set; }
    public required JsonDocument Value { get; set; }
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Append-only (enforced by a database trigger).</summary>
public class AuditLog
{
    public long Id { get; set; }
    public Guid? ActorId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public JsonDocument? Before { get; set; }
    public JsonDocument? After { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
