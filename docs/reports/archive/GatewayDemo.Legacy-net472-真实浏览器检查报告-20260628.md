# GatewayDemo.Legacy-net472 真实浏览器检查报告

检查对象：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`

检查时间：2026-06-28 09:52-10:05（Asia/Shanghai）

本次不是静态模拟检查。压缩包已重新解压到 `E:\验证页面\_realcheck_zip_20260628_095211`，并用 IIS 原生 ASP.NET 4.x 管道启动本次专用站点：

- 网关站点：`CodexGatewayRealUse20260628_34850`，端口 `34850/34851`
- Mock 后端：`CodexBackendRealUse20260628_34855`，端口 `34855`
- 部署脚本参数：`-BuildBeforeDeploy:$false -ConfigureHttpSysLongUrlSupport:$false`
- 构建验证：`E:\Visual Studio BuildTools\MSBuild\Current\Bin\MSBuild.exe GatewayDemo.Legacy.sln /t:Build /p:Configuration=Release "/p:Platform=Any CPU" /m /v:m`，通过，三个项目均重新输出 DLL

## 真实操作结果

通过项：

- 未授权访问 `http://localhost:34850/portal`：真实浏览器进入网关申请页。
- 提交申请：填写企业、申请人、电话、原因后提交，页面显示“待审核”。
- 后台登录：`http://localhost:34851/admin` 使用默认管理员账号登录成功。
- 审批：待审申请在后台可见，批准后转为“已授权”。
- 授权后业务访问：同一浏览器上下文访问 `/portal`，进入 mock ERP 登录页，不再回申请页。
- 业务登录：`mock-user / MockDemo!2026` 登录成功，回到 `http://localhost:34850/portal`。
- 静态资源：`/assets/mock.css`、`/assets/mock.js`、`/assets/kuaipu-mark.svg`、`/assets/grid.svg` 均通过网关返回 200。
- 普通路由：点击“报表”“运维”后 URL 保持在网关域名下，刷新 `/ops` 仍正常。
- 移动视口：390x844 下页面可用，无明显遮挡或资源失败。
- 移动请求：浏览器内真实 `fetch` 发出 `/user/login`、`/WCFService/PostBus.ashx?action=Echo`、`/ashx/ChatUploadImg.ashx`，均返回 200 JSON。
- Vue/SPA：`/jvs-aps-ui/`、`/jvs-aps-ui/orders`、`/jvs-aps-ui/reports/2026` 可访问，深层路由刷新正常；`/jvs-aps-ui/api/spa/ping?route=/reports/2026` 返回 200 JSON。
- Legacy proxy fallback：`/proxy/2027/portal` 可访问。
- WebSocket：`ws://localhost:34850/socket` 和 `ws://localhost:34850/proxy/2027/socket` 均打开成功，并返回 `echo:*`。
- 后台更新：执行“更新授权站点”，审计日志出现 `authorization-sites-updated`。
- 接口凭据：执行“签发接口凭据”，凭据页正常显示。截图 `realcheck-34851-14-admin-credential.png` 含测试 APP Secret，应按敏感材料处理。

运行态日志：

- 本次命名的控制台日志均无 warning/error。
- 网络日志未发现 404/500；关键请求均为 200/302 预期状态。
- Chrome verbose 仅提示密码框建议加 `autocomplete`，不是运行错误。

主要证据文件在 `E:\验证页面`：

- `realcheck-34850-01-apply.png`
- `realcheck-34850-02-apply-submitted.png`
- `realcheck-34851-03-admin-dashboard.png`
- `realcheck-34851-04-admin-after-approve.png`
- `realcheck-34850-06-portal-after-login.png`
- `realcheck-34850-09-mobile-ops.png`
- `realcheck-34850-11-vue-root-mobile.png`
- `realcheck-34850-12-vue-reports-mobile.png`
- `realcheck-34850-13-proxy-fallback.png`
- `realcheck-34851-14-admin-credential.png`
- 对应 `*-network.txt`、`*-console.txt`

## 发现的问题

1. zip 原始配置带历史测试端口。

   直接从 zip 读取的 `src/GatewayDemo.Legacy.Web/App_Data/gateway-sites.json` 里是：

   - `UpstreamBaseUrl = http://localhost:33455/mock/erp-main/`
   - `HostNames = localhost:33450, localhost:33451`

   `src/GatewayDemo.Legacy.Web/Web.config` 里 `Admin.ManagementPort = 33451`。

   部署脚本会覆盖这些值，所以按 README 跑脚本没问题；但如果研发或现场人员直接复制站点目录、不跑脚本，入口会指向旧测试端口。这是交付包本身的环境痕迹，建议打包前改为模板值或占位值，或把 `gateway-sites.json` 改为首次部署生成。

