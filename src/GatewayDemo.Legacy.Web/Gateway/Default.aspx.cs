using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.UI;
using GatewayDemo.Legacy.Core.Models;
using GatewayDemo.Legacy.Web.Infrastructure;

namespace GatewayDemo.Legacy.Web.Gateway
{
    public partial class Default : Page
    {
        private const string LastResolvedSiteCookieName = "gw_last_proxy_site";
        private string html;
        private bool skipRender;

        protected void Page_Load(object sender, EventArgs e)
        {
            Response.ContentType = "text/html; charset=utf-8";
            Response.ContentEncoding = Encoding.UTF8;

            var runtime = LegacyGatewayRuntime.Current;
            var repository = runtime.CreateRepository();
            DeviceContext device;
            try
            {
                device = runtime.CreateDeviceCredentialService().GetOrCreateContext(Context);
            }
            catch (DeviceCredentialException ex)
            {
                Response.StatusCode = ex.StatusCode;
                html = runtime.Renderer.RenderDeviceCredentialErrorPage(ex.Message);
                return;
            }

            if (repository.ProcessExternalDecision(device.DeviceId))
            {
                runtime.InvalidateAuthorizationCache(device.DeviceId);
            }

            var requestedTarget = GatewayTargetStore.Resolve(Request);
            var targetSiteKey = ResolveTargetSiteKey(runtime, requestedTarget);
            var target = ResolveTargetForSite(runtime, requestedTarget, targetSiteKey);

            if (Request.HttpMethod == "POST"
                && string.Equals(Request.Form["action"], "request-access", StringComparison.OrdinalIgnoreCase))
            {
                if (HandleRequestSubmission(runtime, repository, device, target, targetSiteKey))
                {
                    return;
                }
            }

            RenderGateway(runtime, repository, device, target, targetSiteKey, string.Empty, 200);
        }

        protected override void Render(HtmlTextWriter writer)
        {
            if (!skipRender)
            {
                writer.Write(html ?? string.Empty);
            }
        }

