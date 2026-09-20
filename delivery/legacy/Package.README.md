# GatewayDemo.Legacy-net472

部署方式：先启用 IIS 和 ASP.NET：

```bat
dism /online /enable-feature /featurename:IIS-WebServer /all
dism /online /enable-feature /featurename:IIS-ASPNET45 /all
%windir%\Microsoft.NET\Framework64\v4.0.30319\aspnet_regiis.exe -ir
```

然后以管理员身份运行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\legacy\setup-demo-sites.ps1
```

脚本默认会创建网关站点和本机 mock 后端站点，并把默认站点键 `2027` 的 `/proxy/2027/portal` 接到 mock，方便先做冒烟验证。站点键可用 `-SiteKey` 覆盖，脚本会同步写入 `/proxy/<SiteKey>/...` 路径。

接入真实 ERP 时，把 `src\GatewayDemo.Legacy.Web\App_Data\gateway-sites.json` 里的 `UpstreamBaseUrl` 改成你们的 ERP 地址，`EntryPath` 改成 ERP 入口，例如 `default.aspx`。`gateway-sites.json` 支持热更新，保存后下一次请求会自动读取新配置。配置文件推荐使用站点数组；单站点对象也会被兼容读取，避免脚本更新单站点时误把站点列表写成对象后导致无站点可用。

网关默认使用 Windows/.NET 系统代理访问上游，适配只能通过系统代理访问外部 HTTPS 源站的服务器环境。如生产环境要求 IIS 进程直连内网上游，可在 `Web.config` 设置 `Gateway.UseDefaultUpstreamProxy=false` 后重启站点。

如果浏览器能访问真实 HTTPS 上游，但 IIS 应用池访问时报 `ConnectFailure`，通常是当前登录用户有代理而 IIS 服务身份没有代理。可在部署时显式指定上游代理，例如：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\legacy\setup-demo-sites.ps1 -UpstreamBaseUrl "https://erp.example.com:2027/" -GatewayEntryPath "Resource/app/index.html" -UpstreamProxyUrl "http://<代理地址>:<代理端口>"
```

也可以部署后在 `Web.config` 设置 `Gateway.UpstreamProxyUrl=http://<代理地址>:<代理端口>`，再重启 IIS 站点。

如需按域名归属站点，可在站点配置里填 `HostNames`；多个站点共用同一入口域名时，可在站点配置里填 `RequestMatchPatterns`，按请求 path、query 或完整相对 URL 归属站点，支持 `*` 通配，例如 `"/WebEnterprise/OEM/*PageConfigId=xxxx"`。审批前需要放行的初始化接口可填 `AnonymousAllowedPaths`，支持 `*` 通配；SPA、Vue/Vite 静态资源、通用 legacy API 和长轮询规则可用 `PathRuleProfiles=["legacy-web-default"]` 统一展开，避免在 `AnonymousAllowedPaths`、`UpstreamOriginRootPatterns` 和 `PathRules` 里重复维护。JVS 专用目录使用 `jvs-default`，例如 demo 默认写入 `["legacy-web-default","jvs-default"]`。只做外部官网跳转的站点可设置 `RedirectToUpstream=true`。

多站点部署时 `HostNames` 请写成带端口的 `IP:端口` 或 `域名:端口`，并保证各站点之间不重复；条目可以带 `http://` 前缀，网关会自动归一化。带端口的条目优先于只写主机名的条目参与归属，避免某个站点写了裸 IP 后抢走其他端口的请求。`EntryPath` 是多段路径（例如 `jvs-aps-ui/index.html`）的站点，其首段目录（`/jvs-aps-ui/*`）会自动归属该站点，深链接和刷新不需要额外配置。多个站点都配置了 `ExposeLegacyAppAtRoot=true` 时，只有各自 `HostNames` 命中的端口才按根路径提供该站点；经 `/proxy/<站点键>/` 访问的其他站点会保持代理前缀，不会被折叠到根路径。

如果现场有新的前端目录、资源目录、长轮询接口或流式接口，不要在代码里继续增加路径特判。可在站点配置里添加 `PathRuleProfileDefinitions` 并用 `PathRuleProfiles` 引用，配置项包括 `AnonymousAllowedPaths`、`UpstreamOriginRootPatterns` 和 `PathRules`；单条 `PathRules` 支持 `Name`、`Patterns`、`ForceUpstreamOriginRoot`、`DisableResponseRewrite`、`TreatAsLongPolling`、`TreatAsStreaming`。网关会统一用同一套候选路径解析逻辑匹配原始 path、代理 path、解码 path、入口路径相对 path 和 query 变体，避免 Vue history 刷新、资源转发、`__root__` 回源和移动端接口各写一套特殊处理。

