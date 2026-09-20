using System;
using System.Web;
using System.Web.Caching;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal static class GatewayTargetStore
    {
        private const int InlineTargetThreshold = 1500;
        private const string CachePrefix = "Gateway.Target.";
        private static readonly TimeSpan TargetTtl = TimeSpan.FromMinutes(20);

        public static string BuildGatewayUrl(string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target.Length <= InlineTargetThreshold)
            {
                return "/Gateway/Default.aspx?target=" + HttpUtility.UrlEncode(target ?? string.Empty);
            }

            return "/Gateway/Default.aspx?targetKey=" + HttpUtility.UrlEncode(Store(target));
        }

        public static string Resolve(HttpRequest request)
        {
            if (request == null)
            {
                return string.Empty;
            }

            if (string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                return request.Form["targetPath"];
            }

            var targetKey = request.QueryString["targetKey"];
            if (!string.IsNullOrWhiteSpace(targetKey))
            {
                var cached = HttpRuntime.Cache[CachePrefix + targetKey.Trim()] as string;
                if (!string.IsNullOrWhiteSpace(cached))
                {
                    return cached;
                }
            }

            return request.QueryString["target"];
        }

        private static string Store(string target)
        {
            var key = Guid.NewGuid().ToString("N");
            HttpRuntime.Cache.Insert(
                CachePrefix + key,
                target ?? string.Empty,
                null,
                Cache.NoAbsoluteExpiration,
                TargetTtl);
            return key;
        }
    }
}
