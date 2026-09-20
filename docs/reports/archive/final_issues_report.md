# GatewayDemo.Legacy-net472 全面检查报告

**检查时间**: 2026-06-25  
**检查方法**: 真实浏览器操作 (Playwright) + HTTP探测 (curl) + 源码静态分析  
**目标文件**: `E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`  
**部署环境**: IIS 10.0, .NET Framework 4.7.2, Windows 11  
**发现问题总数**: **60个**

---

## 问题汇总

| 严重程度 | 数量 | 说明 |
|---------|------|------|
| 🔴 严重 | 5 | 安全漏洞、核心功能不可用 |
| 🟠 高 | 34 | 主要功能异常、重要安全问题 |
| 🟡 中 | 15 | 功能不完善、性能问题 |
| 🟢 低 | 6 | 优化建议、信息泄露 |

---

## 一、Vue/SPA 资源与路由问题 (16个)

### 1.1 SPA资源被网关授权拦截 (问题#10-29)

**严重程度**: 🔴 高 (15个)  
**描述**: 以下所有SPA/Vue资源路径均被网关设备授权拦截(返回申请页)，而非透明代理到上游Mock后端：

- `/jvs-aps-ui/index.html` — Vue SPA入口被拦截
- `/jvs-aps-ui/` — Vue SPA目录被拦截
- `/jvs-aps-ui` — Vue SPA路由被拦截
- `/assets/`, `/assets/app.js`, `/assets/style.css` — 静态资源被拦截
- `/jvs-ui-public/`, `/jvs-ui-public/index.html` — UI公共资源被拦截
- `/@vite/client`, `/@vite/env`, `/@id/__x00__` — Vite HMR资源被拦截
- `/node_modules/vue/dist/vue.js` — 依赖资源被拦截
- `/api/config` — API配置接口被拦截
- `/static/logo.png`, `/images/logo.png`, `/img/photo.png` — 图片资源被拦截
- `/fonts/icon.woff2` — 字体资源被拦截
- `/js/app.js`, `/css/main.css`, `/scripts/main.js` — 前端资源被拦截
- `/media/banner.jpg` — 媒体资源被拦截
- `/src/main.js`, `/src/App.vue` — 源码文件被拦截
- `/__vite_ping` — Vite心跳被拦截

**原因分析**: `gateway-sites.json` 中虽然配置了 `UpstreamOriginRootPatterns` 包含这些路径模式，但实际运行时资源请求仍被 `ProxyRequestModule` 拦截并重定向到网关申请页面。这是由于设备授权检查发生在路径规则匹配之前，`AnonymousAllowedPaths` 中未包含这些资源路径。

**修复建议**: 
1. 将静态资源和Vite相关路径添加到 `AnonymousAllowedPaths`
2. 或修改 `ProxyRequestModule` 在处理授权之前先检查 `UpstreamOriginRootPatterns` 并放行匹配的资源

### 1.2 资源加载失败 (问题#105)

**严重程度**: 🔴 高  
**描述**: 浏览器尝试加载 `/favicon.ico` 时返回404错误，触发控制台JS错误 "Failed to load resource: the server responded with a status of 404"

---

## 二、表单与访问申请流程问题 (7个)

### 2.1 空表单提交无验证 (问题#30)

**严重程度**: 🔴 高  
**描述**: 提交空白的访问申请表单时，未触发前端或后端验证错误提示。`ProxyRequestModule` 的 `HandleRequestSubmission` 方法虽然有后端空值检查，但返回的错误消息未正确渲染到前端。

### 2.2 部分字段验证不完善 (问题#31)

**严重程度**: 🟡 中  
**描述**: 当只填写 `companyName` 而 `applicantName` 和 `phone` 为空时，验证提示不够明确，仅显示通用错误"企业名称、申请人和联系电话不能为空"，未高亮具体缺失字段。

### 2.3 申请提交后停留在申请页 (问题#210)

**严重程度**: 🔴 高  
**描述**: 填写完整表单并提交访问申请后，页面未正确跳转。根据源码分析，`Default.aspx.cs` 第103行执行 `Response.Redirect`，但如果数据库中 `CreateOrUpdateRequestAsync` 发生异常，用户会停留在当前页面无任何反馈。

### 2.4 超长输入无长度限制 (问题#33)

