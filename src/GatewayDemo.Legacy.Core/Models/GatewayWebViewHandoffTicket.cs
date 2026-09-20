using System;

namespace GatewayDemo.Legacy.Core.Models
{
    public sealed class GatewayWebViewHandoffTicket
    {
        public string Id { get; set; }
        public string TicketHash { get; set; }
        public string DeviceId { get; set; }
        public string SiteKey { get; set; }
        public string HostBinding { get; set; }
        public string TargetPath { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? ConsumedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public GatewayWebViewHandoffTicket()
        {
            Id = string.Empty;
            TicketHash = string.Empty;
            DeviceId = string.Empty;
            SiteKey = string.Empty;
            HostBinding = string.Empty;
            TargetPath = string.Empty;
        }
    }
}
