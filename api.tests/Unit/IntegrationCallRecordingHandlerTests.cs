using System.Diagnostics;
using System.Net;
using Api.Services.IntegrationCalls;
using Api.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Api.Tests.Unit;

public class IntegrationCallRecordingHandlerTests
{
    private const string TokenUrl = "https://login.salesforce.com/services/oauth2/token";
    private const string TokenResponseJson =
        """{"access_token":"00Dxx!secret","instance_url":"https://myorg.my.salesforce.com"}""";

    private static (HttpClient Client, List<IntegrationCallRecord> Saved, IIntegrationCallService Service)
        CreateClient(StubHttpHandler inner)
    {
        var saved = new List<IntegrationCallRecord>();
        var service = Substitute.For<IIntegrationCallService>();
        service.SaveAsync(Arg.Do<IntegrationCallRecord>(saved.Add)).Returns(Task.CompletedTask);
        var recorder = new IntegrationCallRecorder(service, NullLogger<IntegrationCallRecorder>.Instance);
        var handler = new IntegrationCallRecordingHandler(recorder, "salesforce") { InnerHandler = inner };
        return (new HttpClient(handler), saved, service);
    }

    private static HttpRequestMessage TokenExchangeRequest() => new(HttpMethod.Post, TokenUrl)
    {
        Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = "eyJhbGci.eyJpc3M.c2ln",
        }),
    };

    [Fact(DisplayName = "a successful call is recorded with redacted bodies, status, and duration")]
    public async Task Send_Success_RecordsRedactedOutboundRow()
    {
        var (client, saved, _) = CreateClient(
            new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.OK, TokenResponseJson)));

        await client.SendAsync(TokenExchangeRequest());

        var record = saved.Should().ContainSingle().Which;
        record.Direction.Should().Be(IntegrationCallDirection.Outbound);
        record.IntegrationName.Should().Be("salesforce");
        record.Method.Should().Be("POST");
        record.Url.Should().Be(TokenUrl);
        record.StatusCode.Should().Be(200);
        record.DurationMs.Should().BeGreaterThanOrEqualTo(0);
        record.Error.Should().BeNull();
        record.RequestBody.Should().Contain("grant_type").And.Contain("[REDACTED]");
        record.RequestBody.Should().NotContain("eyJ");
        record.ResponseBody.Should().Contain("\"access_token\":\"[REDACTED]\"");
        record.ResponseBody.Should().Contain("instance_url");
    }

    [Fact(DisplayName = "the caller can still read the response body after recording")]
    public async Task Send_Success_ResponseBodyRemainsReadable()
    {
        var (client, _, _) = CreateClient(
            new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.OK, TokenResponseJson)));

        var response = await client.SendAsync(TokenExchangeRequest());

        (await response.Content.ReadAsStringAsync()).Should().Be(TokenResponseJson);
    }

    [Fact(DisplayName = "a timeout is recorded with no status code and rethrown")]
    public async Task Send_Timeout_RecordsRowAndRethrows()
    {
        var (client, saved, _) = CreateClient(new StubHttpHandler(_ => throw new TaskCanceledException()));

        var act = () => client.SendAsync(TokenExchangeRequest());

        await act.Should().ThrowAsync<TaskCanceledException>();
        var record = saved.Should().ContainSingle().Which;
        record.StatusCode.Should().BeNull();
        record.Error.Should().Contain("TaskCanceledException");
        record.RequestBody.Should().Contain("[REDACTED]");
    }

    [Fact(DisplayName = "a failing save does not break the call")]
    public async Task Send_WhenSaveFails_StillReturnsResponse()
    {
        var service = Substitute.For<IIntegrationCallService>();
        service.SaveAsync(Arg.Any<IntegrationCallRecord>())
            .Returns<Task>(_ => throw new InvalidOperationException("db down"));
        var recorder = new IntegrationCallRecorder(service, NullLogger<IntegrationCallRecorder>.Instance);
        var handler = new IntegrationCallRecordingHandler(recorder, "salesforce")
        {
            InnerHandler = new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.OK, TokenResponseJson)),
        };
        var client = new HttpClient(handler);

        var response = await client.SendAsync(TokenExchangeRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "the ambient activity's trace id becomes the correlation id")]
    public async Task Send_WithAmbientActivity_RecordsTraceIdAsCorrelationId()
    {
        var (client, saved, _) = CreateClient(
            new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.OK, TokenResponseJson)));
        using var activity = new Activity("test").Start();

        await client.SendAsync(TokenExchangeRequest());

        saved.Single().CorrelationId.Should().Be(activity.TraceId.ToString());
    }
}
