using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GatewayDemo.Services;
using Xunit;

namespace GatewayDemo.Tests;

public sealed class GatewayFlowTests
{
    [Fact]
    public async Task PublicSurface_DoesNotExposeAdminLogin()
    {
        await using var upstream = await FakeUpstreamServer.StartAsync();
        await using var fixture = await GatewayTestFixture.CreateAsync(upstream.BaseUrl);

        using var publicClient = fixture.Host.CreatePublicClient();
        var response = await publicClient.GetAsync("/admin/login");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApprovedDevice_CanReachUpstream_WithBrowserCredential_AndAppCredential()
    {
        await using var upstream = await FakeUpstreamServer.StartAsync();
        await using var fixture = await GatewayTestFixture.CreateAsync(upstream.BaseUrl);

        using var publicClient = fixture.Host.CreatePublicClient();
        using var adminClient = fixture.Host.CreateAdminClient();
        using var appClient = fixture.Host.CreatePublicClient();

        await SubmitAccessRequestAsync(publicClient, "/proxy/erp-main/portal", ["erp-main"]);
        await LoginAdminAsync(adminClient);
        await ApproveLatestPendingRequestAsync(adminClient, ["erp-main"], "approved-by-test");

        var allowedResponse = await publicClient.GetAsync("/proxy/erp-main/portal");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        var allowedHtml = await allowedResponse.Content.ReadAsStringAsync();
        Assert.Contains("backend-ok", allowedHtml);
        Assert.Contains("erp-main", allowedHtml);

        var credential = await IssueLatestAppCredentialAsync(adminClient);
        var appResponse = await SendSignedAppRequestAsync(appClient, credential, HttpMethod.Get, "/proxy/erp-main/portal");

        Assert.Equal(HttpStatusCode.OK, appResponse.StatusCode);
        var appHtml = await appResponse.Content.ReadAsStringAsync();
        Assert.Contains("backend-ok", appHtml);
        Assert.Contains("erp-main", appHtml);
    }

    [Fact]
    public async Task UnifiedAuthorization_AllowsSameDeviceAcrossMultipleSites()
    {
        await using var upstream = await FakeUpstreamServer.StartAsync();
        await using var fixture = await GatewayTestFixture.CreateAsync(upstream.BaseUrl);

        using var publicClient = fixture.Host.CreatePublicClient();
        using var adminClient = fixture.Host.CreateAdminClient();

        await SubmitAccessRequestAsync(publicClient, "/proxy/erp-main/portal", ["erp-main", "wms-east"]);
        await LoginAdminAsync(adminClient);
        await ApproveLatestPendingRequestAsync(adminClient, ["erp-main", "wms-east"], "multi-site-approved");

        var erpResponse = await publicClient.GetAsync("/proxy/erp-main/portal");
        Assert.Equal(HttpStatusCode.OK, erpResponse.StatusCode);

        var wmsResponse = await publicClient.GetAsync("/proxy/wms-east/portal");
        Assert.Equal(HttpStatusCode.OK, wmsResponse.StatusCode);
        var wmsHtml = await wmsResponse.Content.ReadAsStringAsync();
        Assert.Contains("backend-ok", wmsHtml);
        Assert.Contains("wms-east", wmsHtml);
    }

    [Fact]
    public async Task BrowserSignalChange_RequiresReapproval_BeforeProxyingAgain()
    {
        await using var upstream = await FakeUpstreamServer.StartAsync();
        await using var fixture = await GatewayTestFixture.CreateAsync(upstream.BaseUrl);

        using var publicClient = fixture.Host.CreatePublicClient();
        using var adminClient = fixture.Host.CreateAdminClient();

        publicClient.DefaultRequestHeaders.UserAgent.ParseAdd("GatewayBrowser/1.0");

        await SubmitAccessRequestAsync(publicClient, "/proxy/erp-main/portal", ["erp-main"]);
        await LoginAdminAsync(adminClient);
        await ApproveLatestPendingRequestAsync(adminClient, ["erp-main"], "first-approval");

        var firstAllowedResponse = await publicClient.GetAsync("/proxy/erp-main/portal");
        Assert.Equal(HttpStatusCode.OK, firstAllowedResponse.StatusCode);

        publicClient.DefaultRequestHeaders.UserAgent.Clear();
        publicClient.DefaultRequestHeaders.UserAgent.ParseAdd("GatewayBrowser/2.0");

        var challengedResponse = await publicClient.GetAsync("/proxy/erp-main/portal");
        Assert.Equal(HttpStatusCode.Redirect, challengedResponse.StatusCode);
        Assert.StartsWith("/gateway?target=", challengedResponse.Headers.Location?.OriginalString, StringComparison.Ordinal);

        await SubmitAccessRequestAsync(publicClient, "/proxy/erp-main/portal", ["erp-main"]);
        await ApproveLatestPendingRequestAsync(adminClient, ["erp-main"], "reapproved-after-signal-change");

        var reapprovedResponse = await publicClient.GetAsync("/proxy/erp-main/portal");
        Assert.Equal(HttpStatusCode.OK, reapprovedResponse.StatusCode);
    }

    [Fact]
    public async Task RevokedAuthorization_InvalidatesOldAppCredential_EvenAfterReapproval()
    {
        await using var upstream = await FakeUpstreamServer.StartAsync();
        await using var fixture = await GatewayTestFixture.CreateAsync(upstream.BaseUrl);

        using var publicClient = fixture.Host.CreatePublicClient();
        using var adminClient = fixture.Host.CreateAdminClient();
        using var appClient = fixture.Host.CreatePublicClient();

        await SubmitAccessRequestAsync(publicClient, "/proxy/erp-main/portal", ["erp-main"]);
        await LoginAdminAsync(adminClient);
        await ApproveLatestPendingRequestAsync(adminClient, ["erp-main"], "approved-before-revoke");

        var credential = await IssueLatestAppCredentialAsync(adminClient);

        var beforeRevokeResponse = await SendSignedAppRequestAsync(appClient, credential, HttpMethod.Get, "/proxy/erp-main/portal");
        Assert.Equal(HttpStatusCode.OK, beforeRevokeResponse.StatusCode);

        await RevokeLatestAuthorizationAsync(adminClient);

        var revokedCredentialResponse = await SendSignedAppRequestAsync(appClient, credential, HttpMethod.Get, "/proxy/erp-main/portal");
        Assert.Equal(HttpStatusCode.Unauthorized, revokedCredentialResponse.StatusCode);

        await SubmitAccessRequestAsync(publicClient, "/proxy/erp-main/portal", ["erp-main"]);
        await ApproveLatestPendingRequestAsync(adminClient, ["erp-main"], "reapproved-after-revoke");

        var oldCredentialAfterReapproval = await SendSignedAppRequestAsync(appClient, credential, HttpMethod.Get, "/proxy/erp-main/portal");
        Assert.Equal(HttpStatusCode.Unauthorized, oldCredentialAfterReapproval.StatusCode);
    }

    [Fact]
    public async Task AppCredential_RejectsReplay_AndRequiresSignedRequestBody()
    {
        await using var upstream = await FakeUpstreamServer.StartAsync();
        await using var fixture = await GatewayTestFixture.CreateAsync(upstream.BaseUrl);

        using var publicClient = fixture.Host.CreatePublicClient();
        using var adminClient = fixture.Host.CreateAdminClient();
        using var appClient = fixture.Host.CreatePublicClient();

        await SubmitAccessRequestAsync(publicClient, "/proxy/erp-main/portal", ["erp-main"]);
        await LoginAdminAsync(adminClient);
        await ApproveLatestPendingRequestAsync(adminClient, ["erp-main"], "approved-for-app-post");

        var credential = await IssueLatestAppCredentialAsync(adminClient);
        const string targetPath = "/proxy/erp-main/api/submit?mode=full";
        var bodyBytes = Encoding.UTF8.GetBytes("""{"action":"submit","amount":42}""");
        var signedHeaders = CreateAppSignatureHeaders(credential, HttpMethod.Post, targetPath, bodyBytes);

        using var validRequest = CreateSignedAppRequest(HttpMethod.Post, targetPath, signedHeaders, bodyBytes, "application/json");
        var validResponse = await appClient.SendAsync(validRequest);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);

        await using (var validStream = await validResponse.Content.ReadAsStreamAsync())
        {
            using var document = await JsonDocument.ParseAsync(validStream);
            Assert.Equal("erp-main", document.RootElement.GetProperty("siteKey").GetString());
            Assert.Equal("""{"action":"submit","amount":42}""", document.RootElement.GetProperty("body").GetString());
        }

        using var replayRequest = CreateSignedAppRequest(HttpMethod.Post, targetPath, signedHeaders, bodyBytes, "application/json");
        var replayResponse = await appClient.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        var tamperedBodyBytes = Encoding.UTF8.GetBytes("""{"action":"submit","amount":99}""");
        var tamperedHeaders = CreateAppSignatureHeaders(credential, HttpMethod.Post, targetPath, bodyBytes);
        using var tamperedRequest = CreateSignedAppRequest(HttpMethod.Post, targetPath, tamperedHeaders, tamperedBodyBytes, "application/json");
        var tamperedResponse = await appClient.SendAsync(tamperedRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, tamperedResponse.StatusCode);
    }

