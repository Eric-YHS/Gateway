using System;
using System.Collections.Generic;
using System.Web;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal static class GatewayPathCandidateBuilder
    {
        public static IList<string> BuildRequestMatchCandidates(HttpContext context, string requestPath)
        {
            var candidates = new List<string>();
            GatewayPathUtility.AddDistinct(candidates, requestPath ?? string.Empty);

            if (context != null && context.Request != null)
            {
                GatewayPathUtility.AddDistinct(candidates, context.Request.RawUrl ?? string.Empty);
                if (context.Request.Url != null)
                {
                    GatewayPathUtility.AddDistinct(candidates, context.Request.Url.PathAndQuery);
                    var query = context.Request.Url.Query;
                    if (!string.IsNullOrWhiteSpace(query) && !string.IsNullOrWhiteSpace(requestPath))
                    {
                        GatewayPathUtility.AddDistinct(candidates, requestPath + query);
                    }
                }
            }

            AddDecodedVariants(candidates);
            return candidates;
        }

        public static IList<string> BuildRuleCandidates(
            HttpContext context,
            string proxyBasePath,
            GatewaySiteOptions site,
            string siteKey,
            string forcedRelativePath)
        {
            return BuildRuleCandidates(context, proxyBasePath, site, siteKey, forcedRelativePath, string.Empty);
        }

        public static IList<string> BuildRuleCandidates(
            HttpContext context,
            string proxyBasePath,
            GatewaySiteOptions site,
            string siteKey,
            string forcedRelativePath,
            string resolvedRelativePath)
        {
            var candidates = new List<string>();
            if (context == null || context.Request == null)
            {
                return candidates;
            }

            var query = context.Request.Url == null ? string.Empty : context.Request.Url.Query;
            GatewayPathUtility.AddPathCandidate(candidates, context.Request.Path ?? string.Empty, query);
            GatewayPathUtility.AddPathCandidate(candidates, context.Request.RawUrl ?? string.Empty, string.Empty);
            if (context.Request.Url != null)
            {
                GatewayPathUtility.AddPathCandidate(candidates, context.Request.Url.PathAndQuery, string.Empty);
            }

            var relativePath = ResolveRelativePath(context, proxyBasePath, site, siteKey, forcedRelativePath, resolvedRelativePath);
            AddRelativePathVariants(candidates, relativePath, query, site == null ? string.Empty : site.EntryPath);
            AddRelativePathVariants(candidates, forcedRelativePath, query, site == null ? string.Empty : site.EntryPath);
            AddRelativePathVariants(candidates, resolvedRelativePath, query, site == null ? string.Empty : site.EntryPath);
            AddDecodedVariants(candidates);
            return candidates;
        }

        public static string ResolveRelativePath(
            HttpContext context,
            string proxyBasePath,
            GatewaySiteOptions site,
            string siteKey,
            string forcedRelativePath)
        {
            return ResolveRelativePath(context, proxyBasePath, site, siteKey, forcedRelativePath, string.Empty);
        }

        public static string ResolveRelativePath(
            HttpContext context,
            string proxyBasePath,
            GatewaySiteOptions site,
            string siteKey,
            string forcedRelativePath,
            string resolvedRelativePath)
        {
            if (!string.IsNullOrWhiteSpace(resolvedRelativePath))
            {
                return resolvedRelativePath.TrimStart('/');
            }

            if (!string.IsNullOrWhiteSpace(forcedRelativePath))
            {
                return forcedRelativePath.TrimStart('/');
            }

            if (context == null || context.Request == null)
            {
                return string.Empty;
            }

            var contextForcedPath = context.Items[ManagedReverseProxy.ForcedRelativePathContextKey] as string;
            if (!string.IsNullOrWhiteSpace(contextForcedPath))
            {
                return contextForcedPath.TrimStart('/');
            }

            var requestPath = context.Request.Path ?? string.Empty;
            var key = string.IsNullOrWhiteSpace(siteKey) && site != null ? site.Key : siteKey;
            if (!string.IsNullOrWhiteSpace(key))
            {
                var proxyPrefix = GatewayPathUtility.BuildProxySitePrefix(proxyBasePath, key).TrimEnd('/') + "/";
                if (requestPath.StartsWith(proxyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return requestPath.Substring(proxyPrefix.Length).TrimStart('/');
                }
            }

            return requestPath.TrimStart('/');
        }

        private static void AddRelativePathVariants(IList<string> candidates, string relativePath, string query, string entryPath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return;
            }

            GatewayPathUtility.AddPathCandidate(candidates, relativePath, query);
            var withoutRootMarker = GatewayPathUtility.StripLegacyRootPathMarker(relativePath);
            GatewayPathUtility.AddPathCandidate(candidates, withoutRootMarker, query);

            var entry = (entryPath ?? string.Empty).Trim().Trim('/');
            if (!string.IsNullOrWhiteSpace(entry))
            {
                GatewayPathUtility.AddPathCandidate(candidates, GatewayPathUtility.StripEntryPath(relativePath, entry), query);
                GatewayPathUtility.AddPathCandidate(candidates, GatewayPathUtility.StripEntryPath(withoutRootMarker, entry), query);
            }
        }

        private static void AddDecodedVariants(IList<string> candidates)
        {
            var decoded = new List<string>();
            foreach (var candidate in candidates ?? new List<string>())
            {
                var value = HttpUtility.UrlDecode(candidate ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    GatewayPathUtility.AddDistinct(decoded, value);
                }
            }

            foreach (var value in decoded)
            {
                GatewayPathUtility.AddDistinct(candidates, value);
            }
        }
    }
}
