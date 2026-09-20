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
        public int TimeoutMilliseconds { get; set; }
        public int ReadWriteTimeoutMilliseconds { get; set; }
        public int TimeoutSeconds { get; set; }
        public int ReadWriteTimeoutSeconds { get; set; }

        public GatewayPathRuleOptions()
        {
            Name = string.Empty;
            Patterns = new List<string>();
            ResolveFromUpstreamOriginRoot = false;
            TreatAsLongPolling = false;
            TreatAsStreaming = false;
            TimeoutMilliseconds = 0;
            ReadWriteTimeoutMilliseconds = 0;
            TimeoutSeconds = 0;
            ReadWriteTimeoutSeconds = 0;
        }

        public void Normalize()
        {
            Name = (Name ?? string.Empty).Trim();
            Patterns = NormalizeList(Patterns);
            TimeoutMilliseconds = ValidateMilliseconds(TimeoutMilliseconds, "TimeoutMilliseconds");
            ReadWriteTimeoutMilliseconds = ValidateMilliseconds(ReadWriteTimeoutMilliseconds, "ReadWriteTimeoutMilliseconds");
            TimeoutSeconds = ValidateLegacySeconds(TimeoutSeconds, "TimeoutSeconds");
            ReadWriteTimeoutSeconds = ValidateLegacySeconds(ReadWriteTimeoutSeconds, "ReadWriteTimeoutSeconds");

            if (TimeoutMilliseconds > 0 && TimeoutSeconds > 0)
            {
                throw new InvalidOperationException("Gateway path rule cannot set both TimeoutSeconds and TimeoutMilliseconds.");
            }

            if (ReadWriteTimeoutMilliseconds > 0 && ReadWriteTimeoutSeconds > 0)
            {
                throw new InvalidOperationException("Gateway path rule cannot set both ReadWriteTimeoutSeconds and ReadWriteTimeoutMilliseconds.");
            }
        }

        private static int ValidateMilliseconds(int value, string propertyName)
        {
            if (value < 0 || value > 86400000)
            {
                throw new InvalidOperationException(
                    "Gateway path rule " + propertyName + " 的单位是毫秒，取值必须为 0 或 1~86400000。当前配置值：" + value);
            }

            return value;
        }

        private static int ValidateLegacySeconds(int value, string propertyName)
        {
            if (value < 0 || value > 86400)
            {
                throw new InvalidOperationException(
                    "Gateway path rule " + propertyName + " 的单位是秒，取值必须为 0 或 1~86400。"
                    + "若要配置毫秒，请改用 " + propertyName.Replace("Seconds", "Milliseconds") + "。"
                    + " 当前配置值：" + value);
            }

            return value;
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
