using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentProcessing.Api.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class TwoFactorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async Task InitializeAsync() => _factory = new ApiFactory(await postgres.CreateDatabaseAsync());

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Enabled_authenticator_is_required_at_sign_in()
    {
        string key;
        using (var admin = _factory.CreateApiClient())
        {
            (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
            var setup = await (await admin.SendAsync(HttpMethod.Post, "/api/auth/mfa/setup")).Content.ReadFromJsonAsync<JsonElement>();
            key = setup.GetProperty("sharedKey").GetString()!;
            setup.GetProperty("authenticatorUri").GetString().Should().StartWith("otpauth://totp/");

            (await admin.SendAsync(HttpMethod.Post, "/api/auth/mfa/enable", JsonContent.Create(new { code = "000000" })))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var enabled = await admin.SendAsync(HttpMethod.Post, "/api/auth/mfa/enable", JsonContent.Create(new { code = Totp(key) }));
            enabled.StatusCode.Should().Be(HttpStatusCode.OK);
            (await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").GetArrayLength().Should().Be(10);
        }

        using var client = _factory.CreateApiClient();
        var withoutCode = await client.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword);
        withoutCode.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await withoutCode.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requiresTwoFactor").GetBoolean().Should().BeTrue();

        (await client.Http.PostAsJsonAsync("/api/auth/login",
            new { username = ApiFactory.AdminUserName, password = ApiFactory.AdminPassword, twoFactorCode = "000000" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.Http.PostAsJsonAsync("/api/auth/login",
            new { username = ApiFactory.AdminUserName, password = ApiFactory.AdminPassword, twoFactorCode = Totp(key) }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Recovery_codes_work_once_and_admin_reset_turns_two_factor_off()
    {
        const string password = "Bob-Pass-1234!";
        using var admin = _factory.CreateApiClient();
        (await admin.LoginAsync(ApiFactory.AdminUserName, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        (await admin.CreateUserAsync("bob", password, "Reviewer")).EnsureSuccessStatusCode();

        string[] codes;
        string bobId;
        using (var bob = _factory.CreateApiClient())
        {
            (await bob.LoginAsync("bob", password)).EnsureSuccessStatusCode();
            bobId = (await bob.Http.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("userId").GetString()!;
            (await bob.SendAsync(HttpMethod.Post, "/api/auth/mfa/recovery-codes")).StatusCode.Should().Be(HttpStatusCode.Conflict);
            var key = (await (await bob.SendAsync(HttpMethod.Post, "/api/auth/mfa/setup")).Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("sharedKey").GetString()!;
            var enabled = await bob.SendAsync(HttpMethod.Post, "/api/auth/mfa/enable", JsonContent.Create(new { code = Totp(key) }));
            codes = (await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes")
                .EnumerateArray().Select(c => c.GetString()!).ToArray();
        }

        using var client = _factory.CreateApiClient();
        object Login(string? recoveryCode) => new { username = "bob", password, recoveryCode };
        (await client.Http.PostAsJsonAsync("/api/auth/login", Login("not-a-code"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.Http.PostAsJsonAsync("/api/auth/login", Login(codes[0]))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.Http.PostAsJsonAsync("/api/auth/login", Login(codes[0]))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "each code works once");

        (await client.SendAsync(HttpMethod.Post, $"/api/auth/users/{bobId}/mfa/reset")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.SendAsync(HttpMethod.Post, "/api/auth/users/no-such-user/mfa/reset")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.SendAsync(HttpMethod.Post, $"/api/auth/users/{bobId}/mfa/reset")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var afterReset = _factory.CreateApiClient();
        (await afterReset.LoginAsync("bob", password)).StatusCode.Should().Be(HttpStatusCode.OK, "two-factor sign-in is off after the reset");
        (await afterReset.Http.PostAsJsonAsync("/api/auth/login", Login(codes[1]))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Admin_policy_requires_the_mfa_claim_only_when_configured(bool requireMfa, bool signedInWithMfa, bool allowed)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorizationPolicies(requireMfa);
        await using var provider = services.BuildServiceProvider();
        var claims = new List<Claim> { new(ClaimTypes.Role, "Admin") };
        if (signedInWithMfa)
            claims.Add(new Claim(AuthSetup.MfaClaimType, AuthSetup.MfaClaimValue));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, "IsAdmin");

        result.Succeeded.Should().Be(allowed);
    }

    /// <summary>RFC 6238 code for the current 30-second step, as an authenticator app would show it.</summary>
    private static string Totp(string base32Key)
    {
        var key = FromBase32(base32Key);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] FromBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in input.TrimEnd('=').ToUpperInvariant())
        {
            value = (value << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(value >> (bits - 8)));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}
