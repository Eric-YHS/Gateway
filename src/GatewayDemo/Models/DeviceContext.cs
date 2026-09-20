namespace GatewayDemo.Models;

public sealed class DeviceContext
{
    public required string DeviceId { get; init; }
    public required string DeviceCode { get; init; }
    public required string SignalHash { get; init; }
    public required string UserAgent { get; init; }
    public required string ClientIp { get; init; }
    public required string CredentialChannel { get; init; }
    public required GatewayDeviceTrustState TrustState { get; init; }
    public required string ChallengeReason { get; init; }
    public required bool HasAppCredential { get; init; }
    public string AppKeyId { get; init; } = string.Empty;

    public bool RequiresReview => TrustState == GatewayDeviceTrustState.Challenged;
}
