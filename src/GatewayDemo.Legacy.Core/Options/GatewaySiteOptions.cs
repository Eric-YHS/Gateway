using System;
using System.Collections.Generic;
using System.Linq;

namespace GatewayDemo.Legacy.Core.Options
{
    public sealed class GatewaySiteOptions
    {
        private static readonly string[] DefaultAnonymousAllowedPaths =
        {
            "/sys.ashx*",
            "/ashx/sys.ashx*",
            "/datacenter/*",
            "/default.aspx/datacenter/*"
        };

        public string Key { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Accent { get; set; }
        public string UpstreamBaseUrl { get; set; }
        public string EntryPath { get; set; }
        public bool ExposeLegacyAppAtRoot { get; set; }
        public IList<string> HostNames { get; set; }
        public IList<string> RequestMatchPatterns { get; set; }
        public IList<string> AnonymousAllowedPaths { get; set; }
        public IList<string> UpstreamOriginRootPatterns { get; set; }
        public IList<string> PathRuleProfiles { get; set; }
        public IList<GatewayPathRuleProfileOptions> PathRuleProfileDefinitions { get; set; }
        public IList<GatewayPathRuleOptions> PathRules { get; set; }
        public IList<ExternalApiClientOptions> ExternalApiClients { get; set; }
        public IList<string> ExternalApiDefaultPathPatterns { get; set; }
        public LegacyAppProfileOptions LegacyAppProfile { get; set; }
        public bool RedirectToUpstream { get; set; }
        public string LegacyAppVersionResponse { get; set; }
        public string LegacyAppTenantNameResponse { get; set; }

        public GatewaySiteOptions()
        {
            Key = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            Accent = "#0f766e";
            UpstreamBaseUrl = string.Empty;
            EntryPath = string.Empty;
            ExposeLegacyAppAtRoot = false;
            HostNames = new List<string>();
            RequestMatchPatterns = new List<string>();
            AnonymousAllowedPaths = new List<string>(DefaultAnonymousAllowedPaths);
            UpstreamOriginRootPatterns = new List<string>();
            PathRuleProfiles = new List<string>();
            PathRuleProfileDefinitions = new List<GatewayPathRuleProfileOptions>();
            PathRules = new List<GatewayPathRuleOptions>();
            ExternalApiClients = new List<ExternalApiClientOptions>();
            ExternalApiDefaultPathPatterns = new List<string> { "/api/*" };
            LegacyAppProfile = new LegacyAppProfileOptions();
            RedirectToUpstream = false;
            LegacyAppVersionResponse = string.Empty;
            LegacyAppTenantNameResponse = string.Empty;
        }

        public void Normalize()
        {
            Key = (Key ?? string.Empty).Trim();
            Name = string.IsNullOrWhiteSpace(Name) ? Key : Name.Trim();
            Description = (Description ?? string.Empty).Trim();
            Accent = string.IsNullOrWhiteSpace(Accent) ? "#0f766e" : Accent.Trim();
            UpstreamBaseUrl = string.IsNullOrWhiteSpace(UpstreamBaseUrl)
                ? string.Empty
                : UpstreamBaseUrl.TrimEnd('/') + "/";
            EntryPath = string.IsNullOrWhiteSpace(EntryPath)
                ? string.Empty
                : EntryPath.Trim().TrimStart('/');
            HostNames = NormalizeList(HostNames);
            RequestMatchPatterns = NormalizeList(RequestMatchPatterns);
            AnonymousAllowedPaths = NormalizeList(AnonymousAllowedPaths);
            UpstreamOriginRootPatterns = NormalizeList(UpstreamOriginRootPatterns);
            PathRuleProfiles = NormalizeList(PathRuleProfiles);
            PathRuleProfileDefinitions = NormalizePathRuleProfiles(PathRuleProfileDefinitions);
            GatewayPathRuleProfileCatalog.ApplyProfiles(this);
            AnonymousAllowedPaths = NormalizeList(AnonymousAllowedPaths);
            UpstreamOriginRootPatterns = NormalizeList(UpstreamOriginRootPatterns);
            PathRules = NormalizePathRules(PathRules);
            ExternalApiClients = NormalizeExternalApiClients(ExternalApiClients);
            ExternalApiDefaultPathPatterns = NormalizeList(ExternalApiDefaultPathPatterns);
            if (ExternalApiDefaultPathPatterns.Count == 0)
            {
                ExternalApiDefaultPathPatterns = new List<string> { "/api/*" };
            }

            LegacyAppProfile = LegacyAppProfile ?? new LegacyAppProfileOptions();
            LegacyAppProfile.Normalize();
            LegacyAppVersionResponse = (LegacyAppVersionResponse ?? string.Empty).Trim();
            LegacyAppTenantNameResponse = (LegacyAppTenantNameResponse ?? string.Empty).Trim();
        }

        private static IList<string> NormalizeList(IEnumerable<string> values)
        {
            return (values ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IList<GatewayPathRuleOptions> NormalizePathRules(IEnumerable<GatewayPathRuleOptions> values)
        {
            var rules = new List<GatewayPathRuleOptions>();
            foreach (var rule in values ?? new List<GatewayPathRuleOptions>())
            {
                if (rule == null)
                {
                    continue;
                }

                rule.Normalize();
                if (rule.Patterns.Count > 0)
                {
                    rules.Add(rule);
                }
            }

            return rules;
        }

        private static IList<GatewayPathRuleProfileOptions> NormalizePathRuleProfiles(IEnumerable<GatewayPathRuleProfileOptions> values)
        {
            var profiles = new List<GatewayPathRuleProfileOptions>();
            foreach (var profile in values ?? new List<GatewayPathRuleProfileOptions>())
            {
                if (profile == null)
                {
                    continue;
                }

                profile.Normalize();
                if (!string.IsNullOrWhiteSpace(profile.Name))
                {
                    profiles.Add(profile);
                }
            }

            return profiles;
        }

        private static IList<ExternalApiClientOptions> NormalizeExternalApiClients(IEnumerable<ExternalApiClientOptions> values)
        {
            var clients = new List<ExternalApiClientOptions>();
            foreach (var client in values ?? new List<ExternalApiClientOptions>())
            {
                if (client == null)
                {
                    continue;
                }

                client.Normalize();
                if (!string.IsNullOrWhiteSpace(client.ClientId))
                {
                    clients.Add(client);
                }
            }

            return clients;
        }
    }
}
