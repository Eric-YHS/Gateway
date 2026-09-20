using System;

namespace GatewayDemo.Legacy.Core.Models
{
    public sealed class GatewayAccessRequestSite
    {
        public Guid Id { get; set; }
        public Guid RequestId { get; set; }
        public string SiteKey { get; set; }

        public GatewayAccessRequestSite()
        {
            SiteKey = string.Empty;
        }
    }
}
