namespace GatewayDemo.Models;

public sealed class GatewayAccessRequest
{
    public Guid Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string SignalHash { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public GatewayAccessRequestStatus Status { get; set; } = GatewayAccessRequestStatus.Pending;
    public string ReviewNote { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public List<GatewayAccessRequestSite> RequestedSites { get; set; } = [];
}
