# GatewayDemo.Legacy SQLite 慢写、锁竞争与线程积压修复落地报告

> 依据 `E:\验证页面\temptplan.md` 实施。候选包已从零解压、构建、单测和真实 IIS 验收。

## 候选 ZIP

- 候选 ZIP：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472-db-lock-fixed-20260813.zip`
- 候选 SHA-256：`6fbe7a00485dddcea4f78cc1d4c92ee404878f4929681b7173d8d35cd0850854`
- 原包 SHA-256：`d35a9804dae6be3d55c18b98f5833b98e9364335f56495b60290c0f1bbf5fd9f`（实施前后一致，原包未被静默覆盖）
- 实施工作目录：`E:\验证页面\work_db_lock_fix_20260813_170026`（ZIP 全新解压，未直接修改历史验收目录）
- 真实 IIS 验收证据目录：`E:\验证页面\_db_lock_verify_20260813`（含部署站点、应用日志、DB、验收脚本与结果）

## 根因修复（对照 temptplan.md 五组修复）

1. **只读事务不再获取写保留锁**：`GetDashboardSnapshotAsync` 由无参 `BeginTransaction()` 改为显式 deferred（`BeginTransaction(IsolationLevel.ReadCommitted, true)`），快照保持一致性读取但不阻塞 writer。全仓库 27 处 `BeginTransaction()` 逐一审核：全部为写事务（INSERT/UPDATE/DELETE），保留原语义；唯一纯读事务为管理台快照，已改 deferred。
2. **连接 PRAGMA 与 provider 重试参数**：连接串统一用 `SQLiteConnectionStringBuilder` 显式设置 `DefaultTimeout=0`、`DefaultMaximumSleepTime=0`、`BusyTimeout=配置值(默认50)`、`PrepareRetries=0`、`StepRetries=0`、`Pooling=true`、`ForeignKeys=true`、`FailIfMissing=true`；每个业务连接打开后执行 `foreign_keys=ON; synchronous=NORMAL; wal_autocheckpoint=1000; busy_timeout=<短值>`；初始化/迁移的 `ConfigureDatabase`/`AcquireImmediateWriteLock` 使用独立 5 秒短预算，删除"配置后恢复 15 秒 busy timeout"逻辑。
3. **写预算/重试执行器**：`ExecuteDatabaseWrite` 重写为总预算（默认 2000 ms）语义：预算覆盖等待 `DatabaseWriteSyncRoot`（`Monitor.TryEnter` 带剩余预算）、provider 短 busy wait、应用层退避；退避序列 25/50/100/200/300 ms + 0–25 ms 抖动，**退避时不持有进程锁**；达到预算或最大尝试次数后抛新增 `GatewayDatabaseBusyException`（含 operation/attempts/sqliteBusyCount/processLockWaitMs/retryDelayMs/databaseCallMs/elapsedMs/resultCode）。HTTP 转换：`Global.asax.cs` 集中遍历异常链，503 + `Retry-After: 1` + 安全中文消息（JSON 请求返回可解析 JSON）；管理台操作与快照同步处理。诊断字段：`operation/outcome/attempts/sqliteBusyCount/processLockWaitMs/providerBusyTimeoutMs/retryDelayMs/databaseCallMs/elapsedMs/writeBudgetMs`（pid/processId/appDomain 由 GatewayErrorLogger 统一记录）；慢写 500 ms 记诊断、1000 ms 记错误、任何 busy 恢复记录 `DatabaseWriteLockRecovered`、预算耗尽记录 `DatabaseWriteBudgetExceeded`。
4. **关键与非关键审计分离**：新增 `GatewayBestEffortAuditDispatcher`（`BlockingCollection` 有界队列，默认容量 2048；单专用消费者；每批最多 50 条一个事务；未满批聚合 250 ms；同设备/kind/siteKey 30 秒窗合并；队列满 drop-newest 计数；跨进程 busy 有界重试后放回/丢弃计数；低优先级零等待获取进程写锁）。`ProxyRequestModule` 两处 `Task.Run(...).GetAwaiter().GetResult()` 删除，改为 `TryQueueBestEffortAudit`（保留 `ShouldRecordUnauthorizedAudit` 时间窗去重）；`BrowserDeviceService` 重复 Cookie 审计同样入队；`BrowserRecovery` 凭据恢复审计保持同步持久化（关键安全事件）。dispatcher 由 `LegacyGatewayRuntime` 持有并接入 `IRegisteredObject` 生命周期：`Stop(false)` 最多 2 秒 drain，`Stop(true)` 立即取消，Dispose 幂等。
5. **审计清理移出请求写路径**：`InsertAudit` 移除 `auditCleanupCounter` 同步清理，只负责插入；后台维护任务（dispatcher 内，首延迟 2 分钟、默认每 10 分钟一轮）先删 90 天前过期数据、再删超出最新 10,000 条的溢出，每批最多 500 行（`IX_GatewayAuditLogs_CreatedAtUtc` 有界批次），每批提交后释放进程写锁并让出 50 ms，每轮最多 10 批；每轮记录删除数、批次数、耗时、DB/WAL/SHM 大小。

## 具体修改文件

- `src/GatewayDemo.Legacy.Web/Infrastructure/GatewayDatabaseBusyException.cs`（新增）：预算耗尽异常与异常链判断。
- `src/GatewayDemo.Legacy.Web/Infrastructure/GatewayAuditRecord.cs`（新增）：非关键审计记录与合并键。
- `src/GatewayDemo.Legacy.Web/Infrastructure/GatewayBestEffortAuditDispatcher.cs`（新增）：有界队列、单消费者、批量写、同键合并、丢弃计数、后台清理维护、生命周期。
- `src/GatewayDemo.Legacy.Web/Infrastructure/SQLiteConnectionConfigurator.cs`：连接级 PRAGMA 全连接统一；删除 15 秒预算与"恢复 15 秒 busy_timeout"；初始化短预算重试。
- `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayConfiguration.cs`：8 个新配置项（有界解析，缺键用默认值）；连接串 builder 显式限制托管重试。
- `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRepository.cs`：deferred 只读事务；`ExecuteDatabaseWrite` 总预算重写；`InsertAudit` 移除清理；`DeleteAuditLogsBefore/Overflow/CountAuditLogs` 有界批删除；`TryQueueBestEffortAudit`；`DatabaseWriteSyncRoot` 提升为 internal 供 dispatcher 低优先级获取。
- `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRuntime.cs`：持有并启动 dispatcher，接入 Stop/Dispose 生命周期，暴露 `BestEffortAuditDispatcher` 静态入口。
- `src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs`：两处非关键审计改 `TryQueueBestEffortAudit`。
- `src/GatewayDemo.Legacy.Web/Infrastructure/BrowserDeviceService.cs`：重复 Cookie 审计改 `TryQueueBestEffortAudit`。
- `src/GatewayDemo.Legacy.Web/Global.asax.cs`：集中 503 转换（含 JSON 请求）。
- `src/GatewayDemo.Legacy.Web/Admin/Default.aspx.cs`：管理台操作/快照的 busy 转换与提示。
- `src/GatewayDemo.Legacy.Web/Infrastructure/GatewayDatabaseInitializer.cs`：升级写守卫错误消息改为动态预算值。
- `src/GatewayDemo.Legacy.Web/Web.config`：新配置键示例。
- `src/GatewayDemo.Legacy.Web/GatewayDemo.Legacy.Web.csproj`：新文件编译项。
- `scripts/legacy/setup-demo-sites.ps1`：8 个 SQLite 并发参数 + 部署输出摘要。
- `README.md`：新增“SQLite 并发与维护”章节（单库单应用、单 worker、备份、路径约束、诊断来源、不调大 busy timeout）。
- `tests/GatewayDemo.Legacy.Tests/*`：新增 5 个测试类 + 测试基础设施（详见下）。

## 单元/组件测试

工具链：MSBuild 17.14.23（`E:\Visual Studio BuildTools`）、dotnet SDK 10.0.201、xunit 2.9.3、System.Data.SQLite 1.0.118（真实 DLL，非 mock）。

- 总数 42（原有 18 + 新增 24），通过 42，失败 0。
- 锁注入结果（真实 SQLite 复制库，独立连接 `BEGIN IMMEDIATE`）：
  - 250 ms：预算内恢复成功（elapsed < 1500 ms）
  - 1,100 ms：预算内恢复成功（elapsed < 2200 ms）
  - 3,200 ms：约 2 秒抛 `GatewayDatabaseBusyException`（elapsed < 3000 ms）
  - 16,000 ms：同样短预算失败（elapsed < 3000 ms），不等待 16 秒
- 16 连接 PRAGMA 结果：全部 `wal / synchronous=1 / foreign_keys=1 / busy_timeout=配置值`，池复用后仍正确；连接串断言 `DefaultTimeout=0、DefaultMaximumSleepTime=0、PrepareRetries=0、StepRetries=0`。
- 100k 行审计清理：多轮收敛至 ≤10,000 条，90 天前数据全部删除，`integrity_check=ok`；前台插入不触发清理；中断后可续。
- 队列：容量上限有效、生产者不阻塞、同键合并、丢弃计数、批量写库、优雅 shutdown drain、immediate shutdown 不死锁、停止后拒绝入队。

## 真实 IIS 验收（IIS 10 / 真实 w3wp / 真实 System.Data.SQLite / 本地 mock backend）

- 站点：`GatewayDbLockVerify`（端口 20050 业务 / 20051 管理台）、`MockBackendDbLockVerify`（20055）；应用池 `maxProcesses=1`、`disallowOverlappingRotation=true`；w3wp PID 10788（后回收为 44464）。
- 空闲基线：管理台快照 20 次刷新 p50=49–99 ms、p95=50–148 ms、max ≤ 148 ms；无 `DatabaseWriteSlow`。
- 管理台读写并发：40 请求（20 快照 + 20 业务）0 错误。
- 申请→审批→撤销全流程：提交申请（真实 CSRF）成功、批准 55–115 ms、撤销 812 ms。
- 外部持锁矩阵（独立进程 `BEGIN IMMEDIATE`，真实 IIS 批准写路径）：

| 持锁 | 结果 | 日志 |
|---|---:|---|
| 250 ms | 302 成功（120 ms） | — |
| 1,100 ms | 302 成功（1,094 ms） | `DatabaseWriteLockRecovered` attempts=2 sqliteBusyCount=1 |
| 3,200 ms | 503 + Retry-After:1（2,227 ms） | `DatabaseWriteBudgetExceeded` elapsedMs=2015 writeBudgetMs=2000 |
| 16,000 ms | 503 + Retry-After:1（2,207 ms） | 同短预算失败，无线程风暴 |

- 回收测试：应用池回收后业务与管理台立即恢复（200/302），无需手工干预。
- 线程数：w3wp 66 线程基线，审计压力下无持续上涨；审计队列有界（默认 2048），无丢弃（本次压力未达上限）。

## 业务回归

- bootstrap：GET /gateway 200 + 设备 Cookie 创建成功。
- 申请提交：`action=request-access` + CSRF → 200 提交成功。
- 管理台登录、审批（55–115 ms）、撤销（812 ms）成功；快照无写锁阻塞。
- 数据库不变量：`integrity_check=ok`；`foreign_key_check` 无违规；无重复有效授权（每设备单条 Approved 授权）；撤销后设备回 Challenged。
- 已知未回归项（基线行为保持不变）：POST `/gateway` 路由 405 为 IIS 无扩展名处理默认行为（与基线一致，验收经 `/Gateway/Default.aspx` 提交）；新设备首触限流 30 次/60 分钟为基线安全策略，测试期间触发属预期。

## 数据库

- `integrity_check: ok`；`foreign_key_check: []`。
- 验收库：64 设备、7 条已批准授权、7 条待审、86 条审计（64 device-created / 15 request-created / 7 request-approved），90 天前审计 0 条；`AuditCleanupRound` 后台轮次正常执行。

## 证据目录

- `E:\验证页面\_db_lock_verify_20260813`：候选包解压部署（含应用日志 `App_Data/logs/gateway-errors-2026-08-13.jsonl`、数据库、Web.config、gateway-sites.json）、`verify_iis.ps1`、`lock_matrix.ps1`、`lock-holder.ps1`、`revoke_recycle.ps1` 及运行输出。

## 清理结果

- 验收站点 `GatewayDbLockVerify`/`MockBackendDbLockVerify` 及应用池已删除；端口 20050/20051/20055 无监听残留；临时目录与标志文件已删除；`test*.trx` 已清理。
- 未创建证书、计划任务或额外端口占用。

## 构建命令与警告

- 主项目：`MSBuild.exe src\GatewayDemo.Legacy.Web\GatewayDemo.Legacy.Web.csproj /t:Build /p:Configuration=Release /p:Platform=AnyCPU` → 0 错误。
- 测试：`dotnet test tests\GatewayDemo.Legacy.Tests\GatewayDemo.Legacy.Tests.csproj -c Release` → 42/42。
- 警告：仅测试项目 6 条 xUnit1031（同步 Task 调用，测试场景有意为之）与 1 条 CS0618（`BeginTransaction(IsolationLevel,bool)` 过时 API，为获得 deferred 语义必须使用，已用 pragma 说明）；Web 项目 0 警告。

## 已知未测风险

- 未做双进程重叠回收专项（当前池已 `disallowOverlappingRotation`）；跨进程行为已由独立进程锁注入矩阵覆盖。
- 未执行浏览器级 WebAuthn/激活通知回归（本次改动不触及这两条路径，且原包 18 项 bootstrap 单测全部通过）。
- 备份/杀毒软件扫描干扰未在本机复现（计划 9.3 已在 README 给出 ProcMon 排查指引）。
