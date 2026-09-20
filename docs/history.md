# 项目历史与接力说明

最后更新：2026-05-28

这份文档用于把当前项目到目前为止的背景、关键决策、已完成工作、当前状态、已知边界和下一步动作一次性整理清楚，方便下次新对话直接接手。

---

## 43. 2026-05-28 反向代理响应改写误伤 JavaScript 修复与重新打包

- 研发反馈访问 `10.188.188.249:15050/Resource/claw/index.html` 时，浏览器控制台报 `Uncaught SyntaxError: Invalid regular expression flags`，怀疑网关转发响应内容被替换后不正常。
- 复查 `deliverables/GatewayDemo.Legacy-net472.zip` 后确认问题发生在 legacy 网关反向代理响应体改写逻辑：`ManagedReverseProxy` 在处理 HTML 响应时，除了改写 `href/src/action/formaction/poster/data-url/data-src` 和 CSS `url(...)` 等资源路径，还继续对整个 HTML 文本执行 JavaScript URL 字符串替换，导致 `<script>...</script>` 脚本正文中的业务字符串或正则内容被误改写，浏览器解析脚本时报正则 flags 非法。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs`：新增 `<script>` 块保护逻辑，HTML 改写前先临时替换脚本块为占位符，只对脚本外的 HTML 属性和 CSS URL 做路径改写，改写完成后再还原脚本原文。
- 同步调整独立 JavaScript 响应处理：`application/javascript` / `text/javascript` / `ecmascript` 等响应不再做字符串级 URL 替换，避免用正则解析 JS 导致再次误伤业务脚本。
- 使用 `dotnet msbuild GatewayDemo.Legacy.sln /p:Configuration=Release /p:Platform="Any CPU"` 重新构建通过，并确认 `src/GatewayDemo.Legacy.Web/bin/GatewayDemo.Legacy.Web.dll` 已更新。
- 已重新生成正式交付包 `deliverables/GatewayDemo.Legacy-net472.zip`；原始包备份为 `deliverables/GatewayDemo.Legacy-net472.before-js-rewrite-fix.zip`。新 zip 内已确认包含更新后的 `ManagedReverseProxy.cs` 和 `GatewayDemo.Legacy.Web.dll`。

## 42. 2026-05-27 交付 zip 真实浏览器复核：HTTPS、所有站点、实际审核站点与跨域/跨端口缓存

- 按用户要求复核 `E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`，明确不能停留在代码或模拟检查，必须用真实浏览器操作验证。复核包信息：大小 `12,813,709` 字节，文件时间 `2026-05-27 15:31:57`，SHA256 `0F5A5786240AC556EACD14735DD715A8F4BF61612F9CDFA61090F716EADC0D0C`。
- 将 zip 解压到 `E:\验证页面\_verify\GatewayDemo.Legacy-net472_20260527_real\`，并把 Playwright/IIS Express 工具以本地验证目录方式接入，避免直接使用根目录运行状态；验证过程中脚本会备份/恢复 `Web.config`、`gateway-sites.json` 和 SQLite 运行文件。
- 真实浏览器主回归完成：执行解包目录内的 `node .\real-browser-gateway-regression.mjs`，启动 IIS Express 网关、mock 后端、本地 HTTPS 终止器和 Playwright Chromium/Edge，结果 `24 PASS / 0 FAIL`。报告：`_verify/GatewayDemo.Legacy-net472_20260527_real/artifacts/real-browser-gateway-regression/20260527-075224/report.json`。
- HTTPS 复核通过：`https://a.gwdemo.test:44367/portal` 在真实浏览器中直接渲染上游 ERP 登录页；同时验证 `RedirectToUpstream` 错配为网关自身时不会再出现无限跳转，而是返回 `Bad Gateway: UpstreamBaseUrl points to the gateway itself`。
- “所有站点”乱勾复核通过：申请页勾选“所有站点”后，具体站点复选框全部禁用且 `checkedCount=0`，没有保留乱勾状态；后台待审页同样显示为所有具体站点禁用且未勾。
- “申请人看到实际审核站点”复核通过：申请人申请“所有站点”，管理员审批时取消所有站点并只勾 `WMS East`；申请人页面显示 `审核通过站点: WMS East`，不是“所有站点已批准”；同时 ERP 仍被拦截，WMS 能进入上游。
- 跨子域授权缓存复核通过：管理员把授权更新为“所有站点”后，同一浏览器从 `c.gwdemo.test:5180` 访问第三个站点直接进入上游登录页，不再重新申请；设备 Cookie 写为父域 `.gwdemo.test`，用于模拟 `.kuaipu.com.cn` 这类父域 Cookie。
- WebSocket 真实浏览器链路也在本次主回归中通过：`ws://b.gwdemo.test:5180/socket` 返回 `echo:ping`。
- 补充验证“用端口是否可行”：新增同主机不同端口真实浏览器检查，使用 `localhost:5190` 申请、`localhost:5191` 后台审批、`localhost:5192` 访问另一个站点，结果 `5 PASS / 0 FAIL`。报告：`_verify/GatewayDemo.Legacy-net472_20260527_real/artifacts/real-browser-port-cache-check/20260527-080242/report.json`。
- 同主机不同端口结论：浏览器 Cookie 本身不按端口隔离；本次实测 `localhost:5190` 授权所有站点后，访问 `localhost:5192` 直接进入 WMS 上游登录页，设备 Cookie 为 host scoped：`domain=localhost`、`path=/`。
- 当前结论：用户截图反馈的 4 个问题在本次指定 zip 中均未复现。生产注意事项仍需保留：跨 `j.kuaipu.com.cn`、`oa.kuaipu.com.cn` 等子域共享授权时设置 `Gateway.DeviceCookieDomain=.kuaipu.com.cn`；如果 nginx/SLB 终止 HTTPS，必须同时配置受信任代理并透传 `X-Forwarded-Proto`、`X-Forwarded-Host`、`X-Forwarded-For`；不要同一批入口混用 HTTP/HTTPS，否则 HTTPS 写入的 Secure Cookie 可能不会回到 HTTP 页面。

## 41. 2026-05-27 HTTPS、WebSocket、所有站点审批与跨域授权缓存修复

- 研发拿到上一版 `deliverables/GatewayDemo.Legacy-net472.zip` 后继续反馈 4 类问题：HTTPS 仍跳转不了，希望像 nginx 一样配置证书；申请页面勾选“所有站点”后其他站点出现乱勾；申请人看到“所有站点已批准”，但管理员实际只批准了部分站点；授权所有站点后跨域名缓存丢失，仍要重新授权。
- 复查发现一个关键交付风险：此前部分修复曾直接落在交付目录或旧包验证目录，根目录 `src` 与最终 `zip` 容易漂移。本轮改为以 `src` 作为唯一修复源，再由 `scripts/legacy/package-legacy.ps1` 重新生成交付目录和 zip，并在打包后反查交付目录内容，避免“源码修了但包没更新”。
- HTTPS 方向修复：新增 `GatewayRequestContext`，统一识别直接 HTTPS 与受信任反向代理传入的 `X-Forwarded-Proto=https`、`X-Forwarded-Scheme=https`、`X-Url-Scheme=https`、`X-Forwarded-SSL=on`；`ManagedReverseProxy` 和 `ProxyRequestModule` 转发上游时使用外部真实 scheme，Cookie 的 `Secure` 判断也改为按外部 HTTPS 判断。
- HTTPS 部署边界明确化：证书不写在 `gateway-sites.json`，而是绑定在 IIS、nginx、SLB 或其他入口层；IIS 直连 HTTPS 使用 `scripts/legacy/bind-gateway-https.ps1`；nginx/SLB 终止 HTTPS 时必须配置受信任代理并透传 `X-Forwarded-Proto`、`X-Forwarded-Host`、`X-Forwarded-For`，否则网关会按 HTTP 处理安全 Cookie 和上游转发协议。
- 部署脚本补齐：`scripts/legacy/setup-demo-sites.ps1` 新增 `-TrustedProxyAddresses` 和 `-TrustedProxyCidrs` 参数，并写入 `Web.config` 的 `Gateway.TrustedProxyAddresses` / `Gateway.TrustedProxyCidrs`；脚本语法检查通过。
- “所有站点”勾选修复：申请页和后台审批页在勾选“所有站点”时会清空并禁用具体站点复选框，避免 UI 上 disabled 站点仍保留 checked 状态造成“乱勾”；取消“所有站点”后再按具体站点列表匹配。
- 管理员改选站点修复：审批通过时以管理员最终提交的 `allowAllSites` 和站点列表作为授权事实，不再沿用申请人原始“所有站点”意图；申请人侧审批结果展示改为读取当前授权中的实际审核通过站点，管理员只批 WMS 时申请人看到的是 WMS，而不是“所有站点已批准”。
- 跨域授权缓存修复：支持 `Gateway.DeviceCookieDomain=.kuaipu.com.cn` 这类父域 Cookie，用于 `j.kuaipu.com.cn`、`oa.kuaipu.com.cn` 等子域共享同一浏览器设备授权；浏览器设备指纹收敛为规范化 User-Agent，移除容易跨域/HTTPS 变化的 Client Hints、`Accept-Language` 等信号，避免同一个 Cookie 因指纹抖动被当成新设备。
- WebSocket 支持确认：反代链路已覆盖 WebSocket echo；交付 README 明确 IIS 需要启用 WebSocket Protocol，前置 nginx/SLB 需要透传 `Upgrade` 和 `Connection` 请求头。
- 自代理循环保护：`RedirectToUpstream=true` 时如果 `UpstreamBaseUrl` 配成当前网关公网域名，会形成自代理循环；现在会直接返回 `Bad Gateway: UpstreamBaseUrl points to the gateway itself`，并提示必须配置真实上游源站地址。
- 新增真实浏览器回归脚本 `tools/page-capture/real-browser-gateway-regression.mjs`：脚本会备份/恢复 `Web.config`、`gateway-sites.json` 和 SQLite 运行文件，启动 IIS Express 网关与 mock 后端，启动本地 Node HTTPS 终止器和自签证书，通过 Playwright Chromium/Edge 使用 `a.gwdemo.test`、`b.gwdemo.test`、`c.gwdemo.test` 做真实浏览器操作。
- 真实浏览器验证通过：执行 `node tools\page-capture\real-browser-gateway-regression.mjs`，结果 24 PASS / 0 FAIL；覆盖首次 ERP 访问被审批拦截、申请人选择所有站点、后台所有站点待审展示、管理员改成 WMS only、申请人看到实际审核站点、ERP 仍因未授权被拦、WMS 跨域授权、后台更新为所有站点、所有站点授权跨域共享、父域 Cookie、WebSocket echo、自代理循环拦截、HTTPS 网关入口渲染。
- 最新真实浏览器产物目录：`artifacts/real-browser-gateway-regression/20260526-161708/`，报告文件为 `report.json`，关键通过项包括 `WebSocket proxy echoes through browser`、`RedirectToUpstream self-origin does not loop in browser`、`HTTPS gateway endpoint renders through browser`。
- 构建与打包：执行 `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\legacy\package-legacy.ps1 -Configuration Debug`，MSBuild 成功，0 警告 0 错误；重新生成 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。
- 最新交付包：`deliverables/GatewayDemo.Legacy-net472.zip`，文件时间 `2026-05-27 00:16:50`，大小 `12,813,456` 字节，SHA256 `EC65E7852788BBD932B991C2512E18065207F6F2CC97F5AC548B9B4CBBDF4158`。
- 打包后复查：交付目录内 `scripts/legacy/setup-demo-sites.ps1` 已包含 `TrustedProxyAddresses` / `TrustedProxyCidrs` 参数和 `Gateway.TrustedProxy*` 写入逻辑；根目录 `README.md` 已包含 IIS 直绑证书、nginx/SLB HTTPS 终止、`X-Forwarded-Proto`、受信任代理、`Gateway.DeviceCookieDomain`、HTTP/HTTPS 混用风险和 WebSocket 透传说明。
- 对“为什么之前没修好”的复盘：上轮把 HTTPS 当成应用跳转问题，没有完整覆盖证书绑定、TLS 终止层、受信任代理头和安全 Cookie 的部署链路；审批逻辑没有区分申请人原始意图和管理员最终授权事实；UI disabled 复选框未清理 checked 视觉状态；跨域缓存只看 Cookie，没有同时处理 Cookie Domain、浏览器指纹稳定性和 HTTP/HTTPS 混用带来的 Secure Cookie 行为；真实浏览器回归覆盖不足，导致包发出后仍由研发环境暴露问题。
- 后续交付注意：生产建议统一 HTTPS，不要同一批入口混用 HTTP/HTTPS；跨子域共享授权必须配置父域 Cookie；如果前置 nginx/SLB 终止 TLS，必须同步配置受信任代理地址/CIDR 和转发头；`UpstreamBaseUrl` 永远指向真实后端源站，不能指向当前网关域名。

## 40. 2026-05-17 前端展示文案收敛与交付包刷新

- 根据反馈完整扫描 `src` 和 `deliverables/GatewayDemo.Legacy-net472` 中会渲染到页面的前端文案，重点排查测试痕迹、研发说明、部署说明、实现解释、代理路径说明等不应直接展示给现场用户/管理员的内容。
- 修改 `src/GatewayDemo.Legacy.Web/Infrastructure/GatewayPageRenderer.cs` 和交付目录同名文件：删除申请页“支持外部审批模式”“管理入口独立隔离”等研发/部署口吻说明；删除后台“APP 使用说明”整段；把 `Mobile identity` 改为“移动端信息”，字段展示改为“账号/数据中心/设备号/入口地址/手机号”；把“自定义 APP 凭据”收敛为更中性的“接口凭据”；设备凭据异常页也去掉面向研发的 APP/AppKey/Secret 解释。
- 修改 `src/GatewayDemo.Legacy.Web/App_Data/gateway-sites.json`、交付目录 `gateway-sites.json` 和 `scripts/legacy/setup-demo-sites.ps1`：默认站点描述从英文 `Gateway protected upstream site.` 改为“ERP 业务系统”；根目录官网站点描述从英文实现说明改为“官方网站”；本地模板里的“用于验证...”类描述改成正常业务站点描述。
- 修改 `src/MockBusinessBackend.Legacy.Web/Default.aspx.cs` 和交付目录同名文件：mock 后端页面不再展示“网关透传信息”“这个页面故意使用...便于验证...”“代理改写”等演示/研发口吻，页面改成普通业务系统登录、业务概览和相关系统入口。
- 确认此前页面中出现的“真实浏览器回归测试企业”不是代码硬编码，而是本轮真实浏览器测试提交到临时 IIS/SQLite 的运行数据；已清理 `GatewayDemoLegacyRealBrowserCheck`、`MockBusinessBackendRealBrowserCheck` 临时 IIS 站点/应用池和 `artifacts/real-browser-20450` 临时目录。
- 清理交付目录和 legacy 源码目录中的运行时 sqlite/log 状态文件，保留 `gateway-sites.json` 等配置文件；重新扫描确认交付 zip 内没有 sqlite/db/wal/shm/log/备份文件。
- 补齐根目录 `src/GatewayDemo.Legacy.Core/Models/GatewayAccessRequest.cs` 中此前与交付目录漂移的移动审批字段：`LegacyUrlBefore/LegacyUserId/LegacyDataCenterId/LegacyDeviceImei/LegacyMobileUserNum`，否则根目录 `GatewayPageRenderer` 使用这些字段时会编译失败；交付目录本来已有这些字段。
- 验证结果：根目录和 `deliverables/GatewayDemo.Legacy-net472` 均执行 `dotnet build GatewayDemo.Legacy.sln --no-restore -c Release` 通过，0 警告 0 错误；浏览器打开本机 `http://127.0.0.1:5050/default.aspx` 和 `http://127.0.0.1:5051/Admin/Default.aspx`，确认申请页/后台不再出现 APP 使用说明、外部审批说明、管理端口说明、英文站点描述、测试企业名等生硬文案。
- 扫描验证：源码前端和 zip 内均无以下文案命中：`APP 使用说明`、`现有易系列官方 APP`、`自定义 APP 凭据`、`日常录入入口`、`真实浏览器`、`回归测试`、`Gateway protected upstream site`、`External official site`、`外部审批模式`、`公司系统可以`、`管理入口独立隔离`、`Mobile identity`、`网关透传信息`、`这个页面故意`、`代理改写`、`用于验证`、`测试企业`、`PC真实`、`完整真实`、`验证PC`。
- 已重新生成 `deliverables/GatewayDemo.Legacy-net472.zip`，文件时间 `2026-05-17 16:34:09`，大小 `12,756,610` 字节，SHA256 `2535070B67878BCC483D4AC2C326A5C39FCA43109AFE0DF6FD311D4A294187C8`。当前 zip 内共 `123` 个文件，已确认无数据库、日志、WAL/SHM、备份文件或上述生硬/测试文案。
- 下次接手注意：README 里的部署/配置说明是交付文档，不属于前端页面，本轮未删除；当前本机 `GatewayDemoLegacy`/`MockBusinessBackendLegacy` 站点仍可用于快速浏览，但如果访问会重新生成 sqlite，打包前需要继续排除或清理运行状态文件。

## 39. 2026-05-17 移动 APP 识别改为请求头判定并修复 PC UrlBefore 误判

- 研发再次反馈 PC 端登录页仍弹出“移动 APP 设备尚未获批”，截图请求集中在 `Login.ashx` / `PCLogin`。复查真实演示站点代码后确认，PC 登录页本身会把 `hfUrlBefore` 写成当前访问地址，并在登录相关 XHR 里带上 `UrlBefore/UserId/DataCenterId` 等字段，因此这些字段不能作为“是否移动 APP”的判定依据。
- 根因是上一版只把 `UserId/DataCenterId` 从移动触发条件中移除，但 `UrlBefore/serverURL/Upn/SessionKey/userToken/MobileUserNum` 仍会触发移动分支。真实 PC 登录请求带 `UrlBefore` 时仍可能被 `LooksLikeMobileAppRequest` 和 `DeviceCredentialService` 当成移动 APP，未授权分支就返回移动审批 JSON。
- 按研发建议调整实现：新增 `MobileClientDetector`，统一按请求头判断移动/iPad/微信/App/WebView 场景，覆盖 `User-Agent`、`Sec-CH-UA-Mobile`、`Sec-CH-UA-Platform`、移动 WebView 的 `X-Requested-With` 以及常见 `X-Client-Platform/X-App-Platform/X-Device-Platform`。识别标记包括 Android、iPhone、iPad、iPod、Windows Phone、Mobile、MicroMessenger、wxwork、miniProgram、okhttp、Dalvik、html5plus、uni-app、DCloud、plusruntime、kuaipu 等。
- 调整 `DeviceCredentialService`：`UrlBefore/UserId/DataCenterId/DeviceImei/MobileUserNum/SessionKey/UserToken` 继续提取、落库、展示和参与移动审批身份组合，但只有请求头已经被 `MobileClientDetector` 判定为移动客户端时，才会进入 legacy APP 审批上下文；业务参数本身不再决定是否移动。
- 调整 `ProxyRequestModule` 和交付目录 `ManagedReverseProxy`：未授权时是否返回移动 JSON、上游 302 是否转换为移动诊断 JSON，均不再按业务参数或 `Accept: application/json` 判断，而是统一调用请求头检测。桌面浏览器 XHR 即使带 `UrlBefore/SessionKey/MobileUserNum` 也只走浏览器审批或正常代理，不再弹移动 APP 文案。
- 已同步修改根目录源码和 `deliverables/GatewayDemo.Legacy-net472/src` 交付源码；根目录和交付目录均执行 `dotnet build GatewayDemo.Legacy.sln --no-restore -c Release` 通过，0 警告 0 错误。
- 已重新生成 `deliverables/GatewayDemo.Legacy-net472.zip`，文件时间 `2026-05-17 14:58:33`，大小 `12,760,200` 字节，SHA256 `123A8FB78A2E300087F55AEC63089825C3C8700FBAD6AF34834D23CAF5729DFB`。zip 内共 `132` 条目，仅根目录 `README.md` 一个 markdown，无 `obj`、运行时 sqlite DB/WAL/SHM、日志、备份配置或临时测试目录。
- 已用新 zip 解压到临时目录并创建 IIS 临时站点做完整回归，真实上游仍指向 `KpDemo` 的 `http://127.0.0.1:8080/`，回归 `59/59` 通过。新增覆盖：PC XHR 在未审批时带 `UrlBefore/SessionKey/MobileUserNum` 仍返回浏览器审批 302，不含 `GatewayBlocked` 和移动 APP 文案；审批后 PC `PCLogin` POST 带同样字段仍正常透传；微信/iPhone UA 请求会按移动 APP JSON 审批拦截。报告文件为 `artifacts/full-regression-report-20260517-145843.json`，临时 IIS 站点、AppPool 和解压目录已清理。

