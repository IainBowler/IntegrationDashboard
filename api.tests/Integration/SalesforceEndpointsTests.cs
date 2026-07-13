using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Api.Contracts;
using Api.Options;
using Api.Services.Integrations.Salesforce;
using Api.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Api.Tests.Integration;

/// <summary>
/// Exercises GET /api/integrations/salesforce/accounts through the real
/// pipeline (routing, JwtBearer auth) with the Salesforce HTTP handler and
/// token provider stubbed — no test touches the network.
/// </summary>
public class SalesforceEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AccountsUrl = "/api/integrations/salesforce/accounts";

    private const string OneAccountJson = """
        {
          "totalSize": 1,
          "done": true,
          "records": [
            {
              "attributes": { "type": "Account", "url": "/services/data/v66.0/sobjects/Account/001A" },
              "Id": "001A",
              "Name": "Acme",
              "Industry": "Technology",
              "Type": "Customer - Direct",
              "Website": "https://acme.example",
              "LastModifiedDate": "2026-07-01T12:34:56.000+0000"
            }
          ]
        }
        """;

    private readonly WebApplicationFactory<Program> _factory;

    public SalesforceEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(StubHttpHandler handler, ISalesforceTokenProvider? tokenProvider = null)
    {
        if (tokenProvider is null)
        {
            tokenProvider = Substitute.For<ISalesforceTokenProvider>();
            tokenProvider.GetSessionAsync(Arg.Any<CancellationToken>())
                .Returns(new SalesforceSession("sf-access-token", "https://myorg.my.salesforce.com"));
        }
        return _factory.WithAuthSettings()
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.AddTransient(_ => tokenProvider);
                // last registration wins over the typed-client one from Program.cs
                s.AddTransient(sp => new SalesforceConnector(
                    new HttpClient(handler),
                    tokenProvider,
                    sp.GetRequiredService<IOptions<SalesforceOptions>>(),
                    sp.GetRequiredService<ILogger<SalesforceConnector>>()));
            }))
            .CreateClient();
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

    [Fact(DisplayName = "GET /api/integrations/salesforce/accounts 401s without a token — before any Salesforce call")]
    public async Task Accounts_WithoutToken_ReturnsUnauthorized()
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("must not be reached"));
        var client = CreateClient(handler);

        var response = await client.SendAsync(Get(AccountsUrl));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        handler.Requests.Should().BeEmpty();
    }

    [Fact(DisplayName = "GET /api/integrations/salesforce/accounts returns mapped accounts for a valid token")]
    public async Task Accounts_WithValidToken_ReturnsMappedAccounts()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.OK, OneAccountJson));
        var client = CreateClient(handler);

        var response = await client.SendAsync(Get(AccountsUrl, TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("attributes", "raw Salesforce JSON must not leak to callers");

        var accounts = await response.Content.ReadFromJsonAsync<List<SalesforceAccountDto>>();
        accounts.Should().HaveCount(1);
        accounts![0].Should().Be(new SalesforceAccountDto(
            "001A", "Acme", "Technology", "Customer - Direct", "https://acme.example",
            new DateTimeOffset(2026, 7, 1, 12, 34, 56, TimeSpan.Zero)));

        handler.Requests.Single().Request.Headers.Authorization!.Parameter
            .Should().Be("sf-access-token", "the Salesforce session token, not the caller's JWT, goes upstream");
    }

    [Fact(DisplayName = "a Salesforce failure surfaces as a 502 ProblemDetails, not an unhandled 500")]
    public async Task Accounts_WhenSalesforceFails_Returns502Problem()
    {
        var handler = new StubHttpHandler(_ =>
            StubHttpHandler.Json(HttpStatusCode.InternalServerError, """[{"errorCode":"UNKNOWN_EXCEPTION"}]"""));
        var client = CreateClient(handler);

        var response = await client.SendAsync(Get(AccountsUrl, TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.Should().Be("Salesforce request failed");
        problem.Status.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact(DisplayName = "GET /api/integrations/salesforce/auth 401s without a token")]
    public async Task Auth_WithoutToken_ReturnsUnauthorized()
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("must not be reached"));
        var client = CreateClient(handler);

        var response = await client.SendAsync(Get("/api/integrations/salesforce/auth"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "GET /api/integrations/salesforce/auth returns 200 when a Salesforce session is established")]
    public async Task Auth_WhenSessionEstablished_ReturnsOk()
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("must not be reached"));
        var client = CreateClient(handler);

        var response = await client.SendAsync(
            Get("/api/integrations/salesforce/auth", TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "GET /api/integrations/salesforce/auth surfaces a failed exchange as a 502 ProblemDetails")]
    public async Task Auth_WhenExchangeFails_Returns502Problem()
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("must not be reached"));
        var tokenProvider = Substitute.For<ISalesforceTokenProvider>();
        tokenProvider.GetSessionAsync(Arg.Any<CancellationToken>())
            .Returns<SalesforceSession>(_ => throw new SalesforceApiException(
                SalesforceFailure.AuthFailed, "Salesforce token exchange failed: invalid_grant"));
        var client = CreateClient(handler, tokenProvider);

        var response = await client.SendAsync(
            Get("/api/integrations/salesforce/auth", TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.Should().Be("Salesforce request failed");
        problem.Detail.Should().Contain("invalid_grant");
    }

    [Fact(DisplayName = "a Salesforce timeout surfaces as a 504 ProblemDetails")]
    public async Task Accounts_WhenSalesforceTimesOut_Returns504Problem()
    {
        var handler = new StubHttpHandler(_ => throw new TaskCanceledException());
        var client = CreateClient(handler);

        var response = await client.SendAsync(Get(AccountsUrl, TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.Should().Be(StatusCodes.Status504GatewayTimeout);
    }

    private const string LeadsUrl = "/api/integrations/salesforce/leads";
    private const string LeadCreatedJson = """{"id":"00QA000001abcDE","success":true,"errors":[]}""";

    private static HttpRequestMessage PostLead(object body, string? bearerToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, LeadsUrl) { Content = JsonContent.Create(body) };
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        return request;
    }

    private static object ValidLead() => new
    {
        lastName = "Sample-abc123",
        company = "Integration Dashboard",
        firstName = "Dashboard",
        email = "sample-abc123@example.com",
    };

    [Fact(DisplayName = "POST /api/integrations/salesforce/leads 401s without a token — before any Salesforce call")]
    public async Task Leads_WithoutToken_ReturnsUnauthorized()
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("must not be reached"));
        var client = CreateClient(handler);

        var response = await client.SendAsync(PostLead(ValidLead()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        handler.Requests.Should().BeEmpty();
    }

    [Fact(DisplayName = "POST /api/integrations/salesforce/leads returns 201 with the created id for a valid token")]
    public async Task Leads_WithValidToken_Returns201WithCreatedId()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.Created, LeadCreatedJson));
        var client = CreateClient(handler);

        var response = await client.SendAsync(PostLead(ValidLead(), TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("success", "raw Salesforce JSON must not leak to callers");
        var created = await response.Content.ReadFromJsonAsync<SalesforceLeadCreatedDto>();
        created.Should().Be(new SalesforceLeadCreatedDto("00QA000001abcDE"));

        handler.Requests.Single().Request.Headers.Authorization!.Parameter
            .Should().Be("sf-access-token", "the Salesforce session token, not the caller's JWT, goes upstream");
    }

    [Fact(DisplayName = "blank required fields return a 400 ValidationProblem — before any Salesforce call")]
    public async Task Leads_WithBlankRequiredFields_Returns400ValidationProblem()
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("must not be reached"));
        var client = CreateClient(handler);

        var response = await client.SendAsync(
            PostLead(new { lastName = "", company = "  " }, TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Keys.Should().BeEquivalentTo("lastName", "company");
        handler.Requests.Should().BeEmpty();
    }

    [Fact(DisplayName = "a Salesforce failure on create surfaces as a 502 ProblemDetails, not an unhandled 500")]
    public async Task Leads_WhenSalesforceFails_Returns502Problem()
    {
        var handler = new StubHttpHandler(_ =>
            StubHttpHandler.Json(HttpStatusCode.InternalServerError, """[{"errorCode":"UNKNOWN_EXCEPTION"}]"""));
        var client = CreateClient(handler);

        var response = await client.SendAsync(PostLead(ValidLead(), TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.Should().Be("Salesforce request failed");
    }

    [Fact(DisplayName = "a Salesforce timeout on create surfaces as a 504 ProblemDetails")]
    public async Task Leads_WhenSalesforceTimesOut_Returns504Problem()
    {
        var handler = new StubHttpHandler(_ => throw new TaskCanceledException());
        var client = CreateClient(handler);

        var response = await client.SendAsync(PostLead(ValidLead(), TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.Should().Be(StatusCodes.Status504GatewayTimeout);
    }
}
