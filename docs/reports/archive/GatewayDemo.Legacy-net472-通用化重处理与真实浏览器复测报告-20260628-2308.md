# GatewayDemo.Legacy-net472 通用化重处理与真实浏览器复测报告

检查对象：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`

处理时间：2026-06-28 23:08

## 处理结论

已重新处理并覆盖交付包。此次不是模拟检查：先在修改工作区部署 IIS 并用真实 Chromium 完整操作一轮，再从最终 zip 重新解压、重新编译、重新部署 IIS，并再次用真实 Chromium 完整操作一轮。

最终结果：

- 最终 zip：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`
- 最终 zip 大小：12,890,230 bytes
- 原包备份：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip.bak-20260628-230608`
- 最终 zip 内容检查：140 个 entry；未发现 `obj`、运行数据库、浏览器产物、临时脚本、临时日志入包

## 已抽通用化

1. 路径候选构建已统一到 `GatewayPathCandidateBuilder`：
   - 覆盖原始 path、代理 path、解码 path、入口路径相对 path、`__root__` 回源 path、query 变体。
   - `ProxyRequestModule` 和 `ManagedReverseProxy` 共用同一套候选逻辑，减少 Vue history 刷新、资源、外部 API、匿名放行、回源根路径各写一套特殊判断。

2. path profile 已支持配置自定义定义：
   - 新增 `PathRuleProfileDefinitions`。
   - 单条 `PathRules` 支持 `TreatAsLongPolling`、`TreatAsStreaming`。
   - 新增现场前端目录、资源目录、轮询接口、流式接口时，可配置 profile 后由 `PathRuleProfiles` 引用，不需要继续改代码加路径特判。

3. 移动 APP 兼容逻辑已抽通用：
   - 移动端路径匹配、bootstrap action、root probe、UA/请求特征识别统一到 `LegacyAppCompatibilityPolicy`。
   - 移动 APP 阻断 JSON、DoActionResult、ready 响应统一到 `LegacyAppResponseWriter`。
   - `ProxyRequestModule` 与 `DeviceCredentialService` 共用这些策略。

4. 审批信息保留规则已抽通用：
   - 原仓储内的移动 APP 复审信息保留评分抽到 `LegacyReviewMergePolicy`。
   - 后续调整申请人、手机号、原因、目标路径、legacy user id 评分时不用改仓储主流程。

5. 浏览器设备复审策略已配置化：
   - 新增 `Gateway.BrowserDevice.ReviewOnSignalChange`。
   - 新增 `Gateway.BrowserDevice.SignalHeaders`。
   - 部署脚本支持 `-BrowserDeviceReviewOnSignalChange` 和 `-BrowserDeviceSignalHeaders`。

## 修改文件要点

- `src/GatewayDemo.Legacy.Web/Infrastructure/GatewayPathCandidateBuilder.cs`
- `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyAppCompatibilityPolicy.cs`
- `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyAppResponseWriter.cs`
- `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyReviewMergePolicy.cs`
- `src/GatewayDemo.Legacy.Core/Options/GatewayPathRuleProfileOptions.cs`
- `src/GatewayDemo.Legacy.Core/Options/GatewayPathRuleOptions.cs`
- `src/GatewayDemo.Legacy.Core/Options/GatewaySiteOptions.cs`
- `src/GatewayDemo.Legacy.Core/Options/GatewayPathRuleProfileCatalog.cs`
- `src/GatewayDemo.Legacy.Core/Options/GatewayOptions.cs`
- `src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs`
- `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs`
- `src/GatewayDemo.Legacy.Web/Infrastructure/DeviceCredentialService.cs`
- `src/GatewayDemo.Legacy.Web/Infrastructure/BrowserDeviceService.cs`
- `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayConfiguration.cs`
- `src/GatewayDemo.Legacy.Web/Web.config`
- `scripts/legacy/setup-demo-sites.ps1`
- `README.md`

## 真实浏览器验证

### 修改工作区验证

部署端口：

- 网关业务端口：36250
- 管理端口：36251
- mock 后端端口：36255

结果文件：

`E:\验证页面\_codex_generalize_zip_20260628_224221\browser-artifacts-36250-generalized\result.json`

结果摘要：

- 真实 Chromium 操作步骤：22
- failed：0
- networkIssues：0
- consoleIssues：0
- failedRequests：0
- 覆盖：申请、审批、授权更新、代理门户、`/reports`、`/ops`、CSS/JS/SVG 资源、JVS/Vue SPA root、SPA 内部跳转、SPA 深层刷新、移动视口、UA 变化复审、移动 APP 审批前阻断、移动 APP 审批后登录、WCF、上传接口、外部 API allowlist、长 URL、WebSocket echo

### 最终 zip 解压件验证

最终 zip 重新解压目录：

`E:\验证页面\_verify_generalized_zip_20260628_2307`

部署端口：

- 网关业务端口：36350
- 管理端口：36351
- mock 后端端口：36355

结果文件：

`E:\验证页面\_verify_generalized_zip_20260628_2307\browser-artifacts-36350-finalzip\result.json`

结果摘要：

- 最终 zip 解压后重新编译：通过
- 真实 Chromium 操作步骤：22
- failed：0
- networkIssues：0
- consoleIssues：0
- failedRequests：0
- WebSocket echo：通过
- 移动 UA 变化复审：按预期触发
- 移动 APP 审批前阻断：HTTP 200 兼容响应，`X-Gateway-Original-Status=403`
- 移动 APP 审批后登录、WCF、上传：通过

## 备注

安装脚本检测到 IIS WebSocket Protocol 需要系统重启后才能完全生效，但本次两个真实浏览器环境下 WebSocket echo 均已通过。最终 zip 内的默认 `gateway-sites.json` 已恢复为原交付默认端口 `5050/5051/5055`，未带入验证端口 `36250/36350`。
