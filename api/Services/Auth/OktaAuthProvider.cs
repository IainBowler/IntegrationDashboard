using System.Text.Json;
using Api.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Api.Services.Auth;

public class OktaAuthProvider(HttpClient httpClient, IOptions<OktaOptions> options) : IExternalAuthProvider
{
    public string Name => "okta";

    public string BuildAuthorizationUrl(string state, string codeChallenge, string redirectUri)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = options.Value.ClientId,
            ["response_type"] = "code",
            ["scope"] = "openid profile email",
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        return QueryHelpers.AddQueryString($"{options.Value.Issuer}/v1/authorize", query);
    }

    public async Task<ExternalUserInfo?> ExchangeCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct = default)
    {
        var tokenResponse = await httpClient.PostAsync(
            $"{options.Value.Issuer}/v1/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier,
                ["client_id"] = options.Value.ClientId,
                ["client_secret"] = options.Value.ClientSecret,
            }),
            ct);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            return null;
        }

        string? accessToken;
        using (var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct)))
        {
            accessToken = tokenJson.RootElement.TryGetProperty("access_token", out var tokenElement)
                ? tokenElement.GetString()
                : null;
        }
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        // Tokens arrive directly from Okta's token endpoint over TLS with
        // client-secret auth, so /userinfo stands in for id_token validation.
        using var userInfoRequest = new HttpRequestMessage(
            HttpMethod.Get, $"{options.Value.Issuer}/v1/userinfo");
        userInfoRequest.Headers.Authorization = new("Bearer", accessToken);
        var userInfoResponse = await httpClient.SendAsync(userInfoRequest, ct);
        if (!userInfoResponse.IsSuccessStatusCode)
        {
            return null;
        }

        using var userJson = JsonDocument.Parse(await userInfoResponse.Content.ReadAsStringAsync(ct));
        var root = userJson.RootElement;
        var subject = root.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
        if (string.IsNullOrEmpty(subject))
        {
            return null;
        }

        return new ExternalUserInfo(
            Name,
            subject,
            root.TryGetProperty("email", out var email) ? email.GetString() : null,
            root.TryGetProperty("name", out var name) ? name.GetString() : null);
    }
}
