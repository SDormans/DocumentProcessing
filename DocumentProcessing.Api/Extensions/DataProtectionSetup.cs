using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace DocumentProcessing.Api.Extensions;

public static class DataProtectionSetup
{
    /// <summary>
    /// Keys that sign the auth and anti-forgery cookies. With <c>DataProtection:BlobUri</c> set they are kept in
    /// blob storage (shared by all replicas, kept across restarts), so a deploy doesn't sign everyone out.
    /// Without it they stay in the container and are lost on restart (fine for local development).
    /// </summary>
    public static IServiceCollection AddPersistentDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddDataProtection().SetApplicationName("DocumentProcessing");
        var blobUri = configuration["DataProtection:BlobUri"];
        if (!string.IsNullOrWhiteSpace(blobUri))
        {
            var clientId = configuration["FileStorage:AzureBlob:ManagedIdentityClientId"];
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId });
            builder.PersistKeysToAzureBlobStorage(new Uri(blobUri), credential);
        }
        return services;
    }
}
