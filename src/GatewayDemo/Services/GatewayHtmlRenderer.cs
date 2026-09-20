using System.Net;
using GatewayDemo.Models;
using GatewayDemo.Options;

namespace GatewayDemo.Services;

public sealed class GatewayHtmlRenderer(GatewayOptions gatewayOptions)
{
    public string RenderGatewayPage(
        DeviceContext device,
        string targetPath,
        GatewayDeviceAuthorization? authorization,
        GatewayAccessRequest? latestRequest,
        string errorMessage = "",
        string companyName = "",
        string applicantName = "",
        string phone = "",
        string reason = "",
        IReadOnlyCollection<string>? selectedSiteKeys = null)
    {
        var targetSiteKey = ExtractSiteKeyFromTarget(targetPath);
        var targetSite = gatewayOptions.FindSite(targetSiteKey) ?? gatewayOptions.Sites[0];
        var chosenSiteKeys = selectedSiteKeys?.Count > 0
            ? selectedSiteKeys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : latestRequest?.RequestedSites.Select(x => x.SiteKey).ToHashSet(StringComparer.OrdinalIgnoreCase)
              ?? new HashSet<string>([targetSite.Key], StringComparer.OrdinalIgnoreCase);

        return RenderLayout(
            "网关入口",
            "gateway-page",
            $$"""
            {{RenderPublicHeader()}}
            <main class="page-grid">
              <section class="hero-card">
                <div class="hero-copy">
                  <p class="eyebrow">Security Gateway</p>
                  <h2>{{E(gatewayOptions.AppName)}}</h2>
                  <div class="hero-tags">
                    {{RenderStatusChip(device.CredentialChannel == "app-hmac" ? "APP 签名凭据" : "浏览器设备凭据", "neutral")}}
                    {{RenderStatusChip(device.RequiresReview ? "需要重新审批" : "设备凭据有效", device.RequiresReview ? "danger" : "warning")}}
                  </div>
                </div>
                <div class="hero-metrics">
                  <div class="metric-card">
                    <span>当前设备号</span>
                    <strong>{{E(device.DeviceCode)}}</strong>
                    <button class="ghost-button" type="button" data-copy="{{E(device.DeviceCode)}}">复制</button>
                  </div>
                  <div class="metric-card">
                    <span>目标站点</span>
                    <strong>{{E(targetSite.Name)}}</strong>
                    <small>{{E(targetPath)}}</small>
                  </div>
                  <div class="metric-card">
                    <span>当前状态</span>
                    <strong>{{RenderRequestStatus(latestRequest)}}</strong>
                    <small>IP: {{E(device.ClientIp)}}</small>
                  </div>
                </div>
              </section>

              <section class="panel">
                <div class="panel-heading">
                  <div>
                    <p class="eyebrow">设备信息</p>
                    <h3>当前访问上下文</h3>
                  </div>
                </div>
                <div class="detail-list">
                  <div><span>设备号</span><strong>{{E(device.DeviceCode)}}</strong></div>
                  <div><span>设备标识</span><strong>{{E(device.DeviceId)}}</strong></div>
                  <div><span>凭据通道</span><strong>{{E(device.CredentialChannel)}}</strong></div>
                  <div><span>信号摘要</span><strong>{{E(device.SignalHash)}}</strong></div>
                  <div><span>访问 IP</span><strong>{{E(device.ClientIp)}}</strong></div>
                </div>
                {{RenderCredentialNotice(device)}}
                {{RenderAuthorizationNotice(authorization)}}
                {{RenderLatestRequestNotice(latestRequest)}}
              </section>

              <section class="panel span-two">
                <div class="panel-heading">
                  <div>
                    <p class="eyebrow">接入申请</p>
                    <h3>{{(authorization is null || device.RequiresReview ? "提交设备接入申请" : "申请追加站点授权")}}</h3>
                  </div>
                </div>
                {{RenderError(errorMessage, "提交失败")}}
                <form class="access-form" method="post" action="/gateway/request-access">
                  <input type="hidden" name="targetPath" value="{{E(targetPath)}}" />
                  <div class="form-grid">
                    <label>
                      <span>公司名称</span>
                      <input name="companyName" maxlength="100" value="{{E(Fallback(companyName, latestRequest?.CompanyName))}}" />
                    </label>
                    <label>
                      <span>申请人</span>
                      <input name="applicantName" maxlength="60" value="{{E(Fallback(applicantName, latestRequest?.ApplicantName))}}" />
                    </label>
                    <label>
                      <span>联系电话</span>
                      <input name="phone" maxlength="30" value="{{E(Fallback(phone, latestRequest?.Phone))}}" />
                    </label>
                    <label>
                      <span>申请原因</span>
                      <input name="reason" maxlength="200" value="{{E(Fallback(reason, latestRequest?.Reason))}}" />
                    </label>
                  </div>
                  <div>
                    <p class="form-caption">业务站点</p>
                    <div class="site-option-grid">
                      {{string.Join(string.Empty, gatewayOptions.Sites.Select(site => RenderSiteOption(site, chosenSiteKeys.Contains(site.Key))))}}
                    </div>
                  </div>
                  <div class="form-actions">
                    <button class="primary-button" type="submit">{{(authorization is null || device.RequiresReview ? "提交接入申请" : "提交追加授权")}}</button>
                  </div>
                </form>
              </section>

              <section class="panel">
                <div class="panel-heading">
                  <div>
                    <p class="eyebrow">站点目录</p>
                    <h3>统一认证覆盖范围</h3>
                  </div>
                </div>
                <div class="site-card-list">
                  {{string.Join(string.Empty, gatewayOptions.Sites.Select(RenderSiteCard))}}
                </div>
              </section>
            </main>
            """);
    }

