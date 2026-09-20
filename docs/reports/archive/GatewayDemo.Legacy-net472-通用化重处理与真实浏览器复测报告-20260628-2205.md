# GatewayDemo.Legacy-net472 通用化重处理与真实浏览器复测报告

时间：2026-06-28 22:05  
处理对象：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`

## 已重新处理的交付包

- 新交付包：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`
- 原包备份：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.before-generalize-20260628_220028.zip`
- 本次源码工作目录：`E:\验证页面\_codex_generalize_zip_20260628_214643`
- 新包验证解压目录：`E:\验证页面\_verify_generalized_zip_20260628_2200`

## 通用化改造

1. 移动端请求识别集中化
   - 修改：`src\GatewayDemo.Legacy.Web\Infrastructure\MobileClientDetector.cs`
   - 新增 profile-aware 判定：`IsMobileClientRequest(HttpRequest, LegacyAppProfileOptions)`
   - 删除 `DeviceCredentialService`、`ProxyRequestModule` 中重复的移动端 UA/profile marker 判定。

2. 移动 APP 自动申请创建集中化
   - 新增：`LegacyAppAccessRequestDraft.cs`
   - 新增：`LegacyAppAccessRequestFactory.cs`
   - `ProxyRequestModule.AutoCreateLegacyAppRequest` 不再手工拼字段，统一通过工厂生成公司、申请人、原因、目标路径、站点、legacy 身份字段。

3. 公网 URL 上下文集中化
   - 新增：`GatewayPublicUrlContext.cs`
   - 统一管理：
     - `X-Gateway-Public-Origin`
     - `X-Gateway-Public-Base`
     - `X-Gateway-Proxy-Base`
     - `X-Gateway-Proxy-Site-Base`
     - root 暴露与 `/proxy/{site}` 暴露的路径选择
     - Cookie `Path` 重写
   - `ManagedReverseProxy` 已改为调用该上下文，减少 header、绝对路径、Cookie Path 三处逻辑分叉。

4. 旧式 csproj 同步
   - 修改：`src\GatewayDemo.Legacy.Web\GatewayDemo.Legacy.Web.csproj`
   - 已把新增 `.cs` 文件加入 `<Compile>`，确保 .NET Framework 旧项目真实编译。

## 构建验证

- 工作目录构建：通过
  - 命令：`.NET Framework MSBuild` 构建 `GatewayDemo.Legacy.sln`
  - 输出：`GatewayDemo.Legacy.Web.dll` 成功生成
- 新 zip 解压后部署构建：通过
  - 通过包内 `scripts\legacy\setup-demo-sites.ps1 -BuildBeforeDeploy $true` 重新构建并部署

## 真实浏览器复测

IIS 验证站点：

- 网关入口：`http://localhost:36250/gateway`
- 管理入口：`http://localhost:36251/admin`
- 后端模拟站：`http://localhost:36255/mock/erp-main/portal`

浏览器结果：

- 结果 JSON：`E:\验证页面\_verify_generalized_zip_20260628_2200\browser-real-results\latest-result.json`
- 截图目录：`E:\验证页面\_verify_generalized_zip_20260628_2200\browser-real-results`
- Chromium 检查：15/15 通过

通过项：

- 桌面端访问申请提交
- 管理端审批桌面申请
- 审批后访问门户
- `/reports` deep route
- `/proxy/2027/portal` 显式代理路径
- `/jvs-aps-ui/reports/2026` Vue/history 路由与 API
- 移动 viewport 无横向溢出
- `/api/ping` loopback 外部 API allowlist
- WebSocket echo
- 移动 APP 未授权登录返回网关 JSON block
- 管理端审批移动 APP 自动申请
- 移动 APP 审批后登录成功
- 移动 WCF service 请求携带会话成功
- 移动上传接口成功
- 管理端签发 APP credential 页面成功

额外更新检查：

- 运行中修改 `GatewayDemo.Legacy.Web\App_Data\gateway-sites.json` 的站点名。
- 使用真实 Chromium 打开网关申请页，确认新站点名已热加载显示。
- 恢复原配置后再次打开页面，确认新站点名不再显示。
- 结果：通过。

## 结论

本次不是模拟检查，已经对更新后的 zip 重新解压、重新编译、重新部署 IIS，并用真实 Chromium 完成桌面端、管理端、Vue/history、代理路径、外部 API、WebSocket、移动 APP 登录/服务/上传、credential 签发和配置热加载检查。

已把适合通用化的重复设计抽出到公共组件，当前交付包已更新。
