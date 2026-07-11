namespace Api.Services.Integrations.Salesforce;

public enum SalesforceFailure
{
    AuthFailed,
    UpstreamError,
    Timeout,
}

/// <summary>
/// The single failure surface of the Salesforce integration. Messages are safe
/// to return to callers as ProblemDetails: they carry Salesforce error codes
/// and HTTP statuses, never tokens or key material.
/// </summary>
public class SalesforceApiException(SalesforceFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public SalesforceFailure Failure { get; } = failure;
}
