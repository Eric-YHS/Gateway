using System;
using System.Collections.Generic;
using System.Web;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal sealed class GatewayPublicUrlContext
    {
        internal const string PublicOriginHeaderName = "X-Gateway-Public-Origin";
        internal const string PublicBaseHeaderName = "X-Gateway-Public-Base";
        internal const string ProxyBaseHeaderName = "X-Gateway-Proxy-Base";
        internal const string ProxySiteBaseHeaderName = "X-Gateway-Proxy-Site-Base";

        private readonly LegacyGatewayConfiguration configuration;
        private readonly GatewaySiteOptions site;

        private GatewayPublicUrlContext(LegacyGatewayConfiguration configuration, GatewaySiteOptions site)
        {
            this.configuration = configuration;
            this.site = site;
            Origin = string.Empty;
            ProxyBasePath = "/proxy";
            ProxyBase = string.Empty;
            ProxySiteBase = string.Empty;
            PublicBase = string.Empty;
            UpstreamBasePath = ResolveUpstreamBasePath(site);
            UsesGatewayRootPaths = GatewayPathUtility.UsesGatewayRootPathsForRequest(site);
        }

        public string Origin { get; private set; }
        public string ProxyBasePath { get; private set; }
        public string ProxyBase { get; private set; }
        public string ProxySiteBase { get; private set; }
        public string PublicBase { get; private set; }
        public string UpstreamBasePath { get; private set; }
        public bool UsesGatewayRootPaths { get; private set; }
        public bool HasPublicOrigin { get; private set; }

        public static GatewayPublicUrlContext ForSite(
            LegacyGatewayConfiguration configuration,
            GatewaySiteOptions site)
        {
            return new GatewayPublicUrlContext(configuration, site);
        }

        public static GatewayPublicUrlContext FromHttpContext(
            HttpContext context,
            LegacyGatewayConfiguration configuration,
            GatewaySiteOptions site)
        {
            var result = new GatewayPublicUrlContext(configuration, site);
            if (context == null || context.Request == null || configuration == null || site == null)
            {
                return result;
            }

            var gateway = configuration.Gateway;
            if (gateway == null)
            {
                return result;
            }

            var scheme = GatewayRequestContext.GetExternalScheme(context, gateway);
            var host = GatewayRequestContext.GetExternalHost(context, gateway);
            if (string.IsNullOrWhiteSpace(scheme) || string.IsNullOrWhiteSpace(host))
            {
                return result;
            }

            var origin = scheme.TrimEnd(':') + "://" + host.Trim().TrimEnd('/');
            var proxyBasePath = string.IsNullOrWhiteSpace(gateway.ProxyBasePath)
                ? "/proxy"
                : gateway.ProxyBasePath.TrimEnd('/');
            var proxyBase = origin + proxyBasePath;
            var proxySiteBase = origin + configuration.BuildProxyPath(site.Key, string.Empty).TrimEnd('/');

            result.Origin = origin;
            result.ProxyBasePath = proxyBasePath;
            result.ProxyBase = proxyBase;
            result.ProxySiteBase = proxySiteBase;
            result.PublicBase = result.UsesGatewayRootPaths ? origin : proxySiteBase;
            result.HasPublicOrigin = true;
            return result;
        }

        public IDictionary<string, string> ToForwardedHeaders()
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!HasPublicOrigin)
            {
                return headers;
            }

            headers[PublicOriginHeaderName] = Origin;
            headers[PublicBaseHeaderName] = PublicBase;
            headers[ProxyBaseHeaderName] = ProxyBase;
            headers[ProxySiteBaseHeaderName] = ProxySiteBase;
            return headers;
        }

        public string BuildPublicPath(string relativePath, string suffix)
        {
            if (UsesGatewayRootPaths)
            {
                return GatewayPathUtility.BuildGatewayRootPath(relativePath, suffix);
            }

            if (configuration == null || site == null)
            {
                return (relativePath ?? string.Empty) + (suffix ?? string.Empty);
            }

            return configuration.BuildProxyPath(site.Key, (relativePath ?? string.Empty).TrimStart('/'))
                + (suffix ?? string.Empty);
        }

        public string RewriteCookiePath(string pathValue)
        {
            if (configuration == null || site == null)
            {
                return string.IsNullOrWhiteSpace(pathValue) ? "/" : pathValue;
            }

            if (string.IsNullOrWhiteSpace(pathValue))
            {
                return UsesGatewayRootPaths
                    ? "/"
                    : configuration.BuildProxyPath(site.Key, string.Empty);
            }

            if (UsesGatewayRootPaths
                && (string.Equals(pathValue, "/", StringComparison.Ordinal)
                    || string.Equals(pathValue.TrimEnd('/') + "/", UpstreamBasePath, StringComparison.OrdinalIgnoreCase)))
            {
                return "/";
            }

            if (pathValue.StartsWith("/", StringComparison.Ordinal))
            {
                if (UpstreamBasePath.Length > 1
                    && pathValue.StartsWith(UpstreamBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = pathValue.Substring(UpstreamBasePath.Length).TrimStart('/');
                    return BuildPublicPath(relativePath, string.Empty);
                }

                return UsesGatewayRootPaths
                    ? pathValue
                    : configuration.BuildProxyPath(site.Key, pathValue.TrimStart('/'));
            }

            return BuildPublicPath(pathValue, string.Empty);
        }

        private static string ResolveUpstreamBasePath(GatewaySiteOptions site)
        {
            if (site == null || string.IsNullOrWhiteSpace(site.UpstreamBaseUrl))
            {
                return "/";
            }

            Uri upstreamBase;
            if (!Uri.TryCreate(site.UpstreamBaseUrl, UriKind.Absolute, out upstreamBase))
            {
                return "/";
            }

            return NormalizeBasePath(upstreamBase.AbsolutePath);
        }

        private static string NormalizeBasePath(string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
            {
                return "/";
            }

            return basePath.TrimEnd('/') + "/";
        }
    }
}