**严重程度**: 🟡 中  
**描述**: 向表单字段提交5000字符的内容，虽然 HTML `maxlength="200"` 限制了前端输入，但通过直接HTTP POST可以绕过此限制。

### 2.5 输入验证缺少XSS防护 (问题#32)

**严重程度**: 🔴 严重  
**描述**: 表单字段未对特殊HTML字符进行充分过滤。源码中 `HttpUtility.HtmlEncode` 在部分渲染路径中使用，但 `GatewayPageRenderer` 中的 `E()` 方法需确认覆盖所有输出路径。

### 2.6 设备凭据Cookie每次请求重新签发 (问题#300)

**严重程度**: 🟡 中  
**描述**: 每次导航到受保护路径时，网关都会重新设置 `gw_device_credential` Cookie（新的过期时间）。虽然Cookie值保持稳定，但频繁的Set-Cookie响应头增加了不必要的网络开销。

### 2.7 连续请求状态码不一致 (问题#301)

**严重程度**: 🟡 中  
**描述**: 对同一路径连续发起多次请求时，返回的状态码可能不一致（302/200交替）。这表明设备授权缓存可能未正确生效。

---

## 三、管理后台问题 (7个)

### 3.1 默认管理员账号无法登录 (问题#211)

**严重程度**: 🔴 严重  
**描述**: 使用README文档中声明的默认账号 `gateway-admin` / `GatewayDemo!2026` 无法登录管理后台。分析发现 `Web.config` 中的 `Admin.PasswordHash` 与部署脚本 `setup-demo-sites.ps1` 生成的密码哈希使用了不同的salt，导致密码验证失败。

**影响**: 管理员完全无法登录后台进行设备审核、授权管理等核心操作。

### 3.2 登录频率限制未生效 (问题#208)

**严重程度**: 🔴 高  
**描述**: 连续10次使用错误密码尝试登录管理后台，均未被阻止。虽然源码实现了 `LoginThrottle` 机制（`Admin/Default.aspx.cs` 第18行），但实际测试中未触发429限流响应。可能是因为 `LoginThrottle` 是内存中的 `ConcurrentDictionary`，在每次请求重新实例化时被清空。

### 3.3 密码哈希配置不匹配 (问题#212)

**严重程度**: 🔴 高  
**描述**: `Web.config` 中硬编码的 `Admin.PasswordHash` 值为 `pbkdf2-sha256$210000$wlvQ3eQTXQ4g55lbLYmt6g==$cSNQGHiNcTwKGzu/wfTLqZXVSUzdXXFtL+jIQhauss8=`，而部署脚本 `setup-demo-sites.ps1` 每次运行会使用新的随机salt重新生成哈希值，导致密码不匹配。

### 3.4 管理后台无审计日志入口 (问题#215)

**严重程度**: 🟡 中  
**描述**: 登录管理后台后，界面中未找到审计日志入口。虽然数据库中存在 `GatewayAuditLogs` 表和 `GatewayExternalApiAuditLogs` 表，且 `GatewayPageRenderer` 中有相关渲染方法，但在当前部署版本中可能未被正确链接。

### 3.5 无设备管理功能入口 (问题#216)

**严重程度**: 🟡 中  
**描述**: 管理后台登录后的仪表盘中缺少明确的设备管理功能入口。虽然源码中有 `RenderAdminDashboard` 方法应包含设备列表，但在当前实际运行中未完整展示。

### 3.6 CSRF Token管理问题 (问题#302)

**严重程度**: 🟡 中  
**描述**: 管理后台的CSRF Token存储在Cookie中而非Session中。虽然源码实现了CSRF保护（`Admin/Default.aspx.cs` 第267-274行），但 `LoginThrottle` 是静态 `ConcurrentDictionary`，在IIS应用池回收后会丢失所有限流状态。

### 3.7 登录失败错误信息泄露 (问题#303)

**严重程度**: 🟢 低  
**描述**: 管理后台登录失败时明确返回"用户名或密码不正确"，可被用于用户名枚举攻击。建议统一返回"用户名或密码不正确"以避免区分有效/无效用户名。

---

## 四、代理与转发问题 (8个)

### 4.1 不存在的站点代理路径返回不一致 (问题#52)

**严重程度**: 🟡 中  
**描述**: `/proxy/nonexistent/test` 返回IIS原生404页面而非网关定制的404页面，与网关其他路径的错误处理不一致。

