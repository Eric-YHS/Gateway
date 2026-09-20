using System;
using System.Collections.Generic;
using System.Linq;

namespace GatewayDemo.Legacy.Core.Options
{
    public sealed class ExternalApiClientOptions
    {
        public string ClientId { get; set; }
        public bool Enabled { get; set; }
        public IList<string> AllowedIpRanges { get; set; }
        public IList<string> AllowedSiteKeys { get; set; }
        public IList<string> AllowedPathPatterns { get; set; }
        public string Contact { get; set; }
        public string Description { get; set; }

        public ExternalApiClientOptions()
        {
            ClientId = string.Empty;
            Enabled = false;
            AllowedIpRanges = new List<string>();
            AllowedSiteKeys = new List<string>();
            AllowedPathPatterns = new List<string>();
            Contact = string.Empty;
            Description = string.Empty;
        }

        public void Normalize()
        {
            ClientId = (ClientId ?? string.Empty).Trim();
            AllowedIpRanges = NormalizeList(AllowedIpRanges);
            AllowedSiteKeys = NormalizeList(AllowedSiteKeys);
            AllowedPathPatterns = NormalizeList(AllowedPathPatterns);
            Contact = (Contact ?? string.Empty).Trim();
            Description = (Description ?? string.Empty).Trim();
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
