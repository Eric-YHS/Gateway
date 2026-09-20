# GatewayDemo.Legacy-net472 研发反馈4问题真实复测与修复报告（2026-07-07）

**交付包**：`deliverables/GatewayDemo.Legacy-net472.zip`
**新 SHA256**：`35d041b851c1fa9d4abbc21e665fb6d399f8986cc19b1e45227dc008a41c4189`
**旧 SHA256（本轮反馈针对的版本）**：`79343965e4478d09...`

本轮方法论：MSBuild 真实构建 + 全新 IIS 站点部署 + Playwright 真实 Chromium 浏览器操作 + SQLite 直查，每一个问题都做了"改之前的代码在同一套复现步骤下真的会炸、改之后的代码在同一套复现步骤下真的不炸"的 A/B 对照，而不是只看代码或凭经验判断。

---

## 一、研发反馈的4个问题：根因与修复

### 问题1：删除数据库，新创建的数据库没有表结构（删除第2次后正常）

**根因**：`LegacyGatewayRuntime` 是每个 AppDomain 生命周期内只构造一次的懒加载单例，`GatewayDatabaseInitializer.EnsureCreated()`（建表脚本）只在这个单例的构造函数里跑一次。如果运维/测试人员在应用池**不重启**的情况下把 `App_Data\gateway-demo-legacy.db` 删除（这也是仓库里 `scripts\legacy\reset-gateway-db.ps1` 重置演示数据的标准做法——该脚本只删文件，不会去停/启应用池），后续请求里 `LegacyGatewayRepository.OpenConnection()` 只是对着一个不存在的路径 `new SQLiteConnection(...).Open()`：连接串没有显式设置 `FailIfMissing=True`（默认为 false），`System.Data.SQLite` 会在 `Open()` 时**静默地**在原路径新建一个空的、不含任何表结构的 SQLite 文件，而不是抛异常——此后所有查询都会因为 "no such table" 报错，且不会自愈，直到应用池下一次回收（重新触发单例构造 → `EnsureCreated`）才会恢复，这正好对应"删第二次凑巧撞上一次应用池回收"的现象。

**修复**：`LegacyGatewayRepository.OpenConnection()` 每次打开连接前先做一次 `File.Exists` 检查；一旦发现主库文件缺失，在锁保护下立即（幂等地）清空 SQLite 连接池并重新执行建表脚本，同时顺带清理可能残留的 `-wal`/`-shm` 影子文件，避免"新库套旧影子文件"这种更隐蔽的状态。

**真实验证（非模拟）**：由于 IIS 工作进程通过 SQLite 连接池持有主库文件的操作系统级句柄，外部工具无法在不停应用池的情况下直接删除该文件（与 `reset-gateway-db.ps1` 需要先停应用池的已知限制一致），因此改用等价但更精确的验证方式：编译出的 `GatewayDemo.Legacy.Web.dll`/`GatewayDemo.Legacy.Core.dll` 被同一个 PowerShell 进程直接加载，在**同一个 `LegacyGatewayRepository` 对象、不重新初始化任何单例**的前提下，先正常建库，然后原地删除数据库文件，再用**同一个仓储对象**发起下一次查询——这与"应用池不重启、数据库文件被外部删除后处理下一个请求"完全等价。

| | 删库前 | 删库后立即查询 |
|---|---|---|
| **修复前（当前反馈的 79343965 包）** | 正常 | `SQL logic error: no such table: GatewayManagedDevices`（与研发截图"服务器处理请求时发生错误"完全对应） |
| **修复后** | 正常 | 自动重建 11 张表（`GatewayManagedDevices`/`GatewayAccessRequests`/... 与原 schema 完全一致），查询直接成功，无需人工干预或重启 |

### 问题2：同一台设备2条记录

