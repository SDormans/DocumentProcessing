using DocumentProcessing.Application.Services;

namespace DocumentProcessing.UnitTests.Services;

public sealed class TestCurrentUser(string? userId, bool unrestricted) : ICurrentUser
{
    public string? UserId { get; set; } = userId;
    public bool HasUnrestrictedDocumentAccess { get; set; } = unrestricted;
    public IReadOnlyCollection<string> Roles { get; set; } = unrestricted ? ["Admin"] : ["Reviewer"];

    public bool IsInRole(string role) => Roles.Contains(role);
}
