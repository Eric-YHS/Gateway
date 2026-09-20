# GatewayDemo.Legacy-net472 公测修复与真实验收报告

验收日期：2026-07-27  
验收对象：`GatewayDemo.Legacy-net472.zip`

## 结论

聊天记录中需要修复或兑现的功能已经完成，最终压缩包已原位更新并通过源码构建、真实 IIS HTTP/HTTPS 部署、受信任证书链、真实 Chromium 操作、WebAuthn、HMAC、IP 白名单、mTLS、WSS、代理差异、故障恢复、数据库升级与并发升级测试。

本次还修复了一个公测风险：原包中的无签名 SDK/AJAX/JSON 请求会先创建浏览器设备和申请相关数据，再返回未授权。现在这类调用在没有 HMAC、没有命中 IP 白名单且没有既有浏览器凭据时，直接返回紧凑的 401 JSON，不再污染现有公测申请、设备或审核数据。

在最终 HTTPS 回归中又发现并修复了两个 Session 风险。1.0.3 已解决网关 Cookie 覆盖上游 `Set-Cookie` 的问题；本轮 1.0.4 又针对上游发出 `SameSite=None` 但缺少 `Secure` 的旧式 `ASP.NET_SessionId`，仅在公网入口确认为 HTTPS 时安全补齐 `Secure`。从最终 ZIP 重新部署后，真实 Chromium 已保存该 Cookie，刷新前后 SessionId 不变，Session 计数从 1 增至 2。

本轮还修复了移动浏览器被误判为“移动APP”的问题。`Sec-CH-UA-Mobile: ?1`、Android/iPhone UA 等现在只说明浏览器运行在移动设备上，不再被当作原生 APP 身份；只有明确的原生桥接/运行时信号或站点配置的 APP UA 标记才进入旧 APP 兼容申请流程。真实 Pixel 7 浏览器请求返回 401，且后台没有新增“移动APP”申请、设备或目标路径记录。

最终文件：

