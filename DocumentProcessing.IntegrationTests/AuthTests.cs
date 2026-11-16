using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class AuthTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async Task InitializeAsync() => _factory = new ApiFactory(await postgres.CreateDatabaseAsync());

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Protected_endpoints_require_a_signed_in_user()
    {
        using var client = _factory.CreateApiClient();

        (await client.Http.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.Http.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.LoginAsync(ApiFactory.AdminUserName, "not-the-password");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.Http.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Seeded_admin_can_sign_in_and_out()
    {
        using var client = _factory.CreateApiClient();

        (await client.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        var me = await client.Http.GetFromJsonAsync<JsonElement>("/api/auth/me");
        me.GetProperty("userName").GetString().Should().Be(ApiFactory.AdminUserName);
        me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Contain("Admin");
        (await client.Http.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.SendAsync(HttpMethod.Post, "/api/auth/logout")).EnsureSuccessStatusCode();
        (await client.Http.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Only_admins_can_create_users()
    {
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        (await admin.CreateUserAsync("reviewer1", "Reviewer-Pass-1!", "Reviewer")).EnsureSuccessStatusCode();

        using var reviewer = _factory.CreateApiClient();
        (await reviewer.LoginAsync("reviewer1", "Reviewer-Pass-1!")).EnsureSuccessStatusCode();
        var response = await reviewer.CreateUserAsync("reviewer2", "Reviewer-Pass-2!", "Admin");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Mutations_without_an_anti_forgery_token_are_rejected()
    {
        using var client = _factory.CreateApiClient();
        (await client.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();

        var response = await client.Http.PostAsJsonAsync("/api/auth/register",
            new { username = "x", password = "Reviewer-Pass-1!", email = "x@example.test", role = "Reviewer" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("anti-forgery");
    }
}
