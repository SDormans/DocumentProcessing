namespace DocumentProcessing.Infrastructure.Storage;

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// <c>Local</c> (default, development) or <c>AzureBlob</c>.
    /// </summary>
    public string Provider { get; set; } = FileStorageProviders.Local;

    /// <summary>
    /// Root directory for <see cref="LocalFileStorage"/>.
    /// </summary>
    public string? BasePath { get; set; }

    public AzureBlobStorageOptions AzureBlob { get; set; } = new();
}

public class AzureBlobStorageOptions
{
    /// <summary>
    /// Storage account connection string (e.g. Azurite for local testing). Takes precedence over <see cref="ServiceUri"/>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Blob endpoint such as <c>https://account.blob.core.windows.net</c>, authenticated with <c>DefaultAzureCredential</c>
    /// (managed identity in Azure). The identity needs the <c>Storage Blob Data Contributor</c> role on the container.
    /// </summary>
    public string? ServiceUri { get; set; }

    /// <summary>
    /// Client id of a user-assigned managed identity; leave empty for system-assigned identity or developer credentials.
    /// </summary>
    public string? ManagedIdentityClientId { get; set; }

    public string ContainerName { get; set; } = "documents";

    /// <summary>
    /// Create the container on first upload. Keep off in production, where the container is provisioned up front.
    /// </summary>
    public bool CreateContainerIfNotExists { get; set; }
}

public static class FileStorageProviders
{
    public const string Local = "Local";
    public const string AzureBlob = "AzureBlob";
}
