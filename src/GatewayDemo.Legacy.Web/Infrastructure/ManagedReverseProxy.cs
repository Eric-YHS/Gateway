using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.WebSockets;
using GatewayDemo.Legacy.Core.Models;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    public sealed class ManagedReverseProxy
    {
        internal const string ForcedRelativePathContextKey = "__Gateway.ForcedRelativePath";
        internal const string ForceUpstreamOriginRootContextKey = "__Gateway.ForceUpstreamOriginRoot";
        internal const string SuppressUpstreamRedirectContextKey = "__Gateway.SuppressUpstreamRedirect";
        internal const string ExternalApiClientContextKey = "__Gateway.ExternalApiClient";
        private const string ResolvedRelativePathContextKey = "__Gateway.ResolvedRelativePath";
        private const string LegacyRootPathMarker = GatewayPathUtility.LegacyRootPathMarker;
        private const string GatewayPublicOriginHeaderName = GatewayPublicUrlContext.PublicOriginHeaderName;
        private const string GatewayPublicBaseHeaderName = GatewayPublicUrlContext.PublicBaseHeaderName;
        private const string GatewayProxyBaseHeaderName = GatewayPublicUrlContext.ProxyBaseHeaderName;
        private const string GatewayProxySiteBaseHeaderName = GatewayPublicUrlContext.ProxySiteBaseHeaderName;

        private static readonly Regex CharsetRegex = new Regex(
            "(?is)\\bcharset\\s*=\\s*[\"']?\\s*(?<charset>[^\\s\"'/>;]+)",
            RegexOptions.Compiled);

        private static readonly Regex CssCharsetRegex = new Regex(
            "(?is)@charset\\s+[\"'](?<charset>[^\"']+)",
            RegexOptions.Compiled);

        private static readonly Regex XmlEncodingRegex = new Regex(
            "(?is)<\\?xml[^>]*\\bencoding\\s*=\\s*[\"'](?<charset>[^\"']+)",
            RegexOptions.Compiled);

        private readonly LegacyGatewayConfiguration configuration;

        public ManagedReverseProxy(LegacyGatewayConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public void Proxy(HttpContext context, GatewaySiteOptions site, DeviceContext device, DeviceCredentialService credentialService)
        {
            Uri upstreamUri;
            try
            {
                upstreamUri = BuildUpstreamUri(context, site);
            }
            catch (UriFormatException ex)
            {
                WriteBadGateway(context, site.UpstreamBaseUrl, ex);
                return;
            }

            if (IsGatewayServedOrigin(context, upstreamUri))
            {
                WriteUpstreamLoopConfigurationError(context, upstreamUri);
                return;
            }

            if (IsWebSocketUpgradeRequest(context))
            {
                ProxyWebSocket(context, site, upstreamUri);
                return;
            }

            var pathRule = ResolvePathRule(context, site);
            var isStreaming = IsStreamingRequest(context, pathRule);
            var isLongPolling = isStreaming || IsLongPollingRequest(context, pathRule);
            var request = CreateUpstreamRequest(context, site, upstreamUri, isLongPolling, isStreaming, pathRule);

            HttpWebResponse upstreamResponse = null;
            try
            {
                WriteRequestBody(context, request);
                upstreamResponse = (HttpWebResponse)request.GetResponse();
                upstreamResponse = TryRetryFromUpstreamOriginRoot(context, site, upstreamUri, upstreamResponse, credentialService, isLongPolling, isStreaming, pathRule)
                    ?? upstreamResponse;
                CopyResponse(context, upstreamResponse, site, device, credentialService, isStreaming);
            }
            catch (WebException ex)
            {
                upstreamResponse = ex.Response as HttpWebResponse;
                if (upstreamResponse == null)
                {
                    WriteBadGateway(context, upstreamUri.ToString(), ex);
                    return;
                }

                var fallbackResponse = TryRetryFromUpstreamOriginRoot(context, site, upstreamUri, upstreamResponse, credentialService, isLongPolling, isStreaming, pathRule);
                if (fallbackResponse != null)
                {
                    upstreamResponse = fallbackResponse;
                }

                CopyResponse(context, upstreamResponse, site, device, credentialService, isStreaming);
            }
            finally
            {
                if (upstreamResponse != null)
                {
                    upstreamResponse.Dispose();
                }
            }
        }

        private static void WriteBadGateway(HttpContext context, string upstream, Exception exception)
        {
            context.Response.Clear();
            context.Response.StatusCode = 502;
            context.Response.StatusDescription = "Bad Gateway";
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Write("Bad Gateway: upstream service is unavailable.");

            System.Diagnostics.Trace.TraceError(
                "GatewayDemo WriteBadGateway: upstream={0}, exception={1}: {2}",
                upstream ?? "(null)",
                exception == null ? "(null)" : exception.GetType().Name,
                exception == null ? "(null)" : exception.Message);

            context.ApplicationInstance.CompleteRequest();
        }

        private static void WriteUpstreamLoopConfigurationError(HttpContext context, Uri upstreamUri)
        {
            context.Response.Clear();
            context.Response.StatusCode = 502;
            context.Response.StatusDescription = "Bad Gateway";
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Write("Bad Gateway: UpstreamBaseUrl points to the gateway itself.");
            context.Response.Write(Environment.NewLine + "Configure UpstreamBaseUrl as the real upstream address reachable from this gateway server.");
            if (upstreamUri != null)
            {
                context.Response.Write(Environment.NewLine + "Upstream: " + upstreamUri);
            }

            context.ApplicationInstance.CompleteRequest();
        }

        private bool IsGatewayServedOrigin(HttpContext context, Uri upstreamUri)
        {
            if (context == null || context.Request == null || context.Request.Url == null || upstreamUri == null)
            {
                return false;
            }

            if (string.Equals(context.Request.Url.Scheme, upstreamUri.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(context.Request.Url.Host, upstreamUri.Host, StringComparison.OrdinalIgnoreCase)
                && context.Request.Url.Port == upstreamUri.Port)
            {
                return true;
            }

            if (configuration != null && configuration.Gateway != null)
            {
                var targetAuthority = upstreamUri.IsDefaultPort ? upstreamUri.Host : upstreamUri.Authority;
                if (configuration.Gateway.FindSiteByConfiguredHost(targetAuthority) != null
                    || configuration.Gateway.FindSiteByConfiguredHost(upstreamUri.Host) != null)
                {
                    return true;
                }

                var requestHost = GatewayRequestContext.GetExternalHost(context, configuration.Gateway);
                if (HostMatches(requestHost, upstreamUri))
                {
                    if ((!AuthorityHasExplicitPort(requestHost) && upstreamUri.IsDefaultPort)
                        || SameAuthorityPort(requestHost, upstreamUri))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HostMatches(string requestHost, Uri upstreamUri)
        {
            if (string.IsNullOrWhiteSpace(requestHost) || upstreamUri == null)
            {
                return false;
            }

            Uri requestUri;
            if (Uri.TryCreate("http://" + requestHost, UriKind.Absolute, out requestUri))
            {
                return string.Equals(requestUri.Host, upstreamUri.Host, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(requestHost, upstreamUri.Host, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameAuthorityPort(string requestHost, Uri upstreamUri)
        {
            Uri requestUri;
            return Uri.TryCreate("http://" + requestHost, UriKind.Absolute, out requestUri)
                && requestUri.Port == upstreamUri.Port;
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

        private Uri BuildUpstreamUri(HttpContext context, GatewaySiteOptions site)
        {
            var requestPath = context.Request.Path ?? string.Empty;
            var relativePath = context.Items[ForcedRelativePathContextKey] as string;
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                var proxyPrefix = configuration.Gateway.ProxyBasePath + "/" + site.Key + "/";
                relativePath = requestPath.StartsWith(proxyPrefix, StringComparison.OrdinalIgnoreCase)
                    ? requestPath.Substring(proxyPrefix.Length)
                    : string.Empty;
            }

            var useUpstreamOriginRoot = IsTrueContextItem(context, ForceUpstreamOriginRootContextKey);
            relativePath = (relativePath ?? string.Empty).TrimStart('/');
            relativePath = CleanLegacyRootMarkerRelativePath(relativePath, site);
            relativePath = NormalizeLegacyAppRelativePath(context, site, relativePath);
            context.Items[ResolvedRelativePathContextKey] = relativePath;

            var baseUri = new Uri(site.UpstreamBaseUrl, UriKind.Absolute);
            var resolverBase = useUpstreamOriginRoot
                ? new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/")
                : baseUri;
            var upstreamTarget = new Uri(resolverBase, relativePath);

            var builder = new UriBuilder(upstreamTarget);
            builder.Query = NormalizeLegacyAppQuery(context, relativePath);
            return builder.Uri;
        }

        private HttpWebResponse TryRetryFromUpstreamOriginRoot(
            HttpContext context,
            GatewaySiteOptions site,
            Uri attemptedUri,
            HttpWebResponse upstreamResponse,
            DeviceCredentialService credentialService,
            bool isLongPolling,
            bool isStreaming,
            GatewayPathRuleOptions pathRule)
        {
            Uri fallbackUri;
            if (!TryBuildUpstreamOriginRootFallbackUri(context, site, attemptedUri, upstreamResponse, out fallbackUri))
            {
                return null;
            }

            var fallbackRequest = CreateUpstreamRequest(context, site, fallbackUri, isLongPolling, isStreaming, pathRule);
            WriteRequestBody(context, fallbackRequest);
            try
            {
                var fallbackResponse = (HttpWebResponse)fallbackRequest.GetResponse();
                upstreamResponse.Dispose();
                return fallbackResponse;
            }
            catch (WebException ex)
            {
                var fallbackResponse = ex.Response as HttpWebResponse;
                if (fallbackResponse != null)
                {
                    upstreamResponse.Dispose();
                    return fallbackResponse;
                }

                return null;
            }
        }

        private static bool TryBuildUpstreamOriginRootFallbackUri(
            HttpContext context,
            GatewaySiteOptions site,
            Uri attemptedUri,
            HttpWebResponse upstreamResponse,
            out Uri fallbackUri)
        {
            fallbackUri = null;
            if (context == null
                || context.Request == null
                || site == null
                || upstreamResponse == null
                || upstreamResponse.StatusCode != HttpStatusCode.NotFound
                || IsTrueContextItem(context, ForceUpstreamOriginRootContextKey))
            {
                return false;
            }

            if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Uri baseUri;
            if (string.IsNullOrWhiteSpace(site.UpstreamBaseUrl)
                || !Uri.TryCreate(site.UpstreamBaseUrl, UriKind.Absolute, out baseUri)
                || string.IsNullOrWhiteSpace(baseUri.AbsolutePath)
                || string.Equals(baseUri.AbsolutePath, "/", StringComparison.Ordinal))
            {
                return false;
            }

            var relativePath = context.Items[ResolvedRelativePathContextKey] as string;
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            var originRoot = new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/");
            var target = new Uri(originRoot, relativePath.TrimStart('/'));
            var builder = new UriBuilder(target);
            builder.Query = NormalizeLegacyAppQuery(context, relativePath);
            fallbackUri = builder.Uri;
            return attemptedUri == null || !Uri.Compare(attemptedUri, fallbackUri, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase).Equals(0);
        }

        private static string NormalizeLegacyAppRelativePath(HttpContext context, GatewaySiteOptions site, string relativePath)
        {
            return relativePath ?? string.Empty;
        }

        private static string NormalizeLegacyAppQuery(HttpContext context, string relativePath)
        {
            var rawQuery = context.Request.Url == null ? string.Empty : context.Request.Url.Query.TrimStart('?');
            return rawQuery;
        }

        private static bool IsTrueContextItem(HttpContext context, string key)
        {
            if (context == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var value = context.Items[key];
            return value is bool && (bool)value;
        }

        private static GatewayPathRuleOptions ResolvePathRule(HttpContext context, GatewaySiteOptions site)
        {
            if (context == null || context.Request == null || site == null || site.PathRules == null || site.PathRules.Count == 0)
            {
                return null;
            }

            var relativePath = context.Items[ResolvedRelativePathContextKey] as string;
            var siteKey = site == null ? string.Empty : site.Key;
            var runtime = LegacyGatewayRuntime.Current;
            var proxyBasePath = runtime == null || runtime.Configuration == null || runtime.Configuration.Gateway == null
                ? "/proxy"
                : runtime.Configuration.Gateway.ProxyBasePath;
            var candidates = GatewayPathCandidateBuilder.BuildRuleCandidates(
                context,
                proxyBasePath,
                site,
                siteKey,
                context.Items[ForcedRelativePathContextKey] as string,
                relativePath);

            foreach (var rule in site.PathRules)
            {
                if (rule == null || rule.Patterns == null || rule.Patterns.Count == 0)
                {
                    continue;
                }

                foreach (var pattern in rule.Patterns)
                {
                    if (GatewayPathUtility.MatchesAnyAllowedPath(candidates, pattern))
                    {
                        return rule;
                    }
                }
            }

            return null;
        }

        private static int ResolveTimeoutMilliseconds(int configuredSeconds, int fallbackMilliseconds)
        {
            if (configuredSeconds <= 0)
            {
                return fallbackMilliseconds;
            }

            var milliseconds = (long)configuredSeconds * 1000L;
            return milliseconds > int.MaxValue ? int.MaxValue : (int)milliseconds;
        }

        private HttpWebRequest CreateUpstreamRequest(HttpContext context, GatewaySiteOptions site, Uri upstreamUri, bool isLongPolling = false, bool isStreaming = false, GatewayPathRuleOptions pathRule = null)
        {
            var request = (HttpWebRequest)WebRequest.Create(upstreamUri);
            request.Method = context.Request.HttpMethod;
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = isStreaming
                ? DecompressionMethods.None
                : DecompressionMethods.GZip | DecompressionMethods.Deflate;
            ApplyUpstreamProxy(request);
            pathRule = pathRule ?? ResolvePathRule(context, site);
            request.Timeout = ResolveTimeoutMilliseconds(
                pathRule == null ? 0 : pathRule.TimeoutSeconds,
                isLongPolling ? 1800000 : 100000);
            request.ReadWriteTimeout = ResolveTimeoutMilliseconds(
                pathRule == null ? 0 : pathRule.ReadWriteTimeoutSeconds,
                isLongPolling ? 1800000 : 100000);
            request.KeepAlive = true;
            request.ContentType = context.Request.ContentType;
            CopyRequestHeaders(context, request, isStreaming);

            if (isStreaming)
            {
                request.Headers["Accept-Encoding"] = "identity";
            }

            request.Headers["X-Gateway-Site"] = site.Key;
            request.Headers["X-Forwarded-For"] = BuildForwardedFor(context);
            request.Headers["X-Forwarded-Host"] = GatewayRequestContext.GetExternalHost(context, configuration.Gateway);
            request.Headers["X-Forwarded-Proto"] = GatewayRequestContext.GetExternalScheme(context, configuration.Gateway);
            foreach (var header in BuildGatewayPublicContextHeaders(context, site))
            {
                request.Headers[header.Key] = header.Value;
            }

            var externalApiClient = context.Items[ExternalApiClientContextKey] as string;
            if (!string.IsNullOrWhiteSpace(externalApiClient))
            {
                request.Headers["X-Gateway-External-Client"] = externalApiClient;
            }

            if (context.Request.ContentLength > 0)
            {
                request.ContentLength = context.Request.ContentLength;
            }

            return request;
        }

        private void ApplyUpstreamProxy(HttpWebRequest request)
        {
            if (request == null)
            {
                return;
            }

            var configuredProxy = configuration == null || configuration.Gateway == null
                ? string.Empty
                : configuration.Gateway.UpstreamProxyUrl;
            if (!string.IsNullOrWhiteSpace(configuredProxy))
            {
                request.Proxy = CreateConfiguredUpstreamProxy(configuredProxy, request.RequestUri);
                return;
            }

            if (configuration == null
                || configuration.Gateway == null
                || !configuration.Gateway.UseDefaultUpstreamProxy)
            {
                request.Proxy = null;
                return;
            }

            var proxy = WebRequest.DefaultWebProxy;
            if (proxy == null)
            {
                request.Proxy = null;
                return;
            }

            proxy.Credentials = CredentialCache.DefaultCredentials;
            request.Proxy = proxy;
        }

        private static IWebProxy CreateConfiguredUpstreamProxy(string proxySetting, Uri upstreamUri)
        {
            var selectedProxy = SelectProxySettingForUpstream(proxySetting, upstreamUri);
            if (string.IsNullOrWhiteSpace(selectedProxy))
            {
                return null;
            }

            var normalized = selectedProxy.Trim();
            if (normalized.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                normalized = "http://" + normalized;
            }

            Uri proxyUri;
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out proxyUri))
            {
                return null;
            }

            var proxy = new WebProxy(proxyUri);
            proxy.Credentials = CredentialCache.DefaultCredentials;
            return proxy;
        }

        private static string SelectProxySettingForUpstream(string proxySetting, Uri upstreamUri)
        {
            if (string.IsNullOrWhiteSpace(proxySetting))
            {
                return string.Empty;
            }

            var trimmed = proxySetting.Trim();
            if (trimmed.IndexOf('=') < 0)
            {
                return trimmed;
            }

            var upstreamScheme = upstreamUri == null ? string.Empty : (upstreamUri.Scheme ?? string.Empty);
            if (string.Equals(upstreamScheme, "wss", StringComparison.OrdinalIgnoreCase))
            {
                upstreamScheme = "https";
            }
            else if (string.Equals(upstreamScheme, "ws", StringComparison.OrdinalIgnoreCase))
            {
                upstreamScheme = "http";
            }

            string fallback = string.Empty;
            foreach (var part in trimmed.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = part.IndexOf('=');
                if (separator <= 0 || separator >= part.Length - 1)
                {
                    continue;
                }

                var scheme = part.Substring(0, separator).Trim();
                var value = part.Substring(separator + 1).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (string.Equals(scheme, upstreamScheme, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }

                if (string.IsNullOrWhiteSpace(fallback)
                    && string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase))
                {
                    fallback = value;
                }
            }

            return fallback;
        }

        private static void CopyRequestHeaders(HttpContext context, HttpWebRequest request, bool isStreaming)
        {
            foreach (var headerName in context.Request.Headers.AllKeys)
            {
                if (ShouldSkipRequestHeader(headerName, isStreaming))
                {
                    continue;
                }

                var headerValue = context.Request.Headers[headerName];
                if (string.IsNullOrWhiteSpace(headerValue))
                {
                    continue;
                }

                if (string.Equals(headerName, "Accept", StringComparison.OrdinalIgnoreCase))
                {
                    request.Accept = headerValue;
                }
                else if (string.Equals(headerName, "User-Agent", StringComparison.OrdinalIgnoreCase))
                {
                    request.UserAgent = headerValue;
                }
                else if (string.Equals(headerName, "Referer", StringComparison.OrdinalIgnoreCase))
                {
                    request.Referer = headerValue;
                }
                else if (string.Equals(headerName, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    request.ContentType = headerValue;
                }
                else
                {
                    request.Headers[headerName] = headerValue;
                }
            }
        }

        private const long MaxRequestBodyBytes = 100L * 1024 * 1024; // 100 MB

        private static void WriteRequestBody(HttpContext context, HttpWebRequest request)
        {
            if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                && !context.Request.HttpMethod.Equals("PUT", StringComparison.OrdinalIgnoreCase)
                && !context.Request.HttpMethod.Equals("PATCH", StringComparison.OrdinalIgnoreCase)
                && !context.Request.HttpMethod.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!context.Request.InputStream.CanRead)
            {
                return;
            }

            var contentLength = context.Request.ContentLength;
            if (contentLength > MaxRequestBodyBytes)
            {
                throw new System.Web.HttpException(413, "Request body exceeds maximum allowed size.");
            }

            if (context.Request.InputStream.CanSeek)
            {
                context.Request.InputStream.Position = 0;
            }

            using (var upstreamStream = request.GetRequestStream())
            {
                context.Request.InputStream.CopyTo(upstreamStream);
            }
        }

        private void CopyResponse(HttpContext context, HttpWebResponse upstreamResponse, GatewaySiteOptions site, DeviceContext device, DeviceCredentialService credentialService, bool isStreamingRequest)
        {
            if (TryWriteSuppressedUpstreamRedirect(context, upstreamResponse, site, device, credentialService))
            {
                return;
            }

            if (TryWriteLegacyAppUpstreamError(context, upstreamResponse, site, device, credentialService))
            {
                return;
            }

            if (TryWriteLegacyAppUnexpectedHtmlResponse(context, upstreamResponse, site, device, credentialService))
            {
                return;
            }

            var isStreamingResponse = isStreamingRequest || IsStreamingResponse(upstreamResponse.ContentType);
            var shouldCaptureLegacyAppLogin = credentialService != null
                && credentialService.ShouldCaptureLegacyAppLoginResponse(context, device, upstreamResponse.ContentType);
            var responseEncoding = ResolveResponseEncoding(upstreamResponse, null);
            byte[] bufferedResponseBody = null;

            // Keep upstream response bodies byte-for-byte intact. The gateway maps
            // subsequent browser request URLs to upstream URLs instead of editing HTML/CSS/JS.
            if (!isStreamingResponse && shouldCaptureLegacyAppLogin)
            {
                using (var responseStream = upstreamResponse.GetResponseStream())
                {
                    bufferedResponseBody = responseStream == null ? new byte[0] : ReadAllBytes(responseStream);
                }

                responseEncoding = ResolveResponseEncoding(upstreamResponse, bufferedResponseBody);
                credentialService.CaptureLegacyAppLoginSession(
                    context,
                    device,
                    DecodeResponseBytes(bufferedResponseBody, responseEncoding));
            }

            context.Response.StatusCode = (int)upstreamResponse.StatusCode;
            if (!string.IsNullOrWhiteSpace(upstreamResponse.StatusDescription))
            {
                // HTTP status lines carry no charset; non-ASCII reason phrases from
                // legacy upstreams reach browsers as mojibake, so keep ASCII only.
                context.Response.StatusDescription = ToAsciiStatusDescription(
                    upstreamResponse.StatusDescription,
                    (int)upstreamResponse.StatusCode);
            }

            if (!string.IsNullOrWhiteSpace(upstreamResponse.ContentType))
            {
                context.Response.ContentType = upstreamResponse.ContentType;
            }

            context.Response.ContentEncoding = responseEncoding;

            if (isStreamingResponse)
            {
                PrepareStreamingResponse(context);
            }

            foreach (var headerName in upstreamResponse.Headers.AllKeys)
            {
                if (ShouldSkipResponseHeader(headerName))
                {
                    continue;
                }

                var headerValues = upstreamResponse.Headers.GetValues(headerName);
                if (headerValues == null)
                {
                    continue;
                }

                foreach (var originalValue in headerValues)
                {
                    var headerValue = originalValue;
                    if (string.Equals(headerName, "Location", StringComparison.OrdinalIgnoreCase))
                    {
                        headerValue = RewriteLocation(headerValue, site);
                    }
                    else if (string.Equals(headerName, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                    {
                        headerValue = RewriteSetCookie(headerValue, site, GatewayRequestContext.IsExternalHttps(context, configuration.Gateway));
                    }
                    else if (string.Equals(headerName, "Content-Disposition", StringComparison.OrdinalIgnoreCase))
                    {
                        headerValue = NormalizeContentDispositionHeader(headerValue);
                    }

                    context.Response.AppendHeader(headerName, headerValue);
                }
            }

            if (bufferedResponseBody != null)
            {
                context.Response.OutputStream.Write(bufferedResponseBody, 0, bufferedResponseBody.Length);
            }
            else
            {
                using (var responseStream = upstreamResponse.GetResponseStream())
                {
                    if (responseStream != null)
                    {
                        CopyResponseStream(responseStream, context.Response, isStreamingResponse);
                    }
                }
            }

            FlushResponse(context.Response);
        }

        private static bool TryWriteSuppressedUpstreamRedirect(
            HttpContext context,
            HttpWebResponse upstreamResponse,
            GatewaySiteOptions site,
            DeviceContext device,
            DeviceCredentialService credentialService)
        {
            if (context == null
                || upstreamResponse == null)
            {
                return false;
            }

            var suppress = context.Items[SuppressUpstreamRedirectContextKey];
            if (!(suppress is bool) || !((bool)suppress))
            {
                return false;
            }

            var statusCode = (int)upstreamResponse.StatusCode;
            if (!IsRedirectStatusCode(statusCode))
            {
                return false;
            }

            var location = upstreamResponse.Headers["Location"] ?? string.Empty;
            var serializer = new JavaScriptSerializer();

            context.Response.Clear();
            context.Response.StatusCode = 200;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["X-Gateway-Upstream-Status"] = statusCode.ToString();
            context.Response.Headers["X-Gateway-Upstream-Location"] = location;
            context.Response.Write(serializer.Serialize(new
            {
                Status = 0,
                Code = 502,
                Msg = "Upstream returned a redirect during an anonymous bootstrap request.",
                Message = "Upstream returned a redirect during an anonymous bootstrap request.",
                GatewayUpstreamBlocked = true,
                UpstreamLocation = location,
                Site = site == null ? string.Empty : site.Key,
                Data = string.Empty
            }));
            context.Response.Flush();
            return true;
        }

        private static bool TryWriteLegacyAppUpstreamError(
            HttpContext context,
            HttpWebResponse upstreamResponse,
            GatewaySiteOptions site,
            DeviceContext device,
            DeviceCredentialService credentialService)
        {
            if (context == null
                || upstreamResponse == null
                || credentialService == null)
            {
                return false;
            }

            var descriptor = credentialService.DescribeLegacyAppRequest(context);
            var isLegacyAppResponse = (device != null && credentialService.IsLegacyAppContext(device))
                || (descriptor != null && descriptor.IsLegacyApp)
                || LooksLikeMobileAppRequest(context, descriptor);
            if (!isLegacyAppResponse)
            {
                return false;
            }

            var statusCode = (int)upstreamResponse.StatusCode;
            if (!IsRedirectStatusCode(statusCode))
            {
                return false;
            }

            var location = upstreamResponse.Headers["Location"] ?? string.Empty;
            var serializer = new JavaScriptSerializer();
            var message = "Upstream returned a redirect instead of a mobile API response. Check the upstream APP endpoint, HostNames mapping, and product line site.";

            context.Response.Clear();
            context.Response.StatusCode = 200;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["X-Gateway-Upstream-Status"] = statusCode.ToString();
            context.Response.Headers["X-Gateway-Upstream-Location"] = location;

            string payload;
            if (descriptor != null && descriptor.IsLoginRequest)
            {
                payload = serializer.Serialize(new
                {
                    LoginStatus = 0,
                    Msg = message,
                    GatewayUpstreamBlocked = true,
                    UpstreamLocation = location,
                    Applicant = descriptor.ApplicantName,
                    Company = descriptor.CompanyName,
                    Site = site == null ? string.Empty : site.Key
                });
            }
            else
            {
                payload = serializer.Serialize(new
                {
                    Status = 0,
                    Code = 502,
                    Msg = message,
                    Message = message,
                    GatewayUpstreamBlocked = true,
                    UpstreamLocation = location,
                    Applicant = descriptor == null ? string.Empty : descriptor.ApplicantName,
                    Company = descriptor == null ? string.Empty : descriptor.CompanyName,
                    Site = site == null ? string.Empty : site.Key,
                    Data = string.Empty
                });
            }

            context.Response.Write(payload);
            context.Response.Flush();
            return true;
        }

        private static bool IsRedirectStatusCode(int statusCode)
        {
            return statusCode >= 300
                && statusCode <= 399
                && statusCode != (int)HttpStatusCode.NotModified;
        }

        private static bool TryWriteLegacyAppUnexpectedHtmlResponse(
            HttpContext context,
            HttpWebResponse upstreamResponse,
            GatewaySiteOptions site,
            DeviceContext device,
            DeviceCredentialService credentialService)
        {
            if (context == null
                || upstreamResponse == null
                || credentialService == null)
            {
                return false;
            }

            var descriptor = credentialService.DescribeLegacyAppRequest(context);
            var isLegacyAppResponse = (device != null && credentialService.IsLegacyAppContext(device))
                || LooksLikeMobileAppRequest(context, descriptor);
            if (!isLegacyAppResponse)
            {
                return false;
            }

            if (descriptor == null || !descriptor.IsLoginRequest)
            {
                return false;
            }

            var contentType = upstreamResponse.ContentType ?? string.Empty;
            if (contentType.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) < 0
                && contentType.IndexOf("application/xhtml+xml", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            var serializer = new JavaScriptSerializer();
            var message = "Mobile login expected APP JSON, but upstream returned an HTML page. Please verify the upstream mobile login endpoint.";

            context.Response.Clear();
            context.Response.StatusCode = 200;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["X-Gateway-Upstream-Content-Type"] = contentType;
            context.Response.Write(serializer.Serialize(new
            {
                LoginStatus = 0,
                Msg = message,
                GatewayUpstreamBlocked = true,
                Applicant = descriptor.ApplicantName,
                Company = descriptor.CompanyName,
                Site = site == null ? string.Empty : site.Key
            }));
            context.Response.Flush();
            return true;
        }

        private static bool LooksLikeMobileAppRequest(HttpContext context, LegacyAppRequestDescriptor descriptor)
        {
            if (descriptor != null && descriptor.IsLegacyApp)
            {
                return true;
            }

            if (context == null || context.Request == null)
            {
                return false;
            }

            return false;
        }

        private string RewriteLocation(string headerValue, GatewaySiteOptions site)
        {
            if (string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue;
            }

            Uri absoluteUri;
            if (headerValue.StartsWith("//", StringComparison.Ordinal))
            {
                var rewrittenProtocolRelativeUrl = RewriteProtocolRelativeUrl(headerValue, site);
                return string.IsNullOrWhiteSpace(rewrittenProtocolRelativeUrl)
                    ? headerValue
                    : rewrittenProtocolRelativeUrl;
            }

            if (Uri.TryCreate(headerValue, UriKind.Absolute, out absoluteUri))
            {
                var upstreamSite = FindSiteByAbsoluteOrigin(absoluteUri);
                if (upstreamSite != null)
                {
                    return RewriteAbsoluteUriPath(absoluteUri, upstreamSite);
                }

                return headerValue;
            }

            if (headerValue.StartsWith("/", StringComparison.Ordinal))
            {
                return RewriteAbsolutePath(headerValue, site);
            }

            return headerValue;
        }

        private static bool ShouldSkipResponseHeader(string headerName)
        {
            return string.Equals(headerName, "Content-Length", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Content-Type", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Content-Encoding", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Keep-Alive", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Server", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Connection", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeContentDispositionHeader(string headerValue)
        {
            if (string.IsNullOrWhiteSpace(headerValue)
                || headerValue.IndexOf("filename*", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return headerValue;
            }

            var fileName = ExtractContentDispositionFileName(headerValue);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return headerValue;
            }

            fileName = TryDecodeHeaderFileName(fileName);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return headerValue;
            }

            var parameters = SplitContentDisposition(headerValue);
            var builder = new StringBuilder();
            foreach (var parameter in parameters)
            {
                if (string.IsNullOrWhiteSpace(parameter))
                {
                    continue;
                }

                var trimmed = parameter.Trim();
                if (trimmed.StartsWith("filename=", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("filename*=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(trimmed);
            }

            if (builder.Length == 0)
            {
                builder.Append("attachment");
            }

            builder.Append("; filename=\"")
                .Append(EscapeQuotedHeaderValue(BuildAsciiFileNameFallback(fileName)))
                .Append("\"; filename*=UTF-8''")
                .Append(PercentEncodeUtf8(fileName));
            return builder.ToString();
        }

        private static string ExtractContentDispositionFileName(string headerValue)
        {
            var parameters = SplitContentDisposition(headerValue);
            foreach (var parameter in parameters)
            {
                var trimmed = (parameter ?? string.Empty).Trim();
                if (!trimmed.StartsWith("filename=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = trimmed.Substring("filename=".Length).Trim();
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                {
                    value = value.Substring(1, value.Length - 2);
                }

                return value.Replace("\\\"", "\"").Trim();
            }

            return string.Empty;
        }

        private static IList<string> SplitContentDisposition(string headerValue)
        {
            var values = new List<string>();
            if (string.IsNullOrEmpty(headerValue))
            {
                return values;
            }

            var start = 0;
            var inQuote = false;
            for (var i = 0; i < headerValue.Length; i++)
            {
                var ch = headerValue[i];
                if (ch == '"' && (i == 0 || headerValue[i - 1] != '\\'))
                {
                    inQuote = !inQuote;
                    continue;
                }

                if (ch == ';' && !inQuote)
                {
                    values.Add(headerValue.Substring(start, i - start));
                    start = i + 1;
                }
            }

            values.Add(headerValue.Substring(start));
            return values;
        }

        private static string TryDecodeHeaderFileName(string fileName)
        {
            var value = (fileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var urlDecoded = HttpUtility.UrlDecode(value, Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(urlDecoded))
            {
                value = urlDecoded;
            }

            var allLatin1 = true;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] > 255)
                {
                    allLatin1 = false;
                    break;
                }
            }

            if (!allLatin1)
            {
                return value;
            }

            try
            {
                var bytes = Encoding.GetEncoding(28591).GetBytes(value);
                var decoded = new UTF8Encoding(false, true).GetString(bytes);
                return ContainsCjk(decoded) ? decoded : value;
            }
            catch (DecoderFallbackException)
            {
                return value;
            }
            catch (ArgumentException)
            {
                return value;
            }
        }

        private static bool ContainsCjk(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if ((ch >= 0x4E00 && ch <= 0x9FFF)
                    || (ch >= 0x3400 && ch <= 0x4DBF)
                    || (ch >= 0xF900 && ch <= 0xFAFF))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildAsciiFileNameFallback(string fileName)
        {
            var extension = SanitizeAsciiHeaderToken(Path.GetExtension(fileName ?? string.Empty)).Trim('.');
            var baseName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
            var fallback = SanitizeAsciiHeaderToken(baseName).Trim(' ', '.', '_');
            if (string.IsNullOrWhiteSpace(fallback))
            {
                fallback = "download";
            }

            return string.IsNullOrWhiteSpace(extension) ? fallback : fallback + "." + extension;
        }

        private static string SanitizeAsciiHeaderToken(string value)
        {
            var builder = new StringBuilder();
            foreach (var ch in value ?? string.Empty)
            {
                if (ch >= 32 && ch <= 126 && ch != '"' && ch != '\\' && ch != '/' && ch != ':' && ch != '*' && ch != '?' && ch != '<' && ch != '>' && ch != '|')
                {
                    builder.Append(ch);
                }
                else
                {
                    builder.Append('_');
                }
            }

            return builder.ToString();
        }

        private static string EscapeQuotedHeaderValue(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string PercentEncodeUtf8(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            var builder = new StringBuilder(bytes.Length * 3);
            foreach (var b in bytes)
            {
                if ((b >= 'A' && b <= 'Z')
                    || (b >= 'a' && b <= 'z')
                    || (b >= '0' && b <= '9')
                    || b == '-' || b == '_' || b == '.' || b == '~')
                {
                    builder.Append((char)b);
                }
                else
                {
                    builder.Append('%').Append(b.ToString("X2"));
                }
            }

            return builder.ToString();
        }

        private static bool ShouldSkipRequestHeader(string headerName, bool isStreaming)
        {
            return string.Equals(headerName, "Host", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Connection", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Content-Length", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Accept-Encoding", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Keep-Alive", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Proxy-Connection", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Expect", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "If-Modified-Since", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Date", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Range", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "X-Forwarded-For", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "X-Forwarded-Host", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "X-Forwarded-Proto", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "X-Gateway-Site", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, GatewayPublicOriginHeaderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, GatewayPublicBaseHeaderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, GatewayProxyBaseHeaderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, GatewayProxySiteBaseHeaderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, DeviceCredentialService.AppKeyHeaderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, DeviceCredentialService.AppTimestampHeaderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, DeviceCredentialService.AppNonceHeaderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, DeviceCredentialService.AppBodyHashHeaderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, DeviceCredentialService.AppSignatureHeaderName, StringComparison.OrdinalIgnoreCase);
        }

        private static void PrepareStreamingResponse(HttpContext context)
        {
            context.Response.BufferOutput = false;
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Cache.SetNoStore();
            context.Response.AppendHeader("X-Accel-Buffering", "no");
        }

        private static byte[] ReadAllBytes(Stream stream)
        {
            if (stream == null)
            {
                return new byte[0];
            }

            using (var memory = new MemoryStream())
            {
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    memory.Write(buffer, 0, bytesRead);
                }

                return memory.ToArray();
            }
        }

        private static string DecodeResponseBytes(byte[] bytes, Encoding encoding)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            return (encoding ?? Encoding.UTF8).GetString(bytes);
        }

        private static void CopyResponseStream(Stream responseStream, HttpResponse response, bool flushEachChunk)
        {
            var buffer = new byte[16 * 1024];
            while (true)
            {
                if (!response.IsClientConnected)
                {
                    return;
                }

                int bytesRead;
                try
                {
                    bytesRead = responseStream.Read(buffer, 0, buffer.Length);
                }
                catch (IOException)
                {
                    return;
                }
                catch (WebException)
                {
                    return;
                }

                if (bytesRead <= 0)
                {
                    return;
                }

                try
                {
                    response.OutputStream.Write(buffer, 0, bytesRead);
                    if (flushEachChunk)
                    {
                        FlushResponse(response);
                    }
                }
                catch (HttpException)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }
            }
        }

        private static void FlushResponse(HttpResponse response)
        {
            if (response == null || !response.IsClientConnected)
            {
                return;
            }

            try
            {
                response.Flush();
            }
            catch (HttpException)
            {
            }
            catch (IOException)
            {
            }
        }

        private string BuildForwardedFor(HttpContext context)
        {
            return ClientIpResolver.BuildForwardedFor(context, configuration.Gateway);
        }

        private IDictionary<string, string> BuildGatewayPublicContextHeaders(HttpContext context, GatewaySiteOptions site)
        {
            return GatewayPublicUrlContext
                .FromHttpContext(context, configuration, site)
                .ToForwardedHeaders();
        }

        private static bool IsStreamingResponse(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return false;
            }

            var normalized = contentType.ToLowerInvariant();
            return normalized.Contains("text/event-stream");
        }

        private string RewriteProtocolRelativeUrl(string url, GatewaySiteOptions site)
        {
            if (string.IsNullOrWhiteSpace(url) || site == null || string.IsNullOrWhiteSpace(site.UpstreamBaseUrl))
            {
                return url;
            }

            Uri upstreamBase;
            Uri absoluteUri;
            if (!Uri.TryCreate(site.UpstreamBaseUrl, UriKind.Absolute, out upstreamBase)
                || !Uri.TryCreate(upstreamBase.Scheme + ":" + url, UriKind.Absolute, out absoluteUri))
            {
                return url;
            }

            var upstreamSite = FindSiteByAbsoluteOrigin(absoluteUri);
            return upstreamSite == null
                ? url
                : RewriteAbsoluteUriPath(absoluteUri, upstreamSite);
        }

        private GatewaySiteOptions FindSiteByAbsoluteOrigin(Uri absoluteUri)
        {
            if (absoluteUri == null)
            {
                return null;
            }

            return configuration.Gateway.FindSiteByUpstreamOrigin(absoluteUri)
                ?? configuration.Gateway.FindSiteByHost(absoluteUri.Authority);
        }

        private string RewriteAbsoluteUriPath(Uri absoluteUri, GatewaySiteOptions site)
        {
            var pathAndSuffix = MergePathQueryFragment(absoluteUri.AbsolutePath, absoluteUri.Query, absoluteUri.Fragment);
            string rewrittenProxyRootPath;
            if (TryRewriteExistingProxyRootPath(pathAndSuffix, site, out rewrittenProxyRootPath))
            {
                return rewrittenProxyRootPath;
            }

            return RewriteAbsolutePath(pathAndSuffix, site);
        }

        private string RewriteAbsolutePath(string absolutePath, GatewaySiteOptions site)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return absolutePath;
            }

            var publicContext = GatewayPublicUrlContext.ForSite(configuration, site);
            var absoluteBasePath = publicContext.UpstreamBasePath;

            string requestPath;
            string suffix;
            SplitPathSuffix(absolutePath, out requestPath, out suffix);
            requestPath = CleanExternalProxyRootPath(requestPath, site);
            requestPath = StripLegacyRootPathMarker(requestPath);
            if (!requestPath.StartsWith("/", StringComparison.Ordinal))
            {
                requestPath = "/" + requestPath.TrimStart('/');
            }

            string relativePath;
            if (absoluteBasePath.Length > 1
                && requestPath.StartsWith(absoluteBasePath, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = requestPath.Substring(absoluteBasePath.Length).TrimStart('/');
                return publicContext.BuildPublicPath(relativePath, suffix);
            }

            relativePath = requestPath.TrimStart('/');
            return publicContext.BuildPublicPath(relativePath, suffix);
        }

        private string RewriteExistingProxyPath(string url, GatewaySiteOptions currentSite)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            string path;
            string suffix;
            SplitPathSuffix(url, out path, out suffix);
            var siteKey = configuration.ExtractSiteKeyFromRequestPath(path);
            var site = configuration.Gateway.FindSite(siteKey) ?? currentSite;
            if (site == null)
            {
                return url;
            }

            var relativePath = configuration.ExtractRelativeTargetPath(path);
            relativePath = StripLegacyRootPathMarker(relativePath);
            return GatewayPublicUrlContext
                .ForSite(configuration, site)
                .BuildPublicPath(relativePath, suffix);
        }

        private string RewriteSetCookie(string headerValue, GatewaySiteOptions site, bool isSecureConnection)
        {
            if (string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue;
            }

            var parts = headerValue.Split(';');
            if (parts.Length == 0)
            {
                return headerValue;
            }

            var rewritten = new StringBuilder(parts[0].Trim());
            var hasPath = false;
            for (var i = 1; i < parts.Length; i++)
            {
                var segment = (parts[i] ?? string.Empty).Trim();
                if (segment.Length == 0)
                {
                    continue;
                }

                if (segment.StartsWith("Domain=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (segment.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
                {
                    hasPath = true;
                    var pathValue = segment.Substring("Path=".Length).Trim().Trim('"');
                    rewritten.Append("; Path=").Append(RewriteCookiePath(pathValue, site));
                    continue;
                }

                if (segment.Equals("Secure", StringComparison.OrdinalIgnoreCase) && !isSecureConnection)
                {
                    continue;
                }

                rewritten.Append("; ").Append(segment);
            }

            if (!hasPath)
            {
                rewritten.Append("; Path=").Append(RewriteCookiePath("/", site));
            }

            return rewritten.ToString();
        }

        private string RewriteCookiePath(string pathValue, GatewaySiteOptions site)
        {
            return GatewayPublicUrlContext
                .ForSite(configuration, site)
                .RewriteCookiePath(pathValue);
        }

        private string CleanLegacyRootMarkerRelativePath(string relativePath, GatewaySiteOptions site)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || site == null)
            {
                return relativePath;
            }

            var marker = configuration.Gateway.ProxyBasePath.TrimEnd('/') + "/" + site.Key + "/" + LegacyRootPathMarker + "/";
            var normalized = relativePath.Replace("\\", "/");
            if (string.Equals(normalized, LegacyRootPathMarker, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (normalized.StartsWith(LegacyRootPathMarker + "/", StringComparison.OrdinalIgnoreCase))
            {
                var rootRelative = normalized.Substring(LegacyRootPathMarker.Length).TrimStart('/');
                rootRelative = ReplaceIgnoreCase(rootRelative, marker.TrimStart('/'), string.Empty);
                return rootRelative.TrimStart('/');
            }

            return ReplaceIgnoreCase(normalized, marker.TrimStart('/'), string.Empty).TrimStart('/');
        }

        private bool TryRewriteExistingProxyRootPath(string url, GatewaySiteOptions site, out string rewritten)
        {
            rewritten = string.Empty;
            if (!UsesGatewayRootPaths(site) || string.IsNullOrWhiteSpace(url) || site == null)
            {
                return false;
            }

            string path;
            string suffix;
            SplitPathSuffix(url, out path, out suffix);
            var proxyRootPrefix = configuration.Gateway.ProxyBasePath.TrimEnd('/') + "/" + site.Key + "/" + LegacyRootPathMarker;
            if (!path.StartsWith(proxyRootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rootSuffix = path.Substring(proxyRootPrefix.Length);
            if (rootSuffix.Length == 0)
            {
                rewritten = "/" + suffix;
                return true;
            }

            if (rootSuffix.StartsWith("/", StringComparison.Ordinal))
            {
                rootSuffix = rootSuffix.Substring(1);
            }

            var nestedMarker = proxyRootPrefix.TrimStart('/') + "/";
            rootSuffix = ReplaceIgnoreCase(rootSuffix.Replace("\\", "/"), nestedMarker, string.Empty).TrimStart('/');
            rewritten = BuildGatewayRootPath(rootSuffix, suffix);
            return true;
        }

        private string StripLegacyRootPathMarker(string relativePath)
        {
            return GatewayPathUtility.StripLegacyRootPathMarker(relativePath);
        }

        private static void SplitPathSuffix(string value, out string path, out string suffix)
        {
            GatewayPathUtility.SplitPathSuffix(value, out path, out suffix);
        }

        private static string MergePathQueryFragment(string path, string query, string fragment)
        {
            return (path ?? string.Empty) + (query ?? string.Empty) + (fragment ?? string.Empty);
        }

        private string CleanExternalProxyRootPath(string requestPath, GatewaySiteOptions site)
        {
            return GatewayPathUtility.CleanExternalProxyRootPath(requestPath, configuration.Gateway.ProxyBasePath, site);
        }

        private static string BuildGatewayRootPath(string relativePath, string suffix)
        {
            return GatewayPathUtility.BuildGatewayRootPath(relativePath, suffix);
        }

        private string BuildLegacyProxyPath(GatewaySiteOptions site, string relativePath)
        {
            return configuration.BuildProxyPath(site.Key, (relativePath ?? string.Empty).TrimStart('/'));
        }

        private static string ToAsciiStatusDescription(string statusDescription, int statusCode)
        {
            var value = statusDescription ?? string.Empty;
            foreach (var character in value)
            {
                if (character > 0x7E || character < 0x20)
                {
                    var standard = HttpWorkerRequest.GetStatusDescription(statusCode);
                    return string.IsNullOrWhiteSpace(standard) ? "Status " + statusCode : standard;
                }
            }

            return value;
        }

        private static bool UsesGatewayRootPaths(GatewaySiteOptions site)
        {
            return GatewayPathUtility.UsesGatewayRootPathsForRequest(site);
        }

        private static string ReplaceIgnoreCase(string input, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue))
            {
                return input;
            }

            return Regex.Replace(input, Regex.Escape(oldValue), newValue ?? string.Empty, RegexOptions.IgnoreCase);
        }

        private static string NormalizeBasePath(string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
            {
                return "/";
            }

            return basePath.TrimEnd('/') + "/";
        }

        private static Encoding ResolveResponseEncoding(HttpWebResponse upstreamResponse, byte[] responseBytes)
        {
            Encoding encoding;
            if (TryGetEncodingFromContentType(upstreamResponse == null ? string.Empty : upstreamResponse.ContentType, out encoding))
            {
                return encoding;
            }

            if (TryGetEncodingFromBom(responseBytes, out encoding))
            {
                return encoding;
            }

            if (TryGetEncodingFromBodyDeclaration(responseBytes, out encoding))
            {
                return encoding;
            }

            return Encoding.UTF8;
        }

        private static bool TryGetEncodingFromContentType(string contentType, out Encoding encoding)
        {
            encoding = null;
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return false;
            }

            var match = CharsetRegex.Match(contentType);
            return match.Success && TryGetEncoding(match.Groups["charset"].Value, out encoding);
        }

        private static bool TryGetEncodingFromBom(byte[] bytes, out Encoding encoding)
        {
            encoding = null;
            if (bytes == null || bytes.Length < 2)
            {
                return false;
            }

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encoding = Encoding.UTF8;
                return true;
            }

            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = Encoding.Unicode;
                return true;
            }

            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode;
                return true;
            }

            return false;
        }

        private static bool TryGetEncodingFromBodyDeclaration(byte[] bytes, out Encoding encoding)
        {
            encoding = null;
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            var length = Math.Min(bytes.Length, 8192);
            var sample = Encoding.ASCII.GetString(bytes, 0, length);

            var match = CharsetRegex.Match(sample);
            if (match.Success && TryGetEncoding(match.Groups["charset"].Value, out encoding))
            {
                return true;
            }

            match = CssCharsetRegex.Match(sample);
            if (match.Success && TryGetEncoding(match.Groups["charset"].Value, out encoding))
            {
                return true;
            }

            match = XmlEncodingRegex.Match(sample);
            return match.Success && TryGetEncoding(match.Groups["charset"].Value, out encoding);
        }

        private static bool TryGetEncoding(string charset, out Encoding encoding)
        {
            encoding = null;
            var normalized = (charset ?? string.Empty).Trim().Trim('"', '\'', ';').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            try
            {
                encoding = Encoding.GetEncoding(normalized);
                return true;
            }
            catch (ArgumentException)
            {
                if (string.Equals(normalized, "gbk", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "gb_2312-80", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        encoding = Encoding.GetEncoding(936);
                        return true;
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }

            return false;
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

        private void ProxyWebSocket(HttpContext context, GatewaySiteOptions site, Uri upstreamHttpUri)
        {
            if (context == null || !context.IsWebSocketRequest || upstreamHttpUri == null)
            {
                WriteWebSocketNotSupported(context);
                return;
            }

            var proxyRequest = BuildWebSocketProxyRequest(context, site, upstreamHttpUri);
            var options = new AspNetWebSocketOptions
            {
                RequireSameOrigin = false,
                SubProtocol = proxyRequest.AcceptedSubProtocol
            };

            context.AcceptWebSocketRequest(
                socketContext => RunWebSocketProxyAsync(socketContext, proxyRequest),
                options);
        }

        private WebSocketProxyRequest BuildWebSocketProxyRequest(HttpContext context, GatewaySiteOptions site, Uri upstreamHttpUri)
        {
            var builder = new UriBuilder(upstreamHttpUri);
            builder.Scheme = string.Equals(builder.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";

            var requestedProtocols = new List<string>();
            var protocolHeader = context.Request.Headers["Sec-WebSocket-Protocol"] ?? string.Empty;
            foreach (var protocol in protocolHeader.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = protocol.Trim();
                if (trimmed.Length > 0)
                {
                    requestedProtocols.Add(trimmed);
                }
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var headerName in context.Request.Headers.AllKeys)
            {
                if (ShouldSkipWebSocketRequestHeader(headerName))
                {
                    continue;
                }

                var headerValue = context.Request.Headers[headerName];
                if (!string.IsNullOrWhiteSpace(headerValue))
                {
                    headers[headerName] = headerValue;
                }
            }

            headers["X-Gateway-Site"] = site.Key;
            headers["X-Forwarded-For"] = BuildForwardedFor(context);
            headers["X-Forwarded-Host"] = GatewayRequestContext.GetExternalHost(context, configuration.Gateway);
            headers["X-Forwarded-Proto"] = GatewayRequestContext.GetExternalScheme(context, configuration.Gateway);
            foreach (var header in BuildGatewayPublicContextHeaders(context, site))
            {
                headers[header.Key] = header.Value;
            }

            return new WebSocketProxyRequest
            {
                UpstreamUri = builder.Uri,
                Headers = headers,
                RequestedSubProtocols = requestedProtocols,
                AcceptedSubProtocol = requestedProtocols.Count > 0 ? requestedProtocols[0] : null,
                UseDefaultUpstreamProxy = configuration.Gateway.UseDefaultUpstreamProxy,
                UpstreamProxyUrl = configuration.Gateway.UpstreamProxyUrl
            };
        }

        private static async Task RunWebSocketProxyAsync(AspNetWebSocketContext socketContext, WebSocketProxyRequest proxyRequest)
        {
            var downstream = socketContext.WebSocket;
            using (var upstream = new ClientWebSocket())
            using (var cts = new CancellationTokenSource())
            {
                upstream.Options.Proxy = CreateWebSocketProxy(proxyRequest);
                upstream.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                foreach (var protocol in proxyRequest.RequestedSubProtocols)
                {
                    upstream.Options.AddSubProtocol(protocol);
                }

                foreach (var header in proxyRequest.Headers)
                {
                    try
                    {
                        upstream.Options.SetRequestHeader(header.Key, header.Value);
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (NotSupportedException)
                    {
                    }
                }

                var upstreamConnected = false;
                try
                {
                    await upstream.ConnectAsync(proxyRequest.UpstreamUri, cts.Token).ConfigureAwait(false);
                    upstreamConnected = true;
                }
                catch
                {
                }

                if (!upstreamConnected)
                {
                    await CloseWebSocketAsync(downstream, WebSocketCloseStatus.EndpointUnavailable, "Upstream WebSocket unavailable", CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                var downstreamToUpstream = PumpWebSocketAsync(downstream, upstream, cts.Token);
                var upstreamToDownstream = PumpWebSocketAsync(upstream, downstream, cts.Token);
                await Task.WhenAny(downstreamToUpstream, upstreamToDownstream).ConfigureAwait(false);
                cts.Cancel();
                await IgnoreWebSocketPumpAsync(downstreamToUpstream).ConfigureAwait(false);
                await IgnoreWebSocketPumpAsync(upstreamToDownstream).ConfigureAwait(false);
            }
        }

        private static async Task PumpWebSocketAsync(WebSocket source, WebSocket destination, CancellationToken cancellationToken)
        {
            var buffer = new byte[16 * 1024];
            while (!cancellationToken.IsCancellationRequested
                && source.State == WebSocketState.Open
                && destination.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await source.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CloseWebSocketAsync(destination, result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription, cancellationToken).ConfigureAwait(false);
                    return;
                }

                try
                {
                    await destination.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }
            }
        }

        private static async Task CloseWebSocketAsync(WebSocket socket, WebSocketCloseStatus closeStatus, string description, CancellationToken cancellationToken)
        {
            if (socket == null
                || (socket.State != WebSocketState.Open && socket.State != WebSocketState.CloseReceived))
            {
                return;
            }

            try
            {
                await socket.CloseAsync(closeStatus, description ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private static async Task IgnoreWebSocketPumpAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private static bool ShouldSkipWebSocketRequestHeader(string headerName)
        {
            return string.IsNullOrWhiteSpace(headerName)
                || string.Equals(headerName, "Host", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Connection", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Upgrade", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Content-Length", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Sec-WebSocket-Version", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Sec-WebSocket-Extensions", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Sec-WebSocket-Protocol", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "X-Forwarded-For", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "X-Forwarded-Host", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "X-Forwarded-Proto", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "X-Gateway-Site", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, GatewayPublicOriginHeaderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, GatewayPublicBaseHeaderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, GatewayProxyBaseHeaderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, GatewayProxySiteBaseHeaderName, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class WebSocketProxyRequest
        {
            public Uri UpstreamUri;
            public IDictionary<string, string> Headers;
            public IList<string> RequestedSubProtocols;
            public string AcceptedSubProtocol;
            public bool UseDefaultUpstreamProxy;
            public string UpstreamProxyUrl;
        }

        private static IWebProxy CreateWebSocketProxy(WebSocketProxyRequest proxyRequest)
        {
            if (proxyRequest == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(proxyRequest.UpstreamProxyUrl))
            {
                return CreateConfiguredUpstreamProxy(proxyRequest.UpstreamProxyUrl, proxyRequest.UpstreamUri);
            }

            if (!proxyRequest.UseDefaultUpstreamProxy)
            {
                return null;
            }

            var proxy = WebRequest.DefaultWebProxy;
            if (proxy != null)
            {
                proxy.Credentials = CredentialCache.DefaultCredentials;
            }

            return proxy;
        }

        private static void WriteWebSocketNotSupported(HttpContext context)
        {
            context.Response.StatusCode = 501;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Write("{\"Status\":0,\"Code\":501,\"Msg\":\"IIS WebSocket Protocol feature is not enabled or this request did not reach ASP.NET as a WebSocket upgrade.\",\"Message\":\"IIS WebSocket Protocol feature is required\"}");
        }

        private static bool IsLongPollingRequest(HttpContext context, GatewayPathRuleOptions pathRule)
        {
            if (pathRule != null && pathRule.TreatAsLongPolling)
            {
                return true;
            }

            if (context == null || context.Request == null)
            {
                return false;
            }

            var accept = context.Request.Headers["Accept"] ?? string.Empty;
            if (accept.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var requestPath = (context.Request.Path ?? string.Empty).ToLowerInvariant();
            if (requestPath.IndexOf("/signalr/", StringComparison.OrdinalIgnoreCase) >= 0
                || requestPath.IndexOf("/notifications/", StringComparison.OrdinalIgnoreCase) >= 0
                || requestPath.IndexOf("/messagehub", StringComparison.OrdinalIgnoreCase) >= 0
                || requestPath.IndexOf("/chathub", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static bool IsStreamingRequest(HttpContext context, GatewayPathRuleOptions pathRule)
        {
            if (pathRule != null && pathRule.TreatAsStreaming)
            {
                return true;
            }

            if (context == null || context.Request == null)
            {
                return false;
            }

            var accept = context.Request.Headers["Accept"] ?? string.Empty;
            if (accept.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var transport = (context.Request["transport"] ?? string.Empty).Trim();
            return string.Equals(transport, "serverSentEvents", StringComparison.OrdinalIgnoreCase);
        }
    }
}
