using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentProcessing.UnitTests.Identity;

public class IdentityUserServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public IdentityUserServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 12;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 3;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IdentityUserService>();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
    }

    private IdentityUserService Service => _scope.ServiceProvider.GetRequiredService<IdentityUserService>();

    private Task SeedAsync(Dictionary<string, string?>? bootstrap = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(bootstrap ?? new()).Build();
        return IdentitySeeder.SeedAsync(_scope.ServiceProvider, configuration);
    }

    [Fact]
    public async Task Seed_WithoutBootstrapCredentials_CreatesRolesButNoUsers()
    {
        await SeedAsync();

        (await Service.ValidateUserAsync("admin", "Admin123!")).Should().BeNull();
        var roleManager = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        (await roleManager.RoleExistsAsync(AppRoles.Admin)).Should().BeTrue();
        (await roleManager.RoleExistsAsync(AppRoles.Reviewer)).Should().BeTrue();
    }

    [Fact]
    public async Task Seed_WithBootstrapCredentials_CreatesUserThatCanSignIn()
    {
        await SeedAsync(new()
        {
            ["Authentication:Bootstrap:Username"] = "portfolio-admin",
            ["Authentication:Bootstrap:Password"] = "A-long-demo-password-2026",
            ["Authentication:Bootstrap:Email"] = "admin@example.invalid",
            ["Authentication:Bootstrap:Role"] = "Admin"
        });

        var user = await Service.ValidateUserAsync("portfolio-admin", "A-long-demo-password-2026");
        user.Should().NotBeNull();
        user!.Role.Should().Be("Admin");
        (await Service.ValidateUserAsync("portfolio-admin", "wrong-password")).Should().BeNull();
    }

    [Fact]
    public async Task Seed_WithPartialBootstrapCredentials_Throws()
    {
        var act = () => SeedAsync(new() { ["Authentication:Bootstrap:Username"] = "admin" });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateUser_PersistsUserWithRole()
    {
        await SeedAsync();

        var created = await Service.CreateUserAsync("reviewer1", "Reviewer-password-1", "r@example.invalid", "Reviewer");

        (await Service.UserExistsAsync("reviewer1")).Should().BeTrue();
        var loaded = await Service.GetUserByIdAsync(created.UserId);
        loaded!.Roles.Should().ContainSingle().Which.Should().Be("Reviewer");
    }

    [Fact]
    public async Task CreateUser_WithWeakPassword_ThrowsArgumentException()
    {
        await SeedAsync();

        var act = () => Service.CreateUserAsync("reviewer1", "short", "r@example.invalid", "Reviewer");

        await act.Should().ThrowAsync<ArgumentException>();
        (await Service.UserExistsAsync("reviewer1")).Should().BeFalse();
    }

    [Fact]
    public async Task CreateUser_WithUnsupportedRole_ThrowsArgumentException()
    {
        await SeedAsync();

        var act = () => Service.CreateUserAsync("x", "Reviewer-password-1", "x@example.invalid", "Root");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ValidateUser_LocksOutAfterRepeatedFailures()
    {
        await SeedAsync();
        await Service.CreateUserAsync("reviewer1", "Reviewer-password-1", "r@example.invalid", "Reviewer");

        for (var i = 0; i < 3; i++)
            await Service.ValidateUserAsync("reviewer1", "wrong-password");

        (await Service.ValidateUserAsync("reviewer1", "Reviewer-password-1")).Should().BeNull();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