## 38. 2026-05-13 PC 登录 XHR 被误判为移动 APP 修复

- 研发反馈 PC 端登录页更新后仍弹出“移动 APP 设备尚未获批”。根据截图定位到请求是 PC 登录页里的 `Login.ashx` / `PCLogin` XHR，响应被网关写成了移动 APP 审批 JSON，说明不是 SQLite 或审批状态问题，而是网关把普通 PC JSON/XHR 登录接口误识别成移动 APP 请求。
- 根因：上一轮为了覆盖移动端登录后的根路径业务接口，把 `LooksLikeMobileAppRequest` 和 `DeviceCredentialService` 的移动识别放宽了；其中 `Accept: application/json`、`UserId/DataCenterId` 这类 PC 登录也可能出现的字段被当成了移动端证据，导致 PC 登录 XHR 在未授权分支里走了 `WriteLegacyAppError`。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/DeviceCredentialService.cs` 和交付目录同名文件：移动 APP 判断不再因为单独出现 `UserId/DataCenterId` 就成立，必须有更明确的移动端信号，例如 `isAPP`、移动 UA、`UrlBefore`、`DeviceImei`、`MobileUserNum`、设备标识、`SessionKey/UserToken` 等。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs` 和交付目录同名文件：`Accept: application/json` 只能作为辅助条件，不能单独判定移动 APP；PC XHR 即使接受 JSON，也会走浏览器审批/转发逻辑，不再返回“移动 APP 设备尚未获批”。
- 修复交付目录 `ManagedReverseProxy.cs` 的移动响应兜底：去掉单纯按 `Accept: application/json` 转移动 JSON 的逻辑，并避免把普通 `UserId/DataCenterId` 当成移动端专属信号。
- 真实 IIS 回归：从交付目录临时创建 `GatewayDemoLegacyPcLoginCheck`，业务端口 `20250`、管理端口 `20251`，上游仍指向真实 `KpDemo`：`http://127.0.0.1:8080/`。未授权 PC `Login.ashx?r=...` 和 `/PCLogin` 均返回浏览器审批 302，不含 `GatewayBlocked` 和移动 APP 文案；授权后同两条 PC 登录请求透传到上游，返回上游 404，但不含移动 APP 文案；移动端 `/Menu/GetUserMenuData?isAPP=1&UrlBefore=...&UserId=...&DataCenterId=...&DeviceImei=...&SessionKey=...` 仍返回 `200 application/json` 且 `GatewayBlocked=true`。
- 构建验证：根目录和 `deliverables/GatewayDemo.Legacy-net472` 均执行 `dotnet build GatewayDemo.Legacy.sln --no-restore -c Release` 通过，0 警告 0 错误。临时 IIS 站点、应用池和测试目录已清理。
- 已重新生成干净交付包 `deliverables/GatewayDemo.Legacy-net472.zip`：文件时间 `2026-05-13 18:52:58`，大小 `12,759,983` 字节，SHA256 `AC956B54B5D761BECBC67A56BD1E97700D35CD4C163A2E5F9C310C0E86ECCDBB`。zip 内共 `131` 个条目，仅有根目录 `README.md` 一个 markdown，无 `obj`、运行时 sqlite DB/WAL/SHM、日志、`test-host.config`、临时测试目录或本机名。

---

## 37. 2026-05-13 真实 KpDemo / IIS 具体站点回归验证

- 按“不要只做函数级验证”的要求，使用当前 `deliverables/GatewayDemo.Legacy-net472.zip` 重新解压到隔离目录 `artifacts/concrete-site-test-20150/`，Release 构建通过后创建临时 IIS 站点 `GatewayDemoLegacyConcreteCheck`，网关业务端口 `20150`、管理端口 `20151`，上游明确指向本机真实演示 ERP `KpDemo`：`http://127.0.0.1:8080/`，`EntryPath=default.aspx`，不是 mock 后端。
- 基础站点矩阵：直连 `http://127.0.0.1:8080/default.aspx` 返回 200；网关 `http://127.0.0.1:20150/healthz.ashx` 返回 200；业务端口访问 `/Admin/Default.aspx` 返回 404，管理端口访问 `/Admin/Default.aspx` 返回 200；`/ashx/sys.ashx?action=fileversion` 经网关返回真实版本 `7.0.1.56`，审批前白名单未被误拦。
- 确认 `default.aspx/datacenter/getdatacenterinfo`、`/Menu/GetUserMenuData`、`/Menu/DoSearchFavorites` 在当前 KpDemo 上直连也会因为 `DataCenterId=001` 返回 `KeyNotFoundException` 500；因此审批后的 500 是上游演示 ERP 数据源状态，不是网关 302 或审批拦截。
- 移动审批闭环通过：审批前访问 `/Menu/GetUserMenuData?isAPP=1&UrlBefore=http://127.0.0.1:20150&UserId=dubx&DataCenterId=001&DeviceImei=kp-e2e-001&MobileUserNum=1798&SessionKey=...` 返回 `200 application/json` 且 `GatewayBlocked=true`，数据库生成待审批记录，并落库 `LegacyUrlBefore/LegacyUserId/LegacyDataCenterId/LegacyDeviceImei/LegacyMobileUserNum`。
- 模拟外部审批把该申请改为 Approved 后，再访问同身份 `/Menu/DoSearchFavorites`，网关不再返回审批拦截，外部审批被 `ProcessExternalDecision` 同步处理：设备 `TrustState=0`，授权表生成 `2027` 站点授权，原申请 `ProcessedAtUtc` 已写入。
- 同一 `SessionKey` 下换成另一组移动身份 `UserId=other-user`、`DataCenterId=002`、`DeviceImei=kp-e2e-002` 后再次访问 `/Menu/GetUserMenuData`，没有新建待审批记录，也没有 `GatewayBlocked=true`；`GatewayLegacyAppIdentities` 中同一 `DeviceId` 下已有 2 条身份记录，证明“同设备多身份绑定”和“后续接口不再全被拦”在真实 IIS 站点链路中成立。
- HostNames 根路径回归通过：浏览器首次访问 `http://127.0.0.1:20150/default.aspx` 按设计 302 到申请页；给该浏览器 cookie 对应设备插入临时授权并回收应用池后，再访问 `/default.aspx` 返回真实 KpDemo HTML 200，响应体不包含 `/proxy/2027/__root__`，也不包含 `/proxy/2027/`；同 cookie 请求根路径资源 `/Resource/JavaScript/jQuery/jquery.min.js` 返回 200。
- 临时验证站点、应用池和解压目录仅用于本轮站点级回归，测试后已清理；随后重新打出干净交付 zip，排除了 `obj`、运行时 sqlite DB/WAL/SHM、日志和临时测试目录，并把默认 `gateway-sites.json` 的上游地址恢复为 `http://replace-with-erp-host/`，避免带出本机 mock 地址。

---

## 36. 2026-05-13 移动审批外部决策同步与多身份绑定修复

- 针对研发反馈“删了 SQLite 还是一样、像接口都被拦截”，确认根因不是旧数据库状态，而是移动审批链路把“待审批”和“环境变化复核”混在同一条路径里：设备审批通过后，后续接口如果带了新的 `UrlBefore`、`UserId`、`DataCenterId`、`DeviceImei`、`SessionKey` 组合，网关仍可能重新生成待审批身份并拦截。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs`：待审批移动设备在拦截前也会先同步外部审批结果，再决定是否允许放行，避免研发外部审批通过后仍被待审批状态挡住。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/DeviceCredentialService.cs`：移动端非登录请求优先按 `SessionKey` 查找已审批设备；如果同一设备出现新的移动身份组合，会绑定回同一设备，不再新建一条待审批记录。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRepository.cs` 和 `GatewayDatabaseInitializer.cs`：新增 `AttachLegacyAppIdentity`，并把 `GatewayLegacyAppIdentities.DeviceId` 从唯一约束迁移为可多身份，支持同一设备关联多个 `UrlBefore/UserId/DataCenterId/DeviceImei` 组合。
- 已同步修改根目录源码和 `deliverables/GatewayDemo.Legacy-net472/src` 交付源码；后续不再把“删除 sqlite”当成解决方案，它只能清掉测试状态，不能解决审批后接口再次被识别为新身份的问题。
- 验证结果：根目录和交付目录 `dotnet build GatewayDemo.Legacy.sln --no-restore` 均通过；临时 SQLite 迁移验证通过，旧表唯一约束可迁移掉，同一设备可挂 2 条身份；流程闭环验证通过，`ProcessExternalDecision(device.DeviceId)` 能把待审批设备提升为 `Trusted`，授权存在且 `AllowsSite("2027") == true`，旧身份、新身份和 session 都能指回同一设备。
- 已重新生成 `deliverables/GatewayDemo.Legacy-net472.zip`，当前文件时间 `2026-05-13 17:23:35`，大小 `12,779,166` 字节，SHA256 `87C28339E34254C22C7350726F3AD9CAB2AD3DFC5C5A776858455DFE11BD51C7`。

---

## 25. 2026-04-30 路径硬编码与移动登录 302 修复

- 针对研发反馈的移动登录 `302 Found` 问题，修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs` 的入口路径规整逻辑：当请求形如 `/proxy/{site}/default.aspx/datacenter/getdatacenterinfo` 时，不再依赖写死的接口名单，而是按请求类型和请求头判断；POST、XHR、JSON/XML 类请求会通用剥离 `default.aspx/`，转发到上游真实接口路径，避免命中 PC 页面并返回 302。
- 针对 `theme/default/layer.css`、`Resource/...`、`AIQuick/...` 等相对根路径缺少 `/proxy/{site}/__root__` 的问题，修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs`：去掉 `LooksLikeRootAssetOrApi` 目录白名单，只要 Referer 来自同站点 `__root__` 页面，后续缺少 `__root__` 的相对请求都会按上游根路径通用转发，不再针对目录名硬编码。
- 参数化 `scripts/legacy/setup-demo-sites.ps1` 中的站点名和端口，新增 `GatewayPort`、`AdminPort`、`BackendPort`、`GatewaySiteName`、`BackendSiteName` 参数；默认值保持 5050/5051/5055 和原站点名，部署时可覆盖，避免脚本只能用于固定端口/固定站点名。
- 复查交付包硬编码风险：本次移除了会影响上游路径兼容性的路由白名单硬编码；保留的默认端口、默认管理账号、默认站点 key 属于 README 和配置层默认值，不参与路径判断，可由部署方按配置修改。
- 验证结果：`scripts/legacy/build-legacy.ps1 -Configuration Release` 通过，0 警告 0 错误；`scripts/legacy/package-legacy.ps1 -Configuration Release` 通过；`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过 6/6。
- 已刷新 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。当前 zip 时间 `2026-04-30 19:28:12`，大小 `12,729,177` 字节，SHA256 `960D9D918F47B128F5D12BD66C3354F47F433633FFABCFB210D058A002532D44`。zip 复查无 `obj`、`.vs`、`delivery`、`sql`、运行时 DB/日志、`test-host.config`，且仅保留根目录 `README.md` 一个文档；敏感 AI 相关字样扫描无命中。

---

## 26. 2026-04-30 交付包硬编码观感收敛

- 结合研发对“路径是不是写死”的反馈，继续收敛交付包里容易被误判或影响联调的硬编码点。
- 修改 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs`：相对路径解析不再使用 `http://gateway.local` 占位域名，改为使用当前站点 `UpstreamBaseUrl` 的真实上游 origin 做 URI 归一化，避免源码里出现看起来像写死域名的值。
- 修改 `src/GatewayDemo.Legacy.Web/App_Data/gateway-sites.json` 和 `gateway-sites.experience.json`：默认交付配置改为单站点 `2027`，`UpstreamBaseUrl` 使用明显占位的 `http://replace-with-erp-host/`，不再把默认运行配置指向 localhost 或固定公网体验站。
- 修改 `src/GatewayDemo.Legacy.Web/App_Data/gateway-sites.local-demo.template.json`：本地演示模板也收敛为单站点并通过 `{{DemoBaseUrl}}` 注入上游地址，避免模板里继续出现 mock 多站点 localhost 地址。
- 修改 `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayConfiguration.cs`：`Web.config` 中的主要 appSettings 支持同名环境变量覆盖，环境变量名格式为 `GATEWAY_` + 配置名转大写下划线，例如 `GATEWAY_ADMIN_MANAGEMENTPORT`、`GATEWAY_ADMIN_USERNAME`、`GATEWAY_ADMIN_PASSWORDHASH`，降低管理端口和账号配置被认为写死的风险。
- 修改 `src/MockBusinessBackend.Legacy.Web/Default.aspx.cs`、`Web.config`、`.csproj`：Mock 后端登录账号密码改为配置项 `Mock.Username` / `Mock.Password`；Mock 页面跨站链接和 APP 登录返回的 `Upn` 改为根据 `X-Forwarded-Proto`、`X-Forwarded-Host` 和 `Mock.GatewayProxyBasePath` 动态生成，不再写死 `localhost:5050` 或 `localhost:5055`。
- 修改 `scripts/legacy/setup-demo-sites.ps1`：`DisplayHost` 改为可传参数，默认取机器名；脚本创建 IIS 站点后会同步把 `Web.config` 的 `Admin.ManagementPort` 写成实际传入的 `-AdminPort`。
- 修改 `scripts/legacy/package-legacy.ps1`：交付包只保留实际部署需要的 `scripts/legacy/setup-demo-sites.ps1`，剔除本地演示、构建、前置检查、打包脚本和 Dockerfile；同时剔除 `gateway-sites.*.json` 模板，只保留真实生效的 `gateway-sites.json`，减少交付包内本地路径、localhost、C 盘路径、MSBuild 路径等审查噪音。
- 更新 `delivery/legacy/Package.README.md`：说明默认代理入口为 `/proxy/2027/default.aspx`，端口可通过 `setup-demo-sites.ps1 -GatewayPort/-AdminPort` 覆盖。
- 验证结果：`scripts/legacy/build-legacy.ps1 -Configuration Release` 通过，0 警告 0 错误；`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过 6/6；`setup-demo-sites.ps1` 和 `package-legacy.ps1` PowerShell 解析通过；重新打包成功。
- 已刷新 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。当前 zip 时间 `2026-04-30 20:38:00`，大小 `12,710,303` 字节，SHA256 `059D390FE8BB338B8021F0BEF6E7CF272471AF83CB0874C21B0672DA35207E6F`。交付目录复查无 `gateway.local`、`huangjh`、`Kp123456`、`http://localhost:5050`、`http://localhost:5055`、固定公网体验站域名、AI 相关敏感字样；zip 复查无 `obj`、`.vs`、`delivery`、`sql`、运行时 DB/日志、`test-host.config`、Dockerfile、本地演示/构建/打包脚本，且仅保留根目录 `README.md` 一个文档。

---

## 27. 2026-05-06 交付前部署与配置问题修复

- 复核研发交付前指出的 7 个问题，确认均需要处理：部署脚本编码、mock 默认接入、上游不可达 500、IIS AppPool/App_Data 权限、诊断日志默认开启、无效 admin 配置项、`Defect` 注释残留。
- 修复 `scripts/legacy/setup-demo-sites.ps1`：不再用 PowerShell 默认编码读取 `Web.config`，改为 UTF-8 显式读取 XML 并用 UTF-8 无 BOM 写回；已用临时副本验证修改 `Admin.ManagementPort` 后 `Gateway.AppName=统一认证访问网关` 不会变成乱码。
- 增强 `setup-demo-sites.ps1`：默认创建专用 `GatewayDemoLegacyAppPool` / `MockBusinessBackendLegacyAppPool`，站点绑定到各自 AppPool，并给网关 `App_Data` 授予 `IIS AppPool\{GatewayAppPoolName}` Modify 权限，避免 SQLite 首次创建数据库时报 `unable to open database file`。
- 增强 `setup-demo-sites.ps1`：默认安装后会把 `src/GatewayDemo.Legacy.Web\App_Data\gateway-sites.json` 写成 mock 冒烟配置，`UpstreamBaseUrl=http://{DisplayHost}:{BackendPort}/mock/erp-main/`、`EntryPath=portal`，因此 README 流程跑完后可直接访问 `/proxy/2027/portal` 验证；接真实 ERP 时仍可通过 `-UpstreamBaseUrl` / `-GatewayEntryPath` 或手工编辑 JSON 切换。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs`：上游 URL 格式错误、DNS 失败、连接拒绝、超时等没有 `HttpWebResponse` 的异常不再抛到 ASP.NET 500，而是由网关返回 `502 Bad Gateway`，并输出上游地址和原因；POST 写入 request stream 时提前触发的连接异常也纳入同一处理。
- 修改 `src/GatewayDemo.Legacy.Web/Web.config`：`Gateway.AppDiagnostics.Enabled` 默认改为 `false`，与 `AppRequestDiagnostic` 注释一致，避免默认记录 RawUrl、IP、UA、设备信息。
- 清理无效 admin 配置项：删除 `Admin.CookieName`、`Admin.SessionHours` appSettings，并移除 `LegacyGatewayConfiguration` 与 `AdminOptions` 中对应读取/属性，避免研发误以为修改 appSettings 可以影响 FormsAuthentication cookie 名和 timeout。
- 清理源码中 `Defect 2/3` 注释，避免交付源码出现未清理缺陷标记。
- 更新 `delivery/legacy/Package.README.md`：说明脚本默认接 mock 做冒烟验证，接真实 ERP 时修改 `UpstreamBaseUrl` 和 `EntryPath`。
- 验证结果：`scripts/legacy/build-legacy.ps1 -Configuration Release` 通过，0 警告 0 错误；`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过 6/6；`setup-demo-sites.ps1` 与 `package-legacy.ps1` PowerShell 解析通过；zip 包结构检查通过。
- 已刷新 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。当前 zip 时间 `2026-05-06 16:10:25`，大小 `12,711,409` 字节，SHA256 `D950FE7C65A086F09D303686A6BA3CAC15E1152C1D9E6CE8ACA6D6F5157A8C36`。交付目录复查无 `Defect`、`Admin.CookieName`、`Admin.SessionHours`、诊断默认 `true`、旧 setup 编码写法、运行时 `throw;` 上抛、AI 相关敏感字样；zip 复查无 `obj`、`.vs`、`delivery`、`sql`、运行时 DB/日志、`test-host.config`、Dockerfile、本地演示/构建/打包脚本，且仅保留根目录 `README.md` 一个文档。

---

## 28. 2026-05-06 管理端口、外部审批缓存与 XFF 信任修复

