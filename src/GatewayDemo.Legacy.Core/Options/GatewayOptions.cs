using System;
using System.Collections.Generic;
using System.Linq;

namespace GatewayDemo.Legacy.Core.Options
{
    public sealed class GatewayOptions
    {
        public const string SectionName = "Gateway";

        public string AppName { get; set; }
        public string ProxyBasePath { get; set; }
        public string DeviceCookieName { get; set; }
        public string DeviceCookieDomain { get; set; }
        public string DeviceCookieSecurePolicy { get; set; }
        public bool WebViewHandoffEnabled { get; set; }
        public int WebViewHandoffTicketLifetimeSeconds { get; set; }
        public int WebViewHandoffCookieLifetimeMinutes { get; set; }
        public string WebViewHandoffCookieName { get; set; }
        public string ExternalScheme { get; set; }
        public bool UseDefaultUpstreamProxy { get; set; }
        public string UpstreamProxyUrl { get; set; }
        public IList<string> TrustedProxyAddresses { get; set; }
        public IList<string> TrustedProxyCidrs { get; set; }
        public bool BrowserDeviceReviewOnSignalChange { get; set; }
        public IList<string> BrowserDeviceSignalHeaders { get; set; }
        public bool LegacyAppAllowAccountOnlyIdentityFallback { get; set; }
        public bool CorsEnabled { get; set; }
        public IList<string> CorsAllowedOrigins { get; set; }
        public IList<string> CorsAllowedMethods { get; set; }
        public IList<string> CorsAllowedHeaders { get; set; }
        public bool CorsAllowCredentials { get; set; }
        public int CorsMaxAgeSeconds { get; set; }
        public IList<GatewaySiteOptions> Sites { get; set; }

        public GatewayOptions()
        {
            AppName = "统一认证访问网关";
            ProxyBasePath = "/proxy";
            DeviceCookieName = "gw_device_credential";
            DeviceCookieDomain = string.Empty;
            DeviceCookieSecurePolicy = "Never";
            WebViewHandoffEnabled = false;
            WebViewHandoffTicketLifetimeSeconds = 60;
            WebViewHandoffCookieLifetimeMinutes = 720;
            WebViewHandoffCookieName = "gw_webview_credential";
            ExternalScheme = string.Empty;
            UseDefaultUpstreamProxy = true;
            UpstreamProxyUrl = string.Empty;
            TrustedProxyAddresses = new List<string>();
            TrustedProxyCidrs = new List<string>();
            BrowserDeviceReviewOnSignalChange = true;
            BrowserDeviceSignalHeaders = new List<string> { "User-Agent", "Sec-CH-UA-Platform", "Sec-CH-UA-Mobile" };
            LegacyAppAllowAccountOnlyIdentityFallback = false;
            CorsEnabled = false;
            CorsAllowedOrigins = new List<string>();
            CorsAllowedMethods = new List<string> { "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS" };
            CorsAllowedHeaders = new List<string> { "Content-Type", "Authorization", "X-Requested-With" };
            CorsAllowCredentials = true;
            CorsMaxAgeSeconds = 600;
            Sites = new List<GatewaySiteOptions>();
        }

        public GatewaySiteOptions FindSite(string siteKey)
        {
            return Sites.FirstOrDefault(
                site => string.Equals(site.Key, siteKey, StringComparison.OrdinalIgnoreCase));
        }