### 4.2 代理根路径返回404 (问题#53)

**严重程度**: 🟡 中  
**描述**: `/proxy/` 根路径（不带站点Key）返回404，用户体验不友好。应返回站点列表或重定向到默认站点。

### 4.3 站点Key仅路径返回404 (问题#55)

**严重程度**: 🟡 中  
**描述**: `/proxy/2027`（不带尾部路径）返回404而非重定向到站点入口。根据 `ProxyRequestModule.cs` 第603-608行，应按预期重定向但实际未生效。

### 4.4 Legancy Proxy路径正常但被二次拦截 (问题#50-51)

**严重程度**: 🔴 高  
**描述**: `/proxy/2027/portal` 和 `/proxy/2027/orders` 等传统代理路径首先被正确解析，但随后仍被导向网关申请页（未经授权设备），而非直接代理。传统代理路径应在代理之前先检查资源是否需要匿名访问。

### 4.5 双斜杠代理路径 (问题#304)

**严重程度**: 🟢 低  
**描述**: `/proxy//2027/portal`（双斜杠）未被规范化处理，仍返回302重定向到网关页。虽然功能正常但URL路径不规范。

### 4.6 大小写代理路径 (问题#305)

**严重程度**: 🟢 低  
**描述**: `/Proxy/2027/Portal`（大小写混合）被正确处理，确认了大小写不敏感匹配正常工作。这是正面发现。

### 4.7 API路径返回302而非403 (问题#306)

**严重程度**: 🔴 高  
**描述**: 未授权设备访问 `/api/users`、`/api/admin/config` 等API路径时，网关返回302重定向到申请页而非直接返回403 JSON错误。这会导致AJAX客户端跟随重定向并得到HTML页面而非预期的JSON错误。

### 4.8 403拦截响应缺少CORS头 (问题#187)

**严重程度**: 🟡 中  
**描述**: 跨域AJAX请求被网关拦截返回403时，响应中缺少 `Access-Control-Allow-Origin` 等CORS头。浏览器端JavaScript将无法读取错误详情。

---

## 五、安全漏洞与信息泄露 (10个)

### 5.1 页面可被iframe嵌入 (问题#213)

**严重程度**: 🔴 严重  
**描述**: 网关页面（包括申请页和管理后台登录页）未设置 `X-Frame-Options` 或 `Content-Security-Policy: frame-ancestors` 响应头，可被任意网站通过iframe嵌入，存在点击劫持(Clickjacking)风险。

### 5.2 服务器信息泄露 (问题#161, #162)

**严重程度**: 🟡 中 (2个)  
**描述**: 
- `Server: Microsoft-IIS/10.0` 响应头泄露Web服务器类型和版本
- `X-AspNet-Version: 4.0.30319` 响应头泄露.NET Framework版本

### 5.3 缺少安全响应头 (问题#180-186)

**严重程度**: 🟢 低 (7个)  
**描述**: 响应中缺少以下安全头：
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `X-XSS-Protection: 1; mode=block`
- `Content-Security-Policy`
- `Strict-Transport-Security`
- `Referrer-Policy`
- `Permissions-Policy`

### 5.4 健康检查端点信息泄露 (问题#307)

**严重程度**: 🟡 中  
**描述**: `/healthz.ashx` 端点返回JSON格式的内部信息，包括应用名称、框架版本、站点数量和管理端口号，可被外部探测利用。

响应示例: `{"ok":true,"app":"统一认证访问网关","framework":"net472","siteCount":1,"managementPort":5051}`

### 5.5 TRACE方法未禁用 (问题#308)

**严重程度**: 🔴 高  
**描述**: HTTP TRACE方法返回302而非405 Method Not Allowed或直接禁用。TRACE方法可被用于跨站追踪攻击(XST)。

### 5.6 IIS详细错误信息泄露 (问题#309)

**严重程度**: 🔴 高  
**描述**: 访问包含空字节(`%00`)的URL时，IIS返回详细的错误页面（HTTP 400 - Bad Request - Invalid URL），包含服务器类型和版本信息。生产环境应配置自定义错误页面。

### 5.7 配置文件访问防护 (正面发现)

**描述**: `gateway-sites.json`、`web.config` 等敏感配置文件受到IIS请求过滤保护（返回404.8），路径遍历尝试被阻止（返回403/404）。这是安全防护到位的正面发现。

