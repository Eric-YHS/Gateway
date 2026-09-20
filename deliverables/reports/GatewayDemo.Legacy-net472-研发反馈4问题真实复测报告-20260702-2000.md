# GatewayDemo.Legacy-net472 研发反馈4问题真实复测报告

日期：2026-07-02
对象：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`（SHA256 前缀 789DCAC5，12,679,721 bytes，2026-07-02 17:46 打包）

## 检查方式（真实操作，非模拟）

1. 全新解包 zip 到 `E:\验证页面\_fixwork_20260702b\zip_extract`，逐文件哈希与工作区 `src` 比对（0 处内容差异），确认取证对象与最新交付一致。
2. MSBuild 17.14 Release 重新构建：0 warning / 0 error。
3. 用官方 `setup-demo-sites.ps1` 部署 IIS，`gateway-sites.json` 手工改写为复刻研发截图的 4 站点拓扑（2388/2027/17011/9999，其中 9999 的 `EntryPath=/jvs-aps-ui/index.html`、`ExposeLegacyAppAtRoot=true`、`HostNames` 带 `http://` 前缀，与研发截图逐字段一致）。
4. **Playwright 真实 Chromium 浏览器**驱动，脚本 `E:\验证页面\_fixwork_20260702b\repro.js`，全程用 SQLite 直接查库核对后端真实状态（而非只看页面文案），并对问题4做了应用程序池 Classic⇄Integrated 管线模式的真实切换复现实验。
5. IIS 站点保留：`AuditGw20260702`(29070主/29071管理/29072=2388/29073=2027/29076=9999)、`AuditBk20260702`(29075)，可直接打开核对。

## 一、研发反馈的4个问题 —— 逐项结论

### 问题1：同一设备两站点分两次同时申请，实际只勾了一个站点 —— **确认复现，真实 bug**

**根因**：`Infrastructure/BrowserDeviceService.cs` 的 `GetOrCreateContext`（23-93行）在"检查设备 Cookie → 无则创建新设备 → 下发 Set-Cookie"这个过程中**没有任何跨请求锁**，而 Set-Cookie 必须经过一次网络往返才能被浏览器带到下一个请求。当同一浏览器（同一个 Cookie Jar）**几乎同时**打开两个不同站点的申请页面并提交（比如两个 Tab 同时操作），两个请求可能都在"无 Cookie"状态下各自被 `CreateBrowserManagedDevice`（203-272行）无条件铸造一个新的 `DeviceId`（`Guid.NewGuid()`，不做去重检查）。

真实复测实锤：
- 用同一个 Chromium 浏览器 Context（同一 Cookie Jar）并发打开 2388、2027 两个站点的申请表单并几乎同时提交，直接查 SQLite：**产生了 2 个不同的 `GatewayManagedDevices` 记录**（本应是同一台设备）。
- 后果链路已完整走通：管理台按 `DeviceId` 分卡片展示"待审批"，管理员批准了其中一张卡片对应的设备后，**另一个 DeviceId 因为浏览器 Cookie 已被覆盖、再也没有会话能关联上它**，其申请永久卡在待审批队列里、不会再被任何人看到——这正是"两个站点申请了，最终只有一个生效"的真实成因。
- 该竞态的最终外在表现有一定概率性（有时两次提交因为落在同一次锁窗口内被正确合并成一张卡片，有时则被拆成两个 `DeviceId`），这与研发描述的"偶发"现象吻合。

**建议修复方向**：在"读取设备 Cookie → 创建新设备"这一步加进程级锁（比照 `LegacyGatewayRepository.DatabaseWriteSyncRoot` 的做法），或在 `CreateBrowserManagedDevice` 前先按 `SignalHash+ClientIp+极短时间窗口` 做一次去重查询。

### 问题2："更新授权站点"报错（需要重启IIS站点）—— **本次未能复现，建议按此结论处理**

已用真实浏览器构造多种边界场景对 `update-authorization-sites` 操作施压，全部返回正常中文提示、应用池全程保持 `Started`、无需重启：
- 不勾选任何站点也不勾"所有站点"→ 正常返回"请至少选择一个授权站点"提示（非崩溃）。
- 伪造一个不存在的 `authorizationId` → 正常返回"未找到对应的授权记录"提示。
- 使用过期/伪造的 CSRF token → 正常返回"管理页面已过期，本次操作未执行"提示。
- 同一账号 10 个并发更新请求压测 → 全部正常返回，应用池未崩溃，压测后管理台无需重启即可继续访问。

