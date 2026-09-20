using System.Security.Cryptography;
using System.Text;
using GatewayDemo.Data;
using GatewayDemo.Models;
using GatewayDemo.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace GatewayDemo.Services;

public sealed class DeviceCredentialService(
    GatewayDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    GatewayOptions options)
{
    public const string AppKeyHeaderName = "X-Gateway-Device-Key";
    public const string AppTimestampHeaderName = "X-Gateway-Device-Timestamp";
    public const string AppNonceHeaderName = "X-Gateway-Device-Nonce";
    public const string AppBodyHashHeaderName = "X-Gateway-Device-Body-SHA256";
    public const string AppSignatureHeaderName = "X-Gateway-Device-Signature";

    private static readonly TimeSpan AppSignatureWindow = TimeSpan.FromMinutes(5);
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("GatewayDemo.DeviceCredentialService.v1");

    public async Task<DeviceContext> GetOrCreateContextAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var signalHash = BuildSignalHash(context);

        if (HasAnyAppHeaders(context.Request))
        {
            return await ResolveAppDeviceAsync(context, clientIp, userAgent, signalHash, cancellationToken);
        }

        return await ResolveBrowserDeviceAsync(context, clientIp, userAgent, signalHash, cancellationToken);
    }

    public async Task<IssuedAppCredential> IssueAppCredentialAsync(string deviceId, CancellationToken cancellationToken)
    {
        var device = await dbContext.ManagedDevices
            .FirstOrDefaultAsync(x => x.DeviceId == deviceId, cancellationToken)
            ?? throw new InvalidOperationException("Managed device not found.");

        var previousAppKeyId = device.AppKeyId;
        var appKeyId = $"app_{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(9))}";
        var appSecret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var exampleTimestamp = now.ToUnixTimeSeconds();
        var exampleNonce = BuildNonce();
        var exampleBodyHash = BuildBodyHash(Array.Empty<byte>());
        var examplePayload = BuildSignaturePayload(
            exampleTimestamp,
            exampleNonce,
            HttpMethods.Get,
            "/proxy/erp-main/portal",
            QueryString.Empty.Value,
            exampleBodyHash);
        var exampleSignature = BuildSignature(appSecret, examplePayload);

        if (!string.IsNullOrWhiteSpace(previousAppKeyId))
        {
            var previousNonces = await dbContext.AppReplayNonces
                .Where(x => x.AppKeyId == previousAppKeyId)
                .ToListAsync(cancellationToken);

            if (previousNonces.Count > 0)
            {
                dbContext.AppReplayNonces.RemoveRange(previousNonces);
            }
        }

        device.AppKeyId = appKeyId;
        device.ProtectedAppSecret = protector.Protect(appSecret);
        device.LastSeenAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new IssuedAppCredential(
            device.DeviceCode,
            appKeyId,
            appSecret,
            exampleTimestamp,
            exampleNonce,
            exampleBodyHash,
            examplePayload,
            exampleSignature);
    }

    private async Task<DeviceContext> ResolveBrowserDeviceAsync(
        HttpContext context,
        string clientIp,
        string userAgent,
        string signalHash,
        CancellationToken cancellationToken)
    {
        var token = context.Request.Cookies[options.DeviceCookieName];
        GatewayManagedDevice? device = null;

        if (!string.IsNullOrWhiteSpace(token))
        {
            var tokenHash = ComputeHash(token);
            device = await dbContext.ManagedDevices
                .FirstOrDefaultAsync(x => x.BrowserTokenHash == tokenHash, cancellationToken);
        }

        if (device is null)
        {
            token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            device = new GatewayManagedDevice
            {
                Id = Guid.NewGuid(),
                DeviceId = ComputeHash(Guid.NewGuid().ToString("N"))[..24],
                DeviceCode = BuildDeviceCode(),
                BrowserTokenHash = ComputeHash(token),
                SignalHash = signalHash,
                UserAgent = Limit(userAgent, 400),
                RegisteredIp = clientIp,
                LastSeenIp = clientIp,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                LastSeenAtUtc = DateTimeOffset.UtcNow,
                TrustState = GatewayDeviceTrustState.Trusted,
            };

            dbContext.ManagedDevices.Add(device);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (!string.Equals(device.SignalHash, signalHash, StringComparison.Ordinal))
            {
                device.TrustState = GatewayDeviceTrustState.Challenged;
                device.ChallengeReason = "浏览器环境发生变化，需要重新审批。";
                device.ChallengedAtUtc = DateTimeOffset.UtcNow;
                device.SignalHash = signalHash;
            }

            device.UserAgent = Limit(userAgent, 400);
            device.LastSeenIp = clientIp;
            device.LastSeenAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        context.Response.Cookies.Append(
            options.DeviceCookieName,
            token!,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                Path = "/",
            });

        return BuildContext(device, clientIp, userAgent, signalHash, "browser-cookie");
    }

    private async Task<DeviceContext> ResolveAppDeviceAsync(
        HttpContext context,
        string clientIp,
        string userAgent,
        string signalHash,
        CancellationToken cancellationToken)
    {
        var keyId = context.Request.Headers[AppKeyHeaderName].ToString().Trim();
        var timestampText = context.Request.Headers[AppTimestampHeaderName].ToString().Trim();
        var nonce = context.Request.Headers[AppNonceHeaderName].ToString().Trim();
        var bodyHash = context.Request.Headers[AppBodyHashHeaderName].ToString().Trim();
        var signature = context.Request.Headers[AppSignatureHeaderName].ToString().Trim();

        if (string.IsNullOrWhiteSpace(keyId)
            || string.IsNullOrWhiteSpace(timestampText)
            || string.IsNullOrWhiteSpace(nonce)
            || string.IsNullOrWhiteSpace(bodyHash)
            || string.IsNullOrWhiteSpace(signature))
        {
            throw new DeviceCredentialException("APP 设备凭证不完整。", StatusCodes.Status401Unauthorized);
        }

        if (!long.TryParse(timestampText, out var unixSeconds))
        {
            throw new DeviceCredentialException("APP 设备时间戳无效。", StatusCodes.Status401Unauthorized);
        }

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        var now = DateTimeOffset.UtcNow;
        if ((now - timestamp).Duration() > AppSignatureWindow)
        {
            throw new DeviceCredentialException("APP 设备签名已过期。", StatusCodes.Status401Unauthorized);
        }

        var device = await dbContext.ManagedDevices
            .FirstOrDefaultAsync(x => x.AppKeyId == keyId, cancellationToken)
            ?? throw new DeviceCredentialException("APP 设备未注册。", StatusCodes.Status401Unauthorized);

        if (string.IsNullOrWhiteSpace(device.ProtectedAppSecret))
        {
            throw new DeviceCredentialException("APP 设备密钥已失效。", StatusCodes.Status401Unauthorized);
        }

        var actualBodyHash = await ComputeBodyHashAsync(context.Request, cancellationToken);
        if (!SecureEquals(bodyHash, actualBodyHash))
        {
            throw new DeviceCredentialException("APP 请求体摘要校验失败。", StatusCodes.Status401Unauthorized);
        }

        string secret;
        try
        {
            secret = protector.Unprotect(device.ProtectedAppSecret);
        }
        catch (CryptographicException)
        {
            throw new DeviceCredentialException("APP 设备密钥无法解密，请重新签发。", StatusCodes.Status401Unauthorized);
        }

        var payload = BuildSignaturePayload(
            unixSeconds,
            nonce,
            context.Request.Method,
            context.Request.Path.Value,
            context.Request.QueryString.Value,
            bodyHash);
        var expectedSignature = BuildSignature(secret, payload);

        if (!SecureEquals(signature, expectedSignature))
        {
            throw new DeviceCredentialException("APP 设备签名校验失败。", StatusCodes.Status401Unauthorized);
        }

        await ConsumeNonceAsync(keyId, nonce, now, cancellationToken);

        device.UserAgent = Limit(userAgent, 400);
        device.LastSeenIp = clientIp;
        device.LastSeenAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return BuildContext(device, clientIp, userAgent, signalHash, "app-hmac");
    }

    private async Task ConsumeNonceAsync(string appKeyId, string nonce, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var nonces = await dbContext.AppReplayNonces
            .Where(x => x.AppKeyId == appKeyId)
            .ToListAsync(cancellationToken);

        var expiredNonces = nonces
            .Where(x => x.ExpiresAtUtc <= now)
            .ToList();

        if (expiredNonces.Count > 0)
        {
            dbContext.AppReplayNonces.RemoveRange(expiredNonces);
        }

        var nonceHash = ComputeHash(nonce);
        dbContext.AppReplayNonces.Add(new GatewayAppReplayNonce
        {
            Id = Guid.NewGuid(),
            AppKeyId = appKeyId,
            NonceHash = nonceHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(AppSignatureWindow),
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await IsNonceAlreadyUsedAsync(appKeyId, nonceHash, cancellationToken))
            {
                throw new DeviceCredentialException("APP 请求疑似重放。", StatusCodes.Status401Unauthorized);
            }

            throw;
        }
    }

    private async Task<bool> IsNonceAlreadyUsedAsync(string appKeyId, string nonceHash, CancellationToken cancellationToken)
    {
        return await dbContext.AppReplayNonces
            .AsNoTracking()
            .AnyAsync(x => x.AppKeyId == appKeyId && x.NonceHash == nonceHash, cancellationToken);
    }

    private static DeviceContext BuildContext(
        GatewayManagedDevice device,
        string clientIp,
        string userAgent,
        string signalHash,
        string credentialChannel)
    {
        return new DeviceContext
        {
            DeviceId = device.DeviceId,
            DeviceCode = device.DeviceCode,
            SignalHash = signalHash,
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent,
            ClientIp = clientIp,
            CredentialChannel = credentialChannel,
            TrustState = device.TrustState,
            ChallengeReason = device.ChallengeReason,
            HasAppCredential = device.HasAppCredential,
            AppKeyId = device.AppKeyId ?? string.Empty,
        };
    }

    private static bool HasAnyAppHeaders(HttpRequest request) =>
        request.Headers.ContainsKey(AppKeyHeaderName)
        || request.Headers.ContainsKey(AppTimestampHeaderName)
        || request.Headers.ContainsKey(AppNonceHeaderName)
        || request.Headers.ContainsKey(AppBodyHashHeaderName)
        || request.Headers.ContainsKey(AppSignatureHeaderName);

    private static string BuildSignalHash(HttpContext context)
    {
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var language = context.Request.Headers.AcceptLanguage.ToString();
        var platform = context.Request.Headers["sec-ch-ua-platform"].ToString();
        var rawSignal = $"{userAgent}|{language}|{platform}";
        return ComputeHash(rawSignal)[..12];
    }

    public static string BuildNonce() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18));

    public static string BuildBodyHash(ReadOnlySpan<byte> bodyBytes) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(bodyBytes));

    public static string BuildSignaturePayload(
        long unixSeconds,
        string nonce,
        string method,
        string? path,
        string? queryString,
        string bodyHash)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? "/" : path;
        var normalizedQuery = string.IsNullOrWhiteSpace(queryString) ? string.Empty : queryString;
        return $"{unixSeconds}\n{nonce}\n{method.ToUpperInvariant()}\n{normalizedPath}{normalizedQuery}\n{bodyHash}";
    }

    public static string BuildSignature(string secret, string payload)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(secretBytes);
        return WebEncoders.Base64UrlEncode(hmac.ComputeHash(payloadBytes));
    }

    private static async Task<string> ComputeBodyHashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        await using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        request.Body.Position = 0;

        return BuildBodyHash(buffer.ToArray());
    }

    private static bool SecureEquals(string actual, string expected)
    {
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static string BuildDeviceCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);
        var raw = Convert.ToHexString(bytes);
        return $"DEV-{raw[..4]}-{raw[4..8]}-{raw[8..12]}";
    }

    private static string Limit(string value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Length <= maxLength ? value : value[..maxLength];

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
