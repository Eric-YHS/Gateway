# GatewayDemo.Legacy-net472 终版真实浏览器复测报告

检查时间：2026-06-28 10:49-10:52（Asia/Shanghai）

检查对象：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`

## 处理结论

已重新处理并覆盖生成交付包。终版包不是模拟检查，已从 zip 解压到独立目录并用 IIS 部署真实站点验证。

终版部署验证端口：

- 网关业务端口：`http://localhost:35350`
- 管理后台端口：`http://localhost:35351`
- Mock 后端端口：`http://localhost:35355`
- IIS 站点：`CodexGatewayFinal35350` / `CodexBackendFinal35355`

## 主要修复

- 将 `legacy-web-default` 拆成通用 SPA/API/长连接 profile，并新增 `jvs-default` 承载 JVS 专用路径。
- 将移动 APP 识别路径、字段别名、SessionKey JSON 路径、tenant lookup、UA marker、自动申请文案抽成 `LegacyAppProfile` / `LegacyAppTenantLookupOptions`。
- 外部 API client 不再隐式放行 `/mgr/*`、`/jvs-public/*`；无显式 `AllowedPathPatterns` 时只使用站点 `ExternalApiDefaultPathPatterns`，默认 `["/api/*"]`。
- 部署脚本支持 `-SiteKey`、`-PathRuleProfiles`、`-ExternalApiAllowedPathPatterns`、`-LegacyAppMobileUserAgentMarkers`，不再把站点键和 profile 写死在逻辑里。
- 修复默认配置旧端口 `33450/33451/33455`，改为 README 默认 `5050/5051/5055`。
- 修复 `*.ashx/*.svc` 和 `/socket` 被通用 profile 错误强制转到上游 origin root 的问题。
- Mock 资源从 `kuaipu-mark.svg` 改为 `erp-mark.svg`，SPA demo 文案改为正常中文。
- 交付 README 已补充 profile、外部 API 默认路径、移动端 profile 和部署参数说明。

## 真实浏览器验证结果

- 未审批访问 `/portal`：跳转申请页，表单提交成功。
- 管理后台 `/admin`：默认账号登录成功，待审核申请显示正确，批准成功。
- 审批后业务入口 `/portal`：真实代理到 mock ERP 页面。
- 静态资源：`/assets/mock.css`、`/assets/mock.js`、`/assets/erp-mark.svg` 均 200。
- 页面转发：`/reports`、`/ops`、`/proxy/2027/portal` 均 200。
- 外部 API：`/api/ping` 返回 200 JSON。
- 移动请求：`/proxy/2027/user/login`、`/proxy/2027/WCFService/PostBus.ashx?action=Echo`、`/proxy/2027/ashx/ChatUploadImg.ashx` 均返回 200 JSON。
- SPA/Vue history：`/jvs-aps-ui/`、`/jvs-aps-ui/orders`、`/jvs-aps-ui/reports/2026` 均正常；SPA 内部 API ping 为 true。
- 移动视口：390x844 下 SPA 导航和正文无明显重叠。
- WebSocket：`ws://localhost:35350/socket` 和 `ws://localhost:35350/proxy/2027/socket` 均返回 `echo:ping`。
- 热更新：修改部署目录 `gateway-sites.json` 后，`/sys.ashx?action=version` 和 `/ashx/Sys.ashx?action=tenant_name...` 无需重启返回新配置值。

## 构建和包内检查

- `MSBuild GatewayDemo.Legacy.sln /p:Configuration=Release`：0 错误。
- `scripts\legacy\package-legacy.ps1`：成功生成 zip。
- 包内 `gateway-sites.json` 可用 UTF-8 JSON 解析。
- 包内 `Web.config`：
  - `Gateway.UseDefaultUpstreamProxy=true`
  - `Admin.ManagementPort=5051`
- 包内未发现旧端口 `33450/33451/33455`。
- 包内资源为 `erp-mark.svg`，未发现 `kuaipu-mark.svg`。

## 环境注意

部署脚本在本机提示 IIS WebSocket Protocol 处于未启用或待重启状态，但最终浏览器 WebSocket 实测已通过。生产服务器仍建议按 README 启用 IIS WebSocket Protocol，并在需要时重启系统或 HTTP 服务。
