namespace GatewayDemo.Models;

public sealed class GatewayAuditLog
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string SiteKey { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
