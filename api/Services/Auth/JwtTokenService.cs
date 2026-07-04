using System.Text;
using Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Services.Auth;

public class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    private static readonly JsonWebTokenHandler Handler = new();

    public (string AccessToken, int ExpiresInSeconds) CreateAccessToken(UserRecord user)
    {
        var jwt = options.Value;
        var lifetime = TimeSpan.FromMinutes(jwt.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.UserId.ToString(),
            ["provider"] = user.Provider,
        };
        if (user.Email is not null)
        {
            claims[JwtRegisteredClaimNames.Email] = user.Email;
        }
        if (user.DisplayName is not null)
        {
            claims[JwtRegisteredClaimNames.Name] = user.DisplayName;
        }

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Expires = DateTime.UtcNow.Add(lifetime),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
        return (token, (int)lifetime.TotalSeconds);
    }
}
