using System.Security.Claims;
using MsrEdu.Api.Data;

namespace MsrEdu.Api.Auth;

public interface ICurrentUser
{
    Guid? Id { get; }
    bool IsAuthenticated { get; }
    IReadOnlyCollection<string> Roles { get; }
    /// <summary>owner_admin satisfies every role check.</summary>
    bool HasRole(string role);
    bool IsStaff { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? Id => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true && Id is not null;
    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToArray() ?? [];
    public bool HasRole(string role) => Roles.Contains(RoleKeys.OwnerAdmin) || Roles.Contains(role);
    public bool IsStaff => Roles.Any(r => RoleKeys.Staff.Contains(r));
}
