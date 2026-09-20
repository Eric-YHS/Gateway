# GatewayDemo.Legacy-net472 未修复问题修复计划

## 问题 3：移动请求授权时，有姓名等完整信息的时候申请原因和移动端信息不要覆盖

### 修改点 1：ProxyRequestModule.AutoCreateLegacyAppRequest

位置：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472\src\GatewayDemo.Legacy.Web\Infrastructure\ProxyRequestModule.cs` 约第 1534 行

当前逻辑：
```csharp
var reason = descriptor.IsLoginRequest
    ? "快普移动客户端自动发起接入申请。"
    : "快普移动客户端访问受保护接口时自动发起接入申请。";
if (!string.IsNullOrWhiteSpace(descriptor.ReviewSummary))
{
    reason = reason + " " + descriptor.ReviewSummary;
}
```

修复后逻辑：
1. 先调用 repository.GetPendingRequestAsync(device.DeviceId, CancellationToken.None) 查询该设备是否存在 Pending 申请。
2. 若存在，且现有 Reason 非空、不是以自动前缀开头，则保留原 Reason。
3. 若现有 Reason 为空或已是自动前缀开头，则使用新的自动前缀 + ReviewSummary。
4. 若保留原 Reason，且原 Reason 尚未包含 ReviewSummary 内容，则将 ReviewSummary 追加到原 Reason 末尾。

自动前缀：
- "快普移动客户端自动发起接入申请。"
- "快普移动客户端访问受保护接口时自动发起接入申请。"

新增 repository 方法：
```csharp
public Task<GatewayAccessRequest> GetPendingRequestAsync(string deviceId, CancellationToken cancellationToken)
{
    using (var connection = OpenConnection())
    using (var command = connection.CreateCommand())
    {
        command.CommandText = @"
SELECT *
FROM GatewayAccessRequests
WHERE DeviceId = @DeviceId AND Status = @Status
ORDER BY UpdatedAtUtc DESC
LIMIT 1;";
        command.Parameters.AddWithValue("@DeviceId", deviceId ?? string.Empty);
        command.Parameters.AddWithValue("@Status", (int)GatewayAccessRequestStatus.Pending);
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                return Task.FromResult<GatewayAccessRequest>(null);
            }

            var request = MapRequest(reader);
            request.RequestedSites = GetRequestSites(connection, request.Id, null);
            return Task.FromResult(request);
        }
    }
}
```

### 修改点 2：LegacyGatewayRepository.CreateOrUpdateRequestAsync 更新分支

位置：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472\src\GatewayDemo.Legacy.Web\Infrastructure\LegacyGatewayRepository.cs` 约第 589 行

当前逻辑会把所有 legacy 字段和申请人信息全部覆盖为新值。

修复：在更新已有 Pending 申请时，若新传入的字段为空字符串但数据库中该字段已有非空值，则保留原值。需要保留的字段：
- CompanyName
- ApplicantName
- Phone
- LegacyUrlBefore
- LegacyCompanyId
- LegacyUserId
- LegacyDataCenterId
- LegacyDeviceImei
- LegacyMobileUserNum

实现方式：定义一个局部辅助方法或内联判断：
```csharp
string CoalescePreserve(string newValue, string existingValue)
{
    return string.IsNullOrWhiteSpace(newValue) && !string.IsNullOrWhiteSpace(existingValue)
        ? existingValue
        : (newValue ?? string.Empty);
}
```

然后：
```csharp
request.CompanyName = CoalescePreserve(companyName, request.CompanyName);
request.ApplicantName = CoalescePreserve(applicantName, request.ApplicantName);
request.Phone = CoalescePreserve(phone, request.Phone);
request.LegacyUrlBefore = CoalescePreserve(legacyUrlBefore, request.LegacyUrlBefore);
request.LegacyCompanyId = CoalescePreserve(legacyCompanyId, request.LegacyCompanyId);
request.LegacyUserId = CoalescePreserve(legacyUserId, request.LegacyUserId);
request.LegacyDataCenterId = CoalescePreserve(legacyDataCenterId, request.LegacyDataCenterId);
request.LegacyDeviceImei = CoalescePreserve(legacyDeviceImei, request.LegacyDeviceImei);
request.LegacyMobileUserNum = CoalescePreserve(legacyMobileUserNum, request.LegacyMobileUserNum);
```

## 问题 4：外部系统调用内部系统 API 没有浏览器指纹，需考虑

### 修改点：DeviceCredentialService.ResolveSignedAppContext

位置：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472\src\GatewayDemo.Legacy.Web\Infrastructure\DeviceCredentialService.cs` 约第 258 行

当前逻辑：`SignalHash = BuildSignalHash(context)` 使用浏览器头。

修复：
1. 新增方法：
```csharp
private static string BuildAppSignalHash(HttpContext context, string credentialKey)
{
    var clientIp = ClientIpResolver.GetClientIp(context, configuration.Gateway);
    var userAgent = context.Request.UserAgent ?? string.Empty;
    return ComputeHash(string.Concat(clientIp, "|", credentialKey ?? string.Empty, "|", userAgent)).Substring(0, 12);
}
```

2. 在 ResolveSignedAppContext 返回 DeviceContext 前，将 SignalHash 改为：
```csharp
SignalHash = BuildAppSignalHash(context, credentialKey),
```

## 问题 5：原来直接访问内部系统的接口现在通过网关访问，如何审核管理

实现基于配置的外部 API 客户端白名单管理。

### 修改点 1：新增 ExternalApiClientOptions 配置类

新建文件：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472\src\GatewayDemo.Legacy.Core\Options\ExternalApiClientOptions.cs`

