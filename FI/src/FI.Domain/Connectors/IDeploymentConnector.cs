namespace FI.Domain.Connectors;

public interface IDeploymentConnector
{
    string ProviderKey { get; }

    NormalizedDeployment Normalize(RawInboundPayload payload);

    bool VerifySignature(RawInboundPayload payload, string secret);
}
