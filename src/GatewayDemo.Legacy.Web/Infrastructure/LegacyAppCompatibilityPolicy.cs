using System;
using System.Collections.Generic;
using System.Web;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal static class LegacyAppCompatibilityPolicy
    {
        public static LegacyAppProfileOptions GetProfile(GatewaySiteOptions site)
        {
            var profile = site == null ? null : site.LegacyAppProfile;
            if (profile == null)
            {
                profile = new LegacyAppProfileOptions();
                profile.Normalize();
            }

            return profile;
        }

        public static bool MatchesPath(string requestPath, IEnumerable<string> patterns)
        {
            if (string.IsNullOrWhiteSpace(requestPath) || patterns == null)
            {
                return false;
            }

            var normalizedPath = requestPath.Trim().Replace("\\", "/");
            if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
            {
                normalizedPath = "/" + normalizedPath.TrimStart('/');
            }

            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                var normalizedPattern = pattern.Trim().Replace("\\", "/");
                if (normalizedPattern.IndexOf('*') >= 0)
                {
                    if (!normalizedPattern.StartsWith("/", StringComparison.Ordinal)
                        && !normalizedPattern.StartsWith("*", StringComparison.Ordinal))
                    {
                        normalizedPattern = "/" + normalizedPattern.TrimStart('/');
                    }

                    if (GatewayPathUtility.WildcardMatch(normalizedPath, normalizedPattern))
                    {
                        return true;
                    }

                    continue;
                }

                if (!normalizedPattern.StartsWith("/", StringComparison.Ordinal))
                {
                    normalizedPattern = "/" + normalizedPattern.TrimStart('/');
                }

                if (string.Equals(normalizedPath, normalizedPattern, StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.EndsWith(normalizedPattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsConfiguredAction(string action, IEnumerable<string> configuredActions)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            foreach (var item in configuredActions ?? new List<string>())
            {
                if (string.Equals(item, action.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryHandleCompatibilityRequest(HttpContext context, GatewaySiteOptions site)
        {
            if (context == null || context.Request == null || site == null)
            {
                return false;
            }

            var requestPath = context.Request.Path ?? string.Empty;
            var profile = GetProfile(site);
            if (!profile.Enabled || !IsCompatibilityPath(requestPath, profile))
            {
                return false;
            }

            var action = (context.Request["action"] ?? string.Empty).Trim();
            if (IsConfiguredAction(action, profile.VersionActions) || string.IsNullOrWhiteSpace(action))
            {
                if (!string.IsNullOrWhiteSpace(site.LegacyAppVersionResponse))
                {
                    LegacyAppResponseWriter.WritePlainText(context, site.LegacyAppVersionResponse);
                    return true;
                }

                return false;
            }

            if (IsConfiguredAction(action, profile.TenantNameActions))
            {
                if (!string.IsNullOrWhiteSpace(site.LegacyAppTenantNameResponse))
                {
                    LegacyAppResponseWriter.WritePlainText(context, site.LegacyAppTenantNameResponse);
                    return true;
                }

                return false;
            }

            return false;
        }

        public static bool IsCompatibilityPath(string requestPath, LegacyAppProfileOptions profile)
        {
            return profile != null && MatchesPath(requestPath, profile.BootstrapPathPatterns);
        }

        public static bool IsRootCandidatePath(string requestPath, GatewaySiteOptions site)
        {
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                return false;
            }

            var profile = GetProfile(site);
            return profile.Enabled && MatchesPath(requestPath, profile.RootCandidatePathPatterns);
        }

        public static bool LooksLikeMobileAppRequest(HttpContext context, GatewaySiteOptions site)
        {
            if (context == null || context.Request == null)
            {
                return false;
            }

            var path = (context.Request.Path ?? string.Empty).ToLowerInvariant();
            var profile = GetProfile(site);
            if (!MobileClientDetector.IsMobileClientRequest(context.Request, profile))
            {
                return false;
            }

            return profile.Enabled
                && (MatchesPath(path, profile.BootstrapPathPatterns)
                    || MatchesPath(path, profile.LoginPathPatterns)
                    || MatchesPath(path, profile.ServicePathPatterns)
                    || MatchesPath(path, profile.UploadPathPatterns)
                    || MatchesPath(path, profile.RootCandidatePathPatterns));
        }

        public static bool IsMobileAppRootProbe(HttpContext context, string requestPath)
        {
            if (context == null || context.Request == null || !MobileClientDetector.IsNativeAppHealthProbeRequest(context.Request))
            {
                return false;
            }

            var path = string.IsNullOrWhiteSpace(requestPath) ? "/" : requestPath.Trim();
            return string.Equals(path, "/", StringComparison.Ordinal);
        }
    }
}
