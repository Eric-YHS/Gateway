using System;

namespace GatewayDemo.Legacy.Core.Models
{
    public sealed class GatewayManagedDevice
    {
        public Guid Id { get; set; }
        public string DeviceId { get; set; }
        public string DeviceCode { get; set; }
        public GatewayDeviceTrustState TrustState { get; set; }
        public string ChallengeReason { get; set; }
        public string SignalHash { get; set; }
        public string UserAgent { get; set; }
        public string RegisteredIp { get; set; }
        public string LastSeenIp { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? LastSeenAtUtc { get; set; }
        public DateTime? ChallengedAtUtc { get; set; }
        public bool HasAppCredential { get; set; }

        public GatewayManagedDevice()
        {
            DeviceId = string.Empty;
            DeviceCode = string.Empty;
            ChallengeReason = string.Empty;
            SignalHash = string.Empty;
            UserAgent = string.Empty;
            RegisteredIp = string.Empty;
            LastSeenIp = string.Empty;
            TrustState = GatewayDeviceTrustState.Trusted;
        }
    }
}
