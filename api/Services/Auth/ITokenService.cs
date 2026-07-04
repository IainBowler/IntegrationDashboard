namespace Api.Services.Auth;

public interface ITokenService
{
    (string AccessToken, int ExpiresInSeconds) CreateAccessToken(UserRecord user);
}
