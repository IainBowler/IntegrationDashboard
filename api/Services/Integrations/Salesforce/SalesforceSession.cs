namespace Api.Services.Integrations.Salesforce;

/// <summary>
/// The instance_url from the token response is the base for all data calls —
/// it is org-specific and must never be hardcoded.
/// </summary>
public sealed record SalesforceSession(string AccessToken, string InstanceUrl);