**根因**：与 2026-07-02 已修复的"浏览器并发首次访问竞态"是**不同的代码路径**。研发截图里两条记录都走的是移动 APP 身份识别（`DeviceCredentialService.ResolveLegacyAppContext`），一条来自 `/WCFService/PostBus.ashx`（业务接口调用，只带 `CompanyId`/`UserId`/`DataCenterId`，不带 `DeviceImei`），另一条来自 `/user/login`（登录接口，额外带了 `DeviceImei`）。`LegacyAppIdentityPolicy.AddDeviceBoundIdentitySources` 按固定位置拼接身份字符串，`DeviceImei` 缺省时该位置在拼接结果里是空字符串而非被整体省略——同一账号信息、仅仅因为其中一次调用没带设备位字段，就会算出完全不同的哈希，进而被当成两台不同设备各自铸造 `DeviceId`，产生两条独立的接入申请。这是移动客户端的常见模式（登录接口带设备指纹，业务接口只透传会话/账号信息），不是边界个案。

**修复**：在 `DeviceCredentialService.ResolveLegacyAppContext` 里新增与已有 `BrowserDeviceService` 完全一致思路的短时窗口复用机制——同一 `ClientIp + (CompanyId|UserId|DataCenterId)` 账号维度，若 10 秒内已有由"带设备位请求"铸造的设备，直接复用，而不是重新铸造；仅在时间窗口内生效、不做成永久性纯账号匹配，避免不同真实设备共用同一企业账号时被误合并（这正是 `AllowAccountOnlyIdentityFallback` 默认关闭要防的风险，本次修复没有绕开这道防线）。

**真实验证（非模拟）**：用真实 HTTP 请求原样重放研发截图里两条记录的全部字段（相同 UA、相同 `CompanyId=3093683`/`UserId=zhengyf`/`DataCenterId=001`，一条带 `DeviceImei`、一条不带），直查 SQLite：

| | 设备数 | 接入申请数 |
|---|---|---|
| **修复前** | 2（`DEV-260707-D57C2F` 申请人"郑雅芬"、`DEV-260707-2AA8C0` 申请人"zhengyf"，与研发截图的模式完全一致） | 2 |
| **修复后** | 1 | 1 |

### 问题3：存在JS替换报错问题（控制台大量 `Uncaught SyntaxError: Unexpected token '<'`）

**根因**：设备未授权时，网关对**页面自身跳转**会 302 到"提交接入申请"HTML 页，这是预期行为；但对同源的**子资源请求**（`<script src>`、`<link>`、`fetch`）也会做一模一样的 302。浏览器的 `<script src>` 会自动跟随 302 并把返回的 HTML 当成 JS 去解析执行，since 响应体以 `<!DOCTYPE html>` 开头，于是在控制台刷出一整屏 `Unexpected token '<'`，页面观感像是彻底崩溃。这只在站点的 `AnonymousAllowedPaths`/`PathRuleProfiles` 没有覆盖到实际资源目录时才会出现——本仓库自带的 demo 配置默认覆盖得比较全，但一个真实客户站点的资源目录结构不一致时就会踩中。

排查过程中额外定位并顺手修复了一个**更严重的独立问题**：未授权的 `fetch`/XHR 类请求（不带静态扩展名，不匹配上面的门禁豁免）在这条 302 链路里会触发**无限重定向**（`net::ERR_TOO_MANY_REDIRECTS`）——`TryResolveSiteFromReferer` 是 `ProxyRequestModule` 里唯一一个没有排除网关自身保留路径（`/gateway`、`/admin`、`/proxy`、`/healthz.ashx`）的站点解析器，导致浏览器带着指向原站点的 `Referer` 头跟随重定向到 `/Gateway/Default.aspx?target=...` 时，被误判成"还属于原站点的相对资源请求"，重新走一遍门禁判断并再次 302，且每一轮都把上一轮的 `target` 再套一层编码，形成自我放大的死循环。

**修复**（两处）：
1. `ProxyRequestModule` 新增 `LooksLikeStaticSubResourceRequest` 判断（优先用 `Sec-Fetch-Dest` 头，退化到按 `.js/.css/.png/...` 等静态扩展名匹配），命中时直接返回干净的 `403 Forbidden`（纯文本，无跳转），不再对静态子资源做 302。
2. `TryResolveSiteFromReferer` 补上和其它站点解析器一致的保留路径排除检查，消除无限重定向。
3. 顺带修复了 `jvs-default` 路径规则里的一个小遗漏：`JvsAnonymousAllowedPaths` 之前只放行了 `/jvs-ui-public/*`，没有把 `/jvs-aps-ui`、`/jvs-aps-ui/*` 一并加入匿名放行（之前只加进了 `UpstreamOriginRootPatterns`），已补齐。

