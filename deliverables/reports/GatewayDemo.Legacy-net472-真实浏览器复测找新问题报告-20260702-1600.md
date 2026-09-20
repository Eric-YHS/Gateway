# GatewayDemo.Legacy-net472 真实浏览器复测报告（找新问题）

日期：2026-07-02
对象：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`
SHA256：`1175CA466E39B94CD11BA460E5E09B4D0C606743E5023AB83E1951F521ACDE2A`（12,677,707 bytes，2026-07-02 14:24）

## 检查方式（真实操作，非模拟）

1. 从当前交付 zip **全新解包**到 `E:\验证页面\_recheck_20260702\extract`（哈希与交付包一致）。
2. MSBuild 17.14 Release **重新构建**：0 warning / 0 error。
3. 用包内 `setup-demo-sites.ps1` 部署到 IIS 全新端口，并覆盖 `gateway-sites.json` 复刻**研发现场 4 站点拓扑**：
   - 2388 OA/WMS（根暴露，HostNames=29050+29051，其中 29051 同时是管理端口）
   - 2027 ERP（根暴露，HostNames 带 `http://` 前缀 = 29052）
   - 17011 财务（无 HostNames、不根暴露，仅 `/proxy/17011/` 可达）
   - 9999 APS/Vue SPA（EntryPath=`/jvs-aps-ui/index.html`，HostNames=29053）
   - 后端 mock = 29055
