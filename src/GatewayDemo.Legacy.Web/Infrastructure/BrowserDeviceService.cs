using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using GatewayDemo.Legacy.Core.Models;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    public sealed class BrowserDeviceService
    {
        // 同一物理浏览器几乎同时对多个站点发起首次请求时，两个请求都还没有设备 Cookie，
        // 若各自独立铸造新设备会拆成两个 DeviceId（同设备的两次站点申请因此被拆散，只有
        // 其中一个能被浏览器最终保留的 Cookie 覆盖到）。这里用"同一客户端IP + 同一环境指纹"
        // 在极短窗口内复用刚铸造的 Token，把这类并发首次访问归并回同一个设备。
        // 权衡：窗口越长越能兜住"用户手速慢/网络抖动"下的并发，但也越可能把"同一NAT出口+
        // 同一浏览器版本(如公司统一镜像)的两台不同真实设备"误合并成一个 DeviceId；取 2 秒是
        // 在两者间的折中——实测同浏览器双标签页竞态窗口通常在数百毫秒量级，2 秒已足够覆盖，
        // 同时明显短于"两个不同真实用户凑巧同秒访问"的现实概率窗口。
        private static readonly ConcurrentDictionary<string, FirstContactToken> RecentFirstContactTokens =
            new ConcurrentDictionary<string, FirstContactToken>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, object> FirstContactLocks =
            new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        private static readonly TimeSpan FirstContactReuseWindow = TimeSpan.FromSeconds(2);

        private readonly LegacyGatewayConfiguration configuration;
        private readonly LegacyGatewayRepository repository;

        public BrowserDeviceService(LegacyGatewayConfiguration configuration, LegacyGatewayRepository repository)
        {
            this.configuration = configuration;
            this.repository = repository;
        }

        public DeviceContext GetOrCreateContext(HttpContext context)
        {
            var clientIp = ClientIpResolver.GetClientIp(context, configuration.Gateway);
            var userAgent = context.Request.UserAgent ?? string.Empty;
            var signalHash = BuildSignalHash(context, configuration.Gateway);
            var isWebSocketUpgrade = IsWebSocketUpgradeRequest(context);
            var requestToken = context.Request.Cookies[configuration.Gateway.DeviceCookieName] == null
                ? string.Empty
                : context.Request.Cookies[configuration.Gateway.DeviceCookieName].Value;

            GatewayManagedDevice device = null;
            var tokenChanged = string.IsNullOrWhiteSpace(requestToken);
            if (!string.IsNullOrWhiteSpace(requestToken))
            {
                device = repository.GetManagedDeviceByBrowserTokenHash(ComputeHash(requestToken));
                if (device == null)
                {
                    tokenChanged = true;
                }
            }

            if (device == null)
            {
                device = CreateOrReuseFirstContactDevice(clientIp, signalHash, userAgent, out requestToken);
                tokenChanged = true;
            }
            else
            {
                var requiresReview = configuration.Gateway.BrowserDeviceReviewOnSignalChange
                    && !isWebSocketUpgrade
                    && !string.Equals(device.SignalHash, signalHash, StringComparison.Ordinal);
                var observedSignalHash = isWebSocketUpgrade ? device.SignalHash : signalHash;
                repository.UpdateManagedDeviceObservation(
                    device.DeviceId,
                    observedSignalHash,
                    userAgent,
                    clientIp,
                    requiresReview,
                    requiresReview ? "浏览器环境指纹发生变化，需要重新审核该设备。" : string.Empty);
                device = repository.GetManagedDeviceByDeviceId(device.DeviceId);
                if (isWebSocketUpgrade && device != null)
                {
                    signalHash = device.SignalHash;
                }
            }

            // Only set cookie when token is new or has changed, reducing response header bloat.
            if (tokenChanged)
            {
                var cookie = new HttpCookie(configuration.Gateway.DeviceCookieName, requestToken);
                cookie.HttpOnly = true;
                cookie.Path = "/";
                cookie.Expires = DateTime.UtcNow.AddYears(1);
                var cookieDomain = GatewayRequestContext.ResolveDeviceCookieDomain(context, configuration.Gateway);
                if (!string.IsNullOrWhiteSpace(cookieDomain))
                {
                    cookie.Domain = cookieDomain;
                }

                if (GatewayRequestContext.ShouldUseSecureDeviceCookie(context, configuration.Gateway))
                {
                    cookie.Secure = true;
                }

                context.Response.Cookies.Set(cookie);
            }

            return new DeviceContext
            {
                DeviceId = device.DeviceId,
                DeviceCode = device.DeviceCode,
                SignalHash = signalHash,
                UserAgent = string.IsNullOrWhiteSpace(userAgent) ? "未知" : userAgent,
                ClientIp = clientIp,
                CredentialChannel = "browser-cookie",
                TrustState = device.TrustState,
                ChallengeReason = device.ChallengeReason,
                HasAppCredential = device.HasAppCredential,
                AppCredentialKey = string.Empty
            };
        }

        private GatewayManagedDevice CreateOrReuseFirstContactDevice(string clientIp, string signalHash, string userAgent, out string requestToken)
        {
            var fingerprintKey = BuildFirstContactKey(clientIp, signalHash);
            var lockObject = FirstContactLocks.GetOrAdd(fingerprintKey, delegate { return new object(); });
            lock (lockObject)
            {
                FirstContactToken recent;
                if (RecentFirstContactTokens.TryGetValue(fingerprintKey, out recent)
                    && DateTime.UtcNow - recent.CreatedAtUtc <= FirstContactReuseWindow)
                {
                    var recentDevice = repository.GetManagedDeviceByBrowserTokenHash(ComputeHash(recent.Token));
                    if (recentDevice != null)
                    {
                        requestToken = recent.Token;
                        return recentDevice;
                    }
                }

                requestToken = BuildToken();
                var created = repository.CreateBrowserManagedDevice(
                    ComputeHash(requestToken),
                    signalHash,
                    userAgent,
                    clientIp);
                RecentFirstContactTokens[fingerprintKey] = new FirstContactToken(requestToken, DateTime.UtcNow);
                return created;
            }
        }

        private static string BuildFirstContactKey(string clientIp, string signalHash)
        {
            return (clientIp ?? string.Empty).Trim().ToLowerInvariant() + "|" + (signalHash ?? string.Empty);
        }

        private static string BuildSignalHash(HttpContext context, GatewayOptions gatewayOptions)
        {
            var signalSource = BuildSignalSource(context, gatewayOptions);
            return ComputeHash(signalSource).Substring(0, 12);
        }

        private static string BuildSignalSource(HttpContext context, GatewayOptions gatewayOptions)
        {
            IEnumerable<string> headers = gatewayOptions == null || gatewayOptions.BrowserDeviceSignalHeaders == null || gatewayOptions.BrowserDeviceSignalHeaders.Count == 0
                ? new[] { "User-Agent" }
                : gatewayOptions.BrowserDeviceSignalHeaders;
            var builder = new StringBuilder();
            foreach (var headerName in headers)
            {
                var name = (headerName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var value = string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase)
                    ? context.Request.UserAgent ?? string.Empty
                    : context.Request.Headers[name] ?? string.Empty;
                if (builder.Length > 0)
                {
                    builder.Append("|");
                }

                builder.Append(name.ToLowerInvariant())
                    .Append("=")
                    .Append(NormalizeSignalValue(name, value));
            }

            return builder.Length == 0
                ? "user-agent=" + NormalizeSignalValue("User-Agent", context.Request.UserAgent ?? string.Empty)
                : builder.ToString();
        }

        private static string NormalizeSignalValue(string headerName, string value)
        {
            var normalized = value ?? string.Empty;
            if (!string.Equals(headerName, "Sec-CH-UA-Mobile", StringComparison.OrdinalIgnoreCase))
            {
                normalized = Regex.Replace(normalized, @"\d+(?:[._]\d+)*", "#");
            }

            normalized = Regex.Replace(normalized, @"\s+", " ").Trim().ToLowerInvariant();
            return normalized;
        }

        private static bool IsWebSocketUpgradeRequest(HttpContext context)
        {
            if (context == null || context.Request == null)
            {
                return false;
            }

            var upgrade = context.Request.Headers["Upgrade"] ?? string.Empty;
            var connection = context.Request.Headers["Connection"] ?? string.Empty;
            return string.Equals(upgrade.Trim(), "websocket", StringComparison.OrdinalIgnoreCase)
                && connection.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Base64UrlEncode(bytes);
        }

        private static string ComputeHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var item in hash)
                {
                    builder.Append(item.ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private sealed class FirstContactToken
        {
            public FirstContactToken(string token, DateTime createdAtUtc)
            {
                Token = token;
                CreatedAtUtc = createdAtUtc;
            }

            public string Token { get; private set; }
            public DateTime CreatedAtUtc { get; private set; }
        }
    }
}
