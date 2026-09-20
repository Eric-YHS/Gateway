using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;
using GatewayDemo.Legacy.Core.Models;
using GatewayDemo.Legacy.Web.Infrastructure;

namespace GatewayDemo.Legacy.Web.Gateway
{
    public sealed class WebViewHandoff : IHttpHandler
    {
        private const string TicketParameter = "ticket";
        private const int MaxBodyBytes = 16 * 1024;

        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext context)
        {
            context.Response.SuppressFormsAuthenticationRedirect = true;
            var runtime = LegacyGatewayRuntime.Current;
            if (!runtime.Configuration.Gateway.WebViewHandoffEnabled)
            {
                WriteJson(context, 404, new { error = "webview_handoff_disabled", message = "WebView 会话交接未启用。" });
                return;
            }

            if (runtime.Configuration.IsManagementRequest(context))
            {
                WriteJson(context, 404, new { error = "not_found", message = "页面不存在。" });
                return;
            }

            try
            {
                if (string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    CreateTicket(context, runtime);
                    return;
                }

                if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    ConsumeTicket(context, runtime);
                    return;
                }

                context.Response.Headers["Allow"] = "GET, POST";
                WriteJson(context, 405, new { error = "method_not_allowed", message = "只支持 GET 或 POST。" });
            }
            catch (DeviceCredentialException ex)
            {
                WriteJson(context, ex.StatusCode, new { error = "handoff_authentication_failed", message = ex.Message });
            }
            catch (Exception)
            {
                WriteJson(context, 500, new { error = "handoff_failed", message = "会话交接失败。" });
            }
        }

        private static void CreateTicket(HttpContext context, LegacyGatewayRuntime runtime)
        {
            if (!HasCompleteAppHeaders(context.Request))
            {
                WriteJson(context, 401, new { error = "app_credential_required", message = "必须使用完整的 APP 签名凭据。" });
                return;
            }

            IDictionary<string, object> payload;
            if (!TryReadJson(context.Request, out payload))
            {
                WriteJson(context, 400, new { error = "invalid_json", message = "请求体必须是 JSON 对象。" });
                return;
            }

            var siteKey = ReadValue(payload, "siteKey");
            var targetPath = ReadValue(payload, "targetPath");
            var site = runtime.Configuration.Gateway.FindSite(siteKey);
            if (site == null)
            {
                WriteJson(context, 400, new { error = "invalid_site", message = "站点不存在。" });
                return;
            }

            if (!TryNormalizeTarget(targetPath, runtime, siteKey, out targetPath))
            {
                WriteJson(context, 400, new { error = "invalid_target", message = "目标路径不合法。" });
                return;
            }

            var credentialService = runtime.CreateDeviceCredentialService();
            var device = credentialService.GetOrCreateContext(context);
            if (device == null || device.RequiresReview || !string.Equals(device.CredentialChannel, "app-hmac", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(context, 403, new { error = "device_not_authorized", message = "设备尚未获批或 APP 凭据不可用。" });
                return;
            }

            var repository = runtime.CreateRepository();
            var authorization = repository.GetActiveAuthorizationAsync(device.DeviceId, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            if (authorization == null || !authorization.AllowsSite(siteKey))
            {
                WriteJson(context, 403, new { error = "site_not_authorized", message = "设备未获准访问该站点。" });
                return;
            }

            var now = DateTime.UtcNow;
            var rawTicket = DeviceCredentialService.BuildOpaqueToken();
            var ticket = new GatewayWebViewHandoffTicket
            {
                Id = Guid.NewGuid().ToString("D"),
                TicketHash = DeviceCredentialService.ComputeHash(rawTicket),
                DeviceId = device.DeviceId,
                SiteKey = siteKey.Trim(),
                HostBinding = NormalizeHostBinding(GatewayRequestContext.GetExternalHost(context, runtime.Configuration.Gateway)),
                TargetPath = targetPath,
                ExpiresAtUtc = now.AddSeconds(runtime.Configuration.Gateway.WebViewHandoffTicketLifetimeSeconds),
                CreatedAtUtc = now
            };
            if (string.IsNullOrWhiteSpace(ticket.HostBinding))
            {
                WriteJson(context, 400, new { error = "invalid_host", message = "无法确认网关主机。" });
                return;
            }

            repository.CreateWebViewHandoffTicket(ticket);
            var consumeUrl = BuildConsumeUrl(context, rawTicket, siteKey);
            WriteJson(context, 200, new { ok = true, consumeUrl = consumeUrl, expiresAtUtc = ticket.ExpiresAtUtc.ToString("o") });
        }

        private static void ConsumeTicket(HttpContext context, LegacyGatewayRuntime runtime)
        {
            if (HasAnyAppHeaders(context.Request))
            {
                WriteJson(context, 400, new { error = "app_headers_not_allowed", message = "APP 签名头不能用于 WebView 兑换。" });
                return;
            }

            var rawTicket = (context.Request.QueryString[TicketParameter] ?? string.Empty).Trim();
            if (rawTicket.Length < 40 || rawTicket.Length > 128)
            {
                WriteJson(context, 410, new { error = "invalid_ticket", message = "会话交接票据无效或已过期。" });
                return;
            }

            var hostBinding = NormalizeHostBinding(GatewayRequestContext.GetExternalHost(context, runtime.Configuration.Gateway));
            if (string.IsNullOrWhiteSpace(hostBinding))
            {
                WriteJson(context, 400, new { error = "invalid_host", message = "无法确认网关主机。" });
                return;
            }

            var siteKey = (context.Request.QueryString["siteKey"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(siteKey))
            {
                WriteJson(context, 410, new { error = "invalid_ticket", message = "会话交接票据缺少站点绑定。" });
                return;
            }
            var site = runtime.Configuration.Gateway.FindSite(siteKey);
            if (site == null)
            {
                WriteJson(context, 410, new { error = "invalid_ticket", message = "会话交接票据无效或已过期。" });
                return;
            }

            var now = DateTime.UtcNow;
            var repository = runtime.CreateRepository();
            var pendingTicket = repository.GetActiveWebViewHandoffTicket(
                DeviceCredentialService.ComputeHash(rawTicket), siteKey, hostBinding, now);
            if (pendingTicket == null)
            {
                WriteJson(context, 410, new { error = "invalid_ticket", message = "会话交接票据无效或已过期。" });
                return;
            }

            var pendingDevice = repository.GetManagedDeviceByDeviceId(pendingTicket.DeviceId);
            var pendingAuthorization = pendingDevice == null
                ? null
                : repository.GetActiveAuthorizationAsync(pendingDevice.DeviceId, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            if (pendingDevice == null || pendingDevice.TrustState != GatewayDeviceTrustState.Trusted
                || pendingAuthorization == null || !pendingAuthorization.AllowsSite(siteKey))
            {
                repository.RevokeWebViewCredentials(pendingTicket.DeviceId);
                WriteJson(context, 403, new { error = "device_not_authorized", message = "设备授权已失效。" });
                return;
            }

            var rawCredential = DeviceCredentialService.BuildOpaqueToken();
            var credential = repository.ConsumeWebViewHandoffTicket(
                DeviceCredentialService.ComputeHash(rawTicket),
                siteKey,
                hostBinding,
                now,
                DeviceCredentialService.ComputeHash(rawCredential),
                now.AddMinutes(runtime.Configuration.Gateway.WebViewHandoffCookieLifetimeMinutes));
            if (credential == null)
            {
                WriteJson(context, 410, new { error = "invalid_ticket", message = "会话交接票据无效或已过期。" });
                return;
            }

            var device = repository.GetManagedDeviceByDeviceId(credential.DeviceId);
            var authorization = device == null
                ? null
                : repository.GetActiveAuthorizationAsync(device.DeviceId, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            if (device == null || device.TrustState != GatewayDeviceTrustState.Trusted || authorization == null || !authorization.AllowsSite(siteKey))
            {
                repository.RevokeWebViewCredentials(credential.DeviceId);
                WriteJson(context, 403, new { error = "device_not_authorized", message = "设备授权已失效。" });
                return;
            }

            var cookie = new HttpCookie(runtime.Configuration.Gateway.WebViewHandoffCookieName, rawCredential)
            {
                HttpOnly = true,
                Path = "/",
                Expires = DateTime.UtcNow.AddMinutes(runtime.Configuration.Gateway.WebViewHandoffCookieLifetimeMinutes),
                SameSite = SameSiteMode.Lax
            };
            var cookieDomain = GatewayRequestContext.ResolveDeviceCookieDomain(context, runtime.Configuration.Gateway);
            if (!string.IsNullOrWhiteSpace(cookieDomain))
            {
                cookie.Domain = cookieDomain;
            }
            if (GatewayRequestContext.ShouldUseSecureDeviceCookie(context, runtime.Configuration.Gateway))
            {
                cookie.Secure = true;
            }
            context.Response.Cookies.Set(cookie);
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Cache.SetNoStore();
            context.Response.Redirect(credential.TargetPath, false);
            context.ApplicationInstance.CompleteRequest();
        }

        private static bool TryReadJson(HttpRequest request, out IDictionary<string, object> payload)
        {
            payload = null;
            if (request == null || request.InputStream == null || request.ContentLength < 0 || request.ContentLength > MaxBodyBytes)
            {
                return false;
            }
            if (request.InputStream.CanSeek) request.InputStream.Position = 0;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8, true, 4096, true))
            {
                var text = reader.ReadToEnd();
                if (request.InputStream.CanSeek) request.InputStream.Position = 0;
                if (string.IsNullOrWhiteSpace(text)) return false;
                payload = new JavaScriptSerializer().DeserializeObject(text) as IDictionary<string, object>;
                return payload != null;
            }
        }

        private static string ReadValue(IDictionary<string, object> payload, string key)
        {
            object value;
            return payload != null && payload.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value).Trim()
                : string.Empty;
        }

        private static bool TryNormalizeTarget(string value, LegacyGatewayRuntime runtime, string siteKey, out string target)
        {
            target = (value ?? string.Empty).Trim();
            if (target.Length == 0 || target.Length > 1024 || !target.StartsWith("/", StringComparison.Ordinal)
                || target.IndexOf("//", StringComparison.Ordinal) >= 0
                || target.IndexOf("..", StringComparison.Ordinal) >= 0
                || target.IndexOf('\0') >= 0 || target.IndexOf('#') >= 0)
            {
                return false;
            }

            var path = target;
            var query = string.Empty;
            var queryIndex = target.IndexOf('?');
            if (queryIndex >= 0)
            {
                path = target.Substring(0, queryIndex);
                query = target.Substring(queryIndex);
            }
            if (path.StartsWith("/Gateway", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/proxy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "/healthz.ashx", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var site = runtime.Configuration.Gateway.FindSite(siteKey);
            if (site == null) return false;
            if (!GatewayPathUtility.UsesGatewayRootPaths(site))
            {
                var proxyPrefix = runtime.Configuration.BuildProxyPath(siteKey, string.Empty).TrimEnd('/') + "/";
                if (!path.StartsWith(proxyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            target = path + query;
            return true;
        }

        private static string BuildConsumeUrl(HttpContext context, string ticket, string siteKey)
        {
            var origin = GatewayRequestContext.GetExternalScheme(context, LegacyGatewayRuntime.Current.Configuration.Gateway)
                + "://" + GatewayRequestContext.GetExternalHost(context, LegacyGatewayRuntime.Current.Configuration.Gateway);
            return origin.TrimEnd('/') + "/Gateway/WebViewHandoff.ashx?siteKey="
                + HttpUtility.UrlEncode(siteKey) + "&ticket=" + HttpUtility.UrlEncode(ticket);
        }

        private static bool HasCompleteAppHeaders(HttpRequest request)
        {
            return !string.IsNullOrWhiteSpace(request.Headers[DeviceCredentialService.AppKeyHeaderName])
                && !string.IsNullOrWhiteSpace(request.Headers[DeviceCredentialService.AppTimestampHeaderName])
                && !string.IsNullOrWhiteSpace(request.Headers[DeviceCredentialService.AppNonceHeaderName])
                && !string.IsNullOrWhiteSpace(request.Headers[DeviceCredentialService.AppBodyHashHeaderName])
                && !string.IsNullOrWhiteSpace(request.Headers[DeviceCredentialService.AppSignatureHeaderName]);
        }

        private static bool HasAnyAppHeaders(HttpRequest request)
        {
            return !string.IsNullOrWhiteSpace(request.Headers[DeviceCredentialService.AppKeyHeaderName])
                || !string.IsNullOrWhiteSpace(request.Headers[DeviceCredentialService.AppTimestampHeaderName])
                || !string.IsNullOrWhiteSpace(request.Headers[DeviceCredentialService.AppNonceHeaderName])
                || !string.IsNullOrWhiteSpace(request.Headers[DeviceCredentialService.AppBodyHashHeaderName])
                || !string.IsNullOrWhiteSpace(request.Headers[DeviceCredentialService.AppSignatureHeaderName]);
        }

        private static string NormalizeHostBinding(string value)
        {
            return (value ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();
        }

        private static void WriteJson(HttpContext context, int statusCode, object payload)
        {
            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Cache.SetNoStore();
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Write(new JavaScriptSerializer().Serialize(payload));
            context.ApplicationInstance.CompleteRequest();
        }
    }
}
