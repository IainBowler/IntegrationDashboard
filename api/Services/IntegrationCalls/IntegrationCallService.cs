using Dapper;
using Microsoft.Data.SqlClient;

namespace Api.Services.IntegrationCalls;

public class IntegrationCallService(string connectionString) : IIntegrationCallService
{
    public async Task SaveAsync(IntegrationCallRecord call)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.ExecuteAsync(
            """
            INSERT INTO dbo.IntegrationCall
                (Direction, IntegrationName, IntegrationEndpointId, CorrelationId, UserId, Method, Url,
                 StatusCode, DurationMs, RequestBody, ResponseBody, Error)
            SELECT @Direction, @IntegrationName,
                   (SELECT e.IntegrationEndpointId
                      FROM dbo.IntegrationEndpoint e
                      JOIN dbo.Integration i ON i.IntegrationId = e.IntegrationId
                     WHERE i.Name = @IntegrationName AND e.Name = @EndpointName),
                   @CorrelationId, @UserId, @Method, @Url,
                   @StatusCode, @DurationMs, @RequestBody, @ResponseBody, @Error;
            """,
            new
            {
                Direction = call.Direction.ToString(),
                call.IntegrationName,
                call.EndpointName,
                call.CorrelationId,
                call.UserId,
                call.Method,
                call.Url,
                call.StatusCode,
                call.DurationMs,
                call.RequestBody,
                call.ResponseBody,
                call.Error,
            });
    }
}
