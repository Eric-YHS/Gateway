using System;
using System.Collections.Generic;
using System.Linq;

namespace GatewayDemo.Legacy.Core.Options
{
    public static class GatewayPathRuleProfileCatalog
    {
        public const string LegacyWebDefault = "legacy-web-default";
        public const string SpaViteDefault = "spa-vite-default";
        public const string LegacyApiDefault = "legacy-api-default";
        public const string LegacyRealtimeDefault = "legacy-realtime-default";
        public const string JvsDefault = "jvs-default";

        private static readonly string[] SpaStaticAnonymousAllowedPaths =
        {
            "/@vite/*",
            "/@id/*",
            "/@fs/*",
            "/node_modules/*",
            "/static/*",
            "/scripts/*",
            "/css/*",
            "/js/*",
            "/images/*",
            "/img/*",
            "/fonts/*",
            "/media/*",
            "/favicon*",
            "/assets/*",
            "/src/*",
            "/__vite_ping",
            "*.js",
            "*.css",
            "*.map",
            "*.json",
            "*.png",
            "*.jpg",
            "*.jpeg",
            "*.gif",
            "*.svg",
            "*.ico",
            "*.webp",
            "*.woff",
            "*.woff2",
            "*.ttf",
            "*.otf",
            "*.wasm",
            "/socket"
        };

        private static readonly string[] SpaStaticOriginRootPatterns =
        {
            "/@vite/*",
            "/@id/*",
            "/@fs/*",
            "/node_modules/*",
            "/static/*",
            "/scripts/*",
            "/css/*",
            "/js/*",
            "/images/*",
            "/img/*",
            "/fonts/*",
            "/media/*",
            "/favicon*",
            "*.js",
            "*.css",
            "*.map",
            "*.json",
            "*.png",
            "*.jpg",
            "*.jpeg",
            "*.gif",
            "*.svg",
            "*.ico",
            "*.webp",
            "*.woff",
            "*.woff2",
            "*.ttf",
            "*.otf"
        };

        private static readonly string[] LegacyApiOriginRootPatterns =
        {
            "/api/*"
        };

        private static readonly string[] RealtimeOriginRootPatterns =
        {
            "/api/stream/*",
            "/signalr/*",
            "/notifications/*",
            "/socket"
        };

        private static readonly string[] JvsAnonymousAllowedPaths =
        {
            "/jvs-ui-public/*",
            "/jvs-aps-ui",
            "/jvs-aps-ui/*"
        };

        private static readonly string[] JvsOriginRootPatterns =
        {
            "/jvs-public/*",
            "/mgr/*",
            "/jvs-ui-public/*",
            "/jvs-aps-ui",
            "/jvs-aps-ui/*"
        };

