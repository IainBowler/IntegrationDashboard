using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Api.Services.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Api.Tests.Integration;

public class TestProviderGatingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirects =
        new() { AllowAutoRedirect = false };

    private readonly WebApplicationFactory<Program> _factory;

    public TestProviderGatingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TestProvider_WithoutFlag_DoesNotExist()
    {
        var client = _factory.WithAuthSettings().CreateClient(NoRedirects);

        var response = await client.GetAsync("/auth/login/test");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TestProvider_WithFlagInProduction_DoesNotExist()
    {
        var client = _factory.WithAuthSettings()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                b.UseSetting("Auth:EnableTestProvider", "true");
            })
            .CreateClient(NoRedirects);

        var response = await client.GetAsync("/auth/login/test");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TestProvider_WithFlagInDevelopment_RedirectsToOwnCallback()
    {
        var client = _factory.WithAuthSettings()
            .WithWebHostBuilder(b => b.UseSetting("Auth:EnableTestProvider", "true"))
            .CreateClient(NoRedirects);

        var response = await client.GetAsync("/auth/login/test");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith($"{TestAuth.ApiBaseUrl}/auth/callback/test?");
        location.Should().Contain("code=test-code");
        location.Should().Contain("state=");
    }

    [Fact]
    public async Task TestProvider_FullLoginFlow_MintsWorkingTokens()
    {
        // real TestAuthProvider, flow service, code store, and token service;
        // only the database-backed services are substituted
        var user = new UserRecord(9, "test", "e2e-subject", "e2e@example.com", "E2E Test User");
        var users = Substitute.For<IUserService>();
        users.UpsertExternalUserAsync(Arg.Any<ExternalUserInfo>()).Returns(user);
        users.GetByIdAsync(9).Returns(user);
        var refreshTokens = Substitute.For<IRefreshTokenService>();
        refreshTokens.IssueAsync(9).Returns("refresh-raw");

        var client = _factory.WithAuthSettings()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Auth:EnableTestProvider", "true");
                b.ConfigureServices(s =>
                {
                    s.AddTransient(_ => users);
                    s.AddTransient(_ => refreshTokens);
                });
            })
            .CreateClient(NoRedirects);

        // login redirects to our own callback; replay that callback locally
        var loginResponse = await client.GetAsync("/auth/login/test");
        var callbackUri = new Uri(loginResponse.Headers.Location!.ToString());
        var callbackResponse = await client.GetAsync(callbackUri.PathAndQuery);

        var fragment = new Uri(callbackResponse.Headers.Location!.ToString()).Fragment;
        fragment.Should().StartWith("#code=");
        var handoffCode = fragment["#code=".Length..];

        var tokenResponse = await client.PostAsJsonAsync(
            "/auth/token", new ExchangeTokenRequest(handoffCode));
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
        tokens!.User.Provider.Should().Be("test");
        tokens.User.DisplayName.Should().Be("E2E Test User");
    }
}
