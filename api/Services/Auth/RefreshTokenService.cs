using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;

namespace Api.Services.Auth;

public class RefreshTokenService(string connectionString, int refreshTokenDays) : IRefreshTokenService
{
    public async Task<string> IssueAsync(long userId)
    {
        var raw = GenerateToken();
        await using var conn = new SqlConnection(connectionString);
        // Opportunistic cleanup keeps the table from accumulating dead rows
        // without needing a scheduled job.
        await conn.ExecuteAsync(
            """
            DELETE FROM dbo.RefreshToken
            WHERE UserId = @UserId AND ExpiresAtUtc < SYSUTCDATETIME();

            INSERT INTO dbo.RefreshToken (UserId, TokenHash, ExpiresAtUtc)
            VALUES (@UserId, @TokenHash, DATEADD(day, @Days, SYSUTCDATETIME()));
            """,
            new { UserId = userId, TokenHash = TokenHasher.Sha256Hex(raw), Days = refreshTokenDays });
        return raw;
    }

    public async Task<(UserRecord User, string NewRefreshToken)?> ValidateAndRotateAsync(string refreshToken)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        var row = await conn.QuerySingleOrDefaultAsync<RefreshTokenRow>(
            """
            SELECT rt.RefreshTokenId, rt.UserId, rt.ExpiresAtUtc, rt.RevokedAtUtc,
                   u.Provider, u.ExternalSubjectId, u.Email, u.DisplayName
            FROM dbo.RefreshToken rt
            JOIN dbo.[User] u ON u.UserId = rt.UserId
            WHERE rt.TokenHash = @TokenHash
            """,
            new { TokenHash = TokenHasher.Sha256Hex(refreshToken) });
        if (row is null)
        {
            return null;
        }

        if (row.RevokedAtUtc is not null)
        {
            // A revoked token being replayed means it leaked (or the rotation
            // response was lost): revoke the user's whole token family.
            await conn.ExecuteAsync(
                """
                UPDATE dbo.RefreshToken
                SET RevokedAtUtc = SYSUTCDATETIME()
                WHERE UserId = @UserId AND RevokedAtUtc IS NULL
                """,
                new { row.UserId });
            return null;
        }

        if (row.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return null;
        }

        var newRaw = GenerateToken();
        await conn.ExecuteAsync(
            """
            INSERT INTO dbo.RefreshToken (UserId, TokenHash, ExpiresAtUtc)
            VALUES (@UserId, @NewHash, DATEADD(day, @Days, SYSUTCDATETIME()));

            UPDATE dbo.RefreshToken
            SET RevokedAtUtc = SYSUTCDATETIME(), ReplacedByTokenHash = @NewHash
            WHERE RefreshTokenId = @RefreshTokenId;
            """,
            new
            {
                row.UserId,
                NewHash = TokenHasher.Sha256Hex(newRaw),
                Days = refreshTokenDays,
                row.RefreshTokenId,
            });

        var user = new UserRecord(row.UserId, row.Provider, row.ExternalSubjectId, row.Email, row.DisplayName);
        return (user, newRaw);
    }

    public async Task RevokeAsync(string refreshToken)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.ExecuteAsync(
            """
            UPDATE dbo.RefreshToken
            SET RevokedAtUtc = SYSUTCDATETIME()
            WHERE TokenHash = @TokenHash AND RevokedAtUtc IS NULL
            """,
            new { TokenHash = TokenHasher.Sha256Hex(refreshToken) });
    }

    private static string GenerateToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private sealed record RefreshTokenRow(
        long RefreshTokenId,
        long UserId,
        DateTime ExpiresAtUtc,
        DateTime? RevokedAtUtc,
        string Provider,
        string ExternalSubjectId,
        string? Email,
        string? DisplayName);
}
