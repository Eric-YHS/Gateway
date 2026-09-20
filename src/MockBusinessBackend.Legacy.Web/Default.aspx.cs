using System;
using System.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.UI;

namespace MockBusinessBackend.Legacy.Web
{
    public partial class Default : Page
    {
        private const string AuthCookieName = "kp_mock_auth";
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        private sealed class SiteInfo
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string Accent { get; set; }
        }

        private static readonly IDictionary<string, SiteInfo> Sites = new Dictionary<string, SiteInfo>(StringComparer.OrdinalIgnoreCase)
        {
            { "erp-main", new SiteInfo { Name = "ERP 主站", Description = "统一访问 ERP 门户、报表与业务页面。", Accent = "#0f766e" } },
            { "wms-east", new SiteInfo { Name = "WMS 东仓", Description = "集中处理入库、出库与库存盘点。", Accent = "#b45309" } },
            { "finance", new SiteInfo { Name = "财务共享", Description = "集中处理应收、应付与经营报表。", Accent = "#1d4ed8" } }
        };

        private string html;

        protected void Page_Load(object sender, EventArgs e)
        {
            var siteKey = Convert.ToString(Context.Items[Global.RouteSiteKeyItemName] ?? Page.RouteData.Values["siteKey"]);
            var page = Convert.ToString(Page.RouteData.Values["page"]);
            var pageInfo = Convert.ToString(Context.Items[Global.RoutePageInfoItemName] ?? Page.RouteData.Values["pageInfo"]);
            var currentPage = string.IsNullOrWhiteSpace(pageInfo)
                ? (string.IsNullOrWhiteSpace(page) ? (Request.Path ?? string.Empty).TrimStart('/') : page.Trim('/'))
                : pageInfo.Trim('/');

            if (TryHandleSpaRequest(currentPage))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(siteKey))
            {
                Response.Redirect("/mock/erp-main/portal", false);
                Context.ApplicationInstance.CompleteRequest();
                return;
            }

            SiteInfo site;
            if (!Sites.TryGetValue(siteKey, out site))
            {
                Response.StatusCode = 404;
                html = "<!DOCTYPE html><html lang=\"zh-CN\"><body><h1>页面不存在</h1></body></html>";
                return;
            }

            currentPage = string.IsNullOrWhiteSpace(currentPage) ? "portal" : currentPage;

            if (TryServeAsset(currentPage))
            {
                return;
            }

            if (Context.IsWebSocketRequest && string.Equals(currentPage, "socket", StringComparison.OrdinalIgnoreCase))
            {
                Context.AcceptWebSocketRequest(EchoWebSocketAsync);
                Context.ApplicationInstance.CompleteRequest();
                return;
            }

            if (string.Equals(currentPage, "logout", StringComparison.OrdinalIgnoreCase))
            {
                SignOut(siteKey);
                Response.Redirect(BuildMockPath(siteKey, "login"), false);
                Context.ApplicationInstance.CompleteRequest();
                return;
            }

            if (TryHandleAppApi(siteKey, currentPage))
            {
                return;
            }

            if (Request.HttpMethod == "POST" && string.Equals(currentPage, "login", StringComparison.OrdinalIgnoreCase))
            {
                HandleLogin(siteKey);
                return;
            }

