# GatewayDemo.Legacy-net472 M1-M3 修复 + R1-R6 补测报告

日期：2026-07-02
对象：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`

## 交付物

- 最终包：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`
- SHA256：`789DCAC5E9FEF0228CF2C383DB45876B7C652E598E71586AA3DF2AA86F692288`
- 大小：`12,679,721` bytes（2026-07-02 17:46）
- 覆盖前备份：`GatewayDemo.Legacy-net472.before-recheck-fix-20260702-174626.zip`
- 打包方式：官方脚本 `scripts\legacy\package-legacy.ps1 -Configuration Release`，源码取自工作区 `src`（MSBuild 0 warning / 0 error）

## 一、M1/M2/M3 修复内容

### M1 — API/AJAX 请求被拦截时返回 401 JSON，不再 302 到 HTML

- `Infrastructure/ProxyRequestModule.cs`：新增 `LooksLikeJsonApiClient` 探测（`X-Requested-With: XMLHttpRequest` / 请求体 `Content-Type: application/json` / `Accept` 含 `application/json`），命中时用新方法返回 401 JSON；真实浏览器整页导航的 `Accept` 从不含 `application/json`，浏览器路径零影响。
- `Infrastructure/LegacyAppResponseWriter.cs`：新增 `WriteAuthorizationRequiredJson`，返回 `{Status:0,Code:401,Msg,Message,GatewayBlocked:true,Site}`。
- 验证：`curl -H "Accept: application/json"` 未授权访问返回 `401` + JSON；普通浏览器访问仍是 `302`（不回归）。

### M2 — 隐藏 IIS 原生错误页的版本横幅/详细信息

- 新增 `Gateway/errors/blocked.html`（品牌化静态错误页，`<meta charset="utf-8">`，随 csproj Content 一并交付）。
- `Web.config` 新增 `system.webServer/httpErrors`（400/403/404/404.8/405/406/414/500，`existingResponse="Auto"`）。
- **踩坑记录**：最初用 `existingResponse="Replace"` 会无条件覆盖响应体，把网关自绘的 404/401 页也吞掉（回归！）；改 `PassThrough` 又会把 IIS 请求过滤产生的"空 body"错误也当成"已有响应"而不替换；最终用 IIS 默认的 `Auto` 模式，两头都对：网关自绘页（已设 `TrySkipIisCustomErrors=true`）原样保留，请求过滤类错误（`..%2f` 触发的 404.8）不再显示"IIS 10.0 详细错误"版本横幅，改为通用短消息。
- 验证：`..%2f` 请求穿越尝试从"IIS 10.0 详细错误 - 404.8"变为无版本信息的通用 404；已验证过的自绘错误页（404/401/500）内容不变。
- 遗留说明：request-filtering 触发的这一类 404.8 目前显示的是 IIS 内置通用短消息（非泄露版本，但也不是我们自己的 `blocked.html` 品牌页——IIS 在这类"从未产生响应体"的场景下命中了比我们的 `subStatusCode="-1"/"8"` 更具体的内置默认映射）。核心安全目标（隐藏版本/详细信息）已达成，品牌一致性是次要的观感问题。

### M3 — 停止向匿名请求方回显原始异常信息

- `Global.asax.cs` 的 `Application_Error`：不再把 `exception.GetBaseException().Message` 写入响应，改为固定中文提示"服务器处理请求时发生错误，请稍后重试或联系管理员。"；新增 `Trace.TraceError` 记录真实异常（复用 `ManagedReverseProxy.WriteBadGateway` 的记录方式），保证线上仍可排障。
- `Web.config`：`customErrors mode` 从 `RemoteOnly` 改为 `On`（兜底防御；`Application_Error` 本身通过 `Server.ClearError()` 已让 `customErrors` 基本不会被触发，改这个开关是双保险）。
- 说明：`Admin/Default.aspx.cs` 里两处管理员操作失败时的 `ex.Message` 回显未改动——那是登录后管理员自己看的操作反馈（如"请至少选择一个站点"），运维需要看到具体原因，且不面向匿名用户，不在本次范围内。

### 回归验证

三轮完整脚本（`hunt.js` 32 项、`login-flow.js`、`final-probe.js`）在改动前后全部重跑，结果一致，未发现回归。

## 二、R1-R6 补测结果

| 项 | 结论 | 说明 |
|---|---|---|
| R1 HTTPS 前置 | **通过** | `Gateway.ExternalScheme=https` + `DeviceCookieSecurePolicy=Always` 下，`Set-Cookie` 正确带 `secure`，`X-Gateway-Public-Origin` 等头正确显示 `https://`；登录/退出全流程无回归。因测试环境是 `localhost`（浏览器对其有安全豁免），未能复现"真实域名下纯 HTTP 连接会导致浏览器拒收 Secure cookie"这一**浏览器自身**行为限制——这不是代码问题，真实部署只要前端真的有 TLS 卸载（该配置的设计前提）就不会遇到。 |
| R2 多域名 Cookie 作用域 | **服务端逻辑通过，浏览器端受限于沙箱环境** | 直接 HTTP 请求验证：`DeviceCookieDomain=".xxx"` 正确下发 `Domain` 属性到两个子域；不兼容域名（如 `localhost`）正确降级为 host-only cookie，不会错误污染。真实 Chromium/Firefox 浏览器对这台机器上自定义主机名的直连被网络层拦截（响应带 `proxy-connection` 头，且不受 Chromium `--no-proxy-server` 参数影响，说明拦截发生在浏览器网络栈之下），这是本沙箱环境的网络限制，非产品代码缺陷。 |
| R3 WebSocket 代理 | **通过** | 起了真实 WS echo 上游服务，通过网关 `/proxy/wstest/` 代理，真实 Chromium 浏览器 `new WebSocket()` 完成握手、双向消息、正确关闭全流程。IIS-WebSockets 系统功能虽显示 `EnablePending`（等待重启），但实测功能已生效。 |
| R4 HTTP.sys 超长 URL | **通过** | 注册表参数确认已配置（`UrlSegmentMaxLength=32766` 等）；实测单个 URL 段 20000 字符仍返回 200，证明该机器上此配置已真实生效。未重启 HTTP 服务/Windows（避免影响机器上其他 IIS 站点）。全新目标服务器首次部署仍需按 README 提示重启一次使其生效。 |
| R5 SQLite 并发 | **通过** | 20 个设备并发提交申请 0 错误、全部正确入库；12 个管理台会话真并行（非顺序）点击批准不同申请，0 错误、0 丢单。`busy_timeout=15000` + WAL 按预期工作，未做代码改动。 |
| R6 跨浏览器 | **通过** | Chromium、Firefox、WebKit 三引擎（含新安装的 Firefox/WebKit）分别真实跑通：SPA 深链接刷新、客户端导航、登录、退出登录，0 差异。 |

**结论：R1-R6 均未发现需要修复的代码问题**；R2 的浏览器端验证受限于本沙箱环境网络策略，服务端逻辑已通过直接 HTTP 请求确认正确。

## 三、复测环境

- 全新解包 → MSBuild Release 重新构建（0 warning/0 error）→ 官方 `setup-demo-sites.ps1` 部署 4 站点拓扑（2388/2027/17011/9999）→ Playwright 真实浏览器复测。
- 最终验证站点保留在 IIS：`FinalPkgGw20260702`（30050/30051/30052/30053）、`FinalPkgBk20260702`（30055），可直接打开核对。
