using DocumentProcessing.Application.Exceptions;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DocumentProcessing.UnitTests.Storage;

public class FileStorageRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddFileStorage(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Defaults_to_local_storage()
    {
        using var provider = Build(new() { ["FileStorage:BasePath"] = Path.Combine(Path.GetTempPath(), "dp-storage-tests") });

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IFileStorage>().Should().BeOfType<LocalFileStorage>();
    }

    [Fact]
    public void Selects_azure_blob_storage_case_insensitively()
    {
        using var provider = Build(new()
        {
            ["FileStorage:Provider"] = "azureblob",
            ["FileStorage:AzureBlob:ConnectionString"] = "UseDevelopmentStorage=true"
        });

        provider.GetRequiredService<IFileStorage>().Should().BeOfType<AzureBlobFileStorage>();
    }

    [Fact]
    public void Service_uri_uses_token_credential_and_configured_container()
    {
        var container = FileStorageServiceCollectionExtensions.CreateContainerClient(new AzureBlobStorageOptions
        {
            ServiceUri = "https://account.blob.core.windows.net",
            ContainerName = "invoices"
        });

        container.Uri.Should().Be(new Uri("https://account.blob.core.windows.net/invoices"));
    }

    [Fact]
    public void Azure_blob_without_endpoint_fails_when_resolved()
    {
        using var provider = Build(new() { ["FileStorage:Provider"] = "AzureBlob" });

        var resolve = () => provider.GetRequiredService<IFileStorage>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionString or an absolute ServiceUri*");
    }

    [Fact]
    public void Unknown_provider_fails_at_startup()
    {
        var register = () => Build(new() { ["FileStorage:Provider"] = "Ftp" });

        register.Should().Throw<InvalidOperationException>().WithMessage("*Unknown FileStorage:Provider 'Ftp'*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("../secret.pdf")]
    [InlineData("a/../b.pdf")]
    [InlineData("/abs.pdf")]
    [InlineData("a\\b.pdf")]
    [InlineData("a//b.pdf")]
    public async Task Rejects_unsafe_storage_keys_before_calling_storage(string key)
    {
        var storage = new AzureBlobFileStorage(
            FileStorageServiceCollectionExtensions.CreateContainerClient(new AzureBlobStorageOptions { ConnectionString = "UseDevelopmentStorage=true" }),
            createContainerIfNotExists: false,
            NullLogger<AzureBlobFileStorage>.Instance);

        var act = () => storage.ExistsAsync(key);

        if (key.Length == 0)
            await act.Should().ThrowAsync<ArgumentException>();
        else
            await act.Should().ThrowAsync<StorageException>();
    }
}
