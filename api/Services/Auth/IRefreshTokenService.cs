namespace Api.Services.Auth;

public interface IRefreshTokenService
{
    /// <summary>Issues a new refresh token for the user and returns the raw
    /// value (only the SHA-256 hash is stored).</summary>
    Task<string> IssueAsync(long userId);

    /// <summary>Rotates a valid token: revokes it, issues a replacement, and
    /// returns the owning user. Returns null for unknown/expired tokens.
    /// Presenting an already-revoked token is treated as theft and revokes
    /// every live token for that user.</summary>
    Task<(UserRecord User, string NewRefreshToken)?> ValidateAndRotateAsync(string refreshToken);

    /// <summary>Revokes the token if it is live; no-op otherwise.</summary>
    Task RevokeAsync(string refreshToken);
}
