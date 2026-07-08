namespace GatewayDemo.Legacy.Core.Options
{
    public sealed class GatewayDataProtectionOptions
    {
        public const string SectionName = "DataProtection";

        public string ApplicationName { get; set; }
        public string KeyRingPath { get; set; }

        public GatewayDataProtectionOptions()
        {
            ApplicationName = "GatewayDemo";
            KeyRingPath = "App_Data/DataProtection-Keys";
        }
    }
}