代码走查（`Admin/Default.aspx.cs` 129-257行）确认 `update-authorization-sites` 分支被完整的 `try/catch(InvalidOperationException)/catch(Exception)` 包裹，任何异常都会走 `RenderDashboard` 输出友好中文提示，不会让异常裸奔到客户端。

研发提供的截图文件夹时间戳显示这批截图来自 **2026年6月**，而交付包在 6-7 月间经历过多轮"隐藏 IIS 原生错误页/统一异常提示"相关修复（含当天 18:22 刚完成的 M2/M3：httpErrors 品牌化错误页 + `Application_Error` 不再回显原始异常）。截图里的乱码文字特征（UTF-8 中文被按 GBK 误解析、末尾出现不完整字节）说明当时的响应**根本没有走到会显式设置 `charset=utf-8` 的 Page_Load**，更像是某种"框架/IIS 层面裸露的原生错误页"，而不是当前代码里任何一条已读到的业务分支能产生的结果——这类症状正是本次交付里 M2/M3 两项修复要解决的目标。**结论：大概率是历史版本问题、已被后续修复覆盖**；若研发在当前包上仍能复现，请提供更精确的操作序列（具体点击了哪个按钮、浏览器类型、是否伴随其他并发操作）以便针对性排查。

### 问题3：浏览器设备授权默认只把 User-Agent 作为设备信号 —— **设计本身没有问题，但顺带发现一个新问题**

代码走查 + 真实浏览器验证澄清了这一点：**设备身份（DeviceId）来自一个 32 字节随机数生成、SHA256 哈希存储的 Cookie token，与 User-Agent 无关**；`Web.config` 里配置的 `Gateway.BrowserDevice.SignalHeaders`（默认 `User-Agent,Sec-CH-UA-Platform,Sec-CH-UA-Mobile`）只是用来判断"这台已登记设备的环境是否发生了变化、要不要触发一次复审"，从未参与身份识别或去重判断。真实复测证明：
- 两个完全独立的浏览器 Context（模拟两台不同物理设备）即使 User-Agent 字符串完全相同，也拿到了两个不同的设备 token，不会被误判为同一设备。
- 同一个设备 Cookie 换一个完全不同的 User-Agent 后，`DeviceId` 保持不变（只是被标记"需要复审"）。

**研发担心的"UA 重复率≈99% 导致设备号冲突"在当前实现下不成立**，之前 Web.config 里给出的调参说明（`SignalHeaders`/`ReviewOnSignalChange`）依然准确有效，无需改动。

**新发现（不在研发原4条反馈里，属本次额外真实测试挖出的问题）**：`ProxyRequestModule.HandleRequest` 顶部有一段 `LegacyAppCompatibilityPolicy.IsMobileAppRootProbe` 短路逻辑（255-261行）——只要 User-Agent 命中 `MobileClientDetector` 里 "android/iphone/ipad/mobile/..." 等一长串关键字、且请求路径恰好是站点根路径 `/`，就会跳过所有站点路由和授权判断，直接返回一段裸 JSON `{"Msg":"网关已就绪",...}`。这组关键字覆盖的不只是"老旧原生 App/内嵌 WebView"，**也覆盖了所有真实的手机浏览器（iPhone Safari、Android Chrome 等）**。真实复测验证：用 iPhone Safari 的 User-Agent 访问站点根域名（`http://127.0.0.1:29073/`），得到的是一段没有任何跳转链接、没有设置设备 Cookie 的裸 JSON，用户在手机浏览器里点开链接后**卡死在这段 JSON 上，无法继续申请访问或看到任何页面**；同一 UA 访问 `/portal` 等非根路径则完全正常。也就是说，**只要有真实用户直接用手机浏览器打开这些网关站点的根网址（而不是带路径的深链接），就会遇到这个问题**，与研发对"移动端设备"的关注点相关但机制不同，建议一并排查修复（比如把这条根路径探测逻辑收窄到"确实是原生 App/WebView 会带的专属头"，而不是泛化到所有含 mobile 关键字的 UA）。

### 问题4：会报 404（`jvs-aps-ui/index.html`）—— **确认复现，根因已定位到 IIS 部署配置，非应用代码缺陷**

**根因**：`Web.config` 里 `ProxyRequestModule` 的注册依赖 `runAllManagedModulesForAllRequests="true"`，这个开关**只有在 IIS 应用程序池的"托管管线模式"是 Integrated 时才生效**。官方部署脚本 `scripts/legacy/setup-demo-sites.ps1` 建站时明确指定了 `/managedPipelineMode:Integrated`（246行），但如果目标服务器是手工建站、或复用了一个历史遗留的 Classic 模式应用程序池，`.html` 这类扩展名的请求会被 IIS 原生 `StaticFileModule` 直接处理、根本不会进入网关的托管代码，物理目录里当然没有 `jvs-aps-ui\index.html` 这个文件（它应该由网关代理到上游取回），于是 IIS 报它自己原生的 404。

