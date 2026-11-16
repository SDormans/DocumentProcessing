using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Services;
using Microsoft.AspNetCore.Identity;

namespace DocumentProcessing.Infrastructure.Identity;

/// <summary>
/// <see cref="IUserService"/> backed by ASP.NET Core Identity and the application database.
/// </summary>
public class IdentityUserService : IUserService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityUserService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<UserInfoDto?> ValidateUserAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
            return null;

        if (_userManager.SupportsUserLockout && await _userManager.IsLockedOutAsync(user))
            return null;

        if (!await _userManager.CheckPasswordAsync(user, password))
        {
            if (_userManager.SupportsUserLockout)
                await _userManager.AccessFailedAsync(user);
            return null;
        }

        if (_userManager.SupportsUserLockout)
            await _userManager.ResetAccessFailedCountAsync(user);

        return await ToDtoAsync(user);
    }

    public async Task<UserInfoDto?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        return user is null ? null : await ToDtoAsync(user);
    }

    public async Task<bool> UserExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        return await _userManager.FindByNameAsync(username) is not null;
    }

    public async Task<UserInfoDto> CreateUserAsync(string username, string password, string email, string role, CancellationToken cancellationToken = default)
    {
        if (!AppRoles.All.Contains(role))
            throw new ArgumentException($"Unsupported role '{role}'.", nameof(role));

        if (await UserExistsAsync(username, cancellationToken))
            throw new InvalidOperationException("The user already exists.");

        var user = new ApplicationUser { UserName = username, Email = email };
        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new ArgumentException(string.Join(" ", result.Errors.Select(e => e.Description)), nameof(password));

        var roleResult = await _userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            throw new InvalidOperationException(string.Join(" ", roleResult.Errors.Select(e => e.Description)));
        }

        return await ToDtoAsync(user);
    }

    private async Task<UserInfoDto> ToDtoAsync(ApplicationUser user)
    {
        var roles = (await _userManager.GetRolesAsync(user)).OrderBy(r => r == AppRoles.Admin ? 0 : 1).ToList();
        return new UserInfoDto
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Role = roles.FirstOrDefault() ?? string.Empty,
            Roles = roles
        };
    }
}
