using Dapper;
using Microsoft.Data.SqlClient;

namespace Api.Services.Auth;

public class UserService(string connectionString) : IUserService
{
    public async Task<UserRecord> UpsertExternalUserAsync(ExternalUserInfo info)
    {
        // UPDATE-then-INSERT keyed on the unique (Provider, ExternalSubjectId)
        // constraint; concurrent first logins are backstopped by the constraint.
        const string sql = """
            UPDATE dbo.[User]
            SET Email = @Email, DisplayName = @DisplayName, LastLoginAtUtc = SYSUTCDATETIME()
            WHERE Provider = @Provider AND ExternalSubjectId = @SubjectId;

            IF @@ROWCOUNT = 0
                INSERT INTO dbo.[User] (Provider, ExternalSubjectId, Email, DisplayName)
                VALUES (@Provider, @SubjectId, @Email, @DisplayName);

            SELECT UserId, Provider, ExternalSubjectId, Email, DisplayName
            FROM dbo.[User]
            WHERE Provider = @Provider AND ExternalSubjectId = @SubjectId;
            """;

        await using var conn = new SqlConnection(connectionString);
        return await conn.QuerySingleAsync<UserRecord>(
            sql, new { info.Provider, info.SubjectId, info.Email, info.DisplayName });
    }

    public async Task<UserRecord?> GetByIdAsync(long userId)
    {
        await using var conn = new SqlConnection(connectionString);
        return await conn.QuerySingleOrDefaultAsync<UserRecord>(
            """
            SELECT UserId, Provider, ExternalSubjectId, Email, DisplayName
            FROM dbo.[User]
            WHERE UserId = @UserId
            """,
            new { UserId = userId });
    }
}
