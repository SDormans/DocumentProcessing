using DocumentProcessing.Application.Services;
using FluentAssertions;
using Xunit;

namespace DocumentProcessing.UnitTests.Services;

public class DevelopmentUserServiceTests
{
    [Fact]
    public async Task ServiceWithoutBootstrapCredentials_HasNoDefaultAccounts()
    {
        var service = new DevelopmentUserService();

        (await service.ValidateUserAsync("admin", "Admin123!")).Should().BeNull();
        (await service.ValidateUserAsync("reviewer", "Reviewer123!")).Should().BeNull();
    }

    [Fact]
    public async Task BootstrapUser_UsesConfiguredCredentials()
    {
        var service = new DevelopmentUserService(
            "portfolio-admin",
            "A-long-demo-password-2026",
            "admin@example.invalid",
            "Admin");

        (await service.ValidateUserAsync("portfolio-admin", "A-long-demo-password-2026"))
            .Should().NotBeNull();
        (await service.ValidateUserAsync("portfolio-admin", "wrong-password"))
            .Should().BeNull();
    }

    [Fact]
    public void PartialBootstrapCredentials_AreRejected()
    {
        var act = () => new DevelopmentUserService("admin", null);

        act.Should().Throw<InvalidOperationException>();
    }
}
