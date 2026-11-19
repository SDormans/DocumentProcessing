using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DocumentProcessing.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DocumentProcessing.UnitTests.Ai;

public class AnthropicProviderTests
{
    private const string ToolInput = """
        {"supplierName":"Acme BV","invoiceNumber":"INV-42","invoiceDate":"2026-01-15","dueDate":null,"currency":"EUR",
         "subtotal":100.00,"taxAmount":21.00,"totalAmount":121.00,"lines":[],"warnings":[]}
        """;

    private readonly AiOptions _options = new()
    {
        Provider = "Anthropic",
        Endpoint = AnthropicProvider.DefaultEndpoint,
        ApiKey = "test-key",
        ModelName = "claude-test",
        MaxOutputTokens = 1000,
        InputCostPerMillionTokens = 3m,
        OutputCostPerMillionTokens = 15m
    };

    [Fact]
    public async Task Sends_a_forced_tool_call_and_parses_the_tool_input()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Message("tool_use", ToolInput, "tool_use"));

        var result = await CreateProvider(handler).ExtractAsync("Invoice text");

        result.Success.Should().BeTrue();
        result.SupplierName.Should().Be("Acme BV");
        result.InvoiceNumber.Should().Be("INV-42");
        result.EstimatedCost.Should().Be(1000 / 1_000_000m * 3m + 500 / 1_000_000m * 15m);

        var request = handler.Request!;
        request.RequestUri!.ToString().Should().Be("https://api.anthropic.com/v1/messages");
        request.Headers.GetValues("x-api-key").Should().Equal("test-key");
        request.Headers.GetValues("anthropic-version").Should().Equal(AnthropicProvider.ApiVersion);
        var body = JsonNode.Parse(handler.Body!)!;
        body["model"]!.GetValue<string>().Should().Be("claude-test");
        body["tool_choice"]!["type"]!.GetValue<string>().Should().Be("tool");
        body["tool_choice"]!["name"]!.GetValue<string>().Should().Be(body["tools"]![0]!["name"]!.GetValue<string>());
        body["tools"]![0]!["input_schema"]!["properties"]!["supplierName"].Should().NotBeNull();
        body["messages"]![0]!["content"]!.GetValue<string>().Should().Be("Invoice text");
    }

    [Fact]
    public async Task Truncated_response_fails_but_still_reports_the_cost()
    {
        var result = await CreateProvider(new StubHandler(HttpStatusCode.OK, Message("tool_use", ToolInput, "max_tokens"))).ExtractAsync("x");

        result.Success.Should().BeFalse();
        result.Warnings.Should().ContainMatch("*truncated*");
        result.EstimatedCost.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Response_without_a_tool_call_fails()
    {
        var result = await CreateProvider(new StubHandler(HttpStatusCode.OK, Message("text", "\"Sorry\"", "end_turn"))).ExtractAsync("x");

        result.Success.Should().BeFalse();
        result.Warnings.Should().ContainMatch("*did not contain the structured invoice data*");
    }

    [Fact]
    public async Task Bad_request_is_reported_without_throwing()
    {
        var result = await CreateProvider(new StubHandler(HttpStatusCode.BadRequest, """{"type":"error"}""")).ExtractAsync("x");

        result.Success.Should().BeFalse();
        result.Warnings.Should().ContainMatch("*HTTP 400*");
    }

    [Fact]
    public async Task Server_errors_throw_so_the_resilience_pipeline_can_retry()
    {
        var act = () => CreateProvider(new StubHandler(HttpStatusCode.ServiceUnavailable, "")).ExtractAsync("x");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private AnthropicProvider CreateProvider(StubHandler handler)
    {
        var client = new HttpClient(handler);
        AnthropicProvider.ConfigureClient(client, _options);
        return new AnthropicProvider(client, _options, NullLogger<AnthropicProvider>.Instance);
    }

    private static string Message(string contentType, string payload, string stopReason)
    {
        var content = contentType == "tool_use"
            ? $$$"""{"type":"tool_use","id":"t1","name":"record_invoice","input":{{{payload}}}}"""
            : $$"""{"type":"text","text":{{payload}}}""";
        return $$$"""{"id":"m1","type":"message","role":"assistant","content":[{{{content}}}],"stop_reason":"{{{stopReason}}}","usage":{"input_tokens":1000,"output_tokens":500}}""";
    }

    private sealed class StubHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
