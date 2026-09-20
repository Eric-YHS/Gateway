using System;

namespace GatewayDemo.Legacy.Core.Models
{
    public sealed class GatewayWebViewCredential
    {
        public string Id { get; set; }
        public string TokenHash { get; set; }
        public string DeviceId { get; set; }
        public string SiteKey { get; set; }
        public string HostBinding { get; set; }
        public string TargetPath { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public GatewayWebViewCredential()
        {
            Id = string.Empty;
            TokenHash = string.Empty;
            DeviceId = string.Empty;
            SiteKey = string.Empty;
            HostBinding = string.Empty;
            TargetPath = string.Empty;
        }
    }
}
