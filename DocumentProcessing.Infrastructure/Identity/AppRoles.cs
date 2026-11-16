namespace DocumentProcessing.Infrastructure.Identity;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Reviewer = "Reviewer";

    public static readonly IReadOnlyList<string> All = [Admin, Reviewer];
}
