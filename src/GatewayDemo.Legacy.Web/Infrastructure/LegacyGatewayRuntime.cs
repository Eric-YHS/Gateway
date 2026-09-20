using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Web;
using GatewayDemo.Legacy.Core.Models;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    public sealed class LegacyGatewayRuntime
    {
        private static readonly object SyncRoot = new object();
        private static volatile LegacyGatewayRuntime current;

        private readonly ConcurrentDictionary<string, AuthorizationCacheEntry> authorizationCache =
            new ConcurrentDictionary<string, AuthorizationCacheEntry>(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan AuthorizationCacheTtl = TimeSpan.FromSeconds(30);

        private LegacyGatewayRuntime(LegacyGatewayConfiguration configuration)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Configuration = configuration;
            PasswordHasher = new AdminPasswordHasher();
            Renderer = new GatewayPageRenderer(configuration);
            ReverseProxy = new ManagedReverseProxy(configuration);
            GatewayDatabaseInitializer.EnsureCreated(configuration);
        }

        public LegacyGatewayConfiguration Configuration { get; private set; }

        public AdminPasswordHasher PasswordHasher { get; private set; }

        public GatewayPageRenderer Renderer { get; private set; }

        public ManagedReverseProxy ReverseProxy { get; private set; }

        internal GatewayDeviceAuthorization GetCachedAuthorization(string deviceId, Func<GatewayDeviceAuthorization> factory)
        {
            var now = DateTime.UtcNow;
            AuthorizationCacheEntry entry;
            if (authorizationCache.TryGetValue(deviceId, out entry) && (now - entry.Timestamp) < AuthorizationCacheTtl)
            {
                return entry.Authorization;
            }

            var authorization = factory();
            if (authorization != null)
            {
                authorizationCache[deviceId] = new AuthorizationCacheEntry(authorization, now);
            }
            else
            {
                InvalidateAuthorizationCache(deviceId);
            }

            return authorization;
        }

        internal void InvalidateAuthorizationCache(string deviceId)
        {
            AuthorizationCacheEntry entry;
            authorizationCache.TryRemove(deviceId, out entry);
        }

        internal void ClearAuthorizationCache()
        {
            authorizationCache.Clear();
        }

        private void RefreshConfigurationIfNeeded()
        {
            try
            {
                if (Configuration.ReloadSitesIfChanged())
                {
                    ClearAuthorizationCache();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(
                    "GatewayDemo: failed to reload site configuration: {0}", ex.Message);
            }
        }

        private sealed class AuthorizationCacheEntry
        {
            public readonly GatewayDeviceAuthorization Authorization;
            public readonly DateTime Timestamp;

            public AuthorizationCacheEntry(GatewayDeviceAuthorization authorization, DateTime timestamp)
            {
                Authorization = authorization;
                Timestamp = timestamp;
            }
        }

        public static LegacyGatewayRuntime Current
        {
            get
            {
                if (current == null)
                {
                    lock (SyncRoot)
                    {
                        if (current == null)
                        {
                            var contentRootPath = HttpRuntime.AppDomainAppPath;
                            if (string.IsNullOrWhiteSpace(contentRootPath))
                            {
                                contentRootPath = AppDomain.CurrentDomain.BaseDirectory;
                            }

                            if (!Directory.Exists(contentRootPath))
                            {
                                Directory.CreateDirectory(contentRootPath);
                            }

                            current = new LegacyGatewayRuntime(LegacyGatewayConfiguration.Load(contentRootPath));
                        }
                    }
                }

                current.RefreshConfigurationIfNeeded();
                return current;
            }
        }

        public LegacyGatewayRepository CreateRepository()
        {
            return new LegacyGatewayRepository(Configuration);
        }

        public BrowserDeviceService CreateBrowserDeviceService()
        {
            return new BrowserDeviceService(Configuration, CreateRepository());
        }

        public DeviceCredentialService CreateDeviceCredentialService()
        {
            var repository = CreateRepository();
            return new DeviceCredentialService(
                Configuration,
                repository,
                new BrowserDeviceService(Configuration, repository));
        }
    }
}
