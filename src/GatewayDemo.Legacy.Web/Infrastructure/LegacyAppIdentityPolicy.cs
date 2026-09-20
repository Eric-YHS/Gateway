using System;
using System.Collections.Generic;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal sealed class LegacyAppIdentitySignals
    {
        public string UrlHost { get; set; }
        public string CompanyId { get; set; }
        public string UserId { get; set; }
        public string DataCenterId { get; set; }
        public string DeviceImei { get; set; }
        public string MachineId { get; set; }
        public string AppKey { get; set; }
        public string AppName { get; set; }
        public string DeviceVersion { get; set; }
        public string AppVersion { get; set; }
        public string MobileUserNum { get; set; }
        public string DeviceNo { get; set; }

        public LegacyAppIdentitySignals()
        {
            UrlHost = string.Empty;
            CompanyId = string.Empty;
            UserId = string.Empty;
            DataCenterId = string.Empty;
            DeviceImei = string.Empty;
            MachineId = string.Empty;
            AppKey = string.Empty;
            AppName = string.Empty;
            DeviceVersion = string.Empty;
            AppVersion = string.Empty;
            MobileUserNum = string.Empty;
            DeviceNo = string.Empty;
        }
    }

    internal static class LegacyAppIdentityPolicy
    {
        public static IList<string> BuildIdentitySources(LegacyAppIdentitySignals signals, bool allowAccountOnlyFallback)
        {
            var sources = new List<string>();
            if (signals == null)
            {
                return sources;
            }

            AddDeviceBoundIdentitySources(sources, signals);

            if (sources.Count == 0)
            {
                AddIdentitySource(
                    sources,
                    signals.MachineId,
                    signals.AppKey,
                    signals.AppName,
                    signals.DeviceVersion,
                    signals.AppVersion,
                    signals.MobileUserNum,
                    signals.DeviceNo);
            }

            if (allowAccountOnlyFallback)
            {
                AddAccountScopedIdentitySources(sources, signals);
            }

            return sources;
        }

        private static void AddDeviceBoundIdentitySources(IList<string> sources, LegacyAppIdentitySignals signals)
        {
            AddIdentitySource(sources, signals.UrlHost, signals.UserId, signals.DataCenterId, signals.DeviceImei);
            AddIdentitySource(sources, signals.CompanyId, signals.UrlHost, signals.UserId, signals.DataCenterId, signals.DeviceImei);
            AddIdentitySource(sources, signals.CompanyId, signals.UserId, signals.DataCenterId, signals.DeviceImei);
            AddIdentitySource(sources, signals.UserId, signals.DataCenterId, signals.DeviceImei);
            AddIdentitySource(sources, signals.CompanyId, signals.DataCenterId, signals.DeviceImei);
            AddIdentitySource(sources, signals.DataCenterId, signals.DeviceImei);
            AddIdentitySource(sources, signals.DeviceImei);
            AddIdentitySource(sources, signals.MachineId);
            AddIdentitySource(sources, signals.DeviceNo);
        }

        private static void AddAccountScopedIdentitySources(IList<string> sources, LegacyAppIdentitySignals signals)
        {
            AddIdentitySource(sources, signals.UrlHost, signals.UserId, signals.DataCenterId);
            AddIdentitySource(sources, signals.CompanyId, signals.UserId, signals.DataCenterId);
            AddIdentitySource(sources, signals.UserId, signals.DataCenterId);
        }

        private static void AddIdentitySource(IList<string> sources, params string[] parts)
        {
            if (sources == null || parts == null || !HasStableIdentityParts(parts))
            {
                return;
            }

            AddDistinct(sources, string.Join("|", parts));
        }

        private static bool HasStableIdentityParts(IEnumerable<string> parts)
        {
            if (parts == null)
            {
                return false;
            }

            var count = 0;
            var single = string.Empty;
            foreach (var part in parts)
            {
                var value = (part ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                count++;
                single = value;
            }

            if (count >= 2)
            {
                return true;
            }

            return count == 1 && LooksLikeStableDeviceIdentifier(single);
        }

        private static bool LooksLikeStableDeviceIdentifier(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (LooksLikeDeviceIdentifier(normalized))
            {
                return true;
            }

            if (normalized.Length < 20)
            {
                return false;
            }

            for (var i = 0; i < normalized.Length; i++)
            {
                if (char.IsDigit(normalized[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeDeviceIdentifier(string value)
        {
            Guid ignored;
            if (Guid.TryParse(value, out ignored))
            {
                return true;
            }

            return value.Length >= 30 && value.IndexOf('-') >= 0;
        }

        private static void AddDistinct(IList<string> values, string value)
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
    }
}
