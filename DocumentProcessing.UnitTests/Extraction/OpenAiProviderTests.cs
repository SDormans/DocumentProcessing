using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DocumentProcessing.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocumentProcessing.UnitTests.Extraction;

public class OpenAiProviderTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (OpenAiProvider Provider, StubHandler Handler) Create(HttpStatusCode status, string body, string header = "Authorization")
    {
        var options = new AiOptions { Provider = "OpenAI", ApiKey = "test-key", ApiKeyHeader = header, Endpoint = "https://llm.example/v1" };
        var handler = new StubHandler(status, body);
        var client = new HttpClient(handler);
        OpenAiProvider.ConfigureClient(client, options);
        return (new OpenAiProvider(client, Options.Create(options), NullLogger<OpenAiProvider>.Instance), handler);
    }

    private static string Completion(string? content, string finishReason = "stop", string? refusal = null) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["finish_reason"] = finishReason,
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content, ["refusal"] = refusal }
        }),
        ["usage"] = new JsonObject { ["prompt_tokens"] = 1000, ["completion_tokens"] = 200 }
    }.ToJsonString();

    [Fact]
    public async Task ExtractAsync_SendsStrictJsonSchemaRequest()
    {
        var (provider, handler) = Create(HttpStatusCode.OK, Completion(InvoiceExtractionSchemaTests.ValidJson));

        var result = await provider.ExtractAsync("prompt text");

        result.Success.Should().BeTrue();
        result.InvoiceNumber.Should().Be("INV-2026-001");
        handler.Request!.RequestUri.Should().Be(new Uri("https://llm.example/v1/chat/completions"));
        handler.Request.Headers.Authorization!.ToString().Should().Be("Bearer test-key");
        var body = JsonNode.Parse(handler.RequestBody!)!;
        body["response_format"]!["type"]!.GetValue<string>().Should().Be("json_schema");
        body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>().Should().BeTrue();
        body["messages"]![1]!["content"]!.GetValue<string>().Should().Be("prompt text");
    }

    [Fact]
    public async Task ExtractAsync_AzureStyleHeader_UsesApiKeyHeader()
    {
        var (provider, handler) = Create(HttpStatusCode.OK, Completion(InvoiceExtractionSchemaTests.ValidJson), "api-key");

        await provider.ExtractAsync("prompt");

        handler.Request!.Headers.GetValues("api-key").Should().ContainSingle("test-key");
        handler.Request.Headers.Authorization.Should().BeNull();
    }

    [Theory]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":42}}]}")]
    public async Task ExtractAsync_MalformedEnvelope_ReturnsFailure(string body)
    {
        var (provider, _) = Create(HttpStatusCode.OK, body);

        var result = await provider.ExtractAsync("prompt");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_Refusal_ReturnsFailure()
    {
        var (provider, _) = Create(HttpStatusCode.OK, Completion(null, refusal: "cannot help"));

        var result = await provider.ExtractAsync("prompt");

        result.Success.Should().BeFalse();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("cannot help");
    }

    [Fact]
    public async Task ExtractAsync_Truncated_ReturnsFailure()
    {
        var (provider, _) = Create(HttpStatusCode.OK, Completion("{\"supplierName\":", finishReason: "length"));

        (await provider.ExtractAsync("prompt")).Success.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "stop", "cannot help")]
    [InlineData("{\"supplierName\":", "length", null)]
    [InlineData("", "stop", null)]
    public async Task ExtractAsync_FailedResponse_StillReportsTheBilledCost(string? content, string finishReason, string? refusal)
    {
        var options = new AiOptions
        {
            Provider = "OpenAI", ApiKey = "test-key", Endpoint = "https://llm.example/v1",
            InputCostPerMillionTokens = 1m, OutputCostPerMillionTokens = 10m
        };
        var client = new HttpClient(new StubHandler(HttpStatusCode.OK, Completion(content, finishReason, refusal)));
        OpenAiProvider.ConfigureClient(client, options);
        var provider = new OpenAiProvider(client, Options.Create(options), NullLogger<OpenAiProvider>.Instance);

        var result = await provider.ExtractAsync("prompt");

        result.Success.Should().BeFalse();
        result.EstimatedCost.Should().Be(0.003m);
    }

    [Fact]
    public async Task ExtractAsync_ServerError_Throws()
    {
        var (provider, _) = Create(HttpStatusCode.ServiceUnavailable, "{}");

        var act = () => provider.ExtractAsync("prompt");

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
