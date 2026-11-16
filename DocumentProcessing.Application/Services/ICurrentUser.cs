using DocumentProcessing.Domain.Entities;

namespace DocumentProcessing.Application.Services;

/// <summary>
/// The user on whose behalf the current request runs.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }

    /// <summary>
    /// True for roles listed in <see cref="DocumentAccessOptions.UnrestrictedRoles"/>; such users see every document.
    /// </summary>
    bool HasUnrestrictedDocumentAccess { get; }
}

public class DocumentAccessOptions
{
    public const string SectionName = "DocumentAccess";

    /// <summary>
    /// Roles that may read and review all documents. Everyone else only sees documents they uploaded.
    /// </summary>
    public List<string> UnrestrictedRoles { get; set; } = ["Admin"];
}

public static class DocumentAccess
{
    public static bool CanAccess(this ICurrentUser user, Document document) =>
        user.HasUnrestrictedDocumentAccess ||
        (!string.IsNullOrEmpty(user.UserId) && document.OwnerId == user.UserId);
}
