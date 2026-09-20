# GatewayDemo.Legacy-net472 真实完整使用检查报告

检查对象：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`

检查时间：2026-06-28 22:18-22:26

## 本次检查方式

- 重新解压到：`E:\验证页面\_codex_real_usecheck_zip_20260628_221801`
- 使用压缩包自带脚本真实编译并部署到 IIS：
  - 网关：`http://localhost:36150`
  - 管理后台：`http://localhost:36151`
  - mock 上游：`http://localhost:36155`
- 使用 Playwright 驱动真实 Chromium 操作浏览器，不是模拟 HTTP 检查。
- 结果文件：`E:\验证页面\_codex_real_usecheck_zip_20260628_221801\browser-artifacts-36150\result.json`
- 截图目录：`E:\验证页面\_codex_real_usecheck_zip_20260628_221801\browser-artifacts-36150`

## 结论

本次真实浏览器主流程未发现阻断性问题。22 个浏览器步骤完成，网络 4xx/5xx、控制台错误、请求失败均为 0。

已实际验证通过：

- 首次访问受保护 `/portal` 会进入申请页。
- 浏览器端提交访问申请。
- 管理后台登录、查看待审、审批通过。
- 管理后台更新授权站点。
- 审批后访问代理门户、报表、运维页面。
- 代理后的 CSS/JS/SVG 资源均为 200，`mock.js` 确认执行。
- JVS/Vue 风格 SPA `/jvs-aps-ui` 可打开、内部点击 `/reports/2026` 可用、深链刷新可用。
- 同一已授权浏览器设备切到 390px 移动视口后可访问 `/reports`。
- 移动 APP 登录请求审批前返回业务 JSON 阻断，`X-Gateway-Original-Status=403`。
- 后台审批移动 APP 设备后，移动登录成功。
- 移动 WCF 请求、上传请求成功。
- loopback 外部 API 白名单请求成功。
- 4140 字符长 URL 代理成功。
- WebSocket `/socket` echo 成功。

## 需要注意的行为

同一授权 cookie 复制到“移动 UA 的新浏览器上下文”后访问 `/reports`，会进入复审页。这不是代理失败，而是设备信号变化触发复审。该行为如果符合安全设计，可以保留；如果现场用户经常因为浏览器 UA、客户端壳、WebView 版本变化被要求复审，需要把设备信号权重做成可配置策略。

## 可抽为通用方法/配置的点

1. 路径候选构建应继续集中化。
   现在 `ProxyRequestModule` 里 anonymous、external API、origin-root、legacy app 等分支各自组装 path candidates。建议抽 `GatewayRequestPathContext/GatewayPathCandidateBuilder`，统一生成：原始 path、proxy 相对 path、去掉 `__root__`、去掉 entryPath、带 query 版本。

2. Path profile 应数据化。
   `GatewayPathRuleProfileCatalog` 已经把 Vite、legacy API、realtime、JVS 收束成 profile，但 profile 仍写死在 C#。建议支持配置文件定义 profile，至少把 `/jvs-aps-ui`、`/mgr/*`、`/jvs-public/*`、`/socket`、`/signalr/*` 这类规则外置。

3. 移动 APP 兼容逻辑应独立成策略类。
   `ProxyRequestModule` 和 `DeviceCredentialService` 都在处理 legacy app path、bootstrap action、登录/服务/上传、自动申请、阻断 JSON。建议抽 `LegacyAppCompatibilityPolicy` 和 `LegacyAppErrorResponseWriter`，避免新增一个移动端接口时要改多个大类。

4. 长轮询/实时请求判断应走 path rule。
   `ManagedReverseProxy.IsLongPollingRequest` 仍硬编码 `/signalr/`、`/notifications/`、`/messagehub`、`/chathub`。建议把“长轮询/流式/超时”作为 `GatewayPathRuleOptions` 属性，统一由配置驱动。

5. 授权信息合并评分应 profile 化。
   `LegacyGatewayRepository` 里对移动申请信息有固定评分和固定 applicant 名判断。建议使用 `LegacyAppProfile.AutoRequestApplicantName`、`LoginPathPatterns`、字段权重配置，减少特定 APP 文案或路径变更造成的特殊问题。

6. 设备复审策略应显式配置。
   本次实测看到 UA 变化会触发复审。建议把浏览器指纹中的硬信号、软信号和允许漂移项抽成 `DeviceTrustPolicy`，并在后台/日志里显示触发复审的具体字段。

## 交付包层面

- zip 条目数：137
- 未发现绝对路径或 `..` 路径穿越条目。
- 本次检查只改动了解压后的检查目录，未修改原 zip。
