using Api.Contracts;

namespace Api.Services.Auth;

public interface IAuthFlowService
{
    /// <summary>Returns the external provider's authorization URL to redirect
    /// the browser to, or null for an unknown provider.</summary>
    string? BeginLogin(string providerName);

    /// <summary>Completes the OIDC callback: validates state, exchanges the
    /// code, upserts the user, and returns a one-time handoff code for the
    /// SPA. Null on any failure.</summary>
    Task<string?> HandleCallbackAsync(
        string providerName, string code, string state, CancellationToken ct = default);

    Task<TokenResponse?> ExchangeHandoffCodeAsync(string code);

    Task<TokenResponse?> RefreshAsync(string refreshToken);

    Task LogoutAsync(string refreshToken);
}
