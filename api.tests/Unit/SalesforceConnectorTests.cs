using System.Net;
using Api.Contracts;
using Api.Options;
using Api.Services.IntegrationCalls;
using Api.Services.Integrations.Salesforce;
using Api.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Api.Tests.Unit;

public class SalesforceConnectorTests
{
    private const string InstanceUrl = "https://myorg.my.salesforce.com";
    private const string AccessToken = "sf-access-token";

    // Real Salesforce query envelope: attributes blobs and a colon-less
    // UTC offset in LastModifiedDate, exactly as the API returns them.
    private const string TwoAccountsJson = """
        {
          "totalSize": 2,
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
            },
            {
              "attributes": { "type": "Account", "url": "/services/data/v66.0/sobjects/Account/001B" },
              "Id": "001B",
              "Name": "Globex",
              "Industry": null,
              "Type": null,
              "Website": null,
              "LastModifiedDate": "2026-06-30T08:00:00.000+0000"
            }
          ]
        }
        """;

    private static (SalesforceConnector Connector, ISalesforceTokenProvider TokenProvider) CreateConnector(
        StubHttpHandler handler)
    {
        var tokenProvider = Substitute.For<ISalesforceTokenProvider>();
        tokenProvider.GetSessionAsync(Arg.Any<CancellationToken>())
            .Returns(new SalesforceSession(AccessToken, InstanceUrl));
        var connector = new SalesforceConnector(
            new HttpClient(handler),
            tokenProvider,
            Microsoft.Extensions.Options.Options.Create(new SalesforceOptions()),
            NullLogger<SalesforceConnector>.Instance);
        return (connector, tokenProvider);
    }

