using FI.Domain.Connectors;

namespace FI.Infrastructure.Connectors;

public sealed class ConnectorRegistry : IConnectorRegistry
{
    private readonly Dictionary<string, IIntegrationConnector> _integrationConnectors;
    private readonly Dictionary<string, IDeploymentConnector> _deploymentConnectors;

    public ConnectorRegistry(IEnumerable<IIntegrationConnector> integrationConnectors, IEnumerable<IDeploymentConnector> deploymentConnectors)
    {
        _integrationConnectors = integrationConnectors.ToDictionary(c => c.ProviderKey, StringComparer.OrdinalIgnoreCase);
        _deploymentConnectors = deploymentConnectors.ToDictionary(c => c.ProviderKey, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetIntegrationConnector(string providerKey, out IIntegrationConnector connector) =>
        _integrationConnectors.TryGetValue(providerKey, out connector!);

    public bool TryGetDeploymentConnector(string providerKey, out IDeploymentConnector connector) =>
        _deploymentConnectors.TryGetValue(providerKey, out connector!);
}
