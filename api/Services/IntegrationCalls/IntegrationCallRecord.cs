namespace Api.Services.IntegrationCalls;

public enum IntegrationCallDirection
{
    Inbound,
    Outbound,
}

/// <summary>
/// One audited integration call. Bodies here may still contain sensitive
/// values — <see cref="IntegrationCallRecorder"/> redacts them before they
/// reach storage. StatusCode null + Error set means a transport-level failure
/// (timeout, DNS) where no HTTP response existed.
/// </summary>
public sealed record IntegrationCallRecord(
    IntegrationCallDirection Direction,
    string IntegrationName,
    string? CorrelationId,
    long? UserId,
    string Method,
    string Url,
    int? StatusCode,
    int DurationMs,
    string? RequestBody,
    string? ResponseBody,
    string? Error);
