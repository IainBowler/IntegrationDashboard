using System.Security.Claims;
using Api.Contracts;
using Api.Options;
using Api.Services.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth").WithTags("Auth");

        group.MapGet("/login/{provider}", BeginLogin).WithName("BeginLogin");
        group.MapGet("/callback/{provider}", HandleCallback).WithName("HandleAuthCallback");
        group.MapPost("/token", ExchangeToken).WithName("ExchangeToken");
        group.MapPost("/refresh", Refresh).WithName("RefreshSession");
        group.MapPost("/logout", Logout).WithName("Logout");
        group.MapGet("/me", GetCurrentUser).WithName("GetCurrentUser").RequireAuthorization();

        return routes;
    }

    private static Results<RedirectHttpResult, NotFound> BeginLogin(
        string provider,
        IAuthFlowService flow)
    {
        var authorizationUrl = flow.BeginLogin(provider);
        return authorizationUrl is null
            ? TypedResults.NotFound()
            : TypedResults.Redirect(authorizationUrl);
    }

    private static async Task<RedirectHttpResult> HandleCallback(
        string provider,
        string? code,
        string? state,
        IAuthFlowService flow,
        IOptions<AuthOptions> authOptions)
    {
        string? handoffCode = null;
        if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(state))
        {
            handoffCode = await flow.HandleCallbackAsync(provider, code, state);
        }

        // Always land the user back on the SPA; the fragment never reaches
        // server logs.
        var frontend = authOptions.Value.FrontendBaseUrl.TrimEnd('/');
        var fragment = handoffCode is null ? "error=login_failed" : $"code={handoffCode}";
        return TypedResults.Redirect($"{frontend}/auth/callback#{fragment}");
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult>> ExchangeToken(
        ExchangeTokenRequest request,
        IAuthFlowService flow)
    {
        var tokens = await flow.ExchangeHandoffCodeAsync(request.Code);
        return tokens is null ? TypedResults.Unauthorized() : TypedResults.Ok(tokens);
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult>> Refresh(
        RefreshTokenRequest request,
        IAuthFlowService flow)
    {
        var tokens = await flow.RefreshAsync(request.RefreshToken);
        return tokens is null ? TypedResults.Unauthorized() : TypedResults.Ok(tokens);
    }

    private static async Task<NoContent> Logout(
        LogoutRequest request,
        IAuthFlowService flow)
    {
        await flow.LogoutAsync(request.RefreshToken);
        return TypedResults.NoContent();
    }

    private static Ok<UserResponse> GetCurrentUser(ClaimsPrincipal user)
    {
        long.TryParse(user.FindFirstValue("sub"), out var userId);
        return TypedResults.Ok(new UserResponse(
            userId,
            user.FindFirstValue("provider") ?? "",
            user.FindFirstValue("email"),
            user.FindFirstValue("name")));
    }
}
