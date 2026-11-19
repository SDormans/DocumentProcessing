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

    Task<bool> IsTwoFactorEnabledAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Checks an authenticator-app (TOTP) code.</summary>
    Task<bool> VerifyTwoFactorCodeAsync(string userId, string code, CancellationToken cancellationToken = default);

    /// <summary>Creates a new authenticator key. Two-factor sign-in stays off until <see cref="EnableTwoFactorAsync"/> succeeds.</summary>
    Task<AuthenticatorSetup> ResetAuthenticatorAsync(string userId, string issuer, CancellationToken cancellationToken = default);

    /// <summary>Turns on two-factor sign-in when <paramref name="code"/> matches the current key.</summary>
    Task<bool> EnableTwoFactorAsync(string userId, string code, CancellationToken cancellationToken = default);

    /// <summary>Replaces the user's one-time recovery codes; only the new codes work afterwards.</summary>
    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, int count, CancellationToken cancellationToken = default);

    /// <summary>Signs in with a recovery code instead of an authenticator code; each code works once.</summary>
    Task<bool> RedeemRecoveryCodeAsync(string userId, string code, CancellationToken cancellationToken = default);

    /// <summary>Turns two-factor sign-in off and discards the authenticator key and recovery codes. False if the user does not exist.</summary>
    Task<bool> ResetTwoFactorAsync(string userId, CancellationToken cancellationToken = default);
}

/// <param name="SharedKey">Base32 key to type into an authenticator app.</param>
/// <param name="AuthenticatorUri"><c>otpauth://</c> URI to render as a QR code.</param>
public sealed record AuthenticatorSetup(string SharedKey, string AuthenticatorUri);