2. `legacy-web-default` 仍混有 JVS 特定路径。

   `GatewayPathRuleProfileCatalog.cs` 的默认 profile 同时包含通用资源规则和产品线规则：

   - `/jvs-ui-public/*`
   - `/jvs-public/*`
   - `/jvs-aps-ui`
   - `/jvs-aps-ui/*`
   - `/mgr/*`

   位置：`src/GatewayDemo.Legacy.Core/Options/GatewayPathRuleProfileCatalog.cs:11`、`:49`、`:101`。

   建议拆成多个 profile：

   - `spa-vite-default`：`/@vite/*`、`/@id/*`、`/@fs/*`、`/node_modules/*`、静态扩展名、`/assets/*`
   - `legacy-iis-default`：`/api/*`、`*.ashx`、`*.svc`、`/signalr/*`
   - `jvs-default`：`/jvs-public/*`、`/jvs-ui-public/*`、`/jvs-aps-ui/*`、`/mgr/*`

   这样后续接非 JVS/Vue 系统时，不需要改核心代码或把无关产品线规则带进所有站点。

3. 移动 APP 请求识别和字段别名仍硬编码在服务里。

   位置：

   - `DeviceCredentialService.cs:80`：登录/Bootstrap 路径和动作数组
   - `DeviceCredentialService.cs:487` 到 `:642`：`MachineId`、`DeviceImei`、`DataCenterId`、`CompanyId`、`UserId`、`SessionKey` 等字段别名和 APP 判定
   - `ProxyRequestModule.cs:1576` 到 `:1603`：根路径候选硬编码 `/user/`、`/wcfservice/`、`/ashx/`、`.ashx`、`.svc`、`/default.aspx`

   这些逻辑本次真实移动请求通过了，但它们仍偏“兼容某类老 APP”。建议抽成 `LegacyAppProfile` 配置，至少包含：

   - `LoginPathPatterns`
   - `BootstrapPathPatterns`
   - `BootstrapActions`
   - `ServicePathPatterns`
   - `UploadPathPatterns`
   - `IdentityFieldAliases`
   - `SessionTokenJsonPaths`
   - `CompanyFieldAliases`
   - `UserFieldAliases`

   默认提供一套 `kuaipu-mobile-default` 或 `legacy-mobile-default`，站点按需启用。

4. `tenant_name` 补企业号逻辑是固定接口。

   `DeviceCredentialService.cs:692` 到 `:741` 固定请求 `ashx/Sys.ashx?action=tenant_name&DataCenterId=...`，`NormalizeTenantNameResponse` 固定解析几个字段名。这个逻辑符合 README 描述，也能覆盖当前产品线，但对其他 ERP 不通用。

   建议改为配置：

   ```json
   "TenantLookup": {
     "Enabled": true,
     "Path": "ashx/Sys.ashx",
     "Query": "action=tenant_name&DataCenterId={DataCenterId}",
     "ResponseJsonPaths": ["tenant_name", "TenantName", "CompanyId", "data.tenant_name"],
     "TimeoutMs": 2000
   }
   ```

5. 自动申请文案写死“快普移动客户端”。

   `ProxyRequestModule.cs:1687` 到 `:1710` 的自动申请原因使用“快普移动客户端”。如果这个网关作为通用交付件，对非快普产品线会显得不通用。建议放进站点配置或移动 profile 的 `DisplayName`/`AutoRequestReasonTemplate`。

6. 外部 API 默认路径仍带 JVS 特例。

   当 `ExternalApiClients.AllowedPathPatterns` 为空时，`ProxyRequestModule.cs:1177` 到 `:1204` 会默认允许 `/api/`、`/mgr/`、`/jvs-public/`。脚本生成配置也硬编码 `"/jvs-public/*"` 和 `"/proxy/2027/jvs-public/*"`，见 `scripts/legacy/setup-demo-sites.ps1:138` 到 `:154`。

   建议取消代码里的 JVS fallback，只允许显式配置；如果保留默认，也应来自 profile，而不是散落在模块判断里。

7. 部署脚本仍硬编码 demo site key `2027`。

   `setup-demo-sites.ps1:138` 到 `:154` 生成站点清单时固定 `Key = "2027"`；`:451` 输出 fallback 时也固定 `/proxy/2027/...`。作为 demo 可接受，但如果该脚本用于交付多个系统，建议加 `-SiteKey` 参数，并同步生成 `AllowedSiteKeys`、`AllowedPathPatterns`。

## 结论

本次在 IIS + Playwright 真实浏览器下，主流程、后台审批、资源代理、移动端请求、SPA/Vue history 路由、legacy proxy fallback、WebSocket、后台更新和凭据签发都通过，没有发现运行态 404/500 或控制台错误。

最需要优先处理的不是运行失败，而是“通用交付洁净度”：zip 内原始配置带历史端口，以及若干产品线规则仍在核心代码/默认 profile 中。建议先把配置模板化和 profile 拆分做掉，再把移动 APP 字段/路径/租户查询抽成配置化 profile。