真实复测实锤（同一套代码、同一份 `gateway-sites.json`，只切换 IIS 应用程序池的托管管线模式）：
- **Integrated 模式**（官方脚本默认）：访问 `http://<host>/jvs-aps-ui/index.html` → HTTP 200，正确返回网关代理的 SPA 页面。
- **手动切到 Classic 模式**并回收应用程序池后：**同一个 URL 立即变成 HTTP 404**，响应文案与研发截图里的"找不到文件或目录"完全对应。
- 切回 Integrated 模式后立即恢复 200 正常。

这证明了两点：(1) `ProxyRequestModule` 自身对 `/jvs-aps-ui/index.html` 这类多级路径 `EntryPath` 的匹配逻辑是正确的、没有缺陷；(2) 现象 100% 由 IIS 应用程序池的托管管线模式决定。**建议**：交付文档里明确要求"必须使用官方 `setup-demo-sites.ps1` 部署，或手工建站时务必确认应用程序池托管管线模式为 Integrated"；更稳妥的代码侧加固是给 `ProxyRequestModule` 的 `<modules>` 注册项额外附加校验/在启动时探测当前管线模式并记录警告日志，避免完全依赖运维记忆。

## 二、合规扫描结论（对交付 zip 本身，非本地测试目录）

- **AI/测试字眼**：交付 zip 原始内容中未发现 Claude/Codex/GPT/Copilot/AI生成 等字样及探针、TODO/FIXME、遗留测试代码。
- **文档规范**：全包仅 1 个 Markdown 文件（根目录 `README.md`），面向部署人员，文风统一专业，未见开发过程记录用语。
- **前端文案**：管理台/网关页面提示语均为合理产品级文案，未发现"仅供测试"类说明性文字。
- **硬编码**：未发现开发机绝对路径、非法公网 IP；默认账号密码在 README 中已声明"仅供冒烟、需重置"，属合理范围；域名仅作文档示例出现，代码里均从配置读取。
- **构建产物**：`<compilation debug="false">` 正确；zip 内无 `.pdb`/`bin`/`obj`/`.git` 等残留。
- 本次为做真实浏览器验证在本地生成的 `bin/`、`obj/`、临时 `gateway-demo-legacy.db` 等文件**只存在于本地测试目录，不在交付 zip 内**，无需处理。

## 三、结论汇总

| 问题 | 结论 |
|---|---|
| 1. 两站点并发申请只勾一个 | **已修复**（见下方"四、问题1修复记录"），根因是设备身份铸造阶段的竞态（无锁） |
| 2. 更新授权站点报错+乱码 | **本次未复现**，多种边界/并发场景均正常，怀疑是历史版本问题、已被今日 M2/M3 修复覆盖 |
| 3. 设备信号仅用UA | **设计无问题**（身份靠随机Cookie而非UA）；顺带发现的新bug"移动UA访问站点根路径卡在裸JSON页面"**已修复**（见下方"五、问题3新发现修复记录"） |
| 4. jvs-aps-ui/index.html 404 | **真实存在**，但根因是IIS应用程序池托管管线模式配置错误（Classic而非Integrated），非应用代码缺陷 |
| 合规（测试字眼/README/硬编码等） | 交付 zip 本身干净，无问题 |

## 四、问题1修复记录（2026-07-02 19:42）

**修复文件**：`src/GatewayDemo.Legacy.Web/Infrastructure/BrowserDeviceService.cs`

**修复方式**：在"检查设备 Cookie → 无则创建新设备"这一步，加入按 `ClientIp + SignalHash`（客户端IP + 浏览器环境指纹）为键、2 秒短时效窗口的"首次接触 Token 复用"机制（`CreateOrReuseFirstContactDevice`）：
- 用 `ConcurrentDictionary` 记录"刚为某个 IP+指纹组合铸造的设备 Token"，配合按同一键取的 `lock` 对象保证同键请求互斥。
- 同一浏览器（同 IP、同环境指纹）在 2 秒内的第二次"无 Cookie"请求，会直接复用第一次请求刚创建的设备 Token/DeviceId，而不是再铸造一个新设备——从根上避免了"两个站点分别产生独立 DeviceId"的拆分。
- 权衡说明：2 秒窗口是"覆盖真实并发（同浏览器开两个Tab）"与"避免误合并两台不同真实设备（如同一NAT出口+相同浏览器镜像版本的两台电脑凑巧同时首次访问）"之间的折中，已在代码注释中说明。

