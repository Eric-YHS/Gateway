namespace GatewayDemo.Options;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public string AppName { get; set; } = "统一认证网关 DEMO";
    public string ProxyBasePath { get; set; } = "/proxy";
    public string DeviceCookieName { get; set; } = "gw_device_credential";
    public List<string> TrustedProxyAddresses { get; set; } = [];
    public List<string> TrustedProxyCidrs { get; set; } = [];
    public List<GatewaySiteOptions> Sites { get; set; } = [];

    public void Normalize()
    {
        ProxyBasePath = string.IsNullOrWhiteSpace(ProxyBasePath)
            ? "/proxy"
            : ProxyBasePath.StartsWith('/') ? ProxyBasePath.TrimEnd('/') : $"/{ProxyBasePath.TrimEnd('/')}";

        TrustedProxyAddresses = TrustedProxyAddresses
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        TrustedProxyCidrs = TrustedProxyCidrs
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Sites = Sites
            .Where(site => !string.IsNullOrWhiteSpace(site.Key) && !string.IsNullOrWhiteSpace(site.UpstreamBaseUrl))
            .Select(site =>
            {
                site.Normalize();
                return site;
            })
            .ToList();

        if (Sites.Count == 0)
        {
            Sites.Add(new GatewaySiteOptions
            {
                Key = "erp-main",
                Name = "ERP 主站",
                Description = "默认站点",
                UpstreamBaseUrl = "http://localhost:5055/mock/erp-main/",
            });
        }
    }

    public GatewaySiteOptions? FindSite(string siteKey) =>
        Sites.FirstOrDefault(site => string.Equals(site.Key, siteKey, StringComparison.OrdinalIgnoreCase));
}
