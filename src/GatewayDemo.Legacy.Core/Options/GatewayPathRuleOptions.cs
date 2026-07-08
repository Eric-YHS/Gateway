using System;
using System.Collections.Generic;
using System.Linq;

namespace GatewayDemo.Legacy.Core.Options
{
    public sealed class GatewayPathRuleOptions
    {
        public string Name { get; set; }
        public IList<string> Patterns { get; set; }
        public bool ResolveFromUpstreamOriginRoot { get; set; }
        public bool TreatAsLongPolling { get; set; }
        public bool TreatAsStreaming { get; set; }
        public int TimeoutSeconds { get; set; }
        public int ReadWriteTimeoutSeconds { get; set; }

        public GatewayPathRuleOptions()
        {
            Name = string.Empty;
            Patterns = new List<string>();
            ResolveFromUpstreamOriginRoot = false;
            TreatAsLongPolling = false;
            TreatAsStreaming = false;
            TimeoutSeconds = 0;
            ReadWriteTimeoutSeconds = 0;
        }

        public void Normalize()
        {
            Name = (Name ?? string.Empty).Trim();
            Patterns = NormalizeList(Patterns);
        }

        private static IList<string> NormalizeList(IEnumerable<string> values)
        {
            return (values ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
