# GatewayDemo.Legacy 数据库锁、数据库调用硬预算与请求体中止修复落地报告（fixed4）

> 依据 `E:\验证页面\temptplan.md`（fixed4）实施。输入候选 fixed3（ZIP + SHA-256 与计划一致），
> 只修复计划列出的四项问题（读路径裸 500 / 写预算穿透 / ZIP 绝对路径 / 请求体中止 0x800703E3），
> 并完整回归 fixed1/fixed2/fixed3 已修复的授权、移动 APP、审计队列与生命周期行为。
> 未重写仓储层、未切换数据库、未替换整个反向代理、未改变业务状态机。

## 1. 候选包与哈希

| 包 | SHA-256 |
|---|---|
| 新候选 `GatewayDemo.Legacy-net472-db-lock-fixed4-20260814.zip` | `034699EF820857782A60846E96592111960D934846B8B997643EC6AE34E589C0` |
| 输入 fixed3 | `F43096A3203DA31815D895EB891E4C289B89BD2239AC197DD5A4916FAB191242`（未覆盖） |
| fixed2 | `9B60C55F9D27EA91670DE411E348090E977111D6FAB0E9C1C319A5C52F02EEA5`（未覆盖） |
| fixed1 | `6FBE7A00485DDDCEA4F78CC1D4C92EE404878F4929681B7173D8D35CD0850854`（未覆盖） |
| 正式原包 | `D35A9804DAE6BE3D55C18B98F5833B98E9364335F56495B60290C0F1BBF5FD9F`（未覆盖） |

主要工作目录与证据：

- 工作源码：`E:\验证页面\work_db_lock_fix4_20260814_131647`（fixed3 ZIP 全新解压基线，修改前哈希见 `pre-modification-hashes.txt`）
- 机器 IIS 验收：`E:\验证页面\output\gateway-db-lock-fixed4-acceptance-20260814_143012`（全部脚本与 evidence/）
- 洁净 staging：`work_db_lock_fix4_package_stage_20260814`；全新解压复验：`work_db_lock_fix4_verify_20260814_0710`
- 生命周期探针（fixed4 二进制）：`output\gateway-db-lock-fixed4-acceptance-20260814_143012\fixed4-probe-tests\fixed4-stop-upgrade-probes.trx`

## 2. 真实根因

### 2.1 读路径裸 500

fixed3 现场栈（独立验收 `pressure-500-records.json`）：

```text
SQLite3.Prepare -> SQLiteCommand.BuildNextCommand -> ExecuteNonQuery
  -> SQLiteConnectionConfigurator.ConfigureOpenConnection
  -> LegacyGatewayRepository.OpenConnection -> HasRevokedAuthorization
ResultCode=Busy, ErrorCode=5, "database is locked"
```

根因：`OpenConnection` 在每个业务连接打开后执行组合 PRAGMA batch
（`foreign_keys=ON; synchronous=NORMAL; wal_autocheckpoint=1000; busy_timeout=50`）。
批量执行时第一条语句的 `Prepare`（System.Data.SQLite 逐条构建命令）在外部写锁
（独立进程 `BEGIN IMMEDIATE` 1100/400 ms 循环）下抛 Busy；该异常不在写执行器的
Busy 分类范围内（读路径直接调用 `OpenConnection`），穿透为 HTTP 500。

基线探针证据（`PragmaBusyProbeTests.IndividualPragmaStatementsProbeMatrixUnderExternalLocks`，
Console 输出 PRAGMA-PROBE 矩阵）：逐条执行四条语句时，无锁基线全部 ok；外锁下
需要触碰数据库文件的语句（首条 `foreign_keys=ON` 的 Prepare 触发的 WAL 索引/恢复路径）
返回 Busy，连接本地语句（`synchronous`/`busy_timeout`/`wal_autocheckpoint`）不抛。

### 2.2 写预算穿透（11210 ms）

fixed3 记录：`attempts=1, sqliteBusyCount=0, databaseCallMs=11210, elapsedMs=11210, writeBudgetMs=2000`
——单次成功 provider 调用耗时 11.2 秒、无 Busy 计数，2000 ms 预算完全无法介入。

