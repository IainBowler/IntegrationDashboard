using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/health").WithTags("Health");
        // intentionally NO .RequireAuthorization() — health is public

        group.MapGet("/", Handle).WithName("GetHealth");

        // App Service "Always On" pings the site root every 5 minutes and its
        // path is not configurable; without this route every ping logs a 404.
        routes.MapGet("/", Handle).WithName("GetRoot").WithTags("Health");

        return routes;
    }

    private static Ok Handle() => TypedResults.Ok();
}
