using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Api.Tests.Integration;

public class CorsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CorsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "requests from an allowed origin get the CORS header")]
    public async Task Request_FromAllowedOrigin_ReturnsCorsHeader()
    {
        var client = _factory.WithWebHostBuilder(b =>
            b.UseSetting("AllowedOrigins", "https://example.com"))
            .CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://example.com");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().Contain("https://example.com");
    }

    [Fact(DisplayName = "requests from an unknown origin get no CORS header")]
    public async Task Request_FromUnknownOrigin_DoesNotReturnCorsHeader()
    {
        var client = _factory.WithWebHostBuilder(b =>
            b.UseSetting("AllowedOrigins", "https://example.com"))
            .CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://attacker.com");

        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
