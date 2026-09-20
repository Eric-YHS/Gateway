namespace GatewayDemo.Models;

public sealed class GatewayDeviceAuthorization
{
    public Guid Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public GatewayAuthorizationStatus Status { get; set; } = GatewayAuthorizationStatus.Approved;
    public string PolicyMode { get; set; } = "device";
    public string ReviewNote { get; set; } = string.Empty;
    public string LastSeenIp { get; set; } = string.Empty;
    public string LastSeenSiteKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ApprovedAtUtc { get; set; }
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public List<GatewayDeviceAuthorizationSite> AuthorizedSites { get; set; } = [];

    public bool AllowsSite(string siteKey) =>
        AuthorizedSites.Any(site => string.Equals(site.SiteKey, siteKey, StringComparison.OrdinalIgnoreCase));
}
