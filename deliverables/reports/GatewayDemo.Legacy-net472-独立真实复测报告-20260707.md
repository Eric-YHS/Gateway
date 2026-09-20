# GatewayDemo.Legacy-net472 独立真实复测报告（针对研发反馈4问题的修复结果）

**复测对象**：`deliverables/GatewayDemo.Legacy-net472.zip`
**SHA256**：`35d041b851c1fa9d4abbc21e665fb6d399f8986cc19b1e45227dc008a41c4189`
**复测性质**：本报告独立于修复者自己写的报告（`GatewayDemo.Legacy-net472-研发反馈4问题真实复测与修复报告-20260707.md`），是对同一个交付包做的第二轮、不预设结论的真实环境复测——全新解包、全新 MSBuild 构建、全新 IIS 多站点部署、真实 Playwright Chromium 浏览器操作、SQLite 直查，没有直接采信修复者自己的验证结论。

**结论先行**：研发反馈的 4 个问题在当前交付包里**全部确认已修复/已排除**，回归测试无新问题；额外发现 **1 项低优先级、非阻断的代码质量观察项**（见第四节），交付包合规扫描（测试残留/AI 字眼/README/硬编码/前端说明文案）**全部通过**。

---

## 一、研发反馈4问题逐项复测

### 问题1：删除数据库，新库无表结构（删第2次才正常）—— ✅ 确认已修复

**真实操作**：先证明了"外部工具在 IIS 应用池运行期间无法直接删除数据库文件"这个环境限制是真实存在的——对着真实部署的 IIS 站点，无论用 `rm -f` 还是仓库自带的 `scripts\legacy\reset-gateway-db.ps1`，都会因为 `w3wp.exe` 通过 SQLite 连接池（`Pooling=True`）持有文件的操作系统级句柄而报 `IOException`（"另一个进程正在使用此文件"）。

因此改用等价但更精确的复测方式：用 PowerShell `Add-Type` 直接加载交付包里编译好的 `GatewayDemo.Legacy.Core.dll`/`GatewayDemo.Legacy.Web.dll`（与 IIS 里运行的是同一份二进制），在**同一个进程、同一个 `LegacyGatewayRepository` 对象、不重新初始化任何单例**的前提下，先正常查询一次，删除数据库主文件，立刻用**同一个仓储对象**再查询：

| | 结果 |
|---|---|
| 删库前查询 | 正常 |
| 删库后立即查询（同一对象、无重建） | **自愈成功**，无异常，自动重建全部 11 张表（`GatewayManagedDevices`/`GatewayAccessRequests`/... 与原 schema 一致） |
| 再次查询确认稳定性 | 正常 |

代码核查：`LegacyGatewayRepository.OpenConnection()` 每次打开连接前调用 `EnsureDatabaseFileExists()`，文件缺失时在锁保护下清连接池、清理残留 `-wal`/`-shm`、重新建表，逻辑与实测行为一致。

