using Api.Services.Auth;
using Api.Services.IntegrationCalls;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace Api.Tests.DataAccess;

[Collection("SqlServerDatabase")]
[Trait("Category", "Database")]
public class IntegrationCallServiceDataTests
{
    private readonly SqlServerFixture _fixture;
    private readonly IntegrationCallService _sut;
    private readonly UserService _users;

    public IntegrationCallServiceDataTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
        _sut = new IntegrationCallService(fixture.ConnectionString);
        _users = new UserService(fixture.ConnectionString);
    }

    private sealed record IntegrationCallRow(
        string Direction,
        string IntegrationName,
        string? CorrelationId,
        long? UserId,
        string Method,
        string Url,
        int? StatusCode,
        int DurationMs,
        string? RequestBody,
        string? ResponseBody,
        string? Error,
        DateTime CalledAtUtc);

    private Task<IntegrationCallRow> ReadBackAsync(string url)
    {
        var conn = new SqlConnection(_fixture.ConnectionString);
        return conn.QuerySingleAsync<IntegrationCallRow>(
            """
            SELECT Direction, IntegrationName, CorrelationId, UserId, Method, Url,
                   StatusCode, DurationMs, RequestBody, ResponseBody, Error, CalledAtUtc
            FROM dbo.IntegrationCall WHERE Url = @url
            """,
            new { url });
    }

    [DatabaseFact(DisplayName = "an outbound call round-trips through the real table with a defaulted timestamp")]
    public async Task Save_OutboundCall_RoundTrips()
    {
        var url = $"https://myorg.my.salesforce.com/services/data/v66.0/query?marker={Guid.NewGuid():N}";

        await _sut.SaveAsync(new IntegrationCallRecord(
            IntegrationCallDirection.Outbound, "salesforce", "trace-abc", null,
            "GET", url, 200, 123,
            RequestBody: null,
            ResponseBody: """{"records":[]}""",
            Error: null));

        var row = await ReadBackAsync(url);
        row.Direction.Should().Be("Outbound");
        row.IntegrationName.Should().Be("salesforce");
        row.CorrelationId.Should().Be("trace-abc");
        row.UserId.Should().BeNull();
        row.Method.Should().Be("GET");
        row.StatusCode.Should().Be(200);
        row.DurationMs.Should().Be(123);
        row.RequestBody.Should().BeNull();
        row.ResponseBody.Should().Be("""{"records":[]}""");
        row.Error.Should().BeNull();
        row.CalledAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
    }

    [DatabaseFact(DisplayName = "an inbound call with a user satisfies the User foreign key")]
    public async Task Save_InboundCallWithUser_RoundTrips()
    {
        var user = await _users.UpsertExternalUserAsync(
            new ExternalUserInfo("okta", $"sub-{Guid.NewGuid():N}", "user@example.com", "Test User"));
        var url = $"/api/integrations/salesforce/auth?marker={Guid.NewGuid():N}";

        await _sut.SaveAsync(new IntegrationCallRecord(
            IntegrationCallDirection.Inbound, "salesforce", null, user.UserId,
            "GET", url, 200, 5, null, null, null));

        var row = await ReadBackAsync(url);
        row.Direction.Should().Be("Inbound");
        row.UserId.Should().Be(user.UserId);
    }

    [DatabaseFact(DisplayName = "a transport failure stores a null status code with the error")]
    public async Task Save_TransportFailure_StoresNullStatusWithError()
    {
        var url = $"https://login.salesforce.com/services/oauth2/token?marker={Guid.NewGuid():N}";

        await _sut.SaveAsync(new IntegrationCallRecord(
            IntegrationCallDirection.Outbound, "salesforce", null, null,
            "POST", url, null, 30000,
            RequestBody: "grant_type=jwt-bearer&assertion=[REDACTED]",
            ResponseBody: null,
            Error: "TaskCanceledException: The request was canceled."));

        var row = await ReadBackAsync(url);
        row.StatusCode.Should().BeNull();
        row.Error.Should().Contain("TaskCanceledException");
    }
}
