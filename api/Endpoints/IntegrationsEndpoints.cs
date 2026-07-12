using Api.Contracts;
using Api.Services.Integrations;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.Endpoints;

public static class IntegrationsEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationsEndpoints(this IEndpointRouteBuilder routes)
    {
        // Meta/read-only surface about integrations — deliberately NOT audited:
        // no IntegrationCallRecordingFilter here, because these calls aren't
        // integration traffic and would pollute the statistics they report.
        var group = routes.MapGroup("/api/integrations")
            .WithTags("Integrations")
            .RequireAuthorization();

        group.MapGet("/", GetIntegrations).WithName("GetIntegrations");
        group.MapGet("/{name}/statistics", GetStatistics).WithName("GetIntegrationStatistics");

        return routes;
    }

    private static async Task<Ok<IReadOnlyList<IntegrationResponse>>> GetIntegrations(
        IIntegrationDirectoryService service)
    {
        return TypedResults.Ok(await service.GetIntegrationsAsync());
    }

    private static async Task<Results<Ok<IntegrationStatisticsResponse>, NotFound>> GetStatistics(
        string name,
        IIntegrationDirectoryService service)
    {
        return await service.GetStatisticsAsync(name) is { } statistics
            ? TypedResults.Ok(statistics)
            : TypedResults.NotFound();
    }
}
