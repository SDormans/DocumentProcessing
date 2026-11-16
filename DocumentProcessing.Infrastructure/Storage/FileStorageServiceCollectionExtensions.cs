using Azure.Identity;
using Azure.Storage.Blobs;
using DocumentProcessing.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Infrastructure.Storage;

public static class FileStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IFileStorage"/> selected by <c>FileStorage:Provider</c>.
    /// </summary>
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(FileStorageOptions.SectionName);
        var options = section.Get<FileStorageOptions>() ?? new FileStorageOptions();

        if (string.Equals(options.Provider, FileStorageProviders.Local, StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IFileStorage, LocalFileStorage>();
        }
        else if (string.Equals(options.Provider, FileStorageProviders.AzureBlob, StringComparison.OrdinalIgnoreCase))
        {
            var blob = options.AzureBlob;
            // Resolved lazily so EF design-time tooling and tests can build the container without storage settings.
            services.AddSingleton<IFileStorage>(sp => new AzureBlobFileStorage(
                CreateContainerClient(blob),
                blob.CreateContainerIfNotExists,
                sp.GetRequiredService<ILogger<AzureBlobFileStorage>>()));
        }
        else
        {
            throw new InvalidOperationException(
                $"Unknown FileStorage:Provider '{options.Provider}'. Use '{FileStorageProviders.Local}' or '{FileStorageProviders.AzureBlob}'.");
        }

        return services;
    }

    internal static BlobContainerClient CreateContainerClient(AzureBlobStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ContainerName))
        {
            throw new InvalidOperationException("FileStorage:AzureBlob:ContainerName is required.");
        }

        BlobServiceClient service;
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            service = new BlobServiceClient(options.ConnectionString);
        }
        else if (Uri.TryCreate(options.ServiceUri, UriKind.Absolute, out var serviceUri))
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) ? null : options.ManagedIdentityClientId
            });
            service = new BlobServiceClient(serviceUri, credential);
        }
        else
        {
            throw new InvalidOperationException(
                "FileStorage:AzureBlob requires either ConnectionString or an absolute ServiceUri.");
        }

        return service.GetBlobContainerClient(options.ContainerName);
    }
}