    private static async Task SubmitAccessRequestAsync(HttpClient publicClient, string targetPath, IEnumerable<string> siteKeys)
    {
        var blockedResponse = await publicClient.GetAsync(targetPath);
        Assert.Equal(HttpStatusCode.Redirect, blockedResponse.StatusCode);
        Assert.StartsWith("/gateway?target=", blockedResponse.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var requestPost = await publicClient.PostAsync(
            "/gateway/request-access",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["targetPath"] = targetPath,
                ["companyName"] = "Contoso",
                ["applicantName"] = "Wang",
                ["phone"] = "13800000000",
                ["reason"] = "integration-test",
                ["siteKeys"] = string.Join(",", siteKeys),
            }));

        Assert.Equal(HttpStatusCode.Redirect, requestPost.StatusCode);
    }

    private static async Task LoginAdminAsync(HttpClient adminClient)
    {
        var adminLoginGet = await adminClient.GetAsync("/admin/login");
        Assert.Equal(HttpStatusCode.OK, adminLoginGet.StatusCode);

        var loginPost = await adminClient.PostAsync(
            "/admin/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = GatewayTestFixture.AdminUsername,
                ["password"] = GatewayTestFixture.AdminPassword,
            }));

        Assert.Equal(HttpStatusCode.Redirect, loginPost.StatusCode);
    }

    private static async Task ApproveLatestPendingRequestAsync(HttpClient adminClient, IEnumerable<string> siteKeys, string note)
    {
        var dashboardHtml = await adminClient.GetStringAsync("/admin");
        var requestId = ExtractGuid(dashboardHtml, "/admin/requests/([0-9a-fA-F-]+)/approve");

        var approvePost = await adminClient.PostAsync(
            $"/admin/requests/{requestId}/approve",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["siteKeys"] = string.Join(",", siteKeys),
                ["note"] = note,
            }));

        Assert.Equal(HttpStatusCode.Redirect, approvePost.StatusCode);
    }

    private static async Task RevokeLatestAuthorizationAsync(HttpClient adminClient)
    {
        var dashboardHtml = await adminClient.GetStringAsync("/admin");
        var authorizationId = ExtractGuid(dashboardHtml, "/admin/authorizations/([0-9a-fA-F-]+)/revoke");

        var revokePost = await adminClient.PostAsync($"/admin/authorizations/{authorizationId}/revoke", content: null);
        Assert.Equal(HttpStatusCode.Redirect, revokePost.StatusCode);
    }

    private static async Task<AppCredential> IssueLatestAppCredentialAsync(HttpClient adminClient)
    {
        var dashboardHtml = await adminClient.GetStringAsync("/admin");
        var authorizationId = ExtractGuid(dashboardHtml, "/admin/authorizations/([0-9a-fA-F-]+)/issue-app-credential");

        var issueResponse = await adminClient.PostAsync($"/admin/authorizations/{authorizationId}/issue-app-credential", content: null);
        Assert.Equal(HttpStatusCode.OK, issueResponse.StatusCode);

        var credentialHtml = await issueResponse.Content.ReadAsStringAsync();
        return new AppCredential(
            ExtractText(credentialHtml, "id=\"app-key\">([^<]+)</code>"),
            ExtractText(credentialHtml, "id=\"app-secret\">([^<]+)</code>"));
    }

    private static async Task<HttpResponseMessage> SendSignedAppRequestAsync(
        HttpClient appClient,
        AppCredential credential,
        HttpMethod method,
        string targetPath,
        byte[]? bodyBytes = null,
        string? contentType = null)
    {
        var normalizedBodyBytes = bodyBytes ?? [];
        var signedHeaders = CreateAppSignatureHeaders(credential, method, targetPath, normalizedBodyBytes);
        using var request = CreateSignedAppRequest(method, targetPath, signedHeaders, normalizedBodyBytes, contentType);
        return await appClient.SendAsync(request);
    }

    private static AppSignatureHeaders CreateAppSignatureHeaders(
        AppCredential credential,
        HttpMethod method,
        string targetPath,
        byte[] bodyBytes,
        long? timestamp = null,
        string? nonce = null)
    {
        var normalizedTimestamp = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var normalizedNonce = nonce ?? DeviceCredentialService.BuildNonce();
        var bodyHash = DeviceCredentialService.BuildBodyHash(bodyBytes);
        var uri = new Uri($"http://gateway.local{targetPath}", UriKind.Absolute);
        var query = uri.Query == "?" ? string.Empty : uri.Query;
        var payload = DeviceCredentialService.BuildSignaturePayload(
            normalizedTimestamp,
            normalizedNonce,
            method.Method,
            uri.AbsolutePath,
            query,
            bodyHash);
        var signature = DeviceCredentialService.BuildSignature(credential.Secret, payload);

        return new AppSignatureHeaders(
            credential.KeyId,
            normalizedTimestamp,
            normalizedNonce,
            bodyHash,
            signature);
    }

    private static HttpRequestMessage CreateSignedAppRequest(
        HttpMethod method,
        string targetPath,
        AppSignatureHeaders headers,
        byte[] bodyBytes,
        string? contentType)
    {
        var request = new HttpRequestMessage(method, targetPath);
        request.Headers.TryAddWithoutValidation(DeviceCredentialService.AppKeyHeaderName, headers.KeyId);
        request.Headers.TryAddWithoutValidation(DeviceCredentialService.AppTimestampHeaderName, headers.Timestamp.ToString());
        request.Headers.TryAddWithoutValidation(DeviceCredentialService.AppNonceHeaderName, headers.Nonce);
        request.Headers.TryAddWithoutValidation(DeviceCredentialService.AppBodyHashHeaderName, headers.BodyHash);
        request.Headers.TryAddWithoutValidation(DeviceCredentialService.AppSignatureHeaderName, headers.Signature);

        if (bodyBytes.Length > 0 || !string.IsNullOrWhiteSpace(contentType))
        {
            request.Content = new ByteArrayContent(bodyBytes);
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
        }

        return request;
    }

    private static string ExtractGuid(string input, string pattern)
    {
        var match = Regex.Match(input, pattern);
        Assert.True(match.Success, $"Pattern not found: {pattern}");
        return match.Groups[1].Value;
    }

    private static string ExtractText(string input, string pattern)
    {
        var match = Regex.Match(input, pattern);
        Assert.True(match.Success, $"Pattern not found: {pattern}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private sealed record AppCredential(string KeyId, string Secret);

    private sealed record AppSignatureHeaders(
        string KeyId,
        long Timestamp,
        string Nonce,
        string BodyHash,
        string Signature);
}

internal sealed class GatewayTestFixture : IAsyncDisposable
{
    public const string AdminUsername = "gateway-admin";
    public const string AdminPassword = "GatewayDemo!2026";

    private readonly string tempDirectory;

    private GatewayTestFixture(string tempDirectory, GatewayProcessHost host)
    {
        this.tempDirectory = tempDirectory;
        Host = host;
    }

    public GatewayProcessHost Host { get; }

    public static async Task<GatewayTestFixture> CreateAsync(string upstreamBaseUrl)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"gateway-demo-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        var hasher = new AdminPasswordHasher();
        var sqlitePath = Path.Combine(tempDirectory, "gateway-demo-tests.db");
        var keyRingPath = Path.Combine(tempDirectory, "dp-keys");
        Directory.CreateDirectory(keyRingPath);

        var host = await GatewayProcessHost.StartAsync(
            upstreamBaseUrl,
            sqlitePath,
            keyRingPath,
            hasher.HashPassword(AdminPassword));
        return new GatewayTestFixture(tempDirectory, host);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();

        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
