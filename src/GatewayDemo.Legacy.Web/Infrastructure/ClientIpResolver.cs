using System;
using System.Linq;
using System.Net;
using System.Web;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal static class ClientIpResolver
    {
        public static string GetClientIp(HttpContext context, GatewayOptions gatewayOptions)
        {
            var remoteAddress = GetRemoteAddress(context);
            if (IsTrustedProxy(remoteAddress, gatewayOptions))
            {
                var forwardedAddress = GetFirstForwardedAddress(context);
                if (!string.IsNullOrWhiteSpace(forwardedAddress))
                {
                    return forwardedAddress;
                }
            }

            return string.IsNullOrWhiteSpace(remoteAddress) ? "未知" : remoteAddress;
        }

        public static string BuildForwardedFor(HttpContext context, GatewayOptions gatewayOptions)
        {
            var remoteAddress = GetRemoteAddress(context);
            var incoming = context == null ? string.Empty : (context.Request.Headers["X-Forwarded-For"] ?? string.Empty).Trim();
            if (IsTrustedProxy(remoteAddress, gatewayOptions) && !string.IsNullOrWhiteSpace(incoming))
            {
                return string.IsNullOrWhiteSpace(remoteAddress)
                    ? incoming
                    : incoming + ", " + remoteAddress;
            }

            return string.IsNullOrWhiteSpace(remoteAddress) ? "未知" : remoteAddress;
        }

        public static bool IsTrustedProxy(HttpContext context, GatewayOptions gatewayOptions)
        {
            return IsTrustedProxy(GetRemoteAddress(context), gatewayOptions);
        }

        private static string GetRemoteAddress(HttpContext context)
        {
            return context == null || context.Request == null
                ? string.Empty
                : NormalizeAddress(context.Request.UserHostAddress);
        }

        private static string GetFirstForwardedAddress(HttpContext context)
        {
            var forwardedFor = context == null ? string.Empty : context.Request.Headers["X-Forwarded-For"];
            if (string.IsNullOrWhiteSpace(forwardedFor))
            {
                return string.Empty;
            }

            var first = forwardedFor.Split(',')[0].Trim();
            return NormalizeAddress(first);
        }

        private static bool IsTrustedProxy(string remoteAddress, GatewayOptions gatewayOptions)
        {
            IPAddress remoteIp;
            if (gatewayOptions == null
                || string.IsNullOrWhiteSpace(remoteAddress)
                || !IPAddress.TryParse(remoteAddress, out remoteIp))
            {
                return false;
            }

            if (gatewayOptions.TrustedProxyAddresses != null
                && gatewayOptions.TrustedProxyAddresses.Any(address => IsSameAddress(remoteIp, address)))
            {
                return true;
            }

            return gatewayOptions.TrustedProxyCidrs != null
                && gatewayOptions.TrustedProxyCidrs.Any(cidr => IsInCidr(remoteIp, cidr));
        }

        private static bool IsSameAddress(IPAddress remoteIp, string configuredAddress)
        {
            IPAddress configuredIp;
            return IPAddress.TryParse(NormalizeAddress(configuredAddress), out configuredIp)
                && remoteIp.Equals(configuredIp);
        }

        private static bool IsInCidr(IPAddress remoteIp, string cidr)
        {
            if (string.IsNullOrWhiteSpace(cidr))
            {
                return false;
            }

            var parts = cidr.Trim().Split('/');
            if (parts.Length != 2)
            {
                return false;
            }

            IPAddress networkIp;
            int prefixLength;
            if (!IPAddress.TryParse(NormalizeAddress(parts[0]), out networkIp)
                || !int.TryParse(parts[1], out prefixLength))
            {
                return false;
            }

            var remoteBytes = remoteIp.GetAddressBytes();
            var networkBytes = networkIp.GetAddressBytes();
            if (remoteBytes.Length != networkBytes.Length || prefixLength < 0 || prefixLength > remoteBytes.Length * 8)
            {
                return false;
            }

            var fullBytes = prefixLength / 8;
            var remainingBits = prefixLength % 8;
            for (var i = 0; i < fullBytes; i++)
            {
                if (remoteBytes[i] != networkBytes[i])
                {
                    return false;
                }
            }

            if (remainingBits == 0)
            {
                return true;
            }

            var mask = (byte)(0xFF << (8 - remainingBits));
            return (remoteBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
        }

        private static string NormalizeAddress(string value)
        {
            var address = (value ?? string.Empty).Trim();
            if (address.StartsWith("[", StringComparison.Ordinal))
            {
                var endBracket = address.IndexOf(']');
                if (endBracket > 0)
                {
                    return address.Substring(1, endBracket - 1);
                }
            }

            var colonIndex = address.IndexOf(':');
            if (colonIndex > 0 && colonIndex == address.LastIndexOf(':'))
            {
                var possiblePort = address.Substring(colonIndex + 1);
                int port;
                if (int.TryParse(possiblePort, out port))
                {
                    return address.Substring(0, colonIndex);
                }
            }

            return address;
        }
    }
}
