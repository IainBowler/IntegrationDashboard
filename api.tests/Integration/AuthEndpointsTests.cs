using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Api.Contracts;
using Api.Services.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Api.Tests.Integration;

public class AuthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirects =
        new() { AllowAutoRedirect = false };

    private readonly WebApplicationFactory<Program> _factory;

    public AuthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient client, IAuthFlowService flow) CreateClientWithFlowSubstitute()
    {
        var flow = Substitute.For<IAuthFlowService>();
        var client = _factory.WithAuthSettings()
            .WithWebHostBuilder(b => b.ConfigureServices(s => s.AddTransient(_ => flow)))
            .CreateClient(NoRedirects);
        return (client, flow);
    }

    [Fact(DisplayName = "GET /auth/login/{provider} 404s for an unknown provider")]
    public async Task BeginLogin_UnknownProvider_ReturnsNotFound()
    {
        var (client, flow) = CreateClientWithFlowSubstitute();
        flow.BeginLogin("myspace").Returns((string?)null);

        var response = await client.GetAsync("/auth/login/myspace");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "GET /auth/login redirects to the provider's authorization URL")]
    public async Task BeginLogin_KnownProvider_RedirectsToAuthorizationUrl()
    {
        var (client, flow) = CreateClientWithFlowSubstitute();
        flow.BeginLogin("okta").Returns("https://idp.example/authorize?state=abc");

        var response = await client.GetAsync("/auth/login/okta");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("https://idp.example/authorize?state=abc");
    }

    [Fact(DisplayName = "the real Okta redirect carries state, PKCE challenge, and client id")]
    public async Task BeginLogin_WithRealOktaProvider_RedirectsToOktaWithOidcParameters()
    {
        // no service substitution: exercises the real flow service, provider,
        // and one-time code store
        var client = _factory.WithAuthSettings()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Auth:Okta:Issuer", "https://dev-123.okta.com/oauth2/default");
                b.UseSetting("Auth:Okta:ClientId", "test-client-id");
            })
            .CreateClient(NoRedirects);

        var response = await client.GetAsync("/auth/login/okta");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("https://dev-123.okta.com/oauth2/default/v1/authorize?");
        location.Should().Contain("client_id=test-client-id");
        location.Should().Contain("state=");
        location.Should().Contain("code_challenge=");
        location.Should().Contain("code_challenge_method=S256");
        location.Should().Contain(Uri.EscapeDataString($"{TestAuth.ApiBaseUrl}/auth/callback/okta"));
    }

    [Fact(DisplayName = "a successful callback redirects to the SPA with the handoff code in the fragment")]
    public async Task Callback_Success_RedirectsToSpaWithHandoffCodeInFragment()
    {
        var (client, flow) = CreateClientWithFlowSubstitute();
        flow.HandleCallbackAsync("okta", "ext-code", "state-1").Returns("handoff-1");

        var response = await client.GetAsync("/auth/callback/okta?code=ext-code&state=state-1");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(
            $"{TestAuth.FrontendBaseUrl}/auth/callback#code=handoff-1");
    }

    [Fact(DisplayName = "a failed callback still lands the user on the SPA, with an error fragment")]
    public async Task Callback_FlowFailure_RedirectsToSpaWithError()
    {
        var (client, flow) = CreateClientWithFlowSubstitute();
        flow.HandleCallbackAsync("okta", "ext-code", "bad-state").Returns((string?)null);

        var response = await client.GetAsync("/auth/callback/okta?code=ext-code&state=bad-state");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(
            $"{TestAuth.FrontendBaseUrl}/auth/callback#error=login_failed");
    }

    [Fact(DisplayName = "a callback without code or state errors out without running the flow")]
    public async Task Callback_MissingCodeOrState_RedirectsToSpaWithErrorWithoutCallingFlow()
    {
        var (client, flow) = CreateClientWithFlowSubstitute();

        var response = await client.GetAsync("/auth/callback/okta");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().EndWith("#error=login_failed");
        await flow.DidNotReceiveWithAnyArgs().HandleCallbackAsync(default!, default!, default!, default);
    }

    [Fact(DisplayName = "POST /auth/token exchanges a valid handoff code for tokens")]
    public async Task ExchangeToken_ValidCode_ReturnsTokens()
    {
        var (client, flow) = CreateClientWithFlowSubstitute();
        var tokens = new TokenResponse("access-jwt", 900, "refresh-raw",
            new UserResponse(1, "okta", "user@example.com", "Test User"));
        flow.ExchangeHandoffCodeAsync("handoff-1").Returns(tokens);

        var response = await client.PostAsJsonAsync("/auth/token", new ExchangeTokenRequest("handoff-1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        body.Should().Be(tokens);
    }

    [Fact(DisplayName = "POST /auth/token 401s for an invalid handoff code")]
    public async Task ExchangeToken_InvalidCode_ReturnsUnauthorized()
    {
        var (client, flow) = CreateClientWithFlowSubstitute();
        flow.ExchangeHandoffCodeAsync("bad").Returns((TokenResponse?)null);

        var response = await client.PostAsJsonAsync("/auth/token", new ExchangeTokenRequest("bad"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "POST /auth/refresh returns a rotated token pair")]
    public async Task Refresh_ValidToken_ReturnsRotatedTokens()
    {
        var (client, flow) = CreateClientWithFlowSubstitute();
        var tokens = new TokenResponse("new-access", 900, "new-refresh",
            new UserResponse(1, "okta", "user@example.com", "Test User"));
        flow.RefreshAsync("old-refresh").Returns(tokens);

        var response = await client.PostAsJsonAsync("/auth/refresh", new RefreshTokenRequest("old-refresh"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        body!.RefreshToken.Should().Be("new-refresh");
    }

    [Fact(DisplayName = "POST /auth/refresh 401s for a stale token")]
    public async Task Refresh_InvalidToken_ReturnsUnauthorized()
    {
        var (client, flow) = CreateClientWithFlowSubstitute();
        flow.RefreshAsync("stale").Returns((TokenResponse?)null);

        var response = await client.PostAsJsonAsync("/auth/refresh", new RefreshTokenRequest("stale"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "POST /auth/logout revokes the session and returns 204")]
    public async Task Logout_ReturnsNoContentAndRevokes()
    {
        var (client, flow) = CreateClientWithFlowSubstitute();

        var response = await client.PostAsJsonAsync("/auth/logout", new LogoutRequest("refresh-raw"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await flow.Received(1).LogoutAsync("refresh-raw");
    }

    [Fact(DisplayName = "the full login flow works end to end: login, callback, handoff, bearer call")]
    public async Task FullLoginFlow_FromLoginToProtectedEndpoint_Succeeds()
    {
        // Real flow service, token service, and one-time code store; only the
        // external IdP and the database-backed services are faked.
        var user = new UserRecord(7, "okta", "sub-7", "user@example.com", "Test User");

        var provider = Substitute.For<IExternalAuthProvider>();
        provider.Name.Returns("okta");
        provider.BuildAuthorizationUrl(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo => $"https://idp.example/authorize?state={callInfo.ArgAt<string>(0)}");
        provider.ExchangeCodeAsync("ext-code", Arg.Any<string>(), Arg.Any<string>())
            .Returns(new ExternalUserInfo("okta", "sub-7", "user@example.com", "Test User"));

        var users = Substitute.For<IUserService>();
        users.UpsertExternalUserAsync(Arg.Any<ExternalUserInfo>()).Returns(user);
        users.GetByIdAsync(7).Returns(user);

        var refreshTokens = Substitute.For<IRefreshTokenService>();
        refreshTokens.IssueAsync(7).Returns("refresh-raw");

        var client = _factory.WithAuthSettings()
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IExternalAuthProvider>();
                s.AddTransient(_ => provider);
                s.AddTransient(_ => users);
                s.AddTransient(_ => refreshTokens);
            }))
            .CreateClient(NoRedirects);

        // 1. begin login and capture the state round-tripped via the fake IdP
        var loginResponse = await client.GetAsync("/auth/login/okta");
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var authorizeUrl = loginResponse.Headers.Location!.ToString();
        var state = QueryHelpers.ParseQuery(new Uri(authorizeUrl).Query)["state"].ToString();
        state.Should().NotBeNullOrEmpty();

        // 2. the IdP calls back; the API redirects to the SPA with a handoff code
        var callbackResponse = await client.GetAsync(
            $"/auth/callback/okta?code=ext-code&state={Uri.EscapeDataString(state)}");
        callbackResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var fragment = new Uri(callbackResponse.Headers.Location!.ToString()).Fragment;
        fragment.Should().StartWith("#code=");
        var handoffCode = fragment["#code=".Length..];

        // 3. the SPA exchanges the handoff code for tokens
        var tokenResponse = await client.PostAsJsonAsync(
            "/auth/token", new ExchangeTokenRequest(handoffCode));
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
        tokens!.RefreshToken.Should().Be("refresh-raw");
        tokens.User.UserId.Should().Be(7);

        // 4. the handoff code is single-use
        var replayResponse = await client.PostAsJsonAsync(
            "/auth/token", new ExchangeTokenRequest(handoffCode));
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 5. the minted access token works against a protected endpoint
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var meResponse = await client.SendAsync(meRequest);
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await meResponse.Content.ReadFromJsonAsync<UserResponse>();
        me.Should().Be(new UserResponse(7, "okta", "user@example.com", "Test User"));
    }
}