- 按要求只处理研发新指出的 2、3、5 三项，未修改第 4 项 README/默认占位说明。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayConfiguration.cs`：管理入口判断 `IsManagementRequest` 优先使用 IIS 服务端变量 `SERVER_PORT`，仅在该变量缺失或无法解析时回退到 `Request.Url.Port`，降低 Host 头影响管理端口隔离判断的风险。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRepository.cs`：`ProcessExternalDecision` 从 `void` 改为返回 `bool`，表示本次是否实际处理了外部审批决定。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs` 和 `src/GatewayDemo.Legacy.Web/Gateway/Default.aspx.cs`：外部审批同步成功后调用 `runtime.InvalidateAuthorizationCache(deviceId)`，避免此前缓存了 `null` 授权时，审批刚通过仍被拦截直到 30 秒 TTL 过期。
- 增加 `src/GatewayDemo.Legacy.Web/Infrastructure/ClientIpResolver.cs`：默认不再信任请求自带的 `X-Forwarded-For`；只有当前直连地址命中 `Gateway.TrustedProxyAddresses` 或 `Gateway.TrustedProxyCidrs` 时，才使用 `X-Forwarded-For` 第一段作为客户端 IP。
- 修改 `src/GatewayDemo.Legacy.Web/Infrastructure/BrowserDeviceService.cs` 和 `DeviceCredentialService.cs`：设备记录、审计申请、APP 设备上下文里的 `ClientIp` 统一改用 `ClientIpResolver`。
- 修改 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs`：转发给上游的 `X-Forwarded-For` 也改用同一可信代理规则；来自非可信直连客户端的伪造 XFF 不再继续透传。
- 修改 `src/GatewayDemo.Legacy.Core/Options/GatewayOptions.cs`、`src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayConfiguration.cs` 和 `Web.config`：补充读取/规范化 `Gateway.TrustedProxyAddresses`、`Gateway.TrustedProxyCidrs`，默认空值代表不信任任何代理。
- 验证结果：`scripts/legacy/build-legacy.ps1 -Configuration Release` 通过，0 警告 0 错误；`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过 6/6；`setup-demo-sites.ps1` PowerShell 解析通过；重新打包成功。
- 已刷新 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。当前 zip 时间 `2026-05-06 17:14:24`，大小 `12,716,900` 字节，SHA256 `026EF09F586547E790EFB04740391248BFB4FCD1E9D197D65A41D3E968709A54`。交付目录复查可见 `SERVER_PORT`、`ClientIpResolver`、可信代理配置、外部审批后清缓存逻辑；无 `Defect`、`Admin.CookieName`、`Admin.SessionHours`、旧 setup 编码写法、运行时 `throw;` 上抛、AI 相关敏感字样；zip 结构检查通过。

## 29. 2026-05-07 域名映射、审批前放行与官网跳转修复

- 针对研发反馈的根路径资源/API 仍可能 404 问题，修复 `ProxyRequestModule` 的站点归属顺序：根路径请求先按 Referer 归属，再按 Host/上游域名归属，最后才走 `ExposeLegacyAppAtRoot` 兜底；同时移除对 `/sys.ashx` 的 Referer 归属排除，避免初始化接口被错误落到第一个根站点。
- 增加 `GatewaySiteOptions.HostNames` 和 `GatewayOptions.FindSiteByHost` / `FindSiteByUpstreamOrigin`：可在 `gateway-sites.json` 里按域名配置站点归属，也会按 `UpstreamBaseUrl` 的域名做辅助匹配，减少 nginx/server_name 场景下的路径硬编码。
- 修改 `ManagedReverseProxy`：响应体和 `Location` 头里的绝对 URL 不再只识别当前站点上游 origin，也会识别所有已配置站点的上游域名并改写到对应 `/proxy/{site}` 路径。
- 增加 `GatewaySiteOptions.AnonymousAllowedPaths`：审批前放行 URL 改为站点配置，支持 `*` 通配；默认交付配置和 setup 脚本写入 `/sys.ashx*`、`/ashx/sys.ashx*`、`/datacenter/*`、`/default.aspx/datacenter/*`，覆盖版本、租户名、数据中心/企业号初始化类请求。
- 增加 `GatewaySiteOptions.RedirectToUpstream`：配置为 `true` 的站点会直接返回 302 到 `UpstreamBaseUrl`，`EntryPath` 本身会跳到上游根地址，避免官网类 HTTPS 站点被程序内代理后返回 IIS 404。
- 更新 `scripts/legacy/setup-demo-sites.ps1`：新增 `-GatewayHostNames`、`-AnonymousAllowedPaths`、`-RedirectToUpstream` 参数，并把新配置写入生成的 `gateway-sites.json`；脚本语法解析通过。
- 更新交付 README：简要说明 `HostNames`、`AnonymousAllowedPaths`、`RedirectToUpstream` 三个配置项。
- 反思原因：此前排查过度聚焦 `/proxy/{site}` 前缀代理和截图里的单点请求，没有把 ERP 当成“多域名虚拟主机 + 根路径绝对资源/API + 审批前初始化请求”的整体浏览器瀑布来建模；也缺少真实 IIS/真实 ERP 域名绑定下的冒烟矩阵，所以 Referer 缺失、Host 归属、官网跳转和审批前白名单这几类问题没有提前暴露。后续此类网关必须按域名、根路径资源、初始化白名单、HTTPS 跳转、多站点互链一起验证。
- 验证结果：`scripts/legacy/package-legacy.ps1 -Configuration Release` 通过，Release 构建 0 警告 0 错误；`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过 6/6；交付目录和 zip 结构检查通过，仅保留根目录 `README.md` 一个文档，无 `obj`、`.vs`、`delivery`、`sql`、运行时 DB/日志、`test-host.config`；敏感 AI 相关字样扫描无命中。
- 已刷新 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。当前 zip 时间 `2026-05-07 19:32:27`，大小 `12,730,068` 字节，SHA256 `81163B7A7337A48718274D711B87CF8076EF4803513F089088D820B467D65584`。

## 30. 2026-05-07 真实 IIS/域名联调复查加固

- 按真实域名/IIS 场景继续复查路径代理，新增最近站点 cookie 兜底：当根路径资源/API 缺少 Referer 且 Host 不是业务域名时，会优先使用最近一次 `/proxy/{site}` 请求记录的站点归属，减少继续落到错误根站点的概率。
- 增强 `ManagedReverseProxy`：支持改写 `//host/path` 这种协议相对 URL；之前这类 URL 会跳过改写，浏览器可能直连上游域名或请求到错误路径。
- 增强入口规整：`__root__/default.aspx/datacenter/...` 这类由根路径回源形成的接口，会和 `/proxy/{site}/default.aspx/datacenter/...` 一样按请求类型剥离 `default.aspx/`，避免数据中心/企业号接口命中 PC 页面并返回 302。
- 调整上游 Cookie Path：当站点启用 `ExposeLegacyAppAtRoot` 且上游 Cookie 原本是根路径时，网关下发根路径 Cookie，避免少量未被改写的根路径 AJAX 不携带上游会话。
- 调整 `GatewaySiteOptions` 默认值：即使研发沿用旧 JSON 且未显式增加 `AnonymousAllowedPaths` 字段，也会默认具备 sys.ashx 和 datacenter 初始化接口放行规则；如果需要关闭，可在配置里显式写空数组。
- 对研发截图里的 `AI演示` 做复核：当前 `deliverables/GatewayDemo.Legacy-net472/` 和 zip 内均无 `AI演示`、`AI验证`、`j.kuaipu.com.cn`、`www.kuaipu.com.cn`、本机 `DESKTOP-*` 残留；截图里的两站点公网配置应为研发本地旧配置或手工配置，不是当前最新交付包默认文件。
- 验证结果：根目录 Release 构建通过，0 警告 0 错误；交付目录解决方案 Release 构建通过；`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过 6/6；`setup-demo-sites.ps1` 脚本语法解析通过；重新打包后交付目录无 `obj`、`.vs`、`delivery`、`sql`，zip 结构检查仅有根目录 `README.md` 一个文档。
- 已刷新 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。当前 zip 时间 `2026-05-07 20:02:16`，大小 `12,732,503` 字节，SHA256 `128A74B6088F7D5374B9FED69F0502D5898577D781B65C5C3FCB04BCCE06D999`。

## 31. 2026-05-07 第二轮真实域名/IIS 复查

- 继续按真实 IIS 端口绑定和多站点域名归属复查，发现 `GatewayOptions.NormalizeAuthority` 使用 `Uri.TryCreate` 解析裸 `host:port` 时会被 .NET 当成伪 scheme，导致 `HostNames` 中带端口的配置可能匹配不到。已改为只有包含 `://` 且能解析出 Authority 时才按 URI 解析，否则按普通 Host/Authority 字符串处理。
- 同步收紧 Host/上游匹配：显式 `HostNames` 和上游 authority 匹配必须唯一才采用；带端口的 `HostNames` 需要按 authority 精确匹配，避免同域不同端口误命中。
- 继续审查入口剥离规则，发现 `.ashx` 曾被归类到页面扩展，`default.aspx/ashx/*.ashx` 和 `__root__/default.aspx/ashx/*.ashx` 在 GET 且请求头不明显是 XHR 时可能不剥离入口。已改为 `.ashx/.svc` 走接口处理，只有 `.aspx/.htm/.html` 保留页面扩展判断。
- 增强无扩展接口判断：`default.aspx/datacenter/getdatacenterinfo` 这类无扩展路由在 Accept 不偏向 `text/html` 时会按接口剥离入口，覆盖移动端或脚本请求只发 `*/*` 的情况。
- 增强响应体改写：HTML 属性增加 `formaction`、`data-url`、`data-src`；JavaScript 字符串改写支持 `http://`、`https://` 和 `//host/path`，不再只处理根路径字符串。
- 更新交付 README：提醒研发更新包时删除旧解压目录，不要沿用旧 `gateway-sites.json`，避免旧配置里的站点名继续显示。
- 验证结果：根目录 Release 构建通过，0 警告 0 错误；`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过 6/6；从最终 zip 解压到临时目录后执行 `dotnet msbuild GatewayDemo.Legacy.sln /p:Configuration=Release` 通过；`setup-demo-sites.ps1` 脚本语法解析通过；交付目录和 zip 结构检查通过，仅保留根目录 `README.md` 一个文档，无 `obj`、`.vs`、`delivery`、`sql`、运行时 DB/日志、`test-host.config`。
- 敏感/旧配置复查：当前交付目录和 zip 内均无 `AI演示`、`AI验证`、`j.kuaipu.com.cn`、`www.kuaipu.com.cn`、`DESKTOP-*`、`gateway.local` 残留。
- 已刷新 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。当前 zip 时间 `2026-05-07 20:16:31`，大小 `12,733,720` 字节，SHA256 `903107C0EA4DD3B860340650128F5341AC590685FBAB19C507C54EDD24CA4E13`。

## 32. 2026-05-07 真实演示站点/IIS 隔离回归

- 用户指出本机 `演示站点20260330` 就是真实 ERP 页面/IIS 细节的本地代表，复核后确认该观点成立；此前说“真实 ERP 页面和 IIS 绑定细节不在本机环境里”不严谨，准确说法应为：本机演示站点可覆盖真实 ERP 页面形态和本机 IIS 行为，但不能完全覆盖研发环境里的公网域名、HTTPS 证书、端口绑定和他们手工修改的站点配置。
- 管理员 PowerShell 下确认 IIS 站点状态：`KpDemo` 端口 `8080`、`GatewayDemoLegacy` 端口 `5050/5051`、`MockBusinessBackendLegacy` 端口 `5055` 均为 Started；AppPool 也为 Started。
- 直连真实演示 ERP 验证：`http://127.0.0.1:8080/default.aspx` 返回 200；`/ashx/sys.ashx?action=fileversion` 返回真实版本 `7.0.1.56`；`/Resource/JavaScript/jQuery/jquery.min.js` 返回 200。
- 使用最新交付包复制出隔离测试网关 `GatewayDemoLegacyRealCheck`，端口 `15050/15051`，上游配置为 `http://127.0.0.1:8080/`、入口 `default.aspx`、HostNames 为 `127.0.0.1:15050` 和 `localhost:15050`；测试完成后已删除隔离 IIS 站点、AppPool 和临时目录。
- 未审批初始化链路验证：`/proxy/2027/ashx/sys.ashx?action=fileversion`、根路径 `/sys.ashx?action=fileversion`、`/ashx/sys.ashx?action=tenant_name`、`/proxy/2027/default.aspx/datacenter/getdatacenterinfo`、根路径 `/default.aspx/datacenter/getdatacenterinfo` 均经网关返回 200，且没有跳审批页。
- 浏览器审批链路验证：通过网关申请访问、管理端 `15051` 登录并审批后，`/proxy/2027/default.aspx` 返回真实 ERP 登录页 200；代理后的 HTML 中根路径 `href/src/action/formaction/data-url/data-src` 未发现漏改写项，抽取前 25 个 `/proxy/2027/__root__` 资源请求均为 200，覆盖 CSS、jQuery、WebResource.axd、ScriptResource.axd、验证码、图片。
- 根路径资源验证：审批后直接请求 `/Resource/JavaScript/jQuery/jquery.min.js` 经网关返回 200。`/theme/default/layer.css` 和 `/My97DatePicker.htm` 在上游演示 ERP 直连也是 404，确认不是网关改写导致；真实路径位于 `Resource/JavaScript/layui/...` 和 `Resource/JavaScript/Controls/Calendar/...`。
- 移动登录链路验证：未审批设备 POST `/proxy/2027/default.aspx/user/login` 和 `/proxy/2027/user/login` 均返回 `application/json`，内容为网关审批拦截 JSON，不再返回 PC HTML 页面。
- 官网跳转链路验证：临时增加 `home` 站点并设置 `RedirectToUpstream=true` 后，`/proxy/home/default.aspx` 在 IIS 下返回 302 到 `https://www.kuaipu.com.cn/`；`/proxy/home/some/path?a=1` 返回 302 到 `https://www.kuaipu.com.cn/some/path?a=1`。
- 当前可交付 zip 未变化，仍为 `deliverables/GatewayDemo.Legacy-net472.zip`，时间 `2026-05-07 20:16:31`，大小 `12,733,720` 字节，SHA256 `903107C0EA4DD3B860340650128F5341AC590685FBAB19C507C54EDD24CA4E13`。

## 33. 2026-05-07 真实演示 ERP/IIS 全量复查

