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
                (Direction, IntegrationName, CorrelationId, UserId, Method, Url,
                 StatusCode, DurationMs, RequestBody, ResponseBody, Error)
            VALUES
                (@Direction, @IntegrationName, @CorrelationId, @UserId, @Method, @Url,
                 @StatusCode, @DurationMs, @RequestBody, @ResponseBody, @Error);
            """,
            new
            {
                Direction = call.Direction.ToString(),
                call.IntegrationName,
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
