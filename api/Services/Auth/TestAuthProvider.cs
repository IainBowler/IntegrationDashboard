using Microsoft.AspNetCore.WebUtilities;

namespace Api.Services.Auth;

/// <summary>
/// A fake identity provider for end-to-end tests: instead of redirecting to
/// an external IdP it bounces straight back to our own callback, so Playwright
/// can exercise the entire login flow (state validation, code exchange, user
/// upsert, handoff code, JWT minting) without Okta credentials in CI.
///
/// SECURITY: never available in production — Program.cs registers it only in
/// the Development environment AND when Auth:EnableTestProvider is true.
/// </summary>
public class TestAuthProvider : IExternalAuthProvider
{
    internal const string AuthorizationCode = "test-code";

    public string Name => "test";

    public string BuildAuthorizationUrl(string state, string codeChallenge, string redirectUri) =>
        QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?>
        {
            ["code"] = AuthorizationCode,
            ["state"] = state,
        });

    public Task<ExternalUserInfo?> ExchangeCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct = default) =>
        Task.FromResult<ExternalUserInfo?>(code == AuthorizationCode
            ? new ExternalUserInfo(Name, "e2e-subject", "e2e@example.com", "E2E Test User")
            : null);
}
