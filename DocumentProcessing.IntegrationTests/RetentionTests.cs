using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentProcessing.Application.Services;
using DocumentProcessing.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class RetentionTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task Admin_can_delete_a_document_and_everything_derived_from_it()
    {
        using var alice = await SignInAsync("alice", ReviewerPassword);
        var id = await UploadAsync(alice);
        await _factory.DrainQueueAsync();

        (await alice.SendAsync(HttpMethod.Delete, $"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = await SignInAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword);
        (await admin.SendAsync(HttpMethod.Delete, $"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.SendAsync(HttpMethod.Delete, $"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await alice.Http.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.InvoiceExtractionResults.CountAsync(r => r.DocumentId == id)).Should().Be(0);
        (await db.DocumentTextExtractions.CountAsync(t => t.DocumentId == id)).Should().Be(0);
        (await db.QueueCommands.CountAsync(c => c.DocumentId == id)).Should().Be(0);
    }

    [Fact]
    public async Task Documents_past_their_retention_date_are_purged()
    {
        using var alice = await SignInAsync("alice", ReviewerPassword);
        var expired = await UploadAsync(alice);
        var kept = await UploadAsync(alice);
        await _factory.DrainQueueAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Documents.SingleAsync(d => d.Id == expired)).SetRetention(DateTime.UtcNow.AddDays(-1));
            (await db.Documents.SingleAsync(d => d.Id == kept)).SetRetention(DateTime.UtcNow.AddYears(7));
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var deleted = await scope.ServiceProvider.GetRequiredService<IDocumentDeletion>().PurgeExpiredAsync(DateTime.UtcNow);
            deleted.Should().Be(1);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Documents.Select(d => d.Id).ToListAsync()).Should().Equal(kept);
        }
    }

    private async Task<ApiClient> SignInAsync(string userName, string password)
    {
        var client = _factory.CreateApiClient();
        (await client.LoginAsync(userName, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<Guid> UploadAsync(ApiClient client)
    {
        var file = new ByteArrayContent(TestPdf.Create("Invoice INV-2026-001 from Acme B.V."));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "invoice.pdf" } };
        var response = await client.SendAsync(HttpMethod.Post, "/api/documents", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