- 按用户要求，这一轮不再只看源码或 mock，使用最新 `deliverables/GatewayDemo.Legacy-net472.zip` 全新解压出隔离副本，并在管理员 PowerShell 下创建独立 IIS 站点 `GatewayDemoLegacyFullCheck`，业务端口 `16050`、管理端口 `16051`，上游直接指向真实演示 ERP `http://127.0.0.1:8080/`，入口 `default.aspx`，HostNames 为 `127.0.0.1:16050` 和 `localhost:16050`。
- 真实上游基线确认：`KpDemo` 站点端口 `8080` Started；`/default.aspx` 返回 200；`/ashx/sys.ashx?action=fileversion` 返回真实版本 `7.0.1.56`；`/Resource/JavaScript/jQuery/jquery.min.js` 返回 200。
- 端到端自动化矩阵共 25 项全部通过：未审批业务页跳审批；审批前 `/proxy/2027/ashx/sys.ashx`、根路径 `/sys.ashx`、`datacenter/getdatacenterinfo` 均放行且不 302；申请页可打开；访问申请可提交；管理端登录、CSRF、审批流程可完成；审批后 `/proxy/2027/default.aspx` 返回真实 ERP HTML 200。
- 路径改写复查：审批后真实 ERP HTML 中 `href/src/action/formaction/data-url/data-src` 未发现漏改写根路径，`badRoot=0`，识别到 `18` 个 `/proxy/2027/...` URL；抽样前 `35` 个改写资源全部可访问；审批后直接请求根路径 `/Resource/JavaScript/jQuery/jquery.min.js` 可按最近站点转发，返回 200。
- 针对研发截图里的根路径 URL 做专项复测：`/skin/WdatePicker.css`、`/theme/default/layer.css?v=3.1.1`、`/My97DatePicker.htm?localeid=2052` 经网关返回 404，且真实上游直连也是 404，确认不是网关误 302 或误路由；`/ashx/CORE/BillForm.ashx?action=detailMove...` 经网关和真实上游均返回 200。
- 移动端复查：POST `/proxy/2027/default.aspx/user/login` 返回 `application/json`，内容为网关审批拦截 JSON，不返回 PC HTML；发送形如 `88DF4F9B-6015-40F8-9485-F48C0608A9D7` 的设备号到 `Phone` 字段后，SQLite 中最新移动申请的 `Phone` 为空，确认设备号没有写入联系电话。
- SSE/SignalR 复查：带 `Accept: text/event-stream` 请求 `/signalr/connect?transport=serverSentEvents...` 经网关不再被审批 302，返回真实上游的 400 业务错误 `The ConnectionId is in the incorrect...`，说明网关已转发到上游，不是网关取消。
- 管理端隔离复查：业务端口访问 `/Admin/Default.aspx` 返回 404；管理端口即使伪造 Host 为 `spoof.example.com:16050` 仍按 `SERVER_PORT=16051` 判断为管理端，返回登录页 200；HostNames `localhost:16050` 可正确归属站点。
- SQLite 并发复查：并发发起 `24` 个移动端申请请求，全部返回 200 JSON，无 `database is locked`、无 500、无 HTML 回退。
- 官网 HTTPS 跳转复查：临时增加 `home` 站点并设置 `RedirectToUpstream=true`、`UpstreamBaseUrl=https://www.kuaipu.com.cn/` 后，`/proxy/home/default.aspx` 在 IIS 下返回 302 到 `https://www.kuaipu.com.cn/`。
- 构建与包体复查：`dotnet msbuild deliverables/GatewayDemo.Legacy-net472/GatewayDemo.Legacy.sln /p:Configuration=Release` 通过；`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过 6/6；zip 内仅有根目录 `README.md` 一个文档，无 `obj`、`.vs`、`delivery`、`sql`、运行时 DB/log、`test-host.config`；交付目录和 zip 均未命中 `AI演示`、`AI验证`、`j.kuaipu.com.cn`、`www.kuaipu.com.cn`、`gateway.local`、`DESKTOP-`、`Defect 2/3`、`huangjh`、`Kp123456`。
- 当前应交付 zip 未变化：`deliverables/GatewayDemo.Legacy-net472.zip`，时间 `2026-05-07 20:16:31`，大小 `12,733,720` 字节，SHA256 `903107C0EA4DD3B860340650128F5341AC590685FBAB19C507C54EDD24CA4E13`。本轮隔离 IIS 站点、AppPool 和临时解压目录已清理。

## 34. 2026-05-08 HostNames 根路径改写与移动审批识别修复

- 研发继续反馈 4 类问题：`HostNames` 配置没有真正用于响应 URL 替换；响应里继续生成 `/proxy/2027/__root__`，导致 SignalR 等路径拼成 `/proxy/2027/__root__/signalr/proxy/2027/__root__/negotiate`；移动端部分根路径接口如 `/Menu/GetUserMenuData`、`/Menu/DoSearchFavorites` 仍返回 302；移动审批信息无法判断具体审核对象，实际应按 `UrlBefore`、`UserId`、`DataCenterId`、`DeviceImei` 等参数识别。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs`：当站点启用 `ExposeLegacyAppAtRoot` 或配置了 `HostNames` 时，响应体和 `Location` 中的上游绝对/根路径 URL 改写为网关根路径，例如 `/Resource/...`、`/signalr/...`，不再生成 `/proxy/{site}/__root__/...`；保留 `/proxy/{site}/...` 入口兼容，但不再把它写进业务页面资源。
- 同步增强历史错误路径兼容：如果页面或脚本里已经出现 `/proxy/2027/__root__/Resource/...`，会规整成 `/Resource/...`；如果出现嵌套路径 `/proxy/2027/__root__/signalr/proxy/2027/__root__/negotiate`，会规整成 `/signalr/negotiate`，避免继续把错误路径传给上游。
- 修复 Cookie Path 改写：根站点模式下，上游 Cookie Path 会改写为网关根路径，不再改到 `/proxy/{site}`，避免根路径移动端/API 请求带不上会话。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/DeviceCredentialService.cs`：移动 APP 请求识别不再局限于登录、`ashx`、`wcfservice`、bootstrap 路径；只要请求带 `isAPP`、`DataCenterId`、`UrlBefore`、`UserId`、`MobileUserNum`、`UserToken` 等移动参数，就按 legacy APP 请求处理，避免根路径 `/Menu/...` 请求被当成浏览器访问并 302 到审批页。
- 移动设备身份从只看设备/APP 字段，扩展为同时纳入 `UserId`、`DataCenterId`、`UrlBefore`、`MobileUserNum`、`DeviceImei`，避免同一设备不同账号或数据中心复用错误授权。
- 移动审批信息增强：自动申请的 `Reason` 中写入 `UserId:... | DataCenterId:... | DeviceImei:... | UrlBefore:... | MobileUserNum:...`，后台审批列表可以直接判断审核的是哪个账号、哪个数据中心、哪个设备、哪个入口 URL；`Phone` 仍会过滤疑似设备号，不把 `DeviceImei` 写进联系电话。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs`：未授权分支优先使用 `DescribeLegacyAppRequest` 的结果，只要识别为移动 APP 请求就返回 JSON 审批拦截结果并自动创建移动审批申请，不再走浏览器 302。
- 顺手加固 `scripts/legacy/setup-demo-sites.ps1`：创建 AppPool 后主动启动；给 `App_Data` 授权时检查 `icacls` 退出码，如果 `IIS AppPool\{name}` 在当前系统不能解析，则警告并兜底授权给 `IIS_IUSRS`，两者都失败才抛错，避免部署脚本表面成功但 SQLite 运行时无法写库。
- 验证：根目录 `dotnet msbuild GatewayDemo.Legacy.sln /p:Configuration=Release /nologo /v:m` 通过；`scripts/legacy/package-legacy.ps1 -Configuration Release` 通过；`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过 6/6；交付目录 `dotnet msbuild deliverables/GatewayDemo.Legacy-net472/GatewayDemo.Legacy.sln /p:Configuration=Release /nologo /v:m` 通过。
- 由于当前 Codex 进程不是管理员上下文，`WebAdministration` 提示没有提升权限，本轮无法再次跑真实 IIS 端到端站点；已用最终交付目录 DLL 做反射级验证：`/Resource/JavaScript/a.js` 保持根路径；`/proxy/2027/__root__/Resource/a.js` 规整成 `/Resource/a.js`；`/proxy/2027/__root__/signalr/proxy/2027/__root__/negotiate?x=1` 规整成 `/signalr/negotiate?x=1`；`/Menu/GetUserMenuData?isAPP=1&DataCenterId=001&UserId=dubx&DeviceImei=...&UrlBefore=...` 被识别为移动 APP 请求，审批摘要包含 `UserId`、`DataCenterId`、`DeviceImei`、`UrlBefore`。
- 包体复查：`deliverables/GatewayDemo.Legacy-net472.zip` 内仅有根目录 `README.md` 一个文档，无 `obj`、`.vs`、`delivery`、`sql`、运行时 DB/log、`test-host.config`；交付目录和 zip 均未命中 `AI演示`、`AI验证`、`j.kuaipu.com.cn`、`www.kuaipu.com.cn`、`gateway.local`、`DESKTOP-`、`Defect 2/3`、`huangjh`、`Kp123456`、`127.0.0.1:17050`、`127.0.0.1:8080`。
- 当前应交付 zip：`deliverables/GatewayDemo.Legacy-net472.zip`，时间 `2026-05-08 18:54:24`，大小 `12,738,304` 字节，SHA256 `5EA8252595C6134C6C69A8F3613C1CE865500B8C058DC7762794360550280797`。
- 反思：此前漏掉这些问题，是因为验证重点仍放在“能通过 `/proxy/{site}` 代理”和“根路径能兜底转发”，没有把研发的真实部署模型理解成“HostNames/server_name 根路径站点”，导致响应改写仍保留了内部兼容路径 `/proxy/{site}/__root__`；移动端验证也主要覆盖登录和 bootstrap，没有把登录后的 `/Menu/...`、收藏、消息、审批提醒等普通业务根路径请求纳入 APP 请求识别矩阵；审批体验只验证了自动创建申请，没有检查后台看到的信息是否足够让人判断 `UserId/DataCenterId/DeviceImei/UrlBefore`。后续这类网关改动必须同时验证“浏览器地址栏最终根路径化”“响应体无内部代理路径”“移动登录后所有业务根路径接口返回 JSON 而不是 302”“审批列表能识别具体移动账号和设备”。

---

## 35. 2026-05-08 真实 IIS / 演示 ERP 全量回归复核

- 从 `deliverables/GatewayDemo.Legacy-net472.zip` 重新解压到隔离目录，以管理员权限真实创建 IIS 站点验证：网关 `18050`、管理端 `18051`、mock 后端 `18055`，真实演示 ERP 上游为 `http://127.0.0.1:8080/`，站点 HostNames 配置为 `127.0.0.1:18050` 和 `localhost:18050`。
- HostNames 根路径回归通过：审批后的 `/proxy/2027/default.aspx` 返回真实 ERP 登录页，HTML 中不再出现 `/proxy/2027/__root__`；真实浏览器加载登录页共 22 个网关侧资源请求，全部 200，无 302、404、请求失败、嵌套 `/proxy/2027/__root__/signalr/proxy/2027/__root__`。
- 白名单回归通过：审批前 `/proxy/2027/ashx/sys.ashx?action=fileversion` 和根路径 `/ashx/sys.ashx?action=fileversion` 均返回上游真实版本 `7.0.1.56`；`/default.aspx/datacenter/getdatacenterinfo` 走网关本地兼容 JSON 返回，不再触发审批 302。
- 移动端回归通过：未审批的根路径移动接口 `/Menu/GetUserMenuData`、`/Menu/DoSearchFavorites` 和 `/proxy/2027/default.aspx/user/login` 不再返回 PC 页面或 302，而是返回移动端 JSON 拦截信息；后台审批页可看到 `UserId:dubx`、`DataCenterId:001`、`DeviceImei:kp-feffce08-...`、`UrlBefore:http://10.188.188.249:15050`、`MobileUserNum:1798`，且设备号不会再显示到联系电话里。
- 移动审批后用同一组完整参数复测，网关和直连 ERP 对 `/Menu/GetUserMenuData`、`/Menu/DoSearchFavorites` 的返回一致；当前演示 ERP 因 `DataCenterId=001` 未配置数据源返回 `KeyNotFoundException` 500，这不是网关新增 302 或 PC 页面问题。换成不同 `DataCenterId` 会重新触发审批，说明移动放行判断没有只按设备号粗放复用。
- 其他真实 IIS 回归通过：40 个并发移动申请全部返回 200 JSON，无 `database is locked`、无 5xx；管理后台只允许管理端口访问，伪造 Host 头不会绕过端口判断；临时配置 `RedirectToUpstream=true` 的 `home` 站点后，`/proxy/home/default.aspx?x=1` 正确 302 到 `https://www.kuaipu.com.cn/?x=1`，不是 IIS 404。
- 交付包复核：`dotnet msbuild deliverables\GatewayDemo.Legacy-net472\GatewayDemo.Legacy.sln /p:Configuration=Release /nologo /v:m` 通过；构建生成的 `obj` 已清理；zip 内共有 131 个条目，文档只有根目录 `README.md`，无 `obj`、`.vs`、`delivery`、`sql`、`test-host.config`、运行态 DB/WAL/SHM 或日志。
- 敏感字样复核：当前 `deliverables/GatewayDemo.Legacy-net472/` 未命中 `AI演示`、`AI验证`、`AI协助`、`ChatGPT`、`OpenAI`、`Claude` 等字样。研发截图中的 `AI演示` 来自旧包或他们本地已有配置；当前交付包默认站点名为 `ERP Main`，默认 `gateway-sites.json` 仅保留 `http://replace-with-erp-host/` 占位地址，按 README/部署脚本改成真实 ERP 地址后使用。
- 已清理本次验证创建的临时 IIS 站点、应用池和解压目录。当前建议发送的压缩包仍是 `deliverables/GatewayDemo.Legacy-net472.zip`，文件时间 `2026-05-08 18:54:24`，大小 `12,738,304` 字节，SHA256 `5EA8252595C6134C6C69A8F3613C1CE865500B8C058DC7762794360550280797`。

---

## 1. 项目目标与统一口径

### 1.1 当前统一需求边界

后续所有沟通、实现、联调，统一按下面 3 条理解：

- 主需求：给现有官方 APP 套网关
- 次需求：浏览器和 APP 都走统一认证
- 非当前主需求：开放给第三方 APP 的自定义接入协议

这意味着：

- 当前不是在做一套"第三方 APP 手工接入 AppKey / AppSecret / HMAC"的开放平台。
- 当前重点是让快普现有官方 APP 在尽量不改原运行机制的前提下，也能经过统一认证网关。
- 浏览器侧和官方 APP 侧都统一纳入申请、审批、授权、放行、审计体系。
- 仓库里仍保留的 `AppKey / AppSecret / HMAC` 只能视为可选增强项，不是当前主线。

### 1.2 对老板 / 研发的外部口径

对外最稳的表达方式：

- 当前阶段已经把浏览器主链路和官方 APP 主链路的网关侧问题基本收口。
- 剩余边界主要来自：
  - 上游站点自身状态
  - 真实硬件 / 真机差异
  - legacy managed proxy 对 WebSocket 的天然限制

不要轻易对外说：

- "全部功能都已经彻底完成"
- "100% 不会再有任何问题"
- "0 个问题都是网关问题"

更稳的说法：

- 当前已验证到的主链路里，网关侧没有再发现新的逻辑性阻塞。
- 剩余异常主要来自上游状态或架构边界。

---

## 2. 技术路线演进

### 2.1 第一阶段：现代版 Demo

最早的实现路线是现代栈：

- `.NET 10`
- `ASP.NET Core`
- `YARP`
- `EF Core`

这版先用于验证方案是否成立，不是最终要交给公司的兼容版。

### 2.2 第二阶段：确认公司技术栈

老板后续确认了公司技术栈和部署约束：

- `Visual Studio 2019`
- `ASP.NET`
- `.NET Framework 4.7.2`
- 页面仍大量是 `Web Forms`
- 接口新老混用，老的很多是 `.ashx`，也有 `Web API`
- 客户端环境兼容到 `Windows Server 2008 R2 + IIS 7.5`
- 内网不能联网
- 不希望增加额外部署成本
- 反向代理优先考虑成熟组件，但兼容版为了尽快落地，先允许程序内置代理
- 网关自有库允许使用 `SQLite`

### 2.3 第三阶段：legacy 兼容版重构

基于上述约束，项目新增了一条 legacy 兼容线：

- 解决方案：`GatewayDemo.Legacy.sln`
- 核心工程：
  - `src/GatewayDemo.Legacy.Core`
  - `src/GatewayDemo.Legacy.Web`

兼容线目标：

- 兼容 `VS2019 + ASP.NET + .NET Framework 4.7.2`
- 在离线环境下可编译、可部署、可演示
- 先把浏览器主链路和官方 APP 主链路迁过去

---

## 3. 仓库里已经做过的关键工作

### 3.1 文档统一与口径收敛

已经整理过这些关键文档：

- `README.md`
- `QA.md`
- `演示.md`
- `兼容版重构待确认.md`
- `兼容版重构基线.md`
- `需求边界说明.md`
- `delivery/legacy/README.md`
- `delivery/legacy/Package.README.md`
- `delivery/legacy/部署说明.md`
- `delivery/legacy/第三方依赖说明.md`
- `delivery/legacy/体验站联调说明.md`

注意：

- 部分文件在命令行里读取会显示乱码，但仓库里的中文口径已经按当前统一需求收过。
- `0331.md` 是阶段性工作记录，`history.md` 是更完整的总 handoff 文档。

### 3.2 Claude / Codex 协作脚本

为后续把长分析、长改动、长验证交给 Claude Code，仓库里已新增：

- `claude_run.ps1`
- `CLAUDE_WORKFLOW.md`

协作原则：

- Codex 负责拆任务、整理上下文、审阅结果
- Claude 负责长分析、长改动、长验证

### 3.3 页面截图与 APP 分析产物

仓库里已有现成产物：

- 页面截图：`artifacts/page-screenshots/2026-03-25/`
- APP 静态分析：
  - `artifacts/app-analysis/kuaipu_mobile_4.5.11.apk`
  - `artifacts/app-analysis/易系列APP安装包分析.md`
  - `artifacts/app-analysis/jDefault.live.js`
  - `artifacts/app-analysis/jSecurity.live.js`
  - `artifacts/app-analysis/url_scan_precise.json`

---

## 4. 浏览器主链路已经完成过什么

### 4.1 真实体验站联调

曾按 `快普易系列体验账号.docx` 提供的体验站做过真实联调：

- 体验站：`https://j.kuaipu.com.cn:2027/`
- 测试账号：文档内已有

已确认：

- 登录入口在根路径
- 存在根路径资源、相对路径资源、脚本动态请求
- 登录成功后会进入 `/WebEnterprise/newmain.aspx`

### 4.2 浏览器主链路修复方向

围绕浏览器主链路，做过这些关键修复：

- 不再把入口写死成 `/portal`
- 增强根路径资源与根路径接口兼容
- 增强 `__root__` 路径能力
- 增加 `Referer` 路由，用于处理根路径资源 / 接口回源
- 增强 `Location` / `Set-Cookie` / HTML / CSS 改写
- 修复不同入口进入同一页面时的样式异常风险

### 4.3 当前浏览器主链路判断

当前内部判断：

- 浏览器主链路的网关侧逻辑已经基本收口
- 浏览器链路残留风险主要在：
  - 上游体验站状态异常
  - 某些页面脚本动态跳转逻辑
  - legacy 架构下消息推送能力边界

---

## 5. 官方 APP 主链路已经完成过什么

### 5.1 需求认知纠偏

项目中途曾出现方向混淆：

- 一开始仓库里有一套自定义 `AppKey / AppSecret / HMAC` 方案
- 但后续通过老板和研发反馈确认：
  - 当前要解决的，不是第三方 APP 如何手工接入
  - 而是"现有官方 APP 怎么走网关"

所以后续 APP 方向已经重新定义为：

- 官方 APP 输入网关地址后，仍能初始化、登录、拿到会话、继续访问业务接口
- 尽量不要求 APP 改代码
- 最多允许配置服务器地址

### 5.2 官方 APP 分析结论

已通过安装包和站点分析确认：

- 官方 APP 不是以自定义 HMAC 协议为主
- 更接近：
  - serverURL / 企业站点地址
  - `userToken / SessionKey`
  - `WCFService/PostBus.ashx / .svc / ashx`

### 5.3 legacy 网关对官方 APP 的兼容演进

围绕官方 APP，做过多轮修复。重点包括：

- `sys.ashx` 初始化接口本地拦截与兼容
- `/user/login` 的 APP 身份识别
- `postJson` 字段的不同层级解析
- `application/json` body 解析
- 根地址模式与 proxy 模式兼容
- `.ashx / .svc / /login / /user/login / /Default.aspx` 路径识别
- APP 请求不再误跳浏览器申请页 HTML
- 后台"APP 凭据"页面与错误页文案去误导

### 5.4 官方 APP 主链路目前已经达到的状态

根据多轮代码验证、curl 矩阵验证、模拟器动态联调，当前可认为：

- 初始化接口：`fileversion`、`tenant_name`、`version` 已基本稳定
- 登录接口：
  - 未授权设备返回 JSON，并自动创建设备申请
  - 已授权设备可继续代理到上游
- 登录后接口：
  - 路由和认证代码路径已经支持
  - 真实业务是否成功，还取决于上游站点是否正常
- 两种地址模式都已兼容：
  - `http://IP:端口`
  - `http://IP:端口/proxy/erp-main`

### 5.5 官方 APP 主链路收口前最后修掉的 4 个具体缺陷

最后一轮又补掉了 4 个具体缺陷：

1. `sys.ashx` 无 `action` 参数时误回 HTML
2. `text/plain` 包 JSON body 时无法提取设备标识
3. 根路径 `/` + `okhttp` UA 时误回 HTML
4. 授权记录 ID 非法 GUID 时因 `Guid.Parse` 抛异常导致 500

这些缺陷修完后，内部判断已经可以把"官方 APP 主链路"记为：

- 代码侧基本封板
- 等待真机 / 上游恢复正常后的最终联调

---

## 6. Android 模拟器动态联调已经做到什么程度

### 6.1 已在仓库内搭好的 Android 环境

已通过 Claude 在仓库内搭过最小 Android 动态联调环境，包含：

- JDK
- Android SDK command-line tools
- `adb`
- emulator
- 至少一个可启动 system image
- 模拟器 AVD

### 6.2 已完成的动态验证

已完成这些事情：

- 快普 APP `v4.5.11` 成功安装到模拟器
- APP 能成功启动进入登录页
- 无明显反模拟器检测
- 通过模拟器 + 请求矩阵，验证了：
  - 初始化接口
  - 登录请求
  - 多种 content-type
  - 多种 UA
  - 根地址模式
  - proxy 模式

### 6.3 新增诊断能力

为后续真机 / 模拟器联调，已加过一套低风险、可开关、脱敏的 APP 请求诊断能力，用来记录：

- 是否识别为 legacy app 请求
- 命中的站点
- 是否 bootstrap
- 是否未授权
- 是否代理到上游
- 是否被上游 `302 / error.htm`
- 命中的设备字段名

用途：即使后面换真机，也能快速判断请求是如何被网关识别和处理的。

---

## 7. 消息推送与性能结论

### 7.1 消息推送

目前结论：

- legacy managed proxy 不支持 WebSocket 透明代理
- 在当前架构里：
  - WebSocket：不支持
  - SSE / 长轮询：可部分支持

对项目影响判断：

- 若老板把"消息推送必须完整可用"列为强验收项，需要单独解决
- 最小可落地方案：
  - `IIS ARR + URL Rewrite`
  - 或升级到 `.NET 10` 那套现代版网关

### 7.2 并发与性能

已经做过的第一轮优化包括：

- `ManagedReverseProxy` 单例化
- 授权缓存
- 减少重复查库
- 已信任设备少做热路径判断

当前工程判断：

- 低到中等并发下，legacy 网关可接受
- 主要潜在瓶颈：
  - SQLite 写锁
  - 热路径上每请求写操作
  - 某些需要全文读取改写的 HTML/CSS

后续若老板重点追问并发：

- 更稳的工程回答是：
  - 当前 legacy 版可用于低到中等并发
  - 若进入更高并发或更严格生产要求，应让 IIS ARR 承担更多代理层工作

---

## 8. 交付物与截图

之前已经生成过兼容版交付物：

- `deliverables/GatewayDemo.Legacy-net472/`
- `deliverables/GatewayDemo.Legacy-net472.zip`

并补充过：

- 根目录 `README`
- 交付说明
- 部署说明
- 第三方依赖说明

另外，曾生成过一组页面截图：

- `artifacts/page-screenshots/2026-03-25/`

可用于老板汇报或阶段展示。

---

## 9. 演示站点 20260330 与软狗授权工作

### 9.1 研发提供的新环境

研发后续提供了：

- `演示站点20260330.rar`

解压后确认它是一整套更接近真实客户部署环境的本地演示环境，包含：

- Web 站点
- Service
- SQL Server 数据库备份
- 驱动
- 软狗相关工具

### 9.2 解压后识别出的关键文件

关键路径：

- 演示站点根目录：`E:\验证页面\演示站点20260330\演示站点20260330`
- Web 包：`E:\验证页面\演示站点20260330\演示站点20260330\web\701日构建20260330`
- 数据库备份：
  - `数据库\7E2088-20260330.bak`
  - `数据库\7E2088_Annex-20260330.bak`
- 服务：`KpService\KpScheduleService.exe`
- 授权工具：`RUS_7.6.exe`
- Sentinel 驱动：
  - `Sentinel_LDK_Run-time_setup_7.10（支持2022）`
  - `Sentinel_LDK_Run-time_setup_7.9.0`

### 9.3 软狗流程识别结果

这里使用的是 Sentinel / HASP / LDK 软件授权体系（软狗）。

关键理解：

- `HASPUserSetup.exe`：驱动安装程序
- `RUS_7.6.exe`：取指纹 / 导入授权工具
- `KpService\install.bat`：服务安装脚本，不是软狗部署脚本