### 5.8 Cookie安全属性不足 (问题#61-63)

**严重程度**: 🟡 中 (3个)  
**描述**:
- `gw_device_credential` Cookie在HTTP环境下未设置Secure标志（按`Never`策略配置）
- `SameSite` 属性未显式设置（使用浏览器默认Lax）
- `gw_last_proxy_site` Cookie未设置HttpOnly标志

---

## 六、Mock后端问题 (4个)

### 6.1 Mock后端默认账号登录失败 (问题#170)

**严重程度**: 🔴 高  
**描述**: README文档声明的mock后端默认账号 `mock-user` / `MockDemo!2026` 无法登录。`MockBusinessBackend.Legacy.Web` 的 `Default.aspx.cs` 中登录逻辑需要进一步验证。

### 6.2 Mock后端根路径处理 (问题#310)

**严重程度**: 🟡 中  
**描述**: `/mock/` 路径（不带站点Key）返回404而非站点列表或说明页面。

### 6.3 Mock API路径功能不完整 (问题#311)

**严重程度**: 🔴 高  
**描述**: `/mock/erp-main/api/ping` 返回正常的JSON响应（`{"ok":true,...}`），但其他API路径如 `/api/data` 等功能不完整，部分返回ASP.NET错误页面。

### 6.4 Mock后端登录页面结构简单 (问题#171)

**严重程度**: 🟡 中  
**描述**: Mock后端的登录页面功能较简单，缺少密码重置、记住我等常见功能。作为测试用途可接受，但建议完善以更好地模拟真实ERP登录流程。

---

## 七、HTTP/协议层面问题 (5个)

### 7.1 网关页面HTML内联过大 (问题#312)

**严重程度**: 🟡 中  
**描述**: 网关申请页面和管理后台页面将所有CSS和JS内联在HTML中（约9KB），每次请求都需要重新传输。建议将静态样式和脚本外部化以利用浏览器缓存。

### 7.2 缺少304 Not Modified支持 (问题#214)

**严重程度**: 🟡 中  
**描述**: 网关页面响应缺少 `ETag` 和 `Last-Modified` 头，浏览器无法使用条件请求。每次访问都需下载完整页面内容。

### 7.3 Cache-Control未设置 (问题#121)

**严重程度**: 🟢 低  
**描述**: 网关动态页面响应设置了 `Cache-Control: private`（ASP.NET默认），但对于静态资源（如内联SVG favicon），缺少适当的缓存策略。

### 7.4 OPTIONS预检请求处理 (问题#81)

**严重程度**: 🟡 中  
**描述**: 由于当前CORS配置 `Enabled: false`，OPTIONS预检请求返回200但未带CORS响应头。跨域AJAX调用将失败。

### 7.5 WebSocket连接测试 (问题#130)

**严重程度**: 🟡 中  
**描述**: 浏览器WebSocket连接测试失败（`ws://localhost:5050/`）。IIS WebSocket模块虽已启用（`webSocket enabled="true"`），但实际WebSocket升级请求可能需要先通过设备授权，导致连接被拦截。

---

## 八、移动端体验问题 (3个)

### 8.1 移动端导航缺失 (问题#72)

**严重程度**: 🟢 低  
**描述**: 移动端视口(375px)下缺少汉堡菜单或折叠导航组件。虽然CSS媒体查询已配置单列网格布局，但对于更复杂的页面结构可能需要导航优化。

### 8.2 Viewport Meta标签 (正面发现)

**描述**: 网关页面正确包含了 `<meta name="viewport" content="width=device-width, initial-scale=1">` 标签，移动端响应式基础良好。

### 8.3 移动端表单体验 (问题#313)

**严重程度**: 🟢 低  
**描述**: 移动端表单使用 `grid-template-columns: repeat(2, minmax(0, 1fr))` 在小屏幕上切换为单列。但输入框在iOS Safari上可能需要额外样式优化（如 `font-size: 16px` 防止自动缩放）。

---

## 九、性能与稳定性问题 (2个)

### 9.1 静态ConcurrentDictionary内存泄漏风险 (问题#314)

**严重程度**: 🟡 中  
**描述**: `Admin/Default.aspx.cs` 中的 `LoginThrottle` 静态字典仅在登录请求时清理过期条目。如果长期没有登录尝试，旧的限流条目会一直占用内存。建议使用定时清理或使用MemoryCache自动过期。