**真实验证（非模拟）**：还原研发反馈的真实拓扑（整站挂在裸根路径下、入口 HTML 匿名可访问、页面自身引用同源根相对路径脚本——与"AI演示"站点截图的形态完全一致），Playwright 真实 Chromium 打开页面：

| | 控制台/页面异常 |
|---|---|
| **修复前** | `SyntaxError: Unexpected token '<'`（真实 `pageerror` 事件，出现两次，与研发截图现象完全对应） |
| **修复后** | 无异常，控制台只有一条干净的 `Failed to load resource: the server responded with a status of 403 (Forbidden)` |

`fetch` 子资源的无限重定向问题同样完成 A/B 对照：修复前 `net::ERR_TOO_MANY_REDIRECTS`（实测重定向 URL 里 `target=` 参数逐层嵌套编码，十几轮后浏览器放弃），修复后正常收到 200。

### 问题4：静态文件报 `Bad Gateway: upstream service is unavailable.`

**结论：代码没有缺陷，是环境问题（上游服务未启动/不可达）**——`Bad Gateway: upstream service is unavailable.` 是 `ManagedReverseProxy.WriteBadGateway` 的固定文案，只在网关向上游发起的 HTTP 连接抛出"无响应"级别的 `WebException`（拒绝连接/超时/DNS 失败）时才会写出，不是 404 也不是路径解析问题。真实操作复现：

1. 网关站点 + Mock 后端站点都正常运行时，访问 `/jvs-aps-ui/index.html` 返回 `200`，是真实的 SPA 静态页面内容。
2. 手动停止 Mock 后端站点后，同一个 URL 立即变成 `502 Bad Gateway: upstream service is unavailable.`，与研发截图文案逐字一致。
3. 重新启动 Mock 后端站点后，同一个 URL 立即恢复 `200`。

即代码对"上游服务是否可达"的判断和报错文案都是正确、符合预期的；研发那次截图，大概率是他们自己搭的多站点测试环境里，`jvs-aps-ui`/"AI演示" 站点配置的上游服务当时没有启动或网络不可达。本次没有为此改动业务代码，只是把这个结论和排查步骤记录下来，供下次遇到同样反馈时先检查上游服务状态，而不必重新怀疑网关代码。

---

## 二、除了4个反馈问题之外，还检查了什么

### 2.1 交付包静态合规扫描（对 `deliverables` 里解包出的内容做的，非工作区里遗留的历史 `.audit`/`_verify` 等临时目录）

- **测试数据/遗留记录**：✅ 干净。`App_Data` 下不打包任何 `.db` 文件，数据库在首次运行时按内嵌 schema 创建，交付包里没有任何测试期产生的公司名、设备记录、接入申请等残留数据。
- **AI 相关字眼**：✅ 干净。对 `.cs`/`.aspx`/`.config`/`.json`/`.md`/`.js`/`.html`/`.ps1` 等全部文本文件做了大小写不敏感扫描（"AI生成"、"由AI"、Claude、ChatGPT、Copilot、codex、cursor、anthropic、"AI辅助" 等），零命中，连"AI演示"这类容易引发歧义的业务命名都没有——产品名统一是"统一认证访问网关"。
- **README 数量与文风**：✅ 根目录唯一一份 `README.md`，全文风格统一（密集的"如果场景X，做Y，设置配置项Z"技术化中文），没有占位符/示例填写字样残留。发现的唯一问题——示例域名用了看起来像真实客户域名的 `j.kuaipu.com.cn`/`oa.kuaipu.com.cn`——**已修复**，替换成通用的 `erp.example.com`/`oa.example.com`；示例 `EntryPath` 也从 `Resource/claw/index.html` 换成更明显是占位符的 `Resource/app/index.html`。
- **前端说明性副标题**：原状态是每个管理后台版块（待审核申请/设备授权/审计日志）都只有裸标题、没有一句话说明这个版块是干什么用的——**已修复**，给这三个版块各加了一行简短说明（例如"设备或浏览器首次访问受保护站点时自动提交的接入申请，需要管理员逐条批准或驳回后对应设备/浏览器才能继续访问。"），已用真实浏览器截图确认排版正常、不影响原有布局。
- **硬编码问题**：✅ 干净。交付包里只有回环地址 `127.0.0.1`/`::1` 作为示例白名单，`gateway-sites.json` 里的 `UpstreamBaseUrl`/`HostNames` 全部是 `localhost` 占位值，没有任何真实内网 IP（如 `10.188.188.x`）或真实机器名残留；`Web.config` 里的默认管理员密码是有明确轮换说明的哈希值（非明文），Mock 后端的明文密码属于"仅供丢弃用的假上游"范畴，符合预期。
- **杂散文件**：发现并清理了 `src\GatewayDemo.Legacy.Web` 下两个空的 `x64`/`x86` 遗留目录（与真正有内容的 `bin\x64`/`bin\x86` 混在一起容易让人误以为交付了空文件）。