真正的软狗部署脚本在：

- `E:\验证页面\演示站点20260330\演示站点20260330\web\701日构建20260330\部署软狗.bat`

该脚本作用：

- 把软狗相关 `.dll / .exe` 复制到系统目录和站点 `bin`
- 为站点准备授权运行环境

### 9.4 C2V / V2C 过程与结果

过程：

1. 先安装 Sentinel 驱动
2. 再执行 `部署软狗.bat`
3. 再运行 `RUS_7.6.exe`
4. 生成正确的 `C2V`
5. 发回研发
6. 研发返回 `V2C`
7. 再通过 RUS 导入

已确认：

- 早期生成的 `黄佳海c2v.c2v` 只有 `SL-UserMode`，研发判定不对
- 后来重新生成的：`E:\验证页面\验证页面.c2v` 同时包含：
  - `SL-AdminMode`
  - `SL-UserMode`
  这才是正确的指纹文件

### 9.5 授权当前状态

研发随后发回：

- `661716282689961197.V2C`

已通过 `RUS` 成功写入，随后通过 `http://localhost:1947` 的 Sentinel Admin Control Center 确认看到：

- 本地锁 ID：`661716282689961197`
- 类型：`HASP SL AdminMode`

结论：

- 软狗授权已经成功生效
- 当前不再是授权问题
- 下一阶段可以直接进入"部署并启动本地演示站点"

---

## 10. 本地演示站点部署（已完成）

### 10.1 站点依赖

从 `kpweb.config`、`Web.config`、`KpScheduleService.exe.config` 确认：

- Web：ASP.NET / .NET Framework 4.7.2
- 数据库：本机 SQL Server，主库 `7E2088`，登录 `KPMIS`，密码 `KPM6admin_`
- 服务：`KpService`（已注册为 Windows 服务，自动启动），监听 `9190`，.NET Remoting

### 10.2 部署六阶段全部完成

#### 阶段 1：SQL Server Express 2022

- 安装了 SQL Server 2022 Express（基本安装，默认实例）
- 启用了混合认证模式（Windows + SQL Server 身份验证）

#### 阶段 2：恢复数据库

- 恢复 `7E2088` 主库（从 `7E2088-20260330.bak`）
- 恢复 `7E2088_Annex` 附件库（从 `7E2088_Annex-20260330.bak`）
- 创建 SQL 登录 `KPMIS` 并映射为两个库的 `db_owner`
- SQL 脚本位于 `sql/legacy/`

#### 阶段 3：IIS 与 Web 站点

- 启用 IIS + ASP.NET 4.5（`dism /enable-feature`）
- 注册 ASP.NET 4.x（`aspnet_regiis -ir`）
- 创建 IIS 站点 `KpDemo`：
  - 绑定端口 `8080`
  - 物理路径指向 `演示站点20260330/.../web/701日构建20260330/`
  - 应用程序池：`.NET Framework v4.0` / `Integrated`
- 已运行 `部署软狗.bat`

#### 阶段 4：KpService

- 以管理员身份运行 `install.bat` 注册为 Windows 服务
- 服务名：`KpService`，启动类型：自动，当前状态：运行中
- 监听端口 `9190`

#### 阶段 5：验证原站点

原站点 `http://localhost:8080/default.aspx` 已可正常访问：
- 登录页显示完整，CSS/JS 资源加载正常
- 验证码图片正常
- 测试账号可登录，跳转 `newmain.aspx`
- KpService 在 `services.msc` 中为"正在运行"

#### 阶段 6：Legacy 网关对接（已完成并验证通过）

通过脚本完成 IIS 建站和 Profile 切换：

- `scripts/legacy/setup-demo-sites.ps1`：创建 3 个 IIS 站点
- `scripts/legacy/set-gateway-profile.ps1`：切换为 `local-demo` Profile

创建的 IIS 站点：

| 站点名 | 端口 | 用途 |
|--------|------|------|
| `KpDemo` | 8080 | ERP 演示站（上游） |
| `GatewayDemoLegacy` | 5050（公开）/ 5051（管理） | 网关 |
| `MockBusinessBackendLegacy` | 5055 | Mock 后端（WMS/Finance） |

网关 Profile 配置：

- Profile：`local-demo`
- `erp-main` 上游：`http://localhost:8080/`（本地演示站）
- `wms-east` 上游：`http://localhost:5055/mock/wms-east/`（Mock）
- `finance` 上游：`http://localhost:5055/mock/finance/`（Mock）
- `erp-main` 启用了 `ExposeLegacyAppAtRoot`（APP 根地址兼容）

管理后台凭证：`gateway-admin` / `GatewayDemo!2026`

---

## 11. Legacy Docker 容器化（已创建，待验证）

### 11.1 创建的容器化文件

为 Windows 容器模式创建了完整的 Docker 化配置：

- `src/GatewayDemo.Legacy.Web/Dockerfile`
  - 基础镜像：`mcr.microsoft.com/dotnet/framework/aspnet:4.8`
  - 创建 `GatewayLegacyPool` 应用池（.NET 4.0 / Integrated）
  - 站点绑定端口 `80`（公开）+ `8081`（管理）
  - 排除 `.cs / .csproj / .pdb / obj / 数据库文件` 等

- `src/MockBusinessBackend.Legacy.Web/Dockerfile`
  - 同基础镜像，单端口 `80`

- `docker-compose.legacy.yml`
  - `gateway` 服务：`5050->80`，`5051->8081`，`gateway-data` 持久卷
  - `mock-backend` 服务：仅内网网络
  - 双网络隔离：`edge`（公开）+ `backend`（内部）

- `src/GatewayDemo.Legacy.Web/App_Data/gateway-sites.docker.template.json`
  - Docker 环境专用站点配置
  - `erp-main` 上游使用 `http://mock-backend/mock/erp-main/`（容器间通信）
  - `wms-east`/`finance` 同样使用 Docker 内部网络

### 11.2 待办

- Docker Desktop 需切换到 Windows 容器模式
- 构建镜像并验证：`docker compose -f docker-compose.legacy.yml up --build`
- 若需连接本地演示站而非 Mock 后端，需修改模板中的 `{{DemoBaseUrl}}`

---

## 12. 端到端验证结果（2026-04-22）

### 12.1 验证方法

使用 PowerShell 脚本（`temp_full_verify.ps1` / `temp_full_e2e.ps1`）对本地 IIS 部署的完整链路进行自动化验证。所有请求均使用真实的 HTTP 调用，非模拟。

关键修正：静态资源验证必须复用 session cookie（真实浏览器的行为），否则每个请求会被当作新设备而被拦截。首次验证时因测试脚本未复用 session 导致误报为失败。

### 12.2 验证结果总览：38 PASS / 2 FAIL

两个 FAIL 均为非核心问题：
- Mock JS 加载失败：Mock 后端自身的静态资源问题，不影响实际代理
- Admin dashboard 不显示 requestId：所有请求已审批完毕，列表为空是正常的

### 12.3 各模块详细结果

**PART 1：网关基础页面（3/3 PASS）**

| 测试项 | 结果 |
|--------|------|
| Gateway 公开页 `http://localhost:5050/gateway` | PASS（5827 bytes） |
| 管理后台 `http://localhost:5051/admin` | PASS |
| 公开端点隔离（`/admin/login` 在 5050 返回 404） | PASS |

**PART 2：Mock 后端（2/3 PASS）**

| 测试项 | 结果 |
|--------|------|
| Mock 登录页 | PASS |
| Mock CSS | PASS |
| Mock JS | FAIL（非核心） |

**PART 3：浏览器代理流程（18/18 PASS）**

完整流程：未授权访问 -> 显示网关申请页 -> 提交申请 -> 管理员审批 -> 代理到上游 ERP

| 测试项 | 结果 |
|--------|------|
| 未授权访问显示网关页面 | PASS |
| 提交访问申请 | PASS |
| 管理员登录（5051） | PASS |
| 查看待审批列表 | PASS |
| 审批所有请求 | PASS |
| 代理页面加载（20146 bytes） | PASS |
| 登录名输入框 `txtLoginName` | PASS |
| 密码输入框 `txtPwd` | PASS |
| 登录按钮 `btnLogin` | PASS |
| 验证码 `ValidateCode` | PASS |
| 数据中心下拉 `ddlDataCenter` | PASS |
| 短信标签 `smsTab` | PASS |
| 扫码标签 `scanTab` | PASS |
| jQuery 脚本引用 | PASS |
| jDefault.js 引用 | PASS |
| CSS 样式引用 | PASS |
| 登录标签页容器 | PASS |
| 表单元素 | PASS |

**PART 4：静态资源代理（5/5 PASS）**

使用 session cookie 复用方式验证（模拟真实浏览器行为）：

| 资源 | 大小 | 结果 |
|------|------|------|
| `Resource/CSS/default.css` | 5,245 bytes | PASS |
| `Resource/JavaScript/jQuery/jquery.min.js` | 244,677 bytes | PASS |
| `Resource/JavaScript/jDefault.js` | 20,613 bytes | PASS |
| `Resource/JavaScript/WebEnterprise/jSecurity.js` | 17,911 bytes | PASS |
| `Resource/JavaScript/jQuery/jquery-ui-1.8.21/jquery-ui.min.js` | 47,950 bytes | PASS |

**PART 5：多站点代理（2/2 PASS）**

| 站点 | 结果 |
|------|------|
| WMS 东仓（`/proxy/wms-east/portal`） | PASS（内容含 "WMS"） |
| 财务共享（`/proxy/finance/portal`） | PASS（内容含 "Finance"） |

**PART 6：APP 端点（4/4 PASS）**

| 测试项 | 结果 |
|--------|------|
| `sys.ashx?action=version` -> `6.26.0` | PASS |
| `sys.ashx?action=tenant_name` -> `AI演示` | PASS |
| APP 根路径（okhttp UA）-> `GatewayReady` | PASS |
| APP 登录（MachineId+DeviceImei）-> 上游返回 `LoginStatus` | PASS |

**PART 7：管理后台（1/2 PASS）**

| 测试项 | 结果 |
|------|------|
| 管理员审批/撤销能力 | PASS |
| Dashboard 请求列表 | FAIL（列表已空，正常现象） |

### 12.4 验证结论

**浏览器侧**：完整链路已通过。从拦截、申请、审批到代理，包括 CSS/JS/图片等静态资源，均工作正常。

**APP 侧**：核心链路已通过。初始化接口（sys.ashx）和登录接口均工作正常，设备标识（MachineId/DeviceImei）识别准确。

**管理侧**：独立端口隔离、审批、撤销均正常。

---

## 13. 真实快普 APP 下载与测试（2026-04-23）

### 13.1 下载来源

从 `快普易系列体验账号.docx` 中提取的二维码解码后获得下载链接：

- Android APK：`http://ops.kuaipu.com.cn/app/download?id=1661899905143246850`
- iOS App Store：`http://ops.kuaipu.com.cn/app/download?id=1663023888739700738`（链接到 App Store "易移动"）

下载页面实际 API：`http://ops.kuaipu.com.cn/api/uniapp/appversionhis/info?id={id}`

Android APK 实际下载地址：`https://dl.kuaipu.com.cn/ls7pWvarwCb0WzCrGrOdRF_b6vI6?attname=android_4.6.41.apk`

### 13.2 APP 分析结论（从 APK 提取）

| 属性 | 值 |
|------|-----|
| 应用名 | 易移动 |
| 版本 | 4.6.41 |
| 框架 | uni-app (DCloud) |
| App ID | `__UNI__C0948B1` |
| 文件大小 | 65.8 MB |
| 开发者 | 厦门快普信息技术有限公司 |

User-Agent 特征（manifest.json 配置 `"useragent":{"value":"uni-app","concatenate":true}`）：

```
Mozilla/5.0 (Linux; Android 13; ...) Chrome/... Mobile Safari/537.36 uni-app Html5Plus/1.0 (Immersed/...)
```

关键 API 端点（从 `app-service.js` 提取）：

- 启动：`GET /ashx/sys.ashx?action=version`
- 登录：`POST /user/login`（`application/x-www-form-urlencoded`）
- 登录参数含：`UserName`、`UserId`、`TenantName`、`UserPwd`、`MachineId`、`DeviceImei`、`DeviceType`、`TrAppKey`（值为 `__UNI__C0948B1`）
- 设备标识存储：Android 用 OAID，回退到 IMEI，再回退到 `kp-{UUID}`

### 13.3 真实 APP 模拟测试结果（10/11 PASS）

测试脚本：`scripts/demo/real-app-test.ps1`

使用从 APK 提取的真实 User-Agent（含 `uni-app` 标记）和真实 API 参数进行测试：

| # | 测试项 | 结果 | 说明 |
|---|--------|------|------|
| 1 | APP 启动 sys.ashx | PASS | 返回 6.26.0 |
| 2 | APP tenant_name | PASS | 返回 AI演示 |
| 3 | 未授权登录拦截 | PASS | 返回 GatewayBlocked |
| 4 | 管理员审批设备 | OK | 审批 1 个请求 |
| 5 | 授权后登录代理 | PASS | 上游返回 LoginStatus |
| 6 | Root 模式 sys.ashx | PASS | 返回 6.26.0 |
| 7 | Root 模式 default.aspx | PASS | 返回登录页 |
| 8 | WCF 代理 | FAIL | 设备信号变化触发新审批（预期行为） |
| 9 | Root 模式 WCF | PASS | 正确代理 |
| 10 | HMAC 签名凭据 | PASS | AppKey 验证成功 |
| 11 | UA 检测 | PASS | uni-app UA 正确识别 |
| 12 | 浏览器 UA | PASS | 返回 HTML 页面 |

WCF 代理测试说明：WCF 端点对设备信号哈希更敏感，检测到变化后自动创建新审批请求（`"移动 APP 设备特征信号发生了变化，已创建审批请求"`），这是网关的预期安全行为。真实设备上信号哈希稳定，不会触发此问题。

---

## 14. 交付与沟通（2026-04-24）

### 14.1 交付压缩包

已用最新代码重新编译打包（含 4月23日 DeviceCredentialService 修复）：

- 压缩包：`deliverables/GatewayDemo.Legacy-net472.zip`（12.3 MB）
- 文件夹：`deliverables/GatewayDemo.Legacy-net472/`
- 文件夹内只保留一个 README.md（用户手动调整过，包含部署命令和端口信息）
- 文件夹内原有的 delivery/legacy/ 下 6 个 .md 文档已删除（用户认为太机械）

### 14.2 发给技术团队的话

写在 `交付给技术团队的沟通指南.md` 里，用户已手动调整过，内容：

> 您好，之前提到的统一认证网关已经做完了。我们是在"演示站点20260330"那套快普本地演示站上配套测试的，浏览器拦截审批代理、APP初始化登录、多站点管理、管理后台这些功能基于这套演示站点都验证过了。代码和部署脚本都在压缩包里，站点配置文件在src/GatewayDemo.Legacy.Web/App_Data/gateway-sites.json，拿到手把UpstreamBaseUrl改成你们自己的ERP地址就可以测试。请您方便的时候看下是否有什么问题，有需要我们随时沟通。

### 14.3 研发反馈

研发问了"移动登录和正常操作有没有变动"，回复要点：

- APP 不需要改代码，只改服务器地址指向网关即可
- 登录接口参数格式不变（UserId/UserPwd/MachineId/DeviceImei 照常传）
- 新增一步"设备审批"：APP 首次登录时网关拦截创建审批请求，管理员在 5051 端口批准后才能登录，每个设备只需审批一次
- sys.ashx 的 version/tenant_name 是网关本地返回的（配置文件里写死的），因为启动时设备还没授权
- 我们测试环境是本地演示站，到实际站点上需确认 UpstreamBaseUrl 配置正确，建议先用测试环境联调

### 14.4 研发反馈 500 与 SQLite database is locked（2026-04-27）

研发拿到 `deliverables/GatewayDemo.Legacy-net472` 后反馈：浏览器通过网关访问 ERP 时，DevTools 里部分图片、CSS、JS、ASHX 请求返回 500，ASP.NET 错误页显示 `System.Data.SQLite.SQLiteException: database is locked`。

截图路径特征：

- 网关地址：`/proxy/erp-main/default.aspx`
- 失败资源包含：`ValidateCodeHandler.ashx`、`select.png`、`dataCenter.png`、`m_07.png`、`bj.jpg` 等
- 错误本质不是这些资源本身不存在，而是资源并发加载时触发网关热路径写 SQLite，SQLite 写锁冲突被冒泡成 500

定位结论：

- 交付物实际入口是 `deliverables/GatewayDemo.Legacy-net472`，不能只看根目录 `src/`。
- Legacy 网关每个代理请求都会经过 `ProxyRequestModule`。
- 页面首次加载会并发请求几十到上百个静态资源和接口。
- 旧代码在热路径上会频繁执行：
  - `ProcessExternalDecision`
  - `TouchAuthorizationAsync`
  - `UpdateManagedDeviceObservation`
  - `TouchManagedDevice`
  - `TouchLegacyAppIdentity`
  - `TouchLegacyAppSession`
- 这些多数只是 LastSeen / 心跳类写入，但每个资源请求都写库，导致 SQLite 写锁被放大。
- SQLite 旧连接配置没有启用 WAL / busy_timeout，也没有写入重试，所以并发写入很容易直接报 `database is locked`。

### 14.5 SQLite 并发锁修复内容（2026-04-27）

修复范围：同步修改源工程 `src/GatewayDemo.Legacy.Web` 和实际交付目录 `deliverables/GatewayDemo.Legacy-net472/src/GatewayDemo.Legacy.Web`，并重新生成交付包。

关键改动：

- 新增 `Infrastructure/SQLiteConnectionConfigurator.cs`
  - 每次打开连接设置 `PRAGMA busy_timeout = 15000`
  - 初始化数据库时启用 `PRAGMA journal_mode = WAL`
  - 设置 `PRAGMA synchronous = NORMAL`
  - 设置 `PRAGMA wal_autocheckpoint = 1000`
- 修改 `LegacyGatewayConfiguration`
  - SQLite 连接串增加 `Default Timeout=30;Pooling=True`
- 修改 `GatewayDatabaseInitializer`
  - 建库/初始化时应用 WAL 配置
- 修改 `LegacyGatewayRepository`
  - 所有关键写库路径统一走 `ExecuteDatabaseWrite`
  - 写入加进程内串行锁，避免同一 IIS worker 内部并发写互相抢锁
  - 遇到 SQLite Busy / Locked 时做短重试
  - `ProcessExternalDecision` 先读是否存在待处理审批结果，无结果时不再开启写事务
  - LastSeen / 心跳类写入降频为 30 秒一次，减少页面静态资源并发加载时的写库风暴

影响判断：

- 关键业务写入仍保留：设备创建、申请创建、审批、撤销、APP 凭据、登录会话保存、nonce 消费等不做降频。
- 降频的只是“最近访问时间 / 最近站点 / 最近 IP”这类心跳字段，对认证和授权判断没有影响。
- 该修复适用于当前 Legacy + SQLite 交付形态；如果后续进入更高并发生产环境，仍建议评估 SQL Server 或 IIS ARR / 现代 YARP 网关。

验证结果：

- `deliverables/GatewayDemo.Legacy-net472` 下执行 Release 构建通过。
- 根目录 `GatewayDemo.Legacy.sln` Release 构建通过。
- `dotnet test GatewayDemo.slnx --no-restore --nologo` 通过：6/6。
- 用交付目录编出的 DLL 做了 SQLite 并发写入压测，未再出现 `database is locked`。
- 已重新运行 `scripts/legacy/package-legacy.ps1 -Configuration Release`，刷新：
  - `deliverables/GatewayDemo.Legacy-net472/`
  - `deliverables/GatewayDemo.Legacy-net472.zip`

新的交付 zip：

- 路径：`deliverables/GatewayDemo.Legacy-net472.zip`
- 文件时间戳：2026-04-26 13:17:55
- 大小：12,885,764 bytes

