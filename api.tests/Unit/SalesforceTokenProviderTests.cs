using System.Net;
using System.Security.Cryptography;
using Api.Options;
using Api.Services.IntegrationCalls;
using Api.Services.Integrations.Salesforce;
using Api.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Tests.Unit;

public class SalesforceTokenProviderTests
{
    private const string LoginUrl = "https://login.salesforce.com";
    private const string ClientId = "test-consumer-key";
    private const string Username = "integration@example.com";

    // One key pair for the whole class: the provider signs with the PEM,
    // tests verify with the same key.
    private static readonly RSA TestRsa = RSA.Create(2048);
    private static readonly string TestPrivateKeyPem = TestRsa.ExportPkcs8PrivateKeyPem();

    private const string HappyTokenJson =
        """{"access_token":"sf-access-token","instance_url":"https://myorg.my.salesforce.com","token_type":"Bearer"}""";

    private static SalesforceTokenProvider CreateProvider(
        StubHttpHandler handler,
        string? privateKey = null,
        IMemoryCache? cache = null) =>
        new(new HttpClient(handler),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(new SalesforceOptions
            {
                LoginUrl = LoginUrl,
                ClientId = ClientId,
                Username = Username,
                PrivateKey = privateKey ?? TestPrivateKeyPem,
            }),
            NullLogger<SalesforceTokenProvider>.Instance);

    private static StubHttpHandler HappyPathHandler() =>
        new(_ => StubHttpHandler.Json(HttpStatusCode.OK, HappyTokenJson));

    private static string FormValue(string body, string key) =>
        body.Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(kv => kv[0] == key)
            .Select(kv => WebUtility.UrlDecode(kv[1]))
            .Single();

    [Fact(DisplayName = "the exchange posts a jwt-bearer grant to the login URL's token endpoint")]
    public async Task GetSession_PostsJwtBearerGrantToTokenEndpoint()
    {
        var handler = HappyPathHandler();

        await CreateProvider(handler).GetSessionAsync();

        var (request, body) = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.ToString().Should().Be($"{LoginUrl}/services/oauth2/token");
        FormValue(body!, "grant_type").Should().Be("urn:ietf:params:oauth:grant-type:jwt-bearer");
    }

    [Fact(DisplayName = "the exchange request is tagged as the 'token' endpoint for call auditing")]
    public async Task GetSession_TagsRequestWithTokenEndpointName()
    {
        var handler = HappyPathHandler();

        await CreateProvider(handler).GetSessionAsync();

        var request = handler.Requests.Single().Request;
        request.Options.TryGetValue(IntegrationCallOptions.EndpointName, out var endpointName)
            .Should().BeTrue();
        endpointName.Should().Be("token");
    }

