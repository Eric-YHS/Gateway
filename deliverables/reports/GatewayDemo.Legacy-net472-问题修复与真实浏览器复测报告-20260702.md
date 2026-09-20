# GatewayDemo.Legacy-net472 问题修复与真实浏览器复测报告

日期：2026-07-02
对象：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`

## 交付物

- 最终包：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`
- SHA256：`1175CA466E39B94CD11BA460E5E09B4D0C606743E5023AB83E1951F521ACDE2A`
- 大小：`12,677,707` bytes（2026-07-02 14:24）
- 覆盖前备份：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.before-20260702-fix.zip`
- 备份 SHA256：`B53CD57943F5A0DD1ACF5B823A33B03E82A1A587B31DB699670EF1819B9DDD14`
- 打包方式：官方脚本 `scripts\legacy\package-legacy.ps1 -Configuration Release` 重新构建生成（MSBuild 17.14，0 warning / 0 error）

## 检查方式（真实操作，非模拟）

按研发现场拓扑在本机 IIS 复刻部署（4 个站点：2388/2027/17011/9999；站点 9999 EntryPath=`/jvs-aps-ui/index.html`、HostNames 带 `http://` 前缀并独占一个端口；多站点同时 `ExposeLegacyAppAtRoot=true`；多端口绑定 + 独立管理端口），先在修复前的包上真实复现问题，修复后用 Playwright Chromium 真实浏览器执行全流程复测；最终 zip 再次解包、用包内 `setup-demo-sites.ps1` 全新构建部署到 IIS（端口 28050-28055），复测 22 项全部通过，无 console 错误、无乱码。

- 复现/复测脚本与截图：`E:\验证页面\_fixwork_20260702\`（repro-output / final-output）
- 最终验证站点保留在 IIS：`CodexFinalGw20260702`（28050/28051/28052/28053）、`CodexFinalBk20260702`（28055），可直接打开核对

## 四个反馈问题的结论与修复

### 1. 同设备两个站点分两次申请，实际只授权一个站点

- 在修复前的包上真实复现：两站点都审批通过后，访问第二个站点 `/proxy/2027/portal` 返回的却是第一个站点（2388）的页面。根因是多个站点 `ExposeLegacyAppAtRoot=true` 共用端口时，网关把 `/proxy/2027/portal` 302 折叠成根路径 `/portal`，随后按 Host 归属又解析回 2388——从使用者角度就是"第二个站点的授权没生效/只勾了一个站点"。
- 修复：只有当前请求的端口确实归属该站点（HostNames 命中或它是唯一根站点）时才折叠为根路径；否则保持 `/proxy/<站点键>/` 前缀直接代理，响应里的 Location/Cookie 改写也按同一规则决定。
- 同时把管理台审批表单的默认勾选改为"本次申请站点 ∪ 该设备已授权站点"，与批准后的合并结果一致，避免误读；申请端两次提交合并、审批端并集合并逻辑经真实浏览器验证正确（T1.1-T1.5 全过，2388 显示 WMS、2027 显示 ERP，各自正确）。

### 2. "更新授权站点"报错（重启 IIS 无效）+ 页面乱码

确定性根因共四条，全部修复：

- 授权是"所有站点"模式时取消勾选后一个站点都没选中，提交触发仓储层异常；管理台整页 500 → customErrors 跳到管理端口不放行的路径 → 无关的 404/乱码页，且重启 IIS 必然复现。现改为页面层校验 + 中文提示横幅"请至少选择一个授权站点"，前端提交时也会拦截。
- 管理台所有操作（审批/驳回/撤销/更新授权/签发凭据）增加异常捕获，失败时在管理台顶部显示中文错误原因，不再跳转丢失上下文；新增 `Application_Error` 兜底，任何未捕获异常都输出带 `charset=utf-8` 的自绘错误页（乱码根因：此前错误通道响应无 charset 声明，UTF-8 中文被 zh-CN 浏览器按 GBK 嗅探，研发截图中"閫夋嫨…鐨勮姹?"正是"选择…的请求"的 UTF-8 字节按 GBK 渲染）。
- IIS 重启/会话超时后旧页面提交：原来返回英文 400"页面不存在"，现改为重新渲染管理台 + 中文提示"管理页面已过期，本次操作未执行；页面已刷新，请重新提交"，重新提交即可（真实重启 IIS 应用池验证通过）。
- 旧版本 SQLite 库升级：补齐 `PolicyMode`、`LastSeenSiteKey`、`ReviewNote`、审计表 `SiteKey/ClientIp` 等全部新列的 `ALTER TABLE` 迁移，避免保留旧 `gateway-demo-legacy.db` 部署新包后"更新授权站点"持续失败。
- 另外：代理透传上游响应时,非 ASCII 的 HTTP 状态行 reason phrase 改为标准英文短语（状态行无 charset 概念，是另一处乱码向量）；网关自绘 404/错误输出全部显式 `text/html; charset=utf-8`。
- 操作成功后管理台现在会显示"已更新授权站点。/已批准该访问申请。"等确认横幅，不再静默。

### 3. 浏览器设备信号只用 User-Agent，会不会导致不同设备同一个设备号？

结论：**不会**。浏览器设备号（设备编号/设备标识）来自浏览器 Cookie 中的随机凭据（32 字节随机数），与 User-Agent 无关；同型号同版本、UA 完全相同的两台设备也会得到不同设备号，一台的审批不会放行另一台。真实浏览器验证：两个相同 UA 的独立浏览器环境得到不同设备编号（T3.1）；UA/平台信号变化时已授权设备会被要求重新审核而不是静默放行（T3.2）。

User-Agent（连同 `Sec-CH-UA-Platform`、`Sec-CH-UA-Mobile`，默认三者，版本号归一化）只作为"环境信号"用于触发复审。可通过 `Web.config` 的 `Gateway.BrowserDevice.SignalHeaders`、`Gateway.BrowserDevice.ReviewOnSignalChange` 及部署脚本同名参数调整，README 已有说明。

### 4. `/jvs-aps-ui/index.html` 返回 404

- 按现场配置（HostNames 带 `http://` 前缀、多站点、多段 EntryPath）在当前包上未复现 IIS 404；截图中的 404 判定来自旧构建的解析链缺陷：任一站点 HostNames 写了不带端口的裸 IP、或多个站点 authority 冲突时，Host 归属整体失效，请求被"最近访问站点 cookie"或"第一个根站点"兜底拿走，代理到不含 `/jvs-aps-ui` 的上游后透传上游 IIS 404。
- 修复（通用化，不做路径特判）：
  - Host 归属分层择优：带端口的 `HostNames` 条目优先于只写主机名的条目，裸 IP 条目不再抢走其他端口的请求；
  - 新增"按 EntryPath 首段归属"：EntryPath 为多段路径（如 `jvs-aps-ui/index.html`）的站点，其首段目录（`/jvs-aps-ui/*`）的请求自动唯一归属该站点，深链接/刷新与端口无关；
  - "最近访问站点"cookie 记录签发时的 `authority`，跨端口不再粘连；
  - 所有站点都无法归属时，网关输出自绘 404 页并提示检查 `gateway-sites.json`，不再落回 IIS 静态 404 误导排障。
