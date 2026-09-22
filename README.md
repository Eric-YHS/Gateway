# GatewayDemo.Legacy（.NET Framework 4.7.2 老系统网关）

交付包名 / 站点名：`GatewayDemo.Legacy-net472`。

![CI](https://img.shields.io/github/actions/workflow/status/Eric-YHS/Gateway/ci.yml?branch=main&logo=githubactions&logoColor=white&label=CI)
![.NET Framework](https://img.shields.io/badge/.NET-4.7.2-512BD4?logo=dotnet&logoColor=white)

给企业内网老系统（ERP / ASP.NET / SPA 混合前端）使用的反向代理 + 设备授权网关，运行在 IIS 上。
一个网关实例可以挂多个后端站点，按域名端口、主机名或 URL 模式归属站点，并在浏览器 / 移动 APP /
外部系统三类调用方与上游之间统一处理设备审核、授权放行、路径改写与协议头传递。

- 站点归属：`HostNames`（带端口的 `IP:端口` 或 `域名:端口`）、`RequestMatchPatterns`（path/query 通配）、
  多段 `EntryPath` 的首段目录自动归属。
- 授权：浏览器设备号（Cookie 内随机凭据，不用 `User-Agent`）、移动 APP 设备级身份
  （`DeviceImei` / `MachineId` / `DeviceNo` / HMAC 凭据）、外部系统按来源 IP + 路径白名单。
- 转发：统一注入 `X-Gateway-Public-Origin`、`X-Gateway-Public-Base`、`X-Gateway-Proxy-Base`、
  `X-Gateway-Proxy-Site-Base`，支持 WebSocket 与长轮询 / 流式接口。
- 管理后台：审批 / 驳回 / 撤销 / 更新授权站点，默认端口 `5051`，网关端口 `5050`。

## 目录

- [快速开始](#快速开始)
- [目录结构](#目录结构)
- [站点配置 gateway-sites.json](#站点配置-gateway-sitesjson)
- [Web.config 开关](#webconfig-开关)
- [HTTPS 与反向代理](#https-与反向代理)
- [部署脚本参数](#部署脚本参数)
- [运维注意事项](#运维注意事项)
- [开发与 CI](#开发与-ci)
- [许可](#许可)

## 快速开始

先启用 IIS 与 ASP.NET：

```bat
dism /online /enable-feature /featurename:IIS-WebServer /all
dism /online /enable-feature /featurename:IIS-ASPNET45 /all
%windir%\Microsoft.NET\Framework64\v4.0.30319\aspnet_regiis.exe -ir
```

如需 WebSocket 代理，再启用 WebSocket Protocol（必要时重启）：

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -All
```

然后以管理员身份运行部署脚本：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\legacy\setup-demo-sites.ps1 -AdminPassword "<新密码>"
```

脚本默认会用本机 .NET Framework MSBuild 先编译 `GatewayDemo.Legacy.sln`，创建网关站点和本机 mock
后端站点，并把默认站点键 `2027` 的 `/proxy/2027/portal` 接到 mock，方便先做冒烟验证。站点键用
`-SiteKey` 覆盖，脚本会同步写入 `/proxy/<SiteKey>/...` 路径。管理后台默认账号 `gateway-admin`，
默认密码 `GatewayDemo!2026` 只供首次冒烟，生产必须用 `-AdminPassword` 重置（脚本会重新计算
`Admin.PasswordHash` 写入 `Web.config`），或按 `pbkdf2-sha256$迭代次数$盐$哈希` 格式自行生成后替换。

接入真实 ERP：把 `src\GatewayDemo.Legacy.Web\App_Data\gateway-sites.json` 的 `UpstreamBaseUrl` 改成
ERP 地址，`EntryPath` 改成 ERP 入口（例如 `default.aspx`）。`gateway-sites.json` 支持热更新，保存后
下一次请求自动生效，不需要替换目录或重启站点。配置文件推荐使用站点数组；单站点对象也会被兼容读取，
避免脚本更新单站点时误把站点列表写成对象后导致无站点可用。

## 目录结构

```
GatewayDemo.Legacy.sln
src/GatewayDemo.Legacy.Core/          # 模型、仓储接口、选项类（站点/路径规则/移动端 Profile 等）
src/GatewayDemo.Legacy.Web/           # 网关站点：代理管线、设备授权、管理后台、Web.config、App_Data/gateway-sites.json
src/MockBusinessBackend.Legacy.Web/   # 本机 mock 后端，用于冒烟与演示（含 jvs-aps-ui 前端目录）
scripts/legacy/setup-demo-sites.ps1   # 部署入口：编译、建站点、写 Web.config
scripts/legacy/bind-gateway-https.ps1 # 绑定 HTTPS 证书（本地证书或 PFX）
packages/                             # 通过 HintPath 引用的 NuGet 依赖（必须入库，见「开发与 CI」）
```

## 站点配置 gateway-sites.json

推荐写成站点数组。多站点部署时 `HostNames` 请写成带端口的 `IP:端口` 或 `域名:端口`，并保证各站点之间
不重复；条目可以带 `http://` 前缀，网关会自动归一化。带端口的条目优先于只写主机名的条目参与归属，
避免某个站点写了裸 IP 后抢走其他端口的请求。

| 字段 | 用途 |
| --- | --- |
| `HostNames` | 按域名/端口归属站点 |
| `RequestMatchPatterns` | 多个站点共用同一入口域名时，按请求 path、query 或完整相对 URL 归属站点，支持 `*` 通配，例如 `"/WebEnterprise/OEM/*PageConfigId=xxxx"` |
| `EntryPath` | ERP 入口；多段路径（如 `jvs-aps-ui/index.html`）的首段目录会自动归属该站点，深链接和刷新不需要额外配置 |
| `UpstreamBaseUrl` | 真实后端源站地址，不能填网关自己的公网域名，否则会形成自代理循环（网关会拒绝继续重定向） |
| `RedirectToUpstream` | 只做外部官网跳转的站点设为 `true` |
| `ExposeLegacyAppAtRoot` | 需要按根路径提供该站点时开启；多个站点同时开启时，只有各自 `HostNames` 命中的端口按根路径提供，经 `/proxy/<站点键>/` 访问的其他站点保持代理前缀 |
| `AnonymousAllowedPaths` | 审批前需要放行的初始化接口，支持 `*` 通配 |
| `UpstreamOriginRootPatterns` | 需要回源到上游根路径的资源模式 |
| `PathRules` | 单条路径规则，支持 `Name`、`Patterns`、`ForceUpstreamOriginRoot`、`DisableResponseRewrite`、`TreatAsLongPolling`、`TreatAsStreaming` |
| `PathRuleProfiles` | 引用 profile 统一展开 SPA、Vue/Vite 静态资源、通用 legacy API 和长轮询规则，例如 `["legacy-web-default"]`；JVS 专用目录用 `jvs-default`，demo 默认写入 `["legacy-web-default","jvs-default"]` |
| `PathRuleProfileDefinitions` | 现场新增前端目录、资源目录、长轮询或流式接口时在这里自定义，并用 `PathRuleProfiles` 引用，字段包含 `AnonymousAllowedPaths`、`UpstreamOriginRootPatterns`、`PathRules`；不要在代码里继续加路径特判 |
| `ExternalApiDefaultPathPatterns` | 站点级外部 API 默认放行路径，默认 `["/api/*"]` |
| `ExternalApiClients` | 外部系统调用白名单：按来源 IP、站点和路径管理，第三方仍按原接口路径调用网关入口，不需要 Cookie 或 HMAC 签名头 |
| `LegacyAppProfile` | 移动端登录路径、bootstrap action、WCF/上传路径、字段别名、SessionKey JSON 路径、品牌 UA marker、自动申请文案，以及 `TenantLookup` |

网关会统一用同一套候选路径解析逻辑匹配原始 path、代理 path、解码 path、入口路径相对 path 和 query
变体，避免 Vue history 刷新、资源转发、`__root__` 回源和移动端接口各写一套特殊处理。

生产环境请把示例里的本机 IP 改成对方服务器出口 IP 或网段，并收窄 `AllowedPathPatterns`。如果某个
client 没有显式配置 `AllowedPathPatterns`，只会使用站点的 `ExternalApiDefaultPathPatterns`，不会自动
放行 JVS 专用目录。

移动端登录请求如果没有携带企业号但携带了 `DataCenterId`，网关会按站点
`LegacyAppProfile.TenantLookup` 补取企业号，默认路径是
`ashx/Sys.ashx?action=tenant_name&DataCenterId=...`，路径、query、超时和响应字段都可配置。

## Web.config 开关

| 键 | 说明 |
| --- | --- |
| `Gateway.UseDefaultUpstreamProxy` | 网关默认使用 Windows/.NET 系统代理访问上游，适配只能通过系统代理访问外部 HTTPS 源站的服务器；生产要求 IIS 进程直连内网上游时设为 `false` 并重启站点 |
| `Gateway.UpstreamProxyUrl` | 显式指定上游代理，例如 `http://<代理地址>:<代理端口>`；浏览器能访问真实 HTTPS 上游但应用池报 `ConnectFailure`，通常是登录用户有代理而 IIS 服务身份没有代理。部署时也可直接传参：`-UpstreamBaseUrl "https://erp.example.com:2027/" -GatewayEntryPath "Resource/app/index.html" -UpstreamProxyUrl "http://<代理地址>:<代理端口>"` |
| `Gateway.Cors.Enabled`、`Gateway.Cors.AllowedOrigins`、`Gateway.Cors.AllowedMethods`、`Gateway.Cors.AllowedHeaders`、`Gateway.Cors.AllowCredentials`、`Gateway.Cors.MaxAgeSeconds` | 跨域访问网关接口；多值用英文逗号或分号分隔，`AllowedOrigins` 可精确到 `https://example.com`，也支持 `https://*.example.com` |
| `Gateway.LegacyApp.AllowAccountOnlyIdentityFallback` | 移动 APP 设备授权默认按设备级身份（`DeviceImei`、`MachineId`、`DeviceNo`、HMAC APP 凭据或已登录会话）识别，`UserId + DataCenterId` 这类账号级字段不会默认继承已有设备授权；老版本 APP 实在无法传设备级标识时临时设 `true`，但建议优先补齐设备标识 |
| `Gateway.BrowserDevice.SignalHeaders` | 浏览器设备信号，默认 `User-Agent,Sec-CH-UA-Platform,Sec-CH-UA-Mobile`，版本号会归一化，避免浏览器小版本更新频繁触发复审 |
| `Gateway.BrowserDevice.ReviewOnSignalChange` | 现场不希望信号变化触发重新审核时设为 `false` |
| `Gateway.ExternalScheme` | 设 `https` 强制按 HTTPS 入口处理 |
| `Gateway.DeviceCookieDomain` | 同一设备需要在多个子域名间共享授权状态（如 `erp.example.com` 与 `oa.example.com`）时设 `.example.com`；未显式设置时按当前外部域名自动推导父域 Cookie；同一域名不同端口浏览器本身共享 Cookie，一般不用设 |
| `Gateway.DeviceCookieSecurePolicy` | `Never`（默认，避免 HTTP/HTTPS 混用时授权 Cookie 在 HTTP 入口丢失）、`ExternalHttps` 或 `Always`；生产建议统一 HTTPS |
| `Admin.PasswordHash` | 管理后台密码哈希，格式 `pbkdf2-sha256$迭代次数$盐$哈希` |

浏览器设备号由浏览器 Cookie 中的随机凭据生成，不由 `User-Agent` 生成；不同浏览器或不同设备即使
`User-Agent` 相同，也会得到不同设备号。

## HTTPS 与反向代理

HTTPS 证书不是写在 `gateway-sites.json` 里的，和 nginx 一样需要绑定在入口服务器。IIS 直接对外提供
HTTPS 时：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\legacy\bind-gateway-https.ps1 -GatewaySiteName GatewayDemoLegacy -HostName erp.example.com -CertThumbprint <LocalMachine-My-thumbprint>
```

如果是 PFX 文件，改用 `-PfxPath .\gateway.pfx -PfxPassword <password>`。

如果用 nginx、SLB 或其他反向代理终止 HTTPS，证书配置在该入口层；入口转发到 IIS 网关时需要保留：

```nginx
proxy_set_header X-Forwarded-Proto https;
proxy_set_header X-Forwarded-Host $host;
proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
proxy_set_header Upgrade $http_upgrade;
proxy_set_header Connection "upgrade";
```

同时需要把入口代理 IP 配为受信任代理，否则网关会忽略 `X-Forwarded-Proto`，浏览器端安全 Cookie 和
上游转发协议会按 HTTP 处理：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\legacy\setup-demo-sites.ps1 -TrustedProxyAddresses "127.0.0.1,<proxy-ip>" -TrustedProxyCidrs "<proxy-cidr>"
```

入口代理和 IIS 在内网或本机转发时，网关会自动采信回环/内网来源的 `X-Forwarded-Proto`，但仍建议生产
环境显式配置。上游需要在 JSON、脚本或移动端返回值里生成公网地址时，优先使用
`X-Gateway-Public-Origin` / `X-Gateway-Public-Base` / `X-Gateway-Proxy-Base` /
`X-Gateway-Proxy-Site-Base`，不要硬编码网关路径或依赖网关改写响应体。

## 部署脚本参数

| 参数 | 默认值 / 作用 |
| --- | --- |
| `-GatewayPort` / `-AdminPort` | `5050` / `5051`，网关业务端口与管理后台端口 |
| `-SiteKey` | `2027`，默认站点键，同时决定 `/proxy/<SiteKey>/` 前缀 |
| `-AdminPassword` | 管理后台密码，生产必传 |
| `-UpstreamBaseUrl` / `-GatewayEntryPath` / `-UpstreamProxyUrl` | 真实上游地址、入口路径与上游代理 |
| `-PathRuleProfiles` / `-ExternalApiAllowedPathPatterns` | 默认 path profile 与外部 API 放行路径 |
| `-LegacyAppMobileUserAgentMarkers` | 写入私有 UA marker |
| `-BrowserDeviceSignalHeaders` / `-BrowserDeviceReviewOnSignalChange` | 浏览器设备信号策略 |
| `-DeviceCookieDomain` | 跨子域共享授权 Cookie，例如 `-DeviceCookieDomain ".example.com"` |
| `-TrustedProxyAddresses` / `-TrustedProxyCidrs` | 受信任的反向代理 |
| `-BuildBeforeDeploy` | 默认 `true`，先用本机 .NET Framework MSBuild 编译解决方案；已手动编译或服务器没有构建工具时传 `$false`，也可用 `-MSBuildPath` 指定 MSBuild |
| `-ConfigureHttpSysLongUrlSupport` | 默认 `true`，设置服务器级 HTTP.sys `UrlSegmentMaxLength=32766`、`MaxFieldLength=65534`、`MaxRequestBytes=131072` 以兼容上游超长 URL；该设置需要重启 HTTP 服务或重启 Windows 后对既有监听完全生效，不希望脚本改动服务器级参数时传 `$false` |

## 运维注意事项

- **只改站点清单**：直接保存 `App_Data\gateway-sites.json`，热更新生效，不需要替换目录或重启站点。
- **更新交付包**：不要直接覆盖正在运行的整个站点目录，`SQLite.Interop.dll` 等运行中 DLL 可能被 IIS
  工作进程占用。建议保留旧目录，解压新包到新目录，迁移当前 `App_Data\gateway-sites.json` 和需要保留
  的数据库文件后，把 IIS 站点物理路径切到新目录。
- **管理后台交互**：审批、驳回、撤销和更新授权站点失败时会在页面顶部显示中文错误提示，不会跳转到
  无关页面；页面放置过久或 IIS 重启后提交表单会提示“页面已过期，请重新提交”，刷新后重新操作即可；
  更新授权站点时如果既没有勾选具体站点也没有勾选“所有站点”，页面会阻止提交并给出提示。
- **WebSocket**：需要启用 `IIS-WebSockets`，前置 nginx/SLB 还要透传 `Upgrade` 与 `Connection`。

## 开发与 CI

用 VS2019（或更高版本）打开 `GatewayDemo.Legacy.sln` 即可查看和编译源码，目标框架
`.NET Framework 4.7.2`。

`.github/workflows/ci.yml` 在每次 push / PR 上执行：

- **build**（`windows-latest`）：`msbuild /t:Build /p:Configuration=Release` 编译整个解决方案，并把
  `bin/` 输出作为构建产物上传。
- **checks**：用 PowerShell 语法解析器校验 `scripts/legacy/*.ps1`（现场唯一的部署入口），校验
  `gateway-sites.json` 可解析（它支持热更新，写坏会同时打挂所有站点），并断言编译产物没有被提交。

仓库依赖策略：

- `packages/` **必须入库**。项目通过 `HintPath` 直接引用
  `packages/Stub.System.Data.SQLite.Core.NetFramework.1.0.118.0/...`，仓库里没有 `packages.config` 也没有
  `PackageReference`，`nuget restore` 不会重建这个目录。
- `bin/`、`obj/` 是编译产物，已加入 `.gitignore`，不再入库；需要交付 DLL 时从 CI 构建产物获取。

## 许可

[保留所有权利](LICENSE)（All rights reserved）。本仓库是面向企业客户的交付代码，如需使用或再分发请
先取得版权持有人书面许可。
