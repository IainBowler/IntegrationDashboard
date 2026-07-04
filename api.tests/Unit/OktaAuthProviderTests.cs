using System.Net;
using System.Text;
using Api.Options;
using Api.Services.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Api.Tests.Unit;

public class OktaAuthProviderTests
{
    private const string Issuer = "https://dev-123.okta.com/oauth2/default";

    private static OktaAuthProvider CreateProvider(StubHttpHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(new OktaOptions
        {
            Issuer = Issuer,
            ClientId = "client-id",
            ClientSecret = "client-secret",
        }));

    [Fact]
    public void BuildAuthorizationUrl_ContainsOidcParameters()
    {
        var provider = CreateProvider(new StubHttpHandler(_ => throw new InvalidOperationException()));

        var url = provider.BuildAuthorizationUrl(
            "state-1", "challenge-1", "https://api.example/auth/callback/okta");

        url.Should().StartWith($"{Issuer}/v1/authorize?");
        url.Should().Contain("client_id=client-id");
        url.Should().Contain("response_type=code");
        url.Should().Contain("scope=openid%20profile%20email");
        url.Should().Contain("redirect_uri=https%3A%2F%2Fapi.example%2Fauth%2Fcallback%2Fokta");
        url.Should().Contain("state=state-1");
        url.Should().Contain("code_challenge=challenge-1");
        url.Should().Contain("code_challenge_method=S256");
    }

    [Fact]
    public async Task ExchangeCode_SendsCodeAndSecretToTokenEndpoint()
    {
        var handler = HappyPathHandler();
        var provider = CreateProvider(handler);

        await provider.ExchangeCodeAsync("auth-code", "verifier-1", "https://api.example/cb");

        var (tokenRequest, tokenBody) = handler.Requests[0];
        tokenRequest.Method.Should().Be(HttpMethod.Post);
        tokenRequest.RequestUri!.ToString().Should().Be($"{Issuer}/v1/token");
        tokenBody.Should().Contain("grant_type=authorization_code");
        tokenBody.Should().Contain("code=auth-code");
        tokenBody.Should().Contain("code_verifier=verifier-1");
        tokenBody.Should().Contain("client_id=client-id");
        tokenBody.Should().Contain("client_secret=client-secret");
        tokenBody.Should().Contain("redirect_uri=https%3A%2F%2Fapi.example%2Fcb");
    }

    [Fact]
    public async Task ExchangeCode_CallsUserInfoWithBearerToken()
    {
        var handler = HappyPathHandler();
        var provider = CreateProvider(handler);

        var result = await provider.ExchangeCodeAsync("auth-code", "verifier-1", "https://api.example/cb");

        var (userInfoRequest, _) = handler.Requests[1];
        userInfoRequest.RequestUri!.ToString().Should().Be($"{Issuer}/v1/userinfo");
        userInfoRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        userInfoRequest.Headers.Authorization.Parameter.Should().Be("okta-access-token");

        result.Should().Be(new ExternalUserInfo("okta", "sub-1", "user@example.com", "Test User"));
    }

    [Fact]
    public async Task ExchangeCode_WhenTokenEndpointFails_ReturnsNull()
    {
        var handler = new StubHttpHandler(_ => Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));

        var result = await CreateProvider(handler).ExchangeCodeAsync("bad", "v", "https://api.example/cb");

        result.Should().BeNull();
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExchangeCode_WhenAccessTokenMissing_ReturnsNull()
    {
        var handler = new StubHttpHandler(_ => Json(HttpStatusCode.OK, """{"token_type":"Bearer"}"""));

        var result = await CreateProvider(handler).ExchangeCodeAsync("code", "v", "https://api.example/cb");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExchangeCode_WhenUserInfoFails_ReturnsNull()
    {
        var handler = new StubHttpHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/token")
                ? Json(HttpStatusCode.OK, """{"access_token":"okta-access-token"}""")
                : Json(HttpStatusCode.Unauthorized, """{"error":"invalid_token"}"""));

        var result = await CreateProvider(handler).ExchangeCodeAsync("code", "v", "https://api.example/cb");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExchangeCode_WhenSubjectMissing_ReturnsNull()
    {
        var handler = new StubHttpHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/token")
                ? Json(HttpStatusCode.OK, """{"access_token":"okta-access-token"}""")
                : Json(HttpStatusCode.OK, """{"email":"user@example.com"}"""));

        var result = await CreateProvider(handler).ExchangeCodeAsync("code", "v", "https://api.example/cb");

        result.Should().BeNull();
    }

    private static StubHttpHandler HappyPathHandler() => new(request =>
        request.RequestUri!.AbsolutePath.EndsWith("/token")
            ? Json(HttpStatusCode.OK, """{"access_token":"okta-access-token","token_type":"Bearer"}""")
            : Json(HttpStatusCode.OK, """{"sub":"sub-1","email":"user@example.com","name":"Test User"}"""));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // capture the body eagerly — the request content is disposed later
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return responder(request);
        }
    }
}
