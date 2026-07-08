using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Security;
using System.Web.UI;
using GatewayDemo.Legacy.Web.Infrastructure;

namespace GatewayDemo.Legacy.Web.Admin
{
    public partial class Default : Page
    {
        private const string AdminCsrfSessionKey = "GatewayAdminCsrfToken";
        private const string AdminCsrfFormField = "csrfToken";
        private static readonly ConcurrentDictionary<string, LoginThrottleEntry> LoginThrottle =
            new ConcurrentDictionary<string, LoginThrottleEntry>(StringComparer.OrdinalIgnoreCase);

        private string html;
        private bool skipRender;

        protected void Page_Load(object sender, EventArgs e)
        {
            Response.ContentType = "text/html; charset=utf-8";
            Response.ContentEncoding = Encoding.UTF8;

            var runtime = LegacyGatewayRuntime.Current;
            if (!runtime.Configuration.IsManagementRequest(Context))
            {
                Response.StatusCode = 404;
                Response.TrySkipIisCustomErrors = true;
                html = runtime.Renderer.RenderNotFoundPage("页面不存在。");
                return;
            }

            var action = (Request.Form["action"] ?? string.Empty).Trim();
            if (Request.IsAuthenticated
                && Request.HttpMethod == "POST"
                && IsAdminMutation(action)
                && !IsValidCsrfToken())
            {
                // 会话（含其中的操作令牌）会在 IIS 重启或闲置超时后重置，而登录票据
                // 可能仍然有效。此时重新渲染管理台并签发新令牌，本次操作不执行。
                Response.TrySkipIisCustomErrors = true;
                RenderDashboard(runtime, string.Empty, "管理页面已过期，本次操作未执行；页面已刷新，请重新提交。");
                return;
            }

            if (Request.HttpMethod == "POST"
                && string.Equals(action, "logout", StringComparison.OrdinalIgnoreCase))
            {
                FormsAuthentication.SignOut();
                Response.Redirect("/Admin/Default.aspx", false);
                Context.ApplicationInstance.CompleteRequest();
                skipRender = true;
                return;
            }

            if (!Request.IsAuthenticated)
            {
                HandleLogin(runtime);
                return;
            }

            if (Request.HttpMethod == "POST" && HandleAdminAction(runtime))
            {
                return;
            }

            RenderDashboard(runtime, ResolveNoticeFromQuery(), string.Empty);
        }

        protected override void Render(HtmlTextWriter writer)
        {
            if (!skipRender)
            {
                writer.Write(html ?? string.Empty);
            }
        }

        private void HandleLogin(LegacyGatewayRuntime runtime)
        {
            if (Request.HttpMethod == "POST"
                && string.Equals(Request.Form["action"], "login", StringComparison.OrdinalIgnoreCase))
            {
                var username = (Request.Form["username"] ?? string.Empty).Trim();
                var password = Request.Form["password"] ?? string.Empty;

                TimeSpan retryAfter;
                if (IsLoginBlocked(runtime, username, out retryAfter))
                {
                    Response.StatusCode = 429;
                    Response.TrySkipIisCustomErrors = true;
                    html = runtime.Renderer.RenderAdminLoginPage("登录尝试过于频繁，请稍后再试。");
                    return;
                }

                if (string.Equals(username, runtime.Configuration.Admin.Username, StringComparison.Ordinal)
                    && runtime.PasswordHasher.VerifyHashedPassword(runtime.Configuration.Admin.PasswordHash, password))
                {
                    ClearLoginFailures(username);
                    RotateCsrfToken();
                    FormsAuthentication.SetAuthCookie(username, false);
                    Response.Redirect("/Admin/Default.aspx", false);
                    Context.ApplicationInstance.CompleteRequest();
                    skipRender = true;
                    return;
                }

                RecordLoginFailure(runtime, username);
                Response.StatusCode = 401;
                Response.TrySkipIisCustomErrors = true;
                Response.SuppressFormsAuthenticationRedirect = true;
                html = runtime.Renderer.RenderAdminLoginPage("用户名或密码不正确。");
                return;
            }

            html = runtime.Renderer.RenderAdminLoginPage(string.Empty);
        }

