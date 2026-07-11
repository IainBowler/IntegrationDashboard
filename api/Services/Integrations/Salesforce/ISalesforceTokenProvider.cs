namespace Api.Services.Integrations.Salesforce;

public interface ISalesforceTokenProvider
{
    Task<SalesforceSession> GetSessionAsync(CancellationToken ct = default);

    /// <summary>Drops the cached session so the next call re-authenticates.</summary>
    void Invalidate();
}
