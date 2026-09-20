namespace GatewayDemo.Models;

public sealed class GatewayAppReplayNonce
{
    public Guid Id { get; set; }
    public string AppKeyId { get; set; } = string.Empty;
    public string NonceHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
