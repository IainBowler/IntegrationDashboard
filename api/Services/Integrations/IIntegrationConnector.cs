namespace Api.Services.Integrations;

/// <summary>
/// A connector to one external system. Data shapes are integration-specific,
/// so this contract carries only the identity; callers depend on the concrete
/// connector for typed data access.
/// </summary>
public interface IIntegrationConnector
{
    string Name { get; }
}
