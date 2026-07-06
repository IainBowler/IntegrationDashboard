using Api.Services.Auth;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace Api.Tests.DataAccess;

[Collection("SqlServerDatabase")]
[Trait("Category", "Database")]
public class RefreshTokenServiceDataTests
{
    private readonly SqlServerFixture _fixture;
    private readonly RefreshTokenService _sut;
    private readonly UserService _users;

    public RefreshTokenServiceDataTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
        _sut = new RefreshTokenService(fixture.ConnectionString, refreshTokenDays: 30);
        _users = new UserService(fixture.ConnectionString);
    }

    private Task<UserRecord> CreateUserAsync() =>
        _users.UpsertExternalUserAsync(
            new ExternalUserInfo("okta", $"sub-{Guid.NewGuid():N}", "user@example.com", "Test User"));

    [DatabaseFact]
    public async Task Issue_StoresOnlyTheTokenHash()
    {
        var user = await CreateUserAsync();

        var raw = await _sut.IssueAsync(user.UserId);

        await using var conn = new SqlConnection(_fixture.ConnectionString);
        var stored = await conn.QuerySingleAsync<(string TokenHash, DateTime ExpiresAtUtc, DateTime? RevokedAtUtc)>(
            "SELECT TokenHash, ExpiresAtUtc, RevokedAtUtc FROM dbo.RefreshToken WHERE UserId = @UserId",
            new { user.UserId });

        stored.TokenHash.Should().Be(TokenHasher.Sha256Hex(raw));
        stored.TokenHash.Should().NotContain(raw);
        stored.RevokedAtUtc.Should().BeNull();
        stored.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
    }

    [DatabaseFact]
    public async Task ValidateAndRotate_ValidToken_ReturnsUserAndRotates()
    {
        var user = await CreateUserAsync();
        var original = await _sut.IssueAsync(user.UserId);

        var rotated = await _sut.ValidateAndRotateAsync(original);

        rotated.Should().NotBeNull();
        rotated!.Value.User.Should().Be(user);
        rotated.Value.NewRefreshToken.Should().NotBe(original);

        await using var conn = new SqlConnection(_fixture.ConnectionString);
        var oldRow = await conn.QuerySingleAsync<(DateTime? RevokedAtUtc, string? ReplacedByTokenHash)>(
            "SELECT RevokedAtUtc, ReplacedByTokenHash FROM dbo.RefreshToken WHERE TokenHash = @Hash",
            new { Hash = TokenHasher.Sha256Hex(original) });
        oldRow.RevokedAtUtc.Should().NotBeNull();
        oldRow.ReplacedByTokenHash.Should().Be(TokenHasher.Sha256Hex(rotated.Value.NewRefreshToken));
    }

    [DatabaseFact]
    public async Task ValidateAndRotate_UnknownToken_ReturnsNull()
    {
        var result = await _sut.ValidateAndRotateAsync("never-issued-token");

        result.Should().BeNull();
    }

    [DatabaseFact]
    public async Task ValidateAndRotate_ExpiredToken_ReturnsNull()
    {
        var user = await CreateUserAsync();
        var expiredIssuer = new RefreshTokenService(_fixture.ConnectionString, refreshTokenDays: -1);
        var expired = await expiredIssuer.IssueAsync(user.UserId);

        var result = await _sut.ValidateAndRotateAsync(expired);

        result.Should().BeNull();
    }

    [DatabaseFact]
    public async Task ValidateAndRotate_ReplayedToken_RevokesWholeFamily()
    {
        var user = await CreateUserAsync();
        var original = await _sut.IssueAsync(user.UserId);
        var rotated = await _sut.ValidateAndRotateAsync(original);

        // replaying the already-rotated token signals theft
        var replay = await _sut.ValidateAndRotateAsync(original);

        replay.Should().BeNull();
        // ...and takes the legitimate successor down with it
        var successor = await _sut.ValidateAndRotateAsync(rotated!.Value.NewRefreshToken);
        successor.Should().BeNull();
    }

    [DatabaseFact]
    public async Task Revoke_ValidToken_PreventsRotation()
    {
        var user = await CreateUserAsync();
        var raw = await _sut.IssueAsync(user.UserId);

        await _sut.RevokeAsync(raw);

        (await _sut.ValidateAndRotateAsync(raw)).Should().BeNull();
    }

    [DatabaseFact]
    public async Task Revoke_IsIdempotent()
    {
        var user = await CreateUserAsync();
        var raw = await _sut.IssueAsync(user.UserId);

        await _sut.RevokeAsync(raw);
        var act = async () => await _sut.RevokeAsync(raw);

        await act.Should().NotThrowAsync();
    }

    [DatabaseFact]
    public async Task Revoke_UnknownToken_DoesNotThrow()
    {
        var act = async () => await _sut.RevokeAsync("never-issued-token");

        await act.Should().NotThrowAsync();
    }

    [DatabaseFact]
    public async Task Issue_CleansUpExpiredTokensForTheUser()
    {
        var user = await CreateUserAsync();
        var expiredIssuer = new RefreshTokenService(_fixture.ConnectionString, refreshTokenDays: -1);
        await expiredIssuer.IssueAsync(user.UserId);
        await expiredIssuer.IssueAsync(user.UserId);

        await _sut.IssueAsync(user.UserId);

        await using var conn = new SqlConnection(_fixture.ConnectionString);
        var expiredCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.RefreshToken WHERE UserId = @UserId AND ExpiresAtUtc < SYSUTCDATETIME()",
            new { user.UserId });
        expiredCount.Should().Be(0);
    }
}
