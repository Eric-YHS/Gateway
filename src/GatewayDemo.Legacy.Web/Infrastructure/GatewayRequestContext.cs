using System;
using System.Net;
using System.Web;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal static class GatewayRequestContext
    {
        private static readonly string[] MultiPartPublicSuffixes =
        {
            "ac.cn", "com.cn", "edu.cn", "gov.cn", "mil.cn", "net.cn", "org.cn",
            "ac.uk", "co.uk", "gov.uk", "org.uk",
            "com.au", "edu.au", "net.au", "org.au",
            "com.hk", "net.hk", "org.hk",
            "com.tw", "net.tw", "org.tw"
        };

        public static bool IsExternalHttps(HttpContext context, GatewayOptions gatewayOptions)
        {
            var configuredScheme = NormalizeScheme(gatewayOptions == null ? string.Empty : gatewayOptions.ExternalScheme);
            if (string.Equals(configuredScheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(configuredScheme, "http", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (context == null || context.Request == null)
            {
                return false;
            }

            if (context.Request.IsSecureConnection)
            {
                return true;
            }

            if (!CanUseForwardedHeaders(context, gatewayOptions))
            {
                return false;
            }

            return HeaderFirstTokenEquals(context, "X-Forwarded-Proto", "https")
                || HeaderFirstTokenEquals(context, "X-Forwarded-Scheme", "https")
                || HeaderFirstTokenEquals(context, "X-Forwarded-Protocol", "https")
                || HeaderFirstTokenEquals(context, "X-Url-Scheme", "https")
                || HeaderFirstTokenEquals(context, "X-Forwarded-SSL", "on")
                || HeaderFirstTokenEquals(context, "Front-End-Https", "on")
                || HeaderFirstTokenEquals(context, "X-Forwarded-Port", "443")
                || HasHeader(context, "X-ARR-SSL")
                || ForwardedFirstParameterEquals(context, "proto", "https");
        }

        public static string GetExternalScheme(HttpContext context, GatewayOptions gatewayOptions)
        {
            var configuredScheme = NormalizeScheme(gatewayOptions == null ? string.Empty : gatewayOptions.ExternalScheme);
            if (!string.IsNullOrWhiteSpace(configuredScheme))
            {
                return configuredScheme;
            }

            return IsExternalHttps(context, gatewayOptions) ? "https" : "http";
        }

        public static string GetExternalHost(HttpContext context, GatewayOptions gatewayOptions)
        {
            if (context == null || context.Request == null)
            {
                return string.Empty;
            }

            if (CanUseForwardedHeaders(context, gatewayOptions))
            {
                var forwardedHost = GetForwardedFirstParameter(context, "host");
                if (!string.IsNullOrWhiteSpace(forwardedHost))
                {
                    return NormalizeAuthority(forwardedHost);
                }

                forwardedHost = FirstHeaderToken(context.Request.Headers["X-Forwarded-Host"]);
                if (!string.IsNullOrWhiteSpace(forwardedHost))
                {
                    return NormalizeAuthority(forwardedHost);
                }

                forwardedHost = FirstHeaderToken(context.Request.Headers["X-Original-Host"]);
                if (!string.IsNullOrWhiteSpace(forwardedHost))
                {
                    return NormalizeAuthority(forwardedHost);
                }
            }

            var host = FirstHeaderToken(context.Request.Headers["Host"]);
            if (string.IsNullOrWhiteSpace(host) && context.Request.Url != null)
            {
                host = context.Request.Url.Authority;
            }

            return NormalizeAuthority(host);
        }

        public static string ResolveDeviceCookieDomain(HttpContext context, GatewayOptions gatewayOptions)
        {
            if (gatewayOptions == null)
            {
                return string.Empty;
            }

            var host = NormalizeHost(GetExternalHost(context, gatewayOptions));
            var configuredDomain = NormalizeCookieDomain(gatewayOptions.DeviceCookieDomain);
            if (!string.IsNullOrWhiteSpace(configuredDomain))
            {
                return IsCookieDomainCompatible(host, configuredDomain) ? configuredDomain : string.Empty;
            }

            var inferredDomain = InferCookieDomain(host);
            return IsCookieDomainCompatible(host, inferredDomain) ? inferredDomain : string.Empty;
        }

        public static bool ShouldUseSecureDeviceCookie(HttpContext context, GatewayOptions gatewayOptions)
        {
            var policy = gatewayOptions == null ? string.Empty : (gatewayOptions.DeviceCookieSecurePolicy ?? string.Empty).Trim();
            if (string.Equals(policy, "Always", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(policy, "ExternalHttps", StringComparison.OrdinalIgnoreCase)
                || string.Equals(policy, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                return IsExternalHttps(context, gatewayOptions);
            }

            return false;
        }

        private static bool CanUseForwardedHeaders(HttpContext context, GatewayOptions gatewayOptions)
        {
            return ClientIpResolver.IsTrustedProxy(context, gatewayOptions)
                || IsLoopbackOrPrivateRemoteAddress(context);
        }

        private static bool IsLoopbackOrPrivateRemoteAddress(HttpContext context)
        {
            if (context == null || context.Request == null)
            {
                return false;
            }

            IPAddress address;
            if (!IPAddress.TryParse(NormalizeAddress(context.Request.UserHostAddress), out address))
            {
                return false;
            }

            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            var bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
            {
                return bytes[0] == 10
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168)
                    || (bytes[0] == 169 && bytes[1] == 254);
            }

            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || (bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC);
        }

        private static bool HeaderFirstTokenEquals(HttpContext context, string name, string expected)
        {
            var token = FirstHeaderToken(context.Request.Headers[name]);
            return string.Equals(token, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasHeader(HttpContext context, string name)
        {
            return !string.IsNullOrWhiteSpace(context.Request.Headers[name]);
        }

        private static bool ForwardedFirstParameterEquals(HttpContext context, string name, string expected)
        {
            return string.Equals(GetForwardedFirstParameter(context, name), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetForwardedFirstParameter(HttpContext context, string name)
        {
            var forwarded = context == null || context.Request == null
                ? string.Empty
                : context.Request.Headers["Forwarded"];
            if (string.IsNullOrWhiteSpace(forwarded) || string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var firstEntry = forwarded.Split(',')[0];
            foreach (var part in firstEntry.Split(';'))
            {
                var trimmed = (part ?? string.Empty).Trim();
                var equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                var key = trimmed.Substring(0, equalsIndex).Trim();
                if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return trimmed.Substring(equalsIndex + 1).Trim().Trim('"');
            }

            return string.Empty;
        }

        private static string FirstHeaderToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Split(',')[0].Trim();
        }

        private static string NormalizeScheme(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return string.Equals(normalized, "https", StringComparison.Ordinal)
                || string.Equals(normalized, "http", StringComparison.Ordinal)
                    ? normalized
                    : string.Empty;
        }

        private static string NormalizeAuthority(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
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

            return trimmed.Trim().TrimEnd('.').ToLowerInvariant();
        }

        private static string NormalizeHost(string value)
        {
            var authority = NormalizeAuthority(value);
            if (string.IsNullOrWhiteSpace(authority))
            {
                return string.Empty;
            }

            if (authority.StartsWith("[", StringComparison.Ordinal))
            {
                var endBracket = authority.IndexOf(']');
                return endBracket > 0 ? authority.Substring(1, endBracket - 1) : authority;
            }

            var colonIndex = authority.LastIndexOf(':');
            return colonIndex > 0 && colonIndex < authority.Length - 1 && IsAllDigits(authority.Substring(colonIndex + 1))
                ? authority.Substring(0, colonIndex)
                : authority;
        }

        private static string NormalizeAddress(string value)
        {
            var address = (value ?? string.Empty).Trim();
            if (address.StartsWith("[", StringComparison.Ordinal))
            {
                var endBracket = address.IndexOf(']');
                if (endBracket > 0)
                {
                    return address.Substring(1, endBracket - 1);
                }
            }

            var colonIndex = address.IndexOf(':');
            if (colonIndex > 0 && colonIndex == address.LastIndexOf(':'))
            {
                var possiblePort = address.Substring(colonIndex + 1);
                if (IsAllDigits(possiblePort))
                {
                    return address.Substring(0, colonIndex);
                }
            }

            return address;
        }

        private static string NormalizeCookieDomain(string value)
        {
            var domain = (value ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(domain))
            {
                return string.Empty;
            }

            domain = domain.TrimStart('.');
            return IsCookieHostCandidate(domain) ? "." + domain : string.Empty;
        }

        private static string InferCookieDomain(string host)
        {
            host = NormalizeHost(host);
            if (!IsCookieHostCandidate(host))
            {
                return string.Empty;
            }

            var labels = host.Split('.');
            var suffixLength = GetMultiPartSuffixLength(host);
            var labelCount = suffixLength > 0 ? suffixLength + 1 : 2;
            if (labels.Length < labelCount)
            {
                return string.Empty;
            }

            return "." + string.Join(".", SubArray(labels, labels.Length - labelCount, labelCount));
        }

        private static int GetMultiPartSuffixLength(string host)
        {
            foreach (var suffix in MultiPartPublicSuffixes)
            {
                if (host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return suffix.Split('.').Length;
                }
            }

            return 0;
        }

        private static bool IsCookieHostCandidate(string host)
        {
            if (string.IsNullOrWhiteSpace(host)
                || host.IndexOf('.') <= 0
                || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            IPAddress address;
            return !IPAddress.TryParse(host, out address);
        }

        private static bool IsCookieDomainCompatible(string host, string domain)
        {
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(domain))
            {
                return false;
            }

            var normalizedDomain = domain.Trim().TrimStart('.').ToLowerInvariant();
            return string.Equals(host, normalizedDomain, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + normalizedDomain, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAllDigits(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (!char.IsDigit(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string[] SubArray(string[] values, int start, int length)
        {
            var result = new string[length];
            Array.Copy(values, start, result, 0, length);
            return result;
        }
    }
}
