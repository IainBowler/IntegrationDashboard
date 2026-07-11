using Api.Services.Auth;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace Api.Tests.DataAccess;

[Collection("SqlServerDatabase")]
[Trait("Category", "Database")]
public class UserServiceDataTests
{
    private readonly SqlServerFixture _fixture;
    private readonly UserService _sut;

    public UserServiceDataTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
        _sut = new UserService(fixture.ConnectionString);
    }

    private static ExternalUserInfo NewExternalUser(
        string? email = "user@example.com", string? name = "Test User") =>
        new("okta", $"sub-{Guid.NewGuid():N}", email, name);

    [DatabaseFact(DisplayName = "first login inserts the user and returns the record")]
    public async Task Upsert_NewUser_InsertsAndReturnsRecord()
    {
        var info = NewExternalUser();

        var user = await _sut.UpsertExternalUserAsync(info);

        user.UserId.Should().BePositive();
        user.Provider.Should().Be("okta");
        user.ExternalSubjectId.Should().Be(info.SubjectId);
        user.Email.Should().Be("user@example.com");
        user.DisplayName.Should().Be("Test User");
    }

    [DatabaseFact(DisplayName = "a second login updates the profile without duplicating the row")]
    public async Task Upsert_SecondLogin_UpdatesProfileWithoutDuplicating()
    {
        var info = NewExternalUser(email: "old@example.com", name: "Old Name");
        var first = await _sut.UpsertExternalUserAsync(info);

        var second = await _sut.UpsertExternalUserAsync(
            info with { Email = "new@example.com", DisplayName = "New Name" });

        second.UserId.Should().Be(first.UserId);
        second.Email.Should().Be("new@example.com");
        second.DisplayName.Should().Be("New Name");

        await using var conn = new SqlConnection(_fixture.ConnectionString);
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.[User] WHERE Provider = @Provider AND ExternalSubjectId = @SubjectId",
            new { info.Provider, info.SubjectId });
        count.Should().Be(1);
    }

    [DatabaseFact(DisplayName = "a second login advances the last-login timestamp")]
    public async Task Upsert_SecondLogin_AdvancesLastLoginTimestamp()
    {
        var info = NewExternalUser();
        await _sut.UpsertExternalUserAsync(info);

        await Task.Delay(50);
        var updated = await _sut.UpsertExternalUserAsync(info);

        await using var conn = new SqlConnection(_fixture.ConnectionString);
        var row = await conn.QuerySingleAsync<(DateTime CreatedAtUtc, DateTime LastLoginAtUtc)>(
            "SELECT CreatedAtUtc, LastLoginAtUtc FROM dbo.[User] WHERE UserId = @UserId",
            new { updated.UserId });
        row.LastLoginAtUtc.Should().BeAfter(row.CreatedAtUtc);
    }

    [DatabaseFact(DisplayName = "the same subject under different providers creates separate users")]
    public async Task Upsert_SameSubjectDifferentProvider_CreatesSeparateUsers()
    {
        var subject = $"sub-{Guid.NewGuid():N}";

        var oktaUser = await _sut.UpsertExternalUserAsync(
            new ExternalUserInfo("okta", subject, "a@example.com", "A"));
        var googleUser = await _sut.UpsertExternalUserAsync(
            new ExternalUserInfo("google", subject, "a@example.com", "A"));

        googleUser.UserId.Should().NotBe(oktaUser.UserId);
    }

    [DatabaseFact(DisplayName = "a user round-trips by id")]
    public async Task GetById_RoundTripsUpsertedUser()
    {
        var created = await _sut.UpsertExternalUserAsync(NewExternalUser());

        var fetched = await _sut.GetByIdAsync(created.UserId);

        fetched.Should().Be(created);
    }

    [DatabaseFact(DisplayName = "an unknown user id returns null")]
    public async Task GetById_UnknownUser_ReturnsNull()
    {
        var fetched = await _sut.GetByIdAsync(long.MaxValue);

        fetched.Should().BeNull();
    }

    [DatabaseFact(DisplayName = "null email and display name persist as null")]
    public async Task Upsert_NullEmailAndName_ArePersistedAsNull()
    {
        var user = await _sut.UpsertExternalUserAsync(NewExternalUser(email: null, name: null));

        var fetched = await _sut.GetByIdAsync(user.UserId);
        fetched!.Email.Should().BeNull();
        fetched.DisplayName.Should().BeNull();
    }
}
