using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Infrastructure.Identity;

/// <summary>
/// Ensures application roles exist and creates the optional bootstrap administrator
/// from <c>Authentication:Bootstrap:*</c> configuration when no such user exists yet.
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        foreach (var appRole in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(appRole))
                await EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(appRole)), $"create role {appRole}");
        }

        var section = configuration.GetSection("Authentication:Bootstrap");
        var username = section["Username"];
        var password = section["Password"];
        var email = section["Email"];
        var role = string.IsNullOrWhiteSpace(section["Role"]) ? AppRoles.Admin : section["Role"]!;

        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
            return;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Bootstrap authentication requires both a username and a password.");

        if (!AppRoles.All.Contains(role))
            throw new InvalidOperationException($"Bootstrap role '{role}' is not supported.");

        var existing = await userManager.FindByNameAsync(username);
        if (existing is not null)
        {
            // Repairs a previous startup that was interrupted between user creation and role assignment.
            if (!await userManager.IsInRoleAsync(existing, role))
                await EnsureSucceeded(await userManager.AddToRoleAsync(existing, role), "assign bootstrap role");
            return;
        }

        var user = new ApplicationUser
        {
            UserName = username,
            Email = string.IsNullOrWhiteSpace(email) ? $"{username}@local.invalid" : email
        };
        await EnsureSucceeded(await userManager.CreateAsync(user, password), "create bootstrap user");
        await EnsureSucceeded(await userManager.AddToRoleAsync(user, role), "assign bootstrap role");
        logger.LogInformation("Created bootstrap user {UserId} with role {Role}.", user.Id, role);
    }

    private static Task EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to {operation}: {string.Join(" ", result.Errors.Select(e => e.Description))}");
        return Task.CompletedTask;
    }
}
