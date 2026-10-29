namespace MsrEdu.Api.Auth;

public class MsrAuthOptions
{
    public const string Section = "Auth";

    /// <summary>Google accounts that become owner_admin on sign-in (bootstrap the first owner).</summary>
    public string[] OwnerEmails { get; set; } = [];

    /// <summary>Development only: enables POST /api/auth/dev-login. Startup fails if set elsewhere.</summary>
    public bool EnableDevLogin { get; set; }
}

public static class Policies
{
    public const string Staff = "staff";
    public const string Content = "content";
    public const string Finance = "finance";
    public const string Support = "support";
    public const string Owner = "owner";
}