本轮处理：

1. 写执行器重构：预算从调用开始覆盖 进程锁等待 → 连接创建/打开 → schema 校验 →
   `BEGIN IMMEDIATE` → 全部 SQL → COMMIT/ROLLBACK → 退避 → 连接收尾；
2. 新增 `SQLiteConnectionBudgetGuard`：绝对 deadline 到达时由 watchdog 线程调用
   `SQLiteConnection.Cancel()`（交付包 System.Data.SQLite.xml 明确：可从任意线程调用，
   使当前操作尽早中止；`SQLiteCommand.Cancel` 未实现故不用）打断单次 provider 调用；
   守卫先于连接关闭 Dispose（停 timer + 等待回调），不存在 Cancel/Close 竞态；
3. 保留 maxAttempts 与退避作为第二层；两条路径谁先到都以
   `GatewayDatabaseBusyException`（503 + Retry-After: 1）结束。

固定4轮次内未复现 11.2 s 离群点（本轮全程 `DatabaseWriteSlow` 记录为 0）。
固定3 记录 `sqliteBusyCount=0 + attempts=1 + 成功` 表明耗时发生在单次调用内部；
本轮已消除其中两个结构性候选（热路径 PRAGMA batch 与逐请求 schema 遍历——实测
`connectionOpenMs=14, schemaCheckMs=0`），并给剩余候选（COMMIT/checkpoint 等）
加了硬边界守卫。每笔写诊断的 `phase=` 字段（connectionCreate/open/schemaCheck/
beginTransaction/command/commit/rollback/close）已就位，若再次复现可立即归属；
见“已知未测风险”。

### 2.3 0x800703E3

研发现场栈（`HttpException(0x80004005) -> COMException(0x800703E3)`，
`IIS7WorkerRequest.ReadEntityCoreSync -> HttpRequest.GetEntireRawContent -> GetInputStream
-> ManagedReverseProxy.WriteRequestBody`）与 fixed3 源码缺口对照：

- fixed3 的 `Proxy` 只 `catch (WebException)`；`WriteRequestBody` 内
  `context.Request.InputStream.CopyTo(...)` 读取客户端正文时抛出的
  `HttpException->COMException(0x800703E3)` 不属于 WebException，穿透到
  `Global.Application_Error` → 500 + 错误页二次写入。
- 组件版本（Web=1.0.34.0 / Core=1.0.14.0 / SQLite=3.42.0）与 fixed3 候选一致。

### 2.4 ZIP 绝对路径

fixed3 ZIP 的 `obj/Release/*.FileListAbsolute.txt` 含生成机路径（P3）；本轮洁净 staging
不再打包任何 `obj`，并以规则化扫描验证。

## 3. 修改文件与关键实现

