using DocumentProcessing.Application.Services.Ai;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentProcessing.UnitTests.Ai;

public class AiProviderConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private static ValidatedAiSettings Validate(params (string Key, string? Value)[] values) =>
        ValidatedAiSettings.Create(AiSettings.Bind(Config(values)));

    [Fact]
    public void Defaults_to_the_fake_cloud_model_with_local_disabled_and_no_fallback()
    {
        var settings = Validate();

        settings.DefaultModelType.Should().Be(AiModelType.Cloud);
        settings.FallbackMode.Should().Be(AiFallbackMode.None);
        settings.Options.Should().ContainSingle(o => o.ModelType == AiModelType.Cloud && o.Enabled && o.Provider == "Fake");
        settings.Options.Should().ContainSingle(o => o.ModelType == AiModelType.Local && !o.Enabled && o.DisabledReason!.Contains("Ai:Local:Provider"));
    }

    [Fact]
    public void Flat_legacy_settings_configure_the_cloud_model()
    {
        var settings = Validate(("Ai:Provider", "OpenAI"), ("Ai:ApiKey", "key"), ("Ai:ModelName", "gpt-4o-mini"), ("Ai:Endpoint", "https://llm.example/v1"));

        settings.Cloud.Provider.Should().Be("OpenAI");
        settings.Cloud.ModelName.Should().Be("gpt-4o-mini");
        settings.Cloud.Endpoint.Should().Be("https://llm.example/v1");
    }

    [Fact]
    public void Local_openai_compatible_runtime_needs_no_api_key_and_defaults_to_ollama()
    {
        var settings = Validate(("Ai:Local:Provider", "openai"), ("Ai:Local:ModelName", "llama3.1:8b"));

        settings.Local.Provider.Should().Be("OpenAI");
        settings.Local.Endpoint.Should().Be(AiProviderRegistry.DefaultLocalEndpoint);
        settings.Options.Should().ContainSingle(o => o.ModelType == AiModelType.Local && o.Enabled && o.ModelName == "llama3.1:8b");
    }

    [Fact]
    public void Anthropic_defaults_to_the_anthropic_endpoint()
    {
        var settings = Validate(("Ai:Cloud:Provider", "Anthropic"), ("Ai:Cloud:ApiKey", "key"), ("Ai:Cloud:ModelName", "claude-sonnet-4-5"));

        settings.Cloud.Endpoint.Should().Be(AnthropicProvider.DefaultEndpoint);
    }

    [Theory]
    [InlineData("Ai:Cloud:Provider", "OpenAI", "Ai:Cloud:ApiKey is required for provider 'OpenAI'")]
    [InlineData("Ai:Cloud:Provider", "Gemini", "Ai:Cloud:Provider 'Gemini' is not supported")]
    [InlineData("Ai:DefaultModelType", "Local", "Ai:DefaultModelType is 'Local', but Ai:Local:Provider is 'None'")]
    [InlineData("Ai:DefaultModelType", "Edge", "Ai:DefaultModelType 'Edge' is not supported")]
    [InlineData("Ai:Fallback:Mode", "Sometimes", "Ai:Fallback:Mode 'Sometimes' is not supported")]
    public void Invalid_settings_fail_with_a_message_naming_the_setting(string key, string value, string expected)
    {
        var act = () => Validate(("Ai:Cloud:ModelName", "m"), (key, value));

        act.Should().Throw<InvalidOperationException>().WithMessage($"Invalid AI configuration:*{expected}*");
    }

    [Fact]
    public void Lists_every_problem_at_once()
    {
        var act = () => Validate(("Ai:Local:Provider", "OpenAI"), ("Ai:Local:Endpoint", "localhost:11434"), ("Ai:Local:ModelName", ""));

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Ai:Local:Endpoint 'localhost:11434' must be an absolute http(s) URL.")
            .And.Contain("Ai:Local:ModelName is required");
    }

    [Fact]
    public void Both_models_disabled_is_rejected()
    {
        var act = () => Validate(("Ai:Cloud:Provider", "None"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Configure at least one model*");
    }

    [Fact]
    public void Registration_resolves_the_provider_for_each_model_type()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAiProvider(Config(
            ("Ai:Cloud:Provider", "OpenAI"), ("Ai:Cloud:ApiKey", "key"), ("Ai:Cloud:ModelName", "gpt-4o-mini"),
            ("Ai:Local:Provider", "Fake"), ("Ai:DefaultModelType", "Local")));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IAiProviderRegistry>();

        registry.Get(AiModelType.Cloud).Should().BeOfType<OpenAiProvider>();
        registry.Get(AiModelType.Local).Should().BeOfType<FakeAiProvider>();
        scope.ServiceProvider.GetRequiredService<IAiProvider>().Should().BeOfType<FakeAiProvider>("the default model type is Local");
    }

    [Fact]
    public void Getting_a_disabled_model_throws_a_configuration_error()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAiProvider(Config());
        using var provider = services.BuildServiceProvider();
        var registry = provider.CreateScope().ServiceProvider.GetRequiredService<IAiProviderRegistry>();

        var act = () => registry.Get(AiModelType.Local);

        act.Should().Throw<AiModelUnavailableException>()
            .Where(e => e.IsConfigurationError && e.ModelType == AiModelType.Local && e.Message.Contains("not configured"));
    }

    [Theory]
    [InlineData("""{"data":[{"id":"llama3.1:latest"}]}""", "llama3.1", true)]
    [InlineData("""{"data":[{"id":"llama3.1"}]}""", "llama3.1:latest", true)]
    [InlineData("""{"data":[{"id":"qwen2.5:7b"}]}""", "llama3.1", false)]
    [InlineData("""{"data":[]}""", "anything", true)]
    public void Recognises_installed_local_models(string body, string model, bool expected) =>
        AiProviderRegistry.ListsModel(body, model).Should().Be(expected);
}
