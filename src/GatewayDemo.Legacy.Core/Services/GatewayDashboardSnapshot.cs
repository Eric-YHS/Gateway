using System.Collections.Generic;
using GatewayDemo.Legacy.Core.Models;

namespace GatewayDemo.Legacy.Core.Services
{
    public sealed class GatewayDashboardSnapshot
    {
        public IList<GatewayAccessRequest> Requests { get; private set; }
        public IList<GatewayDeviceAuthorization> Authorizations { get; private set; }
        public IDictionary<string, GatewayManagedDevice> Devices { get; private set; }
        public IList<GatewayAuditLog> AuditLogs { get; private set; }

        public GatewayDashboardSnapshot()
        {
            Requests = new List<GatewayAccessRequest>();
            Authorizations = new List<GatewayDeviceAuthorization>();
            Devices = new Dictionary<string, GatewayManagedDevice>();
            AuditLogs = new List<GatewayAuditLog>();
        }
    }
}