- 路径：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`
- SHA-256：`C0663C0688F517C3EEEA5A10C1F1166D84B12BC7AD3C8FCC385BE995B424876C`
- 大小：16,531,538 字节
- ZIP 条目：202 个，全部可读
- Web 程序文件版本：`1.0.4.0`
- Mock 后端文件版本：`1.0.1.0`

原始交付包的不可变副本保存在：

`E:\验证页面\acceptance-runs\20260727_193015_gateway_cookie_mobile\original\GatewayDemo.Legacy-net472.zip`

## 聊天记录需求判定与结果

| 聊天内容 | 判定 | 实现及真实验证 | 结果 |
| --- | --- | --- | --- |
| 外部调用方使用后台颁发的 APP Key/APP Secret 做 HMAC 鉴权 | 需要兑现 | 管理端创建站点级接口客户端；最终 ZIP 的 HTTPS 入口上有效签名经 mTLS 到达上游并返回 200；nonce 重放返回不泄露细节的 401 JSON；错误签名、撤销后的凭据也均为 401 | 通过 |
| “所有外部接口是否都要一套 ClientId、APP Secret、请求签名” | 研发提问，不应解释为每个接口单独建凭据 | 凭据按调用方颁发并授权站点，不按单个 URL 重复颁发；同一受权调用方可访问该站点授权范围内的接口 | 符合最终澄清 |
| HMAC 与 `ExternalApiClients/AllowedIpRanges` 同时支持，配置仍能放行 | 需要修复/保持 | 两种模式彼此独立；白名单命中可调用，HMAC 调用不依赖白名单；空白名单不放行 | 通过 |
| 共享代理 IP 不能导致所有接口开放 | 需要兑现 | 仅信任配置内代理传来的转发地址；未命中、缺失或畸形转发地址均 401；调用方伪造的上游身份头会被删除并由网关重写 | 通过 |
| `AnonymousAllowedPaths` 默认空 | 需要兑现 | 最终配置为 `[]`；无签名 API 请求不进入匿名路径，也不创建设备或申请 | 通过 |
| 有效 HMAC 不走设备申请与审核；鉴权失败 401；上游 400 原样返回 | 需要兑现 | HMAC 成功直接代理；失败为紧凑 401；上游 400/500 的状态和正文与直连一致，并带可诊断的来源字段 | 通过 |
| 匿名启动请求遇到上游 302 不应被网关错误拦截 | 明确缺陷 | 真实浏览器访问自建 ASP.NET InProc 上游：启动页 302、跳转目标 200、查询参数保留 | 通过 |
| 有时申请人显示“移动APP” | 明确缺陷 | 移动浏览器提示不再作为原生 APP 身份；最终 ZIP 中真实 Pixel 7 AJAX 为 401，未写入申请、设备或“移动APP”记录；带强原生信号和配置身份字段的旧 APP 兼容流程仍能自动建申请 | 通过 |
| 经网关后浏览器没有保存 `ASP.NET_SessionId` | 明确缺陷 | HTTPS 入口对上游 `SameSite=None` 且缺少 `Secure` 的 Cookie 补齐 `Secure`；最终 ZIP 中真实 Chromium 保存并复用 Cookie，两次请求 SessionId 相同且计数为 1、2 | 通过 |
| 设置 `HttpSessionState` 后报错 | 研发反馈，需要定位兼容性 | 同时覆盖 Cookie 合并和现代浏览器 SameSite 规则；最终 ZIP 真实验证 `ASP.NET_SessionId` 的创建、Secure/HttpOnly/SameSite 属性和后续读取；Session 数据仍由上游 InProc 进程持有，不在网关保存 | 通过 |
| 规则要适配不同受保护站点和外部接口，不能写死“小D”等客户场景 | 需要兑现 | 路由、路径规则、HMAC 授权和 IP 白名单均配置驱动；源码和交付内容未发现“小D”“快会”或其域名等客户专名 | 通过 |

## 本次补强

### 接口鉴权与公测数据保护

- 新增无状态接口请求的前置鉴权判定。无 HMAC、无 IP 白名单命中、无既有浏览器设备 Cookie 的 SDK/AJAX/JSON 调用直接 401，不创建设备、申请或审核记录。
- HMAC 缺失、签名错误、时间戳过期、nonce 重放和凭据撤销对调用方只返回通用错误；具体原因只进入服务端日志，避免泄露鉴权细节。
- 既有浏览器设备 Cookie 会继续走浏览器申请/审核流程，避免把页面的 AJAX、CSS、SVG 等资源误判为外部 SDK。
- 修正浏览器文档判定：浏览器默认 `Accept` 中包含 XML 时，仍优先采信 `Sec-Fetch-*`、`text/html`、浏览器 UA 和文档导航证据。

### 移动浏览器、原生 APP 与 Session Cookie

- 普通移动设备 UA、`Sec-CH-UA-Mobile` 和移动浏览器特征不再单独触发旧 APP 兼容申请。
- 原生运行时/桥接头或站点显式配置的 `MobileUserAgentMarkers` 仍可识别原生 APP；配置了机器、用户、企业等身份字段的旧客户端兼容路径继续保留。
- 无 HMAC、无 IP 白名单且没有既有浏览器凭据的普通移动 AJAX 在写数据库前返回 401，避免继续污染公测申请。
- 上游 Cookie 只有在 `SameSite=None`、缺少 `Secure` 且网关已确认公网入口为 HTTPS 时才补齐 `Secure`；不会剥离上游已有安全属性，也不会在真实 HTTP 公网入口伪造 `Secure`。
- 最终 ZIP 的真实 Chromium 回归使用正常证书校验（`ignoreHTTPSErrors=false`），页面异常 0、请求失败 0。

### 报错日志

- 错误记录新增响应来源和上游状态：`gateway`、`upstream` 及 `X-Gateway-Upstream-Status`。
- 除 5xx 外，补充记录需要诊断的 400、408、409、413、422、429；继续避免把常见 401、403、404 全量写入造成日志噪声。
- 管理端错误详情页显示错误来源和上游状态。
- 真实停止上游应用池得到上游 503；停止后端站点得到网关 502；恢复后健康检查和代理请求自动恢复。
- 真实禁止主日志目录写入后，日志成功落入系统临时目录；恢复 ACL 后主日志继续写入。测试生成的临时降级日志已清理。
- 主日志扫描未发现本次 HMAC APP Key 或 APP Secret。报告和截图中也未保留测试凭据。
- 上游 Cookie 不能安全转换为 ASP.NET Cookie 时记录 `ManagedReverseProxy.UpstreamCookieRejected`，只记录头长度而不保存 Cookie 名值；有效业务 Cookie 不增加错误日志。

### 数据库升级稳定性

- 升级前快照不再只比对行数或主键：现在对升级前已存在的全部列、全部值按稳定主键顺序做带类型和长度边界的流式 SHA-256。
- 升级事务提交前确认原列仍存在，并验证完整内容摘要不变；不一致时回滚。
- 仅在列确实为本次新增时执行相应回填，避免重跑升级时改写已有数据。
- 删除会静默修改重复申请/授权状态的“自动归一化”。发现同设备重复待审核申请或重复有效授权时，升级明确失败并回滚，保留原数据和备份，日志给出冲突数量。
- 升级仍先生成一致性备份，使用事务、初始化锁和 SQLite 忙等待，支持 IIS 多工作进程竞争初始化。

真实数据库测试结果：

- v5 → v6 单进程升级：HTTP 200，`integrity_check` 与 `quick_check` 均为 `ok`，升级前后关键数据完整摘要一致。
- 两个 `w3wp` 工作进程、24 个并发健康请求：全部 200；最终仅得到完整 v6 结构，索引存在，内容摘要仍一致。
- 人工构造重复待审核数据后再升级：健康端返回数据库不可用 503；数据库版本仍为 v5，全部原值及摘要不变，备份保留，未发生静默改状态。
- 将已有验收数据库在应用池停止状态下按 SHA-256 一致复制到最终 ZIP 的全新解压目录，再切换 IIS 物理路径并启动：既有申请、批准、WebAuthn 和接口客户端状态仍可用；Schema v6，`integrity_check=ok`、`quick_check=ok`，健康端返回 `database=ok`。
- 本轮再次将含 8 条既有申请的隔离数据库在源应用池停止后按 SHA-256 一致复制给 1.0.4：启动前后 Schema 均为 v6，16 张业务表的行数和逐表内容摘要全部相同，总内容摘要均为 `FDF240D566C49977327DE4973182205BD81728733FFF393214A2C2983A13F025`，没有改写既有申请。
- 两个独立进程同时发出 48 个相同原生 APP 身份请求：全部完成，无 `busy`、`locked` 或异常，只创建一条申请、一台设备和一个身份；再次并发 24 次后行数保持不变。两轮后 `integrity_check=ok`、外键违规 0、15 项重复/孤儿/撤销残留不变量违规均为 0。
- 最终 ZIP 的真实浏览器申请和批准完成后再次只读检查：Schema v6、`integrity_check=ok`、外键违规 0、15 项业务不变量违规 0。

### HTTPS、WebAuthn、mTLS 与 WSS

- 在隔离环境创建短期本机测试 CA，将公开证书加入 `LocalMachine\Root`，为 `gateway-publicbeta.test`、管理子域和上游子域签发含 SAN 的服务器证书；浏览器和 `SslStream` 均使用 Windows 正常信任链，没有 `ignoreHTTPSErrors`、证书回调放行或命令行忽略证书参数。
- 严格 TLS 建连为 TLS 1.3、`TLS_AES_256_GCM_SHA384`、AES-256；HTTPS 健康端返回 200 和 HSTS。
- 真实 Chromium 完成设备创建、申请、管理员批准，并通过 Chrome DevTools WebAuthn 虚拟平台认证器完成真实 WebAuthn 注册与 assertion；页面为安全上下文，RP ID 与 HTTPS origin 匹配。
- `gw_device_credential`、`gw_session`、`gw_webauthn_verified` 均为 HttpOnly/Secure，WebAuthn 验证票据为 SameSite=Strict。
- 正确客户端证书和正确服务端证书指纹时，HTTPS 与 WSS 均到达要求客户端证书的 IIS 上游；上游确认客户端证书存在，`X-Forwarded-Proto=https`、Host 正确。
- 错误服务端证书指纹由网关返回 502，正文不泄露证书或指纹细节；移除客户端证书后上游真实返回 403，并由 `X-Gateway-Error-Source=upstream`、`X-Gateway-Upstream-Status=403` 明确归属。
- 同源 WSS 完成真实文本回显；从同一受信任证书的另一子域页面发起跨 origin WebSocket 时连接被拒绝。
- 测试结束后删除本轮 CA、服务器证书、mTLS 客户端证书及五个私钥容器；没有把证书、私钥、测试域名或凭据写入交付 ZIP。

## 真实使用回归

所有浏览器检查均使用真实 Chromium，不是 HTML 字符串或模拟 DOM 检查。

- 新浏览器首次访问、设备创建、提交公测申请。
- 管理员真实登录、查看申请并批准。
- 用户进入上游登录页并到达业务门户。
- CSS、SVG 等页面资源全部 200；干净浏览器控制台 0 error、0 warning。
- 管理端撤销后，原浏览器立即回到“授权已撤销”页，未继续访问上游。
- 同一设备重新申请后只产生一条待审核记录；重新批准后恢复访问。
- 360 × 732 移动端页面无横向溢出。
- ASP.NET InProc Session：1.0.3 已验证 302 同时发出网关 Cookie 与上游 `ASP.NET_SessionId`；本轮 1.0.4 进一步验证旧上游发出的 `SameSite=None`、无 Secure Session Cookie 经 HTTPS 网关后安全补齐 Secure，真实 Chromium 刷新后沿用相同 SessionId，访问计数从 1 增至 2。
- HMAC：最终 ZIP 的 HTTPS 入口上有效请求经 mTLS 返回 200；nonce 重放为通用 401 JSON且不泄露内部原因；错误签名 401；撤销凭据 401。
- IP 白名单：可信代理转发的允许地址 200；未允许、缺失、畸形地址均 401；伪造的外部客户端身份未透传。
- 配置热重载：写入畸形 JSON 时连续 20 次健康检查均 200，使用 last-known-good；恢复后回到主配置。
- 代理差异：直连与经网关的上游 400/500 状态和正文一致。
- 无鉴权污染回归：最终部署连续 20 次、从最终 ZIP 再部署连续 10 次无签名 JSON 请求均为 401，设备记录数保持不变。
- 从最终 ZIP 重新解压并部署到全新隔离 IIS 站点后，Web 文件版本为 1.0.4.0；HTTPS 健康端为 200，移动浏览器误判回归、真实申请/批准和 Session Cookie 连续性全部通过。
- 管理端路径在业务端口为 404，业务路径在管理端口为 404；TRACE、编码路径穿越、双重编码路径穿越均未开放；不可信 CORS 预检未返回允许源。

## 构建与交付包卫生

- 最终源码独立 Release Rebuild：0 error；仅有本机旧版 .NET Framework MSBuild 将工程 `ToolsVersion=15.0` 按 4.0 处理的兼容性提示，无源码或引用警告。
- ZIP 202 个条目逐项解压读取成功，逐文件 SHA-256 与干净工作包一致。
- 无绝对路径、盘符路径或 `..` 路径穿越条目。
- XML/配置/工程/依赖元数据文件 25 个全部解析通过；JSON 1 个解析通过；PowerShell 脚本 4 个均无语法错误。
- ZIP 内只有一个 Markdown：根目录 `README.md`。
- ZIP 内无测试记录、验收证据、截图、HAR、TRX、coverage、日志、运行数据库、数据库备份或 last-known-good 运行副本。
- 未发现 OpenAI、ChatGPT、Codex、Claude、Gemini、Copilot、“人工智能”或“AI测试”等字样。
- 未发现“小D”“快会”、相关域名或本次验收名称、日期、测试账号。
- `Admin.PasswordHash`、Mock 密码和通知鉴权等交付默认值为空；未发现私钥或已填充的固定生产凭据。
- 保留的固定值仅是通用演示/安全默认值，例如回环地址、演示端口、文档示例 IP、标准 MSBuild/Windows 工具搜索路径；生产站点、路由、接口客户端、白名单、密码和证书均由配置或部署参数注入。
- 根 README 已补充同机/回环上游必须禁用 IIS/ASP.NET 详细错误页的要求，避免在“保留上游错误正文”的代理契约下暴露上游物理路径或模块信息。
- Microsoft Defender 使用签名 `1.455.371.0` 对最终 ZIP 和与其逐文件一致的解压交付目录执行自定义扫描，两个扫描退出码均为 0、未发现威胁。
- 第三方依赖及许可证随包保留，不属于测试产物。

## 边界与环境说明

- 没有使用公网生产域名或公开 CA 证书；本轮使用 Windows 明确信任的短期本机 CA、SAN 测试域名和真实 SNI IIS 绑定验证协议与应用行为。这覆盖证书链、域名、有效期、TLS、浏览器安全上下文、WebAuthn、mTLS 和 WSS，但不等价于验证某个未来公网 CA 的签发流程、外部 DNS 或互联网路由。
- 腾讯电脑管家保持启用；Microsoft Defender 服务、杀毒引擎、归档扫描及实时保护偏好均已开启，签名为 `1.455.371.0`。由于腾讯电脑管家仍注册为主防病毒，Windows 自动将 Defender 置于 `SxS Passive Mode`，实时拦截由腾讯防护承担；未为取得双实时引擎状态而擅自卸载或停用腾讯防护。
- 测试只操作隔离副本和临时 IIS 站点，没有连接或修改线上公测数据库。所有本次创建的 IIS 站点、应用程序池、端口监听、测试 hosts 项、证书、私钥、临时代理例外和浏览器会话均已清理；原代理设置已恢复，Defender 定期扫描保留开启，验收运行目录和截图作为包外证据保留。

## 包外证据

- 本轮移动误判与 Session Cookie 运行目录：`E:\验证页面\acceptance-runs\20260727_193015_gateway_cookie_mobile`
- 原问题复现日志：`E:\验证页面\acceptance-runs\20260727_193015_gateway_cookie_mobile\evidence\baseline\playwright-baseline-reproduction.log`
- 修复工作目录回归日志：`E:\验证页面\acceptance-runs\20260727_193015_gateway_cookie_mobile\evidence\fixed\playwright-fixed-regression.log`
- 最终 ZIP 再部署浏览器日志与截图：`E:\验证页面\acceptance-runs\20260727_193015_gateway_cookie_mobile\evidence\final-artifact`
- 本轮数据库升级、并发和不变量证据：`E:\验证页面\acceptance-runs\20260727_193015_gateway_cookie_mobile\evidence\database`
- 运行目录：`E:\验证页面\acceptance-runs\20260727_151250_gateway_publicbeta`
- 构建日志：`E:\验证页面\acceptance-runs\20260727_151250_gateway_publicbeta\evidence\final-build-verification.log`
- 数据库升级证据：`E:\验证页面\acceptance-runs\20260727_151250_gateway_publicbeta\evidence\db`
- HTTP/日志证据：`E:\验证页面\acceptance-runs\20260727_151250_gateway_publicbeta\evidence\http`、`evidence\logs`
- 浏览器截图：`E:\验证页面\output\playwright\20260727_151250_gateway_publicbeta\final`
- 最终 ZIP 再部署截图：`E:\验证页面\output\playwright\20260727_151250_gateway_publicbeta\postzip\postzip-browser-application.png`
- HTTPS/mTLS/WebAuthn/WSS 运行目录：`E:\验证页面\acceptance-runs\20260727_180900_gateway_https`
- HTTPS 浏览器截图：`E:\验证页面\output\playwright\20260727_180900_gateway_https`