        public GatewaySiteOptions FindSiteByHost(string hostHeader)
        {
            var authority = NormalizeAuthority(hostHeader);
            var host = NormalizeHost(hostHeader);
            if (string.IsNullOrWhiteSpace(authority) && string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            var explicitMatch = FindSiteByConfiguredHost(hostHeader);
            if (explicitMatch != null)
            {
                return explicitMatch;
            }

            var exactUpstreamMatches = Sites
                .Where(site => MatchesUpstreamAuthority(site, authority))
                .ToList();
            if (exactUpstreamMatches.Count == 1)
            {
                return exactUpstreamMatches[0];
            }

            var hostUpstreamMatches = Sites
                .Where(site => MatchesUpstreamHost(site, host))
                .ToList();
            return hostUpstreamMatches.Count == 1 ? hostUpstreamMatches[0] : null;
        }

        public GatewaySiteOptions FindSiteByConfiguredHost(string hostHeader)
        {
            var authority = NormalizeAuthority(hostHeader);
            var host = NormalizeHost(hostHeader);
            if (string.IsNullOrWhiteSpace(authority) && string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            // Entries that pin an explicit port win over host-only entries so a
            // port-agnostic HostNames entry on one site cannot shadow another
            // site's exact "host:port" binding.
            var authorityMatches = Sites.Where(site => MatchesConfiguredAuthority(site, authority)).ToList();
            if (authorityMatches.Count == 1)
            {
                return authorityMatches[0];
            }

            if (authorityMatches.Count > 1)
            {
                return null;
            }

            var hostOnlyMatches = Sites.Where(site => MatchesConfiguredHostOnly(site, host)).ToList();
            return hostOnlyMatches.Count == 1 ? hostOnlyMatches[0] : null;
        }

        public GatewaySiteOptions FindSiteByUpstreamOrigin(Uri upstreamUri)
        {
            if (upstreamUri == null)
            {
                return null;
            }

            var exactMatches = Sites
                .Where(site => MatchesUpstreamAuthority(site, upstreamUri.Authority))
                .ToList();
            if (exactMatches.Count == 1)
            {
                return exactMatches[0];
            }

            var hostMatches = Sites
                .Where(site => MatchesUpstreamHost(site, upstreamUri.Host))
                .ToList();
            return hostMatches.Count == 1 ? hostMatches[0] : null;
        }

        public GatewaySiteOptions FindLegacyAppRootSite()
        {
            return Sites.FirstOrDefault(site => site.ExposeLegacyAppAtRoot)
                ?? (Sites.Count == 1 ? Sites[0] : null);
        }

        public void Normalize()
        {
            ProxyBasePath = string.IsNullOrWhiteSpace(ProxyBasePath)
                ? "/proxy"
                : ProxyBasePath.StartsWith("/")
                    ? ProxyBasePath.TrimEnd('/')
                    : "/" + ProxyBasePath.TrimEnd('/');
            DeviceCookieName = string.IsNullOrWhiteSpace(DeviceCookieName)
                ? "gw_device_credential"
                : DeviceCookieName.Trim();
            WebViewHandoffTicketLifetimeSeconds = Math.Max(10, Math.Min(600, WebViewHandoffTicketLifetimeSeconds));
            WebViewHandoffCookieLifetimeMinutes = Math.Max(5, Math.Min(43200, WebViewHandoffCookieLifetimeMinutes));
            WebViewHandoffCookieName = string.IsNullOrWhiteSpace(WebViewHandoffCookieName)
                ? "gw_webview_credential"
                : WebViewHandoffCookieName.Trim();
            DeviceCookieDomain = (DeviceCookieDomain ?? string.Empty).Trim();
            DeviceCookieSecurePolicy = NormalizeSecurePolicy(DeviceCookieSecurePolicy);
            ExternalScheme = NormalizeExternalScheme(ExternalScheme);
            UpstreamProxyUrl = (UpstreamProxyUrl ?? string.Empty).Trim();
            CorsAllowedOrigins = NormalizeList(CorsAllowedOrigins);
            CorsAllowedMethods = NormalizeList(CorsAllowedMethods);
            CorsAllowedHeaders = NormalizeList(CorsAllowedHeaders);
            BrowserDeviceSignalHeaders = NormalizeList(BrowserDeviceSignalHeaders);
            if (BrowserDeviceSignalHeaders.Count == 0)
            {
                BrowserDeviceSignalHeaders = new List<string> { "User-Agent", "Sec-CH-UA-Platform", "Sec-CH-UA-Mobile" };
            }

            if (CorsAllowedMethods.Count == 0)
            {
                CorsAllowedMethods = new List<string> { "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS" };
            }

            if (CorsAllowedHeaders.Count == 0)
            {
                CorsAllowedHeaders = new List<string> { "Content-Type", "Authorization", "X-Requested-With" };
            }

            if (CorsMaxAgeSeconds <= 0)
            {
                CorsMaxAgeSeconds = 600;
            }

            foreach (var site in Sites)
            {
                site.Normalize();
            }

            TrustedProxyAddresses = (TrustedProxyAddresses ?? new List<string>())
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .Select(address => address.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            TrustedProxyCidrs = (TrustedProxyCidrs ?? new List<string>())
                .Where(cidr => !string.IsNullOrWhiteSpace(cidr))
                .Select(cidr => cidr.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IList<string> NormalizeList(IEnumerable<string> values)
        {
            return (values ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool MatchesConfiguredAuthority(GatewaySiteOptions site, string authority)
        {
            if (site == null || site.HostNames == null || site.HostNames.Count == 0 || string.IsNullOrWhiteSpace(authority))
            {
                return false;
            }

            foreach (var configuredHost in site.HostNames)
            {
                var configuredAuthority = NormalizeAuthority(configuredHost);
                if (string.Equals(configuredAuthority, authority, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesConfiguredHostOnly(GatewaySiteOptions site, string host)
        {
            if (site == null || site.HostNames == null || site.HostNames.Count == 0 || string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            foreach (var configuredHost in site.HostNames)
            {
                var configuredAuthority = NormalizeAuthority(configuredHost);
                if (AuthorityHasPort(configuredAuthority))
                {
                    continue;
                }

                if (string.Equals(NormalizeHost(configuredHost), host, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesUpstreamAuthority(GatewaySiteOptions site, string authority)
        {
            if (site == null || string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(site.UpstreamBaseUrl))
            {
                return false;
            }

            Uri upstreamUri;
            return Uri.TryCreate(site.UpstreamBaseUrl, UriKind.Absolute, out upstreamUri)
                && string.Equals(upstreamUri.Authority, NormalizeAuthority(authority), StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesUpstreamHost(GatewaySiteOptions site, string host)
        {
            if (site == null || string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(site.UpstreamBaseUrl))
            {
                return false;
            }

            Uri upstreamUri;
            return Uri.TryCreate(site.UpstreamBaseUrl, UriKind.Absolute, out upstreamUri)
                && string.Equals(upstreamUri.Host, NormalizeHost(host), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAuthority(string hostHeader)
        {
            if (string.IsNullOrWhiteSpace(hostHeader))
            {
                return string.Empty;
            }

            var trimmed = hostHeader.Trim();
            Uri uri;
            if (trimmed.IndexOf("://", StringComparison.Ordinal) > 0
                && Uri.TryCreate(trimmed, UriKind.Absolute, out uri)
                && !string.IsNullOrWhiteSpace(uri.Authority))
            {
                return uri.Authority.ToLowerInvariant();
            }

            var slashIndex = trimmed.IndexOf('/');
            if (slashIndex >= 0)
            {
                trimmed = trimmed.Substring(0, slashIndex);
            }

            return trimmed.Trim().ToLowerInvariant();
        }

        private static string NormalizeHost(string hostHeader)
        {
            var authority = NormalizeAuthority(hostHeader);
            if (string.IsNullOrWhiteSpace(authority))
            {
                return string.Empty;
            }

            var colonIndex = authority.LastIndexOf(':');
            return colonIndex > 0 ? authority.Substring(0, colonIndex) : authority;
        }

        private static bool AuthorityHasPort(string authority)
        {
            if (string.IsNullOrWhiteSpace(authority))
            {
                return false;
            }

            var colonIndex = authority.LastIndexOf(':');
            return colonIndex > 0 && colonIndex < authority.Length - 1
                && authority.Skip(colonIndex + 1).All(char.IsDigit);
        }

        private static string NormalizeSecurePolicy(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.Equals(normalized, "Always", StringComparison.OrdinalIgnoreCase))
            {
                return "Always";
            }

            if (string.Equals(normalized, "ExternalHttps", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                return "ExternalHttps";
            }

            return "Never";
        }

        private static string NormalizeExternalScheme(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return string.Equals(normalized, "https", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "http", StringComparison.OrdinalIgnoreCase)
                    ? normalized
                    : string.Empty;
        }
    }
}