    public string RenderAdminLoginPage(string errorMessage = "")
    {
        return RenderLayout(
            "管理后台登录",
            "admin-page",
            $$"""
            {{RenderAdminHeader(false)}}
            <main class="narrow-shell">
              <section class="panel login-panel">
                <div class="panel-heading">
                  <div>
                    <p class="eyebrow">Management Surface</p>
                    <h2>管理端登录</h2>
                  </div>
                </div>
                {{RenderError(errorMessage, "登录失败")}}
                <form class="stack-form" method="post" action="/admin/login">
                  <label>
                    <span>用户名</span>
                    <input type="text" name="username" maxlength="64" autocomplete="username" />
                  </label>
                  <label>
                    <span>密码</span>
                    <input type="password" name="password" maxlength="128" autocomplete="current-password" />
                  </label>
                  <button class="primary-button" type="submit">进入管理后台</button>
                </form>
              </section>
            </main>
            """);
    }

    public string RenderAdminPage(GatewayDashboardSnapshot snapshot)
    {
        var pendingRequests = snapshot.Requests.Where(x => x.Status == GatewayAccessRequestStatus.Pending).ToList();
        var activeAuthorizations = snapshot.Authorizations.Where(x => x.Status == GatewayAuthorizationStatus.Approved).ToList();

        return RenderLayout(
            "管理后台",
            "admin-page",
            $$"""
            {{RenderAdminHeader(true)}}
            <main class="page-grid">
              <section class="hero-card">
                <div class="hero-copy">
                  <p class="eyebrow">Management Surface</p>
                  <h2>网关管理台</h2>
                  <div class="hero-tags">
                    {{RenderStatusChip($"待审批 {pendingRequests.Count}", "warning")}}
                    {{RenderStatusChip($"已放行 {activeAuthorizations.Count}", "success")}}
                    {{RenderStatusChip($"站点 {gatewayOptions.Sites.Count}", "neutral")}}
                  </div>
                </div>
                <form method="post" action="/admin/logout" class="hero-actions">
                  <button class="ghost-button" type="submit">退出管理后台</button>
                </form>
              </section>

              <section class="panel span-two">
                <div class="panel-heading">
                  <div>
                    <p class="eyebrow">待处理</p>
                    <h3>设备接入申请</h3>
                  </div>
                </div>
                <div class="admin-card-list">
                  {{(pendingRequests.Count == 0 ? "<div class=\"empty-state\">当前没有待审批的设备申请。</div>" : string.Join(string.Empty, pendingRequests.Select(request => RenderPendingRequestCard(request, snapshot.Devices))))}}
                </div>
              </section>

              <section class="panel">
                <div class="panel-heading">
                  <div>
                    <p class="eyebrow">放行结果</p>
                    <h3>已授权设备</h3>
                  </div>
                </div>
                <div class="admin-card-list single-column">
                  {{(snapshot.Authorizations.Count == 0 ? "<div class=\"empty-state\">还没有设备通过审批。</div>" : string.Join(string.Empty, snapshot.Authorizations.Select(auth => RenderAuthorizationCard(auth, snapshot.Devices))))}}
                </div>
              </section>

              <section class="panel">
                <div class="panel-heading">
                  <div>
                    <p class="eyebrow">审计日志</p>
                    <h3>最近 120 条记录</h3>
                  </div>
                </div>
                <ul class="audit-list">
                  {{(snapshot.AuditLogs.Count == 0 ? "<li class=\"empty-state\">尚未产生审计日志。</li>" : string.Join(string.Empty, snapshot.AuditLogs.Take(16).Select(RenderAuditItem)))}}
                </ul>
              </section>
            </main>
            """);
    }

