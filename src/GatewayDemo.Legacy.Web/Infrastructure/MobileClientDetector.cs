using System;
using System.Collections.Generic;
using System.Web;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal static class MobileClientDetector
    {
        private static readonly string[] UserAgentMarkers =
        {
            "android",
            "iphone",
            "ipad",
            "ipod",
            "windows phone",
            "iemobile",
            "blackberry",
            "bb10",
            "mobile",
            "micromessenger",
            "wxwork",
            "miniprogram",
            "alipayclient",
            "dingtalk",
            "lark",
            "feishu",
            "mqqbrowser",
            "ucbrowser",
            "opera mini",
            "webview",
            " wv",
            "okhttp",
            "dalvik",
            "html5plus",
            "uni-app",
            "dcloud",
            "plusruntime"
        };

        private static readonly string[] PlatformMarkers =
        {
            "android",
            "ios",
            "iphone",
            "ipad",
            "ipados",
            "harmonyos",
            "windows phone"
        };

        // 只覆盖"根本不是浏览器"的原生 HTTP 客户端/应用运行时标识——不含 android/iphone/mobile/
        // 微信/钉钉/企业微信/飞书/UC/QQ浏览器等一长串真人也会用来点开链接浏览的标识。用于站点根路径
        // 的"原生App健康探测"短路判断（IsMobileAppRootProbe），避免把真实用户的手机浏览器/内嵌浏览器
        // 误判成健康探测请求、卡在一段裸JSON上无法继续操作。
        private static readonly string[] NativeRuntimeOnlyMarkers =
        {
            "okhttp",
            "dalvik",
            "html5plus",
            "uni-app",
            "dcloud",
            "plusruntime"
        };

        public static bool IsMobileClientRequest(HttpRequest request)
        {
            if (request == null)
            {
                return false;
            }

            return HasMobileClientHints(request)
                || ContainsAny(request.UserAgent, UserAgentMarkers)
                || LooksLikeMobileBridgeHeader(request);
        }

        public static bool IsMobileClientRequest(HttpRequest request, LegacyAppProfileOptions profile)
        {
            if (request == null)
            {
                return false;
            }

            return IsMobileClientRequest(request)
                || ContainsAny(request.UserAgent, profile == null ? null : profile.MobileUserAgentMarkers);
        }

        public static bool IsNativeAppHealthProbeRequest(HttpRequest request)
        {
            if (request == null)
            {
                return false;
            }

            return ContainsAny(request.UserAgent, NativeRuntimeOnlyMarkers)
                || LooksLikeMobileBridgeHeader(request);
        }

        private static bool HasMobileClientHints(HttpRequest request)
        {
            var mobileHint = (request.Headers["Sec-CH-UA-Mobile"] ?? string.Empty).Trim();
            if (string.Equals(mobileHint, "?1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mobileHint, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mobileHint, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var platformHint = (request.Headers["Sec-CH-UA-Platform"] ?? string.Empty).Trim().Trim('"');
            return ContainsAny(platformHint, PlatformMarkers);
        }

        private static bool LooksLikeMobileBridgeHeader(HttpRequest request)
        {
            var requestedWith = (request.Headers["X-Requested-With"] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(requestedWith)
                && !string.Equals(requestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                if (requestedWith.StartsWith("com.", StringComparison.OrdinalIgnoreCase)
                    || ContainsAny(requestedWith, UserAgentMarkers))
                {
                    return true;
                }
            }

            return ContainsAny(request.Headers["X-Client-Platform"], PlatformMarkers)
                || ContainsAny(request.Headers["X-App-Platform"], PlatformMarkers)
                || ContainsAny(request.Headers["X-Device-Platform"], PlatformMarkers);
        }

        private static bool ContainsAny(string input, IEnumerable<string> markers)
        {
            if (string.IsNullOrWhiteSpace(input) || markers == null)
            {
                return false;
            }

            var normalized = input.ToLowerInvariant();
            foreach (var marker in markers)
            {
                var normalizedMarker = string.IsNullOrWhiteSpace(marker)
                    ? string.Empty
                    : marker.Trim().ToLowerInvariant();
                if (normalizedMarker.Length > 0
                    && normalized.IndexOf(normalizedMarker, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
