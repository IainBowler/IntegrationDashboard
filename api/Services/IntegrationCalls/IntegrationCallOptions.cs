namespace Api.Services.IntegrationCalls;

public static class IntegrationCallOptions
{
    /// <summary>
    /// Set on an outbound HttpRequestMessage at the call site to name the
    /// seeded dbo.IntegrationEndpoint being called; the recording handler
    /// reads it. Untagged requests are recorded with a null endpoint.
    /// </summary>
    public static readonly HttpRequestOptionsKey<string> EndpointName = new("IntegrationCall.EndpointName");
}
