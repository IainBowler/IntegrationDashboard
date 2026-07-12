using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Api.Contracts;
using Api.Services.IntegrationCalls;
using Api.Services.Integrations;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Api.Tests.Integration;

public class IntegrationsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly IntegrationStatisticsResponse SampleStatistics = new(
        "salesforce", "Salesforce",
        [
            new EndpointStatisticsResponse("auth", "Inbound", 5, 4, 12.5, 30, new DateTime(2026, 7, 12, 9, 0, 0), 200),
            new EndpointStatisticsResponse("token", "Outbound", 0, 0, null, null, null, null),
        ]);

    private readonly WebApplicationFactory<Program> _factory;

    public IntegrationsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient Client, IIntegrationCallService CallService) CreateClient()
    {
        var directory = Substitute.For<IIntegrationDirectoryService>();
        directory.GetIntegrationsAsync()
            .Returns(new List<IntegrationResponse> { new("salesforce", "Salesforce") });
        directory.GetStatisticsAsync("salesforce").Returns(SampleStatistics);
        directory.GetStatisticsAsync(Arg.Is<string>(n => n != "salesforce"))
            .Returns((IntegrationStatisticsResponse?)null);

        var callService = Substitute.For<IIntegrationCallService>();

        var client = _factory.WithAuthSettings()
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.AddSingleton(directory);
                s.AddSingleton(callService);
            }))
            .CreateClient();
        return (client, callService);
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

    [Theory(DisplayName = "the integrations meta endpoints 401 without a token")]
    [InlineData("/api/integrations")]
    [InlineData("/api/integrations/salesforce/statistics")]
    public async Task MetaEndpoints_WithoutToken_ReturnUnauthorized(string path)
    {
        var (client, _) = CreateClient();

        var response = await client.SendAsync(Get(path));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "GET /api/integrations lists the integrations")]
    public async Task GetIntegrations_ReturnsList()
    {
        var (client, _) = CreateClient();

        var response = await client.SendAsync(Get("/api/integrations", TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<IntegrationResponse>>();
        body.Should().ContainSingle().Which.Should().Be(new IntegrationResponse("salesforce", "Salesforce"));
    }

    [Fact(DisplayName = "GET /api/integrations/{name}/statistics returns the per-endpoint statistics")]
    public async Task GetStatistics_KnownIntegration_ReturnsStatistics()
    {
        var (client, _) = CreateClient();

        var response = await client.SendAsync(
            Get("/api/integrations/salesforce/statistics", TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<IntegrationStatisticsResponse>();
        body.Should().BeEquivalentTo(SampleStatistics);
    }

    [Fact(DisplayName = "GET /api/integrations/{name}/statistics 404s for an unknown integration")]
    public async Task GetStatistics_UnknownIntegration_ReturnsNotFound()
    {
        var (client, _) = CreateClient();

        var response = await client.SendAsync(
            Get("/api/integrations/hubspot/statistics", TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "the meta endpoints are not themselves recorded as integration calls")]
    public async Task MetaEndpoints_AreNotAudited()
    {
        var (client, callService) = CreateClient();

        await client.SendAsync(Get("/api/integrations", TestAuth.MintAccessToken()));
        await client.SendAsync(Get("/api/integrations/salesforce/statistics", TestAuth.MintAccessToken()));

        await callService.DidNotReceive().SaveAsync(Arg.Any<IntegrationCallRecord>());
    }
}