            if ((string.Equals(currentPage, "portal", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(currentPage, "reports", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(currentPage, "ops", StringComparison.OrdinalIgnoreCase))
                && !IsSignedIn(siteKey))
            {
                var returnUrl = HttpUtility.UrlEncode(currentPage);
                Response.Redirect("/mock/" + siteKey + "/login?returnUrl=" + returnUrl, false);
                Context.ApplicationInstance.CompleteRequest();
                return;
            }

            html = RenderPage(siteKey, site, currentPage);
        }

        protected override void Render(HtmlTextWriter writer)
        {
            writer.Write(html ?? string.Empty);
        }

        private static async Task EchoWebSocketAsync(System.Web.WebSockets.AspNetWebSocketContext context)
        {
            var socket = context.WebSocket;
            var buffer = new byte[8 * 1024];
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription ?? string.Empty, CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                var prefix = Encoding.UTF8.GetBytes("echo:");
                var payload = new byte[prefix.Length + result.Count];
                System.Buffer.BlockCopy(prefix, 0, payload, 0, prefix.Length);
                System.Buffer.BlockCopy(buffer, 0, payload, prefix.Length, result.Count);
                await socket.SendAsync(new ArraySegment<byte>(payload), result.MessageType, result.EndOfMessage, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private string RenderPage(string siteKey, SiteInfo site, string page)
        {
            if (string.Equals(page, "login", StringComparison.OrdinalIgnoreCase))
            {
                return RenderLoginPage(siteKey, site);
            }

            var title = page == "portal"
                ? "门户"
                : page == "reports"
                    ? "报表"
                    : page == "ops"
                        ? "运维"
                        : page;

            var currentUser = GetCurrentUser(siteKey);
            var relatedSitesBaseUrl = BuildRelatedMockSitesBaseUrl();
            var portalPath = BuildMockPath(siteKey, "portal");
            var reportsPath = BuildMockPath(siteKey, "reports");
            var opsPath = BuildMockPath(siteKey, "ops");
            var logoutPath = BuildMockPath(siteKey, "logout");
            var mockCssPath = BuildAssetPath(siteKey, "assets/mock.css");
            var logoPath = BuildAssetPath(siteKey, "assets/erp-mark.svg");
            return string.Format(
                "<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" /><title>{0} - {1}</title><link rel=\"stylesheet\" href=\"{18}\" /><style>:root{{--accent:{2};}}</style></head><body><div class=\"shell\"><section class=\"hero\"><div><div class=\"brand\"><img src=\"{19}\" alt=\"logo\" /><div><h1>{0} / {1}</h1></div></div><div class=\"nav\"><a class=\"{4}\" href=\"{14}\">门户</a><a class=\"{5}\" href=\"{15}\">报表</a><a class=\"{6}\" href=\"{16}\">运维</a></div></div><div class=\"panel\"><div class=\"kv\"><div><span>站点</span><code>{0}</code></div><div><span>用户</span><code>{12}</code></div><div><span>状态</span><code>运行中</code></div></div><div class=\"actions\"><a class=\"ghost\" href=\"{17}\">退出登录</a></div></div></section><section class=\"grid\"><article class=\"panel\"><h2>{1}</h2><div class=\"kv\"><div><span>订单</span><code>128</code></div><div><span>库存</span><code>3,462</code></div><div><span>客户</span><code>816</code></div></div></article><article class=\"panel\"><h2>入口</h2><div class=\"links\"><a href=\"{14}\">{0}</a></div></article></section></div></body></html>",
                HttpUtility.HtmlEncode(site.Name),
                HttpUtility.HtmlEncode(title),
                HttpUtility.HtmlEncode(site.Accent),
                HttpUtility.HtmlEncode(site.Description),
                page == "portal" ? "active" : string.Empty,
                page == "reports" ? "active" : string.Empty,
                page == "ops" ? "active" : string.Empty,
                HttpUtility.HtmlEncode(Request.Headers["X-Gateway-Site"] ?? string.Empty),
                HttpUtility.HtmlEncode(Request.Headers["X-Forwarded-For"] ?? string.Empty),
                HttpUtility.HtmlEncode(Request.Headers["Host"] ?? string.Empty),
                HttpUtility.HtmlEncode(siteKey),
                HttpUtility.HtmlEncode(page),
                HttpUtility.HtmlEncode(currentUser),
                HttpUtility.HtmlAttributeEncode(relatedSitesBaseUrl),
                HttpUtility.HtmlAttributeEncode(portalPath),
                HttpUtility.HtmlAttributeEncode(reportsPath),
                HttpUtility.HtmlAttributeEncode(opsPath),
                HttpUtility.HtmlAttributeEncode(logoutPath),
                HttpUtility.HtmlAttributeEncode(mockCssPath),
                HttpUtility.HtmlAttributeEncode(logoPath));
        }

        private string RenderLoginPage(string siteKey, SiteInfo site)
        {
            var returnUrl = (Request.QueryString["returnUrl"] ?? "portal").Trim('/');
            if (string.IsNullOrWhiteSpace(returnUrl))
            {
                returnUrl = "portal";
            }

            var error = (Request.QueryString["error"] ?? string.Empty).Trim();
            var loginPath = BuildMockPath(siteKey, "login");
            var mockCssPath = BuildAssetPath(siteKey, "assets/mock.css");
            var logoPath = BuildAssetPath(siteKey, "assets/erp-mark.svg");
            return string.Format(
                "<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" /><title>{0} - 登录</title><link rel=\"stylesheet\" href=\"{7}\" /><style>:root{{--accent:{1};}}</style></head><body><div class=\"shell narrow\"><section class=\"panel login-card\"><div class=\"brand\"><img src=\"{8}\" alt=\"logo\" /><div><h1>{0}</h1></div></div>{3}<form method=\"post\" action=\"{6}\"><input type=\"hidden\" name=\"returnUrl\" value=\"{5}\" /><label><span>用户名</span><input type=\"text\" name=\"username\" /></label><label><span>密码</span><input type=\"password\" name=\"password\" /></label><div class=\"actions\"><button type=\"submit\">登录系统</button></div></form></section></div></body></html>",
                HttpUtility.HtmlEncode(site.Name),
                HttpUtility.HtmlEncode(site.Accent),
                HttpUtility.HtmlEncode(site.Description),
                string.IsNullOrWhiteSpace(error) ? string.Empty : "<div class=\"notice danger\"><strong>登录失败。</strong><span>" + HttpUtility.HtmlEncode(error) + "</span></div>",
                HttpUtility.HtmlEncode(siteKey),
                HttpUtility.HtmlEncode(returnUrl),
                HttpUtility.HtmlAttributeEncode(loginPath),
                HttpUtility.HtmlAttributeEncode(mockCssPath),
                HttpUtility.HtmlAttributeEncode(logoPath));
        }

        private void HandleLogin(string siteKey)
        {
            var username = (Request.Form["username"] ?? string.Empty).Trim();
            var password = Request.Form["password"] ?? string.Empty;
            var returnUrl = (Request.Form["returnUrl"] ?? "portal").Trim('/');
            if (string.IsNullOrWhiteSpace(returnUrl))
            {
                returnUrl = "portal";
            }

            if (!IsValidMockCredential(username, password))
            {
                Response.Redirect(BuildMockPath(siteKey, "login") + "?returnUrl=" + HttpUtility.UrlEncode(returnUrl) + "&error=" + HttpUtility.UrlEncode("用户名或密码不正确。"), false);
                Context.ApplicationInstance.CompleteRequest();
                return;
            }

            var cookie = new HttpCookie(AuthCookieName + "_" + siteKey, username);
            cookie.Path = IsGatewayProxiedRequest() ? "/" : "/mock/" + siteKey + "/";
            cookie.HttpOnly = true;
            Response.Cookies.Add(cookie);
            Response.Redirect(BuildMockPath(siteKey, returnUrl), false);
            Context.ApplicationInstance.CompleteRequest();
        }

        private bool TryHandleAppApi(string siteKey, string currentPage)
        {
            if (string.IsNullOrWhiteSpace(currentPage))
            {
                return false;
            }

            if (Request.HttpMethod == "POST"
                && string.Equals(currentPage, "user/login", StringComparison.OrdinalIgnoreCase))
            {
                HandleAppLogin(siteKey);
                return true;
            }

            if (Request.HttpMethod == "POST"
                && currentPage.StartsWith("WCFService/PostBus.ashx", StringComparison.OrdinalIgnoreCase))
            {
                HandleAppService(siteKey);
                return true;
            }

            if (Request.HttpMethod == "POST"
                && currentPage.StartsWith("ashx/ChatUploadImg.ashx", StringComparison.OrdinalIgnoreCase))
            {
                HandleAppUpload(siteKey);
                return true;
            }

            return false;
        }

        private bool TryHandleSpaRequest(string currentPage)
        {
            var normalized = (currentPage ?? string.Empty).Trim().TrimStart('/').Replace("\\", "/");
            if (normalized.StartsWith("mock/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = normalized.Split(new[] { '/' }, 3);
                normalized = parts.Length == 3 ? parts[2] : string.Empty;
            }

            // SPA 以站点根路径提供时，接口相对根路径调用。
            if (normalized.StartsWith("api/spa/ping", StringComparison.OrdinalIgnoreCase))
            {
                WriteSpaPingResponse();
                return true;
            }

            if (!string.Equals(normalized, "jvs-aps-ui", StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith("jvs-aps-ui/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var spaRelativePath = normalized.Length == "jvs-aps-ui".Length
                ? string.Empty
                : normalized.Substring("jvs-aps-ui/".Length);
            if (spaRelativePath.StartsWith("api/spa/ping", StringComparison.OrdinalIgnoreCase))
            {
                WriteSpaPingResponse();
                return true;
            }

            WriteSpaIndexResponse();
            return true;
        }

        private void WriteSpaPingResponse()
        {
            Response.Clear();
            Response.StatusCode = 200;
            Response.TrySkipIisCustomErrors = true;
            Response.ContentType = "application/json; charset=utf-8";
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            Response.Write(Serializer.Serialize(new
            {
                ok = true,
                route = Request.QueryString["route"] ?? string.Empty,
                app = "jvs-aps-ui"
            }));
            Context.ApplicationInstance.CompleteRequest();
        }

        private void WriteSpaIndexResponse()
        {
            var indexPath = Server.MapPath("~/jvs-aps-ui/index.html");
            if (string.IsNullOrWhiteSpace(indexPath) || !File.Exists(indexPath))
            {
                WriteAssetNotFound();
                return;
            }

            Response.Clear();
            Response.StatusCode = 200;
            Response.TrySkipIisCustomErrors = true;
            Response.ContentType = "text/html; charset=utf-8";
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.TransmitFile(indexPath);
            Context.ApplicationInstance.CompleteRequest();
        }

        private void HandleAppLogin(string siteKey)
        {
            var username = (Request.Form["UserId"] ?? string.Empty).Trim();
            var password = Request.Form["UserPwd"] ?? string.Empty;
            Response.ContentType = "application/json; charset=utf-8";

            if (!IsValidMockCredential(username, password))
            {
                Response.StatusCode = 200;
                Response.Write(Serializer.Serialize(new
                {
                    LoginStatus = 0,
                    Msg = "用户名或密码不正确。"
                }));
                Context.ApplicationInstance.CompleteRequest();
                return;
            }

            var sessionKey = "mock-session-" + siteKey + "-" + Guid.NewGuid().ToString("N");
            Response.StatusCode = 200;
            Response.Write(Serializer.Serialize(new
            {
                LoginStatus = 1,
                Msg = "登录成功",
                UserToken = sessionKey,
                UserId = username,
                Upn = BuildGatewayProxySiteBaseUrl(siteKey)
            }));
            Context.ApplicationInstance.CompleteRequest();
        }

        private static bool IsValidMockCredential(string username, string password)
        {
            var expectedUsername = ReadSetting("Mock.Username");
            var expectedPassword = ReadSetting("Mock.Password");
            return !string.IsNullOrWhiteSpace(expectedUsername)
                && !string.IsNullOrWhiteSpace(expectedPassword)
                && string.Equals(username, expectedUsername, StringComparison.OrdinalIgnoreCase)
                && string.Equals(password, expectedPassword, StringComparison.Ordinal);
        }

        private string BuildGatewayProxySiteBaseUrl(string siteKey)
        {
            var forwardedSiteBase = (Request.Headers["X-Gateway-Proxy-Site-Base"] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(forwardedSiteBase))
            {
                return forwardedSiteBase.TrimEnd('/');
            }

            return BuildGatewayProxyBaseUrl().TrimEnd('/') + "/" + EncodeUrlPathSegment(siteKey);
        }

        private string BuildGatewayProxyBaseUrl()
        {
            var configured = ReadSetting("Mock.GatewayProxyBaseUrl");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.TrimEnd('/');
            }

            var proxyBasePath = ReadSetting("Mock.GatewayProxyBasePath");
            if (string.IsNullOrWhiteSpace(proxyBasePath))
            {
                proxyBasePath = "/proxy";
            }

            proxyBasePath = proxyBasePath.Trim();
            if (!proxyBasePath.StartsWith("/", StringComparison.Ordinal))
            {
                proxyBasePath = "/" + proxyBasePath;
            }

            return BuildPublicOrigin().TrimEnd('/') + proxyBasePath.TrimEnd('/');
        }

        private string BuildRelatedMockSitesBaseUrl()
        {
            if (IsGatewayProxiedRequest())
            {
                var gatewaySiteKey = (Request.Headers["X-Gateway-Site"] ?? string.Empty).Trim().Trim('/');
                if (!string.IsNullOrWhiteSpace(gatewaySiteKey))
                {
                    return BuildPublicOrigin().TrimEnd('/') + "/proxy/" + EncodeUrlPathSegment(gatewaySiteKey) + "/__root__/mock";
                }
            }

            return BuildPublicOrigin().TrimEnd('/') + "/mock";
        }

        private static string EncodeUrlPathSegment(string value)
        {
            return HttpUtility.UrlPathEncode((value ?? string.Empty).Trim('/'));
        }

        private string BuildMockPath(string siteKey, string path)
        {
            var normalizedPath = (path ?? string.Empty).Trim().TrimStart('/');
            if (IsGatewayProxiedRequest())
            {
                return "/" + normalizedPath;
            }

            return "/mock/" + siteKey + "/" + normalizedPath;
        }

        private string BuildAssetPath(string siteKey, string asset)
        {
            var normalizedAsset = (asset ?? string.Empty).Trim().TrimStart('/');
            if (IsGatewayProxiedRequest())
            {
                return "/" + normalizedAsset;
            }

            return "/mock/" + siteKey + "/" + normalizedAsset;
        }

        private bool IsGatewayProxiedRequest()
        {
            return !string.IsNullOrWhiteSpace(Request.Headers["X-Gateway-Site"]);
        }

        private string BuildPublicOrigin()
        {
            var proto = (Request.Headers["X-Forwarded-Proto"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(proto))
            {
                proto = Request.Url == null ? "http" : Request.Url.Scheme;
            }

            var host = (Request.Headers["X-Forwarded-Host"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                host = (Request.Headers["Host"] ?? string.Empty).Trim();
            }

            if (string.IsNullOrWhiteSpace(host) && Request.Url != null)
            {
                host = Request.Url.Authority;
            }

            return proto + "://" + host;
        }

        private static string ReadSetting(string key)
        {
            var value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private bool TryServeAsset(string currentPage)
        {
            var normalized = (currentPage ?? string.Empty).Trim().TrimStart('/').Replace("\\", "/");
            if (!normalized.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var relativePath = normalized.Substring("assets/".Length).TrimStart('/');
            if (!IsSafeAssetPath(relativePath))
            {
                WriteAssetNotFound();
                return true;
            }

            var assetPath = Server.MapPath("~/assets/" + relativePath);
            if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(assetPath))
            {
                WriteAssetNotFound();
                return true;
            }

            Response.Clear();
            Response.StatusCode = 200;
            Response.TrySkipIisCustomErrors = true;
            Response.ContentType = GetAssetContentType(relativePath);
            Response.Cache.SetCacheability(HttpCacheability.Public);
            Response.Cache.SetMaxAge(TimeSpan.FromHours(1));
            Response.TransmitFile(assetPath);
            Context.ApplicationInstance.CompleteRequest();
            return true;
        }

        private static bool IsSafeAssetPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            if (relativePath.IndexOf(':') >= 0)
            {
                return false;
            }

            foreach (var part in relativePath.Split('/'))
            {
                if (string.IsNullOrWhiteSpace(part)
                    || string.Equals(part, ".", StringComparison.Ordinal)
                    || string.Equals(part, "..", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetAssetContentType(string relativePath)
        {
            var extension = Path.GetExtension(relativePath ?? string.Empty);
            if (string.Equals(extension, ".css", StringComparison.OrdinalIgnoreCase))
            {
                return "text/css; charset=utf-8";
            }

            if (string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase))
            {
                return "application/javascript; charset=utf-8";
            }

            if (string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
            {
                return "image/svg+xml";
            }

            return "application/octet-stream";
        }

        private void WriteAssetNotFound()
        {
            Response.Clear();
            Response.StatusCode = 404;
            Response.TrySkipIisCustomErrors = true;
            Response.ContentType = "text/plain; charset=utf-8";
            Response.Write("Not Found");
            Context.ApplicationInstance.CompleteRequest();
        }

        private void HandleAppService(string siteKey)
        {
            var postJson = (Request.Form["postJson"] ?? string.Empty).Trim();
            var sessionKey = TryExtractSessionKey(postJson);
            Response.ContentType = "application/json; charset=utf-8";
            Response.StatusCode = 200;
            Response.Write(Serializer.Serialize(new
            {
                Status = 1,
                Msg = "OK",
                Data = Serializer.Serialize(new
                {
                    SiteKey = siteKey,
                    SessionKey = sessionKey,
                    Echo = "gateway-app-ok"
                })
            }));
            Context.ApplicationInstance.CompleteRequest();
        }

        private void HandleAppUpload(string siteKey)
        {
            Response.ContentType = "application/json; charset=utf-8";
            Response.StatusCode = 200;
            Response.Write(Serializer.Serialize(new
            {
                filepath = "/mock/" + siteKey + "/uploads/avatar.png"
            }));
            Context.ApplicationInstance.CompleteRequest();
        }

        private static string TryExtractSessionKey(string postJson)
        {
            if (string.IsNullOrWhiteSpace(postJson))
            {
                return string.Empty;
            }

            try
            {
                var payload = Serializer.DeserializeObject(postJson) as IDictionary<string, object>;
                if (payload == null)
                {
                    return string.Empty;
                }

                var userToken = GetDictionaryValue(payload, "UserToken") as IDictionary<string, object>;
                if (userToken == null)
                {
                    return string.Empty;
                }

                var sessionKey = Convert.ToString(GetDictionaryValue(userToken, "SessionKey"));
                if (!string.IsNullOrWhiteSpace(sessionKey))
                {
                    return sessionKey;
                }

                return Convert.ToString(GetDictionaryValue(userToken, "UserToken")) ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private static object GetDictionaryValue(IDictionary<string, object> dictionary, string key)
        {
            if (dictionary == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            foreach (var item in dictionary)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return item.Value;
                }
            }

            return null;
        }

        private void SignOut(string siteKey)
        {
            var cookie = new HttpCookie(AuthCookieName + "_" + siteKey, string.Empty);
            cookie.Path = IsGatewayProxiedRequest() ? "/" : "/mock/" + siteKey + "/";
            cookie.Expires = DateTime.UtcNow.AddDays(-1);
            Response.Cookies.Add(cookie);
        }

        private bool IsSignedIn(string siteKey)
        {
            return !string.IsNullOrWhiteSpace(GetCurrentUser(siteKey));
        }

        private string GetCurrentUser(string siteKey)
        {
            var cookie = Request.Cookies[AuthCookieName + "_" + siteKey];
            return cookie == null ? string.Empty : (cookie.Value ?? string.Empty).Trim();
        }
    }
}
