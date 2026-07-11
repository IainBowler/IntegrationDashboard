using System.Text.RegularExpressions;

namespace Api.Services.IntegrationCalls;

/// <summary>
/// Strips credential material from payloads before they are persisted to
/// dbo.IntegrationCall. Note that HTTP headers are never stored at all (only
/// method, URL and bodies), so the Authorization header cannot leak through a
/// redaction gap — these rules are the body-level defence: named credential
/// fields plus catch-alls for anything token-shaped (bearer values, PEM
/// blocks, JWTs).
/// </summary>
public static partial class IntegrationCallRedactor
{
    private const string Marker = "[REDACTED]";

    public static string? Redact(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return payload;
        }

        var result = FormFieldValue().Replace(payload, Marker);
        result = JsonPropertyValue().Replace(result, $"$1{Marker}$2");
        result = BearerValue().Replace(result, $"Bearer {Marker}");
        result = PemBlock().Replace(result, Marker);
        result = JwtShaped().Replace(result, Marker);
        return result;
    }

    // application/x-www-form-urlencoded values of known credential fields
    [GeneratedRegex(@"(?<=(?:^|&)(?:assertion|client_assertion|client_secret|refresh_token|password|code)=)[^&]+")]
    private static partial Regex FormFieldValue();

    // string values of known credential JSON properties
    [GeneratedRegex("""("(?:access_token|refresh_token|id_token|assertion|client_secret|signature)"\s*:\s*")[^"]*(")""")]
    private static partial Regex JsonPropertyValue();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/=]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerValue();

    [GeneratedRegex(@"-----BEGIN [A-Z ]+-----[\s\S]+?-----END [A-Z ]+-----")]
    private static partial Regex PemBlock();

    // anything JWT-shaped anywhere in the payload; runs last as the catch-all
    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+")]
    private static partial Regex JwtShaped();
}
