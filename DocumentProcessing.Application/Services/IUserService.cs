using DocumentProcessing.Application.DTOs;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentProcessing.Application.Services;

/// <summary>
/// Service interface for user authentication and authorization queries.
/// </summary>
public interface IUserService
{
    Task<UserInfoDto?> ValidateUserAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<UserInfoDto?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> UserExistsAsync(string username, CancellationToken cancellationToken = default);
    Task<UserInfoDto> CreateUserAsync(string username, string password, string email, string role, CancellationToken cancellationToken = default);
}