    public string RenderIssuedAppCredentialPage(GatewayDeviceAuthorization authorization, IssuedAppCredential credential)
    {
        return RenderLayout(
            "APP 设备密钥",
            "admin-page",
            $$"""
            {{RenderAdminHeader(true)}}
            <main class="narrow-shell">
              <section class="panel">
                <div class="panel-heading">
                  <div>
                    <p class="eyebrow">APP Device Credential</p>
                    <h2>已签发 APP 设备密钥</h2>
                  </div>
                </div>
                <div class="notice warning">
                  <strong>这是一次性展示页面</strong>
                </div>
                <div class="detail-list">
                  <div><span>设备号</span><strong>{{E(credential.DeviceCode)}}</strong></div>
                  <div><span>公司</span><strong>{{E(authorization.CompanyName)}}</strong></div>
                  <div><span>APP Key</span><strong><code id="app-key">{{E(credential.AppKeyId)}}</code></strong></div>
                  <div><span>APP Secret</span><strong><code id="app-secret">{{E(credential.AppSecret)}}</code></strong></div>
                </div>
                <div class="notice success">
                  <strong>签名头示例</strong>
                  <span>`{{E(DeviceCredentialService.AppKeyHeaderName)}}: {{E(credential.AppKeyId)}}`</span>
                  <span>`{{E(DeviceCredentialService.AppTimestampHeaderName)}}: {{credential.ExampleTimestamp}}`</span>
                  <span>`{{E(DeviceCredentialService.AppNonceHeaderName)}}: {{E(credential.ExampleNonce)}}`</span>
                  <span>`{{E(DeviceCredentialService.AppBodyHashHeaderName)}}: {{E(credential.ExampleBodyHash)}}`</span>
                  <span>`{{E(DeviceCredentialService.AppSignatureHeaderName)}}: {{E(credential.ExampleSignature)}}`</span>
                  <span>签名载荷：<code>{{E(credential.ExamplePayload)}}</code></span>
                </div>
                <div class="form-actions">
                  <a class="primary-link" href="/admin">返回管理后台</a>
                </div>
              </section>
            </main>
            """);
    }

    public string RenderDeviceCredentialErrorPage(string message)
    {
        return RenderLayout(
            "设备凭据错误",
            "gateway-page",
            $$"""
            {{RenderPublicHeader()}}
            <main class="narrow-shell">
              <section class="panel">
                <div class="panel-heading">
                  <div>
                    <p class="eyebrow">Credential Error</p>
                    <h2>设备凭据校验失败</h2>
                  </div>
                </div>
                <div class="notice danger">
                  <strong>请求被拒绝</strong>
                  <span>{{E(message)}}</span>
                </div>
              </section>
            </main>
            """);
    }

    public string RenderNotFoundPage()
    {
        return RenderLayout(
            "404",
            "gateway-page",
            $$"""
            {{RenderPublicHeader()}}
            <main class="narrow-shell">
              <section class="panel">
                <div class="panel-heading">
                  <div>
                    <p class="eyebrow">404</p>
                    <h2>页面不存在</h2>
                  </div>
                </div>
                <div class="form-actions">
                  <a class="primary-link" href="/gateway">回到网关入口</a>
                </div>
              </section>
            </main>
            """);
    }

