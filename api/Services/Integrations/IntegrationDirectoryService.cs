using Api.Contracts;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Api.Services.Integrations;

public class IntegrationDirectoryService(string connectionString) : IIntegrationDirectoryService
{
    public async Task<IReadOnlyList<IntegrationResponse>> GetIntegrationsAsync()
    {
        await using var conn = new SqlConnection(connectionString);
        var integrations = await conn.QueryAsync<IntegrationResponse>(
            "SELECT Name, DisplayName FROM dbo.Integration ORDER BY DisplayName");
        return integrations.ToList();
    }

    public async Task<IntegrationStatisticsResponse?> GetStatisticsAsync(string integrationName)
    {
        await using var conn = new SqlConnection(connectionString);
        var integration = await conn.QuerySingleOrDefaultAsync<(long IntegrationId, string Name, string DisplayName)>(
            "SELECT IntegrationId, Name, DisplayName FROM dbo.Integration WHERE Name = @integrationName",
            new { integrationName });
        if (integration == default)
        {
            return null;
        }

        var endpoints = await conn.QueryAsync<EndpointStatisticsResponse>(
            """
            WITH calls AS (
                SELECT c.IntegrationEndpointId, c.StatusCode, c.DurationMs, c.CalledAtUtc,
                       ROW_NUMBER() OVER (PARTITION BY c.IntegrationEndpointId
                                          ORDER BY c.CalledAtUtc DESC, c.IntegrationCallId DESC) AS rn
                FROM dbo.IntegrationCall c
                WHERE c.IntegrationEndpointId IS NOT NULL
            )
            SELECT e.Name                                        AS EndpointName,
                   e.Direction,
                   COUNT(c.IntegrationEndpointId)                AS TotalCalls,
                   ISNULL(SUM(CASE WHEN c.StatusCode BETWEEN 200 AND 299 THEN 1 ELSE 0 END), 0) AS SuccessCount,
                   AVG(CAST(c.DurationMs AS FLOAT))              AS AvgDurationMs,
                   MAX(c.DurationMs)                             AS MaxDurationMs,
                   MAX(c.CalledAtUtc)                            AS LastCalledAtUtc,
                   MAX(CASE WHEN c.rn = 1 THEN c.StatusCode END) AS LastStatusCode
            FROM dbo.IntegrationEndpoint e
            LEFT JOIN calls c ON c.IntegrationEndpointId = e.IntegrationEndpointId
            WHERE e.IntegrationId = @IntegrationId
            GROUP BY e.IntegrationEndpointId, e.Name, e.Direction
            ORDER BY e.Direction, e.Name;
            """,
            new { integration.IntegrationId });

        return new IntegrationStatisticsResponse(
            integration.Name, integration.DisplayName, endpoints.ToList());
    }
}
