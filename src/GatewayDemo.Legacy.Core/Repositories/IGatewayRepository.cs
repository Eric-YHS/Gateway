using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GatewayDemo.Legacy.Core.Models;
using GatewayDemo.Legacy.Core.Services;

namespace GatewayDemo.Legacy.Core.Repositories
{
    public interface IGatewayRepository
    {
        Task<GatewayDeviceAuthorization> GetActiveAuthorizationAsync(string deviceId, CancellationToken cancellationToken);
        Task<GatewayAccessRequest> GetLatestRequestAsync(string deviceId, CancellationToken cancellationToken);
        Task<GatewayAccessRequest> CreateOrUpdateRequestAsync(
            DeviceContext device,
            string companyName,
            string applicantName,
            string phone,
            string reason,
            string targetPath,
            IReadOnlyCollection<string> siteKeys,
            CancellationToken cancellationToken);
        Task ApproveRequestAsync(Guid requestId, IReadOnlyCollection<string> siteKeys, string note, CancellationToken cancellationToken);
        Task RejectRequestAsync(Guid requestId, string note, CancellationToken cancellationToken);
        Task RevokeAuthorizationAsync(Guid authorizationId, string note, CancellationToken cancellationToken);
        Task TouchAuthorizationAsync(string deviceId, string clientIp, string siteKey, CancellationToken cancellationToken);
        Task RecordAuditAsync(string kind, string message, DeviceContext device, string siteKey, string clientIp, CancellationToken cancellationToken);
        Task<GatewayDashboardSnapshot> GetDashboardSnapshotAsync(CancellationToken cancellationToken);
    }
}
