using System;

namespace GatewayDemo.Legacy.Core.Models
{
    public sealed class GatewayBrowserCredential
    {
        public Guid Id { get; set; }
        public string DeviceId { get; set; }
        public string TokenHash { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public GatewayBrowserCredential()
        {
            DeviceId = string.Empty;
            TokenHash = string.Empty;
        }
    }
}
