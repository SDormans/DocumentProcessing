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

    public static readonly IReadOnlyList<string> DefaultUnrestrictedRoles = ["Admin"];

    /// <summary>
    /// Roles that may read and review all documents. Everyone else only sees documents they uploaded.
    /// Empty means <see cref="DefaultUnrestrictedRoles"/>. Starts empty because configuration binding appends to an
    /// initialized list, which would keep Admin even when configuration lists other roles.
    /// </summary>
    public List<string> UnrestrictedRoles { get; set; } = [];

    public IReadOnlyList<string> EffectiveUnrestrictedRoles =>
        UnrestrictedRoles.Count > 0 ? UnrestrictedRoles : DefaultUnrestrictedRoles;
}

public static class DocumentAccess
{
    public static bool CanAccess(this ICurrentUser user, Document document) =>
        user.HasUnrestrictedDocumentAccess ||
        (!string.IsNullOrEmpty(user.UserId) && document.OwnerId == user.UserId);
}
