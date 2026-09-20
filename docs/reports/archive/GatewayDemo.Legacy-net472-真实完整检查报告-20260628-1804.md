# GatewayDemo.Legacy-net472.zip 真实完整检查报告

检查对象：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`

本次独立解包目录：`E:\验证页面\_codex_real_fullcheck_zip_20260628_172542`

实际运行地址：
- 网关业务：`http://localhost:36050`
- 管理后台：`http://localhost:36051`
- Mock 后端：`http://localhost:36055`

## 真实浏览器验证结论

已用 Playwright Chromium 和 IIS 真实站点执行，不是静态模拟。

最终自动化结果：通过，见：
`E:\验证页面\_codex_real_fullcheck_zip_20260628_172542\browser-real-results\latest-result.json`

关键截图：
- 申请页：`browser-real-results\2026-06-28T10-04-36-257Z-01-gateway-apply.png`
- 审批前后台：`browser-real-results\2026-06-28T10-04-36-257Z-03-admin-before-approve.png`
- 审批后门户：`browser-real-results\2026-06-28T10-04-36-257Z-04-portal.png`
- JVS/Vue-like SPA：`browser-real-results\2026-06-28T10-04-36-257Z-05-jvs-spa.png`
- 移动视口：`browser-real-results\2026-06-28T10-04-36-257Z-06-jvs-mobile.png`
- 签发接口凭据：`browser-real-results\2026-06-28T10-04-36-257Z-07-issued-credential.png`
- 配置热加载：`browser-real-results\hotreload-site-name.png`

已覆盖：
- 桌面端提交访问申请、后台登录、批准、回到业务入口、上游 mock 登录、门户和报表访问。
- `/portal`、`/reports`、`/proxy/2027/portal` 三类入口。
- 资源加载：`/assets/mock.css`、`mock.js`、SVG、favicon。
- JVS/Vue-like history 路由：`/jvs-aps-ui/reports/2026` 直接刷新成功，`/jvs-aps-ui/api/spa/ping` 成功。
- 移动视口 390x844 无横向溢出。
- 外部 API：`/api/ping` loopback allowlist 成功，转发头包含 public/proxy base。
- WebSocket：`ws://localhost:36050/socket` 回显 `echo:ping` 成功。
- 移动 APP 请求：未授权先返回网关 JSON block，后台审批后 `/user/login` 成功，SessionKey 被捕获，`/WCFService/PostBus.ashx` 和 `/ashx/ChatUploadImg.ashx` 成功。
- 后台签发 APP HMAC 接口凭据成功。
- `gateway-sites.json` 热更新：临时改站点名后，未重启 IIS，浏览器申请页读到新站点名；随后已恢复配置。

## 结论

本压缩包在上述真实浏览器和真实 IIS 运行流程下，没有发现阻断性功能缺陷。审批、转发、资源、SPA/history、移动 APP、WebSocket、接口凭据和配置热加载都能跑通。

但代码里仍有几类“特殊逻辑”建议抽成公共策略，否则后续接入新 ERP、Vue/Vite/JVS/移动 APP 时仍容易被研发挑出边界问题。

## 建议抽取的公共逻辑

1. 路由解析与根路径暴露策略

当前路径归属和根路径映射分散在：
- `ProxyRequestModule.cs:430-528`
- `Gateway/Default.aspx.cs:202-218`
- `ManagedReverseProxy.cs:1511-1535`
- `ManagedReverseProxy.cs:1590-1635`

建议抽出 `GatewayRouteResolver` 和 `GatewayPublicPathMapper`，统一处理：
- `/proxy/<site>/...`
- `ExposeLegacyAppAtRoot`
- `__root__`
- host/request pattern/referer/cookie/root fallback
- cookie path、Location、页面申请 target 的路径转换

2. 移动 APP 请求识别和身份提取

当前 `DeviceCredentialService.BuildLegacyAppRequestDescriptor` 同时做路径分类、表单/JSON 字段提取、租户补取、身份候选、申请文案：
- `DeviceCredentialService.cs:494-579`
- `DeviceCredentialService.cs:586-614`
- `ProxyRequestModule.cs:338-365`

建议拆成：
- `LegacyAppRequestClassifier`
- `LegacyAppFieldExtractor`
- `LegacyAppIdentityResolver`
- `LegacyAppAutoRequestBuilder`

这样新增 APP 字段、登录接口、上传接口、WCF 变体时不用继续在一个大方法里叠条件。

3. PathRuleProfiles 已有雏形，但仍是代码内置清单

已有通用化基础：
- `GatewaySiteOptions.cs:26-29`
- `GatewaySiteOptions.cs:76-80`
- `GatewayPathRuleProfileCatalog.cs:9-13`
- `GatewayPathRuleProfileCatalog.cs:15-109`
- `GatewayPathRuleProfileCatalog.cs:151-168`

建议下一步允许从 `gateway-sites.json` 扩展自定义 profile，或把 profile catalog 外置为 JSON。现在 `legacy-web-default`、`jvs-default` 可以覆盖本 demo，但遇到新的 Vue base、webpack publicPath、uni-app 静态目录、私有 long-polling 路径，仍需要改代码发包。

4. Public URL 上下文应成为单一对象

当前 public origin/base/proxy base 在转发头里构造：
- `ManagedReverseProxy.cs:1376-1410`

但 Location、Set-Cookie、已有 proxy path 清洗分别在不同方法里处理：
- `ManagedReverseProxy.cs:954-987`
- `ManagedReverseProxy.cs:1538-1635`
- `ManagedReverseProxy.cs:1661-1693`

建议抽出 `GatewayPublicUrlContext`，所有 header rewrite、cookie rewrite、上游回传 URL、移动端 Upn/public base 都引用同一对象。

5. 配置热加载通过，但缺少可见诊断

热加载逻辑可用：
- `LegacyGatewayConfiguration.cs:79-99`
- `LegacyGatewayRuntime.cs:71-78`

建议增加后台“配置状态/最近加载错误”显示，或 `/healthz.ashx` 返回站点配置更新时间。现在 JSON 写坏时只 Trace，现场很难直接知道当前运行的到底是哪版配置。

6. 移动 UA/桥接头规则建议配置化

基础移动识别在：
- `MobileClientDetector.cs:8-37`
- `MobileClientDetector.cs:50-91`

`LegacyAppProfile.MobileUserAgentMarkers` 可以补私有 UA，但基础 marker 仍写死。建议把基础 marker 和项目私有 marker 合并为可配置集合，方便现场 APP 壳、企业微信/钉钉/小程序变体扩展。

## 建议保留的回归脚本

本次生成的真实浏览器脚本：
`E:\验证页面\_codex_real_fullcheck_zip_20260628_172542\real-browser-fullcheck.js`

建议迁移为正式回归用例，参数化端口和站点路径。每次交付 zip 前跑一次，至少保留以下断言：
- 申请/审批/授权后业务访问。
- `/proxy/<site>` 与根路径双入口。
- SPA history 直接刷新。
- 静态资源不 404。
- 移动 APP 未授权 JSON block、审批后登录、SessionKey 复用、上传。
- WebSocket echo。
- 配置热加载。
