using DocumentProcessing.Application.DTOs;

namespace DocumentProcessing.Application.Services;

/// <summary>
/// Gebruikersdienst voor authenticatie en autorisatie.
/// In development wordt een in-memory winkel gebruikt met seeded gebruikers.
/// </summary>
public interface IUserService
{
    Task<UserInfoDto?> ValidateUserAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<UserInfoDto?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> UserExistsAsync(string username, CancellationToken cancellationToken = default);
    Task<UserInfoDto> CreateUserAsync(string username, string password, string email, string role, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ontwikkelingsgebruikersdienst met in-memory opslag en seeded gebruikers.
/// Gebruikers:
/// - admin / Admin123! (rol: Admin)
/// - reviewer / Reviewer123! (rol: Reviewer)
/// </summary>
public class DevelopmentUserService : IUserService
{
    private static readonly Dictionary<string, UserAccount> _users = new(StringComparer.OrdinalIgnoreCase);

    static DevelopmentUserService()
    {
        // Seed development users
        CreateUserInternal("admin", "Admin123!", "admin@documentprocessing.local", "Admin");
        CreateUserInternal("reviewer", "Reviewer123!", "reviewer@documentprocessing.local", "Reviewer");
    }

    private static void CreateUserInternal(string username, string password, string email, string role)
    {
        if (_users.ContainsKey(username)) return;
        _users[username] = new UserAccount
        {
            UserId = Guid.NewGuid().ToString(),
            UserName = username,
            PasswordHash = HashPassword(password),
            Email = email,
            Role = role,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string HashPassword(string password)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(password);
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
        if (user == null) return Task.FromResult<UserInfoDto?>(null);
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
        if (_users.ContainsKey(username))
            throw new InvalidOperationException("Gebruiker bestaat al.");

        var userId = Guid.NewGuid().ToString();
        _users[username] = new UserAccount
        {
            UserId = userId,
            UserName = username,
            PasswordHash = HashPassword(password),
            Email = email,
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        return Task.FromResult(new UserInfoDto
        {
            UserId = userId,
            UserName = username,
            Email = email,
            Role = role,
            Roles = new List<string> { role }
        });
    }

    private class UserAccount
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