如业务前端需要跨域访问网关接口，可在 `Web.config` 中设置 `Gateway.Cors.Enabled=true`，并配置 `Gateway.Cors.AllowedOrigins`、`Gateway.Cors.AllowedMethods`、`Gateway.Cors.AllowedHeaders`、`Gateway.Cors.AllowCredentials`、`Gateway.Cors.MaxAgeSeconds`。多个值用英文逗号或分号分隔；`AllowedOrigins` 可精确到 `https://example.com`，也可用 `https://*.example.com` 这类通配。

外部系统调用内部 API 时，没有浏览器指纹和交互式审批流程。此类调用在 `gateway-sites.json` 的 `ExternalApiClients` 里按来源 IP、站点和路径白名单管理；第三方仍按原接口路径调用网关入口，不需要增加浏览器 Cookie 或 HMAC 签名头。生产环境请把示例里的本机 IP 改成对方服务器出口 IP 或网段，并收窄 `AllowedPathPatterns`。如果某个 client 没有显式配置 `AllowedPathPatterns`，只会使用站点的 `ExternalApiDefaultPathPatterns`，默认是 `["/api/*"]`，不会自动放行 JVS 专用目录。

移动端登录请求如果没有携带企业号，但携带了 `DataCenterId`，网关会按站点 `LegacyAppProfile.TenantLookup` 配置补取企业号；默认路径是 `ashx/Sys.ashx?action=tenant_name&DataCenterId=...`，可按实际系统改路径、query、超时和响应字段。移动端登录路径、bootstrap action、WCF/上传路径、字段别名、SessionKey JSON 路径、品牌 UA marker 和自动申请文案也都在 `LegacyAppProfile` 里配置；部署脚本可用 `-LegacyAppMobileUserAgentMarkers` 写入私有 UA marker。网关转发到上游时会统一传递 `X-Gateway-Public-Origin`、`X-Gateway-Public-Base`、`X-Gateway-Proxy-Base`、`X-Gateway-Proxy-Site-Base`，上游需要在 JSON、脚本或移动端返回值里生成公网地址时优先使用这些头，不要硬编码网关路径或依赖网关改写响应体。

移动 APP 设备授权默认按设备级身份识别，例如 `DeviceImei`、`MachineId`、`DeviceNo`、HMAC APP 凭据或已登录会话；`UserId + DataCenterId` 这类账号级字段不会默认继承已有设备授权。若老版本 APP 确实无法传递任何设备级标识，可在 `Web.config` 临时设置 `Gateway.LegacyApp.AllowAccountOnlyIdentityFallback=true` 开启兼容，但建议优先补齐设备标识。

浏览器设备号由浏览器 Cookie 中的随机凭据生成，不由 `User-Agent` 生成；不同浏览器或不同设备即使 `User-Agent` 相同，也会得到不同设备号。浏览器设备信号默认使用 `User-Agent,Sec-CH-UA-Platform,Sec-CH-UA-Mobile`，版本号会归一化，避免浏览器小版本更新频繁触发复审；如需调整，可在 `Web.config` 设置 `Gateway.BrowserDevice.SignalHeaders`。如果现场不希望信号变化触发重新审核，可设置 `Gateway.BrowserDevice.ReviewOnSignalChange=false`，部署脚本也支持 `-BrowserDeviceSignalHeaders` 和 `-BrowserDeviceReviewOnSignalChange`。

HTTPS 证书不是写在 `gateway-sites.json` 里的，和 nginx 一样需要绑定在入口服务器。IIS 直接对外提供 HTTPS 时可用：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\legacy\bind-gateway-https.ps1 -GatewaySiteName GatewayDemoLegacy -HostName erp.example.com -CertThumbprint <LocalMachine-My-thumbprint>
```

如果是 PFX 文件，可改用 `-PfxPath .\gateway.pfx -PfxPassword <password>`。`UpstreamBaseUrl` 必须配置为真实后端源站地址，不能配置成当前网关公网域名；否则会形成自代理循环，网关会拒绝继续重定向。

如果用 nginx、SLB 或其他反向代理终止 HTTPS，证书配置在该入口层；入口转发到 IIS 网关时需要保留：

```nginx
proxy_set_header X-Forwarded-Proto https;
proxy_set_header X-Forwarded-Host $host;
proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
proxy_set_header Upgrade $http_upgrade;
proxy_set_header Connection "upgrade";
```

同时需要把入口代理 IP 配为受信任代理，否则网关会忽略 `X-Forwarded-Proto`，浏览器端安全 Cookie 和上游转发协议会按 HTTP 处理：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\legacy\setup-demo-sites.ps1 -TrustedProxyAddresses "127.0.0.1,<proxy-ip>" -TrustedProxyCidrs "<proxy-cidr>"
```