| 文件 | 变更 |
|---|---|
| `SQLiteConnectionConfigurator.cs` | 删除组合 PRAGMA batch；`ConfigureOpenConnection` 改为“验证”式（连接本地 PRAGMA 只读查询，不再与外锁竞争）；保留 init 级 `ConfigureDatabase`(WAL)/`AcquireImmediateWriteLock`；新增 `IsDatabaseInterrupted` |
| `LegacyGatewayConfiguration.cs` | 连接串增加 `Synchronous=Normal`（经公开索引器，provider 内部枚举不可引用）；新增 `SqliteReadBudgetMilliseconds`（默认 500，上限不超过写预算） |
| `GatewayDatabaseInitializer.cs` | 新增 AppDomain 级 schema 校验缓存（按库路径）；`EnsureCreated` 两个成功出口记录校验版本；请求路径 `EnsureSchemaVerifiedOnConnection` 只在缓存未命中时执行一次 `HasCurrentSchema`（fail closed，不缓存失败） |
| `DatabaseWriteOperationContext.cs`（新） | 写操作上下文：绝对预算、phase 计时、Cancel/Commit 状态标志、`BeginWriteTransaction/Commit/Rollback` 包装器；生命周期由写执行器拥有 |
| `SQLiteConnectionBudgetGuard.cs`（新） | watchdog Timer → `SQLiteConnection.Cancel()`；Dispose 先于连接关闭；幂等；回调不访问数据库 |
| `LegacyGatewayRepository.cs` | 写执行器重构（连接由执行器打开并装配守卫；Interrupt+本次守卫→busy，非本次→保留原异常记录来源；COMMIT 明确成功后返回成功并记 `DatabaseWriteBudgetOverrunAfterCommit`；收尾失败禁止回池 + quick_check）；新增统一读执行器 `ExecuteDatabaseRead`（500 ms 预算、10/25/50/100+抖动退避、Busy/Locked→有界重试/503）；32 个写调用点与 21 个读方法全部改接（审计 best-effort 路径保持既有语义）；写退避阶梯按计划 25/100/225/300 ms |
| `GatewayDatabaseBusyException.cs` | 增加 IsReadPath/SqliteLockedCount/SqliteInterruptCount/PhaseSnapshot/CancelRequested/CancelObserved/CommitCompleted/CommitOutcomeUncertain 诊断字段 |
| `ManagedReverseProxy.cs` | `ContainsOperationAbortedHResult` 精确分类器（深度≤16、完整 HRESULT）；正文转发拆阶段并返回 `RequestBodyWriteResult`（Completed/NoBody/ClientBodyAborted/RejectedTooLarge）；64 KiB 手工复制循环 + 未知长度 100 MiB 上限；命中中止后幂等 Abort 上游、不写响应、不重试；`ClientBodyAbortEventAggregator` 5 秒窗口聚合防刷盘（计数守恒） |
| `Global.asax.cs` | 错误处理顺序修复：先按 `IsDatabaseBusy` 决定 503/500 写入响应，再写诊断记录——expected 503 不再被记录为 Global 500 |
| `Web.config` | 新增 `Storage.Sqlite.ReadBudgetMilliseconds=500` |
| `GatewayDemo.Legacy.Web.csproj` | 登记 3 个新源文件 |
| `tests/**` | 新增 8 个测试文件（见 §4）；更新 2 个既有测试以匹配新语义（schema 缓存化、SyncMode 连接串） |
| `README.md` | 连接配置分层、读写预算、取消守卫、断流事件与配置项说明 |

## 4. 单元与组件测试

- 本地全量：**115/115 通过**（61 fixed3 回归 + 54 新增），两轮（工作目录 `round1-fixed4.trx`/`round2-fixed4.trx` 与 ZIP 全新解压 `fresh-extract-fixed4.trx`）。
- 新增覆盖：分类器 12 项、正文复制 10 项、聚合器 8 项、预算守卫 9 项、真实 provider 写预算 2 项、读预算/锁矩阵 6 项、PRAGMA 基线探针 2 项、schema 缓存 4 项（另有既有 PRAGMA/连接串测试更新）。
- 关键真实 provider 证明：长递归 CTE 被 `SQLiteConnection.Cancel()` 打断（ResultCode=Interrupt，打断后连接可安全复用）；16 s 外部锁写 ~2 s 有界结束；1100/400 周期锁 100 次写全部成功且 max<2500 ms；EXCLUSIVE 外锁下 1000 次读只有成功/有界 busy；`integrity_check=ok` + `foreign_key_check=0`。
- Stop 升级探针（fixed4 二进制，独立探针工程，固定顺序+反射）：**151/151 通过**（含 100 次 Stop(false)→Stop(true) 与 50 次 Dispose→Stop(true)；52 s）。

## 5. 真实机器 IIS 验收（管理员高完整性令牌 S-1-16-12288）

站点 `CodexDbLockFixed4Gw20260814`（业务 49920/管理台 49921，应用池 maxProcesses=1 + 不重叠回收）
+ 交付 mock `CodexDbLockFixed4Bk20260814`（49925）；全新解压目录部署、独立测试库；
healthz=200/admin=200/backend=200（`evidence/machine-iis-deploy.json`）。

### 5.1 外锁矩阵（真实批准写路径 + 独立进程 BEGIN IMMEDIATE）