内容：
```csharp
using System.Collections.Generic;
using System.Linq;

namespace GatewayDemo.Legacy.Core.Options
{
    public sealed class ExternalApiClientOptions
    {
        public string ClientId { get; set; }
        public string ApiKeySha256 { get; set; }
        public bool Enabled { get; set; }
        public IList<string> AllowedIpRanges { get; set; }
        public IList<string> AllowedSiteKeys { get; set; }

        public ExternalApiClientOptions()
        {
            ClientId = string.Empty;
            ApiKeySha256 = string.Empty;
            Enabled = false;
            AllowedIpRanges = new List<string>();
            AllowedSiteKeys = new List<string>();
        }

        public void Normalize()
        {
            ClientId = (ClientId ?? string.Empty).Trim();
            ApiKeySha256 = (ApiKeySha256 ?? string.Empty).Trim().ToLowerInvariant();
            AllowedIpRanges = (AllowedIpRanges ?? new List<string>())
                .Where(ip => !string.IsNullOrWhiteSpace(ip))
                .Select(ip => ip.Trim())
                .Distinct()
                .ToList();
            AllowedSiteKeys = (AllowedSiteKeys ?? new List<string>())
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
```

### 修改点 2：GatewaySiteOptions 增加 ExternalApiClients

位置：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472\src\GatewayDemo.Legacy.Core\Options\GatewaySiteOptions.cs`

- 增加属性：`public IList<ExternalApiClientOptions> ExternalApiClients { get; set; }`
- 构造函数中初始化：`ExternalApiClients = new List<ExternalApiClientOptions>();`
- Normalize 中调用：`foreach (var client in ExternalApiClients) { client.Normalize(); }`

### 修改点 3：DeviceCredentialService 增加辅助方法

位置：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472\src\GatewayDemo.Legacy.Web\Infrastructure\DeviceCredentialService.cs`

新增两个 public 方法：

```csharp
public bool IsAppHmacContext(DeviceContext device)
{
    return device != null
        && string.Equals(device.CredentialChannel, "app-hmac", StringComparison.OrdinalIgnoreCase);
}

public ExternalApiClientOptions ResolveExternalApiClient(DeviceContext device, string siteKey)
{
    if (device == null
        || !IsAppHmacContext(device)
        || string.IsNullOrWhiteSpace(device.AppCredentialKey)
        || string.IsNullOrWhiteSpace(siteKey))
    {
        return null;
    }

    var credentialKeyHash = ComputeHash(device.AppCredentialKey).ToLowerInvariant();
    foreach (var site in configuration.Gateway.Sites)
    {
        if (site.ExternalApiClients == null || site.ExternalApiClients.Count == 0)
        {
            continue;
        }

        foreach (var client in site.ExternalApiClients)
        {
            if (!client.Enabled)
            {
                continue;
            }

            if (!string.Equals(client.ApiKeySha256, credentialKeyHash, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (client.AllowedSiteKeys.Count > 0
                && !client.AllowedSiteKeys.Any(key => string.Equals(key, siteKey, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return client;
        }
    }

    return null;
}
```

### 修改点 4：ProxyRequestModule 处理外部 API 客户端白名单

位置：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472\src\GatewayDemo.Legacy.Web\Infrastructure\ProxyRequestModule.cs` 约第 313-324 行

在获取 authorization 之后、检查 device.RequiresReview 之前，插入：

```csharp
if (credentialService.IsAppHmacContext(device))
{
    var externalClient = credentialService.ResolveExternalApiClient(device, siteKey);
    if (externalClient != null)
    {
        repository.TouchAuthorizationAsync(device.DeviceId, device.ClientIp, siteKey, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        if (diag != null)
        {
            diag.AuthorizationStatus = "external-api-allowed";
            diag.IsProxied = true;
            diag.MatchedSiteKey = siteKey;
            diag.ElapsedMs = (long)(DateTime.UtcNow - diagStart).TotalMilliseconds;
            AppRequestDiagnostic.Record(diag);
        }

        runtime.ReverseProxy.Proxy(context, site, device, credentialService);
        context.ApplicationInstance.CompleteRequest();
        return;
    }
}
```

### 修改点 5：gateway-sites.json 增加示例配置

位置：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472\src\GatewayDemo.Legacy.Web\App_Data\gateway-sites.json`

在 ERP Main 站点中增加 ExternalApiClients 示例：

```json
"ExternalApiClients": [
    {
        "ClientId": "erp-sync-client",
        "ApiKeySha256": "[GENERATED_AT_DEPLOYMENT]",
        "Enabled": false,
        "AllowedIpRanges": ["127.0.0.1", "::1"],
        "AllowedSiteKeys": ["2027"]
    }
]
```

## 通用要求
- 保持现有代码风格。
- 只修改与这 3 个问题直接相关的代码，不动其它安全问题。
- 所有字符串比较使用 OrdinalIgnoreCase。
- 修改后需编译通过。