- 复测：`:28053/jvs-aps-ui/index.html` 直开、SPA history 深路由刷新、`:28053/` 根路径、带 scheme 的 HostNames 端口全部 200 且内容正确（T4.1-T4.9）。

## 其他检查与清理（交付合规）

- 测试记录/AI 痕迹：全包扫描（85 个源码/脚本/配置文件）无 AI/Claude/Codex/Copilot/测试报告/复测等字眼，无测试产物目录；清除了三处自动化测试插桩残留（SPA 的 `window.__jvsApsUiApiOk` 与 `data-spa` 标记、`assets/mock.js` 探针文件及页面注入、ApiPing 对 `X-Demo-Header`/`Authorization` 原文的回显——后者改为仅返回是否携带）。
- Markdown 文档：包内仅根目录一个 `README.md`，运维手册口吻统一；更新了多站点 HostNames/EntryPath 归属规则、管理台错误提示行为、默认密码必须修改等说明；示例中的本机代理端口改为占位符。
- 前端文案：逐页核对无解释性副标题、无"(示例)/用于演示"字样；管理台仅存的两条英文提示（令牌过期、登录限流）改为中文，全站口吻一致；渲染器内 4 处 `\u` 转义改回中文字面量，消除补丁痕迹。
- 硬编码：删除部署脚本里开发机专有的 `E:\Visual Studio BuildTools` MSBuild 探测路径；源码内无 IP/机器名/站点键特判（全量核验）；`.dockerignore`、Core 冗余 bin、`*.pdb` 不再进包；mock 站点 `compilation debug` 关闭。
- 安全加固：部署脚本新增 `-AdminPassword` 参数（自动按 `pbkdf2-sha256$210000$盐$哈希` 重算写入 Web.config），README 明确默认密码仅供冒烟、生产必须修改；部署脚本不再把管理端口写进站点 HostNames。
- 移动 APP 兼容：根探测返回"网关已就绪"JSON、`sys.ashx` 匿名引导链路正常（移动端身份/审批逻辑本轮未改动）。

## 复测覆盖清单（Playwright Chromium，最终 zip 全新部署）

22 项全部 PASS：jvs 深链接未授权/已授权/深路由刷新/根路径（4 项）、带 scheme HostNames 端口解析、管理台可达与无乱码（2 项）、同设备两站点两次申请合并与双站点审批双站点可达（5 项）、更新授权站点移除/恢复/拦截空选择/IIS 重启后过期提示/重新登录后可用（6 项）、相同 UA 不同设备号与信号变化触发复审（2 项）、未注册站点 404 的 charset 与无乱码、全程无 console 错误（2 项）。
