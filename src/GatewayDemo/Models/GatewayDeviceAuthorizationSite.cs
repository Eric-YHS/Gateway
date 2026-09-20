namespace GatewayDemo.Models;

public sealed class GatewayDeviceAuthorizationSite
{
    public Guid Id { get; set; }
    public Guid AuthorizationId { get; set; }
    public string SiteKey { get; set; } = string.Empty;
    public GatewayDeviceAuthorization? Authorization { get; set; }
}