| 外锁 | 结果 | 判定 |
|---|---:|---|
| 250 ms | 200，373 ms | 通过（预算内恢复） |
| 1,100 ms | 200，1,269 ms | 通过（预算内恢复） |
| 3,200 ms | 503 + Retry-After:1，2,178 ms | 通过（未等待完整外锁） |
| 16,000 ms | 503 + Retry-After:1，2,182 ms | 通过（未等待 16 秒） |

IIS 上预算耗尽记录的诊断（attempts=6, sqliteBusyCount=6, retryDelayMs≈1190,
databaseCallMs≈680, elapsed≈2014, cancelRequested=false）说明外锁矩阵用例由
“50 ms 短 busy + 应用退避 + 总预算”路径有界结束；连接级 Cancel 守卫针对的是
“单次 provider 调用超过剩余预算”的场景（长查询/checkpoint 等），该路径由真实
provider 组件测试证明（长递归 CTE 被打断为 ResultCode=Interrupt，且打断后连接
可安全复用）。两条路径共同构成硬边界，均以 503 + Retry-After:1 结束。

### 5.2 JSON 503 契约

503 / 2,184 ms / `Retry-After: 1` / `application/json; charset=utf-8` / 可解析 /
无 SQLite/异常/堆栈/路径泄露；正文 `{"ok":false,"message":"数据库当前繁忙，请稍后重试。","requestId":"…"}`。

### 5.3 状态码感知压力（连续 5 轮，1100/400 外锁 + 6 设备）

| 轮次 | 审计请求 | 审计错误 | 关键写 | 关键错误 | 审计 p95/max(ms) | 关键写 p95/max(ms) |
|---|---:|---:|---:|---:|---:|---:|
| 1 | 5,044 | 0 | 54/54 | 0 | —/1,516 | —/1,532 |
| 2 | 6,624 | 0 | 54/54 | 0 | —/1,462 | —/1,486 |
| 3 | 6,437 | 0 | 54/54 | 0 | —/1,414 | —/1,426 |
| 4 | 5,183 | 0 | 54/54 | 0 | —/4,950 | —/1,391 |
| 5 | 4,586 | 0 | 54/54 | 0 | —/265 | —/199 |

合计 27,874 次状态码感知审计请求、270 次关键写（申请/批准/撤销各 90），
**unexpected 5xx=0、raw 500=0、关键写 100% 成功、关键写 max=1,532 ms ≤ 2,500 ms**、
无预算穿透（`DatabaseWriteBudgetOverrunAfterCommit=0`）、无孤儿后台工作。
第 4 轮 1 笔审计请求 max=4,950 ms 为单笔离群（外锁窗口内排队+重试），p95 与 0 错误门槛不受影响。
限流说明：验收部署临时把 `Gateway.RequestRateLimit.PermitLimit/UnsafePermitLimit` 与
`Gateway.BrowserDevice.FirstContactPermitLimit` 提高（原值/临时值记录于
`pressure-rate-limit-config.json`）；候选 ZIP 内 Web.config 仍为默认值；临时部署已整体删除。

### 5.4 cold AppDomain + 外锁读路径（6 轮）

每轮：recycle → healthz → 启动 1100/400 外锁 → 6 设备并发 1,020 次状态码感知请求：
**6/6 全部通过，serverErrors=0、上游泄露=0**（fixed3 现场 500 场景未再出现）。

### 5.5 多进程（临时 web garden maxProcesses=2，仅测试）

2,040 次读 + 100 次关键写：readErrors=0、critical 100/100、无 500、无重复授权；
3 个 w3wp PID 参与；结束后恢复 maxProcesses=1。

### 5.6 IIS recycle（6 轮）

每轮创建待审申请 → 40 次审计命中 → recycle → healthz=200 → 待审申请存活 →
批准 302 → 撤销：**6/6 通过**。

### 5.7 真实 headed Chrome 状态机（并行外锁循环）

12/12：申请 → 批准（1,432 ms）→ 上游登录页 → 撤销 → 设备被拦 → 重新申请 →
重新批准 → 恢复访问；设备/admin console error 0、network failure 0。
证据：`evidence/browser/`（7 张截图 + results JSON）。

### 5.8 移动 APP 协议回归（6/6；无真实 APK——协议级验证，原生控件未测）