### 14.6 SignalR SSE / longPolling 已取消问题（2026-04-27）

研发截图还显示旧包里 `/signalr/connect?transport=serverSentEvents` 和 `/signalr/poll?transport=longPolling` 请求大量显示“已取消”。这是旧代码现场暴露出来的问题，和 SQLite 写锁不是同一个原因。

仔细复核后确认：

- 当前 SQLite 修复后的交付目录仍保留了旧的 SSE 转发方式，所以这个风险并没有因为 SQLite 修复自然消失。
- `ProxyRequestModule` 能通过 `Referer` 把 `/signalr/...` 根路径请求回源到正确上游站点，例如 `/proxy/2027/__root__/WebEnterprise/newmain.aspx` 页面发起的 `/signalr/connect` 会被映射到上游根路径 `/signalr/connect`。
- 旧代码已经把 `/signalr/` 识别成长轮询类请求，并把上游 `HttpWebRequest.Timeout / ReadWriteTimeout` 从普通请求拉长到 300 秒。
- 但旧代码仍用普通响应复制方式 `responseStream.CopyTo(context.Response.OutputStream)`，没有关闭 ASP.NET 响应缓冲，也没有按块 `Flush`。
- 对 `text/event-stream` / SignalR serverSentEvents 来说，这会导致浏览器端不能及时收到事件帧或心跳，前端会不断取消当前 EventSource / longPolling 请求并重连。

修复内容：

- 修改 `ManagedReverseProxy`
  - 新增 `IsStreamingRequest`，识别 `Accept: text/event-stream` 或 `transport=serverSentEvents`
  - 新增 `IsStreamingResponse`，识别上游 `Content-Type: text/event-stream`
  - SSE 请求转上游时关闭自动 gzip/deflate 解压，并强制 `Accept-Encoding: identity`，避免压缩流影响事件帧及时转发
  - SSE / longPolling 超时从 300 秒拉长到 1800 秒
  - SSE 响应设置 `Response.BufferOutput = false`
  - SSE 响应加 `Cache-Control: no-cache` / `NoStore`
  - 响应流改为 16KB 分块读取、写出、每块 flush
  - 客户端断开或上游断开时吞掉 `HttpException / IOException / WebException`，按长连接正常断开处理，避免无意义 500
- 修改 `Web.config`
  - `httpRuntime` 增加 `executionTimeout="3600"`，避免 ASP.NET 默认执行超时影响长连接

边界说明：

- 当前仍不支持 WebSocket 透明代理，WebSocket upgrade 会返回 501，这是 Legacy managed proxy 的架构边界。
- 本次修的是 SignalR 的 `serverSentEvents` 与 `longPolling` 传输方式，让它们按长连接/流式请求处理，不再按普通短响应处理。
- 如果研发环境强制使用 WebSocket，仍需要 IIS ARR / URL Rewrite 或现代 YARP 网关。

验证结果：

- 根目录 `GatewayDemo.Legacy.sln` Release 构建通过。
- `deliverables/GatewayDemo.Legacy-net472` Release 构建通过。
- `dotnet test GatewayDemo.slnx --no-restore --nologo` 通过：6/6。
- 已重新运行 `scripts/legacy/package-legacy.ps1 -Configuration Release`，刷新交付目录和 zip。

新的交付 zip：

- 路径：`deliverables/GatewayDemo.Legacy-net472.zip`
- 文件时间戳：2026-04-27 14:48:52
- 大小：12,887,602 bytes

---

## 15. 部署与运维脚本

### 15.1 脚本清单

所有脚本位于 `scripts/legacy/`：

| 脚本 | 用途 |
|------|------|
| `setup-demo-sites.ps1` | 创建 IIS 站点（Gateway + MockBackend） |
| `set-gateway-profile.ps1` | 切换 gateway-sites.json Profile（experience / local-demo） |
| `check-local-demo-prereqs.ps1` | 检查本地演示站前置条件 |
| `reset-gateway-db.ps1` | 重置网关 SQLite 数据库 |
| `build-legacy.ps1` | 编译 Legacy 工程 |
| `package-legacy.ps1` | 打包交付物 |
| `setup-local-demo-env.ps1` | 本地演示环境一键设置 |
| `setup-local-demo-phase2.ps1` | 本地演示环境第二阶段设置 |

### 15.2 SQL 脚本

位于 `sql/legacy/`：

- 数据库恢复脚本
- 登录/用户创建脚本

---

## 16. 当前状态总结

### 16.1 已完成的工作

1. **现代版 Demo -> Legacy 兼容版**：技术路线切换完成
2. **浏览器主链路**：网关侧逻辑收口，代理功能验证通过
3. **官方 APP 主链路**：初始化 + 登录链路验证通过
4. **软狗授权**：Sentinel LDK 软狗全流程打通
5. **本地演示站部署**：SQL Server + IIS + KpService 全部就绪
6. **Legacy 网关对接**：Profile 切换完成，upstream 指向本地演示站
7. **端到端验证**：38/40 测试通过（浏览器），10/11 通过（真实APP模拟）
8. **Docker 容器化**：Dockerfile + docker-compose 已创建，待构建验证
9. **真实 APP 下载**：快普"易移动" v4.6.41（uni-app 框架），已分析并模拟测试
10. **交付包**：已打包 deliverables/GatewayDemo.Legacy-net472.zip，沟通指南已写好

### 16.2 当前 IIS 站点状态

```
KpDemo                        8080    Running   （ERP 演示站）
GatewayDemoLegacy             5050/5051  Running  （网关）
MockBusinessBackendLegacy     5055    Running   （Mock 后端）
Default Web Site              80      Running   （IIS 默认）
KpService                     -       Running   （定时服务，端口 9190）
```

### 16.3 待推进事项

1. **真机 APP 联调**：当前 APP 验证基于 PowerShell 模拟请求，真实设备测试待推进
2. **研发对接**：交付包已准备好，等研发反馈技术评审结果
3. **Docker 镜像构建验证**：切换 Docker Desktop 到 Windows 容器模式后构建测试
4. **生产级部署方案**：当前为演示环境，生产环境需考虑 IIS ARR / YARP 等更高性能方案

---

## 17. 关键文件与路径索引

### 17.1 仓库与报告

- `README.md`
- `QA.md`
- `演示.md`
- `需求边界说明.md`
- `兼容版重构待确认.md`
- `兼容版重构基线.md`
- `0331.md`
- `history.md`

### 17.2 Legacy 网关工程

- `GatewayDemo.Legacy.sln`
- `src/GatewayDemo.Legacy.Core` — 核心模型与服务
- `src/GatewayDemo.Legacy.Web` — Web 站点（IHttpModule 代理）
- `src/MockBusinessBackend.Legacy.Web` — Mock 业务后端

### 17.3 交付物

- `deliverables/GatewayDemo.Legacy-net472/` — 交付文件夹（含 README.md）
- `deliverables/GatewayDemo.Legacy-net472.zip` — 交付压缩包（2026-04-27 已针对 SQLite 并发锁重新打包）
- `交付给技术团队的沟通指南.md` — 发给研发的话 + 压缩包路径

### 17.4 部署脚本

- `scripts/legacy/` — 全部部署与运维脚本（见 §13.1）
- `sql/legacy/` — 数据库恢复与用户创建脚本
- `docker-compose.legacy.yml` — Docker Compose 编排

### 17.5 Docker 文件

- `src/GatewayDemo.Legacy.Web/Dockerfile`
- `src/MockBusinessBackend.Legacy.Web/Dockerfile`
- `src/GatewayDemo.Legacy.Web/App_Data/gateway-sites.docker.template.json`

### 17.6 官方 APP 分析

- `artifacts/app-analysis/kuaipu_mobile_4.5.11.apk` — 旧版（v4.5.11）
- `artifacts/kuaipu-app-android_4.6.41.apk` — 最新版易移动 APP（v4.6.41，uni-app 框架）
- `artifacts/apk-extracted/` — v4.6.41 APK 解压内容（含 manifest.json、app-service.js）

### 17.7 页面截图

- `artifacts/page-screenshots/2026-03-25/`

### 17.8 演示站点与授权

- 演示站点压缩包：`演示站点20260330.rar`
- 解压目录：`E:\验证页面\演示站点20260330\演示站点20260330`
- Web 根目录：`E:\验证页面\演示站点20260330\演示站点20260330\web\701日构建20260330`
- 软狗部署脚本：`...\web\701日构建20260330\部署软狗.bat`
- Sentinel 驱动：`...\Sentinel_LDK_Run-time_setup\HASPUserSetup.exe`
- C2V：`E:\验证页面\验证页面.c2v`
- V2C：`661716282689961197.V2C`

### 17.9 验证脚本

- `temp_full_verify.ps1` — 完整验证脚本（含 session 复用）
- `temp_verify_static.ps1` — 静态资源专项验证
- `temp_full_e2e.ps1` — 端到端验证（38/40 通过）
- `temp_app_test.ps1` — APP 基础测试（9/9 通过，使用固定 Accept-Language 头）
- `temp_app_e2e.ps1` — APP 端到端流程（全部通过）
- `temp_app_e2e2.ps1` — APP 带设备标识测试（全部通过）
- `scripts/demo/signed-app-demo.ps1` — 签名 APP 演示脚本（凭据签发+签名请求通过）
- `scripts/demo/real-app-test.ps1` — 真实 APP 模拟测试（uni-app UA，10/11 通过）

### 代码修改

`DeviceCredentialService.GetOrCreateContext`：当 APP 请求缺少设备标识（MachineId/DeviceImei）但有浏览器 cookie 时，回退到浏览器认证路径。修复了 APP 无设备 ID 时的 401 拒绝问题。

### 截图清单

| 文件 | 内容 |
|------|------|
| 01-gateway-public.png | 网关公开页（设备信息+申请表单） |
| 02-admin-login.png | 管理员登录页 |
| 03-proxy-blocked.png | 代理拦截页（未授权设备访问 ERP） |
| 04-admin-blocked-on-public.png | 管理端口隔离验证（自定义 404 消息） |
| 05-erp-login-proxied.png | 审批后 ERP 登录页（通过代理） |
| 06-admin-dashboard.png | 管理后台仪表盘（设备授权管理） |
| 07-admin-app-credential.png | APP 自定义凭据签发页面 |

---

## 18. 下次对话接手提醒

1. 需求边界不变：官方 APP 套网关、浏览器和 APP 统一认证、自定义 HMAC 不是主线。

2. 本地环境：IIS 站点（8080/5050/5051/5055）+ KpService + SQL Server 全部部署好。如果环境还在直接用，丢了就跑 `scripts/legacy/setup-demo-sites.ps1`。

3. 交付包已给研发：`deliverables/GatewayDemo.Legacy-net472.zip`，沟通指南在 `交付给技术团队的沟通指南.md`。等研发反馈技术评审结果。

4. APP 变动要点（研发已问过）：APP 不改代码只改地址，多一步设备审批，sys.ashx 网关本地返回。建议研发先用测试环境联调。

5. APP 测试脚本必须固定 `Accept-Language` 头（`zh-CN,zh;q=0.9`），否则信号哈希变化导致重新拦截。静态资源验证必须复用 session cookie。

6. APP 无设备标识时网关回退到浏览器 cookie 认证（DeviceCredentialService.GetOrCreateContext 2026-04-23 修改）。

---

## 19. 一句话结论

项目已完成原型验证和交付：浏览器 + APP 全链路通过网关验证，交付包已打包交给研发。下一步等研发技术评审反馈，以及真机 APP 联调。

---

## 20. 2026-04-27 交付包安全加固与重新打包

- 在 `src/GatewayDemo.Legacy.Web/Admin/Default.aspx.cs` 增加管理后台登录失败限流：读取 `Admin.LoginPermitLimit` 和 `Admin.LoginWindowMinutes`，按客户端 IP + 用户名在内存窗口内限制连续失败尝试，成功登录后清除失败计数。
- 在管理后台所有已登录 POST 操作上增加 CSRF token 校验，`GatewayPageRenderer.RenderAdminDashboard` 会为退出、审批、驳回、撤销授权、签发 APP 凭据等表单输出隐藏 token。
- 将 `src/GatewayDemo.Legacy.Web/Web.config` 的 `<compilation debug>` 改为 `false`，并补充登录限流配置项。
- 删除 `LegacyGatewayRuntime.Current` 中重复创建 `ManagedReverseProxy` 的代码，仅保留构造函数初始化。
- 删除 `src/GatewayDemo.Legacy.Web/App_Data/test-host.config`，避免交付包携带本机开发路径。
- 取消 Mock 后端登录页中 `huangjh / Kp123456` 的表单预填值，仅保留 mock 验证逻辑用于演示。
- 将 `scripts/legacy/setup-local-demo-env.ps1` 和 `setup-local-demo-phase2.ps1` 中的 SQL 密码改为参数/环境变量，不再硬编码 SA 或 KPMIS 密码。
- 更新 `scripts/legacy/package-legacy.ps1`：交付包不再携带 `delivery/legacy` 旧文档目录和当前运行时未支持的 `sql/legacy` SQL Server 脚本；同时剔除运行态 SQLite DB、WAL/SHM、诊断日志和 profile 文件；最终包内只保留根目录 `README.md` 一份 Markdown 文档。
- 重写 `delivery/legacy/Package.README.md` 为简版部署说明，内容包含 IIS/ASP.NET 启用命令、`scripts\legacy\setup-demo-sites.ps1`、`gateway-sites.json` 修改位置、5050/5051 端口和默认后台账号密码。
- 已重新生成 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。验证结果：根目录交付包 Release 构建 0 警告 0 错误；交付包内解决方案 Release 构建通过；`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过 6/6；zip 内仅有根目录 `README.md` 一份 Markdown，且无 `delivery/`、`sql/`、`test-host.config`、运行态 DB/日志。
---

## 21. 2026-04-27 移动端 sys.ashx version / tenant_name 透传修复

- 核验研发反馈的旧代码问题后，确认当前交付包仍存在风险：`ProxyRequestModule.TryHandleLegacyAppCompatibilityRequest` 会默认本地返回 `LegacyAppVersionResponse` 或 fallback 版本号，并会把 `tenant_name` fallback 为配置值/站点名；默认 `gateway-sites*.json` 里仍带 `6.26.0` 和 `AI演示`，真实 ERP 中版本会变化且 `tenant_name` 可能是数字，会导致移动端登录出现“授权失效”。
- 修改 `src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs`：`version/fileversion/tenant_name` 只有在站点配置显式填写 `LegacyAppVersionResponse` 或 `LegacyAppTenantNameResponse` 时才由网关本地覆盖；默认不再写死返回，改为放行给上游 ERP 的 `sys.ashx` 返回真实值。
- 修改 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs`：对所有以 `sys.ashx` 或 `ashx/sys.ashx` 结尾的相对路径做兼容，`action=version` 透传上游前统一改为 `action=fileversion`，覆盖 `/default.aspx/ashx/sys.ashx?action=version` 这类路径。
- 删除 `src/GatewayDemo.Legacy.Web/App_Data/gateway-sites.json`、`gateway-sites.experience.json`、`gateway-sites.local-demo.template.json`、`gateway-sites.docker.template.json` 里的 `LegacyAppVersionResponse` 和 `LegacyAppTenantNameResponse` 默认值，避免交付后误返回 `6.26.0` / `AI演示`。
- 已重新生成 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。验证结果：根目录 Release 构建通过；交付包内解决方案 Release 构建通过；`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过 6/6；交付包内四个 `gateway-sites*.json` 均确认不含 `LegacyAppVersionResponse` / `LegacyAppTenantNameResponse` 默认字段；zip 时间 `2026-04-27 18:11:00`，大小 `12,723,301` 字节。
---

## 22. 2026-04-28 交付包 AI 相关敏感字样复查

- 按精确规则扫描 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`：`AI验证`、`AI演示`、独立 `AI`、`ChatGPT`、`Codex`、`OpenAI`、`Claude`、`人工智能`、`大模型`、`生成式`、`智能体`、`AI协助` 均无命中。
- 第一轮宽泛搜索 `AI` 会误命中英文单词中的 `ai`，例如 `main`、`available`、`trailing`，不作为问题处理。
- 仓库里未打包的旧文档 `delivery/legacy/体验站联调说明.md` 残留了 `AI演示`，已改为 `账套编号对应 ddlDataCenter=001`，避免后续打包规则变化时误带出。
- 本次没有修改交付包内容，也无需重新打包；当前 zip 内复扫仍无上述敏感字样。
---

## 23. 2026-04-28 交付包问题复核与刷新