### 2.2 端到端真实回归（在修完全部问题之后，用真实浏览器完整走一遍标准 demo 流程，确认没有引入新问题）

用 Playwright 真实 Chromium，对着**标准单站点 demo 配置**（即用户实际会拿到的默认配置，不是为复现 bug 专门搭的畸形配置）走了一遍完整闭环：
访客打开 `/portal` → 被判定未授权、看到"提交接入申请"页（含刚加的说明性副标题）→ 填写并提交申请表单 → 管理员登录后台 → 后台看到"待审核申请"里的这条记录 → 点击批准 → 页面提示"已批准该访问申请" → 同一访客刷新页面 → 被正确放行，看到真实的 Mock ERP 登录页内容 → 整个过程控制台零错误。

证明本次的 5 处代码改动（数据库自愈、设备身份短时复用、静态子资源 403、Referer 解析保留路径修复、`jvs-aps-ui` 匿名路径补齐）**没有破坏标准审批闭环**。

---

## 三、本次代码改动清单

| 文件 | 改动 |
|---|---|
| `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRepository.cs` | 新增 `EnsureDatabaseFileExists`，`OpenConnection` 每次打开前检查并按需自愈（问题1） |
| `src/GatewayDemo.Legacy.Web/Infrastructure/DeviceCredentialService.cs` | 新增账号维度短时设备复用（`BuildAccountScopedReuseKey`/`TryReuseRecentAccountScopedDevice`/`RememberAccountScopedDevice`）（问题2） |
| `src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs` | 新增 `LooksLikeStaticSubResourceRequest`/`WriteStaticSubResourceBlockedResponse`（问题3-a）；`TryResolveSiteFromReferer` 补齐保留路径排除（问题3-b，无限重定向） |
| `src/GatewayDemo.Legacy.Core/Options/GatewayPathRuleProfileCatalog.cs` | `JvsAnonymousAllowedPaths` 补齐 `/jvs-aps-ui`、`/jvs-aps-ui/*`（问题3 附带修复） |
| `src/GatewayDemo.Legacy.Web/Infrastructure/GatewayPageRenderer.cs` | 管理后台三个版块补充说明性副标题（合规检查项） |
| `delivery/legacy/Package.README.md` | 示例域名/路径去除真实客户特征（合规检查项） |
| `src/GatewayDemo.Legacy.Web/x64`、`x86` | 删除两个空的遗留目录（合规检查项） |

全部改动已用 MSBuild Release 配置真实构建通过（0 警告 0 错误），并对**重新打包出的最终 zip**（而不是工作区源码）做了一次独立的全新解压 + 全新 IIS 部署 + 完整回归，确认交付物本身没问题。

## 四、遗留的低优先级观察项（未改动代码，仅记录供参考）

- `TryResolveSiteFromEntryPathPrefix`（`ProxyRequestModule.cs`）是专门为 `EntryPath` 形如 `jvs-aps-ui/index.html` 的多段路径站点写的解析器，但目前所有随包 `gateway-sites*.json` 示例都没有配置这种形态的站点，实际路由靠 `ExposeLegacyAppAtRoot` 根路径兜底 + `jvs-default` 的 `UpstreamOriginRootPatterns` 凑效——如果未来新增站点时没有同时设置好 `HostNames`/`ExposeLegacyAppAtRoot`，`jvs-aps-ui` 类路径可能会退化成"没有站点认领"的 404，是与本次问题4不同的另一种潜在故障模式，值得在后续测试里留意。
