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
        approve.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await approve.Content.ReadAsStringAsync()).Should().Contain("Document not found");
        (await bob.SendAsync(HttpMethod.Post, $"/api/documents/{id}/review/reject", JsonContent.Create(new { reason = "x" })))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.SendAsync(HttpMethod.Put, $"/api/documents/{id}/review/corrections", CorrectionsContent(("SupplierName", "x"))))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.Http.GetAsync($"/api/documents/{id}/review/audit-log")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.Http.GetFromJsonAsync<JsonElement>("/api/documents")).EnumerateArray()
            .Should().NotContain(d => d.GetProperty("id").GetGuid() == id);

        using var admin = await SignInAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword);
        (await admin.Http.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Review_actions_on_a_missing_document_return_404()
    {
        using var alice = await SignInAsync("alice");
        var missing = Guid.NewGuid();

        (await alice.SendAsync(HttpMethod.Post, $"/api/documents/{missing}/review/approve", JsonContent.Create(new { })))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await alice.SendAsync(HttpMethod.Post, $"/api/documents/{missing}/review/reject", JsonContent.Create(new { reason = "x" })))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await alice.SendAsync(HttpMethod.Put, $"/api/documents/{missing}/review/corrections", CorrectionsContent(("SupplierName", "x"))))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await alice.Http.GetAsync($"/api/documents/{missing}/review/audit-log")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Saved_corrections_are_returned_by_the_review_endpoint_after_reload()
    {
        using var alice = await SignInAsync("alice");
        var id = await UploadAsync(alice, "Invoice INV-2026-001");
        await _factory.DrainQueueAsync();

        (await alice.SendAsync(HttpMethod.Put, $"/api/documents/{id}/review/corrections",
            CorrectionsContent(("InvoiceNumber", "INV-2026-001-A"), ("TotalAmount", "122.00"))))
            .EnsureSuccessStatusCode();
        (await alice.SendAsync(HttpMethod.Put, $"/api/documents/{id}/review/corrections", CorrectionsContent(("DueDate", "2026-11-30"))))
            .EnsureSuccessStatusCode();

        var review = await alice.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/review");
        review.GetProperty("reviewStatus").GetString().Should().Be("Corrected");
        var fields = FieldsByName(review);
        fields.Keys.Should().Contain(new[] { "SupplierName", "InvoiceNumber", "InvoiceDate", "DueDate", "Currency", "Subtotal", "TaxAmount", "TotalAmount", "Lines" });
        fields["InvoiceNumber"].GetProperty("value").GetString().Should().Be("INV-2026-001");
        fields["InvoiceNumber"].GetProperty("correctedValue").GetString().Should().Be("INV-2026-001-A");
        fields["InvoiceNumber"].GetProperty("effectiveValue").GetString().Should().Be("INV-2026-001-A");
        fields["InvoiceNumber"].GetProperty("isCorrected").GetBoolean().Should().BeTrue();
        fields["TotalAmount"].GetProperty("effectiveValue").GetString().Should().Be("122.00");
        fields["DueDate"].GetProperty("effectiveValue").GetString().Should().Be("2026-11-30");
        fields["SupplierName"].GetProperty("effectiveValue").GetString().Should().Be("Acme B.V.");
        fields["SupplierName"].GetProperty("isCorrected").GetBoolean().Should().BeFalse();

        var audit = await alice.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/review/audit-log");
        audit.EnumerateArray().Should().Contain(e =>
            e.GetProperty("fieldName").GetString() == "InvoiceNumber"
            && e.GetProperty("oldValue").GetString() == "INV-2026-001"
            && e.GetProperty("newValue").GetString() == "INV-2026-001-A");
    }

    [Fact]
    public async Task Invalid_corrections_are_rejected_with_400_and_not_saved()
    {
        using var alice = await SignInAsync("alice");
        var id = await UploadAsync(alice, "Invoice INV-2026-001");
        await _factory.DrainQueueAsync();

        var response = await alice.SendAsync(HttpMethod.Put, $"/api/documents/{id}/review/corrections",
            CorrectionsContent(("InvoiceDate", "31/10/2026"), ("NotAField", "x")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").GetArrayLength().Should().Be(2);
        var review = await alice.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/review");
        review.GetProperty("reviewStatus").GetString().Should().Be("Pending");
        FieldsByName(review)["InvoiceDate"].GetProperty("effectiveValue").GetString().Should().Be("2026-10-01");
    }

    [Fact]
    public async Task Approval_validates_the_corrected_values()
    {
        using var alice = await SignInAsync("alice");
        var id = await UploadAsync(alice, "Invoice INV-2026-001");
        await _factory.DrainQueueAsync();

        (await alice.SendAsync(HttpMethod.Put, $"/api/documents/{id}/review/corrections", CorrectionsContent(("InvoiceNumber", ""))))
            .EnsureSuccessStatusCode();

        var review = await alice.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/review");
        review.GetProperty("validationResults").EnumerateArray().Should().Contain(r =>
            r.GetProperty("ruleId").GetString() == "MISSING_REQUIRED_FIELDS" && r.GetProperty("affectedFields").GetString() == "InvoiceNumber");

        var blocked = await alice.SendAsync(HttpMethod.Post, $"/api/documents/{id}/review/approve", JsonContent.Create(new { }));
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("InvoiceNumber");
        (await GetDocumentAsync(alice, id)).GetProperty("status").GetString().Should().Be("NeedsReview");

        (await alice.SendAsync(HttpMethod.Put, $"/api/documents/{id}/review/corrections", CorrectionsContent(("InvoiceNumber", "INV-2026-001-B"))))
            .EnsureSuccessStatusCode();
        (await alice.SendAsync(HttpMethod.Post, $"/api/documents/{id}/review/approve", JsonContent.Create(new { })))
            .EnsureSuccessStatusCode();

        review = await alice.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/review");
        FieldsByName(review)["InvoiceNumber"].GetProperty("approvedValue").GetString().Should().Be("INV-2026-001-B");

        var again = await alice.SendAsync(HttpMethod.Post, $"/api/documents/{id}/review/approve", JsonContent.Create(new { }));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static JsonContent CorrectionsContent(params (string Field, string Value)[] values) =>
        JsonContent.Create(new { correctedValues = values.ToDictionary(v => v.Field, v => v.Value) });

    private static Dictionary<string, JsonElement> FieldsByName(JsonElement review) =>
        review.GetProperty("extractedFields").EnumerateArray().ToDictionary(f => f.GetProperty("fieldName").GetString()!);

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