        public static void ApplyProfiles(GatewaySiteOptions site)
        {
            if (site == null || site.PathRuleProfiles == null || site.PathRuleProfiles.Count == 0)
            {
                return;
            }

            foreach (var profile in site.PathRuleProfiles)
            {
                if (ApplyConfiguredProfile(site, profile))
                {
                    continue;
                }

                if (string.Equals(profile, LegacyWebDefault, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyLegacyWebDefault(site);
                    continue;
                }

                if (string.Equals(profile, SpaViteDefault, StringComparison.OrdinalIgnoreCase))
                {
                    ApplySpaViteDefault(site);
                    continue;
                }

                if (string.Equals(profile, LegacyApiDefault, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyLegacyApiDefault(site);
                    continue;
                }

                if (string.Equals(profile, LegacyRealtimeDefault, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyLegacyRealtimeDefault(site);
                    continue;
                }

                if (string.Equals(profile, JvsDefault, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyJvsDefault(site);
                }
            }
        }

        private static bool ApplyConfiguredProfile(GatewaySiteOptions site, string profileName)
        {
            if (site == null
                || site.PathRuleProfileDefinitions == null
                || site.PathRuleProfileDefinitions.Count == 0
                || string.IsNullOrWhiteSpace(profileName))
            {
                return false;
            }

            var profile = site.PathRuleProfileDefinitions.FirstOrDefault(item =>
                item != null && string.Equals(item.Name, profileName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                return false;
            }

            AddRange(site.AnonymousAllowedPaths, profile.AnonymousAllowedPaths);
            AddRange(site.UpstreamOriginRootPatterns, profile.UpstreamOriginRootPatterns);
            foreach (var rule in profile.PathRules ?? new List<GatewayPathRuleOptions>())
            {
                AddOrMergeRule(
                    site,
                    rule.Name,
                    rule.Patterns,
                    rule.ResolveFromUpstreamOriginRoot,
                    rule.TimeoutSeconds,
                    rule.ReadWriteTimeoutSeconds,
                    rule.TreatAsLongPolling,
                    rule.TreatAsStreaming);
            }

            return true;
        }

        private static void ApplyLegacyWebDefault(GatewaySiteOptions site)
        {
            ApplySpaViteDefault(site);
            ApplyLegacyApiDefault(site);
            ApplyLegacyRealtimeDefault(site);
        }

        private static void ApplySpaViteDefault(GatewaySiteOptions site)
        {
            AddRange(site.AnonymousAllowedPaths, SpaStaticAnonymousAllowedPaths);
            AddRange(site.UpstreamOriginRootPatterns, SpaStaticOriginRootPatterns);
            AddOrMergeRule(site, "spa-static-assets", new[] { "/assets/*", "/@vite/*", "/@id/*", "/@fs/*", "/node_modules/*", "*.js", "*.css", "*.map", "*.html", "*.htm" }, true, 60, 60, false, false);
        }

        private static void ApplyLegacyApiDefault(GatewaySiteOptions site)
        {
            AddRange(site.UpstreamOriginRootPatterns, LegacyApiOriginRootPatterns);
            AddOrMergeRule(site, "legacy-api-forwarding", new[] { "/api/*" }, true, 100, 100, false, false);
        }

        private static void ApplyLegacyRealtimeDefault(GatewaySiteOptions site)
        {
            AddOrMergeRule(site, "api-long-polling", RealtimeOriginRootPatterns, false, 1800, 1800, true, false);
        }

        private static void ApplyJvsDefault(GatewaySiteOptions site)
        {
            AddRange(site.AnonymousAllowedPaths, JvsAnonymousAllowedPaths);
            AddRange(site.UpstreamOriginRootPatterns, JvsOriginRootPatterns);
            AddOrMergeRule(site, "jvs-ui-assets", new[] { "/jvs-ui-public/*", "/jvs-aps-ui", "/jvs-aps-ui/*" }, true, 60, 60, false, false);
            AddOrMergeRule(site, "jvs-backend-forwarding", new[] { "/jvs-public/*", "/mgr/*" }, true, 100, 100, false, false);
        }

        private static void AddOrMergeRule(GatewaySiteOptions site, string name, IEnumerable<string> patterns, bool resolveFromUpstreamOriginRoot, int timeoutSeconds, int readWriteTimeoutSeconds, bool treatAsLongPolling, bool treatAsStreaming)
        {
            if (site.PathRules == null)
            {
                site.PathRules = new List<GatewayPathRuleOptions>();
            }

            var rule = site.PathRules.FirstOrDefault(item => item != null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                rule = new GatewayPathRuleOptions
                {
                    Name = name,
                    ResolveFromUpstreamOriginRoot = resolveFromUpstreamOriginRoot,
                    TreatAsLongPolling = treatAsLongPolling,
                    TreatAsStreaming = treatAsStreaming,
                    TimeoutSeconds = timeoutSeconds,
                    ReadWriteTimeoutSeconds = readWriteTimeoutSeconds
                };
                site.PathRules.Add(rule);
            }
            else
            {
                rule.ResolveFromUpstreamOriginRoot = rule.ResolveFromUpstreamOriginRoot || resolveFromUpstreamOriginRoot;
                rule.TreatAsLongPolling = rule.TreatAsLongPolling || treatAsLongPolling;
                rule.TreatAsStreaming = rule.TreatAsStreaming || treatAsStreaming;
                if (rule.TimeoutSeconds <= 0)
                {
                    rule.TimeoutSeconds = timeoutSeconds;
                }

                if (rule.ReadWriteTimeoutSeconds <= 0)
                {
                    rule.ReadWriteTimeoutSeconds = readWriteTimeoutSeconds;
                }
            }

            if (rule.Patterns == null)
            {
                rule.Patterns = new List<string>();
            }

            AddRange(rule.Patterns, patterns);
        }

        private static void AddRange(IList<string> target, IEnumerable<string> values)
        {
            if (target == null || values == null)
            {
                return;
            }

            foreach (var value in values)
            {
                AddDistinct(target, value);
            }
        }

        private static void AddDistinct(IList<string> target, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (var existing in target)
            {
                if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            target.Add(value);
        }
    }
}
