using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class UploadExtractReviewTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string ReviewerPassword = "Reviewer-Pass-1!";
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        (await admin.CreateUserAsync("alice", ReviewerPassword, "Reviewer")).EnsureSuccessStatusCode();
        (await admin.CreateUserAsync("bob", ReviewerPassword, "Reviewer")).EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Uploaded_invoice_is_extracted_and_can_be_corrected_and_approved()
    {
        using var alice = await SignInAsync("alice");

        var id = await UploadAsync(alice, "Invoice INV-2026-001 from Acme B.V. Total EUR 121.00");
        (await GetDocumentAsync(alice, id)).GetProperty("status").GetString().Should().Be("Queued");

        (await _factory.DrainQueueAsync()).Should().Equal(QueueCommandOutcome.Completed);

        var review = await alice.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/review");
        review.GetProperty("status").GetString().Should().Be("NeedsReview");
        review.GetProperty("reviewStatus").GetString().Should().Be("Pending");
        var fields = review.GetProperty("extractedFields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("fieldName").GetString()!, f => f.GetProperty("value").GetString());
        fields.Should().ContainValue("INV-2026-001");
        var invoiceNumberField = fields.Single(f => f.Value == "INV-2026-001").Key;

        var corrections = new { correctedValues = new Dictionary<string, string> { [invoiceNumberField] = "INV-2026-001-A" } };
        (await alice.SendAsync(HttpMethod.Put, $"/api/documents/{id}/review/corrections", JsonContent.Create(corrections)))
            .EnsureSuccessStatusCode();
        (await alice.SendAsync(HttpMethod.Post, $"/api/documents/{id}/review/approve", JsonContent.Create(new { comment = "ok" })))
            .EnsureSuccessStatusCode();

        review = await alice.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/review");
        review.GetProperty("reviewStatus").GetString().Should().Be("Approved");
        using (var scope = _factory.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DocumentReviews
                .SingleAsync(r => r.DocumentId == id);
            stored.ApprovedValuesJson.Should().Contain("INV-2026-001-A");
        }

        var audit = await alice.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/review/audit-log");
        audit.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Processing_the_same_document_twice_does_not_duplicate_results()
    {
        using var alice = await SignInAsync("alice");
        var id = await UploadAsync(alice, "Invoice INV-2026-001");

        (await _factory.DrainQueueAsync()).Should().Equal(QueueCommandOutcome.Completed);
        (await _factory.DrainQueueAsync()).Should().BeEmpty();

        (await GetDocumentAsync(alice, id)).GetProperty("status").GetString().Should().Be("NeedsReview");
    }

    [Fact]
    public async Task Reviewers_cannot_see_or_review_each_others_documents()
    {
        using var alice = await SignInAsync("alice");
        using var bob = await SignInAsync("bob");
        var id = await UploadAsync(alice, "Invoice INV-2026-001");
        await _factory.DrainQueueAsync();

        (await bob.Http.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.Http.GetAsync($"/api/documents/{id}/review")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var approve = await bob.SendAsync(HttpMethod.Post, $"/api/documents/{id}/review/approve", JsonContent.Create(new { comment = "x" }));
        approve.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await approve.Content.ReadAsStringAsync()).Should().Contain("Document niet gevonden");
        (await bob.Http.GetFromJsonAsync<JsonElement>("/api/documents")).EnumerateArray()
            .Should().NotContain(d => d.GetProperty("id").GetGuid() == id);

        using var admin = await SignInAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword);
        (await admin.Http.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<ApiClient> SignInAsync(string userName, string password = ReviewerPassword)
    {
        var client = _factory.CreateApiClient();
        (await client.LoginAsync(userName, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<Guid> UploadAsync(ApiClient client, string text)
    {
        var file = new ByteArrayContent(TestPdf.Create(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "invoice.pdf" } };

        var response = await client.SendAsync(HttpMethod.Post, "/api/documents", form);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<JsonElement> GetDocumentAsync(ApiClient client, Guid id) =>
        client.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}");
}
