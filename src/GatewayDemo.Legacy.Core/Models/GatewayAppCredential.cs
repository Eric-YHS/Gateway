using System;

namespace GatewayDemo.Legacy.Core.Models
{
    public sealed class GatewayAppCredential
    {
        public Guid Id { get; set; }
        public string DeviceId { get; set; }
        public string CredentialKey { get; set; }
        public string ProtectedSecret { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? LastRotatedAtUtc { get; set; }

        public GatewayAppCredential()
        {
            DeviceId = string.Empty;
            CredentialKey = string.Empty;
            ProtectedSecret = string.Empty;
        }
    }
}
