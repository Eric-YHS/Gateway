namespace GatewayDemo.Models;

public sealed class GatewayManagedDevice
{
    public Guid Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string? BrowserTokenHash { get; set; }
    public string? AppKeyId { get; set; }
    public string? ProtectedAppSecret { get; set; }
    public GatewayDeviceTrustState TrustState { get; set; } = GatewayDeviceTrustState.Trusted;
    public string ChallengeReason { get; set; } = string.Empty;
    public string SignalHash { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string RegisteredIp { get; set; } = string.Empty;
    public string LastSeenIp { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public DateTimeOffset? ChallengedAtUtc { get; set; }

    public bool HasAppCredential =>
        !string.IsNullOrWhiteSpace(AppKeyId) && !string.IsNullOrWhiteSpace(ProtectedAppSecret);
}