    [Fact(DisplayName = "the assertion is an RS256 JWT with iss=ClientId, sub=Username, aud=LoginUrl, signed by the configured key")]
    public async Task GetSession_SignsAValidAssertion()
    {
        var handler = HappyPathHandler();

        await CreateProvider(handler).GetSessionAsync();

        var assertion = FormValue(handler.Requests.Single().Body!, "assertion");
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(assertion,
            new TokenValidationParameters
            {
                ValidIssuer = ClientId,
                ValidAudience = LoginUrl,
                IssuerSigningKey = new RsaSecurityKey(TestRsa),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            });
        result.IsValid.Should().BeTrue();

        var token = new JsonWebTokenHandler().ReadJsonWebToken(assertion);
        token.Subject.Should().Be(Username);
        token.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(3), TimeSpan.FromMinutes(1));
    }

    [Fact(DisplayName = "a successful exchange yields the access token and the org's instance_url")]
    public async Task GetSession_ReturnsAccessTokenAndInstanceUrl()
    {
        var session = await CreateProvider(HappyPathHandler()).GetSessionAsync();

        session.Should().Be(new SalesforceSession("sf-access-token", "https://myorg.my.salesforce.com"));
    }

    [Fact(DisplayName = "the session is cached — a second call makes no HTTP request")]
    public async Task GetSession_CachesSession()
    {
        var handler = HappyPathHandler();
        var provider = CreateProvider(handler);

        var first = await provider.GetSessionAsync();
        var second = await provider.GetSessionAsync();

        second.Should().Be(first);
        handler.Requests.Should().HaveCount(1);
    }

    [Fact(DisplayName = "Invalidate drops the cached session so the next call re-authenticates")]
    public async Task Invalidate_ForcesReExchange()
    {
        var handler = HappyPathHandler();
        var provider = CreateProvider(handler);

        await provider.GetSessionAsync();
        provider.Invalidate();
        await provider.GetSessionAsync();

        handler.Requests.Should().HaveCount(2);
    }

    [Fact(DisplayName = "a rejected exchange surfaces AuthFailed with Salesforce's error code")]
    public async Task GetSession_WhenExchangeRejected_ThrowsAuthFailed()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"user hasn't approved this consumer"}"""));

        var act = () => CreateProvider(handler).GetSessionAsync();

        var ex = (await act.Should().ThrowAsync<SalesforceApiException>()).Which;
        ex.Failure.Should().Be(SalesforceFailure.AuthFailed);
        ex.Message.Should().Contain("invalid_grant");
    }

    [Fact(DisplayName = "missing configuration fails fast without calling Salesforce")]
    public async Task GetSession_WithoutConfiguration_ThrowsBeforeAnyHttpCall()
    {
        var handler = HappyPathHandler();

        var act = () => CreateProvider(handler, privateKey: "").GetSessionAsync();

        (await act.Should().ThrowAsync<SalesforceApiException>())
            .Which.Failure.Should().Be(SalesforceFailure.AuthFailed);
        handler.Requests.Should().BeEmpty();
    }

    [Fact(DisplayName = "an invalid PEM surfaces AuthFailed without echoing key material")]
    public async Task GetSession_WithInvalidPem_ThrowsAuthFailedWithoutKeyMaterial()
    {
        var handler = HappyPathHandler();
        const string badKey = "-----BEGIN PRIVATE KEY-----\nnot-a-real-key\n-----END PRIVATE KEY-----";

        var act = () => CreateProvider(handler, privateKey: badKey).GetSessionAsync();

        var ex = (await act.Should().ThrowAsync<SalesforceApiException>()).Which;
        ex.Failure.Should().Be(SalesforceFailure.AuthFailed);
        ex.Message.Should().NotContain("not-a-real-key");
        handler.Requests.Should().BeEmpty();
    }

    [Fact(DisplayName = "a token response missing instance_url surfaces UpstreamError")]
    public async Task GetSession_WhenInstanceUrlMissing_ThrowsUpstreamError()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.OK,
            """{"access_token":"sf-access-token"}"""));

        var act = () => CreateProvider(handler).GetSessionAsync();

        (await act.Should().ThrowAsync<SalesforceApiException>())
            .Which.Failure.Should().Be(SalesforceFailure.UpstreamError);
    }

    [Fact(DisplayName = "an HttpClient timeout surfaces as Timeout")]
    public async Task GetSession_WhenRequestTimesOut_ThrowsTimeout()
    {
        var handler = new StubHttpHandler(_ => throw new TaskCanceledException());

        var act = () => CreateProvider(handler).GetSessionAsync();

        (await act.Should().ThrowAsync<SalesforceApiException>())
            .Which.Failure.Should().Be(SalesforceFailure.Timeout);
    }

    [Fact(DisplayName = "a network failure surfaces as UpstreamError")]
    public async Task GetSession_WhenNetworkFails_ThrowsUpstreamError()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("connection refused"));

        var act = () => CreateProvider(handler).GetSessionAsync();

        (await act.Should().ThrowAsync<SalesforceApiException>())
            .Which.Failure.Should().Be(SalesforceFailure.UpstreamError);
    }
}
