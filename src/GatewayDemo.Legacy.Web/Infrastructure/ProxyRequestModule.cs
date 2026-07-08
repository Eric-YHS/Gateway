using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using GatewayDemo.Legacy.Core.Models;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    public sealed class ProxyRequestModule : IHttpModule
    {
        private const string LastResolvedSiteCookieName = "gw_last_proxy_site";
        private const string ProxyCompatibilityRedirectedContextKey = "__Gateway.ProxyCompatibilityRedirected";
        internal const string ResolvedSiteKeyContextKey = "__Gateway.ResolvedSiteKey";
        internal const string ResolvedSiteContextKey = "__Gateway.ResolvedSite";

        public void Init(HttpApplication application)
        {
            application.BeginRequest += OnBeginRequest;
            application.PostAuthenticateRequest += OnPostAuthenticateRequest;
        }

        public void Dispose()
        {
        }

        private static bool TryHandleCorsPreflight(HttpContext context, GatewayOptions gatewayOptions)
        {
            if (!ApplyCorsHeaders(context, gatewayOptions))
            {
                return false;
            }

            if (context == null
                || context.Request == null
                || !string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(context.Request.Headers["Access-Control-Request-Method"]))
            {
                return false;
            }

            context.Response.ClearContent();
            context.Response.StatusCode = 204;
            context.Response.StatusDescription = "No Content";
            context.Response.TrySkipIisCustomErrors = true;
            return true;
        }

        private static bool ApplyCorsHeaders(HttpContext context, GatewayOptions gatewayOptions)
        {
            if (context == null
                || context.Request == null
                || context.Response == null
                || gatewayOptions == null
                || !gatewayOptions.CorsEnabled)
            {
                return false;
            }

            var origin = (context.Request.Headers["Origin"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(origin))
            {
                return false;
            }

            var allowedOrigin = ResolveCorsAllowedOrigin(gatewayOptions, origin);
            if (string.IsNullOrWhiteSpace(allowedOrigin))
            {
                return false;
            }

            context.Response.Headers["Access-Control-Allow-Origin"] = allowedOrigin;
            context.Response.Headers["Access-Control-Allow-Methods"] = string.Join(",", gatewayOptions.CorsAllowedMethods);
            context.Response.Headers["Access-Control-Allow-Headers"] = ResolveCorsAllowedHeaders(context, gatewayOptions);
            context.Response.Headers["Access-Control-Max-Age"] = gatewayOptions.CorsMaxAgeSeconds.ToString();
            if (gatewayOptions.CorsAllowCredentials && !string.Equals(allowedOrigin, "*", StringComparison.Ordinal))
            {
                context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            }

            var existingVary = context.Response.Headers["Vary"] ?? string.Empty;
            if (existingVary.IndexOf("Origin", StringComparison.OrdinalIgnoreCase) < 0)
            {
                context.Response.Headers["Vary"] = string.IsNullOrWhiteSpace(existingVary)
                    ? "Origin"
                    : existingVary + ", Origin";
            }

            return true;
        }

        private static string ResolveCorsAllowedOrigin(GatewayOptions gatewayOptions, string origin)
        {
            if (gatewayOptions.CorsAllowedOrigins == null || gatewayOptions.CorsAllowedOrigins.Count == 0)
            {
                return string.Empty;
            }

            foreach (var allowedOrigin in gatewayOptions.CorsAllowedOrigins)
            {
                var pattern = (allowedOrigin ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                if (string.Equals(pattern, "*", StringComparison.Ordinal))
                {
                    return gatewayOptions.CorsAllowCredentials ? origin : "*";
                }

                if (WildcardMatch(origin, pattern))
                {
                    return origin;
                }
            }

            return string.Empty;
        }

        private static string ResolveCorsAllowedHeaders(HttpContext context, GatewayOptions gatewayOptions)
        {
            var requestedHeaders = context == null || context.Request == null
                ? string.Empty
                : (context.Request.Headers["Access-Control-Request-Headers"] ?? string.Empty).Trim();

            if (gatewayOptions.CorsAllowedHeaders != null)
            {
                foreach (var allowedHeader in gatewayOptions.CorsAllowedHeaders)
                {
                    if (string.Equals((allowedHeader ?? string.Empty).Trim(), "*", StringComparison.Ordinal))
                    {
                        return string.IsNullOrWhiteSpace(requestedHeaders) ? "*" : requestedHeaders;
                    }
                }
            }

            return gatewayOptions.CorsAllowedHeaders == null || gatewayOptions.CorsAllowedHeaders.Count == 0
                ? requestedHeaders
                : string.Join(",", gatewayOptions.CorsAllowedHeaders);
        }

        private static void SetResolvedSiteContext(HttpContext context, GatewaySiteOptions site, string siteKey)
        {
            if (context == null)
            {
                return;
            }

            context.Items[ResolvedSiteKeyContextKey] = siteKey ?? string.Empty;
            context.Items[ResolvedSiteContextKey] = site;
            context.Items[GatewayPathUtility.SiteOwnsRequestRootContextKey] =
                SiteOwnsRequestRoot(LegacyGatewayRuntime.Current, context, site);
        }

        // A site may render/accept clean root paths only when the current request's
        // host resolves back to it (or it is the deployment's single root site).
        // Otherwise root paths on a shared host/port would be captured by another site.
        private static bool SiteOwnsRequestRoot(LegacyGatewayRuntime runtime, HttpContext context, GatewaySiteOptions site)
        {
            if (runtime == null
                || runtime.Configuration == null
                || runtime.Configuration.Gateway == null
                || context == null
                || site == null
                || !GatewayPathUtility.UsesGatewayRootPaths(site))
            {
                return false;
            }

            var gateway = runtime.Configuration.Gateway;
            var host = GatewayRequestContext.GetExternalHost(context, gateway);
            var hostSite = gateway.FindSiteByConfiguredHost(host);
            if (hostSite != null)
            {
                return string.Equals(hostSite.Key, site.Key, StringComparison.OrdinalIgnoreCase);
            }

            var rootSite = gateway.FindLegacyAppRootSite();
            return rootSite != null && string.Equals(rootSite.Key, site.Key, StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteHtmlPage(HttpContext context, int statusCode, string html)
        {
            context.Response.StatusCode = statusCode;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentEncoding = System.Text.Encoding.UTF8;
            context.Response.Write(html);
        }

        private static void OnBeginRequest(object sender, EventArgs args)
        {
            var application = (HttpApplication)sender;
            if (IsWebSocketUpgradeRequest(application.Context))
            {
                return;
            }

            HandleRequest(sender, args);
        }

        private static void OnPostAuthenticateRequest(object sender, EventArgs args)
        {
            var application = (HttpApplication)sender;
            if (!IsWebSocketUpgradeRequest(application.Context))
            {
                return;
            }

            HandleRequest(sender, args);
        }

        private static void HandleRequest(object sender, EventArgs args)
        {
            var application = (HttpApplication)sender;
            var context = application.Context;
            var runtime = LegacyGatewayRuntime.Current;
            var requestPath = context.Request.Path ?? string.Empty;
            if (TryHandleCorsPreflight(context, runtime.Configuration.Gateway))
            {
                context.ApplicationInstance.CompleteRequest();
                return;
            }

            ApplyCorsHeaders(context, runtime.Configuration.Gateway);

            var diagStart = DateTime.UtcNow;
            DiagnosticRecord diag = null;
            AppRequestDiagnostic.EnsureInitialized();
            if (AppRequestDiagnostic.Enabled)
            {
                diag = AppRequestDiagnostic.BeginRecord(context);
            }

            var repository = runtime.CreateRepository();
            var credentialService = runtime.CreateDeviceCredentialService();

            if (TryHandleGatewayFaviconRequest(context, requestPath))
            {
                context.ApplicationInstance.CompleteRequest();
                return;
            }

            if (runtime.Configuration.IsManagementRequest(context)
                && !IsManagementPortAllowedPath(requestPath))
            {
                WriteHtmlPage(context, 404, runtime.Renderer.RenderNotFoundPage("页面不存在。"));
                context.ApplicationInstance.CompleteRequest();
                return;
            }

            if (LegacyAppCompatibilityPolicy.IsMobileAppRootProbe(context, requestPath))
            {
                LegacyAppResponseWriter.WriteGatewayReady(context);
                context.ApplicationInstance.CompleteRequest();
                if (diag != null) { diag.MatchedSiteKey = "(mobile-root)"; diag.ElapsedMs = (long)(DateTime.UtcNow - diagStart).TotalMilliseconds; AppRequestDiagnostic.Record(diag); }
                return;
            }

            GatewaySiteOptions site;
            string siteKey;
            string forcedRelativePath;
            bool forceUpstreamOriginRoot;
            if (!TryResolveSite(runtime, context, requestPath, credentialService, out site, out siteKey, out forcedRelativePath, out forceUpstreamOriginRoot))
            {
                if (diag != null) { diag.MatchedSiteKey = "(no-site)"; diag.ElapsedMs = (long)(DateTime.UtcNow - diagStart).TotalMilliseconds; AppRequestDiagnostic.Record(diag); }
                return;
            }

            SetResolvedSiteContext(context, site, siteKey);
            if (!forceUpstreamOriginRoot && ShouldForceUpstreamOriginRootForPath(site, context, forcedRelativePath))
            {
                forceUpstreamOriginRoot = true;
            }

            if (!string.IsNullOrWhiteSpace(forcedRelativePath))
            {
                context.Items[ManagedReverseProxy.ForcedRelativePathContextKey] = forcedRelativePath;
            }

            if (forceUpstreamOriginRoot)
            {
                context.Items[ManagedReverseProxy.ForceUpstreamOriginRootContextKey] = true;
            }

            RememberResolvedSite(context, siteKey);

            if (LegacyAppCompatibilityPolicy.TryHandleCompatibilityRequest(context, site))
            {
                context.ApplicationInstance.CompleteRequest();
                return;
            }

            var externalApiClient = TryResolveExternalApiClient(context, runtime.Configuration.Gateway, site, siteKey, forcedRelativePath);
            if (externalApiClient != null)
            {
                context.Items[ManagedReverseProxy.SuppressUpstreamRedirectContextKey] = true;
                context.Items[ManagedReverseProxy.ExternalApiClientContextKey] = externalApiClient.ClientId;
                runtime.ReverseProxy.Proxy(context, site, null, credentialService);
                context.ApplicationInstance.CompleteRequest();
                return;
            }

            var isAnonymousAllowed = credentialService.ShouldAllowAnonymousLegacyBootstrapRequest(context)
                || IsAnonymousAllowedRequest(context, runtime, site, siteKey, forcedRelativePath);
            if (isAnonymousAllowed)
            {
                context.Items[ManagedReverseProxy.SuppressUpstreamRedirectContextKey] = true;
                runtime.ReverseProxy.Proxy(context, site, null, credentialService);
                context.ApplicationInstance.CompleteRequest();
                return;
            }

            DeviceContext device;
            try
            {
                device = credentialService.GetOrCreateContext(context);
            }
            catch (DeviceCredentialException ex)
            {
                var descriptor = credentialService.DescribeLegacyAppRequest(context);
                if (descriptor.IsLegacyApp)
                {
                    LegacyAppResponseWriter.WriteBlockedJson(context, ex.StatusCode, ex.Message, descriptor);
                }
                else
                {
                    WriteHtmlPage(context, ex.StatusCode, runtime.Renderer.RenderDeviceCredentialErrorPage(ex.Message));
                }

                context.ApplicationInstance.CompleteRequest();
                return;
            }

            if (diag != null)
            {
                diag.IsLegacyApp = credentialService.IsLegacyAppContext(device);
                diag.MatchedSiteKey = siteKey;
                diag.DeviceId = device.DeviceId;
                diag.DeviceCode = device.DeviceCode;
                diag.TrustState = device.TrustState.ToString();
                diag.DeviceIdFields = DescribeDeviceIdFields(context);
            }

            if (repository.ProcessExternalDecision(device.DeviceId))
            {
                runtime.InvalidateAuthorizationCache(device.DeviceId);

                var currentManagedDevice = repository.GetManagedDeviceByDeviceId(device.DeviceId);
                if (currentManagedDevice != null)
                {
                    device.TrustState = currentManagedDevice.TrustState;
                    device.ChallengeReason = currentManagedDevice.ChallengeReason;
                    device.HasAppCredential = currentManagedDevice.HasAppCredential;
                }
            }

            var authorization = runtime.GetCachedAuthorization(device.DeviceId,
                () => Task.Run(() => repository.GetActiveAuthorizationAsync(device.DeviceId, CancellationToken.None)).GetAwaiter().GetResult());

            if (diag != null)
            {
                diag.AuthorizationStatus = authorization != null
                    ? (authorization.AllowsSite(siteKey) ? "allowed" : "site-blocked")
                    : "no-auth";
                diag.IsBootstrap = isAnonymousAllowed;
            }

            if (device.RequiresReview || authorization == null || !authorization.AllowsSite(siteKey))
            {
                if (diag != null)
                {
                    diag.IsUnauthorized = true;
                    diag.BlockReason = device.RequiresReview ? "requires-review" : (authorization == null ? "no-authorization" : "site-blocked");
                    diag.ElapsedMs = (long)(DateTime.UtcNow - diagStart).TotalMilliseconds;
                    AppRequestDiagnostic.Record(diag);
                }

                var blockMessage = BuildDeviceBlockMessage(device, authorization, siteKey);
                Task.Run(() => repository.RecordAuditAsync(
                    device.RequiresReview ? "device-review-required" : "site-blocked",
                    blockMessage,
                    device,
                    siteKey,
                    device.ClientIp,
                    CancellationToken.None)).GetAwaiter().GetResult();

                var descriptor = credentialService.DescribeLegacyAppRequest(context);
                if (credentialService.IsLegacyAppContext(device) || descriptor.IsLegacyApp || LegacyAppCompatibilityPolicy.LooksLikeMobileAppRequest(context, site))
                {
                    AutoCreateLegacyAppRequest(runtime, repository, device, descriptor, site, siteKey, context.Request.RawUrl ?? requestPath);
                    LegacyAppResponseWriter.WriteBlockedJson(
                        context,
                        403,
                        BuildLegacyAppBlockMessage(device, authorization, siteKey),
                        descriptor);
                    context.ApplicationInstance.CompleteRequest();
                    return;
                }

                if (LooksLikeJsonApiClient(context))
                {
                    LegacyAppResponseWriter.WriteAuthorizationRequiredJson(context, 401, blockMessage, siteKey);
                    context.ApplicationInstance.CompleteRequest();
                    return;
                }

                // 未授权设备请求的若是页面自身引用的静态子资源(<script src="x.js">、<link
                // rel="stylesheet">、图片/字体等)，而不是浏览器地址栏发起的整页跳转，就不能再走
                // 下面这条 302 跳转到"提交接入申请"HTML页的逻辑——script/link 标签会自动跟随
                // 302，把这个HTML页当成 .js/.css 内容加载并尝试执行/解析，最终在控制台刷出一整
                // 屏"Uncaught SyntaxError: Unexpected token '<'"之类的报错，页面观感像是彻底崩溃。
                // 这里直接给这类子资源请求一个干净的 403(无跳转、不返回可执行/可解析的内容)，
                // 浏览器只会记一条资源加载失败，不会被当成语法错误的 JS/CSS 去解析。
                if (LooksLikeStaticSubResourceRequest(context, requestPath))
                {
                    WriteStaticSubResourceBlockedResponse(context, requestPath);
                    context.ApplicationInstance.CompleteRequest();
                    return;
                }

                var redirectUrl = GatewayTargetStore.BuildGatewayUrl(context.Request.RawUrl ?? requestPath);
                context.Response.Redirect(redirectUrl, false);
                context.ApplicationInstance.CompleteRequest();
                return;
            }

            repository.TouchAuthorizationAsync(device.DeviceId, device.ClientIp, siteKey, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (TryRedirectToUpstream(context, runtime, site))
            {
                if (diag != null)
                {
                    diag.IsProxied = true;
                    diag.MatchedSiteKey = siteKey;
                    diag.ElapsedMs = (long)(DateTime.UtcNow - diagStart).TotalMilliseconds;
                    AppRequestDiagnostic.Record(diag);
                }

                context.ApplicationInstance.CompleteRequest();
                return;
            }

            if (diag != null)
            {
                diag.IsProxied = true;
                diag.MatchedSiteKey = siteKey;
                diag.ElapsedMs = (long)(DateTime.UtcNow - diagStart).TotalMilliseconds;
                AppRequestDiagnostic.Record(diag);
            }
            runtime.ReverseProxy.Proxy(context, site, device, credentialService);
            context.ApplicationInstance.CompleteRequest();
        }

        private static bool TryResolveSite(
            LegacyGatewayRuntime runtime,
            HttpContext context,
            string requestPath,
            DeviceCredentialService credentialService,
            out GatewaySiteOptions site,
            out string siteKey,
            out string forcedRelativePath,
            out bool forceUpstreamOriginRoot)
        {
            site = null;
            siteKey = string.Empty;
            forcedRelativePath = string.Empty;
            forceUpstreamOriginRoot = false;

            if (requestPath.StartsWith(runtime.Configuration.Gateway.ProxyBasePath, StringComparison.OrdinalIgnoreCase))
            {
                siteKey = runtime.Configuration.ExtractSiteKeyFromRequestPath(requestPath);
                site = runtime.Configuration.Gateway.FindSite(siteKey);
                if (string.IsNullOrWhiteSpace(siteKey) || site == null)
                {
                    WriteHtmlPage(context, 404, runtime.Renderer.RenderNotFoundPage("请求的站点标识未在网关中注册。"));
                    context.ApplicationInstance.CompleteRequest();
                    return false;
                }

                if (string.Equals(requestPath.TrimEnd('/'), runtime.Configuration.Gateway.ProxyBasePath + "/" + siteKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(requestPath, runtime.Configuration.Gateway.ProxyBasePath + "/" + siteKey + "/", StringComparison.OrdinalIgnoreCase))
                {
                    context.Items[GatewayPathUtility.SiteOwnsRequestRootContextKey] = SiteOwnsRequestRoot(runtime, context, site);
                    context.Response.Redirect(runtime.Configuration.BuildSiteTargetPath(siteKey), false);
                    context.ApplicationInstance.CompleteRequest();
                    return false;
                }

                var proxyPrefix = runtime.Configuration.Gateway.ProxyBasePath + "/" + siteKey + "/";
                var relativePath = requestPath.StartsWith(proxyPrefix, StringComparison.OrdinalIgnoreCase)
                    ? requestPath.Substring(proxyPrefix.Length).TrimStart('/')
                    : string.Empty;
                if (UsesGatewayRootPaths(site))
                {
                    var hasExplicitRootMarker = HasLegacyRootPathMarker(relativePath);
                    // Only collapse /proxy/<key>/... to a clean root path when this
                    // site owns the root of the request's host; otherwise the clean
                    // path would resolve to another site sharing the same host/port.
                    if (SiteOwnsRequestRoot(runtime, context, site))
                    {
                        if (TryHandleProxyCompatibilityRequest(context, runtime, site, relativePath, out forcedRelativePath))
                        {
                            forceUpstreamOriginRoot = hasExplicitRootMarker || ShouldForceUpstreamOriginRoot(site);
                            return true;
                        }

                        if (context.Items[ProxyCompatibilityRedirectedContextKey] != null)
                        {
                            context.ApplicationInstance.CompleteRequest();
                            return false;
                        }
                    }

                    forcedRelativePath = StripLegacyRootPathMarker(relativePath);
                    forceUpstreamOriginRoot = hasExplicitRootMarker || ShouldForceUpstreamOriginRoot(site);
                    return true;
                }

                forcedRelativePath = relativePath;
                return true;
            }

            if (TryResolveSiteFromRequestPattern(runtime, context, requestPath, out site, out siteKey, out forcedRelativePath, out forceUpstreamOriginRoot))
            {
                return true;
            }

            if (TryResolveSiteFromReferer(runtime, context, requestPath, out site, out siteKey, out forcedRelativePath, out forceUpstreamOriginRoot))
            {
                return true;
            }

            if (TryResolveSiteFromEntryPathPrefix(runtime, context, requestPath, out site, out siteKey, out forcedRelativePath, out forceUpstreamOriginRoot))
            {
                return true;
            }

            if (TryResolveSiteFromHost(runtime, context, requestPath, out site, out siteKey, out forcedRelativePath, out forceUpstreamOriginRoot))
            {
                return true;
            }

            if (TryResolveSiteFromCookie(runtime, context, requestPath, out site, out siteKey, out forcedRelativePath, out forceUpstreamOriginRoot))
            {
                return true;
            }

            var legacyRootSite = runtime.Configuration.Gateway.FindLegacyAppRootSite();
            if (IsLegacyAppRootCandidatePath(requestPath, legacyRootSite))
            {
                if (legacyRootSite != null)
                {
                    site = legacyRootSite;
                    siteKey = site.Key;
                    forcedRelativePath = requestPath.Trim().TrimStart('/');
                    forceUpstreamOriginRoot = ShouldForceUpstreamOriginRoot(site);
                    return true;
                }
            }

            if (IsGatewayRootPath(requestPath))
            {
                var rootSite = runtime.Configuration.Gateway.FindLegacyAppRootSite();
                if (rootSite != null)
                {
                    site = rootSite;
                    siteKey = rootSite.Key;
                    forcedRelativePath = BuildHostMatchedRelativePath(rootSite, requestPath);
                    forceUpstreamOriginRoot = ShouldForceUpstreamOriginRoot(rootSite);
                    return true;
                }
            }

            // Root site fallback: when ExposeLegacyAppAtRoot is set, any non-gateway path
            // that hasn't been matched yet should go to the root site.
            // This ensures APP requests to unrecognized paths still get proxied.
            if (!IsGatewayOwnPath(requestPath))
            {
                var rootSite = runtime.Configuration.Gateway.FindLegacyAppRootSite();
                if (rootSite != null)
                {
                    site = rootSite;
                    siteKey = rootSite.Key;
                    forcedRelativePath = requestPath.Trim().TrimStart('/');
                    forceUpstreamOriginRoot = ShouldForceUpstreamOriginRoot(rootSite);
                    return true;
                }

                // No site claimed this request. Answer with the gateway's own 404
                // page instead of letting IIS report a missing physical file, which
                // reads as a deployment defect and hides the real cause.
                WriteHtmlPage(context, 404, runtime.Renderer.RenderNotFoundPage("请求的路径未匹配到任何已配置的站点，请检查 gateway-sites.json 中的 HostNames 与 EntryPath 配置。"));
                context.ApplicationInstance.CompleteRequest();
            }

            return false;
        }

        private static bool TryResolveSiteFromReferer(
            LegacyGatewayRuntime runtime,
            HttpContext context,
            string requestPath,
            out GatewaySiteOptions site,
            out string siteKey,
            out string forcedRelativePath,
            out bool forceUpstreamOriginRoot)
        {
            site = null;
            siteKey = string.Empty;
            forcedRelativePath = string.Empty;
            forceUpstreamOriginRoot = false;

            // 每个其它 TryResolveSiteFrom* 解析器都会在一开始排除网关自身保留路径
            // (/gateway、/admin、/proxy、/healthz.ashx)，唯独这里漏了这一条防线。
            // 缺了它会形成真实可复现的无限重定向：设备门禁把一个不带 /proxy/<site>/
            // 前缀的子资源请求(例如页面自身发起的 fetch/XHR 调用)302 到
            // /Gateway/Default.aspx?target=...；浏览器紧跟这次重定向时仍带着指向原站点
            // 的 Referer 头，若这里不排除保留路径，本方法会把这次对
            // /Gateway/Default.aspx 的请求"误判"成还属于原站点的相对资源请求，重新
            // 走一遍门禁判断并再次 302，且每一轮都会把上一轮的 target 再套一层编码——
            // 最终在浏览器里表现为 net::ERR_TOO_MANY_REDIRECTS。
            if (string.IsNullOrWhiteSpace(requestPath)
                || !requestPath.StartsWith("/", StringComparison.Ordinal)
                || IsGatewayReservedPath(requestPath))
            {
                return false;
            }

            var referer = context.Request.Headers["Referer"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(referer))
            {
                return false;
            }

            Uri refererUri;
            if (!Uri.TryCreate(referer, UriKind.Absolute, out refererUri))
            {
                return false;
            }

            var refererPath = refererUri.AbsolutePath ?? string.Empty;
            var proxyBasePath = runtime.Configuration.Gateway.ProxyBasePath;
            if (!refererPath.StartsWith(proxyBasePath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var refererSiteKey = runtime.Configuration.ExtractSiteKeyFromRequestPath(refererPath);
            if (string.IsNullOrWhiteSpace(refererSiteKey))
            {
                return false;
            }

            var refererSite = runtime.Configuration.Gateway.FindSite(refererSiteKey);
            if (refererSite == null)
            {
                return false;
            }

            var externalHost = GatewayRequestContext.GetExternalHost(context, runtime.Configuration.Gateway);
            var isSameOrigin = MatchesRequestHost(externalHost, refererUri)
                && IsSameExternalPort(context, refererUri);
            if (!isSameOrigin)
            {
                return false;
            }

            site = refererSite;
            siteKey = refererSiteKey;
            forcedRelativePath = BuildHostMatchedRelativePath(refererSite, requestPath);
            forceUpstreamOriginRoot = ShouldForceUpstreamOriginRoot(refererSite);
            return true;
        }

        private static bool TryResolveSiteFromRequestPattern(
            LegacyGatewayRuntime runtime,
            HttpContext context,
            string requestPath,
            out GatewaySiteOptions site,
            out string siteKey,
            out string forcedRelativePath,
            out bool forceUpstreamOriginRoot)
        {
            site = null;
            siteKey = string.Empty;
            forcedRelativePath = string.Empty;
            forceUpstreamOriginRoot = false;

            if (runtime == null
                || runtime.Configuration == null
                || runtime.Configuration.Gateway == null
                || context == null
                || context.Request == null
                || IsGatewayReservedPath(requestPath))
            {
                return false;
            }

            var matches = new List<GatewaySiteOptions>();
            foreach (var candidateSite in runtime.Configuration.Gateway.Sites)
            {
                if (SiteMatchesRequestPattern(candidateSite, context, requestPath))
                {
                    matches.Add(candidateSite);
                }
            }

            if (matches.Count != 1)
            {
                return false;
            }

            site = matches[0];
            siteKey = site.Key;
            forcedRelativePath = BuildHostMatchedRelativePath(site, requestPath);
            forceUpstreamOriginRoot = ShouldForceUpstreamOriginRoot(site);
            return true;
        }

        private static bool SiteMatchesRequestPattern(GatewaySiteOptions site, HttpContext context, string requestPath)
        {
            if (site == null || site.RequestMatchPatterns == null || site.RequestMatchPatterns.Count == 0)
            {
                return false;
            }

            var candidates = GatewayPathCandidateBuilder.BuildRequestMatchCandidates(context, requestPath);
            foreach (var pattern in site.RequestMatchPatterns)
            {
                var normalizedPattern = NormalizeRequestMatchPattern(pattern);
                if (string.IsNullOrWhiteSpace(normalizedPattern))
                {
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    if (WildcardMatch(candidate, normalizedPattern))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string NormalizeRequestMatchPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return string.Empty;
            }

            var trimmed = pattern.Trim();
            if (string.Equals(trimmed, "*", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
            {
                return trimmed;
            }

            Uri uri;
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out uri))
            {
                trimmed = uri.PathAndQuery;
            }

            return trimmed.StartsWith("/", StringComparison.Ordinal)
                ? trimmed
                : "/" + trimmed.TrimStart('/');
        }

        private static bool TryResolveSiteFromHost(
            LegacyGatewayRuntime runtime,
            HttpContext context,
            string requestPath,
            out GatewaySiteOptions site,
            out string siteKey,
            out string forcedRelativePath,
            out bool forceUpstreamOriginRoot)
        {
            site = null;
            siteKey = string.Empty;
            forcedRelativePath = string.Empty;
            forceUpstreamOriginRoot = false;

            if (runtime == null
                || context == null
                || context.Request == null
                || IsGatewayReservedPath(requestPath))
            {
                return false;
            }

            var host = GatewayRequestContext.GetExternalHost(context, runtime.Configuration.Gateway);

            var hostSite = runtime.Configuration.Gateway.FindSiteByConfiguredHost(host);
            if (hostSite == null)
            {
                return false;
            }

            site = hostSite;
            siteKey = hostSite.Key;
            forcedRelativePath = BuildHostMatchedRelativePath(hostSite, requestPath);
            forceUpstreamOriginRoot = ShouldForceUpstreamOriginRoot(hostSite);
            return true;
        }

        // Deep links such as /jvs-aps-ui/index.html belong to the site whose
        // EntryPath lives under that directory, regardless of host ambiguity or
        // stale site cookies. Only multi-segment entry paths participate, and only
        // when exactly one site claims the first path segment.
        private static bool TryResolveSiteFromEntryPathPrefix(
            LegacyGatewayRuntime runtime,
            HttpContext context,
            string requestPath,
            out GatewaySiteOptions site,
            out string siteKey,
            out string forcedRelativePath,
            out bool forceUpstreamOriginRoot)
        {
            site = null;
            siteKey = string.Empty;
            forcedRelativePath = string.Empty;
            forceUpstreamOriginRoot = false;

            if (runtime == null
                || runtime.Configuration == null
                || context == null
                || context.Request == null
                || IsGatewayReservedPath(requestPath))
            {
                return false;
            }

            var requestSegment = GetFirstPathSegment(requestPath);
            if (string.IsNullOrWhiteSpace(requestSegment))
            {
                return false;
            }

            GatewaySiteOptions matched = null;
            foreach (var candidateSite in runtime.Configuration.Gateway.Sites)
            {
                if (candidateSite == null)
                {
                    continue;
                }

                var entryPath = (candidateSite.EntryPath ?? string.Empty).Trim().Trim('/');
                if (entryPath.IndexOf('/') < 0)
                {
                    continue;
                }

                if (!string.Equals(GetFirstPathSegment(entryPath), requestSegment, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (matched != null)
                {
                    return false;
                }

                matched = candidateSite;
            }

            if (matched == null)
            {
                return false;
            }

            site = matched;
            siteKey = matched.Key;
            forcedRelativePath = BuildHostMatchedRelativePath(matched, requestPath);
            forceUpstreamOriginRoot = ShouldForceUpstreamOriginRoot(matched);
            return true;
        }

        private static string GetFirstPathSegment(string path)
        {
            var normalized = (path ?? string.Empty).Trim().TrimStart('/');
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            var slashIndex = normalized.IndexOf('/');
            return slashIndex < 0 ? normalized : normalized.Substring(0, slashIndex);
        }

        private static bool TryResolveSiteFromCookie(
            LegacyGatewayRuntime runtime,
            HttpContext context,
            string requestPath,
            out GatewaySiteOptions site,
            out string siteKey,
            out string forcedRelativePath,
            out bool forceUpstreamOriginRoot)
        {
            site = null;
            siteKey = string.Empty;
            forcedRelativePath = string.Empty;
            forceUpstreamOriginRoot = false;

            if (runtime == null
                || context == null
                || context.Request == null
                || IsGatewayReservedPath(requestPath))
            {
                return false;
            }

            var cookie = context.Request.Cookies[LastResolvedSiteCookieName];
            var cookieValue = cookie == null ? string.Empty : HttpUtility.UrlDecode(cookie.Value ?? string.Empty);
            if (string.IsNullOrWhiteSpace(cookieValue))
            {
                return false;
            }

            // Browser cookies ignore ports, so the sticky-site cookie records the
            // authority it was issued for; entries from another host:port must not
            // pull requests to the previously visited site.
            var cookieSiteKey = cookieValue.Trim();
            var separatorIndex = cookieSiteKey.IndexOf('|');
            if (separatorIndex >= 0)
            {
                var cookieAuthority = cookieSiteKey.Substring(0, separatorIndex).Trim();
                cookieSiteKey = cookieSiteKey.Substring(separatorIndex + 1).Trim();
                var currentAuthority = GatewayRequestContext.GetExternalHost(context, runtime.Configuration.Gateway);
                if (!string.Equals(cookieAuthority, currentAuthority, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(cookieSiteKey))
            {
                return false;
            }

            var cookieSite = runtime.Configuration.Gateway.FindSite(cookieSiteKey);
            if (cookieSite == null)
            {
                return false;
            }

            site = cookieSite;
            siteKey = cookieSite.Key;
            forcedRelativePath = BuildHostMatchedRelativePath(cookieSite, requestPath);
            forceUpstreamOriginRoot = ShouldForceUpstreamOriginRoot(cookieSite);
            return true;
        }

        private static bool TryRedirectToUpstream(HttpContext context, LegacyGatewayRuntime runtime, GatewaySiteOptions site)
        {
            if (context == null || context.Request == null || runtime == null || site == null || !site.RedirectToUpstream)
            {
                return false;
            }

            Uri baseUri;
            if (!Uri.TryCreate(site.UpstreamBaseUrl, UriKind.Absolute, out baseUri))
            {
                return false;
            }

            var relativePath = GatewayPathCandidateBuilder.ResolveRelativePath(
                context,
                runtime.Configuration.Gateway.ProxyBasePath,
                site,
                site.Key,
                string.Empty);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                relativePath = site.EntryPath ?? string.Empty;
            }

            var entryPath = (site.EntryPath ?? string.Empty).Trim().Trim('/');
            relativePath = (relativePath ?? string.Empty).TrimStart('/');
            relativePath = StripRootEntryPath(relativePath, entryPath);
            if (!string.IsNullOrWhiteSpace(entryPath)
                && string.Equals(relativePath.Trim('/'), entryPath, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = string.Empty;
            }

            Uri targetUri;
            if (IsForceUpstreamOriginRoot(context))
            {
                var originRoot = new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/");
                targetUri = new Uri(originRoot, relativePath);
            }
            else
            {
                targetUri = new Uri(baseUri, relativePath);
            }

            var builder = new UriBuilder(targetUri);
            if (context.Request.Url != null)
            {
                builder.Query = context.Request.Url.Query.TrimStart('?');
            }

            if (IsGatewayServedOrigin(context, runtime, builder.Uri))
            {
                return false;
            }

            context.Response.Clear();
            context.Response.StatusCode = 302;
            context.Response.StatusDescription = "Found";
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.RedirectLocation = builder.Uri.ToString();
            context.Response.Write("Redirecting to upstream site.");
            return true;
        }

        private static bool IsGatewayServedOrigin(HttpContext context, LegacyGatewayRuntime runtime, Uri targetUri)
        {
            if (context == null || context.Request == null || context.Request.Url == null || targetUri == null)
            {
                return false;
            }

            if (runtime == null || runtime.Configuration == null)
            {
                return false;
            }

            var requestScheme = GatewayRequestContext.GetExternalScheme(context, runtime.Configuration.Gateway);
            var requestHost = GatewayRequestContext.GetExternalHost(context, runtime.Configuration.Gateway);
            if (string.Equals(requestScheme, targetUri.Scheme, StringComparison.OrdinalIgnoreCase)
                && MatchesRequestHost(requestHost, targetUri)
                && IsSameExternalPort(context, targetUri))
            {
                return true;
            }

            if (MatchesRequestHost(requestHost, targetUri)
                && ((!AuthorityHasExplicitPort(requestHost) && targetUri.IsDefaultPort)
                    || IsSameExternalPort(context, targetUri)))
            {
                return true;
            }

            var targetAuthority = targetUri.IsDefaultPort
                ? targetUri.Host
                : targetUri.Authority;
            return runtime.Configuration.Gateway.FindSiteByConfiguredHost(targetAuthority) != null;
        }

        private static bool MatchesRequestHost(string requestHost, Uri targetUri)
        {
            if (string.IsNullOrWhiteSpace(requestHost) || targetUri == null)
            {
                return false;
            }

            Uri requestUri;
            if (Uri.TryCreate("http://" + requestHost, UriKind.Absolute, out requestUri))
            {
                return string.Equals(requestUri.Host, targetUri.Host, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(requestHost, targetUri.Host, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameExternalPort(HttpContext context, Uri targetUri)
        {
            if (context == null || context.Request == null || context.Request.Url == null || targetUri == null)
            {
                return false;
            }

            var runtime = LegacyGatewayRuntime.Current;
            var hostHeader = runtime == null || runtime.Configuration == null
                ? (context.Request.Headers["Host"] ?? string.Empty).Trim()
                : GatewayRequestContext.GetExternalHost(context, runtime.Configuration.Gateway);
            Uri hostUri;
            if (AuthorityHasExplicitPort(hostHeader)
                && Uri.TryCreate("http://" + hostHeader, UriKind.Absolute, out hostUri)
                && hostUri.Port > 0)
            {
                return hostUri.Port == targetUri.Port;
            }

            return context.Request.Url.Port == targetUri.Port;
        }

        private static bool AuthorityHasExplicitPort(string authority)
        {
            if (string.IsNullOrWhiteSpace(authority))
            {
                return false;
            }

            var trimmed = authority.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                var endBracket = trimmed.IndexOf(']');
                return endBracket > 0
                    && endBracket < trimmed.Length - 1
                    && trimmed[endBracket + 1] == ':';
            }

            var colonIndex = trimmed.LastIndexOf(':');
            return colonIndex > 0
                && colonIndex < trimmed.Length - 1
                && trimmed.IndexOf(':') == colonIndex;
        }

        private static bool IsForceUpstreamOriginRoot(HttpContext context)
        {
            if (context == null)
            {
                return false;
            }

            var value = context.Items[ManagedReverseProxy.ForceUpstreamOriginRootContextKey];
            return value is bool && (bool)value;
        }

        private static bool TryHandleProxyCompatibilityRequest(
            HttpContext context,
            LegacyGatewayRuntime runtime,
            GatewaySiteOptions site,
            string relativePath,
            out string forcedRelativePath)
        {
            forcedRelativePath = string.Empty;
            if (context == null
                || runtime == null
                || site == null
                || !UsesGatewayRootPaths(site)
                || string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            var cleanRelativePath = StripLegacyRootPathMarker(relativePath);
            if (ShouldRedirectProxyCompatibilityRequest(context, site))
            {
                var cleanPath = BuildGatewayRootPath(cleanRelativePath);
                var query = context.Request.Url == null ? string.Empty : context.Request.Url.Query;
                context.Response.Clear();
                context.Response.StatusCode = 302;
                context.Response.StatusDescription = "Found";
                context.Response.TrySkipIisCustomErrors = true;
                context.Response.RedirectLocation = cleanPath + query;
                context.Response.Write("Redirecting to clean gateway path.");
                context.Items[ProxyCompatibilityRedirectedContextKey] = true;
                return false;
            }

            forcedRelativePath = cleanRelativePath.TrimStart('/');
            return true;
        }

        private static bool ShouldRedirectProxyCompatibilityRequest(HttpContext context, GatewaySiteOptions site)
        {
            if (context == null || context.Request == null)
            {
                return false;
            }

            if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (LegacyAppCompatibilityPolicy.LooksLikeMobileAppRequest(context, site))
            {
                return false;
            }

            var requestedWith = context.Request.Headers["X-Requested-With"] ?? string.Empty;
            if (string.Equals(requestedWith.Trim(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var accept = (context.Request.Headers["Accept"] ?? string.Empty).ToLowerInvariant();
            if (accept.Contains("application/json")
                || accept.Contains("text/json")
                || accept.Contains("application/xml")
                || accept.Contains("text/xml"))
            {
                return false;
            }

            var fetchDest = (context.Request.Headers["Sec-Fetch-Dest"] ?? string.Empty).ToLowerInvariant();
            return string.Equals(fetchDest, "document", StringComparison.OrdinalIgnoreCase)
                || accept.Contains("text/html");
        }

        // Browsers navigating a full page never send an Accept header listing
        // application/json, so this only matches genuine API/AJAX-style callers
        // (curl/fetch/SDKs) and leaves browser navigation redirects untouched.
        private static bool LooksLikeJsonApiClient(HttpContext context)
        {
            if (context == null || context.Request == null)
            {
                return false;
            }

            var requestedWith = context.Request.Headers["X-Requested-With"] ?? string.Empty;
            if (string.Equals(requestedWith.Trim(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var contentType = context.Request.ContentType ?? string.Empty;
            if (contentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var accept = (context.Request.Headers["Accept"] ?? string.Empty).ToLowerInvariant();
            return accept.Contains("application/json") || accept.Contains("text/json");
        }

        // 支持现代浏览器的 Fetch Metadata(Sec-Fetch-Dest)优先判断："document"/"iframe"/"frame"
        // 才是地址栏跳转或框架整页加载，其余(script/style/image/font/...)都是页面自己发起的子
        // 资源加载。拿不到该请求头的老旧客户端/内嵌 WebView，退化成按请求路径的静态资源扩展名
        // 判断，覆盖常见的 JS/CSS/图片/字体/多媒体文件。
        private static readonly string[] StaticSubResourceFetchDests =
        {
            "script", "style", "image", "font", "media", "worker", "sharedworker", "embed", "object", "track"
        };

        private static readonly string[] StaticSubResourceExtensions =
        {
            ".js", ".mjs", ".css", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico",
            ".woff", ".woff2", ".ttf", ".eot", ".otf", ".mp4", ".mp3", ".wasm"
        };

        private static bool LooksLikeStaticSubResourceRequest(HttpContext context, string requestPath)
        {
            if (context == null || context.Request == null)
            {
                return false;
            }

            var fetchDest = (context.Request.Headers["Sec-Fetch-Dest"] ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(fetchDest))
            {
                if (string.Equals(fetchDest, "document", StringComparison.Ordinal)
                    || string.Equals(fetchDest, "iframe", StringComparison.Ordinal)
                    || string.Equals(fetchDest, "frame", StringComparison.Ordinal))
                {
                    return false;
                }

                for (var i = 0; i < StaticSubResourceFetchDests.Length; i++)
                {
                    if (string.Equals(fetchDest, StaticSubResourceFetchDests[i], StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            var path = (requestPath ?? string.Empty).ToLowerInvariant();
            for (var i = 0; i < StaticSubResourceExtensions.Length; i++)
            {
                if (path.EndsWith(StaticSubResourceExtensions[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void WriteStaticSubResourceBlockedResponse(HttpContext context, string requestPath)
        {
            context.Response.Clear();
            context.Response.StatusCode = 403;
            context.Response.StatusDescription = "Forbidden";
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers["X-Gateway-Device-Blocked"] = "1";
            context.Response.Write("Forbidden: device is not yet authorized for this site's resources.");
        }

        private static string BuildGatewayRootPath(string relativePath)
        {
            return GatewayPathUtility.BuildGatewayRootPath(relativePath);
        }

        private static bool ShouldForceUpstreamOriginRoot(GatewaySiteOptions site)
        {
            if (!UsesGatewayRootPaths(site) || site == null || string.IsNullOrWhiteSpace(site.UpstreamBaseUrl))
            {
                return false;
            }

            Uri upstreamBase;
            if (!Uri.TryCreate(site.UpstreamBaseUrl, UriKind.Absolute, out upstreamBase))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(upstreamBase.AbsolutePath)
                || string.Equals(upstreamBase.AbsolutePath, "/", StringComparison.Ordinal);
        }

        private static bool ShouldForceUpstreamOriginRootForPath(GatewaySiteOptions site, HttpContext context, string forcedRelativePath)
        {
            if (site == null || context == null || context.Request == null)
            {
                return false;
            }

            var runtime = LegacyGatewayRuntime.Current;
            var proxyBasePath = runtime == null || runtime.Configuration == null || runtime.Configuration.Gateway == null
                ? "/proxy"
                : runtime.Configuration.Gateway.ProxyBasePath;
            var candidates = GatewayPathCandidateBuilder.BuildRuleCandidates(
                context,
                proxyBasePath,
                site,
                site.Key,
                forcedRelativePath);
            if (site.UpstreamOriginRootPatterns != null)
            {
                foreach (var pattern in site.UpstreamOriginRootPatterns)
                {
                    if (MatchesAnyAllowedPath(candidates, pattern))
                    {
                        return true;
                    }
                }
            }

            if (site.PathRules != null)
            {
                foreach (var rule in site.PathRules)
                {
                    if (rule == null || !rule.ResolveFromUpstreamOriginRoot || rule.Patterns == null)
                    {
                        continue;
                    }

                    foreach (var pattern in rule.Patterns)
                    {
                        if (MatchesAnyAllowedPath(candidates, pattern))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static ExternalApiClientOptions TryResolveExternalApiClient(
            HttpContext context,
            GatewayOptions gatewayOptions,
            GatewaySiteOptions site,
            string siteKey,
            string forcedRelativePath)
        {
            if (context == null
                || context.Request == null
                || site == null
                || site.ExternalApiClients == null
                || site.ExternalApiClients.Count == 0)
            {
                return null;
            }

            var clientIp = ClientIpResolver.GetClientIp(context, gatewayOptions);
            var candidates = GatewayPathCandidateBuilder.BuildRuleCandidates(
                context,
                gatewayOptions == null ? "/proxy" : gatewayOptions.ProxyBasePath,
                site,
                siteKey,
                forcedRelativePath);
            foreach (var client in site.ExternalApiClients)
            {
                if (client == null
                    || !client.Enabled
                    || !ExternalClientAllowsSite(client, siteKey)
                    || !ExternalClientAllowsIp(client, clientIp)
                    || !ExternalClientAllowsPath(client, site.ExternalApiDefaultPathPatterns, candidates))
                {
                    continue;
                }

                return client;
            }

            return null;
        }

        private static bool ExternalClientAllowsSite(ExternalApiClientOptions client, string siteKey)
        {
            if (client.AllowedSiteKeys == null || client.AllowedSiteKeys.Count == 0)
            {
                return true;
            }

            foreach (var allowedSiteKey in client.AllowedSiteKeys)
            {
                if (string.Equals(allowedSiteKey, "*", StringComparison.Ordinal)
                    || string.Equals(allowedSiteKey, siteKey ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ExternalClientAllowsPath(ExternalApiClientOptions client, IList<string> defaultPathPatterns, IList<string> candidates)
        {
            if (client.AllowedPathPatterns != null && client.AllowedPathPatterns.Count > 0)
            {
                foreach (var pattern in client.AllowedPathPatterns)
                {
                    if (MatchesAnyAllowedPath(candidates, pattern))
                    {
                        return true;
                    }
                }

                return false;
            }

            foreach (var pattern in defaultPathPatterns ?? new List<string>())
            {
                if (MatchesAnyAllowedPath(candidates, pattern))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ExternalClientAllowsIp(ExternalApiClientOptions client, string clientIp)
        {
            if (client.AllowedIpRanges == null || client.AllowedIpRanges.Count == 0)
            {
                return false;
            }

            foreach (var allowedRange in client.AllowedIpRanges)
            {
                if (IpMatchesRange(clientIp, allowedRange))
                {
                    return true;
                }
            }

            return false;
        }

        private static string StripQuery(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            var queryIndex = normalized.IndexOf('?');
            return queryIndex >= 0 ? normalized.Substring(0, queryIndex) : normalized;
        }

        private static bool IpMatchesRange(string clientIp, string allowedRange)
        {
            var normalizedRange = (allowedRange ?? string.Empty).Trim();
            if (string.Equals(normalizedRange, "*", StringComparison.Ordinal))
            {
                return true;
            }

            IPAddress remoteIp;
            if (string.IsNullOrWhiteSpace(clientIp)
                || !IPAddress.TryParse(NormalizeAddress(clientIp), out remoteIp))
            {
                return false;
            }

            var cidrSeparator = normalizedRange.IndexOf('/');
            if (cidrSeparator > 0)
            {
                return IsInCidr(remoteIp, normalizedRange);
            }

            IPAddress allowedIp;
            return IPAddress.TryParse(NormalizeAddress(normalizedRange), out allowedIp)
                && remoteIp.Equals(allowedIp);
        }

        private static bool IsInCidr(IPAddress remoteIp, string cidr)
        {
            var parts = (cidr ?? string.Empty).Trim().Split('/');
            if (parts.Length != 2)
            {
                return false;
            }

            IPAddress networkIp;
            int prefixLength;
            if (!IPAddress.TryParse(NormalizeAddress(parts[0]), out networkIp)
                || !int.TryParse(parts[1], out prefixLength))
            {
                return false;
            }

            var remoteBytes = remoteIp.GetAddressBytes();
            var networkBytes = networkIp.GetAddressBytes();
            if (remoteBytes.Length != networkBytes.Length || prefixLength < 0 || prefixLength > remoteBytes.Length * 8)
            {
                return false;
            }

            var fullBytes = prefixLength / 8;
            var remainingBits = prefixLength % 8;
            for (var i = 0; i < fullBytes; i++)
            {
                if (remoteBytes[i] != networkBytes[i])
                {
                    return false;
                }
            }

            if (remainingBits == 0)
            {
                return true;
            }

            var mask = (byte)(0xFF << (8 - remainingBits));
            return (remoteBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
        }

        private static string NormalizeAddress(string value)
        {
            var address = (value ?? string.Empty).Trim();
            if (address.StartsWith("[", StringComparison.Ordinal))
            {
                var endBracket = address.IndexOf(']');
                if (endBracket > 0)
                {
                    return address.Substring(1, endBracket - 1);
                }
            }

            var colonIndex = address.IndexOf(':');
            if (colonIndex > 0 && colonIndex == address.LastIndexOf(':'))
            {
                int port;
                if (int.TryParse(address.Substring(colonIndex + 1), out port))
                {
                    return address.Substring(0, colonIndex);
                }
            }

            return address;
        }

        private static bool TryHandleGatewayFaviconRequest(HttpContext context, string requestPath)
        {
            if (context == null
                || context.Response == null
                || !string.Equals(requestPath, "/favicon.ico", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            context.Response.Clear();
            context.Response.StatusCode = 204;
            context.Response.TrySkipIisCustomErrors = true;
            return true;
        }

        private static string BuildHostMatchedRelativePath(GatewaySiteOptions site, string requestPath)
        {
            var normalizedPath = (requestPath ?? string.Empty).Trim().TrimStart('/');
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return string.IsNullOrWhiteSpace(site.EntryPath)
                    ? string.Empty
                    : site.EntryPath.TrimStart('/');
            }

            return normalizedPath;
        }

        private static void RememberResolvedSite(HttpContext context, string siteKey)
        {
            if (context == null || context.Response == null || string.IsNullOrWhiteSpace(siteKey))
            {
                return;
            }

            var runtime = LegacyGatewayRuntime.Current;
            var authority = runtime == null || runtime.Configuration == null
                ? string.Empty
                : GatewayRequestContext.GetExternalHost(context, runtime.Configuration.Gateway);
            var cookie = new HttpCookie(
                LastResolvedSiteCookieName,
                HttpUtility.UrlEncode((authority ?? string.Empty).Trim() + "|" + siteKey.Trim()))
            {
                HttpOnly = true,
                Path = "/"
            };

            var cookieDomain = runtime == null || runtime.Configuration == null
                ? string.Empty
                : GatewayRequestContext.ResolveDeviceCookieDomain(context, runtime.Configuration.Gateway);
            if (!string.IsNullOrWhiteSpace(cookieDomain))
            {
                cookie.Domain = cookieDomain;
            }

            if (context.Request != null
                && runtime != null
                && runtime.Configuration != null
                && GatewayRequestContext.ShouldUseSecureDeviceCookie(context, runtime.Configuration.Gateway))
            {
                cookie.Secure = true;
            }

            context.Response.Cookies.Set(cookie);
        }

        private static bool IsAnonymousAllowedRequest(
            HttpContext context,
            LegacyGatewayRuntime runtime,
            GatewaySiteOptions site,
            string siteKey,
            string forcedRelativePath)
        {
            if (context == null
                || context.Request == null
                || runtime == null
                || site == null
                || site.AnonymousAllowedPaths == null
                || site.AnonymousAllowedPaths.Count == 0)
            {
                return false;
            }

            var candidates = GatewayPathCandidateBuilder.BuildRuleCandidates(
                context,
                runtime.Configuration.Gateway.ProxyBasePath,
                site,
                siteKey,
                forcedRelativePath);
            foreach (var pattern in site.AnonymousAllowedPaths)
            {
                if (MatchesAnyAllowedPath(candidates, pattern))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddPathCandidate(IList<string> candidates, string path, string query)
        {
            GatewayPathUtility.AddPathCandidate(candidates, path, query);
        }

        private static void AddDistinct(IList<string> values, string value)
        {
            GatewayPathUtility.AddDistinct(values, value);
        }

        private static string StripLegacyRootPathMarker(string relativePath)
        {
            return GatewayPathUtility.StripLegacyRootPathMarker(relativePath);
        }

        private static bool HasLegacyRootPathMarker(string relativePath)
        {
            return GatewayPathUtility.HasLegacyRootPathMarker(relativePath);
        }

        private static string StripEntryPath(string relativePath, string entryPath)
        {
            return GatewayPathUtility.StripEntryPath(relativePath, entryPath);
        }

        private static string StripRootEntryPath(string relativePath, string entryPath)
        {
            return StripLegacyRootPathMarker(relativePath);
        }

        private static bool MatchesAnyAllowedPath(IList<string> candidates, string pattern)
        {
            return GatewayPathUtility.MatchesAnyAllowedPath(candidates, pattern);
        }

        private static string NormalizeAllowedPathPattern(string pattern)
        {
            return GatewayPathUtility.NormalizeAllowedPathPattern(pattern);
        }

        private static bool WildcardMatch(string value, string pattern)
        {
            return GatewayPathUtility.WildcardMatch(value, pattern);
        }

        private static bool IsLegacyAppRootCandidatePath(string requestPath, GatewaySiteOptions site)
        {
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                return false;
            }

            if (string.Equals(requestPath, "/", StringComparison.Ordinal)
                || string.Equals(requestPath, string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            if (IsGatewayOwnPath(requestPath))
            {
                return false;
            }

            return LegacyAppCompatibilityPolicy.IsRootCandidatePath(requestPath, site);
        }

        private static bool IsGatewayOwnPath(string requestPath)
        {
            if (IsGatewayRootPath(requestPath))
            {
                return true;
            }

            if (requestPath.StartsWith("/gateway", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (requestPath.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(requestPath, "/healthz.ashx", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool IsGatewayRootPath(string requestPath)
        {
            return string.IsNullOrWhiteSpace(requestPath)
                || string.Equals(requestPath, "/", StringComparison.Ordinal);
        }

        private static bool IsManagementPortAllowedPath(string requestPath)
        {
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                return false;
            }

            return requestPath.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(requestPath, "/healthz.ashx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(requestPath, "/favicon.ico", StringComparison.OrdinalIgnoreCase);
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

        private static bool IsGatewayReservedPath(string requestPath)
        {
            var path = requestPath ?? string.Empty;
            return path.StartsWith("/gateway", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/proxy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "/healthz.ashx", StringComparison.OrdinalIgnoreCase);
        }

        private static bool UsesGatewayRootPaths(GatewaySiteOptions site)
        {
            return GatewayPathUtility.UsesGatewayRootPaths(site);
        }

        private static void AutoCreateLegacyAppRequest(
            LegacyGatewayRuntime runtime,
            LegacyGatewayRepository repository,
            DeviceContext device,
            LegacyAppRequestDescriptor descriptor,
            GatewaySiteOptions site,
            string siteKey,
            string targetPath)
        {
            var draft = LegacyAppAccessRequestFactory.CreateAutoRequest(
                runtime,
                descriptor,
                site,
                siteKey,
                targetPath);

            repository.CreateOrUpdateRequestAsync(
                    device,
                    draft.CompanyName,
                    draft.ApplicantName,
                    draft.ContactHint,
                    draft.Reason,
                    draft.TargetPath,
                    draft.SiteKeys,
                    draft.LegacyUrlBefore,
                    draft.LegacyCompanyId,
                    draft.LegacyUserId,
                    draft.LegacyDataCenterId,
                    draft.LegacyDeviceImei,
                    draft.LegacyMobileUserNum,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        private static string BuildDeviceBlockMessage(DeviceContext device, GatewayDeviceAuthorization authorization, string siteKey)
        {
            if (device != null && device.RequiresReview)
            {
                if (!string.IsNullOrWhiteSpace(device.ChallengeReason))
                {
                    return device.ChallengeReason;
                }

                return "设备尚未获批，已拦截站点访问。";
            }

            if (authorization == null)
            {
                return "设备尚未获批，已拦截站点访问。";
            }

            if (!authorization.AllowsSite(siteKey))
            {
                return "设备尚未获批当前站点，已拦截站点访问。";
            }

            return "设备尚未获批，已拦截站点访问。";
        }

        private static string BuildLegacyAppBlockMessage(DeviceContext device, GatewayDeviceAuthorization authorization, string siteKey)
        {
            if (device != null && device.RequiresReview)
            {
                if (!string.IsNullOrWhiteSpace(device.ChallengeReason))
                {
                    return device.ChallengeReason;
                }

                return "移动 APP 设备尚未获批，请先在网关后台完成审批。";
            }

            if (authorization == null)
            {
                return "移动 APP 设备尚未获批，请先在网关后台完成审批。";
            }

            if (!authorization.AllowsSite(siteKey))
            {
                return "移动 APP 设备尚未获批当前站点，请在网关后台补充审批。";
            }

            return "移动 APP 设备尚未获批，请先在网关后台完成审批。";
        }

        private static string DescribeDeviceIdFields(HttpContext context)
        {
            try
            {
                var fields = new List<string>();
                var r = context.Request;
                string[] idKeys = { "MachineId", "machineId", "DeviceImei", "deviceImei", "imei",
                    "DeviceNo", "deviceNo", "TrAppKey", "trAppKey", "appKey",
                    "DeviceVersion", "deviceVersion", "AppVersion", "appVersion" };
                foreach (var key in idKeys)
                {
                    var val = r.Form[key] ?? r.QueryString[key];
                    if (!string.IsNullOrEmpty(val)) fields.Add(key);
                }
                return string.Join(",", fields);
            }
            catch { return ""; }
        }

    }
}
