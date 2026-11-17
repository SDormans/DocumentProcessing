using Microsoft.AspNetCore.Identity;

namespace DocumentProcessing.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>The model type this user chose for new uploads; null means the configured default.</summary>
    public DocumentProcessing.Domain.Enums.AiModelType? PreferredAiModelType { get; set; }
}
