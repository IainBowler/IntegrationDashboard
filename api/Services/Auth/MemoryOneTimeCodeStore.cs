using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace Api.Services.Auth;

/// <summary>
/// In-memory store: fine while the API runs as a single instance — codes are
/// short-lived, so a restart mid-login only forces a fresh login. Move to a
/// shared store (database/Redis) before scaling out.
/// </summary>
public class MemoryOneTimeCodeStore(IMemoryCache cache) : IOneTimeCodeStore
{
    public string Issue(string purpose, string payload, TimeSpan ttl)
    {
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        cache.Set(CacheKey(purpose, code), payload, ttl);
        return code;
    }

    public string? Redeem(string purpose, string code)
    {
        var key = CacheKey(purpose, code);
        if (!cache.TryGetValue(key, out string? payload))
        {
            return null;
        }
        cache.Remove(key);
        return payload;
    }

    private static string CacheKey(string purpose, string code) => $"otc:{purpose}:{code}";
}
