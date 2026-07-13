using Api.Services.IntegrationCalls;
using FluentAssertions;

namespace Api.Tests.Unit;

public class IntegrationCallRedactorTests
{
    private const string Jwt = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJjbGllbnQifQ.c2lnbmF0dXJl";

    [Fact(DisplayName = "the assertion form field is redacted while other fields survive")]
    public void Redact_FormBody_RedactsAssertionOnly()
    {
        var body = $"grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer&assertion={Jwt}";

        var result = IntegrationCallRedactor.Redact(body);

        result.Should().Be("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer&assertion=[REDACTED]");
    }

    [Fact(DisplayName = "client_secret and refresh_token form fields are redacted")]
    public void Redact_FormBody_RedactsOtherCredentialFields()
    {
        var result = IntegrationCallRedactor.Redact(
            "client_id=abc&client_secret=s3cret&refresh_token=rt-1&code=auth-code-1");

        result.Should().Be("client_id=abc&client_secret=[REDACTED]&refresh_token=[REDACTED]&code=[REDACTED]");
    }

    [Fact(DisplayName = "token JSON properties are redacted while instance_url survives")]
    public void Redact_TokenResponseJson_RedactsTokensOnly()
    {
        var body = """{"access_token":"00Dxx!AQEAQ","instance_url":"https://myorg.my.salesforce.com","id_token":"abc","refresh_token":"rt","token_type":"Bearer","signature":"sig=="}""";

        var result = IntegrationCallRedactor.Redact(body);

        result.Should().Be(
            """{"access_token":"[REDACTED]","instance_url":"https://myorg.my.salesforce.com","id_token":"[REDACTED]","refresh_token":"[REDACTED]","token_type":"Bearer","signature":"[REDACTED]"}""");
    }

    [Fact(DisplayName = "bearer values in free text are redacted")]
    public void Redact_BearerValue_IsRedacted()
    {
        IntegrationCallRedactor.Redact("Authorization: Bearer abc123.def")
            .Should().Be("Authorization: Bearer [REDACTED]");
    }

    [Fact(DisplayName = "a bare JWT anywhere in the payload is caught by the catch-all")]
    public void Redact_BareJwt_IsRedacted()
    {
        IntegrationCallRedactor.Redact($"some text {Jwt} more text")
            .Should().Be("some text [REDACTED] more text");
    }

    [Fact(DisplayName = "PEM blocks are redacted whole")]
    public void Redact_PemBlock_IsRedacted()
    {
        var pem = "-----BEGIN RSA PRIVATE KEY-----\nMIIEow==\n-----END RSA PRIVATE KEY-----";

        IntegrationCallRedactor.Redact($"key: {pem} end")
            .Should().Be("key: [REDACTED] end");
    }

    [Fact(DisplayName = "an innocent account payload passes through unchanged")]
    public void Redact_AccountJson_IsUntouched()
    {
        var body = """{"totalSize":1,"records":[{"Id":"001A","Name":"Acme","Industry":"Technology","Website":"https://acme.example","LastModifiedDate":"2026-07-01T12:34:56.000+0000"}]}""";

        IntegrationCallRedactor.Redact(body).Should().Be(body);
    }

    [Fact(DisplayName = "a lead payload passes through unchanged — business data (incl. email) is stored as-is")]
    public void Redact_LeadJson_IsUntouched()
    {
        var body = """{"lastName":"Sample-abc123","company":"Integration Dashboard","firstName":"Dashboard","email":"sample-abc123@example.com"}""";

        IntegrationCallRedactor.Redact(body).Should().Be(body);
    }

    [Theory(DisplayName = "null and empty payloads pass through as-is")]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_NullOrEmpty_PassesThrough(string? payload)
    {
        IntegrationCallRedactor.Redact(payload).Should().Be(payload);
    }
}