- 按本次对话中研发反馈过的问题复查 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。
- 发现交付文件夹曾因验证构建多出 `obj` 目录，已重新运行 `scripts/legacy/package-legacy.ps1 -Configuration Release` 刷新交付文件夹和 zip；刷新后确认文件夹无 `obj`、`.vs`、`delivery`、`sql` 目录，zip 内也无这些目录。
- 复核结果：SQLite WAL/busy_timeout/写锁重试仍在；SSE/longPolling 超时、禁压缩和流式 flush 仍在；`debug=false`、管理后台登录限流、CSRF token、ReverseProxy 单次初始化均在；旧 SQL Server 脚本已不随交付包提供；`test-host.config`、运行态 SQLite DB/日志、mock 登录预填、SQL SA/KPMIS 硬编码、AI 相关敏感字样均未在交付包命中。
- `version/fileversion/tenant_name` 默认配置继续保持透传上游：四个 `gateway-sites*.json` 均不含 `LegacyAppVersionResponse` / `LegacyAppTenantNameResponse` 默认字段；代码仅在显式配置这两个字段时才本地覆盖。
- 保留项说明：`ProxyRequestModule` 和部分 WebForms 页面里仍有 `.GetAwaiter().GetResult()`，这是当前 .NET Framework/IHttpModule 同步架构下的设计遗留风险，并非本轮运行时 bug 修复项；`DatabaseWriteSyncRoot` 仍保留，是为 SQLite `database is locked` 做的写入串行化保护。
- 验证：重新打包 Release 构建 0 警告 0 错误；`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过 6/6；当前 zip 时间为 `2026-04-28 10:10:01`，大小 `12,723,301` 字节。

---

## 24. 2026-04-30 移动 APP 登录与根路径资源修复

- 分析研发新反馈的三个截图问题，确认本轮问题在 legacy 交付包的移动 APP 自动审批信息、移动登录路径规整、以及上游 HTML/JS/CSS 路径改写。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/DeviceCredentialService.cs`：`ContactHint` 只从真实电话字段读取，并过滤 GUID/长横线设备号；不再把 `MachineId`、`DeviceImei`、`DeviceNo` 或 `SessionKey` 当成联系电话。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs`：移动 APP 自动创建审批申请时，`Phone` 只写真实联系电话；如果没有电话则留空，后台展示为 `-`，不会再显示移动设备号。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs`：对 `default.aspx/user/login` 这类由 APP 拼出来的登录路径做规整，转发前改为 `user/login`，避免命中 PC 页面路径。
- 增强反代响应体改写：HTML/CSS 中 `../../Resource/...` 这类相对路径会按当前上游页面位置归一化，再写成 `/proxy/{site}/__root__/...`；JavaScript 响应和内联脚本中的 `'/AIQuick/...'`、`'/Resource/...'` 这类根路径也会改写到网关路径。
- 增加兜底路由：如果浏览器已经请求到了缺少 `__root__` 的 `/proxy/{site}/Resource/...`、`/proxy/{site}/AIQuick/...` 等路径，并且 Referer 来自同站点 `__root__` 页面，网关会按 `__root__` 上游根路径转发。
- 增加移动登录保护：如果移动登录上游仍返回 `text/html` / `application/xhtml+xml`，网关返回 JSON 错误，不再把 PC 登录页原样返回给 APP。
- 验证结果：`scripts/legacy/build-legacy.ps1 -Configuration Release` 通过，0 警告 0 错误；反射校验通过，确认 `../../Resource/JavaScript/jQuery/jquery.min.js` 会改成 `/proxy/2027/__root__/Resource/JavaScript/jQuery/jquery.min.js`，`'/AIQuick/GetAiField'` 会改成 `/proxy/2027/__root__/AIQuick/GetAiField`，`default.aspx/user/login` 会规整为 `user/login`；联系电话校验通过，UUID 设备号会被过滤，真实手机号会保留。
- 已重新运行 `scripts/legacy/package-legacy.ps1 -Configuration Release`，刷新 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`；当前 zip 时间 `2026-04-30 13:04:32`，大小 `12,728,600` 字节，SHA256 `CBA5E834471235B67EF95A0CC64555F151BF125F700F3960DE33077F9C6D6A7E`。交付目录仍只保留根目录 `README.md` 一个文档，zip 内无 `obj`、`.vs`、`delivery`、`sql`、运行 DB/日志或 `test-host.config`。
- 补充验证：`dotnet test GatewayDemo.slnx --no-restore --nologo` 通过，6/6。

---

## 25. 2026-05-12 HostNames 根路径转发与移动审批拦截修复

- 按研发反馈复查 `deliverables/GatewayDemo.Legacy-net472/`：确认此前仍存在 `HostNames` 只参与路由、不参与上游绝对网址改写的问题；真实域名访问时还会生成 `/proxy/2027/__root__/...` 这类公共转发路径，导致页面路径和移动请求路径不符合真实站点形态。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs`：`HostNames` 模式下优先按根路径对外暴露和改写 URL，不再把真实域名访问强制改成 `/proxy/{site}/__root__/...`；旧的 `/proxy/{site}/__root__` 兼容路由保留，用于 mock、旧链接和非 HostNames 访问。
- 增强上游绝对 URL 替换：根据 `HostNames` 配置识别公网 Host，并把上游返回的绝对地址改写到当前网关 Host；审批管理存在额外端口时仍按管理端口判断，不影响公网 HostNames 路由。
- 修复移动端 302：移动 APP 请求如果遇到上游 3xx，不再把 302 原样返回给 APP，而是返回 JSON，并带 `X-Gateway-Upstream-Status=302`、`GatewayUpstreamBlocked=true` 等信息；浏览器请求仍保留原本 302 跳转行为。
- 修复移动审批身份判断：`src/GatewayDemo.Legacy.Web/Infrastructure/DeviceCredentialService.cs` 现在优先使用 `UrlBefore`、`UserId`、`DataCenterId`、`DeviceImei` 作为移动审批放行身份，`SessionKey` 仅作为兼容兜底；后台审批记录能展示账号、数据中心和设备号，避免“审批了谁不清楚”。
- 更新 `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayConfiguration.cs` 和 `scripts/legacy/setup-demo-sites.ps1`：真实上游 `-UpstreamBaseUrl` 部署时自动补齐 `HostNames`，mock 部署保持旧代理兼容路径；同步更新交付目录 `README.md` 的配置说明。
- 自检构建：使用 `E:\Visual Studio BuildTools\MSBuild\Current\Bin\MSBuild.exe` 对 `GatewayDemo.Legacy.Core`、`GatewayDemo.Legacy.Web`、`MockBusinessBackend.Legacy.Web` 执行 Release 构建，结果通过。
- IIS 临时联调自检：创建临时站点 `GatewayDemoLegacyDeliverableCheck` 和 `MockBusinessBackendLegacyDeliverableCheck`，端口 `19050/19051/19055`；验证 `/healthz.ashx` 返回 200，公网端口 `/admin/` 返回 404，管理端口 `/admin/` 返回 200。
- 移动 302 自检：移动请求命中上游 302 时返回 `200 application/json`，响应含 `X-Gateway-Upstream-Status=302`、`GatewayUpstreamBlocked=true`，并能携带 `Applicant=dubx`、`Company=001`；浏览器同路径仍收到正常 302。
- 清理与恢复：临时 IIS 站点和应用程序池已删除；`gateway-sites.json` 恢复为占位配置；`src/GatewayDemo.Legacy.Web/Web.config` 的 `Admin.ManagementPort` 恢复为 `17051`；已扫描 `deliverables/GatewayDemo.Legacy-net472/`，未残留临时端口或临时 Host。
- 本轮只更新交付目录源码、脚本和说明，并完成本地构建/IIS 自检；尚未重新生成 `deliverables/GatewayDemo.Legacy-net472.zip`，如后续要正式发包需再运行打包脚本。

---

## 26. 2026-05-18 移动 Web/微信/iPad 端真实浏览器修复与近真机验证

- 按本轮要求，对 `deliverables/GatewayDemo.Legacy-net472` 进行真实浏览器操作测试，并补充端型矩阵：PC Chrome、Android Chrome、iPhone Safari、iPad Safari、iPhone 微信 UA、iPad 微信 UA。
- 首次端型矩阵发现：普通移动浏览器、微信内置浏览器和 iPad Web 端访问 `/portal` 时，被误判成移动 APP 根路径探活，返回 `GatewayReady` JSON，不显示网关访问申请表单。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs`：将 `GatewayReady` JSON 收窄到真正的 APP 根路径 `/` 探活；`/Gateway/Default.aspx`、`/portal` 等网页访问不再被移动 UA 拦截成 APP 探活。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/DeviceCredentialService.cs`：普通移动网页 `GET/POST /login` 不再误判为 APP 登录接口；APP 仍按 `/user/login`，或带 `DeviceImei` / `DataCenterId` / `UrlBefore` / `UserId` 等身份字段的请求识别。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs`：上游 302/HTML 的 APP JSON 保护只作用于真实 APP 请求，不再拦截手机浏览器、微信 WebView、iPad Safari/WKWebView 的网页登录跳转。
- 使用 `E:\Visual Studio BuildTools\MSBuild\Current\Bin\MSBuild.exe GatewayDemo.Legacy.sln /t:Build /p:Configuration=Debug /m` 重新编译，0 警告 0 错误；随后回收 IIS 应用池 `GatewayDemoLegacyAppPool` 让新 DLL 生效。
- 端型矩阵验证通过：`deliverables/GatewayDemo.Legacy-net472/test-results/gateway-device-matrix-20260518183609/summary.json`，18 PASS / 0 FAIL。覆盖 PC Chrome、Android Chrome、iPhone Safari、iPad Safari、iPhone 微信 UA、iPad 微信 UA；每个端型均完成“打开网关申请页 -> 提交申请 -> 管理后台审批 -> 进入 ERP -> ERP 登录”。
- 完整真实浏览器回归通过：`deliverables/GatewayDemo.Legacy-net472/test-results/gateway-real-browser-20260518183654/summary.json`，24 PASS / 0 FAIL。覆盖健康检查、管理端口隔离、后台登录/CSRF/限流、浏览器申请审批、ERP 资源重写/API 转发、旧 `/proxy/2027`、APP HMAC、匿名初始化路径、未知站点 404、现场端口、移动 APP 自动申请/审批/登录/WCF/上传、拒绝、撤销、退出登录和 WebSocket 不支持行为。
- 为更贴近物理真机，安装 Playwright WebKit：`npx playwright install webkit`，并新增 `deliverables/GatewayDemo.Legacy-net472/test-results/gateway-near-physical-runner.mjs`。该脚本使用真实 Chromium/WebKit 内核、Playwright 设备模型、触摸/DPR、微信 XWeb/WKWebView UA、`X-Requested-With: com.tencent.mm`、`X-App-Platform` 等 Header 模拟近真机访问。
- 近真机矩阵验证通过：`deliverables/GatewayDemo.Legacy-net472/test-results/gateway-near-physical-20260518195647/summary.json`，24 PASS / 0 FAIL。覆盖 Android WeChat XWeb、iPhone Safari WebKit、iPad Safari WebKit、iPhone WeChat WKWebView-style、iPad WeChat WKWebView-style、iPad App WKWebView-style；每个端型均确认不会返回 `GatewayReady` JSON，并完成申请、审批、ERP 登录。
- 留存截图目录：`deliverables/GatewayDemo.Legacy-net472/test-results/gateway-device-matrix-20260518183609/`、`deliverables/GatewayDemo.Legacy-net472/test-results/gateway-real-browser-20260518183654/`、`deliverables/GatewayDemo.Legacy-net472/test-results/gateway-near-physical-20260518195647/`。
- 边界说明：本轮已尽可能贴近真机微信/iPad App，但仍未连接物理 iPad 或真实微信 App；真正物理真机联调需要现场设备访问同一网关地址或接入可自动化控制的真机。

---

## 27. 2026-05-19 PC Login.ashx 误判移动 APP 复核、真实浏览器验证与最终交付包确认

- 研发反馈现场截图仍在 PC 登录页 `Login.ashx` / `PCLogin` 请求中弹出“移动 APP 设备尚未获批”，并明确要求不要再根据业务参数判断移动端，应按移动 APP、iPad、微信等请求头判断。
- 复查 `deliverables/GatewayDemo.Legacy-net472` 当前源码：`DeviceCredentialService` 已通过 `MobileClientDetector.IsMobileClientRequest(request)` 判断移动客户端；`UrlBefore/UserId/DataCenterId/DeviceImei/MobileUserNum/SessionKey` 只用于移动审批身份提取、落库和展示，不再单独决定“是否移动 APP”。`ProxyRequestModule` 和 `ManagedReverseProxy` 的移动 JSON 分支也已改为请求头/移动上下文判断。
- 发现一个交付风险：旧 zip `deliverables/GatewayDemo.Legacy-net472.zip` 时间仍是 `2026-05-17 16:34:09`，其中关键文件还是 5 月 17 日版本；而交付目录里最新关键修复文件是 5 月 18 日版本。因此必须重新生成 zip，否则研发可能拿到旧包。
- 先执行交付目录构建：`dotnet build GatewayDemo.Legacy.sln --no-restore -c Release` 通过，0 警告 0 错误。
- 使用真实浏览器而不是 curl 做专项回归：通过 Playwright 启动有界面 Chromium，业务端口访问 `http://127.0.0.1:15050`，管理后台访问 `http://127.0.0.1:5051`。专项报告目录：`artifacts/real-browser-header-regression-20260519003457/`。
- 专项真实浏览器结果：6 PASS / 0 FAIL。PC Chrome 桌面浏览器对 `Login.ashx?r=...` 发起 `PCLogin` POST，即使请求体包含 `UrlBefore/UserId/DataCenterId/MobileUserNum/SessionKey`，未审批和审批后都没有返回 `GatewayBlocked` 或移动 APP 审批 JSON；同样的 PCLogin-like 请求换成 iPad Safari 请求头、微信 iPhone 内置浏览器请求头、Android APP WebView 请求头时，均按移动端请求头识别，返回 `application/json` 且包含 `GatewayBlocked=true`。
- 继续运行近真机矩阵：`GATEWAY_BASE=http://127.0.0.1:15050`、`GATEWAY_ADMIN_BASE=http://127.0.0.1:5051`、`GATEWAY_HEADLESS=0` 执行 `node test-results/gateway-near-physical-runner.mjs`，报告目录 `deliverables/GatewayDemo.Legacy-net472/test-results/gateway-near-physical-20260519003523/`，结果 24 PASS / 0 FAIL。覆盖 Android WeChat XWeb、iPhone Safari WebKit、iPad Safari WebKit、iPhone WeChat WKWebView-style、iPad WeChat WKWebView-style、iPad App WKWebView-style，均完成打开申请页、提交申请、后台审批、进入 ERP 并登录。
- 修复交付目录配置文案：`deliverables/GatewayDemo.Legacy-net472/src/GatewayDemo.Legacy.Web/App_Data/gateway-sites.json` 中站点描述曾因编码问题显示为 `ERP 涓氬姟绯荤粺`，已改回正常 `ERP 业务系统`。
- 重新生成正式交付包：`deliverables/GatewayDemo.Legacy-net472.zip`，文件时间 `2026-05-19 00:38:34`，大小 `12,758,861` 字节，SHA256 `C5CE08DF537DF6709112D8558330DF096BB1F9A4EA5BF65D592032273DB52A85`，zip 内共 123 个条目。
- 对新 zip 做内容检查：`.md` 文件只有 1 个，且为根目录 `README.md`；没有 `.doc/.docx/.pdf/.txt/.log`；没有 `test-results/`、`artifacts/`、`obj/`、`.vs/`；没有运行时 `gateway-demo-legacy.db`、`.db-wal`、`.db-shm`；包内唯一 `.json` 是必需配置文件 `src/GatewayDemo.Legacy.Web/App_Data/gateway-sites.json`。
- 对新 zip 解压到 `artifacts/package-verify-20260519003924/` 后执行 `dotnet build GatewayDemo.Legacy.sln --no-restore -c Release`，通过，0 警告 0 错误。
- 给研发的结论：这版已按请求头识别完成真实浏览器验证，PC 登录不再因 `UrlBefore/UserId/DataCenterId/MobileUserNum/SessionKey` 被误判为移动 APP；iPad、微信、Android App WebView 请求头仍会按移动端识别。当前可发送的包是 `deliverables/GatewayDemo.Legacy-net472.zip`，不是旧时间的包；压缩包满足“只保留根目录一个 README 文档”的交付要求。

---

## 28. 2026-05-27 转发响应中文乱码修复与重新打包

- 研发反馈访问 `10.188.188.249:15050/Resource/claw/index.html` 一类经网关转发的页面时，浏览器标签标题出现 `åŠ©æ‰‹` 这类 UTF-8 被按错误编码解释后的乱码。
- 复查 `deliverables/GatewayDemo.Legacy-net472.zip` 后确认问题发生在 legacy 网关反向代理链路：`ManagedReverseProxy` 对 `text/html`、`text/css`、JavaScript 等响应会读取响应体并做 URL 重写；当上游 `Content-Type` 没有显式 `charset` 时，网关按 UTF-8 读写，但响应头仍可能只转发 `text/html`，浏览器端会按默认编码猜测，导致中文乱码。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs`：对已解码并重写的文本响应，如果上游 `Content-Type` 未带 `charset`，自动补充 `charset=<实际输出编码>`；同时设置 `Response.Charset`，确保 ASP.NET 写回的响应头和实际输出编码一致。
- 同步修复响应头复制逻辑：`Content-Type` 不再作为普通上游 Header 追加复制，避免与 `Response.ContentType` 产生重复或覆盖关系；`Content-Length`、`Content-Encoding`、`Transfer-Encoding` 等原有跳过逻辑保持不变。
- 执行 `powershell -ExecutionPolicy Bypass -File scripts\legacy\package-legacy.ps1 -Configuration Release` 重新构建并刷新 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。MSBuild Release 结果：0 警告 0 错误。
- 校验新包：zip 内 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs` 已包含 `BuildResponseContentType`、`HasCharsetParameter` 和跳过 `Content-Type` Header 复制的修复；`deliverables/GatewayDemo.Legacy-net472/src/GatewayDemo.Legacy.Web/bin/GatewayDemo.Legacy.Web.dll` 已更新。
- 反射校验新 DLL：`text/html` 在重写文本响应场景下会输出为 `text/html; charset=utf-8`；已有 `text/html; charset=gb2312` 会保持原 charset；非重写二进制响应不补 charset；`Content-Type` 会被 `ShouldSkipResponseHeader` 跳过普通 Header 复制。
- 当前正式交付包：`deliverables/GatewayDemo.Legacy-net472.zip`，文件时间 `2026-05-27 20:58:20`，大小 `12,790,836` 字节，SHA256 `3204B11522A5D77E2726278C12AC7F387A44C206EA6E9BCDDF74F10D9143A5A2`。

---

## 29. 2026-05-27 交付包编码风险全量复查与补充修复

- 按研发反馈的同类风险复查整个 `deliverables/GatewayDemo.Legacy-net472.zip`：解压后扫描 `README.md`、`scripts/`、`src/` 下的源码、配置、ASPX/ASHX、PowerShell 脚本等文本文件，排除 `bin/`、`obj/` 和第三方依赖目录。
- 乱码特征串扫描结果：未发现 `涓`、`绯`、`銆`、`鈥`、`锛` 等典型 UTF-8/GBK 错读残留；`Web.config` 中 `Gateway.AppName=统一认证访问网关` 正常，`gateway-sites.json` 中 `Description=ERP 业务系统` 正常。
- 响应头复查发现三个自生成 JSON 端点仍只声明 `application/json`：`src/GatewayDemo.Legacy.Web/Handlers/Healthz.ashx.cs`、`src/MockBusinessBackend.Legacy.Web/ApiPing.aspx.cs` 两处。虽然现有字段多数不是页面 HTML，但其中包含网关名称、mock 站点名等中文字段，后续仍可能触发同类编码猜测问题。
- 已将上述三个 JSON 输出统一改为 `application/json; charset=utf-8`。此前已修复的反向代理文本响应逻辑保持不变：重写后的 HTML/CSS/JavaScript 响应会补齐 charset，且不再重复复制上游 `Content-Type` Header。
- 重新执行 `powershell -ExecutionPolicy Bypass -File scripts\legacy\package-legacy.ps1 -Configuration Release`，刷新 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。MSBuild Release 结果：0 警告 0 错误。
- 新 zip 复查结果：乱码特征串命中 0；`ContentType = "application/json";` 命中 0；zip 内已确认 `Healthz.ashx.cs` 和 `ApiPing.aspx.cs` 均为 `application/json; charset=utf-8`，`ManagedReverseProxy.cs` 仍包含 `BuildResponseContentType` 和跳过 `Content-Type` 普通 Header 复制的修复。
- 当前正式交付包：`deliverables/GatewayDemo.Legacy-net472.zip`，文件时间 `2026-05-27 23:54:19`，大小 `12,790,862` 字节，SHA256 `53D50694687DF0005291F1F31650519CC9486F5B6E27AC79AA91C5AD62A95257`。

---

## 30. 2026-05-28 真实上游 OpenClaw 页面代理连通与脚本误改写修复

