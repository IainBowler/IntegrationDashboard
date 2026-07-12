using Api.Contracts;

namespace Api.Services.Integrations;

public interface IIntegrationDirectoryService
{
    Task<IReadOnlyList<IntegrationResponse>> GetIntegrationsAsync();

    /// <summary>Null when no integration with that name exists (drives the 404).</summary>
    Task<IntegrationStatisticsResponse?> GetStatisticsAsync(string integrationName);
}