        private bool HandleRequestSubmission(LegacyGatewayRuntime runtime, LegacyGatewayRepository repository, DeviceContext device, string target, string targetSiteKey)
        {
            var companyName = (Request.Form["companyName"] ?? string.Empty).Trim();
            var applicantName = (Request.Form["applicantName"] ?? string.Empty).Trim();
            var phone = (Request.Form["phone"] ?? string.Empty).Trim();
            var reason = (Request.Form["reason"] ?? string.Empty).Trim();
            var allowAllSites = IsChecked(Request.Form["allowAllSites"]);
            var selectedSiteKeys = runtime.Configuration.NormalizeSiteKeys(Request.Form.GetValues("siteKeys"));
            if (!allowAllSites && selectedSiteKeys.Count == 0 && !string.IsNullOrWhiteSpace(targetSiteKey))
            {
                selectedSiteKeys.Add(targetSiteKey);
            }

            if (!allowAllSites && selectedSiteKeys.Count == 0)
            {
                RenderGateway(runtime, repository, device, target, "请至少选择一个申请站点。", 200);
                return true;
            }

            if (string.IsNullOrWhiteSpace(companyName)
                || string.IsNullOrWhiteSpace(applicantName)
                || string.IsNullOrWhiteSpace(phone))
            {
                RenderGateway(runtime, repository, device, target, "企业名称、申请人和联系电话不能为空。", 200);
                return true;
            }

            repository.CreateOrUpdateRequestAsync(
                    device,
                    companyName,
                    applicantName,
                    phone,
                    reason,
                    target,
                    selectedSiteKeys.ToList(),
                    allowAllSites,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Response.Redirect(GatewayTargetStore.BuildGatewayUrl(target), false);
            Context.ApplicationInstance.CompleteRequest();
            skipRender = true;
            return true;
        }

        private void RenderGateway(LegacyGatewayRuntime runtime, LegacyGatewayRepository repository, DeviceContext device, string target, string errorMessage, int statusCode)
        {
            RenderGateway(runtime, repository, device, target, ResolveTargetSiteKey(runtime, target), errorMessage, statusCode);
        }

        private void RenderGateway(LegacyGatewayRuntime runtime, LegacyGatewayRepository repository, DeviceContext device, string target, string targetSiteKey, string errorMessage, int statusCode)
        {
            var authorization = Task.Run(() => repository.GetActiveAuthorizationAsync(device.DeviceId, CancellationToken.None)).GetAwaiter().GetResult();
            var latestRequest = Task.Run(() => repository.GetLatestRequestAsync(device.DeviceId, CancellationToken.None)).GetAwaiter().GetResult();

            if (statusCode == 200 && !device.RequiresReview && authorization != null && authorization.AllowsSite(targetSiteKey))
            {
                Task.Run(() => repository.TouchAuthorizationAsync(device.DeviceId, device.ClientIp, targetSiteKey, CancellationToken.None)).GetAwaiter().GetResult();
                Response.Redirect(target, false);
                Context.ApplicationInstance.CompleteRequest();
                skipRender = true;
                return;
            }

            Response.StatusCode = statusCode;
            Response.TrySkipIisCustomErrors = true;
            html = runtime.Renderer.RenderGatewayPage(device, target, targetSiteKey, authorization, latestRequest, errorMessage);
        }

        private string ResolveTargetSiteKey(LegacyGatewayRuntime runtime, string target)
        {
            if (runtime == null || runtime.Configuration == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(target)
                && target.StartsWith(runtime.Configuration.Gateway.ProxyBasePath, StringComparison.OrdinalIgnoreCase))
            {
                var explicitSiteKey = runtime.Configuration.ExtractSiteKeyFromTarget(target);
                if (runtime.Configuration.Gateway.FindSite(explicitSiteKey) != null)
                {
                    return explicitSiteKey;
                }
            }

            var host = GetHostHeader();
            var hostSite = runtime.Configuration.Gateway.FindSiteByConfiguredHost(host)
                ?? runtime.Configuration.Gateway.FindSiteByHost(host);
            if (hostSite != null)
            {
                return hostSite.Key;
            }

            var cookie = Request.Cookies[LastResolvedSiteCookieName];
            var cookieValue = cookie == null ? string.Empty : HttpUtility.UrlDecode(cookie.Value ?? string.Empty);
            var cookieSiteKey = (cookieValue ?? string.Empty).Trim();
            var cookieSeparatorIndex = cookieSiteKey.IndexOf('|');
            if (cookieSeparatorIndex >= 0)
            {
                // Cookie 记录的是“签发时的 authority|站点”，跨端口访问时不采用。
                var cookieAuthority = cookieSiteKey.Substring(0, cookieSeparatorIndex).Trim();
                cookieSiteKey = cookieSiteKey.Substring(cookieSeparatorIndex + 1).Trim();
                if (!string.Equals(cookieAuthority, host, StringComparison.OrdinalIgnoreCase))
                {
                    cookieSiteKey = string.Empty;
                }
            }

            if (!string.IsNullOrWhiteSpace(cookieSiteKey)
                && runtime.Configuration.Gateway.FindSite(cookieSiteKey) != null)
            {
                return cookieSiteKey;
            }

            var fallbackSiteKey = runtime.Configuration.ExtractSiteKeyFromTarget(target);
            if (runtime.Configuration.Gateway.FindSite(fallbackSiteKey) != null)
            {
                return fallbackSiteKey;
            }

            return runtime.Configuration.Gateway.Sites.Count == 0
                ? string.Empty
                : runtime.Configuration.Gateway.Sites[0].Key;
        }

        private string ResolveTargetForSite(LegacyGatewayRuntime runtime, string requestedTarget, string siteKey)
        {
            if (runtime == null || runtime.Configuration == null || string.IsNullOrWhiteSpace(siteKey))
            {
                return runtime == null || runtime.Configuration == null
                    ? (requestedTarget ?? string.Empty)
                    : runtime.Configuration.ResolveTargetPath(requestedTarget);
            }

            var raw = (requestedTarget ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return runtime.Configuration.BuildSiteTargetPath(siteKey);
            }

            Uri absoluteUri;
            if (Uri.TryCreate(raw, UriKind.Absolute, out absoluteUri))
            {
                raw = string.IsNullOrWhiteSpace(absoluteUri.PathAndQuery)
                    ? string.Empty
                    : absoluteUri.PathAndQuery;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return runtime.Configuration.BuildSiteTargetPath(siteKey);
            }

            if (raw.StartsWith(runtime.Configuration.Gateway.ProxyBasePath, StringComparison.OrdinalIgnoreCase))
            {
                return runtime.Configuration.ResolveTargetPath(raw);
            }

            var site = runtime.Configuration.Gateway.FindSite(siteKey);
            if (site == null)
            {
                return runtime.Configuration.ResolveTargetPath(raw);
            }

            if (UsesGatewayRootPaths(site))
            {
                return raw.StartsWith("/", StringComparison.Ordinal) ? raw : "/" + raw.TrimStart('/');
            }

            return runtime.Configuration.BuildProxyPath(siteKey, raw.TrimStart('/'));
        }

        private string GetHostHeader()
        {
            var runtime = LegacyGatewayRuntime.Current;
            return runtime == null || runtime.Configuration == null
                ? (Request.Headers["Host"] ?? string.Empty)
                : GatewayRequestContext.GetExternalHost(Context, runtime.Configuration.Gateway);
        }

        private static bool UsesGatewayRootPaths(GatewayDemo.Legacy.Core.Options.GatewaySiteOptions site)
        {
            return GatewayPathUtility.UsesGatewayRootPathsForRequest(site);
        }

        private static bool IsChecked(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }
    }
}
