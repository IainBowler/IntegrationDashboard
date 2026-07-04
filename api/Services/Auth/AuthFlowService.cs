using System.Security.Cryptography;
using System.Text;
using Api.Contracts;
using Api.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Api.Services.Auth;

public class AuthFlowService(
    IEnumerable<IExternalAuthProvider> providers,
    IOneTimeCodeStore codeStore,
    ITokenService tokenService,
    IUserService userService,
    IRefreshTokenService refreshTokenService,
    IOptions<AuthOptions> authOptions) : IAuthFlowService
{
    private const string StatePurpose = "state";
    private const string HandoffPurpose = "handoff";
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HandoffTtl = TimeSpan.FromSeconds(60);

    public string? BeginLogin(string providerName)
    {
        var provider = FindProvider(providerName);
        if (provider is null)
        {
            return null;
        }

        var codeVerifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        // The PKCE verifier rides in the state entry so the callback can
        // recover it; redeeming the state also proves the callback matches
        // a login this server started (CSRF protection).
        var state = codeStore.Issue(StatePurpose, $"{provider.Name}|{codeVerifier}", StateTtl);
        return provider.BuildAuthorizationUrl(state, codeChallenge, RedirectUri(provider.Name));
    }

    public async Task<string?> HandleCallbackAsync(
        string providerName, string code, string state, CancellationToken ct = default)
    {
        var provider = FindProvider(providerName);
        if (provider is null)
        {
            return null;
        }

        var statePayload = codeStore.Redeem(StatePurpose, state);
        if (statePayload is null)
        {
            return null;
        }
        var separator = statePayload.IndexOf('|');
        if (separator < 0 || statePayload[..separator] != provider.Name)
        {
            return null;
        }
        var codeVerifier = statePayload[(separator + 1)..];

        var externalUser = await provider.ExchangeCodeAsync(
            code, codeVerifier, RedirectUri(provider.Name), ct);
        if (externalUser is null)
        {
            return null;
        }

        var user = await userService.UpsertExternalUserAsync(externalUser);
        return codeStore.Issue(HandoffPurpose, user.UserId.ToString(), HandoffTtl);
    }

    public async Task<TokenResponse?> ExchangeHandoffCodeAsync(string code)
    {
        var payload = codeStore.Redeem(HandoffPurpose, code);
        if (payload is null || !long.TryParse(payload, out var userId))
        {
            return null;
        }

        var user = await userService.GetByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        var refreshToken = await refreshTokenService.IssueAsync(user.UserId);
        return BuildTokenResponse(user, refreshToken);
    }

    public async Task<TokenResponse?> RefreshAsync(string refreshToken)
    {
        var rotated = await refreshTokenService.ValidateAndRotateAsync(refreshToken);
        if (rotated is null)
        {
            return null;
        }
        return BuildTokenResponse(rotated.Value.User, rotated.Value.NewRefreshToken);
    }

    public Task LogoutAsync(string refreshToken) => refreshTokenService.RevokeAsync(refreshToken);

    private TokenResponse BuildTokenResponse(UserRecord user, string refreshToken)
    {
        var (accessToken, expiresInSeconds) = tokenService.CreateAccessToken(user);
        return new TokenResponse(
            accessToken,
            expiresInSeconds,
            refreshToken,
            new UserResponse(user.UserId, user.Provider, user.Email, user.DisplayName));
    }

    private IExternalAuthProvider? FindProvider(string name) =>
        providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private string RedirectUri(string providerName) =>
        $"{authOptions.Value.ApiBaseUrl.TrimEnd('/')}/auth/callback/{providerName}";
}
