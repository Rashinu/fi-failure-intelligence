using FI.Domain.Connectors;
using FI.Infrastructure.Connectors;

namespace FI.Api.Extensions;

public static class ConnectorExtensions
{
    /// <summary>Bkz. Bölüm 34 - Mock Stripe/GitHub/SES/SendGrid connector'ları, ProviderKey'e göre dictionary lookup ile IConnectorRegistry üzerinden çözülür.</summary>
    public static IServiceCollection AddFiConnectors(this IServiceCollection services)
    {
        services.AddSingleton<IIntegrationConnector, StripeConnector>();
        services.AddSingleton<IIntegrationConnector, SesConnector>();
        services.AddSingleton<IIntegrationConnector, SendGridConnector>();
        services.AddSingleton<IDeploymentConnector, GitHubDeploymentConnector>();
        services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();

        return services;
    }
}
