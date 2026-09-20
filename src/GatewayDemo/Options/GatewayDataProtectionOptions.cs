namespace GatewayDemo.Options;

public sealed class GatewayDataProtectionOptions
{
    public const string SectionName = "DataProtection";

    public string ApplicationName { get; set; } = "GatewayDemo";
    public string KeyRingPath { get; set; } = "App_Data/DataProtection-Keys";

    public void Normalize(string contentRoot)
    {
        ApplicationName = string.IsNullOrWhiteSpace(ApplicationName) ? "GatewayDemo" : ApplicationName.Trim();

        if (string.IsNullOrWhiteSpace(KeyRingPath))
        {
            KeyRingPath = Path.Combine(contentRoot, "App_Data", "DataProtection-Keys");
            return;
        }

        if (!Path.IsPathRooted(KeyRingPath))
        {
            KeyRingPath = Path.Combine(contentRoot, KeyRingPath);
        }
    }
}
