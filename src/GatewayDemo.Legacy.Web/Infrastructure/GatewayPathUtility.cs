using System;
using System.Collections.Generic;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal static class GatewayPathUtility
    {
        public const string LegacyRootPathMarker = "__root__";
        public const string SiteOwnsRequestRootContextKey = "__Gateway.SiteOwnsRequestRoot";

        public static bool UsesGatewayRootPaths(GatewaySiteOptions site)
        {
            return site != null
                && (site.ExposeLegacyAppAtRoot
                    || (site.HostNames != null && site.HostNames.Count > 0));
        }

        // Root-style paths are only safe when the current request's host actually
        // resolves back to this site; otherwise clean root links would be picked up
        // by whichever site owns the shared host/port. The flag is stamped per
        // request by ProxyRequestModule; outside a request the site-level decision
        // is kept.
        public static bool UsesGatewayRootPathsForRequest(GatewaySiteOptions site)
        {
            if (!UsesGatewayRootPaths(site))
            {
                return false;
            }

            var context = System.Web.HttpContext.Current;
            if (context == null)
            {
                return true;
            }

            var value = context.Items[SiteOwnsRequestRootContextKey];
            return !(value is bool) || (bool)value;
        }

        public static string BuildGatewayRootPath(string relativePath)
        {
            return BuildGatewayRootPath(relativePath, string.Empty);
        }

        public static string BuildGatewayRootPath(string relativePath, string suffix)
        {
            var normalized = (relativePath ?? string.Empty).TrimStart('/');
            var path = string.IsNullOrWhiteSpace(normalized)
                ? "/"
                : "/" + normalized;
            return path + (suffix ?? string.Empty);
        }

        public static string StripLegacyRootPathMarker(string relativePath)
        {
            var normalized = (relativePath ?? string.Empty).TrimStart('/');
            if (string.Equals(normalized, LegacyRootPathMarker, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return normalized.StartsWith(LegacyRootPathMarker + "/", StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(LegacyRootPathMarker.Length).TrimStart('/')
                : normalized;
        }

        public static bool HasLegacyRootPathMarker(string relativePath)
        {
            var normalized = (relativePath ?? string.Empty).TrimStart('/');
            return string.Equals(normalized, LegacyRootPathMarker, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(LegacyRootPathMarker + "/", StringComparison.OrdinalIgnoreCase);
        }

        public static string StripEntryPath(string relativePath, string entryPath)
        {
            var normalized = (relativePath ?? string.Empty).TrimStart('/');
            var entry = (entryPath ?? string.Empty).Trim().Trim('/');
            if (string.IsNullOrWhiteSpace(entry))
            {
                return normalized;
            }

            return normalized.StartsWith(entry + "/", StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(entry.Length).TrimStart('/')
                : normalized;
        }

        public static string CleanExternalProxyRootPath(string requestPath, string proxyBasePath, GatewaySiteOptions site)
        {
            if (string.IsNullOrWhiteSpace(requestPath) || site == null)
            {
                return requestPath;
            }

            var normalized = requestPath.Replace("\\", "/");
            var proxyPrefix = BuildProxySitePrefix(proxyBasePath, site.Key);
            if (!normalized.StartsWith(proxyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return requestPath;
            }

            var suffix = StripLegacyRootPathMarker(normalized.Substring(proxyPrefix.Length).TrimStart('/'));
            return UsesGatewayRootPathsForRequest(site) ? "/" + suffix : requestPath;
        }

        public static string BuildProxySitePrefix(string proxyBasePath, string siteKey)
        {
            var normalizedProxyBase = string.IsNullOrWhiteSpace(proxyBasePath)
                ? "/proxy"
                : "/" + proxyBasePath.Trim().Trim('/');
            return normalizedProxyBase.TrimEnd('/') + "/" + (siteKey ?? string.Empty).Trim('/');
        }

        public static void AddPathCandidate(IList<string> candidates, string path, string query)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalized = path.Trim();
            if (!normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = "/" + normalized.TrimStart('/');
            }

            AddDistinct(candidates, normalized);
            if (!string.IsNullOrWhiteSpace(query))
            {
                AddDistinct(candidates, normalized + query);
            }
        }

        public static void AddDistinct(IList<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (var existing in values)
            {
                if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            values.Add(value);
        }

        public static bool MatchesAnyAllowedPath(IList<string> candidates, string pattern)
        {
            var normalizedPattern = NormalizeAllowedPathPattern(pattern);
            if (string.IsNullOrWhiteSpace(normalizedPattern))
            {
                return false;
            }

            var patternIncludesQuery = normalizedPattern.IndexOf('?') >= 0;
            foreach (var candidate in candidates ?? new List<string>())
            {
                if (!patternIncludesQuery && (candidate ?? string.Empty).IndexOf('?') >= 0)
                {
                    continue;
                }

                if (WildcardMatch(candidate ?? string.Empty, normalizedPattern))
                {
                    return true;
                }
            }

            return false;
        }

        public static string NormalizeAllowedPathPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return string.Empty;
            }

            var trimmed = pattern.Trim();
            if (string.Equals(trimmed, "*", StringComparison.Ordinal))
            {
                return trimmed;
            }

            Uri uri;
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out uri))
            {
                trimmed = uri.PathAndQuery;
            }

            return trimmed.StartsWith("/", StringComparison.Ordinal)
                ? trimmed
                : "/" + trimmed.TrimStart('/');
        }

        public static bool WildcardMatch(string value, string pattern)
        {
            value = value ?? string.Empty;
            pattern = pattern ?? string.Empty;
            if (string.Equals(pattern, "*", StringComparison.Ordinal))
            {
                return true;
            }

            var parts = pattern.Split(new[] { '*' }, StringSplitOptions.None);
            var index = 0;
            if (!pattern.StartsWith("*", StringComparison.Ordinal)
                && !value.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                var found = value.IndexOf(part, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    return false;
                }

                index = found + part.Length;
            }

            var lastPart = parts.Length == 0 ? string.Empty : parts[parts.Length - 1];
            return pattern.EndsWith("*", StringComparison.Ordinal)
                || string.IsNullOrEmpty(lastPart)
                || value.EndsWith(lastPart, StringComparison.OrdinalIgnoreCase);
        }

        public static void SplitPathSuffix(string value, out string path, out string suffix)
        {
            value = value ?? string.Empty;
            var queryIndex = value.IndexOf('?');
            var fragmentIndex = value.IndexOf('#');
            var separatorIndex = -1;
            if (queryIndex >= 0 && fragmentIndex >= 0)
            {
                separatorIndex = Math.Min(queryIndex, fragmentIndex);
            }
            else if (queryIndex >= 0)
            {
                separatorIndex = queryIndex;
            }
            else if (fragmentIndex >= 0)
            {
                separatorIndex = fragmentIndex;
            }

            path = separatorIndex >= 0 ? value.Substring(0, separatorIndex) : value;
            suffix = separatorIndex >= 0 ? value.Substring(separatorIndex) : string.Empty;
        }
    }
}
