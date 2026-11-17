using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class AiModelSelectionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "Reviewer-Pass-1!";
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        (await admin.CreateUserAsync("dave", Password, "Reviewer")).EnsureSuccessStatusCode();
        (await admin.CreateUserAsync("erin", Password, "Reviewer")).EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<ApiClient> SignInAsync(string user)
    {
        var client = _factory.CreateApiClient();
        (await client.LoginAsync(user, Password)).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<HttpResponseMessage> SelectAsync(ApiClient client, string modelType) =>
        client.SendAsync(HttpMethod.Put, "/api/settings/ai", JsonContent.Create(new { modelType }));

    [Fact]
    public async Task The_choice_is_saved_per_user_and_survives_signing_out()
    {
        using (var dave = await SignInAsync("dave"))
        {
            var settings = await dave.Http.GetFromJsonAsync<JsonElement>("/api/settings/ai");
            settings.GetProperty("selectedModelType").GetString().Should().Be("Cloud");
            settings.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("modelType").GetString())
                .Should().Equal("Cloud", "Local");

            (await SelectAsync(dave, "Local")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await dave.SendAsync(HttpMethod.Post, "/api/auth/logout")).EnsureSuccessStatusCode();
        }

        using var daveAgain = await SignInAsync("dave");
        (await daveAgain.Http.GetFromJsonAsync<JsonElement>("/api/settings/ai?check=true"))
            .GetProperty("selectedModelType").GetString().Should().Be("Local");

        using var erin = await SignInAsync("erin");
        (await erin.Http.GetFromJsonAsync<JsonElement>("/api/settings/ai"))
            .GetProperty("selectedModelType").GetString().Should().Be("Cloud", "each user has their own choice");
    }

    [Fact]
    public async Task Unknown_model_types_are_rejected_with_a_clear_message()
    {
        using var dave = await SignInAsync("dave");

        var response = await SelectAsync(dave, "Quantum");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()
            .Should().Be("Unknown model type 'Quantum'. Use 'Cloud' or 'Local'.");
    }

    [Fact]
    public async Task Uploads_use_the_chosen_model_and_a_failed_document_can_be_retried_with_the_other()
    {
        using var dave = await SignInAsync("dave");
        (await SelectAsync(dave, "Local")).EnsureSuccessStatusCode();
        var id = await UploadAsync(dave);

        (await dave.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}")).GetProperty("aiModelType").GetString().Should().Be("Local");
        await _factory.DrainQueueAsync();
        var detail = await dave.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/review");
        detail.GetProperty("processedWithModelType").GetString().Should().Be("Local");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Documents.SingleAsync(d => d.Id == id)).UpdateStatus(DocumentStatus.Failed);
            await db.SaveChangesAsync();
        }

        using (var erin = await SignInAsync("erin"))
            (await erin.SendAsync(HttpMethod.Post, $"/api/documents/{id}/retry-with-model", JsonContent.Create(new { modelType = "Cloud" })))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, "other users cannot see the document");

        (await dave.SendAsync(HttpMethod.Post, $"/api/documents/{id}/retry-with-model", JsonContent.Create(new { modelType = "Cloud" })))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        await _factory.DrainQueueAsync();

        var document = await dave.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}");
        document.GetProperty("aiModelType").GetString().Should().Be("Cloud");
        document.GetProperty("status").GetString().Should().Be("NeedsReview");
    }

    private static async Task<Guid> UploadAsync(ApiClient client)
    {
        var file = new ByteArrayContent(TestPdf.Create("Invoice INV-2026-010"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var response = await client.SendAsync(HttpMethod.Post, "/api/documents", new MultipartFormDataContent { { file, "file", "invoice.pdf" } });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
