using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    /// <summary>
    /// 低风险、可开关、脱敏的 APP 请求诊断日志。
    /// 默认关闭，通过 Web.config AppSettings 的 Gateway.AppDiagnostics.Enabled 开启。
    /// 日志落到 App_Data/app-diagnostics.log。
    /// </summary>
    public sealed class AppRequestDiagnostic
    {
        private static readonly object _lock = new object();
        private static volatile bool _enabled;
        private static volatile string _logDir;
        private static volatile int _maxFileSizeKb = 2048;
        private static bool _initialized;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                var cfg = System.Configuration.ConfigurationManager.AppSettings;
                _enabled = string.Equals(cfg["Gateway.AppDiagnostics.Enabled"], "true", StringComparison.OrdinalIgnoreCase);
                var dir = cfg["Gateway.AppDiagnostics.LogDirectory"];
                var ctx = HttpContext.Current;
                if (ctx != null)
                {
                    _logDir = string.IsNullOrWhiteSpace(dir)
                        ? ctx.Server.MapPath("~/App_Data")
                        : ctx.Server.MapPath(dir);
                }
                else
                {
                    var appData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data");
                    _logDir = string.IsNullOrWhiteSpace(dir) ? appData : Path.Combine(appData, dir.TrimStart('~', '/').TrimStart('/'));
                }
                var sizeStr = cfg["Gateway.AppDiagnostics.MaxFileSizeKb"];
                int size;
                if (!string.IsNullOrWhiteSpace(sizeStr) && int.TryParse(sizeStr, out size))
                {
                    _maxFileSizeKb = size;
                }
                _initialized = true;
            }
        }

        public static bool Enabled { get { return _enabled; } }

        public static void Record(DiagnosticRecord record)
        {
            if (!_enabled) return;
            try
            {
                var line = record.ToLogLine();
                lock (_lock)
                {
                    var path = Path.Combine(_logDir ?? "App_Data", "app-diagnostics.log");
                    try
                    {
                        var fi = new FileInfo(path);
                        if (fi.Exists && fi.Length > _maxFileSizeKb * 1024)
                        {
                            var backup = path + ".1";
                            if (File.Exists(backup)) File.Delete(backup);
                            File.Move(path, backup);
                        }
                    }
                    catch { /* ignore rotation errors */ }

                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* never let diagnostics break the request */ }
        }

        public static DiagnosticRecord BeginRecord(HttpContext context)
        {
            if (!_enabled) return null;
            var r = new DiagnosticRecord
            {
                Timestamp = DateTime.UtcNow.ToString("O"),
                Method = context.Request.HttpMethod ?? "?",
                Path = context.Request.Path ?? "?",
                RawUrl = context.Request.RawUrl ?? "?",
                QueryKeys = CollectQueryKeys(context),
                ContentType = context.Request.ContentType ?? "",
                UserAgentSummary = MaskUserAgent(context.Request.UserAgent ?? ""),
                ClientIp = context.Request.UserHostAddress ?? "?",
            };
            return r;
        }

        private static string CollectQueryKeys(HttpContext ctx)
        {
            try
            {
                var keys = new List<string>();
                for (int i = 0; i < ctx.Request.QueryString.Count; i++)
                {
                    var k = ctx.Request.QueryString.GetKey(i);
                    if (!string.IsNullOrEmpty(k)) keys.Add(k);
                }
                return string.Join(",", keys);
            }
            catch { return "?"; }
        }

        private static string MaskUserAgent(string ua)
        {
            if (string.IsNullOrEmpty(ua)) return "";
            if (ua.Length > 120) return ua.Substring(0, 120) + "...";
            return ua;
        }
    }

    public sealed class DiagnosticRecord
    {
        public string Timestamp;
        public string Method;
        public string Path;
        public string RawUrl;
        public string QueryKeys;
        public string ContentType;
        public string UserAgentSummary;
        public string ClientIp;

        public bool IsLegacyApp;
        public string MatchedSiteKey;
        public bool IsBootstrap;
        public bool IsUnauthorized;
        public bool IsProxied;
        public bool IsUpstreamBlocked;
        public bool IsUpstreamError;

        public string DeviceIdFields;
        public string DeviceId;
        public string DeviceCode;
        public string TrustState;
        public string AuthorizationStatus;
        public string BlockReason;

        public long ElapsedMs;

        public string ToLogLine()
        {
            var sb = new StringBuilder();
            sb.Append(Timestamp ?? "?");
            sb.Append('\t');
            sb.Append(Method ?? "?");
            sb.Append('\t');
            sb.Append(Path ?? "?");
            sb.Append('\t');
            sb.Append(RawUrl ?? "?");
            sb.Append('\t');
            sb.Append(QueryKeys ?? "");
            sb.Append('\t');
            sb.Append(ContentType ?? "");
            sb.Append('\t');
            sb.Append(UserAgentSummary ?? "");
            sb.Append('\t');
            sb.Append(ClientIp ?? "");
            sb.Append('\t');
            sb.Append(IsLegacyApp ? "1" : "0");
            sb.Append('\t');
            sb.Append(MatchedSiteKey ?? "");
            sb.Append('\t');
            sb.Append(IsBootstrap ? "1" : "0");
            sb.Append('\t');
            sb.Append(IsUnauthorized ? "1" : "0");
            sb.Append('\t');
            sb.Append(IsProxied ? "1" : "0");
            sb.Append('\t');
            sb.Append(IsUpstreamBlocked ? "1" : "0");
            sb.Append('\t');
            sb.Append(IsUpstreamError ? "1" : "0");
            sb.Append('\t');
            sb.Append(DeviceIdFields ?? "");
            sb.Append('\t');
            sb.Append(DeviceId ?? "");
            sb.Append('\t');
            sb.Append(DeviceCode ?? "");
            sb.Append('\t');
            sb.Append(TrustState ?? "");
            sb.Append('\t');
            sb.Append(AuthorizationStatus ?? "");
            sb.Append('\t');
            sb.Append(BlockReason ?? "");
            sb.Append('\t');
            sb.Append(ElapsedMs);
            return sb.ToString();
        }

        private static string Safe(string s, int maxLen = 80)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length > maxLen) return s.Substring(0, maxLen) + "...";
            return s;
        }
    }
}
