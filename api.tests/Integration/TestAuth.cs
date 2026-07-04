using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Tests.Integration;

/// <summary>
/// Shared JWT/auth settings for integration tests, plus a helper that mints
/// real HS256 tokens with the test key so protected endpoints are exercised
/// through the actual JwtBearer middleware rather than a stub auth handler.
/// </summary>
public static class TestAuth
{
    public const string SigningKey = "integration-test-signing-key-0123456789";
    public const string Issuer = "TestIssuer";
    public const string Audience = "TestAudience";
    public const string FrontendBaseUrl = "http://localhost:5173";
    public const string ApiBaseUrl = "https://api.test";

    public static WebApplicationFactory<Program> WithAuthSettings(
        this WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Jwt:Issuer", Issuer);
            builder.UseSetting("Jwt:Audience", Audience);
            builder.UseSetting("Auth:FrontendBaseUrl", FrontendBaseUrl);
            builder.UseSetting("Auth:ApiBaseUrl", ApiBaseUrl);
        });

    public static string MintAccessToken(
        long userId = 1,
        string provider = "okta",
        string? email = "user@example.com",
        string? name = "Test User",
        string? signingKey = null,
        bool expired = false)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = userId.ToString(),
            ["provider"] = provider,
        };
        if (email is not null)
        {
            claims["email"] = email;
        }
        if (name is not null)
        {
            claims["name"] = name;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            NotBefore = DateTime.UtcNow.AddMinutes(-10),
            IssuedAt = DateTime.UtcNow.AddMinutes(-10),
            Expires = expired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(15),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
    }
}
