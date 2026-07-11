using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Services.Integrations.Salesforce;

/// <summary>
/// OAuth 2.0 JWT Bearer flow against Salesforce: signs an RS256 assertion with
/// the org certificate's private key and exchanges it at the token endpoint.
/// Sessions are cached; the JWT-bearer token response carries no expires_in
/// (validity is the org's session timeout), so the TTL is a conservative fixed
/// window with a 401-invalidate backstop in the connector. In-memory cache is
/// fine single-instance — same caveat as the one-time code store.
/// </summary>
public class SalesforceTokenProvider(
    HttpClient httpClient,
    IMemoryCache cache,
    IOptions<SalesforceOptions> options,
    ILogger<SalesforceTokenProvider> logger) : ISalesforceTokenProvider
{
    private const string CacheKey = "salesforce:session";
    private static readonly JsonWebTokenHandler Handler = new();

    public async Task<SalesforceSession> GetSessionAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out SalesforceSession? cached) && cached is not null)
        {
            return cached;
        }

        var session = await ExchangeAsync(ct);
        cache.Set(CacheKey, session, TimeSpan.FromMinutes(options.Value.TokenCacheMinutes));
        return session;
    }

    public void Invalidate() => cache.Remove(CacheKey);

    private async Task<SalesforceSession> ExchangeAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrEmpty(opts.ClientId)
            || string.IsNullOrEmpty(opts.Username)
            || string.IsNullOrEmpty(opts.PrivateKey))
        {
            throw new SalesforceApiException(SalesforceFailure.AuthFailed,
                "Salesforce integration is not configured (Salesforce:ClientId, Salesforce:Username and Salesforce:PrivateKey are required).");
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(
                $"{opts.LoginUrl}/services/oauth2/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = CreateAssertion(opts),
                }),
                ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SalesforceApiException(SalesforceFailure.Timeout,
                "Timed out contacting the Salesforce token endpoint.");
        }
        catch (HttpRequestException ex)
        {
            throw new SalesforceApiException(SalesforceFailure.UpstreamError,
                "Could not reach the Salesforce token endpoint.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = DescribeError(body, (int)response.StatusCode);
                logger.LogWarning("Salesforce token exchange failed: {Error}", error);
                throw new SalesforceApiException(SalesforceFailure.AuthFailed,
                    $"Salesforce token exchange failed: {error}");
            }

            var token = JsonSerializer.Deserialize<TokenResponse>(body);
            if (token?.AccessToken is not { Length: > 0 } || token.InstanceUrl is not { Length: > 0 })
            {
                throw new SalesforceApiException(SalesforceFailure.UpstreamError,
                    "Salesforce token response is missing access_token or instance_url.");
            }

            logger.LogInformation("Salesforce session established for instance {InstanceUrl}", token.InstanceUrl);
            return new SalesforceSession(token.AccessToken, token.InstanceUrl);
        }
    }

    private static string CreateAssertion(SalesforceOptions opts)
    {
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(opts.PrivateKey);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            // Deliberately excludes the key material and the inner exception text.
            throw new SalesforceApiException(SalesforceFailure.AuthFailed,
                "Salesforce:PrivateKey is not a valid PEM RSA private key.");
        }

        return Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = opts.ClientId,
            Audience = opts.LoginUrl,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = opts.Username,
            },
            Expires = DateTime.UtcNow.AddMinutes(3),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
            {
                // The RSA key is disposed as soon as this assertion is signed,
                // so the signature provider must not be cached across calls.
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
            },
        });
    }

    private static string DescribeError(string body, int statusCode)
    {
        // Token-endpoint errors look like {"error":"invalid_grant","error_description":"..."}
        // — no secrets, safe to log and surface.
        try
        {
            using var json = JsonDocument.Parse(body);
            var error = json.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            var description = json.RootElement.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            if (error is not null)
            {
                return description is not null ? $"{error} ({description})" : error;
            }
        }
        catch (JsonException)
        {
        }
        return $"HTTP {statusCode}";
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("instance_url")] string? InstanceUrl);
}
