using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Script.Serialization;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    public sealed class LegacyGatewayConfiguration
    {
        public string ContentRootPath { get; private set; }
        public string SitesConfigPath { get; private set; }
        public string DatabaseFilePath { get; private set; }
        public GatewayOptions Gateway { get; private set; }
        public AdminOptions Admin { get; private set; }
        public StorageOptions Storage { get; private set; }
        private readonly object sitesReloadSyncRoot = new object();
        private DateTime sitesConfigLastWriteUtc;

        private LegacyGatewayConfiguration()
        {
            ContentRootPath = string.Empty;
            SitesConfigPath = string.Empty;
            DatabaseFilePath = string.Empty;
            Gateway = new GatewayOptions();
            Admin = new AdminOptions();
            Storage = new StorageOptions();
        }

        public static LegacyGatewayConfiguration Load(string contentRootPath)
        {
            var configuration = new LegacyGatewayConfiguration();
            configuration.ContentRootPath = contentRootPath;

            configuration.Gateway.AppName = ReadSetting("Gateway.AppName", configuration.Gateway.AppName);
            configuration.Gateway.ProxyBasePath = ReadSetting("Gateway.ProxyBasePath", configuration.Gateway.ProxyBasePath);
            configuration.Gateway.DeviceCookieName = ReadSetting("Gateway.DeviceCookieName", configuration.Gateway.DeviceCookieName);
            configuration.Gateway.DeviceCookieDomain = ReadSetting("Gateway.DeviceCookieDomain", configuration.Gateway.DeviceCookieDomain);
            configuration.Gateway.DeviceCookieSecurePolicy = ReadSetting("Gateway.DeviceCookieSecurePolicy", configuration.Gateway.DeviceCookieSecurePolicy);
            configuration.Gateway.WebViewHandoffEnabled = ReadBoolSetting("Gateway.WebViewHandoff.Enabled", configuration.Gateway.WebViewHandoffEnabled);
            configuration.Gateway.WebViewHandoffTicketLifetimeSeconds = ReadIntSetting("Gateway.WebViewHandoff.TicketLifetimeSeconds", configuration.Gateway.WebViewHandoffTicketLifetimeSeconds);
            configuration.Gateway.WebViewHandoffCookieLifetimeMinutes = ReadIntSetting("Gateway.WebViewHandoff.CookieLifetimeMinutes", configuration.Gateway.WebViewHandoffCookieLifetimeMinutes);
            configuration.Gateway.WebViewHandoffCookieName = ReadSetting("Gateway.WebViewHandoff.CookieName", configuration.Gateway.WebViewHandoffCookieName);
            configuration.Gateway.ExternalScheme = ReadSetting("Gateway.ExternalScheme", configuration.Gateway.ExternalScheme);
            configuration.Gateway.UseDefaultUpstreamProxy = ReadBoolSetting("Gateway.UseDefaultUpstreamProxy", configuration.Gateway.UseDefaultUpstreamProxy);
            configuration.Gateway.UpstreamProxyUrl = ReadSetting("Gateway.UpstreamProxyUrl", configuration.Gateway.UpstreamProxyUrl);
            configuration.Gateway.TrustedProxyAddresses = ReadListSetting("Gateway.TrustedProxyAddresses");
            configuration.Gateway.TrustedProxyCidrs = ReadListSetting("Gateway.TrustedProxyCidrs");
            configuration.Gateway.BrowserDeviceReviewOnSignalChange = ReadBoolSetting("Gateway.BrowserDevice.ReviewOnSignalChange", configuration.Gateway.BrowserDeviceReviewOnSignalChange);
            configuration.Gateway.BrowserDeviceSignalHeaders = ReadListSettingWithDefault("Gateway.BrowserDevice.SignalHeaders", configuration.Gateway.BrowserDeviceSignalHeaders);
            configuration.Gateway.LegacyAppAllowAccountOnlyIdentityFallback = ReadBoolSetting("Gateway.LegacyApp.AllowAccountOnlyIdentityFallback", configuration.Gateway.LegacyAppAllowAccountOnlyIdentityFallback);
            configuration.Gateway.CorsEnabled = ReadBoolSetting("Gateway.Cors.Enabled", configuration.Gateway.CorsEnabled);
            configuration.Gateway.CorsAllowedOrigins = ReadListSettingWithDefault("Gateway.Cors.AllowedOrigins", configuration.Gateway.CorsAllowedOrigins);
            configuration.Gateway.CorsAllowedMethods = ReadListSettingWithDefault("Gateway.Cors.AllowedMethods", configuration.Gateway.CorsAllowedMethods);
            configuration.Gateway.CorsAllowedHeaders = ReadListSettingWithDefault("Gateway.Cors.AllowedHeaders", configuration.Gateway.CorsAllowedHeaders);
            configuration.Gateway.CorsAllowCredentials = ReadBoolSetting("Gateway.Cors.AllowCredentials", configuration.Gateway.CorsAllowCredentials);
            configuration.Gateway.CorsMaxAgeSeconds = ReadIntSetting("Gateway.Cors.MaxAgeSeconds", configuration.Gateway.CorsMaxAgeSeconds);

            configuration.Admin.Username = ReadSetting("Admin.Username", configuration.Admin.Username);
            configuration.Admin.PasswordHash = ReadSetting("Admin.PasswordHash", configuration.Admin.PasswordHash);
            configuration.Admin.ManagementPort = ReadIntSetting("Admin.ManagementPort", configuration.Admin.ManagementPort);
            configuration.Admin.LoginPermitLimit = ReadIntSetting("Admin.LoginPermitLimit", configuration.Admin.LoginPermitLimit);
            configuration.Admin.LoginWindowMinutes = ReadIntSetting("Admin.LoginWindowMinutes", configuration.Admin.LoginWindowMinutes);

            configuration.Storage.Provider = "Sqlite";
            configuration.DatabaseFilePath = ResolvePath(
                contentRootPath,
                ReadSetting("Storage.SqlitePath", @"App_Data\gateway-demo-legacy.db"));
            configuration.Storage.SqliteConnectionString = string.Format(
                "Data Source={0};Version=3;Foreign Keys=True;Default Timeout=30;Pooling=True;",
                configuration.DatabaseFilePath);

            configuration.SitesConfigPath = ResolvePath(
                contentRootPath,
                ReadSetting("Gateway.SitesConfigPath", @"App_Data\gateway-sites.json"));
            configuration.Gateway.Sites = LoadSites(configuration.SitesConfigPath);
            configuration.sitesConfigLastWriteUtc = GetLastWriteUtc(configuration.SitesConfigPath);
            configuration.Gateway.Normalize();
            return configuration;
        }

        public bool ReloadSitesIfChanged()
        {
            var currentLastWriteUtc = GetLastWriteUtc(SitesConfigPath);
            if (currentLastWriteUtc == sitesConfigLastWriteUtc)
            {
                return false;
            }

            lock (sitesReloadSyncRoot)
            {
                currentLastWriteUtc = GetLastWriteUtc(SitesConfigPath);
                if (currentLastWriteUtc == sitesConfigLastWriteUtc)
                {
                    return false;
                }

                Gateway.Sites = LoadSites(SitesConfigPath);
                Gateway.Normalize();
                sitesConfigLastWriteUtc = currentLastWriteUtc;
                return true;
            }
        }

        public string ResolveTargetPath(string requestedTarget)
        {
            if (string.IsNullOrWhiteSpace(requestedTarget))
            {
                return BuildDefaultTargetPath();
            }

            var trimmed = requestedTarget.Trim();
            var siteKey = ExtractSiteKeyFromTarget(trimmed);
            var site = Gateway.FindSite(siteKey);
            if (site == null)
            {
                return BuildDefaultTargetPath();
            }

            var requestSuffix = ExtractRelativeTargetPath(trimmed);
            return string.IsNullOrWhiteSpace(requestSuffix) ? BuildSiteTargetPath(site.Key) : trimmed;
        }

        public string ExtractSiteKeyFromTarget(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return Gateway.Sites.Count == 0 ? string.Empty : Gateway.Sites[0].Key;
            }

            if (!targetPath.StartsWith(Gateway.ProxyBasePath, StringComparison.OrdinalIgnoreCase))
            {
                return Gateway.Sites.Count == 0 ? string.Empty : Gateway.Sites[0].Key;
            }

            var remainder = targetPath.Substring(Gateway.ProxyBasePath.Length).Trim('/');
            if (string.IsNullOrWhiteSpace(remainder))
            {
                return Gateway.Sites.Count == 0 ? string.Empty : Gateway.Sites[0].Key;
            }

            var separatorIndex = remainder.IndexOf('/');
            return separatorIndex >= 0 ? remainder.Substring(0, separatorIndex) : remainder;
        }

        public string ExtractSiteKeyFromRequestPath(string requestPath)
        {
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                return string.Empty;
            }

            if (!requestPath.StartsWith(Gateway.ProxyBasePath, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var remainder = requestPath.Substring(Gateway.ProxyBasePath.Length).Trim('/');
            if (string.IsNullOrWhiteSpace(remainder))
            {
                return string.Empty;
            }

            var separatorIndex = remainder.IndexOf('/');
            return separatorIndex >= 0 ? remainder.Substring(0, separatorIndex) : remainder;
        }

        public IList<string> NormalizeSiteKeys(IEnumerable<string> siteKeys)
        {
            var normalized = new List<string>();
            if (siteKeys == null)
            {
                return normalized;
            }

            foreach (var item in siteKeys)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                var splitItems = item.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var splitItem in splitItems)
                {
                    var trimmed = splitItem.Trim();
                    if (Gateway.FindSite(trimmed) == null)
                    {
                        continue;
                    }

                    if (!normalized.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                    {
                        normalized.Add(trimmed);
                    }
                }
            }

            return normalized;
        }

        public bool IsManagementRequest(HttpContext context)
        {
            int serverPort;
            var serverPortValue = context.Request.ServerVariables["SERVER_PORT"];
            if (int.TryParse(serverPortValue, out serverPort) && serverPort > 0)
            {
                return serverPort == Admin.ManagementPort;
            }

            var fallbackPort = context.Request.Url == null ? 0 : context.Request.Url.Port;
            return fallbackPort == Admin.ManagementPort;
        }

        public string BuildDefaultTargetPath()
        {
            if (Gateway.Sites.Count == 0)
            {
                return Gateway.ProxyBasePath;
            }

            return BuildSiteTargetPath(Gateway.Sites[0].Key);
        }

        public string BuildSiteTargetPath(string siteKey)
        {
            var site = Gateway.FindSite(siteKey);
            if (site == null)
            {
                return Gateway.ProxyBasePath;
            }

            if (UsesGatewayRootPaths(site))
            {
                return string.IsNullOrWhiteSpace(site.EntryPath)
                    ? "/"
                    : "/" + site.EntryPath.TrimStart('/');
            }

            var builder = Gateway.ProxyBasePath + "/" + site.Key;
            if (!string.IsNullOrWhiteSpace(site.EntryPath))
            {
                builder += "/" + site.EntryPath.TrimStart('/');
            }

            return builder;
        }

        public string BuildProxyPath(string siteKey, string relativePath)
        {
            var basePath = Gateway.ProxyBasePath + "/" + (siteKey ?? string.Empty).Trim().Trim('/');
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return basePath + "/";
            }

            return basePath + "/" + relativePath.Trim().TrimStart('/');
        }

        private static bool UsesGatewayRootPaths(GatewaySiteOptions site)
        {
            return GatewayPathUtility.UsesGatewayRootPathsForRequest(site);
        }

        public string ExtractRelativeTargetPath(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return string.Empty;
            }

            if (!targetPath.StartsWith(Gateway.ProxyBasePath, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var remainder = targetPath.Substring(Gateway.ProxyBasePath.Length).Trim('/');
            if (string.IsNullOrWhiteSpace(remainder))
            {
                return string.Empty;
            }

            var separatorIndex = remainder.IndexOf('/');
            return separatorIndex >= 0 && separatorIndex < remainder.Length - 1
                ? remainder.Substring(separatorIndex + 1)
                : string.Empty;
        }

        private static IList<GatewaySiteOptions> LoadSites(string sitesConfigPath)
        {
            if (!File.Exists(sitesConfigPath))
            {
                return new List<GatewaySiteOptions>();
            }

            var serializer = new JavaScriptSerializer();
            var json = (File.ReadAllText(sitesConfigPath) ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<GatewaySiteOptions>();
            }

            if (json.StartsWith("[", StringComparison.Ordinal))
            {
                var sites = serializer.Deserialize<List<GatewaySiteOptions>>(json);
                return sites ?? new List<GatewaySiteOptions>();
            }

            var site = serializer.Deserialize<GatewaySiteOptions>(json);
            return site == null
                ? new List<GatewaySiteOptions>()
                : new List<GatewaySiteOptions> { site };
        }

        private static DateTime GetLastWriteUtc(string path)
        {
            return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
                ? DateTime.MinValue
                : File.GetLastWriteTimeUtc(path);
        }

        private static string ReadSetting(string key, string defaultValue)
        {
            var envValue = Environment.GetEnvironmentVariable(ToEnvironmentVariableName(key));
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                return envValue.Trim();
            }

            var value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
        }

        private static int ReadIntSetting(string key, int defaultValue)
        {
            int value;
            var rawValue = Environment.GetEnvironmentVariable(ToEnvironmentVariableName(key));
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                rawValue = ConfigurationManager.AppSettings[key];
            }

            return int.TryParse(rawValue, out value) && value > 0
                ? value
                : defaultValue;
        }

        private static bool ReadBoolSetting(string key, bool defaultValue)
        {
            var rawValue = Environment.GetEnvironmentVariable(ToEnvironmentVariableName(key));
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                rawValue = ConfigurationManager.AppSettings[key];
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return defaultValue;
            }

            bool value;
            if (bool.TryParse(rawValue.Trim(), out value))
            {
                return value;
            }

            if (string.Equals(rawValue.Trim(), "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawValue.Trim(), "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawValue.Trim(), "on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(rawValue.Trim(), "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawValue.Trim(), "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawValue.Trim(), "off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return defaultValue;
        }

        private static IList<string> ReadListSetting(string key)
        {
            var value = ReadSetting(key, string.Empty);
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<string>();
            }

            return value
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IList<string> ReadListSettingWithDefault(string key, IList<string> defaultValue)
        {
            var value = ReadSetting(key, string.Empty);
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<string>(defaultValue ?? new List<string>());
            }

            return value
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ToEnvironmentVariableName(string key)
        {
            return "GATEWAY_" + (key ?? string.Empty).Replace('.', '_').Replace(':', '_').ToUpperInvariant();
        }

        private static string ResolvePath(string contentRootPath, string configuredPath)
        {
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            return Path.Combine(contentRootPath, configuredPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