**真实复测结果**（全新解包重新构建 → IIS 部署 → 清空数据库 → Playwright 真实浏览器）：
- 同一浏览器并发提交两个站点的申请表单：修复前产生 2 个独立 DeviceId（1~2条独立Pending申请，视竞态时序而定）；**修复后稳定产生 1 个 DeviceId、1 条合并了两个站点的 Pending 申请**。
- 管理员批准该唯一一张卡片后，两个站点均正确进入"已授权"状态，不再有站点"丢失"。
- 补充验证修复的精确性：两个**真正独立**的浏览器 Context（间隔 > 2 秒访问，模拟两台不同物理设备）即使 User-Agent 完全相同，仍然拿到两个不同的 DeviceId——证明修复没有引入"不同设备被误合并"的新问题。
- 回归测试：问题2/3/4 的既有场景、以及常规单设备单站点申请流程全部重新跑过，均无异常，应用池全程未崩溃。

**交付**：已用官方 `scripts\legacy\package-legacy.ps1 -Configuration Release` 从工作区 `src` 重新打包（MSBuild 0 warning/0 error）。
- 新交付包：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`
- SHA256：`377CFEC3C1BDBACCED1CD98824AE2EE8B10EC9C11459024B4571A40327ADE88F`
- 大小：`12,681,411` bytes（2026-07-02 19:42）
- 覆盖前备份：`GatewayDemo.Legacy-net472.before-issue1-fix-20260702-194236.zip`

## 五、问题3新发现修复记录（2026-07-02 20:43）

**修复文件**：`src/GatewayDemo.Legacy.Web/Infrastructure/MobileClientDetector.cs`、`src/GatewayDemo.Legacy.Web/Infrastructure/LegacyAppCompatibilityPolicy.cs`

**修复方式**：`ProxyRequestModule.HandleRequest` 顶部的"移动原生App健康探测"短路逻辑（`IsMobileAppRootProbe`）原先复用了泛化的移动客户端判定（`MobileClientDetector.IsMobileClientRequest`），其关键字列表里 `android/iphone/ipad/mobile/微信/企业微信/钉钉/飞书/UC浏览器/QQ浏览器` 等标识**真实的人也会用来点开链接浏览**，不只是原生App。新增一个专用的窄口径判定 `MobileClientDetector.IsNativeAppHealthProbeRequest`：
- 只保留"根本不是浏览器"的标识：`okhttp`/`dalvik`（Android原生HTTP客户端库，本身不会渲染网页）、`html5plus`/`uni-app`/`dcloud`/`plusruntime`（特定的原生App混合开发框架运行时标识）。
- 以及应用主动自报家门的信号（`X-Requested-With` 以 `com.` 开头的包名、`X-Client-Platform`/`X-App-Platform`/`X-Device-Platform` 这类浏览器不会发送的自定义头）。
- 明确排除了 `Sec-CH-UA-Mobile` 客户端提示（真实手机浏览器也会带）以及泛化UA关键字匹配。
- `IsMobileAppRootProbe` 改为调用这个新的窄口径判定；原来只有它一处引用的 `HasMobileAppUserAgent` 已同步移除（避免留下死代码）。其余用于"是否触发移动端自动申请/续期"等*非根路径*场景的判定（`LooksLikeMobileAppRequest`、`IsMobileClientRequest`）保持不变，不受影响。

**真实复测结果**（重新构建 → 清空数据库 → Playwright 真实浏览器，逐一模拟9种UA）：
- 真人会用的浏览器/内嵌浏览器（iPhone Safari、Android Chrome、微信内置浏览器、企业微信内置浏览器、钉钉内置浏览器、飞书内置浏览器）访问站点根路径：**修复前**返回裸JSON、无法继续操作；**修复后**全部正确返回HTML"访问申请"页面，可以正常提交申请。
- 真正的原生App/HTTP客户端（`okhttp`、`Dalvik`、`uni-app`壳）访问站点根路径：修复前后**均正确返回JSON健康探测响应**，原生App兼容性未受影响。
- 回归测试：同一手机UA访问 `/portal`（非根路径）等既有场景，以及问题1/2/4的全部既有场景，全部重新跑过，30项断言全部通过，无回归。

**交付**：已用官方 `scripts\legacy\package-legacy.ps1 -Configuration Release` 重新打包。
- 新交付包：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`
- SHA256：`79343965E4478D09C7CF149E5B7B3D550C128CF19502D9CD3BC51AD536416898`
- 大小：`12,681,883` bytes（2026-07-02 20:43）
- 覆盖前备份：`GatewayDemo.Legacy-net472.before-mobile-root-fix-20260702-204322.zip`

