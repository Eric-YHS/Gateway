using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;
using GatewayDemo.Legacy.Core.Models;
using GatewayDemo.Legacy.Core.Options;
using GatewayDemo.Legacy.Core.Services;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    public sealed class LegacyAppRequestDescriptor
    {
        public bool IsLegacyApp { get; set; }
        public bool IsLoginRequest { get; set; }
        public bool IsServiceRequest { get; set; }
        public bool IsBootstrapRequest { get; set; }
        public string BootstrapAction { get; set; }
        public string IdentityHash { get; set; }
        public string IdentitySummary { get; set; }
        public string ApplicantName { get; set; }
        public string CompanyName { get; set; }
        public string ContactHint { get; set; }
        public string UrlBefore { get; set; }
        public string CompanyId { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string DataCenterId { get; set; }
        public string DeviceImei { get; set; }
        public string MobileUserNum { get; set; }
        public string ReviewSummary { get; set; }
        public string SessionKey { get; set; }
        public string SessionKeyHash { get; set; }
        public IList<string> IdentityHashCandidates { get; private set; }

        public LegacyAppRequestDescriptor()
        {
            IdentityHash = string.Empty;
            IdentitySummary = string.Empty;
            BootstrapAction = string.Empty;
            ApplicantName = string.Empty;
            CompanyName = string.Empty;
            ContactHint = string.Empty;
            UrlBefore = string.Empty;
            CompanyId = string.Empty;
            UserId = string.Empty;
            UserName = string.Empty;
            DataCenterId = string.Empty;
            DeviceImei = string.Empty;
            MobileUserNum = string.Empty;
            ReviewSummary = string.Empty;
            SessionKey = string.Empty;
            SessionKeyHash = string.Empty;
            IdentityHashCandidates = new List<string>();
        }

        public bool HasIdentity
        {
            get { return !string.IsNullOrWhiteSpace(IdentityHash); }
        }

        public bool HasSessionKey
        {
            get { return !string.IsNullOrWhiteSpace(SessionKeyHash); }
        }
    }

    public sealed class DeviceCredentialService
    {
        public const string AppKeyHeaderName = "X-Gateway-Device-Key";
        public const string AppTimestampHeaderName = "X-Gateway-Device-Timestamp";
        public const string AppNonceHeaderName = "X-Gateway-Device-Nonce";
        public const string AppBodyHashHeaderName = "X-Gateway-Device-Body-SHA256";
        public const string AppSignatureHeaderName = "X-Gateway-Device-Signature";

        private const string LegacyAppDescriptorContextKey = "__Gateway.LegacyAppDescriptor";
        private static readonly TimeSpan AppSignatureWindow = TimeSpan.FromMinutes(5);

        // 见 ResolveLegacyAppContext 里的调用点注释：用于把"同一设备、字段不完整"的相邻请求
        // 在极短时间窗口内合并到同一个 DeviceId，而不是各自铸造新设备。键为
        // ClientIp|CompanyId|UserId|DataCenterId，只在至少两个账号字段非空时才建立，且只在
        // AccountScopedReuseWindow 时间窗口内生效——一旦窗口过期，后续请求只能靠双方各自的
        // IdentityHashCandidates 是否已经在数据库里重叠来匹配，不会退化成永久性的纯账号匹配。
        private static readonly ConcurrentDictionary<string, RecentLegacyAppDeviceEntry> RecentAccountScopedDevices =
            new ConcurrentDictionary<string, RecentLegacyAppDeviceEntry>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, object> RecentAccountScopedLocks =
            new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        private static readonly TimeSpan AccountScopedReuseWindow = TimeSpan.FromSeconds(10);

        private readonly LegacyGatewayConfiguration configuration;
        private readonly LegacyGatewayRepository repository;
        private readonly BrowserDeviceService browserDeviceService;
        private readonly JavaScriptSerializer serializer;

        public DeviceCredentialService(
            LegacyGatewayConfiguration configuration,
            LegacyGatewayRepository repository,
            BrowserDeviceService browserDeviceService)
        {
            this.configuration = configuration;
            this.repository = repository;
            this.browserDeviceService = browserDeviceService;
            serializer = new JavaScriptSerializer();
        }

        public DeviceContext GetOrCreateContext(HttpContext context)
        {
            if (HasAnyAppHeaders(context.Request))
            {
                return ResolveSignedAppContext(context);
            }

            var webViewContext = ResolveWebViewHandoffContext(context);
            if (webViewContext != null)
            {
                return webViewContext;
            }

            var legacyAppRequest = DescribeLegacyAppRequest(context);
            if (legacyAppRequest.IsLegacyApp)
            {
                if (legacyAppRequest.HasIdentity || legacyAppRequest.HasSessionKey)
                {
                    return ResolveLegacyAppContext(context, legacyAppRequest);
                }

                return GetBrowserOrRejectNativeApp(context);
            }

            return GetBrowserOrRejectNativeApp(context);
        }

        private DeviceContext ResolveWebViewHandoffContext(HttpContext context)
        {
            if (context == null || context.Request == null || configuration == null || configuration.Gateway == null
                || !configuration.Gateway.WebViewHandoffEnabled)
            {
                return null;
            }

            var cookieName = configuration.Gateway.WebViewHandoffCookieName;
            if (string.IsNullOrWhiteSpace(cookieName))
            {
                return null;
            }

            var cookie = context.Request.Cookies[cookieName];
            if (cookie == null || string.IsNullOrWhiteSpace(cookie.Value))
            {
                return null;
            }

            var siteKey = Convert.ToString(context.Items[ProxyRequestModule.ResolvedSiteKeyContextKey] ?? string.Empty).Trim();
            var site = string.IsNullOrWhiteSpace(siteKey)
                ? configuration.Gateway.FindSiteByConfiguredHost(GatewayRequestContext.GetExternalHost(context, configuration.Gateway))
                : configuration.Gateway.FindSite(siteKey);
            if (site == null && string.IsNullOrWhiteSpace(siteKey) && configuration.Gateway.Sites.Count == 1)
            {
                site = configuration.Gateway.Sites[0];
            }
            if (site == null)
            {
                return null;
            }

            var hostBinding = NormalizeHostBinding(GatewayRequestContext.GetExternalHost(context, configuration.Gateway));
            var credential = repository.GetActiveWebViewCredential(
                ComputeHash(cookie.Value),
                site.Key,
                hostBinding,
                DateTime.UtcNow);
            if (credential == null)
            {
                return null;
            }

            var device = repository.GetManagedDeviceByDeviceId(credential.DeviceId);
            if (device == null)
            {
                return null;
            }

            return new DeviceContext
            {
                DeviceId = device.DeviceId,
                DeviceCode = device.DeviceCode,
                SignalHash = device.SignalHash,
                UserAgent = string.IsNullOrWhiteSpace(context.Request.UserAgent) ? "WebView" : context.Request.UserAgent,
                ClientIp = ClientIpResolver.GetClientIp(context, configuration.Gateway),
                CredentialChannel = "webview-handoff-cookie",
                TrustState = device.TrustState,
                ChallengeReason = device.ChallengeReason,
                HasAppCredential = device.HasAppCredential,
                AppCredentialKey = string.Empty
            };
        }

        internal string ResolveWebViewHostBinding(HttpContext context)
        {
            return NormalizeHostBinding(GatewayRequestContext.GetExternalHost(context, configuration.Gateway));
        }

        private static string NormalizeHostBinding(string value)
        {
            return (value ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();
        }

        private DeviceContext GetBrowserOrRejectNativeApp(HttpContext context)
        {
            if (MobileClientDetector.IsNativeAppHealthProbeRequest(context == null ? null : context.Request))
            {
                throw new DeviceCredentialException(
                    "原生 APP 设备不可用，请先完成 APP 到 WebView 的安全会话交接。",
                    401);
            }

            return browserDeviceService.GetOrCreateContext(context);
        }

        private static bool IsBrowserDocumentNavigation(HttpRequest request)
        {
            if (request == null)
            {
                return false;
            }

            var fetchDest = (request.Headers["Sec-Fetch-Dest"] ?? string.Empty).Trim();
            if (string.Equals(fetchDest, "document", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fetchDest, "iframe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fetchDest, "frame", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var accept = request.Headers["Accept"] ?? string.Empty;
            return accept.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0
                && !string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.HttpMethod, "PATCH", StringComparison.OrdinalIgnoreCase);
        }

        public LegacyAppRequestDescriptor DescribeLegacyAppRequest(HttpContext context)
        {
            if (context == null)
            {
                return new LegacyAppRequestDescriptor();
            }

            var cached = context.Items[LegacyAppDescriptorContextKey] as LegacyAppRequestDescriptor;
            if (cached != null)
            {
                return cached;
            }

            var descriptor = BuildLegacyAppRequestDescriptor(context);
            context.Items[LegacyAppDescriptorContextKey] = descriptor;
            return descriptor;
        }

        public bool IsLegacyAppContext(DeviceContext device)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.CredentialChannel))
            {
                return false;
            }

            return device.CredentialChannel.StartsWith("legacy-app", StringComparison.OrdinalIgnoreCase);
        }

        public bool ShouldAllowAnonymousLegacyBootstrapRequest(HttpContext context)
        {
            var descriptor = DescribeLegacyAppRequest(context);
            return descriptor.IsBootstrapRequest;
        }

        public bool ShouldCaptureLegacyAppLoginResponse(HttpContext context, DeviceContext device, string contentType)
        {
            if (!IsLegacyAppContext(device))
            {
                return false;
            }

            var descriptor = DescribeLegacyAppRequest(context);
            if (!descriptor.IsLegacyApp || !descriptor.IsLoginRequest)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(contentType))
            {
                return true;
            }

            var normalized = contentType.ToLowerInvariant();
            return normalized.Contains("application/json")
                || normalized.Contains("text/json")
                || normalized.Contains("text/plain");
        }

        public void CaptureLegacyAppLoginSession(HttpContext context, DeviceContext device, string responseBody)
        {
            if (!ShouldCaptureLegacyAppLoginResponse(context, device, "application/json")
                || string.IsNullOrWhiteSpace(responseBody))
            {
                return;
            }

            IDictionary<string, object> payload;
            if (!TryDeserializeObject(responseBody, out payload))
            {
                return;
            }

            var loginStatus = CoerceToString(GetNestedValue(payload, "LoginStatus"));
            var profile = ResolveLegacyAppProfile(context);
            var sessionKey = FirstNonEmpty(
                ReadJsonPathValues(payload, profile.SessionKeyJsonPaths),
                CoerceToString(GetNestedValue(payload, "SessionKey")),
                CoerceToString(GetNestedValue(payload, "UserToken")),
                CoerceToString(GetNestedValue(payload, "data", "SessionKey")),
                CoerceToString(GetNestedValue(payload, "data", "UserToken")));

            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(loginStatus)
                && !string.Equals(loginStatus, "1", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(loginStatus, "8", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            repository.SaveLegacyAppSession(device.DeviceId, ComputeHash(sessionKey), BuildSessionPreview(sessionKey));
        }

        public IssuedAppCredential IssueAppCredential(string deviceId)
        {
            var device = repository.GetManagedDeviceByDeviceId(deviceId);
            if (device == null)
            {
                throw new InvalidOperationException("未找到对应的受管设备。");
            }

            var credentialKey = "app_" + Base64UrlEncode(RandomBytes(9));
            var credentialSecret = Base64UrlEncode(RandomBytes(32));
            var siteKey = configuration.Gateway.Sites.Count == 0
                ? "site"
                : configuration.Gateway.Sites[0].Key;
            var exampleTimestamp = ToUnixTimeSeconds(DateTime.UtcNow);
            var exampleNonce = BuildNonce();
            var exampleBodyHash = BuildBodyHash(new byte[0]);
            var examplePayload = BuildSignaturePayload(
                exampleTimestamp,
                exampleNonce,
                "GET",
                configuration.Gateway.ProxyBasePath + "/" + siteKey + "/api/ping",
                string.Empty,
                exampleBodyHash);
            var exampleSignature = BuildSignature(credentialSecret, examplePayload);

            repository.SaveAppCredential(
                device.DeviceId,
                credentialKey,
                ProtectSecret(credentialSecret));

            return new IssuedAppCredential
            {
                DeviceCode = device.DeviceCode,
                CredentialKey = credentialKey,
                CredentialSecret = credentialSecret,
                ExampleTimestamp = exampleTimestamp,
                ExampleNonce = exampleNonce,
                ExampleBodyHash = exampleBodyHash,
                ExamplePayload = examplePayload,
                ExampleSignature = exampleSignature
            };
        }

        private DeviceContext ResolveSignedAppContext(HttpContext context)
        {
            var request = context.Request;
            var credentialKey = (request.Headers[AppKeyHeaderName] ?? string.Empty).Trim();
            var timestampText = (request.Headers[AppTimestampHeaderName] ?? string.Empty).Trim();
            var nonce = (request.Headers[AppNonceHeaderName] ?? string.Empty).Trim();
            var bodyHash = (request.Headers[AppBodyHashHeaderName] ?? string.Empty).Trim();
            var signature = (request.Headers[AppSignatureHeaderName] ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(credentialKey)
                || string.IsNullOrWhiteSpace(timestampText)
                || string.IsNullOrWhiteSpace(nonce)
                || string.IsNullOrWhiteSpace(bodyHash)
                || string.IsNullOrWhiteSpace(signature))
            {
                throw new DeviceCredentialException("APP 设备凭据请求头不完整。", 401);
            }

            long unixSeconds;
            if (!long.TryParse(timestampText, out unixSeconds))
            {
                throw new DeviceCredentialException("APP 设备时间戳格式不正确。", 401);
            }

            var timestampUtc = FromUnixTimeSeconds(unixSeconds);
            var nowUtc = DateTime.UtcNow;
            var drift = nowUtc > timestampUtc ? nowUtc - timestampUtc : timestampUtc - nowUtc;
            if (drift > AppSignatureWindow)
            {
                throw new DeviceCredentialException("APP 设备签名已过期。", 401);
            }

            var appCredential = repository.GetAppCredentialByKey(credentialKey);
            if (appCredential == null)
            {
                throw new DeviceCredentialException("APP 设备尚未登记。", 401);
            }

            if (appCredential.RevokedAtUtc.HasValue)
            {
                throw new DeviceCredentialException(
                    "移动 APP 设备授权已撤销，请重新申请授权；审批通过后请重新登录。",
                    403);
            }

            var device = repository.GetManagedDeviceByDeviceId(appCredential.DeviceId);
            if (device == null || !device.HasAppCredential)
            {
                throw new DeviceCredentialException("APP 设备凭据已失效。", 401);
            }

            var actualBodyHash = ComputeBodyHash(request);
            if (!SecureEquals(bodyHash, actualBodyHash))
            {
                throw new DeviceCredentialException("APP 请求体哈希校验失败。", 401);
            }

            string secret;
            try
            {
                secret = UnprotectSecret(appCredential.ProtectedSecret);
            }
            catch (CryptographicException)
            {
                throw new DeviceCredentialException("APP Secret 解密失败，请重新签发凭据。", 401);
            }

            var payload = BuildSignaturePayload(
                unixSeconds,
                nonce,
                request.HttpMethod,
                request.Path,
                request.Url == null ? string.Empty : request.Url.Query,
                bodyHash);
            var expectedSignature = BuildSignature(secret, payload);
            if (!SecureEquals(signature, expectedSignature))
            {
                throw new DeviceCredentialException("APP 设备签名校验失败。", 401);
            }

            if (!repository.TryConsumeAppNonce(
                credentialKey,
                ComputeHash(nonce),
                nowUtc,
                nowUtc.Add(AppSignatureWindow)))
            {
                throw new DeviceCredentialException("APP 请求疑似重放。", 401);
            }

            var clientIp = ClientIpResolver.GetClientIp(context, configuration.Gateway);
            var userAgent = string.IsNullOrWhiteSpace(request.UserAgent) ? "未知" : request.UserAgent;
            repository.TouchManagedDevice(device.DeviceId, userAgent, clientIp);

            return new DeviceContext
            {
                DeviceId = device.DeviceId,
                DeviceCode = device.DeviceCode,
                SignalHash = BuildSignalHash(context),
                UserAgent = userAgent,
                ClientIp = clientIp,
                CredentialChannel = "app-hmac",
                TrustState = device.TrustState,
                ChallengeReason = device.ChallengeReason,
                HasAppCredential = true,
                AppCredentialKey = credentialKey
            };
        }

        private DeviceContext ResolveLegacyAppContext(HttpContext context, LegacyAppRequestDescriptor descriptor)
        {
            var request = context.Request;
            var clientIp = ClientIpResolver.GetClientIp(context, configuration.Gateway);
            var profile = ResolveLegacyAppProfile(context);
            var userAgent = string.IsNullOrWhiteSpace(request.UserAgent) ? profile.AutoRequestApplicantName : request.UserAgent;

            GatewayManagedDevice device = null;
            if (descriptor.HasSessionKey && !descriptor.IsLoginRequest)
            {
                device = repository.GetManagedDeviceByLegacyAppSessionHash(descriptor.SessionKeyHash);
                if (device != null)
                {
                    repository.TouchLegacyAppSession(descriptor.SessionKeyHash);
                    if (descriptor.HasIdentity)
                    {
                        AttachLegacyAppIdentities(device.DeviceId, descriptor);
                    }
                }
            }

            if (device == null && descriptor.HasIdentity)
            {
                device = FindManagedDeviceByLegacyAppIdentity(descriptor);
                var accountScopedKey = BuildAccountScopedReuseKey(clientIp, descriptor);
                if (device == null)
                {
                    // 同一台移动设备几乎同时调用"业务接口"和"登录接口"时，两者携带的字段不完全
                    // 一致——典型情况是业务接口(如 WCFService 泛化调用)只带 CompanyId/UserId/
                    // DataCenterId，只有登录接口才带 DeviceImei/MachineId。LegacyAppIdentityPolicy
                    // 按字段位置拼接身份哈希，"带设备位"和"不带设备位"的请求算出的哈希互不相关，
                    // 会把同一台设备拆成两个 DeviceId、两条独立的接入申请记录。这里复用与
                    // BrowserDeviceService 完全一致的思路：极短窗口内、同一 ClientIp + 同一账号
                    // (CompanyId|UserId|DataCenterId)维度，直接复用刚创建的设备，而不是重新铸造；
                    // 只在时间窗口内生效，不做成永久性的纯账号匹配，避免不同真实设备共用同一企业
                    // 账号时被误合并(这正是 AllowAccountOnlyIdentityFallback 默认关闭要防的风险)。
                    device = TryReuseRecentAccountScopedDevice(accountScopedKey);
                }

                if (device == null)
                {
                    device = repository.CreateLegacyAppManagedDevice(
                        descriptor.IdentityHash,
                        descriptor.IdentitySummary,
                        BuildLegacyAppSignalHash(descriptor, request),
                        userAgent,
                        clientIp);
                }
                else
                {
                    repository.TouchLegacyAppIdentity(device.DeviceId, descriptor.IdentityHash, descriptor.IdentitySummary);
                }

                AttachLegacyAppIdentities(device.DeviceId, descriptor);
                RememberAccountScopedDevice(accountScopedKey, device.DeviceId);
            }

            if (device == null && descriptor.HasSessionKey)
            {
                device = repository.GetManagedDeviceByLegacyAppSessionHash(descriptor.SessionKeyHash);
                if (device != null)
                {
                    repository.TouchLegacyAppSession(descriptor.SessionKeyHash);
                    if (descriptor.HasIdentity)
                    {
                        AttachLegacyAppIdentities(device.DeviceId, descriptor);
                    }
                }
            }

            if (device == null)
            {
                throw new DeviceCredentialException("移动 APP 请求缺少可识别的设备标识。", 401);
            }

            repository.TouchManagedDevice(device.DeviceId, userAgent, clientIp);

            return new DeviceContext
            {
                DeviceId = device.DeviceId,
                DeviceCode = device.DeviceCode,
                SignalHash = BuildLegacyAppSignalHash(descriptor, request),
                UserAgent = userAgent,
                ClientIp = clientIp,
                CredentialChannel = descriptor.IsLoginRequest ? "legacy-app-login" : "legacy-app-session",
                TrustState = device.TrustState,
                ChallengeReason = device.ChallengeReason,
                HasAppCredential = device.HasAppCredential,
                AppCredentialKey = string.Empty
            };
        }

        // 只在(至少两个)账号字段非空时才建立复用键；空字段直接返回空字符串，调用方需要据此
        // 跳过复用/记忆逻辑，避免用一个几乎空的键把不相关的请求撞到一起。
        private static string BuildAccountScopedReuseKey(string clientIp, LegacyAppRequestDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return string.Empty;
            }

            var companyId = (descriptor.CompanyId ?? string.Empty).Trim();
            var userId = (descriptor.UserId ?? string.Empty).Trim();
            var dataCenterId = (descriptor.DataCenterId ?? string.Empty).Trim();
            var nonEmptyCount = 0;
            if (!string.IsNullOrWhiteSpace(companyId)) nonEmptyCount++;
            if (!string.IsNullOrWhiteSpace(userId)) nonEmptyCount++;
            if (!string.IsNullOrWhiteSpace(dataCenterId)) nonEmptyCount++;
            if (nonEmptyCount < 2)
            {
                return string.Empty;
            }

            return (clientIp ?? string.Empty).Trim().ToLowerInvariant()
                + "|" + companyId.ToLowerInvariant()
                + "|" + userId.ToLowerInvariant()
                + "|" + dataCenterId.ToLowerInvariant();
        }

        private GatewayManagedDevice TryReuseRecentAccountScopedDevice(string accountScopedKey)
        {
            if (string.IsNullOrWhiteSpace(accountScopedKey))
            {
                return null;
            }

            var lockObject = RecentAccountScopedLocks.GetOrAdd(accountScopedKey, delegate { return new object(); });
            lock (lockObject)
            {
                RecentLegacyAppDeviceEntry recent;
                if (!RecentAccountScopedDevices.TryGetValue(accountScopedKey, out recent)
                    || DateTime.UtcNow - recent.CreatedAtUtc > AccountScopedReuseWindow)
                {
                    return null;
                }

                return repository.GetManagedDeviceByDeviceId(recent.DeviceId);
            }
        }

        private static void RememberAccountScopedDevice(string accountScopedKey, string deviceId)
        {
            if (string.IsNullOrWhiteSpace(accountScopedKey) || string.IsNullOrWhiteSpace(deviceId))
            {
                return;
            }

            var lockObject = RecentAccountScopedLocks.GetOrAdd(accountScopedKey, delegate { return new object(); });
            lock (lockObject)
            {
                RecentAccountScopedDevices[accountScopedKey] = new RecentLegacyAppDeviceEntry(deviceId, DateTime.UtcNow);
            }
        }

        private sealed class RecentLegacyAppDeviceEntry
        {
            public readonly string DeviceId;
            public readonly DateTime CreatedAtUtc;

            public RecentLegacyAppDeviceEntry(string deviceId, DateTime createdAtUtc)
            {
                DeviceId = deviceId;
                CreatedAtUtc = createdAtUtc;
            }
        }

        private GatewayManagedDevice FindManagedDeviceByLegacyAppIdentity(LegacyAppRequestDescriptor descriptor)
        {
            foreach (var identityHash in GetIdentityHashes(descriptor))
            {
                var device = repository.GetManagedDeviceByLegacyAppIdentityHash(identityHash);
                if (device != null)
                {
                    return device;
                }
            }

            return null;
        }

        private void AttachLegacyAppIdentities(string deviceId, LegacyAppRequestDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || descriptor == null || !descriptor.HasIdentity)
            {
                return;
            }

            repository.AttachLegacyAppIdentities(deviceId, GetIdentityHashes(descriptor), descriptor.IdentitySummary);
        }

        private static IList<string> GetIdentityHashes(LegacyAppRequestDescriptor descriptor)
        {
            var hashes = new List<string>();
            if (descriptor == null)
            {
                return hashes;
            }

            AddDistinct(hashes, descriptor.IdentityHash);
            if (descriptor.IdentityHashCandidates != null)
            {
                foreach (var hash in descriptor.IdentityHashCandidates)
                {
                    AddDistinct(hashes, hash);
                }
            }

            return hashes;
        }

        private LegacyAppRequestDescriptor BuildLegacyAppRequestDescriptor(HttpContext context)
        {
            var descriptor = new LegacyAppRequestDescriptor();
            var request = context == null ? null : context.Request;
            if (request == null)
            {
                return descriptor;
            }

            var profile = ResolveLegacyAppProfile(context);
            if (profile == null || !profile.Enabled)
            {
                return descriptor;
            }

            var path = request.Path ?? string.Empty;
            var isLoginPath = LegacyAppCompatibilityPolicy.MatchesPath(path, profile.LoginPathPatterns);
            var postJson = ParsePostJson(request);
            var isServiceRequest = LegacyAppCompatibilityPolicy.MatchesPath(path, profile.ServicePathPatterns);
            var isUploadRequest = LegacyAppCompatibilityPolicy.MatchesPath(path, profile.UploadPathPatterns);
            var action = ReadLegacyValue(request, postJson, profile.ActionFields);
            var isBootstrapPath = LegacyAppCompatibilityPolicy.MatchesPath(path, profile.BootstrapPathPatterns);
            descriptor.BootstrapAction = action;
            descriptor.IsBootstrapRequest = isBootstrapPath && LegacyAppCompatibilityPolicy.IsConfiguredAction(action, profile.BootstrapActions);
            var machineId = ReadLegacyValue(request, postJson, profile.MachineIdFields);
            var deviceImei = ReadLegacyValue(request, postJson, profile.DeviceImeiFields);
            var dataCenterId = ReadLegacyValue(request, postJson, profile.DataCenterIdFields);
            var urlBefore = ReadLegacyValue(request, postJson, profile.UrlBeforeFields);
            var tenantName = ReadLegacyValue(request, postJson, profile.TenantNameFields);
            if (string.IsNullOrWhiteSpace(tenantName) && !descriptor.IsBootstrapRequest)
            {
                tenantName = ResolveTenantNameFromMappedGatewayUrl(context, dataCenterId, profile);
            }

            var companyId = FirstNonEmpty(
                tenantName,
                ReadLegacyValue(request, postJson, profile.CompanyFields));
            var userId = ReadLegacyValue(request, postJson, profile.UserIdFields);
            var userName = ReadLegacyValue(request, postJson, profile.UserNameFields);
            var mobileUserNum = ReadLegacyValue(request, postJson, profile.MobileUserNumFields);
            var appKey = ReadLegacyValue(request, postJson, profile.AppKeyFields);
            var deviceVersion = ReadLegacyValue(request, postJson, profile.DeviceVersionFields);
            var appVersion = ReadLegacyValue(request, postJson, profile.AppVersionFields);
            var appName = ReadLegacyValue(request, postJson, profile.AppNameFields);
            var deviceNo = ReadLegacyValue(request, postJson, profile.DeviceNoFields);
            var hasEquipmentObject = GetNestedValue(postJson, "Equipment") != null;

            descriptor.SessionKey = FirstNonEmpty(
                GetRequestValue(request, profile.SessionKeyFields),
                ReadJsonPathValues(postJson, profile.SessionKeyJsonPaths));
            descriptor.SessionKeyHash = string.IsNullOrWhiteSpace(descriptor.SessionKey)
                ? string.Empty
                : ComputeHash(descriptor.SessionKey);

            descriptor.ApplicantName = FirstNonEmpty(userName, userId, mobileUserNum);
            descriptor.CompanyName = FirstNonEmpty(
                companyId,
                tenantName,
                dataCenterId);
            descriptor.CompanyId = companyId;
            descriptor.UserId = userId;
            descriptor.UserName = userName;
            descriptor.DataCenterId = dataCenterId;
            descriptor.UrlBefore = urlBefore;
            descriptor.DeviceImei = deviceImei;
            descriptor.MobileUserNum = mobileUserNum;
            descriptor.ContactHint = NormalizeContactPhone(ReadLegacyValue(request, postJson, profile.ContactFields));

            var isMobileClient = MobileClientDetector.IsMobileClientRequest(request, profile);
            var hasAppIdentity = !string.IsNullOrWhiteSpace(machineId)
                || !string.IsNullOrWhiteSpace(deviceImei)
                || !string.IsNullOrWhiteSpace(appKey)
                || !string.IsNullOrWhiteSpace(appName)
                || !string.IsNullOrWhiteSpace(deviceNo);
            var hasLegacyAppIdentityContext = hasAppIdentity
                || hasEquipmentObject
                || descriptor.HasSessionKey
                || !string.IsNullOrWhiteSpace(companyId)
                || !string.IsNullOrWhiteSpace(dataCenterId)
                || !string.IsNullOrWhiteSpace(urlBefore)
                || !string.IsNullOrWhiteSpace(userId)
                || !string.IsNullOrWhiteSpace(mobileUserNum);
            var isMutatingLoginPath = isLoginPath
                && !string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase);
            var isDedicatedAppLoginPath = isLoginPath;
            var isAppLikePath = isServiceRequest
                || isUploadRequest
                || isBootstrapPath
                || descriptor.IsBootstrapRequest
                || isDedicatedAppLoginPath
                || (isMutatingLoginPath && hasLegacyAppIdentityContext);
            descriptor.IsLoginRequest = isLoginPath
                || string.Equals(GetNestedString(postJson, "Action"), "Login", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(GetNestedString(postJson, "ServiceName"), "ISecurityService", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(GetNestedString(postJson, "Action"), "Login", StringComparison.OrdinalIgnoreCase));
            descriptor.IsServiceRequest = isServiceRequest || descriptor.IsLoginRequest || descriptor.IsBootstrapRequest;
            descriptor.IsLegacyApp = descriptor.IsBootstrapRequest
                || (isMobileClient && (isAppLikePath || hasLegacyAppIdentityContext));

            if (!descriptor.IsLegacyApp)
            {
                return descriptor;
            }

            var identitySources = LegacyAppIdentityPolicy.BuildIdentitySources(
                new LegacyAppIdentitySignals
                {
                    UrlHost = NormalizeUrlHost(urlBefore),
                    CompanyId = companyId,
                    UserId = userId,
                    DataCenterId = dataCenterId,
                    DeviceImei = deviceImei,
                    MachineId = machineId,
                    AppKey = appKey,
                    AppName = appName,
                    DeviceVersion = deviceVersion,
                    AppVersion = appVersion,
                    MobileUserNum = mobileUserNum,
                    DeviceNo = deviceNo
                },
                configuration.Gateway.LegacyAppAllowAccountOnlyIdentityFallback);

            if (identitySources.Count > 0)
            {
                descriptor.IdentityHash = ComputeHash(identitySources[0]);
                descriptor.IdentitySummary = BuildIdentitySummary(machineId, deviceImei, appName, appVersion, deviceNo, companyId, userId, userName, dataCenterId, urlBefore, mobileUserNum);
                foreach (var identitySource in identitySources)
                {
                    AddDistinct(descriptor.IdentityHashCandidates, ComputeHash(identitySource));
                }
            }

            descriptor.ReviewSummary = BuildReviewSummary(descriptor);

            if (string.IsNullOrWhiteSpace(descriptor.ApplicantName))
            {
                descriptor.ApplicantName = profile.AutoRequestApplicantName;
            }

            if (string.IsNullOrWhiteSpace(descriptor.CompanyName))
            {
                descriptor.CompanyName = profile.AutoRequestApplicantName;
            }

            return descriptor;
        }

        private string ResolveTenantNameFromMappedGatewayUrl(HttpContext context, string dataCenterId, LegacyAppProfileOptions profile)
        {
            if (context == null
                || context.Request == null
                || string.IsNullOrWhiteSpace(dataCenterId)
                || profile == null
                || profile.TenantLookup == null
                || !profile.TenantLookup.Enabled)
            {
                return string.Empty;
            }

            var site = ResolveCurrentSite(context);
            if (site == null)
            {
                return string.Empty;
            }

            var mappedBaseUrl = BuildMappedGatewayBaseUrl(context, site);
            if (string.IsNullOrWhiteSpace(mappedBaseUrl))
            {
                return string.Empty;
            }

            try
            {
                Uri endpoint;
                var encodedDataCenterId = HttpUtility.UrlEncode(dataCenterId);
                var query = (profile.TenantLookup.Query ?? string.Empty)
                    .Replace("{DataCenterId}", encodedDataCenterId)
                    .Replace("{DataCenterIdRaw}", dataCenterId);
                var relativePath = (profile.TenantLookup.Path ?? string.Empty).Trim().TrimStart('/');
                if (!string.IsNullOrWhiteSpace(query))
                {
                    relativePath = relativePath + "?" + query.TrimStart('?');
                }

                if (!Uri.TryCreate(new Uri(mappedBaseUrl), relativePath, out endpoint))
                {
                    return string.Empty;
                }

                var lookupRequest = (HttpWebRequest)WebRequest.Create(endpoint);
                lookupRequest.Method = "GET";
                lookupRequest.Timeout = profile.TenantLookup.TimeoutMs;
                lookupRequest.ReadWriteTimeout = profile.TenantLookup.TimeoutMs;
                lookupRequest.AllowAutoRedirect = false;
                lookupRequest.Proxy = null;
                lookupRequest.UserAgent = context.Request.UserAgent ?? "GatewayTenantLookup";
                lookupRequest.Headers["X-Gateway-Tenant-Lookup"] = "1";

                using (var response = (HttpWebResponse)lookupRequest.GetResponse())
                using (var stream = response.GetResponseStream())
                {
                    if (response.StatusCode != HttpStatusCode.OK || stream == null)
                    {
                        return string.Empty;
                    }

                    var encoding = ResolveTextEncoding(response.ContentType);
                    using (var reader = new StreamReader(stream, encoding, true))
                    {
                        return NormalizeTenantNameResponse(reader.ReadToEnd(), profile.TenantLookup.ResponseValuePaths);
                    }
                }
            }
            catch (WebException)
            {
                return string.Empty;
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UriFormatException)
            {
                return string.Empty;
            }
        }

        private GatewaySiteOptions ResolveCurrentSite(HttpContext context)
        {
            var site = context.Items[ProxyRequestModule.ResolvedSiteContextKey] as GatewaySiteOptions;
            if (site != null)
            {
                return site;
            }

            var siteKey = Convert.ToString(context.Items[ProxyRequestModule.ResolvedSiteKeyContextKey] ?? string.Empty);
            site = configuration.Gateway.FindSite(siteKey);
            if (site != null)
            {
                return site;
            }

            var requestPath = context.Request.Path ?? string.Empty;
            siteKey = configuration.ExtractSiteKeyFromRequestPath(requestPath);
            site = configuration.Gateway.FindSite(siteKey);
            if (site != null)
            {
                return site;
            }

            var host = GatewayRequestContext.GetExternalHost(context, configuration.Gateway);
            return configuration.Gateway.FindSiteByConfiguredHost(host)
                ?? configuration.Gateway.FindLegacyAppRootSite();
        }

        private string BuildMappedGatewayBaseUrl(HttpContext context, GatewaySiteOptions site)
        {
            if (context == null || context.Request == null || site == null)
            {
                return string.Empty;
            }

            var scheme = GatewayRequestContext.GetExternalScheme(context, configuration.Gateway);
            var host = GatewayRequestContext.GetExternalHost(context, configuration.Gateway);
            if (string.IsNullOrWhiteSpace(host))
            {
                return string.Empty;
            }

            var origin = scheme + "://" + host;
            var requestPath = context.Request.Path ?? string.Empty;
            var proxyPrefix = configuration.Gateway.ProxyBasePath + "/" + site.Key;
            var useProxyPath = requestPath.StartsWith(proxyPrefix + "/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(requestPath.TrimEnd('/'), proxyPrefix, StringComparison.OrdinalIgnoreCase);
            if (useProxyPath || !UsesGatewayRootPaths(site))
            {
                return origin.TrimEnd('/') + configuration.Gateway.ProxyBasePath + "/" + Uri.EscapeDataString(site.Key) + "/";
            }

            return origin.TrimEnd('/') + "/";
        }

        private static bool UsesGatewayRootPaths(GatewaySiteOptions site)
        {
            return GatewayPathUtility.UsesGatewayRootPathsForRequest(site);
        }

        private string NormalizeTenantNameResponse(string responseText, IEnumerable<string> responseValuePaths)
        {
            var value = (responseText ?? string.Empty).Trim().Trim('\uFEFF');
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            IDictionary<string, object> payload;
            if ((value.StartsWith("{", StringComparison.Ordinal) || value.StartsWith("[", StringComparison.Ordinal))
                && TryDeserializeObject(value, out payload))
            {
                value = FirstNonEmpty(
                    ReadJsonPathValues(payload, responseValuePaths),
                    CoerceToString(GetNestedValue(payload, "tenant_name")),
                    CoerceToString(GetNestedValue(payload, "TenantName")),
                    CoerceToString(GetNestedValue(payload, "CompanyId")),
                    CoerceToString(GetNestedValue(payload, "CompanyName")),
                    CoerceToString(GetNestedValue(payload, "Data")),
                    CoerceToString(GetNestedValue(payload, "data", "tenant_name")),
                    CoerceToString(GetNestedValue(payload, "data", "TenantName")),
                    CoerceToString(GetNestedValue(payload, "data", "CompanyId")),
                    CoerceToString(GetNestedValue(payload, "data", "CompanyName")));
            }

            value = (value ?? string.Empty).Trim().Trim('"', '\'');
            if (value.IndexOf('<') >= 0 || value.IndexOf('>') >= 0 || value.Length > 200)
            {
                return string.Empty;
            }

            return value;
        }

        private static Encoding ResolveTextEncoding(string contentType)
        {
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                var marker = "charset=";
                var index = contentType.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    var charset = contentType.Substring(index + marker.Length).Trim().Trim('"', '\'', ';');
                    try
                    {
                        return Encoding.GetEncoding(charset);
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }

            return Encoding.UTF8;
        }

        private static string NormalizeContactPhone(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized) || LooksLikeDeviceIdentifier(normalized))
            {
                return string.Empty;
            }

            return normalized.Length > 30 ? normalized.Substring(0, 30) : normalized;
        }

        private static bool LooksLikeDeviceIdentifier(string value)
        {
            Guid ignored;
            if (Guid.TryParse(value, out ignored))
            {
                return true;
            }

            return value.Length >= 30 && value.IndexOf('-') >= 0;
        }

        private IDictionary<string, object> ParsePostJson(HttpRequest request)
        {
            var postJson = request.Form["postJson"];
            if (!string.IsNullOrWhiteSpace(postJson))
            {
                IDictionary<string, object> formPayload;
                return TryDeserializeObject(postJson, out formPayload) ? formPayload : null;
            }

            var contentType = (request.ContentType ?? string.Empty).ToLowerInvariant();
            var isJsonContentType = contentType.Contains("application/json");
            var isTextPlainContentType = contentType.Contains("text/plain");
            if ((!isJsonContentType && !isTextPlainContentType) || request.InputStream == null || !request.InputStream.CanRead)
            {
                return null;
            }

            try
            {
                if (request.InputStream.CanSeek)
                {
                    request.InputStream.Position = 0;
                }

                using (var reader = new System.IO.StreamReader(request.InputStream, System.Text.Encoding.UTF8, true, 4096, true))
                {
                    var rawBody = reader.ReadToEnd();
                    if (request.InputStream.CanSeek)
                    {
                        request.InputStream.Position = 0;
                    }

                    if (!string.IsNullOrWhiteSpace(rawBody))
                    {
                        // For text/plain, only parse if body looks like JSON object
                        if (isTextPlainContentType)
                        {
                            var trimmed = rawBody.TrimStart();
                            if (trimmed.Length == 0 || trimmed[0] != '{') return null;
                        }
                        IDictionary<string, object> rawPayload;
                        return TryDeserializeObject(rawBody, out rawPayload) ? rawPayload : null;
                    }
                }
            }
            catch (System.IO.IOException)
            {
            }

            return null;
        }

        private bool TryDeserializeObject(string json, out IDictionary<string, object> payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                payload = serializer.DeserializeObject(json) as IDictionary<string, object>;
                return payload != null;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string BuildIdentitySummary(string machineId, string deviceImei, string appName, string appVersion, string deviceNo, string companyId, string userId, string userName, string dataCenterId, string urlBefore, string mobileUserNum)
        {
            var builder = new StringBuilder();
            AppendSummary(builder, "CompanyId", companyId);
            AppendSummary(builder, "UserId", userId);
            AppendSummary(builder, "UserName", userName);
            AppendSummary(builder, "DataCenterId", dataCenterId);
            AppendSummary(builder, "DeviceImei", deviceImei);
            AppendSummary(builder, "UrlBefore", NormalizeUrlHost(urlBefore));
            AppendSummary(builder, "App", appName);
            AppendSummary(builder, "Version", appVersion);
            AppendSummary(builder, "Machine", machineId);
            AppendSummary(builder, "DeviceNo", deviceNo);
            return builder.ToString();
        }

        private static string BuildReviewSummary(LegacyAppRequestDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            AppendSummary(builder, "CompanyId", descriptor.CompanyId);
            AppendSummary(builder, "UserId", descriptor.UserId);
            AppendSummary(builder, "UserName", descriptor.UserName);
            AppendSummary(builder, "DataCenterId", descriptor.DataCenterId);
            AppendSummary(builder, "DeviceImei", descriptor.DeviceImei);
            AppendSummary(builder, "UrlBefore", descriptor.UrlBefore);
            return builder.ToString();
        }

        private static void AddDistinct(IList<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (var existing in values)
            {
                if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            values.Add(value);
        }

        private static void AppendSummary(StringBuilder builder, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(label).Append(":").Append(value);
        }

        private static string NormalizeUrlHost(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            Uri uri;
            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri)
                ? uri.GetLeftPart(UriPartial.Authority).TrimEnd('/')
                : value.Trim();
        }

        private static string BuildLegacyAppSignalHash(LegacyAppRequestDescriptor descriptor, HttpRequest request)
        {
            var signalSource = FirstNonEmpty(descriptor.IdentityHash, descriptor.SessionKeyHash);
            if (string.IsNullOrWhiteSpace(signalSource))
            {
                signalSource = (request.UserAgent ?? string.Empty) + "|" + (request.Path ?? string.Empty);
            }

            return ComputeHash(signalSource).Substring(0, 12);
        }

        private LegacyAppProfileOptions ResolveLegacyAppProfile(HttpContext context)
        {
            var site = ResolveCurrentSite(context);
            return LegacyAppCompatibilityPolicy.GetProfile(site);
        }

        private static string ReadLegacyValue(HttpRequest request, IDictionary<string, object> postJson, IEnumerable<string> keys)
        {
            if (keys == null)
            {
                return string.Empty;
            }

            var candidates = new List<string>();
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                candidates.Add(GetRequestValue(request, key));
                candidates.Add(GetNestedStringByPath(postJson, key));
                candidates.Add(GetNestedString(postJson, "UserToken", key));
                candidates.Add(GetNestedString(postJson, "PostData", key));
            }

            return FirstNonEmpty(candidates.ToArray());
        }

        private static string ReadLegacyValue(HttpRequest request, IDictionary<string, object> postJson, params string[] keys)
        {
            return ReadLegacyValue(request, postJson, (IEnumerable<string>)keys);
        }

        private static string ReadJsonPathValues(IDictionary<string, object> json, IEnumerable<string> paths)
        {
            if (json == null || paths == null)
            {
                return string.Empty;
            }

            var candidates = new List<string>();
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                candidates.Add(GetNestedStringByPath(json, path));
            }

            return FirstNonEmpty(candidates.ToArray());
        }

        private static string GetRequestValue(HttpRequest request, params string[] keys)
        {
            return GetRequestValue(request, (IEnumerable<string>)keys);
        }

        private static string GetRequestValue(HttpRequest request, IEnumerable<string> keys)
        {
            if (request == null || keys == null)
            {
                return string.Empty;
            }

            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var value = (request.Form[key] ?? request.QueryString[key] ?? request.Headers[key] ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string GetNestedStringByPath(IDictionary<string, object> dictionary, string path)
        {
            if (dictionary == null || string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var parts = path
                .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0
                ? string.Empty
                : CoerceToString(GetNestedValue(dictionary, parts));
        }

        private static object GetNestedValue(IDictionary<string, object> dictionary, params string[] path)
        {
            object current = dictionary;
            for (var i = 0; i < path.Length; i++)
            {
                var key = path[i];
                if (current == null)
                {
                    return null;
                }

                var nestedDictionary = current as IDictionary<string, object>;
                if (nestedDictionary != null)
                {
                    current = GetDictionaryValue(nestedDictionary, key);
                    continue;
                }

                var arrayList = current as ArrayList;
                if (arrayList != null)
                {
                    if (arrayList.Count == 0)
                    {
                        return null;
                    }

                    current = arrayList[0];
                    i--;
                    continue;
                }

                return null;
            }

            return current;
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

        private static string GetNestedString(IDictionary<string, object> dictionary, params string[] path)
        {
            return CoerceToString(GetNestedValue(dictionary, path));
        }

        private static string CoerceToString(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            var stringValue = value as string;
            if (stringValue != null)
            {
                return stringValue.Trim();
            }

            return Convert.ToString(value) ?? string.Empty;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static bool HasAnyAppHeaders(HttpRequest request)
        {
            return !string.IsNullOrWhiteSpace(request.Headers[AppKeyHeaderName])
                || !string.IsNullOrWhiteSpace(request.Headers[AppTimestampHeaderName])
                || !string.IsNullOrWhiteSpace(request.Headers[AppNonceHeaderName])
                || !string.IsNullOrWhiteSpace(request.Headers[AppBodyHashHeaderName])
                || !string.IsNullOrWhiteSpace(request.Headers[AppSignatureHeaderName]);
        }

        public static string BuildNonce()
        {
            return Base64UrlEncode(RandomBytes(18));
        }

        public static string BuildOpaqueToken()
        {
            return Base64UrlEncode(RandomBytes(32));
        }

        public static string BuildBodyHash(byte[] bodyBytes)
        {
            using (var sha256 = SHA256.Create())
            {
                return Base64UrlEncode(sha256.ComputeHash(bodyBytes ?? new byte[0]));
            }
        }

        public static string BuildSignaturePayload(
            long unixSeconds,
            string nonce,
            string method,
            string path,
            string queryString,
            string bodyHash)
        {
            var normalizedPath = string.IsNullOrWhiteSpace(path) ? "/" : path;
            var normalizedQuery = string.IsNullOrWhiteSpace(queryString) ? string.Empty : queryString;
            return string.Concat(
                unixSeconds.ToString(),
                "\n",
                nonce ?? string.Empty,
                "\n",
                (method ?? "GET").ToUpperInvariant(),
                "\n",
                normalizedPath,
                normalizedQuery,
                "\n",
                bodyHash ?? string.Empty);
        }

        public static string BuildSignature(string secret, string payload)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? string.Empty)))
            {
                return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload ?? string.Empty)));
            }
        }

        private static string ComputeBodyHash(HttpRequest request)
        {
            if (request == null || request.InputStream == null || !request.InputStream.CanRead)
            {
                return BuildBodyHash(new byte[0]);
            }

            if (request.InputStream.CanSeek)
            {
                request.InputStream.Position = 0;
            }

            using (var buffer = new System.IO.MemoryStream())
            {
                request.InputStream.CopyTo(buffer);
                if (request.InputStream.CanSeek)
                {
                    request.InputStream.Position = 0;
                }

                return BuildBodyHash(buffer.ToArray());
            }
        }

        private static string ProtectSecret(string secret)
        {
            var bytes = Encoding.UTF8.GetBytes(secret ?? string.Empty);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(protectedBytes);
        }

        private static string UnprotectSecret(string protectedSecret)
        {
            var protectedBytes = Convert.FromBase64String(protectedSecret ?? string.Empty);
            var unprotectedBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(unprotectedBytes);
        }

        private static string BuildSignalHash(HttpContext context)
        {
            var userAgent = context.Request.UserAgent ?? string.Empty;
            var language = context.Request.Headers["Accept-Language"] ?? string.Empty;
            var platform = context.Request.Headers["sec-ch-ua-platform"] ?? string.Empty;
            return ComputeHash(string.Concat(userAgent, "|", language, "|", platform)).Substring(0, 12);
        }

        private static string BuildSessionPreview(string sessionKey)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                return string.Empty;
            }

            if (sessionKey.Length <= 12)
            {
                return sessionKey;
            }

            return sessionKey.Substring(0, 8) + "..." + sessionKey.Substring(sessionKey.Length - 4);
        }

        private static byte[] RandomBytes(int length)
        {
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return bytes;
        }

        public static string ComputeHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty)));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder((bytes == null ? 0 : bytes.Length) * 2);
            if (bytes != null)
            {
                foreach (var item in bytes)
                {
                    builder.Append(item.ToString("x2"));
                }
            }

            return builder.ToString();
        }

        private static bool SecureEquals(string actual, string expected)
        {
            var actualBytes = Encoding.UTF8.GetBytes(actual ?? string.Empty);
            var expectedBytes = Encoding.UTF8.GetBytes(expected ?? string.Empty);
            if (actualBytes.Length != expectedBytes.Length)
            {
                return false;
            }

            var diff = 0;
            for (var i = 0; i < actualBytes.Length; i++)
            {
                diff |= actualBytes[i] ^ expectedBytes[i];
            }

            return diff == 0;
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes ?? new byte[0])
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static long ToUnixTimeSeconds(DateTime utc)
        {
            var normalized = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
            return (long)(normalized - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static DateTime FromUnixTimeSeconds(long unixSeconds)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSeconds);
        }
    }
}
