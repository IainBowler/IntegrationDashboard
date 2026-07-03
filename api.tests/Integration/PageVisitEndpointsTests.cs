using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Api.Tests.Integration;

public class PageVisitEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PageVisitEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient client, IPageVisitService service) CreateClient()
    {
        var fake = Substitute.For<IPageVisitService>();
        var client = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.AddTransient<IPageVisitService>(_ => fake);
            }))
            .CreateClient();
        return (client, fake);
    }

    [Fact]
    public async Task PostPageVisit_ReturnsCreated()
    {
        var (client, service) = CreateClient();

        var response = await client.PostAsJsonAsync("/page-visits",
            new RecordPageVisitRequest("/dashboard"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await service.Received(1).RecordVisitAsync("/dashboard");
    }

    [Fact]
    public async Task PostPageVisit_CallsServiceWithCorrectPath()
    {
        var (client, service) = CreateClient();

        await client.PostAsJsonAsync("/page-visits",
            new RecordPageVisitRequest("/about"));

        await service.Received(1).RecordVisitAsync("/about");
    }

    [Fact]
    public async Task GetPageVisitCount_ReturnsOkWithCount()
    {
        var (client, service) = CreateClient();
        service.GetVisitCountAsync("/dashboard").Returns(7L);

        var response = await client.GetAsync("/page-visits/count?pagePath=/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PageVisitCountResponse>();
        body!.Count.Should().Be(7);
    }

    [Fact]
    public async Task GetPageVisitCount_CallsServiceWithCorrectPath()
    {
        var (client, service) = CreateClient();
        service.GetVisitCountAsync(Arg.Any<string>()).Returns(0L);

        await client.GetAsync("/page-visits/count?pagePath=/home");

        await service.Received(1).GetVisitCountAsync("/home");
    }

    [Fact]
    public async Task GetPageVisitCount_WhenZeroVisits_ReturnsZero()
    {
        var (client, service) = CreateClient();
        service.GetVisitCountAsync(Arg.Any<string>()).Returns(0L);

        var response = await client.GetAsync("/page-visits/count?pagePath=/never-visited");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PageVisitCountResponse>();
        body!.Count.Should().Be(0);
    }
}