    [Fact(DisplayName = "the SOQL query goes to the session's instance_url with the session's bearer token")]
    public async Task GetAccounts_QueriesInstanceUrlWithBearerToken()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.OK, TwoAccountsJson));
        var (connector, _) = CreateConnector(handler);

        await connector.GetAccountsAsync();

        var request = handler.Requests.Single().Request;
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.ToString().Should().StartWith($"{InstanceUrl}/services/data/v66.0/query?q=");
        Uri.UnescapeDataString(request.RequestUri.Query).Should().Contain(
            "SELECT Id, Name, Industry, Type, Website, LastModifiedDate FROM Account");
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be(AccessToken);
        request.Options.TryGetValue(IntegrationCallOptions.EndpointName, out var endpointName)
            .Should().BeTrue();
        endpointName.Should().Be("query");
    }

    [Fact(DisplayName = "records map to clean DTOs — including nulls and Salesforce's +0000 datetime offset")]
    public async Task GetAccounts_MapsRecordsToDtos()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.OK, TwoAccountsJson));
        var (connector, _) = CreateConnector(handler);

        var accounts = await connector.GetAccountsAsync();

        accounts.Should().HaveCount(2);
        accounts[0].Should().Be(new SalesforceAccountDto(
            "001A", "Acme", "Technology", "Customer - Direct", "https://acme.example",
            new DateTimeOffset(2026, 7, 1, 12, 34, 56, TimeSpan.Zero)));
        accounts[1].Should().Be(new SalesforceAccountDto(
            "001B", "Globex", null, null, null,
            new DateTimeOffset(2026, 6, 30, 8, 0, 0, TimeSpan.Zero)));
    }

    [Fact(DisplayName = "an empty result set maps to an empty list")]
    public async Task GetAccounts_WithNoRecords_ReturnsEmptyList()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.OK,
            """{"totalSize":0,"done":true,"records":[]}"""));
        var (connector, _) = CreateConnector(handler);

        (await connector.GetAccountsAsync()).Should().BeEmpty();
    }

    [Fact(DisplayName = "a 401 invalidates the cached session and retries once, then succeeds")]
    public async Task GetAccounts_On401_InvalidatesAndRetriesOnce()
    {
        var calls = 0;
        var handler = new StubHttpHandler(_ => ++calls == 1
            ? StubHttpHandler.Json(HttpStatusCode.Unauthorized, """[{"errorCode":"INVALID_SESSION_ID"}]""")
            : StubHttpHandler.Json(HttpStatusCode.OK, TwoAccountsJson));
        var (connector, tokenProvider) = CreateConnector(handler);

        var accounts = await connector.GetAccountsAsync();

        accounts.Should().HaveCount(2);
        handler.Requests.Should().HaveCount(2);
        tokenProvider.Received(1).Invalidate();
    }

    [Fact(DisplayName = "a second consecutive 401 gives up with AuthFailed — no retry loop")]
    public async Task GetAccounts_OnRepeated401_ThrowsAuthFailedAfterExactlyTwoAttempts()
    {
        var handler = new StubHttpHandler(_ =>
            StubHttpHandler.Json(HttpStatusCode.Unauthorized, """[{"errorCode":"INVALID_SESSION_ID"}]"""));
        var (connector, _) = CreateConnector(handler);

        var act = () => connector.GetAccountsAsync();

        (await act.Should().ThrowAsync<SalesforceApiException>())
            .Which.Failure.Should().Be(SalesforceFailure.AuthFailed);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact(DisplayName = "a Salesforce 5xx surfaces as UpstreamError with the status code")]
    public async Task GetAccounts_OnServerError_ThrowsUpstreamError()
    {
        var handler = new StubHttpHandler(_ =>
            StubHttpHandler.Json(HttpStatusCode.InternalServerError, """[{"errorCode":"UNKNOWN_EXCEPTION"}]"""));
        var (connector, _) = CreateConnector(handler);

        var act = () => connector.GetAccountsAsync();

        var ex = (await act.Should().ThrowAsync<SalesforceApiException>()).Which;
        ex.Failure.Should().Be(SalesforceFailure.UpstreamError);
        ex.Message.Should().Contain("500");
    }

    [Fact(DisplayName = "an HttpClient timeout surfaces as Timeout")]
    public async Task GetAccounts_WhenRequestTimesOut_ThrowsTimeout()
    {
        var handler = new StubHttpHandler(_ => throw new TaskCanceledException());
        var (connector, _) = CreateConnector(handler);

        var act = () => connector.GetAccountsAsync();

        (await act.Should().ThrowAsync<SalesforceApiException>())
            .Which.Failure.Should().Be(SalesforceFailure.Timeout);
    }

    [Fact(DisplayName = "a network failure surfaces as UpstreamError")]
    public async Task GetAccounts_WhenNetworkFails_ThrowsUpstreamError()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("connection refused"));
        var (connector, _) = CreateConnector(handler);

        var act = () => connector.GetAccountsAsync();

        (await act.Should().ThrowAsync<SalesforceApiException>())
            .Which.Failure.Should().Be(SalesforceFailure.UpstreamError);
    }

    [Fact(DisplayName = "an unexpected response shape surfaces as UpstreamError, not a crash")]
    public async Task GetAccounts_OnMalformedBody_ThrowsUpstreamError()
    {
        var handler = new StubHttpHandler(_ =>
            StubHttpHandler.Json(HttpStatusCode.OK, """{"records":[{"Id":"001A","Name":"Acme","LastModifiedDate":"not-a-date"}]}"""));
        var (connector, _) = CreateConnector(handler);

        var act = () => connector.GetAccountsAsync();

        (await act.Should().ThrowAsync<SalesforceApiException>())
            .Which.Failure.Should().Be(SalesforceFailure.UpstreamError);
    }

    // Real Salesforce create envelope, as returned by POST /sobjects/Lead.
    private const string LeadCreatedJson = """{"id":"00QA000001abcDE","success":true,"errors":[]}""";

    private static readonly CreateSalesforceLeadRequest SampleLead = new(
        "Sample-abc123", "Integration Dashboard", "Dashboard", "sample-abc123@example.com");

    [Fact(DisplayName = "the Lead create POSTs to the session's instance_url with the session's bearer token")]
    public async Task CreateLead_PostsToSobjectsLeadWithBearerToken()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.Created, LeadCreatedJson));
        var (connector, _) = CreateConnector(handler);

        await connector.CreateLeadAsync(SampleLead);

        var (request, body) = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.ToString().Should().Be($"{InstanceUrl}/services/data/v66.0/sobjects/Lead");
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be(AccessToken);
        request.Options.TryGetValue(IntegrationCallOptions.EndpointName, out var endpointName)
            .Should().BeTrue();
        endpointName.Should().Be("create-lead");
        body.Should().Contain("\"LastName\":\"Sample-abc123\"")
            .And.Contain("\"Company\":\"Integration Dashboard\"")
            .And.Contain("\"FirstName\":\"Dashboard\"")
            .And.Contain("\"Email\":\"sample-abc123@example.com\"");
    }

    [Fact(DisplayName = "unset optional fields are omitted from the payload, not sent as explicit nulls")]
    public async Task CreateLead_OmitsNullOptionalFields()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.Created, LeadCreatedJson));
        var (connector, _) = CreateConnector(handler);

        await connector.CreateLeadAsync(new CreateSalesforceLeadRequest("Sample-abc123", "Integration Dashboard"));

        // Salesforce treats an explicit null as a field write, so absence matters.
        handler.Requests.Single().Body.Should().NotContain("FirstName").And.NotContain("Email");
    }

    [Fact(DisplayName = "a created Lead maps to a clean DTO — the success/errors envelope never leaks")]
    public async Task CreateLead_MapsCreatedIdToDto()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.Created, LeadCreatedJson));
        var (connector, _) = CreateConnector(handler);

        var created = await connector.CreateLeadAsync(SampleLead);

        created.Should().Be(new SalesforceLeadCreatedDto("00QA000001abcDE"));
    }

    [Fact(DisplayName = "a 401 invalidates the cached session and retries once with the same body")]
    public async Task CreateLead_On401_InvalidatesAndRetriesOnceWithSameBody()
    {
        var calls = 0;
        var handler = new StubHttpHandler(_ => ++calls == 1
            ? StubHttpHandler.Json(HttpStatusCode.Unauthorized, """[{"errorCode":"INVALID_SESSION_ID"}]""")
            : StubHttpHandler.Json(HttpStatusCode.Created, LeadCreatedJson));
        var (connector, tokenProvider) = CreateConnector(handler);

        var created = await connector.CreateLeadAsync(SampleLead);

        created.Id.Should().Be("00QA000001abcDE");
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].Body.Should().Be(handler.Requests[0].Body,
            "the retry must re-send the full payload");
        tokenProvider.Received(1).Invalidate();
    }

    [Fact(DisplayName = "a second consecutive 401 gives up with AuthFailed — no retry loop")]
    public async Task CreateLead_OnRepeated401_ThrowsAuthFailedAfterExactlyTwoAttempts()
    {
        var handler = new StubHttpHandler(_ =>
            StubHttpHandler.Json(HttpStatusCode.Unauthorized, """[{"errorCode":"INVALID_SESSION_ID"}]"""));
        var (connector, _) = CreateConnector(handler);

        var act = () => connector.CreateLeadAsync(SampleLead);

        (await act.Should().ThrowAsync<SalesforceApiException>())
            .Which.Failure.Should().Be(SalesforceFailure.AuthFailed);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact(DisplayName = "a Salesforce 400 (org validation) surfaces as UpstreamError — we pre-validate, so it's config drift")]
    public async Task CreateLead_OnSalesforce400_ThrowsUpstreamError()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.BadRequest,
            """[{"message":"Required fields are missing: [Company]","errorCode":"REQUIRED_FIELD_MISSING","fields":["Company"]}]"""));
        var (connector, _) = CreateConnector(handler);

        var act = () => connector.CreateLeadAsync(SampleLead);

        var ex = (await act.Should().ThrowAsync<SalesforceApiException>()).Which;
        ex.Failure.Should().Be(SalesforceFailure.UpstreamError);
        ex.Message.Should().Contain("400");
    }

    [Fact(DisplayName = "an HttpClient timeout on create surfaces as Timeout")]
    public async Task CreateLead_WhenRequestTimesOut_ThrowsTimeout()
    {
        var handler = new StubHttpHandler(_ => throw new TaskCanceledException());
        var (connector, _) = CreateConnector(handler);

        var act = () => connector.CreateLeadAsync(SampleLead);

        (await act.Should().ThrowAsync<SalesforceApiException>())
            .Which.Failure.Should().Be(SalesforceFailure.Timeout);
    }

    [Fact(DisplayName = "a network failure on create surfaces as UpstreamError")]
    public async Task CreateLead_WhenNetworkFails_ThrowsUpstreamError()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("connection refused"));
        var (connector, _) = CreateConnector(handler);

        var act = () => connector.CreateLeadAsync(SampleLead);

        (await act.Should().ThrowAsync<SalesforceApiException>())
            .Which.Failure.Should().Be(SalesforceFailure.UpstreamError);
    }

    [Fact(DisplayName = "a 201 without an id surfaces as UpstreamError, not a crash")]
    public async Task CreateLead_OnMalformedBody_ThrowsUpstreamError()
    {
        var handler = new StubHttpHandler(_ =>
            StubHttpHandler.Json(HttpStatusCode.Created, """{"success":true,"errors":[]}"""));
        var (connector, _) = CreateConnector(handler);

        var act = () => connector.CreateLeadAsync(SampleLead);

        (await act.Should().ThrowAsync<SalesforceApiException>())
            .Which.Failure.Should().Be(SalesforceFailure.UpstreamError);
    }
}