    private string RenderLayout(string title, string bodyClass, string content) =>
        $$"""
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>{{E($"{gatewayOptions.AppName} - {title}")}}</title>
          <link rel="stylesheet" href="/styles.css" />
          <script defer src="/app.js"></script>
        </head>
        <body class="{{E(bodyClass)}}">
          <div class="page-shell">
            {{content}}
          </div>
        </body>
        </html>
        """;

    private string RenderPublicHeader() =>
        $$"""
        <header class="topbar">
          <div>
            <p class="eyebrow">Security Gateway Demo</p>
            <h1>{{E(gatewayOptions.AppName)}}</h1>
          </div>
          <nav class="topnav">
            <a href="/gateway" aria-current="page">网关入口</a>
            <a href="{{gatewayOptions.ProxyBasePath}}/{{gatewayOptions.Sites[0].Key}}/portal">代理入口</a>
          </nav>
        </header>
        """;

    private string RenderAdminHeader(bool authenticated) =>
        $$"""
        <header class="topbar">
          <div>
            <p class="eyebrow">Management Surface</p>
            <h1>{{E(gatewayOptions.AppName)}}</h1>
          </div>
          <nav class="topnav">
            <a href="/admin" {{(authenticated ? "aria-current=\"page\"" : string.Empty)}}>管理后台</a>
            <a href="/gateway">网关入口</a>
          </nav>
        </header>
        """;

    private string RenderSiteCard(GatewaySiteOptions site) =>
        $$"""
        <article class="site-card">
          <div class="site-card-top">
            <span class="site-dot" style="--dot:{{E(site.Accent)}}"></span>
            <h4>{{E(site.Name)}}</h4>
          </div>
          <p>{{E(site.Description)}}</p>
          <small>代理入口：<a href="/gateway?target={{Uri.EscapeDataString($"{gatewayOptions.ProxyBasePath}/{site.Key}/portal")}}">{{E($"{gatewayOptions.ProxyBasePath}/{site.Key}/portal")}}</a></small>
        </article>
        """;

    private string RenderCredentialNotice(DeviceContext device)
    {
        if (!device.RequiresReview)
        {
            return $$"""
            <div class="notice success">
              <strong>设备凭据状态正常</strong>
              {{(device.HasAppCredential ? $"<span>当前设备已签发 APP Key：<code>{E(device.AppKeyId)}</code></span>" : string.Empty)}}
            </div>
            """;
        }

        return $$"""
        <div class="notice danger">
          <strong>设备需要重新审批</strong>
          <span>{{E(device.ChallengeReason)}}</span>
        </div>
        """;
    }

    private string RenderAuthorizationNotice(GatewayDeviceAuthorization? authorization)
    {
        if (authorization is null)
        {
            return string.Empty;
        }

        return $$"""
        <div class="notice success">
          <strong>当前设备已通过审批</strong>
          <span>已放行站点：{{RenderSiteChips(authorization.AuthorizedSites.Select(x => x.SiteKey), "success")}}</span>
        </div>
        """;
    }

    private string RenderLatestRequestNotice(GatewayAccessRequest? request)
    {
        if (request is null)
        {
            return string.Empty;
        }

        var tone = request.Status switch
        {
            GatewayAccessRequestStatus.Pending => "warning",
            GatewayAccessRequestStatus.Approved => "success",
            GatewayAccessRequestStatus.Rejected => "danger",
            _ => "neutral",
        };

        var note = string.IsNullOrWhiteSpace(request.ReviewNote) ? string.Empty : $"<span>备注：{E(request.ReviewNote)}</span>";
        return $$"""
        <div class="notice {{tone}}">
          <strong>最新申请状态：{{E(request.Status.ToString())}}</strong>
          <span>申请时间：{{E(FormatDateTime(request.CreatedAtUtc))}}</span>
          <span>目标站点：{{RenderSiteChips(request.RequestedSites.Select(x => x.SiteKey), tone)}}</span>
          {{note}}
        </div>
        """;
    }

