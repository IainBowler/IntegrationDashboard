using Api.Services.Integrations;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace Api.Tests.DataAccess;

[Collection("SqlServerDatabase")]
[Trait("Category", "Database")]
public class IntegrationDirectoryServiceDataTests
{
    private readonly SqlServerFixture _fixture;
    private readonly IntegrationDirectoryService _sut;

    public IntegrationDirectoryServiceDataTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
        _sut = new IntegrationDirectoryService(fixture.ConnectionString);
    }

    [DatabaseFact(DisplayName = "the post-deployment script seeded salesforce and its four endpoints")]
    public async Task PostDeployment_SeededSalesforceMetadata()
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString);

        var endpoints = (await conn.QueryAsync<(string Name, string Direction)>(
            """
            SELECT e.Name, e.Direction
            FROM dbo.IntegrationEndpoint e
            JOIN dbo.Integration i ON i.IntegrationId = e.IntegrationId
            WHERE i.Name = N'salesforce'
            """)).ToList();

        endpoints.Should().BeEquivalentTo(
        [
            ("auth", "Inbound"),
            ("accounts", "Inbound"),
            ("token", "Outbound"),
            ("query", "Outbound"),
        ]);
    }

    [DatabaseFact(DisplayName = "the integration list contains the seeded salesforce row")]
    public async Task GetIntegrations_ContainsSalesforce()
    {
        var integrations = await _sut.GetIntegrationsAsync();

        integrations.Should().Contain(i => i.Name == "salesforce" && i.DisplayName == "Salesforce");
    }

    [DatabaseFact(DisplayName = "statistics for an unknown integration are null")]
    public async Task GetStatistics_UnknownIntegration_ReturnsNull()
    {
        (await _sut.GetStatisticsAsync($"unknown-{Guid.NewGuid():N}")).Should().BeNull();
    }

    [DatabaseFact(DisplayName = "statistics aggregate calls per endpoint and keep zero-call endpoints visible")]
    public async Task GetStatistics_AggregatesPerEndpoint()
    {
        // A guid-named test-only integration isolates the aggregation assertions
        // from the salesforce rows other tests in the shared collection insert.
        var integrationName = $"test-{Guid.NewGuid():N}"[..20];
        await using var conn = new SqlConnection(_fixture.ConnectionString);
        var busyEndpointId = await SeedIntegrationAsync(conn, integrationName);

        var baseTime = new DateTime(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc);
        await InsertCallAsync(conn, integrationName, busyEndpointId, 200, 120, baseTime);
        await InsertCallAsync(conn, integrationName, busyEndpointId, 500, 300, baseTime.AddMinutes(1));
        await InsertCallAsync(conn, integrationName, busyEndpointId, null, 30000, baseTime.AddMinutes(2));

        var stats = await _sut.GetStatisticsAsync(integrationName);

        stats!.Name.Should().Be(integrationName);
        stats.DisplayName.Should().Be("Test Integration");
        stats.Endpoints.Should().HaveCount(2);

        var busy = stats.Endpoints.Single(e => e.EndpointName == "busy");
        busy.Direction.Should().Be("Outbound");
        busy.TotalCalls.Should().Be(3);
        busy.SuccessCount.Should().Be(1);
        busy.AvgDurationMs.Should().BeApproximately((120 + 300 + 30000) / 3.0, 0.01);
        busy.MaxDurationMs.Should().Be(30000);
        busy.LastCalledAtUtc.Should().Be(baseTime.AddMinutes(2));
        // the latest call was a transport failure, so there is no "last status"
        busy.LastStatusCode.Should().BeNull();

        var idle = stats.Endpoints.Single(e => e.EndpointName == "idle");
        idle.TotalCalls.Should().Be(0);
        idle.SuccessCount.Should().Be(0);
        idle.AvgDurationMs.Should().BeNull();
        idle.MaxDurationMs.Should().BeNull();
        idle.LastCalledAtUtc.Should().BeNull();
        idle.LastStatusCode.Should().BeNull();
    }

    private static async Task<long> SeedIntegrationAsync(SqlConnection conn, string integrationName)
    {
        return await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO dbo.Integration (Name, DisplayName) VALUES (@integrationName, N'Test Integration');
            DECLARE @integrationId BIGINT = SCOPE_IDENTITY();
            INSERT INTO dbo.IntegrationEndpoint (IntegrationId, Name, Direction)
            VALUES (@integrationId, N'idle', N'Inbound');
            INSERT INTO dbo.IntegrationEndpoint (IntegrationId, Name, Direction)
            VALUES (@integrationId, N'busy', N'Outbound');
            SELECT SCOPE_IDENTITY();
            """,
            new { integrationName });
    }

    private static Task InsertCallAsync(
        SqlConnection conn, string integrationName, long endpointId,
        int? statusCode, int durationMs, DateTime calledAtUtc)
    {
        return conn.ExecuteAsync(
            """
            INSERT INTO dbo.IntegrationCall
                (Direction, IntegrationName, IntegrationEndpointId, Method, Url,
                 StatusCode, DurationMs, CalledAtUtc)
            VALUES
                (N'Outbound', @integrationName, @endpointId, N'GET', N'https://example.test/busy',
                 @statusCode, @durationMs, @calledAtUtc);
            """,
            new { integrationName, endpointId, statusCode, durationMs, calledAtUtc });
    }
}
