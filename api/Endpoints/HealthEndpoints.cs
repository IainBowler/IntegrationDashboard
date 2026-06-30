using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/health").WithTags("Health");
        // intentionally NO .RequireAuthorization() — health is public

        group.MapGet("/", Handle).WithName("GetHealth");

        return routes;
    }

    private static Ok Handle() => TypedResults.Ok();
}