bootstrap（无 action / action=login）→ 结构化 JSON（401 auth_required），无 500；
未批准登录 → 403“移动 APP 设备尚未获批”（正确设备状态，未误报）；批准后登录
→ SessionKey 访问 ok；撤销后旧 SessionKey 重放 → blocked；
**3200/16000 ms 外锁 → 503 + Retry-After:1 + 繁忙措辞，未误报“设备未获批/会话已失效”**。

### 5.9 每轮 SQLite 不变量（8 轮全部通过）

`integrity_check=ok`、`foreign_key_check=0`、`journal_mode=wal`、token/recovery 重复 0、
设备/站点重复有效授权 0、孤儿会话/凭据 0、缺设备引用 0；行变化与操作计数逐项吻合
（压力 5 轮 90 批准+90 撤销、多进程 100、recycle 6 等，`invariants-0..6-*.json`）。

## 6. 请求体中止验收（真实 IIS + 记录上游）

独立 rig：`CodexIoAbortFixed4Gw20260814`（49930/49931）+ 记录上游站点（49935，
验收环境专用、不在 ZIP 内；记录 requestStarted/headersReceived/bodyBytesReceived/
bodyCompleted/connectionAborted/completedAtUtc + 正文 SHA-256 回显）。

- 正常差分基线 7/7：JSON/表单/multipart/PUT/PATCH/DELETE、64 KiB/1 MiB/16 MiB，
  直连与经网关的 method/path/Content-Type/正文 SHA-256/长度完全一致。
- 断流矩阵：header-only FIN/RST、mid-body FIN/RST、慢传杀进程、incomplete chunk、
  Expect-100 中止、complete-then-disconnect、in-flight 上游中止（delayMs）各 ≥20 次；
  并发 50/100（barrier 同时断开）；recycle/shutdown mid-upload。
- 硬断言：`Global.Application_Error` 含 0x800703E3 = **0**；断流引起的
  `Application_Error.ResponseWrite` = **0**；`WriteBadGateway` = **0**；
  非幂等重试 = **0**；每批后 healthz=200；断流后正常 POST 与直连 SHA-256 一致；
  新分类路径真实命中 12 次（最终轮，其余按竞态由既有 ClientDisconnectedToken 路径
  处理，两条路径都不产生 500；分类器 12 项单测提供确定性证明）。
- 上游终止：绝大多数断流在网关建立上游连接前即被识别并终止——专项脚本 5 轮
  ×100 并发 barrier 断开共 500 个用例中，**0 个上游请求处于“处理中”状态**
  （记录上游零事件，无孤儿上游连接）；矩阵轮次中唯一一个处理中样本实测终止耗时
  ≈1 s（≤1000 ms 门槛）；in-flight 上游中止用例覆盖“完整上传后断开”路径。
  权威证据：`evidence/upstream-abort-latency-results.json`。

## 7. ZIP 交付与卫生（§14）

- 洁净 staging 白名单复制（源码/脚本/packages/README + Web 运行 bin；tests 仅源码与
  csproj；无 obj/TestResults/TRX/数据库/日志）。
