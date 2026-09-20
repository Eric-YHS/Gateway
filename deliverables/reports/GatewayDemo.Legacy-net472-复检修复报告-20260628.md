# GatewayDemo.Legacy-net472 复检修复报告

日期：2026-06-28  
对象：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`

## 交付物

- 最终包：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`
- SHA256：`7CEDD6CF79FB8C282C4E51441F94F5EE3676F040F9B4BB8CE659A61DE749C2BC`
- 大小：`12,668,817` bytes
- 修改时间：`2026-06-28 00:47:08 +08:00`
- 原包备份：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.before-reprocess-20260628.zip`
- 原包 SHA256：`479AAD757BFAD618ADFBE9FDC773296ECDD85AE0FDED632800230044214C6D8B`

本次最终包不是手工只改压缩内容后交付，而是先同步根源码，再通过官方脚本
`E:\验证页面\scripts\legacy\package-legacy.ps1 -Configuration Release`
重新构建并覆盖生成。

## 检查方式

本次复检使用真实 IIS 站点和真实浏览器自动化，不是模拟检查。

- 解压验证目录：`E:\验证页面\_verify_reprocessed_gateway_zip_20260628_final`
- IIS Gateway 站点：`CodexGatewayReprocessedFinal20260628_34750`
- IIS Backend 站点：`CodexBackendReprocessedFinal20260628_34755`
- 业务地址：`http://localhost:34750/gateway`
- 管理地址：`http://localhost:34751/admin`
- 后端地址：`http://localhost:34755/mock/erp-main/portal`
- 浏览器报告：`E:\验证页面\_verify_reprocessed_gateway_zip_20260628_final\realcheck-output\real-browser-report.json`

最终浏览器报告统计：

- 步骤数：`26`
- 通过：`26`
- 失败：`0`
- console 错误：`0`
- page 错误：`0`
- HTTP 错误：`0`
- runId：`mqwlfu0l`

## 已发现并修复的问题

### 1. 移动 APP 授权身份匹配过宽

原包真实浏览器检查发现：新的移动请求只要复用相同 `UserId` 和 `DataCenterId`，即使 `CompanyId`、`DeviceImei` 不同，也可能继承已审批身份。该问题会导致移动设备维度审批不准确，也容易被后续研发在特殊设备、特殊机构、特殊机房场景中复现。

修复方式：

- 新增通用策略类：`src\GatewayDemo.Legacy.Web\Infrastructure\LegacyAppIdentityPolicy.cs`
- 默认只使用设备级稳定身份：`DeviceImei`、`MachineId`、`DeviceNo` 及其与机构、数据中心、应用、主机的组合。
- 账号级弱匹配不再默认参与移动 APP 审批继承。
- 为确有旧 APP 兼容需求的部署保留显式开关：`Gateway.LegacyApp.AllowAccountOnlyIdentityFallback=false`。
- `DeviceCredentialService` 不再内联散落特殊判断，改为调用统一策略。

回归结果：真实浏览器步骤 `new mobile device does not inherit approval from same account and data center` 已通过。

### 2. 路径和 URL 改写逻辑继续通用化

检查中确认代理路径、外部根路径、`/proxy/<site>`、移动根请求、SPA 深链刷新等场景相互影响。为减少后续继续增加特殊分支：

- 扩展 `GatewayPathUtility`，集中处理入口路径剥离、外部代理根路径清洗、代理站点前缀构造。
- `ProxyRequestModule` 调用 `GatewayPathUtility.StripEntryPath`。
- `ManagedReverseProxy` 调用 `GatewayPathUtility.CleanExternalProxyRootPath`。
- 保留站点配置驱动，不把 Vue、移动端、老 Web 站点分别写死。

回归覆盖：`/proxy/2027`、Vue/Vite SPA 根路径、SPA client navigation、SPA deep route refresh、API ping、资源请求均通过。

### 3. 打包/部署脚本的 MSBuild 选择不稳

原先部署脚本可能优先落到 .NET Framework MSBuild，触发 `ToolsVersion` 相关警告，也容易在不同机器上构建行为不一致。

修复方式：

- `scripts\legacy\setup-demo-sites.ps1` 的 `Resolve-MSBuildPath` 优先使用显式 `-MSBuildPath`。
- 自动查找 `vswhere` 和 Visual Studio BuildTools MSBuild。
- 最后才回退到 .NET Framework MSBuild。

回归结果：最终部署使用 VS BuildTools MSBuild `17.14.23`，构建成功，`0` warning，`0` error。

### 4. 文档补充兼容策略说明

已更新最终包内 README 和 `delivery\legacy\Package.README.md`，说明移动 APP 默认按设备级身份审批；旧 APP 如短期缺少设备号，可临时打开 `Gateway.LegacyApp.AllowAccountOnlyIdentityFallback`，但默认不打开。

## 真实浏览器覆盖项

1. health endpoint returns runtime metadata
2. business port blocks admin path
3. admin port blocks business path
4. unauthorized resource allowed by legacy-web-default profile
5. gateway initial page renders and scans without major overflow
6. portal redirects unauthorized browser to access request
7. submit browser access request
8. admin login shows pending browser request
9. approve browser request from admin
10. update browser authorization sites
11. approved browser reaches proxied mock login
12. mock ERP login through gateway and portal render
13. click proxied reports navigation
14. legacy `/proxy/2027` route works after authorization
15. Vue/Vite-style SPA root loads through gateway
16. SPA client-side navigation and API ping work
17. SPA deep route refresh returns index and API ping
18. hot update `gateway-sites.json` reloads on next request
19. restore `gateway-sites.json` after hot update check
20. mobile viewport gateway page renders responsively
21. mobile root probe returns JSON readiness
22. unauthorized mobile APP login returns JSON block and creates request
23. approve mobile request and retry APP login successfully
24. authorized mobile service request forwards with session token
25. new mobile device does not inherit approval from same account and data center
26. mobile admin page is usable after login

全部为 `PASS`。

## 环境说明

部署过程中仍出现 IIS WebSocket 功能提示：`IIS-WebSockets is not enabled or is pending restart`。本次验证的 HTTP、管理端、代理转发、资源、Vue/SPA、移动 APP 请求、热更新和真实操作流程均已通过；WebSocket 属于当前机器 IIS 功能状态，不是本次包内逻辑回归失败。

本轮未展开独立安全专项审计；移动身份匹配问题虽然也有越权风险，但它同时是移动审批逻辑错误和可通用化设计问题，已在本次处理并回归。