如果入口代理和 IIS 在内网或本机转发，网关会自动采信回环/内网来源的 `X-Forwarded-Proto`。仍建议生产环境显式配置 `TrustedProxyAddresses` 或 `TrustedProxyCidrs`。也可以在 `Web.config` 设置 `Gateway.ExternalScheme=https`，强制网关按 HTTPS 入口处理。

如果同一台设备需要在多个子域名间共享授权状态，例如 `erp.example.com` 和 `oa.example.com`，请在 `Web.config` 设置 `Gateway.DeviceCookieDomain=.example.com`，或运行部署脚本时加 `-DeviceCookieDomain ".example.com"`。如果只是同一个域名的不同端口，浏览器本身会共享 Cookie，一般不需要设置该项。

未显式设置 `Gateway.DeviceCookieDomain` 时，网关会按当前外部域名自动推导父域 Cookie，例如 `erp.example.com` 会尝试写入 `.example.com`。生产环境建议统一使用 HTTPS；如需强制安全 Cookie，可设置 `Gateway.DeviceCookieSecurePolicy=ExternalHttps` 或 `Always`。默认 `Never` 是为了避免 HTTP/HTTPS 混用时授权 Cookie 在 HTTP 入口丢失。

WebSocket 代理需要启用 IIS WebSocket Protocol。部署脚本会尝试检测该功能；如果未启用，请以管理员身份运行 `Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -All`，必要时重启。前置 nginx/SLB 还需要透传 `Upgrade` 和 `Connection` 请求头。

更新交付包时不要直接覆盖正在运行的整个站点目录，`SQLite.Interop.dll` 等运行中 DLL 可能被 IIS 工作进程占用。建议先保留旧目录，解压新包到新目录，迁移当前 `App_Data\gateway-sites.json` 和需要保留的数据库文件后，把 IIS 站点物理路径切到新目录；只改站点清单时直接保存 `gateway-sites.json` 即可，不需要替换目录或重启站点。

网关业务端口默认是 `5050`，管理后台端口默认是 `5051`；如需改端口，可在运行 `setup-demo-sites.ps1` 时传 `-GatewayPort` 和 `-AdminPort`。如需改默认站点键，可传 `-SiteKey`；如需改默认 path profile 或外部 API 路径，可传 `-PathRuleProfiles`、`-ExternalApiAllowedPathPatterns`。部署脚本默认会先用本机 .NET Framework MSBuild 编译 `GatewayDemo.Legacy.sln`；如已手动编译或服务器没有构建工具，可传 `-BuildBeforeDeploy $false`，也可用 `-MSBuildPath` 指定 MSBuild。管理后台默认账号是 `gateway-admin`，默认密码是 `GatewayDemo!2026`；该默认密码仅供首次部署冒烟使用，生产环境必须运行脚本时传 `-AdminPassword "<新密码>"` 重置（脚本会重新计算 `Admin.PasswordHash` 写入 `Web.config`），或参照 `pbkdf2-sha256$迭代次数$盐$哈希` 格式自行生成后替换 `Web.config` 中的 `Admin.PasswordHash`。

管理台的审批、驳回、撤销和更新授权站点操作失败时，会在页面顶部显示中文错误提示，不会跳转到无关页面；管理页放置过久或 IIS 重启后提交表单，会提示“页面已过期，请重新提交”，刷新后重新操作即可。更新授权站点时如果既没有勾选具体站点也没有勾选“所有站点”，页面会阻止提交并给出提示。

部署脚本默认会设置服务器级 HTTP.sys `UrlSegmentMaxLength=32766`、`MaxFieldLength=65534`、`MaxRequestBytes=131072`，用于兼容上游系统里的超长 URL。该设置需要重启 HTTP 服务或重启 Windows 后对既有监听完全生效；如生产环境不希望脚本改动该服务器级参数，可运行脚本时传 `-ConfigureHttpSysLongUrlSupport $false`。

用 VS2019 打开 `GatewayDemo.Legacy.sln` 可以查看和编译源码。