- 扫描规则（`scan-zip-hygiene.ps1`）：obj 条目、非通用本机绝对路径（通用文档路径
  C:\nginx/Cert:/HKLM:/系统目录白名单）、机器名 \bDESKTOP-/WIN-+4 字符、临时凭据
  （accept-db4/mock-db4/ioabort/work_db_lock_fix4）、数据文件（*.db/*-wal/*-shm/*.log/
  *.jsonl/*.trx/TestResults/截图）。
- 结果：**条目 229；obj=0、绝对路径=0、机器名=0、凭据=0、数据文件=0**。
- 全新解压 → Release rebuild 0 error → 全量测试 115/115。

## 8. Definition of Done 核对

- [x] 请求路径不再执行组合 PRAGMA batch（源码 + 探针 + 外锁下验证式配置不再抛 Busy）
- [x] Busy handler 在任何可能访问数据库的 phase 前生效（连接串 BusyTimeout 在 Open 安装）
- [x] 读路径 Busy/Locked 不再成为 HTTP 500（cold 6 轮 0 500；读预算 → 503）
- [x] 2000 ms 写预算覆盖单次 provider 调用（Cancel 守卫 + 16 s 锁 ~2.2 s 有界）
- [x] 已 COMMIT 操作不会返回失败（Commit 包装器 + overrun-after-commit 语义 + 单测）
- [x] 状态码感知压力连续 5 轮 unexpected 5xx=0
- [x] 关键写 max=1,532 ≤ 2,500 ms，budget overrun=0
- [x] cold AppDomain + 外锁 6 轮无 500
- [x] 多进程无 500/重复授权/失效凭据恢复（2,040 读 + 100 写）
- [x] 原 61 项测试全部通过（115 = 61 + 54 新增）
- [x] Stop 升级探针 151/151（≥150）
- [x] recycle 6/6；headed Chrome 12/12 console/network 0 异常
- [x] 移动 APP 协议 busy 契约 503 + 繁忙措辞，不误报设备/会话状态（6/6）
- [x] 每轮 SQLite 不变量全部通过（8 轮）
- [x] ZIP obj=0、绝对路径=0、机器名=0、数据库/日志/TRX=0、凭据=0
- [x] `0x800703E3` 只在请求体读取阶段按精确 HRESULT 链分类（分类器 12 项单测）
- [x] 断流后不进入 Global.Application_Error（0 命中）、不写 502/500、不重试非幂等
- [x] 其他 HttpException/COMException 仍按原错误路径处理（分类器反例单测）
- [x] 并发断流后 healthz=200、w3wp 回落（70 线程/773 句柄→62/743，无单调上涨）
- [x] fixed4 从 ZIP 全新解压可 Release build/test（115/115）
- [x] fixed3/fixed2/fixed1/正式原包哈希未变化
- [x] 临时 IIS/端口/锁进程/flag 全部清理（见 §9）

## 9. 资源清理结果

- `CodexDbLockFixed4Gw/Bk20260814`、`CodexIoAbortFixed4Gw/Rec20260814` 站点/池全部删除（appcmd list 0）；
- 端口 49920/49921/49925/49930/49931/49935 与遗留 48920/48921/48925 监听均为 0（Get-NetTCPConnection 逐端口复核）；
- `codex-fixed4-*.flag` 与锁持有进程清理；两套临时验收数据库随部署目录删除；
- 本轮开始前遗留的 fixed3 io-abort 预检站点 `CodexIoAbortFixed3Gw/Bk20260814`（端口 48920/48921/48925）一并清理（证据见 §1 目录 cleanup 记录）。

## 10. 已知未测风险

1. **11.2 s 离群点未复现**：fixed4 全程无 DatabaseWriteSlow；phase 计时已就位，若现场再次出现
   可在单条日志中直接归属 phase（connectionOpen/schemaCheck/begin/command/commit/close）。
   当前结论是“结构性候选（热路径 PRAGMA、逐请求 schema 遍历）已消除 + 硬边界守卫兜底”，
   而非“已实证 11.2 s 的确切来源”。
2. **COMMIT 结果不确定窗口**：Interrupt 发生在 COMMIT 期间的窗口按 WAL 原子性（索引未更新即未提交）
   处理为回滚+503；该推理由 SQLite WAL 提交协议支撑，未做崩溃级实证。
3. **移动 APP 为协议级验证**：无真实 APK/AVD，原生控件未测；完整回环（批准后登录/SessionKey/撤销重放）
   已协议级通过，但 UI 层未覆盖。
4. **32 位互操作未重测**（x64，与既有验收一致）。
5. **同 AppDomain 内 runtime 重建**未做单元级测试（与 fixed2/fixed3 相同，延续真实 IIS recycle 覆盖）。
6. **多进程专项仅覆盖 web garden 方案**（maxProcesses=2，测试后恢复），未建两个独立 IIS 应用。
7. **断流上游终止耗时**：绝大多数用例在网关建立上游连接前已识别并终止（记录上游无请求），
   属更优结果；in-flight 打断路径以专用用例验证，未做百次级统计。

---
生成时间：2026-08-14（UTC）。全部证据目录：
`E:\验证页面\output\gateway-db-lock-fixed4-acceptance-20260814_143012\evidence\`
