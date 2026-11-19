using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Queue;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class RequeueTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        (await admin.CreateUserAsync("carol", "Reviewer-Pass-1!", "Reviewer")).EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Admin_can_requeue_a_failed_document_and_it_is_processed_again()
    {
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        var id = await UploadAsync(admin);

        (await admin.SendAsync(HttpMethod.Post, $"/api/documents/{id}/requeue", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        await _factory.DrainQueueAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var document = await db.Documents.SingleAsync(d => d.Id == id);
            document.UpdateStatus(DocumentStatus.Failed);
            await db.SaveChangesAsync();
        }

        using var carol = _factory.CreateApiClient();
        (await carol.LoginAsync("carol", "Reviewer-Pass-1!")).EnsureSuccessStatusCode();
        (await carol.SendAsync(HttpMethod.Post, $"/api/documents/{id}/requeue", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await admin.SendAsync(HttpMethod.Post, $"/api/documents/{id}/requeue", null))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await admin.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}")).GetProperty("status").GetString().Should().Be("Queued");
        (await _factory.DrainQueueAsync()).Should().NotBeEmpty();
        (await admin.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}")).GetProperty("status").GetString().Should().Be("NeedsReview");

        (await admin.SendAsync(HttpMethod.Post, $"/api/documents/{Guid.NewGuid()}/requeue", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Concurrent_requeues_queue_the_document_once()
    {
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        var id = await UploadAsync(admin);
        await _factory.DrainQueueAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Documents.SingleAsync(d => d.Id == id)).UpdateStatus(DocumentStatus.Failed);
            await db.SaveChangesAsync();
        }

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => admin.SendAsync(HttpMethod.Post, $"/api/documents/{id}/requeue", null)));

        responses.Select(r => r.StatusCode).Should().ContainSingle(s => s == HttpStatusCode.Accepted)
            .And.Subject.Should().OnlyContain(s => s == HttpStatusCode.Accepted || s == HttpStatusCode.Conflict);
        using var verify = _factory.Services.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<AppDbContext>().QueueCommands
            .CountAsync(c => c.DocumentId == id && c.Status != Domain.Enums.QueueCommandStatus.Completed)).Should().Be(1);
    }

    [Fact]
    public async Task Upload_validation_failures_return_415_for_non_pdf_files()
    {
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        var file = new ByteArrayContent("just text"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var response = await admin.SendAsync(HttpMethod.Post, "/api/documents", new MultipartFormDataContent { { file, "file", "notes.txt" } });

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Rejected_documents_cannot_be_requeued_or_retried()
    {
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        var id = await UploadAsync(admin);
        await _factory.DrainQueueAsync();

        (await admin.SendAsync(HttpMethod.Post, $"/api/documents/{id}/review/reject", JsonContent.Create(new { reason = "Not an invoice" })))
            .EnsureSuccessStatusCode();

        var detail = await admin.Http.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/review");
        detail.GetProperty("status").GetString().Should().Be("Rejected");
        detail.GetProperty("failureReason").ValueKind.Should().Be(JsonValueKind.Null);
        (await admin.SendAsync(HttpMethod.Post, $"/api/documents/{id}/requeue", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.SendAsync(HttpMethod.Post, $"/api/documents/{id}/retry-with-model", JsonContent.Create(new { modelType = "Cloud" })))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Completed_queue_commands_older_than_the_retention_are_purged()
    {
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        var oldId = await UploadAsync(admin);
        await _factory.DrainQueueAsync();
        var recentId = await UploadAsync(admin);
        await _factory.DrainQueueAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"QueueCommands\" SET \"CompletedAtUtc\" = now() - interval '40 days' WHERE \"DocumentId\" = {oldId}");

        var purged = await scope.ServiceProvider.GetRequiredService<IQueueCommandRepository>()
            .PurgeCompletedAsync(DateTime.UtcNow - TimeSpan.FromDays(30));

        purged.Should().Be(1);
        (await db.QueueCommands.AnyAsync(c => c.DocumentId == oldId)).Should().BeFalse();
        (await db.QueueCommands.AnyAsync(c => c.DocumentId == recentId)).Should().BeTrue();
        (await db.Documents.AnyAsync(d => d.Id == oldId)).Should().BeTrue();
    }

    private static async Task<Guid> UploadAsync(ApiClient client)
    {
        var file = new ByteArrayContent(TestPdf.Create("Invoice INV-2026-009"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var response = await client.SendAsync(HttpMethod.Post, "/api/documents", new MultipartFormDataContent { { file, "file", "invoice.pdf" } });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