    private string RenderError(string errorMessage, string title)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return string.Empty;
        }

        return $$"""
        <div class="notice danger">
          <strong>{{E(title)}}</strong>
          <span>{{E(errorMessage)}}</span>
        </div>
        """;
    }

    private string RenderSiteOption(GatewaySiteOptions site, bool isChecked) =>
        $$"""
        <label class="site-option">
          <input type="checkbox" name="siteKeys" value="{{E(site.Key)}}" {{(isChecked ? "checked" : string.Empty)}} />
          <span>{{E(site.Name)}}</span>
          <small>{{E(site.Description)}}</small>
        </label>
        """;

    private string RenderPendingRequestCard(GatewayAccessRequest request, IReadOnlyDictionary<string, GatewayManagedDevice> devices)
    {
        devices.TryGetValue(request.DeviceId, out var device);
        var selectedSiteKeys = request.RequestedSites.Select(x => x.SiteKey).ToList();
        var trustChip = device is null
            ? RenderStatusChip("设备记录缺失", "danger")
            : RenderStatusChip(device.TrustState == GatewayDeviceTrustState.Challenged ? "设备待复核" : "设备记录有效", device.TrustState == GatewayDeviceTrustState.Challenged ? "danger" : "neutral");

        return $$"""
        <article class="admin-card">
          <div class="admin-card-top">
            <div>
              <p class="eyebrow">待审批申请</p>
              <h3>{{E(request.CompanyName)}}</h3>
            </div>
            {{RenderStatusChip("待审批", "warning")}}
          </div>
          <div class="detail-list compact">
            <div><span>设备号</span><strong>{{E(request.DeviceCode)}}</strong></div>
            <div><span>申请人</span><strong>{{E(request.ApplicantName)}}</strong></div>
            <div><span>电话</span><strong>{{E(request.Phone)}}</strong></div>
            <div><span>访问 IP</span><strong>{{E(request.ClientIp)}}</strong></div>
            <div><span>设备状态</span><strong>{{trustChip}}</strong></div>
            <div><span>目标站点</span><strong>{{RenderSiteChips(selectedSiteKeys, "warning")}}</strong></div>
          </div>
          {{(string.IsNullOrWhiteSpace(request.Reason) ? string.Empty : $"<p class=\"request-reason\">{E(request.Reason)}</p>")}}
          <form class="stack-form" method="post" action="/admin/requests/{{request.Id}}/approve">
            <div>
              <p class="form-caption">授权站点</p>
              <div class="site-option-grid compact">
                {{string.Join(string.Empty, gatewayOptions.Sites.Select(site => RenderSiteOption(site, selectedSiteKeys.Contains(site.Key, StringComparer.OrdinalIgnoreCase))))}}
              </div>
            </div>
            <label>
              <span>审批备注</span>
              <input name="note" maxlength="200" />
            </label>
            <div class="form-actions">
              <button class="primary-button" type="submit">批准并放行</button>
            </div>
          </form>
          <form method="post" action="/admin/requests/{{request.Id}}/reject">
            <button class="ghost-button danger" type="submit">拒绝申请</button>
          </form>
        </article>
        """;
    }

    private string RenderAuthorizationCard(GatewayDeviceAuthorization authorization, IReadOnlyDictionary<string, GatewayManagedDevice> devices)
    {
        devices.TryGetValue(authorization.DeviceId, out var device);
        var tone = authorization.Status == GatewayAuthorizationStatus.Approved ? "success" : "danger";
        var label = authorization.Status == GatewayAuthorizationStatus.Approved ? "已放行" : "已撤销";
        var revokeForm = authorization.Status == GatewayAuthorizationStatus.Approved
            ? $"<form method=\"post\" action=\"/admin/authorizations/{authorization.Id}/revoke\"><button class=\"ghost-button danger\" type=\"submit\">撤销设备授权</button></form>"
            : string.Empty;
        var issueAppCredentialForm = authorization.Status == GatewayAuthorizationStatus.Approved
            ? $"<form method=\"post\" action=\"/admin/authorizations/{authorization.Id}/issue-app-credential\"><button class=\"ghost-button\" type=\"submit\">签发 APP 密钥</button></form>"
            : string.Empty;
        var deviceState = device is null
            ? RenderStatusChip("设备记录缺失", "danger")
            : RenderStatusChip(device.TrustState == GatewayDeviceTrustState.Challenged ? "设备待复核" : "设备正常", device.TrustState == GatewayDeviceTrustState.Challenged ? "danger" : "neutral");
        var appState = device?.HasAppCredential == true
            ? $"<span>APP Key：<code>{E(device.AppKeyId)}</code></span>"
            : "<span>尚未签发 APP 密钥</span>";

        return $$"""
        <article class="admin-card">
          <div class="admin-card-top">
            <div>
              <p class="eyebrow">已放行设备</p>
              <h3>{{E(authorization.CompanyName)}}</h3>
            </div>
            {{RenderStatusChip(label, tone)}}
          </div>
          <div class="detail-list compact">
            <div><span>设备号</span><strong>{{E(authorization.DeviceCode)}}</strong></div>
            <div><span>申请人</span><strong>{{E(authorization.ApplicantName)}}</strong></div>
            <div><span>放行站点</span><strong>{{RenderSiteChips(authorization.AuthorizedSites.Select(x => x.SiteKey), "success")}}</strong></div>
            <div><span>设备状态</span><strong>{{deviceState}}</strong></div>
            <div><span>最近访问</span><strong>{{E(FormatDateTime(authorization.LastSeenAtUtc))}}</strong></div>
            <div><span>最近站点</span><strong>{{E(gatewayOptions.FindSite(authorization.LastSeenSiteKey)?.Name ?? "未记录")}}</strong></div>
          </div>
          <div class="notice neutral">
            <strong>APP 接入状态</strong>
            {{appState}}
          </div>
          <div class="form-actions">
            {{issueAppCredentialForm}}
            {{revokeForm}}
          </div>
        </article>
        """;
    }

    private string RenderAuditItem(GatewayAuditLog auditLog)
    {
        var siteName = gatewayOptions.FindSite(auditLog.SiteKey)?.Name ?? auditLog.SiteKey;
        var sitePart = string.IsNullOrWhiteSpace(siteName) ? string.Empty : $"<span>{E(siteName)}</span>";

        return $$"""
        <li class="audit-item">
          <div>
            <strong>{{E(auditLog.Message)}}</strong>
            <small>{{E(FormatDateTime(auditLog.CreatedAtUtc))}}</small>
          </div>
          <div class="audit-meta">
            <span>{{E(auditLog.DeviceCode)}}</span>
            {{sitePart}}
            <span>{{E(auditLog.Kind)}}</span>
          </div>
        </li>
        """;
    }

    private string RenderStatusChip(string label, string tone) =>
        $$"""<span class="status-chip {{E(tone)}}">{{E(label)}}</span>""";

    private string RenderRequestStatus(GatewayAccessRequest? request) =>
        request?.Status switch
        {
            GatewayAccessRequestStatus.Pending => RenderStatusChip("等待审批", "warning"),
            GatewayAccessRequestStatus.Approved => RenderStatusChip("最近申请已通过", "success"),
            GatewayAccessRequestStatus.Rejected => RenderStatusChip("最近申请被拒绝", "danger"),
            _ => RenderStatusChip("尚未申请", "neutral"),
        };

    private string RenderSiteChips(IEnumerable<string> siteKeys, string tone)
    {
        var keys = siteKeys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keys.Count == 0)
        {
            return "<span class=\"chip muted\">未选择站点</span>";
        }

        return string.Join(
            string.Empty,
            keys.Select(key =>
            {
                var siteName = gatewayOptions.FindSite(key)?.Name ?? key;
                return $$"""<span class="chip {{E(tone)}}">{{E(siteName)}}</span>""";
            }));
    }

    private string ExtractSiteKeyFromTarget(string targetPath)
    {
        if (!targetPath.StartsWith(gatewayOptions.ProxyBasePath, StringComparison.OrdinalIgnoreCase))
        {
            return gatewayOptions.Sites[0].Key;
        }

        var trimmed = targetPath[gatewayOptions.ProxyBasePath.Length..].Trim('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return gatewayOptions.Sites[0].Key;
        }

        var separator = trimmed.IndexOf('/');
        return separator >= 0 ? trimmed[..separator] : trimmed;
    }

    private static string FormatDateTime(DateTimeOffset? value) =>
        value.HasValue ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "未记录";

    private static string Fallback(string primary, string? fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback ?? string.Empty : primary;

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
