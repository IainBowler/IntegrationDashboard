using System.Net;
using System.Net.Http.Headers;
using Api.Options;
using Api.Services.IntegrationCalls;
using Api.Services.Integrations.Salesforce;
using Api.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Api.Tests.Integration;

/// <summary>
/// Exercises the inbound recording filter through the real pipeline (routing,
/// JwtBearer auth, endpoint filter) with the persistence service substituted —
/// no database and no network.
/// </summary>
public class IntegrationCallRecordingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AuthUrl = "/api/integrations/salesforce/auth";
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

    public IntegrationCallRecordingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient Client, List<IntegrationCallRecord> Saved) CreateClient(
        StubHttpHandler handler, bool saveFails = false)
    {
        var saved = new List<IntegrationCallRecord>();
        var callService = Substitute.For<IIntegrationCallService>();
        if (saveFails)
        {
            callService.SaveAsync(Arg.Any<IntegrationCallRecord>())
                .Returns<Task>(_ => throw new InvalidOperationException("db down"));
        }
        else
        {
            callService.SaveAsync(Arg.Do<IntegrationCallRecord>(saved.Add)).Returns(Task.CompletedTask);
        }

        var tokenProvider = Substitute.For<ISalesforceTokenProvider>();
        tokenProvider.GetSessionAsync(Arg.Any<CancellationToken>())
            .Returns(new SalesforceSession("sf-access-token", "https://myorg.my.salesforce.com"));

        var client = _factory.WithAuthSettings()
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.AddSingleton(callService);
                s.AddTransient(_ => tokenProvider);
                s.AddTransient(sp => new SalesforceConnector(
                    new HttpClient(handler),
                    tokenProvider,
                    sp.GetRequiredService<IOptions<SalesforceOptions>>(),
                    sp.GetRequiredService<ILogger<SalesforceConnector>>()));
            }))
            .CreateClient();
        return (client, saved);
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

    [Fact(DisplayName = "an authorised auth-check call is recorded inbound with the caller's user id")]
    public async Task AuthCheck_RecordsInboundRowWithUserId()
    {
        var (client, saved) = CreateClient(
            new StubHttpHandler(_ => throw new InvalidOperationException("must not be reached")));

        var response = await client.SendAsync(Get(AuthUrl, TestAuth.MintAccessToken(userId: 42)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var record = saved.Should().ContainSingle().Which;
        record.Direction.Should().Be(IntegrationCallDirection.Inbound);
        record.IntegrationName.Should().Be("salesforce");
        record.EndpointName.Should().Be("auth");
        record.Method.Should().Be("GET");
        record.Url.Should().Be(AuthUrl);
        record.StatusCode.Should().Be(200);
        record.UserId.Should().Be(42);
        record.RequestBody.Should().BeNull();
        record.ResponseBody.Should().BeNull();
        record.CorrelationId.Should().NotBeNullOrEmpty();
    }

    [Fact(DisplayName = "an accounts call records the mapped response body")]
    public async Task Accounts_RecordsInboundRowWithResponseBody()
    {
        var (client, saved) = CreateClient(
            new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.OK, OneAccountJson)));

        var response = await client.SendAsync(Get(AccountsUrl, TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var record = saved.Should().ContainSingle().Which;
        record.EndpointName.Should().Be("accounts");
        record.StatusCode.Should().Be(200);
        record.ResponseBody.Should().Contain("Acme").And.NotContain("attributes");
    }

    [Fact(DisplayName = "a Salesforce failure records the 502 ProblemDetails outcome")]
    public async Task Accounts_WhenSalesforceFails_RecordsProblemOutcome()
    {
        var (client, saved) = CreateClient(new StubHttpHandler(_ =>
            StubHttpHandler.Json(HttpStatusCode.InternalServerError, """[{"errorCode":"UNKNOWN_EXCEPTION"}]""")));

        var response = await client.SendAsync(Get(AccountsUrl, TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var record = saved.Should().ContainSingle().Which;
        record.StatusCode.Should().Be(502);
        record.ResponseBody.Should().Contain("Salesforce request failed");
    }

    [Fact(DisplayName = "anonymous 401s never reach the recorder")]
    public async Task AnonymousRequest_IsNotRecorded()
    {
        var (client, saved) = CreateClient(
            new StubHttpHandler(_ => throw new InvalidOperationException("must not be reached")));

        var response = await client.SendAsync(Get(AuthUrl));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        saved.Should().BeEmpty();
    }

    private static HttpRequestMessage PostLead(object body, string? bearerToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/salesforce/leads")
        {
            Content = System.Net.Http.Json.JsonContent.Create(body),
        };
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        return request;
    }

    [Fact(DisplayName = "a lead create records the request body and the created id — traceable in both directions")]
    public async Task PostLeads_RecordsInboundRowWithRequestAndResponseBody()
    {
        var (client, saved) = CreateClient(new StubHttpHandler(_ =>
            StubHttpHandler.Json(HttpStatusCode.Created, """{"id":"00QA000001abcDE","success":true,"errors":[]}""")));

        var response = await client.SendAsync(PostLead(new
        {
            lastName = "Sample-trace1",
            company = "Integration Dashboard",
            email = "sample-trace1@example.com",
        }, TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var record = saved.Should().ContainSingle().Which;
        record.Direction.Should().Be(IntegrationCallDirection.Inbound);
        record.EndpointName.Should().Be("leads");
        record.Method.Should().Be("POST");
        record.StatusCode.Should().Be(201);
        record.RequestBody.Should().Contain("Sample-trace1").And.Contain("Integration Dashboard");
        record.ResponseBody.Should().Contain("00QA000001abcDE");
    }

    [Fact(DisplayName = "a rejected lead create records the 400 outcome with the offending request body")]
    public async Task PostLeads_WhenValidationFails_Records400WithRequestBody()
    {
        var (client, saved) = CreateClient(
            new StubHttpHandler(_ => throw new InvalidOperationException("must not be reached")));

        var response = await client.SendAsync(
            PostLead(new { lastName = "", company = "Integration Dashboard" }, TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var record = saved.Should().ContainSingle().Which;
        record.EndpointName.Should().Be("leads");
        record.StatusCode.Should().Be(400);
        record.RequestBody.Should().Contain("Integration Dashboard");
    }

    [Fact(DisplayName = "a failing save does not fail the endpoint")]
    public async Task AuthCheck_WhenSaveFails_StillReturnsOk()
    {
        var (client, _) = CreateClient(
            new StubHttpHandler(_ => throw new InvalidOperationException("must not be reached")),
            saveFails: true);

        var response = await client.SendAsync(Get(AuthUrl, TestAuth.MintAccessToken()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
