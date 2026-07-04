namespace Api.Services.Auth;

/// <summary>
/// Issues opaque single-use codes bound to a purpose (OIDC state, SPA handoff)
/// with a payload that is returned exactly once on redemption.
/// </summary>
public interface IOneTimeCodeStore
{
    string Issue(string purpose, string payload, TimeSpan ttl);

    /// <summary>Returns the payload and invalidates the code, or null if the
    /// code is unknown, expired, already redeemed, or for another purpose.</summary>
    string? Redeem(string purpose, string code);
}
