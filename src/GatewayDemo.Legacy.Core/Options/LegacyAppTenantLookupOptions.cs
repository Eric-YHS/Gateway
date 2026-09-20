using System;
using System.Collections.Generic;
using System.Linq;

namespace GatewayDemo.Legacy.Core.Options
{
    public sealed class LegacyAppTenantLookupOptions
    {
        public bool Enabled { get; set; }
        public string Path { get; set; }
        public string Query { get; set; }
        public int TimeoutMs { get; set; }
        public IList<string> ResponseValuePaths { get; set; }

        public LegacyAppTenantLookupOptions()
        {
            Enabled = true;
            Path = "ashx/Sys.ashx";
            Query = "action=tenant_name&DataCenterId={DataCenterId}";
            TimeoutMs = 2000;
            ResponseValuePaths = new List<string>
            {
                "tenant_name",
                "TenantName",
                "CompanyId",
                "CompanyName",
                "Data",
                "data.tenant_name",
                "data.TenantName",
                "data.CompanyId",
                "data.CompanyName"
            };
        }

        public void Normalize()
        {
            Path = string.IsNullOrWhiteSpace(Path)
                ? "ashx/Sys.ashx"
                : Path.Trim().TrimStart('/');
            Query = string.IsNullOrWhiteSpace(Query)
                ? "action=tenant_name&DataCenterId={DataCenterId}"
                : Query.Trim().TrimStart('?');
            if (TimeoutMs <= 0)
            {
                TimeoutMs = 2000;
            }

            ResponseValuePaths = NormalizeList(ResponseValuePaths);
            if (ResponseValuePaths.Count == 0)
            {
                ResponseValuePaths = new LegacyAppTenantLookupOptions().ResponseValuePaths;
            }
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
