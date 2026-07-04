namespace Api.Services.Auth;

/// <summary>
/// One implementation per external identity provider (Okta, Google, ...).
/// Register the implementation in Program.cs and the auth flow picks it up
/// by <see cref="Name"/> — no other code changes needed to add a provider.
/// </summary>
public interface IExternalAuthProvider
{
    string Name { get; }

    string BuildAuthorizationUrl(string state, string codeChallenge, string redirectUri);

    /// <summary>Exchanges the authorization code and returns the external
    /// user's identity, or null if the exchange fails.</summary>
    Task<ExternalUserInfo?> ExchangeCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct = default);
}
