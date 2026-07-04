using System.Text;
using Api.Options;
using Api.Services.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Tests.Unit;

public class JwtTokenServiceTests
{
    private const string SigningKey = "unit-test-signing-key-0123456789abcdef";

    private static readonly JwtOptions Options = new()
    {
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        SigningKey = SigningKey,
        AccessTokenMinutes = 15,
    };

    private static readonly UserRecord User = new(42, "okta", "sub-42", "user@example.com", "Test User");

    private static JwtTokenService CreateService(JwtOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? Options));

    [Fact]
    public void CreateAccessToken_ReturnsLifetimeInSeconds()
    {
        var (_, expiresInSeconds) = CreateService().CreateAccessToken(User);

        expiresInSeconds.Should().Be(15 * 60);
    }

    [Fact]
    public void CreateAccessToken_IncludesUserClaims()
    {
        var (accessToken, _) = CreateService().CreateAccessToken(User);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
        token.GetClaim("sub").Value.Should().Be("42");
        token.GetClaim("provider").Value.Should().Be("okta");
        token.GetClaim("email").Value.Should().Be("user@example.com");
        token.GetClaim("name").Value.Should().Be("Test User");
        token.Issuer.Should().Be("TestIssuer");
        token.Audiences.Should().ContainSingle().Which.Should().Be("TestAudience");
    }

    [Fact]
    public void CreateAccessToken_OmitsMissingOptionalClaims()
    {
        var (accessToken, _) = CreateService().CreateAccessToken(
            new UserRecord(1, "okta", "sub-1", null, null));

        var token = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
        token.TryGetClaim("email", out _).Should().BeFalse();
        token.TryGetClaim("name", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CreateAccessToken_ValidatesWithSameKey()
    {
        var (accessToken, _) = CreateService().CreateAccessToken(User);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            accessToken, ValidationParameters(SigningKey));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAccessToken_FailsValidationWithWrongKey()
    {
        var (accessToken, _) = CreateService().CreateAccessToken(User);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            accessToken, ValidationParameters("wrong-signing-key-0123456789abcdef00"));

        result.IsValid.Should().BeFalse();
    }

    private static TokenValidationParameters ValidationParameters(string key) => new()
    {
        ValidIssuer = "TestIssuer",
        ValidAudience = "TestAudience",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
    };
}
