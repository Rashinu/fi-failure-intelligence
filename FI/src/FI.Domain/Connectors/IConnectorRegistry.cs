namespace FI.Domain.Connectors;

/// <summary>
/// Bölüm 34 — "generic repository benzeri bir soyutlama eklenmez"; kayıt yalnızca ProviderKey'e
/// göre basit bir dictionary lookup'tır.
/// </summary>
public interface IConnectorRegistry
{
    bool TryGetIntegrationConnector(string providerKey, out IIntegrationConnector connector);

    bool TryGetDeploymentConnector(string providerKey, out IDeploymentConnector connector);
}