        private bool HandleAdminAction(LegacyGatewayRuntime runtime)
        {
            var repository = runtime.CreateRepository();
            var action = (Request.Form["action"] ?? string.Empty).Trim();

            try
            {
                if (string.Equals(action, "approve-request", StringComparison.OrdinalIgnoreCase))
                {
                    Guid requestId;
                    if (!Guid.TryParse(Request.Form["requestId"], out requestId))
                    {
                        RenderDashboard(runtime, string.Empty, "无法识别该申请记录，请刷新管理页后重试。");
                        return true;
                    }

                    var siteKeys = runtime.Configuration.NormalizeSiteKeys(Request.Form.GetValues("siteKeys"));
                    var allowAllSites = IsChecked(Request.Form["allowAllSites"]);
                    if (!allowAllSites && siteKeys.Count == 0)
                    {
                        RenderDashboard(runtime, string.Empty, "请至少选择一个授权站点，或勾选“所有站点”。本次操作未执行。");
                        return true;
                    }

                    var note = (Request.Form["note"] ?? string.Empty).Trim();
                    Task.Run(() => repository.ApproveRequestAsync(requestId, siteKeys.ToList(), allowAllSites, note, CancellationToken.None)).GetAwaiter().GetResult();
                    runtime.ClearAuthorizationCache();
                    return RedirectWithNotice("approved");
                }

                if (string.Equals(action, "reject-request", StringComparison.OrdinalIgnoreCase))
                {
                    Guid requestId;
                    if (!Guid.TryParse(Request.Form["requestId"], out requestId))
                    {
                        RenderDashboard(runtime, string.Empty, "无法识别该申请记录，请刷新管理页后重试。");
                        return true;
                    }

                    var note = (Request.Form["note"] ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(note))
                    {
                        note = "管理员已驳回该申请。";
                    }

                    Task.Run(() => repository.RejectRequestAsync(requestId, note, CancellationToken.None)).GetAwaiter().GetResult();
                    return RedirectWithNotice("rejected");
                }

                if (string.Equals(action, "revoke-authorization", StringComparison.OrdinalIgnoreCase))
                {
                    Guid authorizationId;
                    if (!Guid.TryParse(Request.Form["authorizationId"], out authorizationId))
                    {
                        RenderDashboard(runtime, string.Empty, "无法识别该授权记录，请刷新管理页后重试。");
                        return true;
                    }

                    var note = (Request.Form["note"] ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(note))
                    {
                        note = "管理员已撤销设备授权。";
                    }

                    Task.Run(() => repository.RevokeAuthorizationAsync(authorizationId, note, CancellationToken.None)).GetAwaiter().GetResult();
                    runtime.ClearAuthorizationCache();
                    return RedirectWithNotice("revoked");
                }

                if (string.Equals(action, "update-authorization-sites", StringComparison.OrdinalIgnoreCase))
                {
                    Guid authorizationId;
                    if (!Guid.TryParse(Request.Form["authorizationId"], out authorizationId))
                    {
                        RenderDashboard(runtime, string.Empty, "无法识别该授权记录，请刷新管理页后重试。");
                        return true;
                    }

                    if (repository.GetAuthorizationById(authorizationId) == null)
                    {
                        RenderDashboard(runtime, string.Empty, "未找到对应的授权记录，请刷新管理页后重试。");
                        return true;
                    }

                    var siteKeys = runtime.Configuration.NormalizeSiteKeys(Request.Form.GetValues("siteKeys"));
                    var allowAllSites = IsChecked(Request.Form["allowAllSites"]);
                    if (!allowAllSites && siteKeys.Count == 0)
                    {
                        RenderDashboard(runtime, string.Empty, "请至少选择一个授权站点，或勾选“所有站点”。本次操作未执行。");
                        return true;
                    }

                    var note = (Request.Form["note"] ?? string.Empty).Trim();
                    Task.Run(() => repository.UpdateAuthorizationSitesAsync(authorizationId, siteKeys.ToList(), allowAllSites, note, CancellationToken.None)).GetAwaiter().GetResult();
                    runtime.ClearAuthorizationCache();
                    return RedirectWithNotice("sites-updated");
                }

                if (string.Equals(action, "issue-app-credential", StringComparison.OrdinalIgnoreCase))
                {
                    Guid authorizationId;
                    if (!Guid.TryParse(Request.Form["authorizationId"], out authorizationId))
                    {
                        RenderDashboard(runtime, string.Empty, "无法识别该授权记录，请刷新管理页后重试。");
                        return true;
                    }

                    var authorization = repository.GetAuthorizationById(authorizationId);
                    if (authorization == null)
                    {
                        RenderDashboard(runtime, string.Empty, "未找到对应的授权记录，请刷新管理页后重试。");
                        return true;
                    }

                    var issued = runtime.CreateDeviceCredentialService().IssueAppCredential(authorization.DeviceId);
                    runtime.ClearAuthorizationCache();
                    html = runtime.Renderer.RenderIssuedAppCredentialPage(authorization, issued);
                    return true;
                }

                return false;
            }
            catch (InvalidOperationException ex)
            {
                Response.TrySkipIisCustomErrors = true;
                RenderDashboard(runtime, string.Empty, ex.Message);
                return true;
            }
            catch (Exception ex)
            {
                Response.TrySkipIisCustomErrors = true;
                RenderDashboard(runtime, string.Empty, "操作失败：" + ex.Message);
                return true;
            }
        }

