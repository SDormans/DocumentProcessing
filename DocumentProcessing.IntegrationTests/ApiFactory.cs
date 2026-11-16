using System.Net;
using System.Net.Http.Json;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Infrastructure.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocumentProcessing.IntegrationTests;

/// <summary>
/// Runs the real API (migrations, Identity, cookie auth, policies) against a Testcontainers PostgreSQL database,
/// with local file storage in a temp directory and a fake AI provider that returns a valid invoice.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminUserName = "admin";
    public const string AdminPassword = "Integration-Admin-1!";
    public const string ExtractedInvoiceJson = """
        {
          "supplierName": "Acme B.V.",
          "invoiceNumber": "INV-2026-001",
          "invoiceDate": "2026-10-01",
          "dueDate": "2026-10-31",
          "currency": "EUR",
          "subtotal": 100.00,
          "taxAmount": 21.00,
          "totalAmount": 121.00,
          "lines": [ { "description": "Consulting", "quantity": 2, "unitPrice": 50, "amount": 100 } ],
          "warnings": []
        }
        """;

    private readonly string _connectionString;
    private readonly string _storagePath = Path.Combine(Path.GetTempPath(), $"dp-it-{Guid.NewGuid():N}");

    public ApiFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("FileStorage:Provider", "Local");
        builder.UseSetting("FileStorage:BasePath", _storagePath);
        builder.UseSetting("Ai:Provider", "Fake");
        builder.UseSetting("Authentication:Bootstrap:Username", AdminUserName);
        builder.UseSetting("Authentication:Bootstrap:Password", AdminPassword);
        builder.UseSetting("Authentication:Bootstrap:Email", "admin@example.test");
        builder.UseSetting("Authentication:Bootstrap:Role", "Admin");
        builder.UseSetting("AllowedHosts", "*");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiProvider>();
            services.AddScoped<IAiProvider>(_ =>
                new FakeAiProvider(true, InvoiceExtractionSchema.Parse(ExtractedInvoiceJson)));
        });
    }

    /// <summary>A cookie-aware client over HTTPS (auth cookies are Secure outside Development).</summary>
    public ApiClient CreateApiClient()
    {
        var cookies = new CookieContainer();
        var client = CreateDefaultClient(new Uri("https://localhost"), new CookieContainerHandler(cookies));
        return new ApiClient(client, cookies);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_storagePath))
            Directory.Delete(_storagePath, recursive: true);
    }
}

public sealed class ApiClient(HttpClient http, CookieContainer cookies) : IDisposable
{
    public HttpClient Http { get; } = http;

    public Task<HttpResponseMessage> LoginAsync(string userName, string password) =>
        SendAsync(HttpMethod.Post, "/api/auth/login", JsonContent.Create(new { username = userName, password }));

    public async Task<HttpResponseMessage> CreateUserAsync(string userName, string password, string role)
    {
        return await SendAsync(HttpMethod.Post, "/api/auth/register",
            JsonContent.Create(new { username = userName, password, email = $"{userName}@example.test", role }));
    }

    /// <summary>Sends a request, echoing the anti-forgery cookie as a header when the API issues one.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        var xsrf = cookies.GetCookies(Http.BaseAddress!)["XSRF-TOKEN"];
        if (xsrf is not null)
            request.Headers.Add("X-XSRF-TOKEN", xsrf.Value);
        return Http.SendAsync(request);
    }

    public void Dispose() => Http.Dispose();
}
