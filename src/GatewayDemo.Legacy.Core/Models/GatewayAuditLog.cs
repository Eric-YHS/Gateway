using System;

namespace GatewayDemo.Legacy.Core.Models
{
    public sealed class GatewayAuditLog
    {
        public Guid Id { get; set; }
        public string Kind { get; set; }
        public string Message { get; set; }
        public string DeviceId { get; set; }
        public string DeviceCode { get; set; }
        public string SiteKey { get; set; }
        public string ClientIp { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public GatewayAuditLog()
        {
            Kind = string.Empty;
            Message = string.Empty;
            DeviceId = string.Empty;
            DeviceCode = string.Empty;
            SiteKey = string.Empty;
            ClientIp = string.Empty;
        }
    }
}
