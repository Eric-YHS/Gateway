namespace GatewayDemo.Legacy.Core.Services
{
    public sealed class IssuedAppCredential
    {
        public string DeviceCode { get; set; }
        public string CredentialKey { get; set; }
        public string CredentialSecret { get; set; }
        public long ExampleTimestamp { get; set; }
        public string ExampleNonce { get; set; }
        public string ExampleBodyHash { get; set; }
        public string ExamplePayload { get; set; }
        public string ExampleSignature { get; set; }

        public IssuedAppCredential()
        {
            DeviceCode = string.Empty;
            CredentialKey = string.Empty;
            CredentialSecret = string.Empty;
            ExampleNonce = string.Empty;
            ExampleBodyHash = string.Empty;
            ExamplePayload = string.Empty;
            ExampleSignature = string.Empty;
        }
    }
}
