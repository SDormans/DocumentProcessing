using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class DocumentListTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string ReviewerPassword = "Reviewer-Pass-1!";
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        (await admin.CreateUserAsync("alice", ReviewerPassword, "Reviewer")).EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task List_is_paged_filtered_and_searchable()
    {
        using var alice = _factory.CreateApiClient();
        (await alice.LoginAsync("alice", ReviewerPassword)).EnsureSuccessStatusCode();
        await UploadAsync(alice, "acme-2026-01.pdf");
        await UploadAsync(alice, "globex-2026-02.pdf");
        await _factory.DrainQueueAsync();
        await UploadAsync(alice, "initech-2026-03.pdf");

        var page = await alice.Http.GetAsync("/api/documents?page=1&pageSize=2");
        page.Headers.GetValues("X-Total-Count").Should().Equal("3");
        (await page.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength().Should().Be(2);
        var last = await alice.Http.GetFromJsonAsync<JsonElement>("/api/documents?page=2&pageSize=2");
        last.GetArrayLength().Should().Be(1);

        var search = await alice.Http.GetFromJsonAsync<JsonElement>("/api/documents?search=ACME");
        search.EnumerateArray().Select(d => d.GetProperty("originalFileName").GetString()).Should().Equal("acme-2026-01.pdf");

        var queued = await alice.Http.GetFromJsonAsync<JsonElement>("/api/documents?status=queued");
        queued.EnumerateArray().Select(d => d.GetProperty("originalFileName").GetString()).Should().Equal("initech-2026-03.pdf");

        (await alice.Http.GetAsync("/api/documents?status=Bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await alice.Http.GetAsync("/api/documents?pageSize=500")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task UploadAsync(ApiClient client, string fileName)
    {
        var file = new ByteArrayContent(TestPdf.Create($"Invoice {fileName}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        (await client.SendAsync(HttpMethod.Post, "/api/documents", form)).EnsureSuccessStatusCode();
    }
}
