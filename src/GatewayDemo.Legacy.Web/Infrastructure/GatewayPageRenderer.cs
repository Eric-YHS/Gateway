using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using GatewayDemo.Legacy.Core.Models;
using GatewayDemo.Legacy.Core.Services;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    public sealed class GatewayPageRenderer
    {
        private readonly LegacyGatewayConfiguration configuration;

        public GatewayPageRenderer(LegacyGatewayConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public string RenderGatewayPage(DeviceContext device, string targetPath, string resolvedTargetSiteKey, GatewayDeviceAuthorization authorization, GatewayAccessRequest latestRequest, string errorMessage)
        {
            var builder = new StringBuilder();
            var targetSiteKey = string.IsNullOrWhiteSpace(resolvedTargetSiteKey)
                ? configuration.ExtractSiteKeyFromTarget(targetPath)
                : resolvedTargetSiteKey;

            builder.Append(BuildPageStart(configuration.Gateway.AppName + " - 访问申请"));
            builder.Append("<div class=\"shell\">");
            builder.Append("<div class=\"hero\"><div><h1>")
                .Append(E(configuration.Gateway.AppName))
                .Append("</h1></div>");
            builder.Append("<div class=\"chips\">")
                .Append(Chip(device.RequiresReview ? "需要复核" : "设备凭据正常", device.RequiresReview ? "danger" : "success"))
                .Append(Chip("目标站点：" + GetSiteName(targetSiteKey), "neutral"))
                .Append("</div></div>");

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                builder.Append("<div class=\"notice danger\"><strong>校验失败。</strong><span>")
                    .Append(E(errorMessage))
                    .Append("</span></div>");
            }

            builder.Append("<section class=\"card\"><h2>当前设备</h2><div class=\"grid\">")
                .Append(Row("设备编号", device.DeviceCode))
                .Append(Row("设备标识", device.DeviceId))
                .Append(Row("凭据类型", GetCredentialChannelText(device.CredentialChannel)))
                .Append(Row("环境指纹", device.SignalHash))
                .Append(Row("访问 IP", device.ClientIp))
                .Append(Row("目标路径", targetPath))
                .Append("</div>");

            builder.Append(device.RequiresReview
                ? "<div class=\"notice danger\"><strong>当前设备需要复核。</strong><span>" + E(device.ChallengeReason) + "</span></div>"
                : "<div class=\"notice success\"><strong>设备凭据状态稳定。</strong></div>");
            builder.Append("</section>");

            if (authorization != null)
            {
                builder.Append("<section class=\"card\"><h2>当前授权</h2><div class=\"grid\">")
                    .Append(Row("企业名称", DisplayValue(authorization.CompanyName)))
                    .Append(Row("申请人", DisplayValue(authorization.ApplicantName)))
                    .Append(Row("联系电话", DisplayValue(authorization.Phone)))
                    .Append(Row("设备编号", DisplayValue(authorization.DeviceCode)))
                    .Append(Row("设备标识", DisplayValue(authorization.DeviceId)))
                    .Append(Row("当前已授权站点", FormatAuthorizationSitesPlainText(authorization)))
                    .Append(Row("最近访问 IP", DisplayValue(authorization.LastSeenIp)))
                    .Append(Row("最近访问站点", DisplayValue(GetSiteName(authorization.LastSeenSiteKey))))
                    .Append(Row("最近访问路径", DisplayValue(authorization.LastSeenPath)))
                    .Append(Row("最近访问时间", FormatNullableDateTime(authorization.LastSeenAtUtc)))
                    .Append(Row("审核说明", DisplayValue(authorization.ReviewNote)))
                    .Append("</div>");
                var authorizationMobileIdentity = BuildMobileIdentitySummary(authorization);
                if (!string.IsNullOrWhiteSpace(authorizationMobileIdentity))
                {
                    builder.Append("<div class=\"notice neutral\"><strong>移动端信息</strong><span>")
                        .Append(authorizationMobileIdentity)
                        .Append("</span></div>");
                }

                builder.Append("</section>");
            }

            if (latestRequest != null)
            {
                builder.Append("<section class=\"card\"><h2>最近一次申请</h2><div class=\"grid\">")
                    .Append(Row("状态", GetRequestStatusText(latestRequest.Status)))
                    .Append(Row("企业名称", DisplayValue(latestRequest.CompanyName)))
                    .Append(Row("申请人", DisplayValue(latestRequest.ApplicantName)))
                    .Append(Row("联系电话", DisplayValue(latestRequest.Phone)))
                    .Append(Row("申请原因", DisplayValue(latestRequest.Reason)))
                    .Append(Row("目标路径", DisplayValue(latestRequest.TargetPath)))
                    .Append(Row("访问 IP", DisplayValue(latestRequest.ClientIp)))
                    .Append(Row("提交时间", FormatDateTime(latestRequest.CreatedAtUtc)))
                    .Append(Row("审核时间", FormatNullableDateTime(latestRequest.ReviewedAtUtc)))
                    .Append(Row("处理完成", FormatNullableDateTime(latestRequest.ProcessedAtUtc)))
                    .Append(Row("审核说明", string.IsNullOrWhiteSpace(latestRequest.ReviewNote) ? "-" : latestRequest.ReviewNote))
                    .Append(Row("本次申请站点", FormatRequestSites(latestRequest)))
                    .Append(latestRequest.Status == GatewayAccessRequestStatus.Approved
                        ? Row(CurrentAuthorizationAfterReviewText(), FormatAuthorizationSitesPlainText(authorization))
                        : string.Empty)
                    .Append("</div>");
                var requestMobileIdentity = BuildMobileIdentitySummary(latestRequest);
                if (!string.IsNullOrWhiteSpace(requestMobileIdentity))
                {
                    builder.Append("<div class=\"notice neutral\"><strong>移动端信息</strong><span>")
                        .Append(requestMobileIdentity)
                        .Append("</span></div>");
                }

                builder.Append("</section>");
            }

            builder.Append("<section class=\"card\"><h2>提交访问申请</h2>")
                .Append("<form method=\"post\" action=\"/Gateway/Default.aspx\">")
                .Append("<input type=\"hidden\" name=\"action\" value=\"request-access\" />")
                .Append("<input type=\"hidden\" name=\"targetPath\" value=\"").Append(E(targetPath)).Append("\" />")
                .Append("<div class=\"form-grid\">")
                .Append(Input("企业名称", "companyName", true))
                .Append(Input("申请人", "applicantName", true))
                .Append(Input("联系电话", "phone", true))
                .Append(Input("申请原因", "reason"))
                .Append("</div><div><p class=\"section-label\">本次申请站点</p><div class=\"sites\">");

            var allowAllSites = latestRequest != null && latestRequest.AllowAllSites;
            builder.Append(AllSitesCheckbox("allowAllSites", allowAllSites));

            var latestRequestSiteKeys = !allowAllSites && latestRequest != null && latestRequest.RequestedSites != null && latestRequest.RequestedSites.Count > 0
                ? latestRequest.RequestedSites.Select(functionSite => functionSite.SiteKey).ToArray()
                : new string[0];
            var selectedSites = allowAllSites
                ? new string[0]
                : MergeSiteKeys(latestRequestSiteKeys, string.IsNullOrWhiteSpace(targetSiteKey) ? new string[0] : new[] { targetSiteKey });

            foreach (var site in configuration.Gateway.Sites)
            {
                var isChecked = !allowAllSites && selectedSites.Any(item => string.Equals(item, site.Key, StringComparison.OrdinalIgnoreCase));
                builder.Append("<label class=\"check\"><input type=\"checkbox\" name=\"siteKeys\" value=\"")
                    .Append(E(site.Key))
                    .Append("\"")
                    .Append(isChecked ? " checked=\"checked\"" : string.Empty)
                    .Append(allowAllSites ? " disabled=\"disabled\"" : string.Empty)
                    .Append(" /><span>")
                    .Append(E(site.Name))
                    .Append("</span></label>");
            }

            builder.Append("</div></div><div class=\"actions\"><button type=\"submit\">提交申请</button></div></form></section>");

            builder.Append("<section class=\"card\"><h2>受保护站点</h2><ul class=\"catalog\">");
            foreach (var site in configuration.Gateway.Sites)
            {
                builder.Append("<li><strong>")
                    .Append(E(site.Name))
                    .Append("</strong><code>")
                    .Append(E(BuildSiteDisplayUrl(site)))
                    .Append("</code></li>");
            }
            builder.Append("</ul></section></div>");
            builder.Append(BuildPageEnd());
            return builder.ToString();
        }

        public string RenderAdminLoginPage(string errorMessage)
        {
            var builder = new StringBuilder();
            builder.Append(BuildPageStart(configuration.Gateway.AppName + " - 管理后台登录"));
            builder.Append("<div class=\"shell narrow\"><section class=\"card\"><h1>管理员登录</h1>");
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                builder.Append("<div class=\"notice danger\"><strong>登录失败。</strong><span>")
                    .Append(E(errorMessage))
                    .Append("</span></div>");
            }

            builder.Append("<form method=\"post\" action=\"/Admin/Default.aspx\"><input type=\"hidden\" name=\"action\" value=\"login\" />")
                .Append(Input("用户名", "username"))
                .Append("<label><span>密码</span><input type=\"password\" name=\"password\" maxlength=\"200\" /></label>")
                .Append("<div class=\"actions\"><button type=\"submit\">进入后台</button></div></form></section></div>")
                .Append(BuildPageEnd());
            return builder.ToString();
        }

        public string RenderAdminDashboard(GatewayDashboardSnapshot snapshot, string csrfToken)
        {
            return RenderAdminDashboard(snapshot, csrfToken, string.Empty, string.Empty);
        }

        public string RenderAdminDashboard(GatewayDashboardSnapshot snapshot, string csrfToken, string noticeMessage, string errorMessage)
        {
            var pendingRequests = snapshot.Requests.Where(item => item.Status == GatewayAccessRequestStatus.Pending).ToList();
            var builder = new StringBuilder();
            builder.Append(BuildPageStart(configuration.Gateway.AppName + " - 管理后台"));
            builder.Append("<div class=\"shell\"><div class=\"toolbar\"><div><h1>网关管理台</h1></div><form method=\"post\" action=\"/Admin/Default.aspx\"><input type=\"hidden\" name=\"action\" value=\"logout\" />")
                .Append(CsrfField(csrfToken))
                .Append("<button type=\"submit\">退出登录</button></form></div>");

            if (!string.IsNullOrWhiteSpace(noticeMessage))
            {
                builder.Append("<div class=\"notice success\"><span>")
                    .Append(E(noticeMessage))
                    .Append("</span></div>");
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                builder.Append("<div class=\"notice danger\"><span>")
                    .Append(E(errorMessage))
                    .Append("</span></div>");
            }

            builder.Append("<section class=\"card\"><h2>待审核申请</h2><p class=\"muted section-subtitle\">设备或浏览器首次访问受保护站点时自动提交的接入申请，需要管理员逐条批准或驳回后对应设备/浏览器才能继续访问。</p>");
            if (pendingRequests.Count == 0)
            {
                builder.Append("<p class=\"muted\">当前没有待审核申请。</p>");
            }
            else
            {
                foreach (var request in pendingRequests)
                {
                    var existingAuthorization = snapshot.Authorizations.FirstOrDefault(authorization =>
                        authorization.Status == GatewayAuthorizationStatus.Approved
                        && string.Equals(authorization.DeviceId, request.DeviceId, StringComparison.OrdinalIgnoreCase));

                    builder.Append("<article class=\"item\"><div class=\"item-head\"><h3>")
                        .Append(E(request.CompanyName))
                        .Append(" / ")
                        .Append(E(request.DeviceCode))
                        .Append("</h3>")
                        .Append(Chip("待审核", "warning"))
                        .Append("</div><div class=\"grid\">")
                        .Append(Row("企业名称", DisplayValue(request.CompanyName)))
                        .Append(Row("申请人", DisplayValue(request.ApplicantName)))
                        .Append(Row("联系电话", DisplayValue(request.Phone)))
                        .Append(Row("访问 IP", DisplayValue(request.ClientIp)))
                        .Append(Row("设备编号", DisplayValue(request.DeviceCode)))
                        .Append(Row("设备标识", DisplayValue(request.DeviceId)))
                        .Append(Row("目标路径", DisplayValue(request.TargetPath)))
                        .Append(Row("申请原因", DisplayValue(request.Reason)))
                        .Append(Row("提交时间", FormatDateTime(request.CreatedAtUtc)))
                        .Append(Row("本次申请站点", FormatRequestSites(request)))
                        .Append(Row("当前已授权站点", FormatAuthorizationSitesPlainText(existingAuthorization)))
                        .Append("</div>");

                    var mobileIdentity = BuildMobileIdentitySummary(request);
                    if (!string.IsNullOrWhiteSpace(mobileIdentity))
                    {
                        builder.Append("<div class=\"notice neutral\"><strong>移动端信息</strong><span>")
                            .Append(mobileIdentity)
                            .Append("</span></div>");
                    }

                    // 审批默认勾选“本次申请站点 ∪ 该设备现有授权站点”，与批准后的
                    // 合并结果保持一致，避免管理员把默认勾选误读成最终授权集合。
                    var effectiveAllowAllSites = request.AllowAllSites
                        || (existingAuthorization != null && existingAuthorization.AllowsAllSites);

                    builder.Append("<form method=\"post\" action=\"/Admin/Default.aspx\"><input type=\"hidden\" name=\"requestId\" value=\"")
                        .Append(request.Id.ToString("D"))
                        .Append("\" />")
                        .Append(CsrfField(csrfToken))
                        .Append("<p class=\"section-label\">本次审核通过站点</p>")
                        .Append(AllSitesCheckbox("allowAllSites", effectiveAllowAllSites))
                        .Append("<div class=\"sites\">");

                    foreach (var site in configuration.Gateway.Sites)
                    {
                        var isChecked = !effectiveAllowAllSites
                            && (request.RequestedSites.Any(item => string.Equals(item.SiteKey, site.Key, StringComparison.OrdinalIgnoreCase))
                                || (existingAuthorization != null
                                    && existingAuthorization.AuthorizedSites != null
                                    && existingAuthorization.AuthorizedSites.Any(item => string.Equals(item.SiteKey, site.Key, StringComparison.OrdinalIgnoreCase))));
                        builder.Append("<label class=\"check\"><input type=\"checkbox\" name=\"siteKeys\" value=\"")
                            .Append(E(site.Key))
                            .Append("\"")
                        .Append(isChecked ? " checked=\"checked\"" : string.Empty)
                        .Append(effectiveAllowAllSites ? " disabled=\"disabled\"" : string.Empty)
                        .Append(" />")
                        .Append(E(site.Name))
                        .Append("</label>");
                    }

                    builder.Append("</div>")
                        .Append(Input("审核说明", "note"))
                        .Append("<div class=\"actions\"><button type=\"submit\" name=\"action\" value=\"approve-request\">批准通过</button><button type=\"submit\" name=\"action\" value=\"reject-request\" class=\"danger-button\">驳回申请</button></div></form></article>");
                }
            }
            builder.Append("</section>");

            builder.Append("<section class=\"card\"><div class=\"section-head\"><h2>设备授权</h2><label class=\"search-box\"><span>搜索授权</span><input type=\"text\" class=\"js-authorization-search\" autocomplete=\"off\" /></label></div><p class=\"muted section-subtitle\">已批准接入申请后生成的设备/浏览器授权记录，可在此撤销授权、调整可访问站点范围或签发移动 APP 接口凭据。</p>");
            if (snapshot.Authorizations.Count == 0)
            {
                builder.Append("<p class=\"muted\">当前还没有已批准的设备。</p>");
            }
            else
            {
                foreach (var authorization in snapshot.Authorizations)
                {
                    GatewayManagedDevice device = null;
                    if (snapshot.Devices.ContainsKey(authorization.DeviceId))
                    {
                        device = snapshot.Devices[authorization.DeviceId];
                    }

                    builder.Append("<article class=\"item js-authorization-item\" data-search=\"")
                        .Append(E(BuildAuthorizationSearchText(authorization, device)))
                        .Append("\"><div class=\"item-head\"><h3>")
                        .Append(E(authorization.CompanyName))
                        .Append(" / ")
                        .Append(E(authorization.DeviceCode))
                        .Append("</h3>")
                        .Append(authorization.Status == GatewayAuthorizationStatus.Approved ? Chip("已授权", "success") : Chip("已撤销", "danger"))
                        .Append("</div><div class=\"grid\">")
                        .Append(Row("企业名称", DisplayValue(authorization.CompanyName)))
                        .Append(Row("申请人", DisplayValue(authorization.ApplicantName)))
                        .Append(Row("联系电话", DisplayValue(authorization.Phone)))
                        .Append(Row("设备编号", DisplayValue(authorization.DeviceCode)))
                        .Append(Row("设备标识", DisplayValue(authorization.DeviceId)))
                        .Append(Row("授权站点", FormatAuthorizationSitesPlainText(authorization)))
                        .Append(Row("最近访问 IP", DisplayValue(authorization.LastSeenIp)))
                        .Append(Row("最近访问站点", DisplayValue(GetSiteName(authorization.LastSeenSiteKey))))
                        .Append(Row("最近访问路径", DisplayValue(authorization.LastSeenPath)))
                        .Append(Row("最近访问时间", FormatNullableDateTime(authorization.LastSeenAtUtc)))
                        .Append(Row("接口凭据", device != null && device.HasAppCredential ? "已签发" : "未签发"))
                        .Append(Row("审核说明", DisplayValue(authorization.ReviewNote)))
                        .Append("</div>");

                    var mobileIdentity = BuildMobileIdentitySummary(authorization);
                    if (!string.IsNullOrWhiteSpace(mobileIdentity))
                    {
                        builder.Append("<div class=\"notice neutral\"><strong>移动端信息</strong><span>")
                            .Append(mobileIdentity)
                            .Append("</span></div>");
                    }

                    if (authorization.Status == GatewayAuthorizationStatus.Approved)
                    {
                        AppendAuthorizationEditForm(builder, authorization, csrfToken);
                        builder.Append("<form method=\"post\" action=\"/Admin/Default.aspx\"><input type=\"hidden\" name=\"action\" value=\"issue-app-credential\" /><input type=\"hidden\" name=\"authorizationId\" value=\"")
                            .Append(authorization.Id.ToString("D"))
                            .Append("\" />")
                            .Append(CsrfField(csrfToken))
                            .Append("<div class=\"actions\"><button type=\"submit\">签发接口凭据</button></div></form>");
                        builder.Append("<form method=\"post\" action=\"/Admin/Default.aspx\"><input type=\"hidden\" name=\"action\" value=\"revoke-authorization\" /><input type=\"hidden\" name=\"authorizationId\" value=\"")
                            .Append(authorization.Id.ToString("D"))
                            .Append("\" />")
                            .Append(CsrfField(csrfToken))
                            .Append(Input("撤销说明", "note"))
                            .Append("<div class=\"actions\"><button type=\"submit\" class=\"danger-button\">撤销授权</button></div></form>");
                    }
                    builder.Append("</article>");
                }

                builder.Append("<p class=\"muted js-authorization-empty\" style=\"display:none;\">没有匹配的授权设备。</p>");
            }
            builder.Append("</section>");

            builder.Append("<section class=\"card\"><h2>审计日志</h2><p class=\"muted section-subtitle\">网关自动记录的设备识别、审核拦截与授权访问事件，用于排查“为什么这台设备/浏览器被拦截了”。</p><ul class=\"audit-list\">");
            foreach (var log in snapshot.AuditLogs.Take(24))
            {
                builder.Append("<li><strong>")
                    .Append(E(log.Message))
                    .Append("</strong><span>")
                    .Append(E(FormatDateTime(log.CreatedAtUtc)))
                    .Append(" | ")
                    .Append(E(log.DeviceCode))
                    .Append(" | ")
                    .Append(E(log.Kind))
                    .Append("</span></li>");
            }
            builder.Append("</ul></section></div>")
                .Append(BuildPageEnd());
            return builder.ToString();
        }

        public string RenderIssuedAppCredentialPage(GatewayDeviceAuthorization authorization, IssuedAppCredential credential)
        {
            var builder = new StringBuilder();
            builder.Append(BuildPageStart(configuration.Gateway.AppName + " - 接口凭据"));
            builder.Append("<div class=\"shell narrow\"><section class=\"card\"><h1>已签发接口凭据</h1>");
            builder.Append("<div class=\"notice warning\"><strong>仅显示一次。</strong></div>");
            builder.Append("<div class=\"grid\">")
                .Append(Row("企业名称", authorization.CompanyName))
                .Append(Row("设备编号", credential.DeviceCode))
                .Append(Row("APP Key", credential.CredentialKey))
                .Append(Row("APP Secret", credential.CredentialSecret))
                .Append(Row("请求头 AppKey", DeviceCredentialService.AppKeyHeaderName))
                .Append(Row("请求头 时间戳", DeviceCredentialService.AppTimestampHeaderName))
                .Append(Row("请求头 Nonce", DeviceCredentialService.AppNonceHeaderName))
                .Append(Row("请求头 BodyHash", DeviceCredentialService.AppBodyHashHeaderName))
                .Append(Row("请求头 Signature", DeviceCredentialService.AppSignatureHeaderName))
                .Append(Row("示例时间戳", credential.ExampleTimestamp.ToString()))
                .Append(Row("示例 Nonce", credential.ExampleNonce))
                .Append(Row("示例 BodyHash", credential.ExampleBodyHash))
                .Append("</div><div class=\"notice success\"><strong>示例签名原文</strong><span><code>")
                .Append(E(credential.ExamplePayload))
                .Append("</code></span><span>示例签名结果：<code>")
                .Append(E(credential.ExampleSignature))
                .Append("</code></span></div><div class=\"actions\"><a href=\"/Admin/Default.aspx\"><button type=\"button\">返回管理台</button></a></div></section></div>")
                .Append(BuildPageEnd());
            return builder.ToString();
        }

        public string RenderDeviceCredentialErrorPage(string message)
        {
            return BuildPageStart(configuration.Gateway.AppName + " - 设备凭据异常")
                + "<div class=\"shell narrow\"><section class=\"card\"><h1>设备凭据校验失败</h1><div class=\"notice danger\"><span>"
                + E(message)
                + "</span></div></section></div>"
                + BuildPageEnd();
        }

        public string RenderNotFoundPage(string message)
        {
            return BuildPageStart(configuration.Gateway.AppName + " - 页面不存在")
                + "<div class=\"shell narrow\"><section class=\"card\"><h1>页面不存在</h1><div class=\"notice danger\"><span>"
                + E(message)
                + "</span></div></section></div>"
                + BuildPageEnd();
        }

        public string RenderServerErrorPage(string message)
        {
            return BuildPageStart(configuration.Gateway.AppName + " - 服务器错误")
                + "<div class=\"shell narrow\"><section class=\"card\"><h1>服务器处理请求时发生错误</h1><div class=\"notice danger\"><span>"
                + E(message)
                + "</span></div></section></div>"
                + BuildPageEnd();
        }

        private void AppendAuthorizationEditForm(StringBuilder builder, GatewayDeviceAuthorization authorization, string csrfToken)
        {
            var allowAllSites = authorization != null && authorization.AllowsAllSites;
            builder.Append("<form method=\"post\" action=\"/Admin/Default.aspx\"><input type=\"hidden\" name=\"action\" value=\"update-authorization-sites\" /><input type=\"hidden\" name=\"authorizationId\" value=\"")
                .Append(authorization.Id.ToString("D"))
                .Append("\" />")
                .Append(CsrfField(csrfToken))
                .Append("<p class=\"section-label\">当前授权站点</p>")
                .Append(AllSitesCheckbox("allowAllSites", allowAllSites))
                .Append("<div class=\"sites\">");

            foreach (var site in configuration.Gateway.Sites)
            {
                var isChecked = !allowAllSites && authorization.AuthorizedSites.Any(item => string.Equals(item.SiteKey, site.Key, StringComparison.OrdinalIgnoreCase));
                builder.Append("<label class=\"check\"><input type=\"checkbox\" name=\"siteKeys\" value=\"")
                    .Append(E(site.Key))
                    .Append("\"")
                    .Append(isChecked ? " checked=\"checked\"" : string.Empty)
                    .Append(allowAllSites ? " disabled=\"disabled\"" : string.Empty)
                    .Append(" />")
                    .Append(E(site.Name))
                    .Append("</label>");
            }

            builder.Append("</div>")
                .Append(Input(UpdateNoteText(), "note"))
                .Append("<div class=\"actions\"><button type=\"submit\">")
                .Append(E(UpdateAuthorizationText()))
                .Append("</button></div></form>");
        }

        private string FormatAuthorizationSites(GatewayDeviceAuthorization authorization)
        {
            if (authorization != null && authorization.AllowsAllSites)
            {
                return E(AllSitesText());
            }

            if (authorization == null || authorization.AuthorizedSites == null || authorization.AuthorizedSites.Count == 0)
            {
                return "-";
            }

            return string.Join(", ", authorization.AuthorizedSites.Select(functionSite => E(GetSiteName(functionSite.SiteKey))));
        }

        private static string[] MergeSiteKeys(params IEnumerable<string>[] siteKeyGroups)
        {
            return (siteKeyGroups ?? new IEnumerable<string>[0])
                .Where(group => group != null)
                .SelectMany(group => group)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private string FormatAuthorizationSitesPlainText(GatewayDeviceAuthorization authorization)
        {
            if (authorization != null && authorization.AllowsAllSites)
            {
                return AllSitesText();
            }

            if (authorization == null || authorization.AuthorizedSites == null || authorization.AuthorizedSites.Count == 0)
            {
                return "-";
            }

            return string.Join(", ", authorization.AuthorizedSites.Select(functionSite => GetSiteName(functionSite.SiteKey)));
        }

        private string FormatRequestSites(GatewayAccessRequest request)
        {
            if (request != null && request.AllowAllSites)
            {
                return AllSitesText();
            }

            if (request == null || request.RequestedSites == null || request.RequestedSites.Count == 0)
            {
                return "-";
            }

            return string.Join(", ", request.RequestedSites.Select(functionSite => GetSiteName(functionSite.SiteKey)));
        }

        private static string AllSitesCheckbox(string name, bool isChecked)
        {
            return string.Concat(
                "<label class=\"check\"><input type=\"checkbox\" class=\"js-all-sites\" name=\"",
                E(name),
                "\" value=\"true\"",
                isChecked ? " checked=\"checked\"" : string.Empty,
                " /><span>",
                E(AllSitesText()),
                "</span></label>");
        }

        private static string AllSitesText()
        {
            return "所有站点";
        }

        private static string UpdateAuthorizationText()
        {
            return "更新授权站点";
        }

        private static string UpdateNoteText()
        {
            return "变更说明";
        }

        private static string CurrentAuthorizationAfterReviewText()
        {
            return "审批后当前授权";
        }

        private string GetSiteName(string siteKey)
        {
            var site = configuration.Gateway.FindSite(siteKey);
            return site == null ? siteKey : site.Name;
        }

        private static string Row(string label, string value)
        {
            return string.Concat("<div><span>", E(label), "</span><strong>", E(value), "</strong></div>");
        }

        private static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private static string Input(string label, string name)
        {
            return Input(label, name, false);
        }

        private static string Input(string label, string name, bool required)
        {
            return string.Concat("<label><span>", E(label), "</span><input type=\"text\" name=\"", E(name), "\" maxlength=\"200\"", required ? " required=\"required\"" : string.Empty, " /></label>");
        }

        private static string CsrfField(string token)
        {
            return string.Concat("<input type=\"hidden\" name=\"csrfToken\" value=\"", E(token), "\" />");
        }

        private static string Chip(string text, string tone)
        {
            return string.Concat("<span class=\"chip ", E(tone), "\">", E(text), "</span>");
        }

        private static string BuildMobileIdentitySummary(GatewayAccessRequest request)
        {
            if (request == null)
            {
                return string.Empty;
            }

            if (!HasLegacyMobileIdentity(
                request.LegacyCompanyId,
                request.LegacyUserId,
                request.LegacyDataCenterId,
                request.LegacyDeviceImei,
                request.LegacyMobileUserNum,
                request.LegacyUrlBefore))
            {
                return BuildMobileIdentitySummary(request.Reason);
            }

            var builder = new StringBuilder();
            AppendIdentityChip(builder, "企业号", request.LegacyCompanyId);
            AppendIdentityChip(builder, "账号", request.LegacyUserId);
            AppendIdentityChip(builder, "姓名", request.ApplicantName);
            AppendIdentityChip(builder, "数据中心", request.LegacyDataCenterId);
            AppendIdentityChip(builder, "设备号", request.LegacyDeviceImei);
            AppendIdentityChip(builder, "手机号", request.LegacyMobileUserNum);
            AppendIdentityChip(builder, "入口地址", request.LegacyUrlBefore);
            return builder.ToString();
        }

        private static string BuildMobileIdentitySummary(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)
                || (reason.IndexOf("CompanyId:", StringComparison.OrdinalIgnoreCase) < 0
                    && reason.IndexOf("UserId:", StringComparison.OrdinalIgnoreCase) < 0
                    && reason.IndexOf("UserName:", StringComparison.OrdinalIgnoreCase) < 0
                    && reason.IndexOf("DeviceImei:", StringComparison.OrdinalIgnoreCase) < 0
                    && reason.IndexOf("DataCenterId:", StringComparison.OrdinalIgnoreCase) < 0
                    && reason.IndexOf("UrlBefore:", StringComparison.OrdinalIgnoreCase) < 0))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            AppendIdentityChip(builder, "企业号", ExtractSummaryValue(reason, "CompanyId"));
            AppendIdentityChip(builder, "账号", ExtractSummaryValue(reason, "UserId"));
            AppendIdentityChip(builder, "姓名", ExtractSummaryValue(reason, "UserName"));
            AppendIdentityChip(builder, "数据中心", ExtractSummaryValue(reason, "DataCenterId"));
            AppendIdentityChip(builder, "设备号", ExtractSummaryValue(reason, "DeviceImei"));
            AppendIdentityChip(builder, "入口地址", ExtractSummaryValue(reason, "UrlBefore"));
            return builder.ToString();
        }

        private static string BuildMobileIdentitySummary(GatewayDeviceAuthorization authorization)
        {
            if (authorization == null)
            {
                return string.Empty;
            }

            if (!HasLegacyMobileIdentity(
                authorization.LegacyCompanyId,
                authorization.LegacyUserId,
                authorization.LegacyDataCenterId,
                authorization.LegacyDeviceImei,
                authorization.LegacyMobileUserNum,
                authorization.LegacyUrlBefore))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            AppendIdentityChip(builder, "企业号", authorization.LegacyCompanyId);
            AppendIdentityChip(builder, "账号", authorization.LegacyUserId);
            AppendIdentityChip(builder, "姓名", authorization.ApplicantName);
            AppendIdentityChip(builder, "数据中心", authorization.LegacyDataCenterId);
            AppendIdentityChip(builder, "设备号", authorization.LegacyDeviceImei);
            AppendIdentityChip(builder, "手机号", authorization.LegacyMobileUserNum);
            AppendIdentityChip(builder, "入口地址", authorization.LegacyUrlBefore);
            return builder.ToString();
        }

        private static bool HasLegacyMobileIdentity(params string[] values)
        {
            return values != null && values.Any(value => !string.IsNullOrWhiteSpace(value));
        }

        private string BuildSiteDisplayUrl(GatewayDemo.Legacy.Core.Options.GatewaySiteOptions site)
        {
            if (site == null)
            {
                return string.Empty;
            }

            var firstHost = site.HostNames == null ? string.Empty : site.HostNames.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstHost))
            {
                return configuration.BuildSiteTargetPath(site.Key);
            }

            var host = firstHost.Trim().TrimEnd('/');
            if (!host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                host = "http://" + host;
            }

            Uri baseUri;
            if (!Uri.TryCreate(host, UriKind.Absolute, out baseUri))
            {
                return configuration.BuildSiteTargetPath(site.Key);
            }

            var path = string.IsNullOrWhiteSpace(site.EntryPath)
                ? string.Empty
                : "/" + site.EntryPath.TrimStart('/');
            return baseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + path;
        }

        private static string BuildAuthorizationSearchText(GatewayDeviceAuthorization authorization, GatewayManagedDevice device)
        {
            if (authorization == null)
            {
                return string.Empty;
            }

            return string.Join(" ", new[]
            {
                authorization.CompanyName,
                authorization.ApplicantName,
                authorization.Phone,
                authorization.DeviceCode,
                authorization.DeviceId,
                authorization.LegacyCompanyId,
                authorization.LegacyUserId,
                authorization.LegacyDataCenterId,
                authorization.LegacyDeviceImei,
                authorization.LegacyUrlBefore,
                device == null ? string.Empty : device.RegisteredIp,
                device == null ? string.Empty : device.LastSeenIp
            }.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        private static void AppendIdentityChip(StringBuilder builder, string label, string value)
        {
            if (builder == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(" ");
            }

            builder.Append("<code>")
                .Append(E(label))
                .Append("=")
                .Append(E(value))
                .Append("</code>");
        }

        private static string ExtractSummaryValue(string reason, string label)
        {
            if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(label))
            {
                return string.Empty;
            }

            var token = label + ":";
            var start = reason.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return string.Empty;
            }

            start += token.Length;
            var end = reason.IndexOf(" | ", start, StringComparison.Ordinal);
            var value = end >= 0 ? reason.Substring(start, end - start) : reason.Substring(start);
            return value.Trim();
        }

        private static string BuildPageStart(string title)
        {
            return "<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" /><title>"
                + E(title)
                + "</title><style>"
                + "body{margin:0;font-family:'Segoe UI',sans-serif;background:#eef4f7;color:#12202b;} .shell{max-width:1100px;margin:0 auto;padding:24px;} .narrow{max-width:720px;} .hero,.toolbar,.section-head{display:flex;justify-content:space-between;gap:18px;align-items:flex-start;margin-bottom:18px;} .section-head{align-items:flex-end;} .chips{display:flex;gap:10px;flex-wrap:wrap;justify-content:flex-end;} h1,h2,h3,p{margin-top:0;} .section-head h2{margin-bottom:0;} .card{background:#fff;border-radius:18px;padding:22px;margin:0 0 18px;box-shadow:0 16px 38px rgba(18,32,43,.07);} .grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;} .grid div{min-width:0;padding:12px;border:1px solid #d7e2ea;border-radius:12px;background:#f7fbfd;} .grid span{display:block;color:#5a7382;font-size:12px;margin-bottom:6px;} .grid strong{display:block;max-width:100%;max-height:160px;overflow:auto;overflow-wrap:anywhere;word-break:break-word;} .muted{color:#597284;} .section-subtitle{font-size:13px;margin-bottom:16px;} .notice{display:flex;flex-direction:column;gap:4px;padding:13px 14px;border-radius:14px;margin:14px 0 0;} .notice.success{background:#dff6e8;} .notice.danger{background:#fee3e3;} .notice.warning{background:#fff1d6;} .notice.neutral{background:#e7f0fb;} .form-grid,.sites{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;} label{display:block;} label span,.section-label{display:block;font-size:13px;color:#4f6b7a;margin:0 0 8px;} input[type=text],input[type=password]{width:100%;padding:11px 12px;border:1px solid #c5d5df;border-radius:12px;box-sizing:border-box;background:#fff;} .search-box{width:min(420px,100%);} .search-box input{border-radius:10px;} .check{display:block;padding:12px;border:1px solid #d7e2ea;border-radius:12px;background:#f7fbfd;} .check small{display:block;color:#597284;margin-top:4px;} .catalog{list-style:none;margin:0;padding:0;display:grid;gap:12px;} .catalog li{display:grid;gap:4px;padding:14px;border:1px solid #d7e2ea;border-radius:12px;background:#f7fbfd;} .catalog code{display:inline-block;max-width:100%;box-sizing:border-box;overflow-wrap:anywhere;padding:4px 8px;border-radius:999px;background:#12202b;color:#fff;font-size:12px;} button{padding:11px 14px;border:0;border-radius:12px;background:#0f766e;color:#fff;font-weight:600;cursor:pointer;} .danger-button{background:#b42318;} .actions{display:flex;gap:10px;flex-wrap:wrap;margin-top:16px;} .item{padding:16px;border:1px solid #d7e2ea;border-radius:14px;background:#fbfdff;margin-bottom:14px;} .item p{line-height:1.6;} .item-head{display:flex;justify-content:space-between;gap:12px;align-items:flex-start;margin-bottom:10px;} .chip{display:inline-flex;align-items:center;max-width:100%;box-sizing:border-box;white-space:normal;overflow-wrap:anywhere;padding:7px 10px;border-radius:999px;font-size:12px;font-weight:700;background:#dce6ee;color:#244050;} .chip.success{background:#dff6e8;color:#155b3c;} .chip.warning{background:#fff1d6;color:#8a5800;} .chip.danger{background:#fee3e3;color:#9f1c1c;} .chip.neutral{background:#e7f0fb;color:#114a7c;} .audit-list{list-style:none;margin:0;padding:0;display:grid;gap:10px;} .audit-list li{display:flex;justify-content:space-between;gap:12px;padding:12px;border:1px solid #d7e2ea;border-radius:12px;background:#f7fbfd;} .audit-list span{color:#5a7382;font-size:13px;} code{font-family:'Consolas','Courier New',monospace;} @media (max-width: 820px){.hero,.toolbar,.section-head{display:block;} .chips{justify-content:flex-start;margin-top:12px;} .grid,.form-grid,.sites{grid-template-columns:1fr;} .audit-list li{display:block;} .search-box{margin-top:12px;}}</style></head><body>";
        }

        private static string BuildPageEnd()
        {
            return "<script>(function(){function sync(box,changed){var form=box.form;if(!form){return;}var disabled=box.checked;var sites=form.querySelectorAll('input[name=\"siteKeys\"]');for(var i=0;i<sites.length;i++){var site=sites[i];if(disabled){if(site.checked){site.setAttribute('data-was-checked','1');}site.checked=false;site.disabled=true;continue;}site.disabled=false;if(changed&&site.getAttribute('data-was-checked')==='1'){site.checked=true;}site.removeAttribute('data-was-checked');}}function bindSites(){var boxes=document.querySelectorAll('.js-all-sites');for(var i=0;i<boxes.length;i++){sync(boxes[i],false);boxes[i].addEventListener('change',function(){sync(this,true);});}}function resolveAction(form,submitter){if(submitter&&submitter.name==='action'){return submitter.value||'';}var hidden=form.querySelector('input[type=\"hidden\"][name=\"action\"]');return hidden?(hidden.value||''):'';}function bindSiteSelectionGuard(){var forms=document.querySelectorAll('form');for(var i=0;i<forms.length;i++){(function(form){var all=form.querySelector('.js-all-sites');if(!all){return;}form.addEventListener('submit',function(e){var action=resolveAction(form,e.submitter||document.activeElement);if(action!=='approve-request'&&action!=='update-authorization-sites'){return;}if(all.checked){return;}var sites=form.querySelectorAll('input[name=\"siteKeys\"]');for(var j=0;j<sites.length;j++){if(sites[j].checked){return;}}e.preventDefault();var warn=form.querySelector('.js-site-warning');if(!warn){warn=document.createElement('div');warn.className='notice danger js-site-warning';warn.textContent='请至少选择一个授权站点，或勾选“所有站点”。';form.insertBefore(warn,form.firstChild);}});})(forms[i]);}}function bindAuthorizationSearch(){var input=document.querySelector('.js-authorization-search');if(!input){return;}var items=document.querySelectorAll('.js-authorization-item');var empty=document.querySelector('.js-authorization-empty');function filter(){var query=(input.value||'').toLowerCase().trim();var shown=0;for(var i=0;i<items.length;i++){var item=items[i];var text=(item.getAttribute('data-search')||'').toLowerCase();var match=!query||text.indexOf(query)>=0;item.style.display=match?'':'none';if(match){shown++;}}if(empty){empty.style.display=shown===0?'':'none';}}input.addEventListener('input',filter);filter();}function bind(){bindSites();bindSiteSelectionGuard();bindAuthorizationSearch();}if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',bind);}else{bind();}}());</script></body></html>";
        }

        private static string E(string value)
        {
            return HttpUtility.HtmlEncode(value ?? string.Empty);
        }

        private static string FormatDateTime(DateTime value)
        {
            return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string FormatNullableDateTime(DateTime? value)
        {
            return value.HasValue ? FormatDateTime(value.Value) : "-";
        }

        private static string GetRequestStatusText(GatewayAccessRequestStatus status)
        {
            switch (status)
            {
                case GatewayAccessRequestStatus.Pending:
                    return "待审核";
                case GatewayAccessRequestStatus.Approved:
                    return "已批准";
                case GatewayAccessRequestStatus.Rejected:
                    return "已驳回";
                default:
                    return status.ToString();
            }
        }

        private static string GetCredentialChannelText(string credentialChannel)
        {
            if (string.Equals(credentialChannel, "browser-cookie", StringComparison.OrdinalIgnoreCase))
            {
                return "浏览器 Cookie";
            }

            if (string.Equals(credentialChannel, "app-hmac", StringComparison.OrdinalIgnoreCase))
            {
                return "APP HMAC";
            }

            if (string.Equals(credentialChannel, "legacy-app-login", StringComparison.OrdinalIgnoreCase)
                || string.Equals(credentialChannel, "legacy-app-session", StringComparison.OrdinalIgnoreCase))
            {
                return "移动 APP 现网会话";
            }

            return credentialChannel ?? string.Empty;
        }
    }
}
