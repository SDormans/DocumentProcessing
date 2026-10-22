using DocumentProcessing.Application.DTOs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentProcessing.Application.Services;

/// <summary>
/// In-memory user store bootstrapped from explicitly supplied configuration.
/// </summary>
public class DevelopmentUserService : IUserService
{
    private const int PasswordIterations = 210_000;
    private static readonly byte[] DummySalt = new byte[16];
    private static readonly byte[] DummyHash = new byte[32];
    private readonly ConcurrentDictionary<string, UserAccount> _users = new(StringComparer.OrdinalIgnoreCase);

    public DevelopmentUserService(
        string? username = null,
        string? password = null,
        string? email = null,
        string? role = null)
    {
        var hasBootstrapUsername = !string.IsNullOrWhiteSpace(username);
        var hasBootstrapPassword = !string.IsNullOrWhiteSpace(password);
        if (hasBootstrapUsername != hasBootstrapPassword)
        {
            throw new InvalidOperationException("Both bootstrap username and password must be configured.");
        }

        if (hasBootstrapUsername)
        {
            CreateUserInternal(username!, password!, email ?? string.Empty, role ?? "Admin");
        }
    }

    private static (byte[] Salt, byte[] Hash) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            PasswordIterations,
            HashAlgorithmName.SHA256,
            32);
        return (salt, hash);
    }

    public Task<UserInfoDto?> ValidateUserAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_users.TryGetValue(username, out var user))
        {
            _ = Rfc2898DeriveBytes.Pbkdf2(password, DummySalt, PasswordIterations, HashAlgorithmName.SHA256, DummyHash.Length);
            return Task.FromResult<UserInfoDto?>(null);
        }

        var attemptedHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            user.PasswordSalt,
            PasswordIterations,
            HashAlgorithmName.SHA256,
            user.PasswordHash.Length);
        if (!CryptographicOperations.FixedTimeEquals(attemptedHash, user.PasswordHash))
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

    public Task<UserInfoDto?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_users.ContainsKey(username));
    }

    public Task<UserInfoDto> CreateUserAsync(string username, string password, string email, string role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateUserInternal(username, password, email, role));
    }

    private UserInfoDto CreateUserInternal(string username, string password, string email, string role)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || password.Length < 12)
        {
            throw new ArgumentException("Username and email are required; passwords must be at least 12 characters.");
        }

        if (role is not ("Admin" or "Reviewer"))
        {
            throw new ArgumentException("The requested role is not supported.", nameof(role));
        }

        var (salt, hash) = HashPassword(password);
        var account = new UserAccount
        {
            UserId = Guid.NewGuid().ToString(),
            UserName = username,
            PasswordSalt = salt,
            PasswordHash = hash,
            Email = email,
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        if (!_users.TryAdd(username, account))
        {
            throw new InvalidOperationException($"User '{username}' already exists.");
        }

        return new UserInfoDto
        {
            UserId = account.UserId,
            UserName = account.UserName,
            Email = account.Email,
            Role = account.Role,
            Roles = new List<string> { role }
        };
    }

    private sealed class UserAccount
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
        public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
