using GatewayDemo.Models;

namespace GatewayDemo.Services;

public sealed record GatewayDashboardSnapshot(
    IReadOnlyList<GatewayAccessRequest> Requests,
    IReadOnlyList<GatewayDeviceAuthorization> Authorizations,
    IReadOnlyDictionary<string, GatewayManagedDevice> Devices,
    IReadOnlyList<GatewayAuditLog> AuditLogs);
