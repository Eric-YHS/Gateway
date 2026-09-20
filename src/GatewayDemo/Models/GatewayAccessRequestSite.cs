namespace GatewayDemo.Models;

public sealed class GatewayAccessRequestSite
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public string SiteKey { get; set; } = string.Empty;
    public GatewayAccessRequest? Request { get; set; }
}
