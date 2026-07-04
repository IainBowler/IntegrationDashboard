using Api.Contracts;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Api.Services;

public class PageVisitService(string connectionString) : IPageVisitService
{
    public async Task RecordVisitAsync(string pagePath)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.ExecuteAsync(
            "INSERT INTO dbo.PageVisit (PagePath) VALUES (@PagePath)",
            new { PagePath = pagePath });
    }

    public async Task<long> GetVisitCountAsync(string pagePath)
    {
        await using var conn = new SqlConnection(connectionString);
        return await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT_BIG(*) FROM dbo.PageVisit WHERE PagePath = @PagePath",
            new { PagePath = pagePath });
    }

    public async Task<IReadOnlyList<PageVisitSummaryItem>> GetSummaryAsync()
    {
        await using var conn = new SqlConnection(connectionString);
        var rows = await conn.QueryAsync<PageVisitSummaryItem>(
            """
            SELECT PagePath, COUNT_BIG(*) AS Count
            FROM dbo.PageVisit
            GROUP BY PagePath
            ORDER BY COUNT_BIG(*) DESC
            """);
        return rows.ToList();
    }
}
