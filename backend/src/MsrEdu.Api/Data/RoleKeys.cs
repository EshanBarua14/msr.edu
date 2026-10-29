namespace MsrEdu.Api.Data;

public static class RoleKeys
{
    public const string OwnerAdmin = "owner_admin";
    public const string ContentManager = "content_manager";
    public const string FinanceManager = "finance_manager";
    public const string SupportStaff = "support_staff";
    public const string Learner = "learner";

    public static readonly string[] Staff = [OwnerAdmin, ContentManager, FinanceManager, SupportStaff];
}