        private void RenderDashboard(LegacyGatewayRuntime runtime, string notice, string error)
        {
            var snapshot = runtime.CreateRepository()
                .GetDashboardSnapshotAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            html = runtime.Renderer.RenderAdminDashboard(snapshot, GetOrCreateCsrfToken(), notice, error);
        }

        private bool RedirectWithNotice(string noticeCode)
        {
            Response.Redirect("/Admin/Default.aspx?notice=" + HttpUtility.UrlEncode(noticeCode ?? string.Empty), false);
            Context.ApplicationInstance.CompleteRequest();
            skipRender = true;
            return true;
        }

        private string ResolveNoticeFromQuery()
        {
            var code = (Request.QueryString["notice"] ?? string.Empty).Trim();
            if (string.Equals(code, "approved", StringComparison.OrdinalIgnoreCase))
            {
                return "已批准该访问申请。";
            }

            if (string.Equals(code, "rejected", StringComparison.OrdinalIgnoreCase))
            {
                return "已驳回该访问申请。";
            }

            if (string.Equals(code, "revoked", StringComparison.OrdinalIgnoreCase))
            {
                return "已撤销设备授权。";
            }

            if (string.Equals(code, "sites-updated", StringComparison.OrdinalIgnoreCase))
            {
                return "已更新授权站点。";
            }

            return string.Empty;
        }

        private static bool IsAdminMutation(string action)
        {
            return string.Equals(action, "logout", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "approve-request", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "reject-request", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "revoke-authorization", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "update-authorization-sites", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "issue-app-credential", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsChecked(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsValidCsrfToken()
        {
            var expected = Session[AdminCsrfSessionKey] as string;
            var actual = Request.Form[AdminCsrfFormField];
            return !string.IsNullOrWhiteSpace(expected)
                && !string.IsNullOrWhiteSpace(actual)
                && string.Equals(expected, actual, StringComparison.Ordinal);
        }

        private string GetOrCreateCsrfToken()
        {
            var token = Session[AdminCsrfSessionKey] as string;
            if (string.IsNullOrWhiteSpace(token))
            {
                token = CreateCsrfToken();
                Session[AdminCsrfSessionKey] = token;
            }

            return token;
        }

        private void RotateCsrfToken()
        {
            Session[AdminCsrfSessionKey] = CreateCsrfToken();
        }

        private static string CreateCsrfToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes);
        }

        private bool IsLoginBlocked(LegacyGatewayRuntime runtime, string username, out TimeSpan retryAfter)
        {
            retryAfter = TimeSpan.Zero;
            var key = BuildLoginThrottleKey(username);
            LoginThrottleEntry entry;
            if (!LoginThrottle.TryGetValue(key, out entry))
            {
                return false;
            }

            lock (entry)
            {
                var now = DateTime.UtcNow;
                var window = GetLoginWindow(runtime);
                if (now - entry.WindowStartedAtUtc >= window)
                {
                    LoginThrottleEntry removed;
                    LoginThrottle.TryRemove(key, out removed);
                    return false;
                }

                if (entry.FailureCount < GetLoginPermitLimit(runtime))
                {
                    return false;
                }

                retryAfter = window - (now - entry.WindowStartedAtUtc);
                return true;
            }
        }

        private void RecordLoginFailure(LegacyGatewayRuntime runtime, string username)
        {
            var key = BuildLoginThrottleKey(username);
            var now = DateTime.UtcNow;
            var window = GetLoginWindow(runtime);
            var entry = LoginThrottle.GetOrAdd(key, ignored => new LoginThrottleEntry(now));

            lock (entry)
            {
                if (now - entry.WindowStartedAtUtc >= window)
                {
                    entry.WindowStartedAtUtc = now;
                    entry.FailureCount = 0;
                }

                entry.FailureCount++;
            }
        }

        private void ClearLoginFailures(string username)
        {
            LoginThrottleEntry entry;
            LoginThrottle.TryRemove(BuildLoginThrottleKey(username), out entry);
        }

        private string BuildLoginThrottleKey(string username)
        {
            var clientIp = Request.UserHostAddress;
            if (string.IsNullOrWhiteSpace(clientIp))
            {
                clientIp = Request.ServerVariables["REMOTE_ADDR"] ?? string.Empty;
            }

            return clientIp.Trim() + "|" + (username ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static int GetLoginPermitLimit(LegacyGatewayRuntime runtime)
        {
            return Math.Max(1, runtime.Configuration.Admin.LoginPermitLimit);
        }

        private static TimeSpan GetLoginWindow(LegacyGatewayRuntime runtime)
        {
            return TimeSpan.FromMinutes(Math.Max(1, runtime.Configuration.Admin.LoginWindowMinutes));
        }

        private sealed class LoginThrottleEntry
        {
            public DateTime WindowStartedAtUtc;
            public int FailureCount;

            public LoginThrottleEntry(DateTime now)
            {
                WindowStartedAtUtc = now;
                FailureCount = 0;
            }
        }
    }
}
