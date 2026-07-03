using Api.Contracts;
using Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.Endpoints;

public static class PageVisitEndpoints
{
    public static IEndpointRouteBuilder MapPageVisitEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/page-visits").WithTags("PageVisits");

        group.MapPost("/", RecordVisit).WithName("RecordPageVisit");
        group.MapGet("/count", GetVisitCount).WithName("GetPageVisitCount");

        return routes;
    }

    private static async Task<Created> RecordVisit(
        RecordPageVisitRequest request,
        IPageVisitService service)
    {
        await service.RecordVisitAsync(request.PagePath);
        return TypedResults.Created();
    }

    private static async Task<Ok<PageVisitCountResponse>> GetVisitCount(
        string pagePath,
        IPageVisitService service)
    {
        var count = await service.GetVisitCountAsync(pagePath);
        return TypedResults.Ok(new PageVisitCountResponse(count));
    }
}