4. **Playwright Chromium 真实浏览器** + request API 执行 3 组脚本（`hunt.js` / `login-flow.js` / `final-probe.js`），覆盖用户点名的全部维度：VUE/SPA、各类转发、静态资源、移动请求、更新、实际操作、体验。脚本与截图在 `E:\验证页面\_recheck_20260702\hunt-output\`。
5. 对**纯净解包副本**做全包合规扫描（75 个源码/脚本/配置文件）。

IIS 站点保留可直接核对：`RecheckGw20260702`(29050/29051/29052/29053)、`RecheckBk20260702`(29055)。

## 结论

**本轮真实浏览器复测未发现功能性 bug。** 约 50 项断言全部通过，交付物合规扫描干净。这与该包已历经多轮修复一致。下面如实列出**已验证通过项**、**极少数瑕疵项（研发评审可能点名，非功能中断）**、以及**本轮单机 loopback 无法充分覆盖、若研发再报新 bug 应优先排查的残留面**。

## 一、已真实验证通过（无问题）

- **VUE/SPA（9999）**：index.html 直开、`/jvs-aps-ui/orders` 与 `/jvs-aps-ui/reports/2026` 深链接刷新（history 路由不落 404，返回 SPA 外壳）、客户端点击导航、运行时 `fetch(/jvs-aps-ui/api/spa/ping)` 返回 200 JSON、`:29053/` 根路径、`base` 路径检测；**根暴露形式与 `/proxy/9999/jvs-aps-ui/` 前缀形式均正确渲染**。无 console/JS 错误。
- **各类转发**：4 站点业务页均正确回源（2388→WMS 东仓、2027→ERP 主站、17011→财务共享、9999→企业应用门户）；根暴露、`/proxy/<key>/` 前缀、**在 2388 端口用 `/proxy/2027/` 跨站取到 2027**、查询串保留均正确；上游 `portal→login` 302 的 Location 正确改写为网关相对路径。
- **静态资源**：`/assets/mock.css`(text/css)、`erp-mark.svg`/`grid.svg`(image/svg+xml) 经网关 200 且 MIME 正确。
- **授权/更新与隔离（核心安全属性）**：申请→管理台审批→访问放行全链路正常；**只授权 2388 的设备访问 2027 被网关正确拦截（302→重新申请），不越权**；未授权设备被拦截；管理台审批/勾选/中文提示无乱码。
- **实际操作（真实登录）**：2388、2027 浏览器 POST 登录，登录后落在网关 `/portal`（**未泄露 `/mock/` 上游路径、无登录回环**），显示已认证内容（用户 mock-user、退出登录）；错误密码中文提示不乱码；**退出登录**正确回到 `/login`。
- **移动/API 请求**：`/sys.ashx`、`/ashx/sys.ashx`、`/datacenter/` 匿名引导 200；`user/login` 返回 JSON `登录成功`（无乱码、可解析）；`WCFService/PostBus.ashx` 返回 JSON；**multipart 文件上传** `ChatUploadImg.ashx` 返回 200 JSON（请求体正确转发）。
- **体验/健壮性**：未知站点/未知路径返回**网关自绘 404**（`统一认证访问网关-页面不存在`，带 `charset=utf-8`）；900 字符超长 URL 段 200；`..%2f` 路径穿越被拦、无配置泄露；管理端口 29051 正确只服务管理台（`/portal`→404），不与业务混淆；全程无乱码。
- **交付合规**：无 AI/Claude/Codex/Copilot 痕迹、无测试/探针字眼、无 TODO/FIXME/HACK、无硬编码 IP/开发机路径/机器名、无 `*.pdb`、无 `debug="true"`、无 `console.log`、仅 1 个 README。

## 二、瑕疵项（研发评审可能点名，均非功能中断）

| # | 级别 | 现象 | 建议 |
|---|------|------|------|
| M1 | 中/低 | 站点**未配置 `ExternalApiClients`** 时，其 `/api/*` 对未认证 API/移动客户端返回 **302→HTML 浏览器认证页**而非 401+JSON（实测 2027 无白名单→302，9999 有白名单→200）。浏览器无碍，但 API/移动集成方拿到 HTML。 | 对明显 API 请求（`Accept: application/json`/XHR）返回 401+JSON；或在 README 强调 API/移动接入的站点必须配 `ExternalApiClients`。 |
| M2 | 低 | 被 **IIS 请求过滤**拦截的 URL（含 `..%2f` 等）命中 **IIS 原生 404.8 详细错误页**，暴露 `IIS 10.0` 版本横幅（普通未知路径已是网关自绘 404）。 | Web.config `httpErrors existingResponse="Replace"` 统一到自定义错误页，隐藏服务器指纹。 |
| M3 | 低 | 网关 `Web.config` 为 `customErrors mode="RemoteOnly"`，本地请求可能看到 ASP.NET 完整错误详情。 | 交付产品建议 `mode="On"` 彻底关闭详细错误。 |
| M4 | 信息 | `Web.config` 内置默认管理口令哈希（GatewayDemo!2026）、`Mock.Password`（MockDemo!2026）。README 已声明"仅供冒烟、生产必须重置"。 | 可接受；若想更稳妥，交付版留占位、仅文档给默认值。 |

## 三、残留风险面（本轮单机 loopback 未能充分覆盖——研发再报新 bug 时优先查这里）

- **R1 HTTPS/SSL 卸载前置**：本轮仅 HTTP。Cookie `Secure`、`ExternalScheme`、`X-Forwarded-Proto`、Location 的 scheme 改写未实测——研发若在 HTTPS/负载均衡后部署，这里最可能出问题。
- **R2 真实多主机域名下的设备 Cookie 作用域**：本轮 loopback 同主机不同端口，Cookie 不按端口隔离，与真实多域名（`DeviceCookieDomain`）行为不同。
- **R3 WebSocket 代理**：IIS-WebSockets 本轮刚启用且 pending restart，未实测；有实时推送/长连接的业务需专项验证。
- **R4 HTTP.sys 长 URL**：900 字符段本轮通过，但更长 URL 在未跑 `-ConfigureHttpSysLongUrlSupport` 时可能 404（需注册表 + 重启 HTTP 服务）。JVS 类超长 URL 场景需确认此配置已生效。
- **R5 高并发/多设备并发写 SQLite**：本轮单用户；无 WAL/重试时高并发可能 `database is locked`。
- **R6 其他浏览器**：本轮仅 Chromium；Firefox/Safari/旧 Edge/IE 内核下 SPA history 与 Cookie 行为未覆盖。

## 建议

交付前若能补测 **R1（HTTPS 前置）** 与 **R3（WebSocket）**，可堵住研发最可能命中的两类环境相关问题。M1/M2/M3 是低成本加固，建议一并处理以减少评审来回。
