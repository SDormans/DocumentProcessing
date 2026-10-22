using DocumentProcessing.Application.DTOs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentProcessing.Application.Services;

/// <summary>
/// Development user service with thread-safe in-memory storage and pre-seeded development users.
/// Default users:
/// - admin / Admin123! (Role: Admin)
/// - reviewer / Reviewer123! (Role: Reviewer)
/// </summary>
public class DevelopmentUserService : IUserService
{
    private static readonly ConcurrentDictionary<string, UserAccount> _users = new(StringComparer.OrdinalIgnoreCase);

    static DevelopmentUserService()
    {
        CreateUserInternal("admin", "Admin123!", "admin@documentprocessing.local", "Admin");
        CreateUserInternal("reviewer", "Reviewer123!", "reviewer@documentprocessing.local", "Reviewer");
    }

    private static void CreateUserInternal(string username, string password, string email, string role)
    {
        _users.TryAdd(username, new UserAccount
        {
            UserId = Guid.NewGuid().ToString(),
            UserName = username,
            PasswordHash = HashPassword(password),
            Email = email,
            Role = role,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static string HashPassword(string password)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    public Task<UserInfoDto?> ValidateUserAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (_users.TryGetValue(username, out var user))
        {
            if (HashPassword(password) == user.PasswordHash)
            {
                return Task.FromResult<UserInfoDto?>(new UserInfoDto
                {
                    UserId = user.UserId,
                    UserName = user.UserName,
                    Email = user.Email,
                    Role = user.Role,
                    Roles = new List<string> { user.Role }
                });
            }
        }
        return Task.FromResult<UserInfoDto?>(null);
    }

    public Task<UserInfoDto?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = _users.Values.FirstOrDefault(u => u.UserId == userId);
        if (user == null)
        {
            return Task.FromResult<UserInfoDto?>(null);
        }

        return Task.FromResult<UserInfoDto?>(new UserInfoDto
        {
            UserId = user.UserId,
            UserName = user.UserName,
            Email = user.Email,
            Role = user.Role,
            Roles = new List<string> { user.Role }
        });
    }

    public Task<bool> UserExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_users.ContainsKey(username));
    }

    public Task<UserInfoDto> CreateUserAsync(string username, string password, string email, string role, CancellationToken cancellationToken = default)
    {
        var account = new UserAccount
        {
            UserId = Guid.NewGuid().ToString(),
            UserName = username,
            PasswordHash = HashPassword(password),
            Email = email,
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        if (!_users.TryAdd(username, account))
        {
            throw new InvalidOperationException($"User '{username}' already exists.");
        }

        return Task.FromResult(new UserInfoDto
        {
            UserId = account.UserId,
            UserName = account.UserName,
            Email = account.Email,
            Role = account.Role,
            Roles = new List<string> { role }
        });
    }

    private sealed class UserAccount
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