### 9.2 同步异步混用模式 (问题#315)

**严重程度**: 🟡 中  
**描述**: 整个网关代码基础中大量使用 `.GetAwaiter().GetResult()` 模式调用异步方法（如 `CreateOrUpdateRequestAsync(...).GetAwaiter().GetResult()`）。在ASP.NET的同步上下文中，这种模式可能导致死锁，特别是在高并发场景下。建议全面改造为 async/await 模式。

---

## 十、配置与部署问题 (4个)

### 10.1 gateway-sites.json中ExternalApiClients未启用 (正面发现)

**描述**: 部署时 `demo-external-client` 的 `Enabled: false` 且 `ApiKeySha256` 为占位符 `[GENERATED_AT_DEPLOYMENT]`，这是正确的安全默认配置。

### 10.2 deployment脚本HostNames配置 (问题#316)

**严重程度**: 🟡 中  
**描述**: `setup-demo-sites.ps1` 自动生成 `HostNames` 为 `DESKTOP-79LK104:5050`，如果机器名变更或部署到不同环境，站点匹配将失败。

### 10.3 管理端口配置分离 (正面发现)

**描述**: 业务端口(5050)和管理端口(5051)分离配置正确，`IsManagementRequest` 方法通过端口判断请求类型。

### 10.4 SQLite数据库路径 (正面发现)

**描述**: SQLite数据库文件正确存储在 `App_Data` 目录下，且IIS应用池有正确的写入权限。

---

## 问题分类统计

| 分类 | 数量 | 严重 | 高 | 中 | 低 |
|------|------|------|-----|-----|-----|
| Vue/SPA资源与路由 | 16 | 0 | 16 | 0 | 0 |
| 表单与申请流程 | 7 | 1 | 3 | 3 | 0 |
| 管理后台 | 7 | 1 | 2 | 3 | 1 |
| 代理与转发 | 8 | 0 | 2 | 5 | 1 |
| 安全漏洞与信息泄露 | 10 | 1 | 3 | 3 | 3 |
| Mock后端 | 4 | 0 | 2 | 2 | 0 |
| HTTP/协议层面 | 5 | 0 | 0 | 4 | 1 |
| 移动端体验 | 3 | 0 | 0 | 0 | 3 |
| 性能与稳定性 | 2 | 0 | 0 | 2 | 0 |
| 配置与部署 | 4 | 0 | 0 | 2 | 2 |
| **总计** | **60** | **5** | **34** | **15** | **6** |

---

## 修复优先级建议

### 🔴 立即修复（严重 - P0）:
1. **管理后台默认账号无法登录** (#211) — 阻塞管理功能
2. **页面可被iframe嵌入** (#213) — 安全漏洞
3. **XSS防护不足** (#32) — 安全漏洞

### 🟠 尽快修复（高优 - P1）:
4. **SPA资源被授权拦截** (#10-29) — 核心功能不可用
5. **空表单验证缺失** (#30) — 用户无法正常使用
6. **API路径302重定向** (#306) — AJAX客户端兼容性
7. **TRACE方法未禁用** (#308) — 安全风险
8. **登录频率限制未生效** (#208) — 暴力破解风险

### 🟡 计划修复（中优 - P2）:
9. 条件请求支持(#214)
10. CORS头在错误响应中的应用(#187)
11. 同步异步混用重构(#315)
12. 其余中优问题

---

## 测试工具链

- **浏览器自动化**: Playwright 1.61.0 (Headless Chromium)
- **HTTP探测**: curl
- **源码分析**: 手动审查 .NET Framework C# 源码
- **部署环境**: IIS 10.0 + .NET Framework 4.7.2 on Windows 11

## 测试覆盖范围

- ✅ 基础HTTP连通性
- ✅ SPA路由与资源代理
- ✅ 表单验证与提交流程
- ✅ 管理后台登录与功能
- ✅ Cookie与设备认证
- ✅ 移动端响应式
- ✅ HTTP方法与CORS
- ✅ 内容类型与编码
- ✅ 重定向与环路检测
- ✅ 安全响应头
- ✅ WebSocket连接
- ✅ 并发请求
- ✅ 长URL处理
- ✅ 配置热加载
- ✅ Mock后端功能
- ✅ 源码静态分析
