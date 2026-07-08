namespace GatewayDemo.Legacy.Core.Models
{
    public sealed class DeviceContext
    {
        public string DeviceId { get; set; }
        public string DeviceCode { get; set; }
        public string SignalHash { get; set; }
        public string UserAgent { get; set; }
        public string ClientIp { get; set; }
        public string CredentialChannel { get; set; }
        public GatewayDeviceTrustState TrustState { get; set; }
        public string ChallengeReason { get; set; }
        public bool HasAppCredential { get; set; }
        public string AppCredentialKey { get; set; }

        public bool RequiresReview
        {
            get { return TrustState == GatewayDeviceTrustState.Challenged; }
        }

        public DeviceContext()
        {
            DeviceId = string.Empty;
            DeviceCode = string.Empty;
            SignalHash = string.Empty;
            UserAgent = string.Empty;
            ClientIp = string.Empty;
            CredentialChannel = string.Empty;
            ChallengeReason = string.Empty;
            AppCredentialKey = string.Empty;
        }
    }
}