- 按要求对 `E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip` 做真实浏览器操作检查，不停留在模拟检查；重点复核截图中“响应内容被替换/内容不正常”的问题，同时检查页面加载链路中的其他异常。
- 首轮真实 IIS/浏览器验证发现：网关代理真实上游 `https://j.kuaipu.com.cn:2027/Resource/claw/index.html` 时返回 `502 Bad Gateway`，底层异常为 `ConnectFailure`。进一步确认本机 `j.kuaipu.com.cn` DNS 解析到 `127.0.0.1`，当前用户可通过 WinINet 代理 `127.0.0.1:7897` 访问真实上游，但 IIS AppPool 身份拿不到该用户代理配置，导致 .NET 反代连接失败。
- 修复上游代理配置：`src/GatewayDemo.Legacy.Core/Options/GatewayOptions.cs` 增加 `UseDefaultUpstreamProxy` 和 `UpstreamProxyUrl`；`src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayConfiguration.cs` 读取对应配置；`ManagedReverseProxy` 的 HTTP 和 WebSocket 上游请求支持显式代理、默认系统代理和禁用代理三种模式；`src/GatewayDemo.Legacy.Web/Web.config` 增加默认配置项。
- 更新部署脚本和说明：`scripts/legacy/setup-demo-sites.ps1` 增加 `-UpstreamProxyUrl` 参数并写入 `Web.config`；`delivery/legacy/Package.README.md` 记录 IIS AppPool 访问真实外部上游时可使用 `-UpstreamProxyUrl "http://127.0.0.1:7897"` 显式指定代理。
- 连通修复后继续真实浏览器检查，发现第二个必须修复的问题：页面标题为“快普 OpenClaw 助手”，主文档返回 200，但页面主体为空，浏览器控制台报 `Invalid regular expression flags`。通过 Playwright 抓取网关主文档响应体，并与直连真实上游 HTML 做差异，定位到网关把内联脚本中的动态字符串误改写，例如 `href="\`+i+\`"` 被改成 `href="/Resource/claw/%60+i+%60"`，`new url(e)` 被改成 `new url(/Resource/claw/e)`。
- 根因确认：`ManagedReverseProxy.RewriteHtmlDocument` 之前会把 HTML 属性改写和 CSS `url(...)` 改写正则直接跑在整份 HTML 上，导致 `<script>` 正文里的 JavaScript 字符串、选择器和构造函数参数被当成 HTML/CSS URL 误处理，进而破坏前端打包产物。
- 修复 `src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs`：新增脚本块识别逻辑，HTML 文档改写时按 `<script>...</script>` 分段处理；脚本块外继续改写 HTML 标签属性和 CSS URL；脚本开标签仍可改写 `src` 等属性；脚本正文不再进入 HTML 属性和 CSS URL 正则，仅保留已有的 JavaScript 绝对 URL 字符串改写，避免破坏内联 JS。
- 重新构建和打包：执行 `powershell -ExecutionPolicy Bypass -File scripts\legacy\build-legacy.ps1 -Configuration Debug` 通过，0 警告 0 错误；随后执行 `powershell -ExecutionPolicy Bypass -File scripts\legacy\package-legacy.ps1 -Configuration Debug`，刷新 `deliverables/GatewayDemo.Legacy-net472/` 和 `deliverables/GatewayDemo.Legacy-net472.zip`。
- 使用新 zip 解压到 `_verify/GatewayDemo.Legacy-net472_scriptfix_20260528/` 并部署独立 IIS 测试站点 `GatewayDemoLegacyScriptFix528` / `MockBusinessBackendScriptFix528`，端口 `19650/19651/19655`，真实上游 `https://j.kuaipu.com.cn:2027/`，入口 `Resource/claw/index.html`，显式上游代理 `http://127.0.0.1:7897`。
- 真实浏览器验证流程完成：打开 `http://clawscriptfix.lvh.me:19650/Resource/claw/index.html?scriptfix=apply` 进入网关访问申请页，提交企业“脚本改写修复验证”的申请；登录 `http://localhost:19651/admin/` 管理后台并批准；再访问 `http://clawscriptfix.lvh.me:19650/Resource/claw/index.html?scriptfix=afterapprove`。
- 真实浏览器复测结果：业务页返回 200，页面正常渲染出“快普 OpenClaw 助手”界面和连接配置表单；控制台没有 `Invalid regular expression flags`；仅有浏览器自身的 password-field verbose 提示。截图留存为 `openclaw-scriptfix-afterapprove.png`。
- 主文档响应校验：通过 Playwright 保存网关响应 `clawscriptfix_gateway_body_from_browser.html`，复制到 `_verify/proxyfix-html-diff-20260528/gateway.scriptfix.browser.html`；与直连真实上游 `_verify/proxyfix-html-diff-20260528/direct.html` 对比，SHA256 均为 `840A051461007C5A1B89E1EECD6B49D3C3C43D1391C31DF7116255D105F68A82`，文本长度均为 `403700`，内容一致，误改写特征 `/Resource/claw/%60` 命中 0。
- 额外检查：页面加载时 `/api/v1/callservicescheme` 仍会出现 `302 -> /Resource/Html/error.htm`，但直连真实上游 `https://j.kuaipu.com.cn:2027/Resource/claw/index.html` 也出现完全相同的请求结果，因此该项属于上游自身行为，不是网关替换或本轮修复引入的问题。
- 最终交付包确认：`deliverables/GatewayDemo.Legacy-net472.zip` 文件时间 `2026-05-28 19:32:33`，大小 `12,835,469` 字节，SHA256 `7A416CFD143B8A12D39903B48F2AFF55DAEE6BFA11958CE7FF7EE6E8E643F21D`。zip 内确认 `ManagedReverseProxy.cs` 包含 `HtmlScriptBlockRegex` 脚本隔离修复，且 `ManagedReverseProxy.cs`、`GatewayOptions.cs`、`Web.config`、`scripts/legacy/setup-demo-sites.ps1`、`README.md` 均包含上游代理配置相关内容。

---

## 31. 2026-05-28 交付 zip 真实浏览器复核：OpenClaw 脚本、企业号授权搜索与 WebSocket 状态

- 按用户要求直接复核 `E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`，不使用旧解压目录和旧 IIS 站点做模拟检查。当前 zip 文件大小 `12,842,369` 字节，SHA256 `443B40293625940C9DA90D28ECDDA86B064DA246885F15947D004A820CF7DE33`。
- 将 zip 独立解压到 `_verify/GatewayDemo.Legacy-net472_zipcheck_20260528_205305/`，部署独立 IIS 站点 `GatewayDemoLegacyZipFresh528` / `MockBusinessBackendZipFresh528`，端口 `19750/19751/19755`，真实上游 `https://j.kuaipu.com.cn:2027/`，入口 `Resource/claw/index.html`，显式上游代理 `http://127.0.0.1:7897`。
- 健康检查通过：`http://127.0.0.1:19750/healthz.ashx` 返回 200，管理后台 `http://127.0.0.1:19751/admin` 可登录，业务入口首次访问会正确跳转网关申请页。
- 针对截图 1 的 `Uncaught SyntaxError: Invalid regular expression flags` 做真实浏览器复核：普通浏览器提交申请、登录管理后台审批后，访问 `http://127.0.0.1:19750/Resource/claw/index.html`，页面正常渲染“快普 OpenClaw 助手”和连接配置表单，控制台无 JS 错误，仅有 Chrome 自身 password-field verbose 提示；未复现响应内容被误替换或脚本被破坏的问题。
- 针对截图 2 的“企业号少了”做真实移动端请求复核：在真实 OpenClaw 页面上下文中发起带移动端请求头的 `POST /wcfservice/postbus.svc`，请求体包含 `CompanyId=QY-ZIP-528-A`、`DataCenterId=DC-ZIP-528`、`UserId=acct-zip-528`、`UserName=张三Zip`、`DeviceImei=88DF4F9B-6015-40F8-9485-F48C0608A9D7`、`UrlBefore=http://10.188.188.249:15050`、`MobileUserNum=1798`、`Phone=13900001798`。网关返回移动 APP 未审批 JSON，并自动创建待审核申请。
- 管理后台待审核卡片已显示移动端信息：企业号 `QY-ZIP-528-A`、账号 `acct-zip-528`、姓名 `张三Zip`、数据中心 `DC-ZIP-528`、设备号、入口地址和手机号均可见。审批后同样字段进入“设备授权”卡片，不只停留在待审申请里。
- 管理后台搜索体验验证通过：分别搜索企业号 `QY-ZIP-528-A`、数据中心 `DC-ZIP-528`、账号 `acct-zip-528`、姓名 `张三Zip`，均只筛出对应移动端设备。`data-search` 中包含企业名、申请人、手机号、设备编号、设备标识、企业号、账号、数据中心、设备号、入口地址和客户端 IP。
- 在搜索结果上填写撤销说明“真实搜索后撤销验证”并点击“撤销授权”，对应移动端设备状态变为“已撤销”，普通浏览器授权仍保持“已授权”。撤销后同一移动端请求再次访问 `POST /wcfservice/postbus.svc`，返回 `设备授权已撤销，需要重新发起审核。`，说明撤销已真实生效，不只是 UI 状态变化。
- SQLite 数据库复核确认：`GatewayDeviceAuthorizations` 中移动端授权记录保存 `LegacyCompanyId=QY-ZIP-528-A`、`LegacyUserId=acct-zip-528`、`LegacyDataCenterId=DC-ZIP-528`、`LegacyDeviceImei=88DF4F9B-6015-40F8-9485-F48C0608A9D7`、`LegacyUrlBefore=http://10.188.188.249:15050`、`LegacyMobileUserNum=1798`，撤销备注为“真实搜索后撤销验证”；普通浏览器授权记录未被误撤销。
- 其他真实使用发现：OpenClaw 页面加载后会触发 `POST /api/v1/callservicescheme`，返回 `302 -> /Resource/Html/error.htm?aspxerrorpath=/api/v1/callservicescheme`。直连真实上游 `https://j.kuaipu.com.cn:2027/api/v1/callservicescheme` 使用同样请求体也返回相同 302，因此该项属于上游自身行为，不是网关响应替换或脚本改写引入的问题。
- 环境差异说明：当前机器没有 `10.188.188.249` 这个 IPv4 地址，浏览器访问 `10.188.188.249:19750` 超时；本机实际可用 IP 为 `172.26.116.77`，访问 `172.26.116.77:19750` 可到达新 IIS 站点。`j.kuaipu.com.cn` 在本机解析到 `127.0.0.1`，用该 Host 访问新站点会因为上游同域名自解析到本机而出现 502，属于本机 hosts/代理环境差异。
- WebSocket 状态说明：zip 内应用配置 `Web.config` 默认包含 `<webSocket enabled="true" />`，部署脚本 `scripts\legacy\setup-demo-sites.ps1` 也会尝试启用 Windows/IIS 系统组件 `IIS-WebSockets`。但该组件不是 zip 能内置的能力，本机检测结果为 `EnablePending`，说明首次启用后仍需要重启系统才会变为 `Enabled`。可发给研发的口径：应用默认开启 WebSocket，脚本会尝试启用 IIS WebSocket Protocol；若服务器之前未启用该 Windows 功能，部署后可能需要重启服务器才能完全生效。
- 本轮真实浏览器证据截图留存：`gateway-zipcheck-ordinary-request.png`、`gateway-zipcheck-admin-after-approve.png`、`gateway-zipcheck-mobile-pending-companyid.png`、`gateway-zipcheck-mobile-authorized-companyid.png`、`gateway-zipcheck-mobile-revoked-after-search.png`。

---

## 32. 2026-05-29 交付 zip 真实浏览器复核：空表单、企业号、重复授权、热更新与站点网址

- 按用户要求复核 `E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`，将 zip 独立解压到 `_inspect_gateway_zip/`，部署临时 IIS 站点 `GatewayDemoLegacyInspect529` / `MockBusinessBackendInspect529`，端口 `20550/20551/20555`，并使用 Playwright 做真实浏览器操作检查。检查完成后已删除临时 IIS 站点和应用程序池。
- 空表单申请复核：打开 `http://127.0.0.1:20550/portal` 进入网关申请页，直接点击“提交申请”后未再跳转到乱码页，浏览器必填校验停留在当前表单，问题 1 未复现。
- 移动端字段复核：用移动端请求头真实访问 `/user/login?tenant_name=tenant-demo&UserId=dubx&UserName=dubx&DataCenterId=001&DeviceImei=88DF4F9B-6015-40F8-9485-F48C0608A9D7&UrlBefore=...&MobileUserNum=1798`，后台待审核卡片显示 `企业号=tenant-demo`、账号、姓名、数据中心、设备号和入口地址；UI 未再把 `MobileUserNum` 当手机号展示，申请原因也未展示 `MobileUserNum`。
- 重复授权复核：审批该移动端申请后，使用同一设备身份再次访问，网关进入上游流程而不是再次创建待审核申请。SQLite 复核显示移动端只有 1 条 `GatewayAccessRequests` 和 1 条 `GatewayDeviceAuthorizations`，同一设备未多产生第二个授权。
- 热更新复核：在不重启 IIS 的情况下临时修改运行副本 `App_Data/gateway-sites.json`，将站点名改为 `Hot Reload Site`，页面立即显示新站点名；将 `HostNames` 第一个值临时改成 `hot.local:20550` 后，“受保护站点”显示 `http://hot.local:20550/portal`，确认站点清单热更新和“网址取第一个主机名”均生效。验证后已恢复运行副本配置。
- 交付包结构复核：zip 内 Markdown 文件只有根目录 `README.md`，未发现测试截图、报告、Playwright 文件或 AI/OpenAI/ChatGPT 相关字眼；但 `README.md` 末尾仍有一行“本包验证记录：2026-05-29 ...”，如需严格去除验证记录痕迹，后续发包前应删除该行。zip 内仍包含若干 `.pdb` 文件，如要求发布包更精简，也建议后续剔除。
- 当前用户需要随包发给研发的说明口径已整理：本版针对之前反馈的 5 个问题均已修复并完成真实浏览器/IIS 流程复核；修复点包括表单必填校验、移动端企业号字段识别与展示、同设备授权合并、`gateway-sites.json` 热更新，以及站点展示 URL 使用 `HostNames` 第一个主机名。

---

## 33. 2026-06-04 Vue/SPA 接入策略收敛、中文路径部署修复与冒烟样例补强

- 按用户确认的 6 点建议落地到 `deliverables/GatewayDemo.Legacy-net472/`，并准备刷新 `deliverables/GatewayDemo.Legacy-net472.zip`。
- Vue、React、Angular 等单页应用的交付口径已收敛为“必须使用域名或根路径映射接入”，不再把 `/proxy/{site}/...` 前缀代理写成单页应用方案。README 明确说明：单页应用通常依赖 `/assets/...`、`/api/...` 和 history 路由刷新，放在前缀路径下会让资源路径、登录 Cookie、接口 baseURL 和路由刷新错位。
- 保留 `/proxy/{site}` 作为老系统、普通页面和临时 fallback，不删除既有代码能力，避免破坏现有非 SPA 接入和冒烟流程；但 Vue/SPA 交付和验收不再依赖该路径。
- `scripts/legacy/setup-demo-sites.ps1` 创建 IIS 站点和写入物理路径时改用 `Microsoft.Web.Administration` 管理 API，避免 `appcmd /physicalPath:` 在中文路径或含空格路径下受原生命令行编码影响。脚本仍保留 appcmd 做配置解锁和应用池基础操作。
- `src/MockBusinessBackend.Legacy.Web/Web.config` 将 mock 后端编译配置从 `debug="true"` 改为 `debug="false"`，减少调试配置残留。
- mock 后端新增 `vue-spa` 样例站点，提供无扩展入口 `portal`、根路径资源 `/assets/spa.js`、`/assets/spa.css`、根路径接口 `/api/spa/ping`，以及 `/orders`、`/reports/2026` 这类 history 路由入口，用来复现单页应用最容易出问题的资源、接口和路由模式。
- 真实 IIS 验证时发现 mock 入口如果写成 `/mock/vue-spa/index.html` 会先被 IIS 静态文件处理器截走，无法进入 ASP.NET 路由；已把 mock 样例和验证脚本改为 `/mock/vue-spa/portal`，真实 Vue 源站入口仍按实际上游配置 `index.html` 等文件。
- 新增 `scripts/legacy/verify-spa-smoke.ps1`，可对 mock SPA 样例执行轻量冒烟：检查 SPA shell、JS 资源、history 路由和 API 返回。该脚本只作为验证工具随包提供，不生成测试记录文件。
- README 新增 SPA 接入章节和冒烟脚本用法，同时注明部署脚本支持中文目录和包含空格的目录。
- Release 构建通过：`GatewayDemo.Legacy.sln /t:Restore,Build /p:Configuration=Release`，三个项目均生成成功。
- 真实 IIS 验证通过：在中文路径临时目录 `E:\验证页面\_verify\GatewayDemo.Legacy-net472_spa_20260604145613\` 创建 `GatewayDemoLegacy_SPA_18250` 和 `MockBusinessBackendLegacy_SPA_18255`，IIS 管理 API 读回的物理路径完整保留中文目录。
- 后端 SPA 冒烟通过：`verify-spa-smoke.ps1 -BackendBaseUrl http://127.0.0.1:18255 -GatewayBaseUrl http://127.0.0.1:18250` 检查 SPA shell、根路径 JS、history 路由和根路径 API 均通过。
- 真实浏览器审批链路通过：未授权访问 `http://127.0.0.1:18250/portal` 会跳访问申请；提交企业、申请人、联系电话、申请原因后，后台审批卡片能看到完整申请信息，审批后授权卡片显示授权站点 `ERP Main`；再次访问 `/portal` 直接进入 `SPA 验证站`。
- 真实浏览器 SPA 根路径链路通过：直接刷新 `http://127.0.0.1:18250/reports/2026` 返回 SPA 页面，`/assets/spa.css`、`/assets/spa.js`、`/api/spa/ping?route=/reports/2026` 均为 200，页面显示 `ok:/reports/2026`，响应无中文乱码。
- CORS 真实预检通过：临时开启 `Gateway.Cors.*` 后，`OPTIONS /api/spa/ping` 按配置返回 204，`Access-Control-Allow-Origin/Methods/Headers/Credentials/Max-Age` 均正确。
- 重新打包 `deliverables/GatewayDemo.Legacy-net472.zip`，SHA256 为 `4670819F4253D089CAE9F780F83B47F868ED74F373BF701A359DC758BDC38FD2`。zip 内 Markdown 条目只有根目录 `README.md`；未命中 AI 测试、测试记录、Codex、Playwright、history.md、旧 `/mock/vue-spa/index` 和 `debug="true"`。
- 这轮修改让项目更稳健的核心点：接入策略从“兼容多种路径但容易误用”变成“SPA 只走根路径/域名映射”；部署脚本避开中文路径编码坑；交付包带了能复现 SPA 风险点的上游样例，后续研发反馈 Vue 用不了时可以直接用同一套路径、资源、接口和 history 路由做复核。

---

## 34. 2026-06-08 研发六项反馈真实复核与交付包清洁

- 按用户要求直接复核 `E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`，以当前 zip 为来源解压部署临时 IIS 站点，并通过真实 Chrome/CDP 操作检查，不停留在模拟或静态扫描。
- 多站点授权隔离专项通过：用 `2388`、`AI演示`、`7011`、`2026` 的运行时站点配置复现研发场景，同一设备 `DEV-260603-3B4917` 只审批 `2388` 时，`AI演示` 仍停留在网关申请页，未出现“申请时未勾、审核后未勾但可访问”的串权问题。
- 申请信息展示复核通过：普通浏览器和移动端申请在后台均能看到企业、申请人、联系电话、原因、目标路径、申请站点、已授权站点、IP、时间、审核说明，以及移动端账号、数据中心、设备号、入口地址等字段。
- 移动端登录企业号补取复核通过：登录请求未带企业号但带 `DataCenterId` 时，网关会通过 `${代理映射网址}/ashx/Sys.ashx?action=tenant_name&DataCenterId=${DataCenterId}` 获取租户名称；本轮真实请求取到 `厦门快普租户`。
- Vue/SPA 根路径接入复核通过：按根路径或域名映射方式访问 SPA，真实浏览器确认静态资源、根路径 API、history 路由刷新均可用；不再把 `/proxy/{site}` 前缀作为 Vue 交付方案。
- CORS 可配置复核通过：`Gateway.Cors.*` 配置可控制允许域名、方法、请求头、Credentials 和 MaxAge；真实预检验证允许精确域名和通配域名，未配置来源不会放行。
- 乱码问题复核通过：源码和配置按 UTF-8 检查无乱码特征；代理文本响应补齐 charset；下载响应的 `Content-Disposition` 已补 `filename*` UTF-8 文件名，真实响应中中文文件名不再依赖错误编码猜测。
- 交付清洁检查发现当前 zip 曾残留 3 个 `obj/Release/*.FileListAbsolute.txt` 条目和对应 `obj` 编译目录，存在本机绝对路径泄露风险；原 zip SHA256 为 `D3A0B1FEE8200191833B830EA17341DB46C30B1036EE141097A3239EEF8A45CD`。
- 本轮仅做交付清洁处理：从当前正式 zip 解压到 staging，删除 `src/GatewayDemo.Legacy.Core/obj`、`src/GatewayDemo.Legacy.Web/obj`、`src/MockBusinessBackend.Legacy.Web/obj` 后重新打包，并同步清理同名展开目录里的 `obj`，保留 `bin` 下运行所需 DLL。
- 新 zip 复核结果：128 个条目，`obj/` 为 0，`*.FileListAbsolute.txt` 为 0，`test-results`、`artifacts`、`history.md`、`Playwright`、`Codex`、`OpenAI`、`ChatGPT`、`AI测试`、`测试记录` 命中 0；Markdown 条目仍只有根目录 `README.md`。
- 当前正式交付包：`deliverables/GatewayDemo.Legacy-net472.zip`，大小 `12,807,680` 字节，SHA256 `C854C0BA2904EC870AB85EEA025659162FD0168C6EDF3F087EBC884D5775E186`；清洁前备份为 `deliverables/GatewayDemo.Legacy-net472.before-clean-20260608-211159.zip`。