**次要发现**：源码注释里提到"这是已被记录的标准操作 `scripts\legacy\reset-gateway-db.ps1`"，但该脚本**实际并未打包进交付 zip**（只存在于内部工作区 `scripts\legacy\` 目录），仅是文档性引用，不影响功能，建议注释措辞后续可以更准确，不算缺陷。

### 问题2：同一台设备2条记录 —— ✅ 确认已修复

**真实操作**：原样重放了研发截图里两条记录的请求形态——一条打 `/WCFService/PostBus.ashx/DoAction/1301`（业务接口，不带 DeviceImei），一条打 `/user/login`（登录接口，带 DeviceImei），用相同的 `CompanyId`/`UserId`/`DataCenterId`、真实 HTTP 请求、真实网关处理，直查 SQLite：

| | 设备数 | 接入申请数 |
|---|---|---|
| 两条请求（业务接口 + 登录接口，10 秒内） | **1**（同一 `DeviceId`） | **1** |

补充验证了修复引入的"账号维度短时复用"没有引入误合并风险：等窗口期（10秒）过后，用**不同的 DeviceImei**（模拟真实的另一台设备）、相同账号信息再发一次请求，正确生成了**第二个独立设备**，未被错误合并——证明修复只在窗口内生效，没有退化成"永久按账号匹配"。

### 问题3：JS替换报错（`Uncaught SyntaxError: Unexpected token '<'`）—— ✅ 确认已修复，含"顺带发现"的无限重定向问题

**真实操作**：本仓库自带的 demo 站点配置默认用了 `PathRuleProfiles=["legacy-web-default","jvs-default"]`，其中 `*.js`/`*.css` 通配符本身就是匿名放行的，不会复现这个 bug（这也是为什么必须专门搭建畸形拓扑才能验证）。为了忠实复现"客户站点资源目录未被覆盖"的真实场景，额外搭建了一个**不使用任何 `PathRuleProfiles`** 的站点，入口页面 `index.html` 匿名可访问、页面内 `<script src="app.js">` 与 `<link href="style.css">` 走同源相对路径但不在匿名名单内，用真实 Playwright Chromium（同源测试页，规避了 Opaque Response Blocking 的干扰）打开该页面：

| | 结果 |
|---|---|
| `page.on('pageerror')` 事件数 | **0**（无任何 JS 语法错误） |
| 控制台错误 | 仅 2 条干净的 `Failed to load resource: ... 403 (Forbidden)` |
| `app.js` 是否被执行 | 否（被正确拦截，未把 HTML 当 JS 执行） |

**顺带验证的无限重定向问题**：在同一站点上，用页面内 `fetch()` 请求一个未匿名放行的路径并带上原站点 `Referer`：结果 `redirected:true`，最终落在 `/Gateway/Default.aspx?target=...`，**只重定向了一次**，正常返回 200，没有出现 `ERR_TOO_MANY_REDIRECTS`。

代码核查：`ProxyRequestModule.LooksLikeStaticSubResourceRequest`（优先用 `Sec-Fetch-Dest` 头判断）和 `TryResolveSiteFromReferer` 的 `IsGatewayReservedPath` 保留路径排除检查均在，且写法与其余 4 个站点解析器（`TryResolveSiteFromHost`/`TryResolveSiteFromRequestPattern`/`TryResolveSiteFromEntryPathPrefix`/`TryResolveSiteFromCookie`）保持一致，未见遗漏。

### 问题4：静态文件报 `Bad Gateway: upstream service is unavailable.` —— ✅ 确认为环境问题，非代码缺陷

**真实操作**：真实启停 Mock 后端 IIS 站点，观察同一 URL 的状态变化：

| 上游状态 | 请求 `jvs-aps-ui/index.html` 结果 |
|---|---|
| 运行中 | `200`，返回真实 SPA 内容 |
| 手动停止上游站点 | `502 Bad Gateway: upstream service is unavailable.`（与研发截图文案逐字一致） |
| 重新启动上游站点 | 立即恢复 `200` |

网关对"上游不可达"的判断和报错文案符合预期，不是路径解析或 404 问题；研发当时的现场应该是对应上游服务没启动或不可达，不是网关代码缺陷。

---

## 二、端到端真实回归（确认5处代码改动没有破坏标准流程）

用真实 Playwright Chromium 走了一遍完整闭环：访客访问受保护站点 → 未授权，跳转到"提交接入申请"页 → 检测到表单字段（企业名称/申请人/电话/理由/站点勾选）→ 真实填写并提交 → 管理员用 `gateway-admin` 账号真实登录后台 → 在"待审核申请"列表里找到刚提交的记录（用随机 tag 精确匹配）→ 点击"批准" → 页面提示"已批准该访问申请" → 访客浏览器（同一 cookie）刷新访问 → 正确放行，看到真实的上游 ERP 登录页内容。

**全程访客侧浏览器控制台 0 条错误**（`pageerror` 和 `console.error` 均为空数组）。

---

## 三、交付包合规扫描（对全新、未被任何测试脚本触碰过的解包副本做的）

| 检查项 | 结果 |
|---|---|
| AI/工具相关字眼（Claude/ChatGPT/Copilot/Codex/Cursor/Anthropic/"AI生成"/"由AI"等） | ✅ 全文件类型扫描，**0 命中**（含大小写不敏感） |
| 测试记录/数据库残留 | ✅ zip 内 `App_Data` 目录**只有** `gateway-sites.json`，不含任何 `.db`/`.db-wal`/`.db-shm` 文件 |
| README 数量与文风 | ✅ 全包**仅有根目录一个** `README.md`；通读全文（88行），技术化中文风格从头到尾一致，无占位符残留 |
| 前端解释性副标题 | ✅ 管理后台"待审核申请"/"设备授权"/"审计日志"三个版块均有一行说明文字，语气统一；已用真实浏览器截图路径确认渲染正常（见第二节回归测试） |
| 硬编码问题 | ✅ 未发现真实内网 IP（如 `10.188.188.x`）、真实客户域名或机器名；`gateway-sites.json` 全部是 `localhost` 占位值；管理员密码是 PBKDF2 哈希（非明文） |
| 杂散文件 | ✅ 包根目录只有 `GatewayDemo.Legacy.sln`/`README.md`/`packages/`/`scripts/`/`src/`，无 `.git`/`.vscode`/`.pdb`/`Dockerfile` 等内部工具痕迹 |

---

## 四、额外发现的低优先级观察项（未改动代码，仅记录）

**静态账号/设备维度短时复用缓存无过期清理，长期运行存在缓量内存增长**：本次修复问题2新增的 `DeviceCredentialService.RecentAccountScopedDevices`/`RecentAccountScopedLocks`（10秒复用窗口），以及此前 07-02 已修复问题引入的 `BrowserDeviceService.RecentFirstContactTokens`/`FirstContactLocks`（2秒复用窗口），都是**静态 `ConcurrentDictionary`，只增不删**——每出现一个新的 `ClientIp+账号` 或 `ClientIp+信号哈希` 组合就永久占一条内存记录，直到应用池回收才释放。

对比同一份代码里 `LegacyGatewayRepository.ShouldWriteHeartbeat` 用的 `RecentHeartbeatWrites` 字典，那里已经有现成的"每1000次写入做一次超期清理"模式（`heartbeatCleanupCounter` + 按 `HeartbeatEntryMaxAge` 过滤），新增的两处复用缓存没有复用这个已有模式。

**影响评估**：单条记录很小，且 IIS 应用池通常会周期性回收（默认约29小时或按配置），实际生产环境风险较低；但如果长期不回收、且访问方（设备/账号组合）持续增长，理论上会造成缓慢的内存泄漏。建议后续给这两处也补上类似 `RecentHeartbeatWrites` 的周期性清理，不建议作为本轮阻断项。

---

## 五、本次复测环境记录

- 全新解包目录：`_verify_20260707/deploy_extract`（构建部署用）与 `_verify_20260707/scan_extract2`（合规扫描用，两者严格分离，未混用）
- 构建：`MSBuild.exe GatewayDemo.Legacy.sln /t:Build /p:Configuration=Release "/p:Platform=Any CPU"`，0 错误 0 警告
- IIS 部署：新建站点 `GatewayLegacyVerify0707`（端口 38050/38051/38052/38053/38058/38059）+ `MockBackendLegacyVerify0707`（端口 38055），三站点配置（默认业务站点 `2027` / 裸根路径 `jvs-aps-ui` 演示站点 `aidemo` / 无 PathRuleProfiles 的问题3复现站点 `rawtest`），均为全新命名，未触碰机器上已有的其余历史验证站点
- 未对交付包本身做任何修改，所有操作都在独立解包副本上进行

**Why 记录进memory**：详见 `[[gateway-legacy-known-bugs-20260707]]` 补充条目。
