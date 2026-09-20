using System;
using System.Collections.Generic;
using System.Linq;

namespace GatewayDemo.Legacy.Core.Options
{
    public sealed class GatewayPathRuleProfileOptions
    {
        public string Name { get; set; }
        public IList<string> AnonymousAllowedPaths { get; set; }
        public IList<string> UpstreamOriginRootPatterns { get; set; }
        public IList<GatewayPathRuleOptions> PathRules { get; set; }

        public GatewayPathRuleProfileOptions()
        {
            Name = string.Empty;
            AnonymousAllowedPaths = new List<string>();
            UpstreamOriginRootPatterns = new List<string>();
            PathRules = new List<GatewayPathRuleOptions>();
        }

        public void Normalize()
        {
            Name = (Name ?? string.Empty).Trim();
            AnonymousAllowedPaths = NormalizeList(AnonymousAllowedPaths);
            UpstreamOriginRootPatterns = NormalizeList(UpstreamOriginRootPatterns);
            PathRules = NormalizeRules(PathRules);
        }

        private static IList<string> NormalizeList(IEnumerable<string> values)
        {
            return (values ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IList<GatewayPathRuleOptions> NormalizeRules(IEnumerable<GatewayPathRuleOptions> values)
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
    }
}
