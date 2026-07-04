using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Api.Contracts;
using Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Api.Tests.Integration;

public class ProtectedEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProtectedEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient client, IPageVisitService pageVisits) CreateClient()
    {
        var pageVisits = Substitute.For<IPageVisitService>();
        var client = _factory.WithAuthSettings()
            .WithWebHostBuilder(b => b.ConfigureServices(s => s.AddTransient(_ => pageVisits)))
            .CreateClient();
        return (client, pageVisits);
    }

    private static HttpRequestMessage Get(string path, string? bearerToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        return request;
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var (client, _) = CreateClient();

        var response = await client.SendAsync(Get("/auth/me"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithGarbageToken_ReturnsUnauthorized()
    {
        var (client, _) = CreateClient();

        var response = await client.SendAsync(Get("/auth/me", "not-a-jwt"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithExpiredToken_ReturnsUnauthorized()
    {
        var (client, _) = CreateClient();

        var response = await client.SendAsync(
            Get("/auth/me", TestAuth.MintAccessToken(expired: true)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithTokenSignedByWrongKey_ReturnsUnauthorized()
    {
        var (client, _) = CreateClient();

        var response = await client.SendAsync(Get("/auth/me",
            TestAuth.MintAccessToken(signingKey: "some-other-signing-key-0123456789abc")));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsClaims()
    {
        var (client, _) = CreateClient();

        var response = await client.SendAsync(Get("/auth/me", TestAuth.MintAccessToken(
            userId: 42, provider: "okta", email: "user@example.com", name: "Test User")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        body.Should().Be(new UserResponse(42, "okta", "user@example.com", "Test User"));
    }

    [Fact]
    public async Task Summary_WithoutToken_ReturnsUnauthorized()
    {
        var (client, _) = CreateClient();

        var response = await client.SendAsync(Get("/page-visits/summary"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Summary_WithValidToken_ReturnsPerPageCounts()
    {
        var (client, pageVisits) = CreateClient();
        pageVisits.GetSummaryAsync().Returns(new List<PageVisitSummaryItem>
        {
            new("/", 12),
            new("/dashboard", 5),
        });

        var response = await client.SendAsync(
            Get("/page-visits/summary", TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PageVisitSummaryResponse>();
        body!.Pages.Should().HaveCount(2);
        body.Pages[0].Should().Be(new PageVisitSummaryItem("/", 12));
        body.Pages[1].Should().Be(new PageVisitSummaryItem("/dashboard", 5));
    }

    [Fact]
    public async Task PublicEndpoints_RemainAnonymous()
    {
        var (client, pageVisits) = CreateClient();
        pageVisits.GetVisitCountAsync(Arg.Any<string>()).Returns(0L);

        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/page-visits/count?pagePath=/")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/page-visits", new RecordPageVisitRequest("/")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
