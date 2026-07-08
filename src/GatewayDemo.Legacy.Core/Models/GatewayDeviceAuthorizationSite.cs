using System;

namespace GatewayDemo.Legacy.Core.Models
{
    public sealed class GatewayDeviceAuthorizationSite
    {
        public Guid Id { get; set; }
        public Guid AuthorizationId { get; set; }
        public string SiteKey { get; set; }

        public GatewayDeviceAuthorizationSite()
        {
            SiteKey = string.Empty;
        }
    }
}
