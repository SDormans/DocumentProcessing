using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using UglyToad.PdfPig;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class DocumentContentTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async Task InitializeAsync() => _factory = new ApiFactory(await postgres.CreateDatabaseAsync());

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Preview_is_served_without_active_content()
    {
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        var file = new ByteArrayContent(ActivePdf.Create());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var upload = await admin.SendAsync(HttpMethod.Post, "/api/documents", new MultipartFormDataContent { { file, "file", "active.pdf" } });
        upload.EnsureSuccessStatusCode();
        var id = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var content = await admin.Http.GetAsync($"/api/documents/{id}/content");

        content.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await content.Content.ReadAsByteArrayAsync();
        Encoding.Latin1.GetString(bytes).Should().NotContain("/JavaScript").And.NotContain("/OpenAction").And.NotContain("/AA");
        using var pdf = PdfDocument.Open(bytes);
        pdf.GetPage(1).Text.Should().Be("Invoice 42");
    }
}
