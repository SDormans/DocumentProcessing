using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class ReviewQueueTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "Queue-Pass-1!";
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        (await admin.CreateUserAsync("uploader", Password, "Reviewer")).EnsureSuccessStatusCode();
        (await admin.CreateUserAsync("alice", Password, "Reviewer")).EnsureSuccessStatusCode();
        (await admin.CreateUserAsync("carol", Password, "Reviewer")).EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Reviewer_claims_from_the_shared_queue_and_only_one_reviewer_wins()
    {
        using var uploader = await SignInAsync("uploader");
        var id = await UploadAsync(uploader, "queued.pdf");
        await _factory.DrainQueueAsync();

        using var alice = await SignInAsync("alice");
        (await alice.Http.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "not claimed yet");
        var queue = await alice.Http.GetFromJsonAsync<JsonElement>("/api/documents/review-queue");
        queue.EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).Should().Equal(id);

        (await alice.SendAsync(HttpMethod.Post, $"/api/documents/{id}/claim")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await alice.SendAsync(HttpMethod.Post, $"/api/documents/{id}/claim")).StatusCode.Should().Be(HttpStatusCode.NoContent, "claiming twice is idempotent");
        (await alice.Http.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await alice.Http.GetFromJsonAsync<JsonElement>("/api/documents")).GetArrayLength().Should().Be(1);

        using var carol = await SignInAsync("carol");
        (await carol.SendAsync(HttpMethod.Post, $"/api/documents/{id}/claim")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await carol.Http.GetFromJsonAsync<JsonElement>("/api/documents/review-queue")).GetArrayLength().Should().Be(0);
        (await carol.SendAsync(HttpMethod.Put, $"/api/documents/{id}/assignee", JsonContent.Create(new { userId = (string?)null })))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "only admins reassign");
    }

    [Fact]
    public async Task Admin_reassigns_and_returns_documents_to_the_queue()
    {
        using var uploader = await SignInAsync("uploader");
        var id = await UploadAsync(uploader, "assign.pdf");
        await _factory.DrainQueueAsync();

        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        using var carol = await SignInAsync("carol");
        var carolId = (await carol.Http.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("userId").GetString();

        (await admin.SendAsync(HttpMethod.Put, $"/api/documents/{id}/assignee", JsonContent.Create(new { userId = carolId })))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await carol.Http.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.SendAsync(HttpMethod.Put, $"/api/documents/{id}/assignee", JsonContent.Create(new { userId = (string?)null })))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await carol.Http.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await carol.Http.GetFromJsonAsync<JsonElement>("/api/documents/review-queue")).GetArrayLength().Should().Be(1);
        (await admin.SendAsync(HttpMethod.Put, $"/api/documents/{Guid.NewGuid()}/assignee", JsonContent.Create(new { userId = carolId })))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<ApiClient> SignInAsync(string userName)
    {
        var client = _factory.CreateApiClient();
        (await client.LoginAsync(userName, Password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<Guid> UploadAsync(ApiClient client, string fileName)
    {
        var file = new ByteArrayContent(TestPdf.Create($"Invoice {fileName}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        var response = await client.SendAsync(HttpMethod.Post, "/api/documents", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
