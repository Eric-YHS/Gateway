using System;

namespace GatewayDemo.Legacy.Core.Models
{
    public sealed class GatewayAppReplayNonce
    {
        public Guid Id { get; set; }
        public string CredentialKey { get; set; }
        public string NonceHash { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }

        public GatewayAppReplayNonce()
        {
            CredentialKey = string.Empty;
            NonceHash = string.Empty;
        }
    }
}
