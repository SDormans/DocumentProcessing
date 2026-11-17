using DocumentProcessing.Application.Services;
using DocumentProcessing.Application.Services.Ai;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Domain.Models;
using DocumentProcessing.UnitTests.Extraction;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DocumentProcessing.UnitTests.Ai;

/// <summary>Which model the extractor calls, and what happens when it fails.</summary>
public class ModelSwitchingTests
{
    private readonly Document _document = Document.Create("invoice.pdf", "application/pdf", 10, "hash", "key.pdf");
    private readonly Mock<IDocumentRepository> _repository = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IDocumentTextExtractor> _textExtractor = new();
    private readonly Mock<IAiProvider> _cloud = new();
    private readonly Mock<IAiProvider> _local = new();

    public ModelSwitchingTests()
    {
        _repository.Setup(r => r.GetByIdAsync(_document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_document);
        _storage.Setup(s => s.OpenReadAsync("key.pdf", It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream());
        _textExtractor.Setup(t => t.ExtractAsync(_document.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextExtractionResult
            {
                DocumentId = _document.Id,
                Method = TextExtractionMethod.Native,
                Pages = new List<ExtractedPage> { new() { PageNumber = 1, Text = "Invoice INV-1" } }
            });
    }

    private InvoiceExtractor CreateExtractor(TestAiProviders providers) => new(
        _repository.Object, _storage.Object, _textExtractor.Object, providers,
        NullLogger<InvoiceExtractor>.Instance, new ValidationService(Array.Empty<IInvoiceValidationRule>()));

    private TestAiProviders Both(AiFallbackMode mode = AiFallbackMode.None) =>
        new TestAiProviders { FallbackMode = mode }.With(AiModelType.Cloud, _cloud.Object).With(AiModelType.Local, _local.Object);

    private static void Succeeds(Mock<IAiProvider> provider) =>
        provider.Setup(p => p.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => InvoiceExtractionSchema.Parse(InvoiceExtractionSchemaTests.ValidJson));

    private static void Fails(Mock<IAiProvider> provider, string message = "connection refused") =>
        provider.Setup(p => p.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(message));

    [Theory]
    [InlineData(AiModelType.Cloud)]
    [InlineData(AiModelType.Local)]
    public async Task Uses_the_model_type_chosen_for_the_document(AiModelType chosen)
    {
        Succeeds(_cloud);
        Succeeds(_local);
        _document.SelectAiModel(chosen);

        var result = await CreateExtractor(Both()).ExtractAsync(_document.Id);

        result.ModelType.Should().Be(chosen);
        result.ModelName.Should().Be($"{chosen.ToString().ToLowerInvariant()}-model");
        (chosen == AiModelType.Cloud ? _local : _cloud).Verify(p => p.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Without_fallback_a_failure_is_reported_and_the_other_model_is_never_called()
    {
        Fails(_local);
        Succeeds(_cloud);
        _document.SelectAiModel(AiModelType.Local);

        var act = () => CreateExtractor(Both()).ExtractAsync(_document.Id);

        var error = (await act.Should().ThrowAsync<AiModelUnavailableException>()).Which;
        error.ModelType.Should().Be(AiModelType.Local);
        error.IsConfigurationError.Should().BeFalse("connection failures are retried");
        error.Message.Should().Be("The Local model (local-model) could not be reached: connection refused");
        _cloud.Verify(p => p.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "documents chosen for the local model must not be sent to the cloud without opt-in");
    }

    [Fact]
    public async Task Automatic_fallback_uses_the_other_model_and_records_a_warning()
    {
        Fails(_local);
        Succeeds(_cloud);
        _document.SelectAiModel(AiModelType.Local);

        var result = await CreateExtractor(Both(AiFallbackMode.Automatic)).ExtractAsync(_document.Id);

        result.Success.Should().BeTrue();
        result.ModelType.Should().Be(AiModelType.Cloud);
        result.Warnings.Should().Contain(w => w.StartsWith("Processed with the Cloud model (cloud-model) because the Local model failed"));
    }

    [Fact]
    public async Task Automatic_fallback_reports_both_failures()
    {
        Fails(_cloud, "HTTP 503");
        Fails(_local, "connection refused");

        var act = () => CreateExtractor(Both(AiFallbackMode.Automatic)).ExtractAsync(_document.Id);

        (await act.Should().ThrowAsync<AiModelUnavailableException>()).Which.Message.Should()
            .Contain("The Cloud model (cloud-model) could not be reached: HTTP 503")
            .And.Contain("The fallback Local model also failed: The Local model (local-model) could not be reached: connection refused");
    }

    [Fact]
    public async Task Automatic_fallback_does_nothing_when_the_other_model_is_not_configured()
    {
        Fails(_cloud);
        var providers = new TestAiProviders { FallbackMode = AiFallbackMode.Automatic }.With(AiModelType.Cloud, _cloud.Object);

        var act = () => CreateExtractor(providers).ExtractAsync(_document.Id);

        await act.Should().ThrowAsync<AiModelUnavailableException>();
    }

    [Fact]
    public async Task A_document_chosen_for_a_model_that_is_no_longer_configured_fails_as_a_configuration_error()
    {
        _document.SelectAiModel(AiModelType.Local);
        var providers = TestAiProviders.For(_cloud.Object);

        var act = () => CreateExtractor(providers).ExtractAsync(_document.Id);

        (await act.Should().ThrowAsync<AiModelUnavailableException>()).Which.IsConfigurationError.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_is_not_treated_as_a_model_failure()
    {
        using var cts = new CancellationTokenSource();
        _cloud.Setup(p => p.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((_, _) => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        var act = () => CreateExtractor(Both(AiFallbackMode.Automatic)).ExtractAsync(_document.Id, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _local.Verify(p => p.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class AiModelSettingsServiceTests
{
    private readonly Mock<IAiModelPreferenceStore> _preferences = new();
    private readonly TestAiProviders _providers = TestAiProviders.For(Mock.Of<IAiProvider>()).With(AiModelType.Local, Mock.Of<IAiProvider>());

    private AiModelSettingsService Service => new(_providers, _preferences.Object);

    [Fact]
    public async Task Uses_the_saved_choice()
    {
        _preferences.Setup(p => p.GetAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(AiModelType.Local);

        (await Service.ResolveAsync("u1")).Should().Be(AiModelType.Local);
        (await Service.GetAsync("u1", checkAvailability: false)).SelectedModelType.Should().Be("Local");
    }

    [Fact]
    public async Task Falls_back_to_the_default_when_nothing_is_saved_or_the_saved_model_is_disabled()
    {
        (await Service.ResolveAsync("u1")).Should().Be(AiModelType.Cloud);

        _preferences.Setup(p => p.GetAsync("u2", It.IsAny<CancellationToken>())).ReturnsAsync(AiModelType.Local);
        var cloudOnly = new AiModelSettingsService(TestAiProviders.For(Mock.Of<IAiProvider>()), _preferences.Object);
        (await cloudOnly.ResolveAsync("u2")).Should().Be(AiModelType.Cloud);
    }

    [Fact]
    public async Task Saves_a_valid_choice()
    {
        var result = await Service.SelectAsync("u1", "local");

        result.Success.Should().BeTrue();
        _preferences.Verify(p => p.SetAsync("u1", AiModelType.Local, It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData("Edge", "Unknown model type 'Edge'. Use 'Cloud' or 'Local'.")]
    [InlineData("1", "Unknown model type '1'. Use 'Cloud' or 'Local'.")]
    [InlineData(null, "Unknown model type ''. Use 'Cloud' or 'Local'.")]
    public async Task Rejects_unknown_model_types(string? value, string message)
    {
        var result = await Service.SelectAsync("u1", value);

        result.Should().Be(new AiModelSelectionResult(false, message, null));
        _preferences.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<AiModelType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rejects_a_model_that_is_not_configured()
    {
        var service = new AiModelSettingsService(TestAiProviders.For(Mock.Of<IAiProvider>()), _preferences.Object);

        var result = await service.SelectAsync("u1", "Local");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("The Local model is not available. Set Ai:Local:Provider to enable it.");
    }
}

public class RetryWithModelTests
{
    private readonly Mock<IDocumentRepository> _repository = new();
    private readonly Mock<IProcessingQueue> _queue = new();
    private readonly Mock<IAiModelPreferenceStore> _preferences = new();
    private readonly Document _document = Document.Create("invoice.pdf", "application/pdf", 10, "hash", "key.pdf", ownerId: "owner");

    public RetryWithModelTests()
    {
        _document.UpdateStatus(DocumentStatus.Queued);
        _document.UpdateStatus(DocumentStatus.Processing);
        _document.UpdateStatus(DocumentStatus.Failed);
        _repository.Setup(r => r.GetByIdAsync(_document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_document);
    }

    private DocumentService Service(string userId = "owner", IAiProviderRegistry? providers = null) => new(
        _repository.Object, Mock.Of<IFileStorage>(), _queue.Object, NullLogger<DocumentService>.Instance,
        Microsoft.Extensions.Options.Options.Create(new DocumentServiceOptions()),
        new Services.TestCurrentUser(userId, unrestricted: false),
        new AiModelSettingsService(providers ?? TestAiProviders.For(Mock.Of<IAiProvider>()).With(AiModelType.Local, Mock.Of<IAiProvider>()), _preferences.Object));

    [Fact]
    public async Task Requeues_a_failed_document_with_the_other_model()
    {
        _document.SelectAiModel(AiModelType.Local);

        var result = await Service().RetryWithModelAsync(_document.Id, "Cloud");

        result.Should().Be(ModelSwitchResult.Requeued);
        _document.AiModelType.Should().Be(AiModelType.Cloud);
        _document.Status.Should().Be(DocumentStatus.Queued);
        _queue.Verify(q => q.EnqueueAsync(_document.Id, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Hides_documents_of_other_users() =>
        (await Service("someone-else").RetryWithModelAsync(_document.Id, "Cloud")).Should().Be(ModelSwitchResult.NotFound);

    [Fact]
    public async Task Rejects_unknown_and_unconfigured_models()
    {
        (await Service().RetryWithModelAsync(_document.Id, "Edge")).Should().Be(ModelSwitchResult.InvalidModel);
        (await Service(providers: TestAiProviders.For(Mock.Of<IAiProvider>())).RetryWithModelAsync(_document.Id, "Local"))
            .Should().Be(ModelSwitchResult.ModelUnavailable);
        _queue.Verify(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Only_failed_documents_can_be_switched()
    {
        var queued = Document.Create("b.pdf", "application/pdf", 10, "hash", "b.pdf", ownerId: "owner");
        _repository.Setup(r => r.GetByIdAsync(queued.Id, It.IsAny<CancellationToken>())).ReturnsAsync(queued);

        (await Service().RetryWithModelAsync(queued.Id, "Local")).Should().Be(ModelSwitchResult.NotFailed);
    }
}
