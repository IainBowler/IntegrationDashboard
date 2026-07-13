using Api.Contracts;
using Api.Services.Integrations.Salesforce;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.Endpoints;

public static class SalesforceEndpoints
{
    public static IEndpointRouteBuilder MapSalesforceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/integrations/salesforce")
            .WithTags("Salesforce")
            .RequireAuthorization()
            .AddEndpointFilter(new IntegrationCallRecordingFilter("salesforce"));

        group.MapGet("/auth", CheckAuth).WithName("CheckSalesforceAuth");
        group.MapGet("/accounts", GetAccounts).WithName("GetSalesforceAccounts");
        group.MapPost("/leads", CreateLead).WithName("CreateSalesforceLead");

        return routes;
    }

    private static async Task<Results<Ok, ProblemHttpResult>> CheckAuth(
        ISalesforceTokenProvider tokenProvider,
        CancellationToken ct)
    {
        try
        {
            await tokenProvider.GetSessionAsync(ct);
            return TypedResults.Ok();
        }
        catch (SalesforceApiException ex)
        {
            return Problem(ex);
        }
    }

    private static async Task<Results<Ok<IReadOnlyList<SalesforceAccountDto>>, ProblemHttpResult>> GetAccounts(
        SalesforceConnector connector,
        CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(await connector.GetAccountsAsync(ct));
        }
        catch (SalesforceApiException ex)
        {
            return Problem(ex);
        }
    }

    private static async Task<Results<Created<SalesforceLeadCreatedDto>, ValidationProblem, ProblemHttpResult>> CreateLead(
        CreateSalesforceLeadRequest request,
        SalesforceConnector connector,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.LastName))
            errors["lastName"] = ["LastName is required."];
        if (string.IsNullOrWhiteSpace(request.Company))
            errors["company"] = ["Company is required."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        try
        {
            // No Location header: there is no GET /leads/{id} route to point at.
            return TypedResults.Created((string?)null, await connector.CreateLeadAsync(request, ct));
        }
        catch (SalesforceApiException ex)
        {
            return Problem(ex);
        }
    }

    private static ProblemHttpResult Problem(SalesforceApiException ex) =>
        TypedResults.Problem(
            title: "Salesforce request failed",
            detail: ex.Message,
            statusCode: ex.Failure == SalesforceFailure.Timeout
                ? StatusCodes.Status504GatewayTimeout
                : StatusCodes.Status502BadGateway);
}
