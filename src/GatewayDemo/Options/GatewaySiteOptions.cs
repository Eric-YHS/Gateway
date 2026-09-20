namespace GatewayDemo.Options;

public sealed class GatewaySiteOptions
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Accent { get; set; } = "#0f766e";
    public string UpstreamBaseUrl { get; set; } = string.Empty;

    public void Normalize()
    {
        Key = Key.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? Key : Name.Trim();
        Description = Description.Trim();
        Accent = string.IsNullOrWhiteSpace(Accent) ? "#0f766e" : Accent.Trim();
        UpstreamBaseUrl = UpstreamBaseUrl.TrimEnd('/') + "/";
    }
}
