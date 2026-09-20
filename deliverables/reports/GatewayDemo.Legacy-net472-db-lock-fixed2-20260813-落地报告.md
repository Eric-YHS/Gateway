# GatewayDemo.Legacy 数据库锁修复第二轮落地报告

> 依据 `E:\验证页面\temptplan.md`（第二轮）实施。本轮从上一候选包零解压基线出发，
> 修复 5 项 P1/P2 问题，并从新候选 ZIP 零解压重编译、55 项测试全通过、真实机器 IIS 验收通过。

## 候选包与哈希（三个包均未被覆盖）

| 包 | SHA-256 |
|---|---|
| 新候选 `GatewayDemo.Legacy-net472-db-lock-fixed2-20260813.zip` | `9b60c55f9d27ea91670de411e348090e977111d6fab0e9c1c319a5c52f02eea5` |
| 上一候选 `GatewayDemo.Legacy-net472-db-lock-fixed-20260813.zip` | `6fbe7a00485dddcea4f78cc1d4c92ee404878f4929681b7173d8d35cd0850854`（未覆盖） |
| 正式原包 `GatewayDemo.Legacy-net472.zip` | `d35a9804dae6be3d55c18b98f5833b98e9364335f56495b60290c0f1bbf5fd9f`（未覆盖） |

工作目录：`E:\验证页面\work_db_lock_fix2_20260813_201015`（上一候选包零解压）。
零解压验证目录：`E:\验证页面\work_db_lock_fix2_verify_202621`（新候选 ZIP 全新解压）。
IIS 验收证据目录：`E:\验证页面\output\gateway-db-lock-fix2-acceptance-20260813-202844`。

## 逐文件变更清单

| 文件 | 变更 |
|---|---|
| `src/GatewayDemo.Legacy.Web/Infrastructure/GatewayBestEffortAuditDispatcher.cs` | 重写：锁内/锁外两阶段；新增 `AuditBatchAttemptOutcome`/`AuditBatchAttemptResult`；`TryWriteBatchOnce` 单次锁内尝试（无任何 sleep/等待）；`TryWriteBatch` 锁外决策循环；`YieldOutsideProcessLock` 用 `ManualResetEventSlim` 实现可被立即停机打断的退避（进程锁繁忙 20 ms / SQLite busy 25/100/225 ms / 重排冷却 100 ms）；`Stop(true)` 置位 `immediateStopRequested` + signal + `CompleteAdding`；`Stop(false)` 优雅 drain（2 秒时限）；Dispose 等待消费者退出后才释放 queue/signal；失败记录拆分 `lastFailure/lastFailureUtc` 与按 source 的 `lastLogUtcBySource` 限频（第一条必然记录）；终止性失败整批计数（failedBatch/failedRecord/terminalDropped）；新增 capacityDropped/shutdownDropped/rejectedAfterStop 细分计数；merge key 只在成功入队后登记；消费者顶层异常边界 + `consumerExited` 信号；清理轮次可被立即停机打断；internal 测试注入构造（delay/logSink/clock）。 |
| `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRepository.cs` | `ExecuteDatabaseWrite` 累计 provider 调用时间：`databaseCallTotalMilliseconds += lastDatabaseCallMilliseconds`；慢写判断改用最后一次调用时间（避免 busy 恢复时重复 `DatabaseWriteSlow` 风暴）；诊断串新增 `lastDatabaseCallMs`；`BuildDatabaseBusyException`/`LogWriteSucceeded`/`BuildWriteDiagnosticDetail`/`LogWriteBudgetExceeded` 全部传入累计值与末次值。 |
| `src/GatewayDemo.Legacy.Web/Infrastructure/GatewayDatabaseBusyException.cs` | 构造函数新增 `lastDatabaseCallMilliseconds`；`DatabaseCallMilliseconds` 保持字段名不变但语义明确为“所有尝试的累计 provider 调用时间”；新增 `LastDatabaseCallMilliseconds`。 |
| `src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRuntime.cs` | `Stop` 用 `Interlocked.CompareExchange(ref bestEffortAuditDispatcher, null, dispatcher)` 原子清除静态引用（只清当前实例）；`Dispose` 在 Stop 后安全 Dispose dispatcher；同 AppDomain 重建 runtime 时新 dispatcher 覆盖静态引用。 |
| `README.md` | 补充：审计消费者退避全部在进程写锁外、merge key 语义、`databaseCallMs`/`lastDatabaseCallMs` 含义、`elapsedMs >= processLockWaitMs + retryDelayMs + databaseCallMs`、计数器定义、graceful/immediate stop 区别、失败限频语义。 |
| `tests/GatewayDemo.Legacy.Tests/BestEffortAuditBackoffTests.cs` | **新增**：P1 barrier 测试（退避期间进程写锁可立即取得 + 黑盒计时探针 <100ms）、`ImmediateStopInterruptsSqliteBusyBackoffWithinHardLimit`、`GracefulStopWithExternalLockExitsBoundedAndCountsShutdownDrops`。 |
| `tests/GatewayDemo.Legacy.Tests/AuditDispatcherTests.cs` | **新增 7 项**：首次失败必记录 + 按 source 限频（注入时钟/日志 sink）、`LastFailure` 追踪最新失败不受限频影响、终止性失败计数与批内条数一致、日志 sink 抛异常消费者不崩溃、队列满丢弃不留假 merge key、并发同键生产者不阻塞且计数守恒、Stop/Dispose 并发安全。收紧：`ImmediateShutdownDoesNotDeadlock` 由 <5000ms 收紧到 <500ms 并验证 `WrittenCount` 停止增长；`EnqueueAfterStopIsRejected` 增加 merged/rejectedAfterStop 断言。 |
| `tests/GatewayDemo.Legacy.Tests/WriteBudgetRetryTests.cs` | **新增**：`DatabaseCallMsIsCumulativeAndLastCallIsSeparate`（累计 ≥ 末次、`elapsedMs >= processLockWaitMs + retryDelayMs + databaseCallMs`、503 异常链同一类型）。 |
| `tests/GatewayDemo.Legacy.Tests/AuditCleanupTests.cs` | **新增**：`CleanupDoesNotRunAfterStop`、`ImmediateStopPreventsFurtherCleanupBatches`。 |

未改 WebAuthn、APP bootstrap、代理头、认证状态机、页面样式；未新增配置项（Web.config 向后兼容）。

## 根因与设计说明

### P1：best-effort 审计消费者在 SQLite busy 退避时持有 `DatabaseWriteSyncRoot`

旧控制流：`Monitor.TryEnter` → SQLite 写入 busy → catch 内 `Thread.Sleep(25/100/225ms)` → finally 才 `Monitor.Exit`，
即退避发生在持有进程写锁期间。独立真实 SQLite 探针复现了 100/225 ms 量级关键线程锁等待。

修复：一次审计批次写入拆成两个阶段：

1. **锁内阶段 `TryWriteBatchOnce`**：检查停机 → `Monitor.TryEnter`（零等待）→ 打开连接/事务 → 批量 INSERT → Commit；
   connection/transaction/command 由 using 在 `Monitor.Exit` **之前**安全 Dispose；catch 只把结果归类为
   `Succeeded / ProcessLockBusy / SqliteBusy / TerminalFailure / Cancelled`，方法内不存在任何 sleep/等待/让出。
2. **锁外阶段 `TryWriteBatch`**：根据结果在**释放进程锁之后**执行退避：进程锁繁忙让出 20 ms、
   SQLite busy 按 25/100/225 ms 有界序列、重排后冷却 100 ms；全部通过 `immediateStopSignal.Wait(delay)`
   实现，可被立即停机打断；达到 3 次尝试/批次预算后按“stopping→停机丢弃，否则放回队尾”处理并输出
   限频诊断（attempts/batchItems/elapsedMs/queueDepth/requeued/dropped）。

结构上保证未来新增 catch 分支时不会再次在锁内 sleep。

### P2：首次非锁失败日志被限频吞掉

旧 `RecordFailure` 先更新 `lastFailureUtc` 再调用 `LogRateLimited`，后者看到“最近失败 < 5 秒”立即返回，
第一条真正失败永远没有日志。修复：`lastFailure/lastFailureUtc`（每次失败更新，不受限频影响）与
`lastLogUtcBySource`（按 source 独立的日志限频时间）完全分离；`audit-cleanup-skip` 不再压掉
`audit-queue-write-failure` 的首条记录。

### P2：`Stop(true)` 不真正通知消费者

旧实现只缩短 join 时间。修复：`Stop(true)` 先置位 `immediateStopRequested` 并 Set `immediateStopSignal`，
再 `CompleteAdding()` 唤醒取件等待；flush 聚合、进程锁退避、SQLite busy 退避全部可被打断；
不再发起新的写尝试；剩余记录按停机丢弃计数；`Stop` join 有界（immediate 1s / graceful 3s）。
`Stop(false)`：`CompleteAdding` 唤醒消费者、最多 2 秒 drain、截止后剩余记录按停机丢弃、不无限 join。

### P2：`databaseCallMs` 只保存最后一次 provider 调用

修复：`databaseCallTotalMilliseconds` 累计所有尝试的 provider 调用时间，`lastDatabaseCallMilliseconds`
保存最后一次；诊断输出 `databaseCallMs=<累计>; lastDatabaseCallMs=<最后一次>`；慢写判断使用
**最后一次**调用时间（无 busy 时与累计相等，行为不变；有 busy 时优先记 `DatabaseWriteLockRecovered`，
不再产生重复 `DatabaseWriteSlow` 风暴）。`GatewayDatabaseBusyException.DatabaseCallMilliseconds` 语义
明确为累计值（字段名保持兼容），新增 `LastDatabaseCallMilliseconds`。满足
`elapsedMs >= processLockWaitMs + retryDelayMs + databaseCallMs`，各分量不重复计时。

### P2：队列满前先写 merge key

旧流程先 `recentMergeKeys[mergeKey] = now` 再 `TryAdd`，队列满时失败记录留下假 key，未来 30 秒
同键请求被计为 merged 但实际没有记录入队。修复：先查“成功入队留下的有效 key”→合并；否则先
`TryAdd`；**入队成功后才登记/刷新 merge key**；入队失败不写 key 并计入丢弃。生产者仍非阻塞；
首次并发同键允许有限重复入队（best-effort 合并退化），计数守恒：
`producerAttempts = enqueued + merged + rejectedAfterStop + capacityDropped`。

## 构建与测试（Release）

- 主项目：`MSBuild.exe src\GatewayDemo.Legacy.Web\GatewayDemo.Legacy.Web.csproj /t:Build /p:Configuration=Release /p:Platform=AnyCPU` → 0 错误。
- 测试：`dotnet test tests\GatewayDemo.Legacy.Tests\GatewayDemo.Legacy.Tests.csproj -c Release`。
- **总数 55（原 42 + 新增 13），执行 55，通过 55，失败 0**（TRX：`test-output\round2-full.trx`）。
- 新增 13 项覆盖：P1 锁外退避 barrier、立即停机打断 busy 退避（<1000ms）、优雅停机时限与停机计数、
  首次失败必记录/按 source 限频/时钟注入、LastFailure 追踪、终止性失败计数、日志异常不杀消费者、
  队列满不留假 merge key、并发生产者计数守恒、Stop/Dispose 并发安全、累计/末次 databaseCallMs、
  清理 stop 语义、清理 immediate stop 语义。
- **零解压复验**：`work_db_lock_fix2_verify_202621`（新 ZIP 全新解压）重新构建 + 55/55 通过
  （TRX：`test-output\fresh-extract.trx`）。

## 真实机器 IIS 验收（IIS 10 / w3wp / System.Data.SQLite / headed Chrome）

- 部署：高完整性管理员令牌 `S-1-16-12288`；站点 `GwDbLockFix2AcceptGw20260813`（业务 20060 / 管理台 20061）、
  后端 `GwDbLockFix2AcceptBk20260813`（20065）；应用池 `maxProcesses=1` + `disallowOverlappingRotation=true`；
  证据 `evidence/machine-iis-deploy.json`（healthz/admin/backend 全部 200，worker PID 46860）。
- 基础状态机（HTTP，12 项）：bootstrap/申请/登录/快照基线（p50=90ms）/40 请求读写并发 0 错误/批准
  103–2195ms/访问 ERP 登录页/撤销/设备被导向网关自己的“访问申请”页/重新申请/重新批准/恢复访问。
- **headed Chrome 状态机 11/11 通过**：真实 Chrome 完成 申请→批准→到达 ERP 主站登录页→撤销→
  设备被重定向到网关页面（不能再访问上游）→重新申请→重新批准→恢复 ERP 登录页；全程 0 console error、
  0 network failure；截图 `evidence/browser/`。
- 外部持锁矩阵（独立进程 `BEGIN IMMEDIATE`，真实批准写路径）：

| 持锁 | 结果 | 备注 |
|---|---:|---|
| 250 ms | 302，66 ms | 预算内恢复 |
| 1,100 ms | 302，666 ms | `DatabaseWriteLockRecovered` attempts=5 sqliteBusyCount=4 |
| 3,200 ms | 503 + `Retry-After: 1`，2,568 ms | `DatabaseWriteBudgetExceeded` |
| 16,000 ms | 503 + `Retry-After: 1`，2,197 ms | 同一短预算失败，不等满 16 秒 |
| JSON 契约 | 503 + 可解析 JSON（86 字节），无路径/SQL 泄露 | |

- **核心新增验收：审计压力 + 关键写并发 + 1100 ms 外部锁**（temptplan §10.4）：
  - 6 个隔离设备并发命中门户，62.7 秒内 **7,095 次非关键审计事件**（0 错误），命中延迟 p50=51 ms /
    p95=80 ms —— 生产者不等待审计落库；
  - 同时连续 **54 次关键操作**（申请/批准/撤销各 18 次，全部 302，**0 失败**）；
  - 关键写延迟 p50=63 ms（无锁时刻）、p95=1,424 ms / max=1,431 ms —— 完全由 1100 ms 外部锁
    busy→重试→恢复构成，**没有** 25/100/225 ms 阶梯式进程锁等待（`processLockWaitMs` 0–265 ms，
    与外部锁无关）；
  - 日志诊断实测新语义：`databaseCallMs=517; lastDatabaseCallMs=82`（累计 vs 末次）、
    `retryDelayMs=729`、`elapsedMs=1302 >= 0+729+549`；
  - 审计消费者 busy 耗尽时输出 `audit-queue-busy-exhausted`（attempts/batchItems/elapsedMs/queueDepth/
    requeued/dropped），8 条全部重排成功、0 丢弃；
  - w3wp 线程 63 / 句柄 630，无无界上涨；无 HTTP 500。
- recycle：审计积压 40 条后立即回收应用池 → healthz 200、新 worker 起来、回收前创建的待审请求
  回收后仍可批准、新请求可提交 —— 6/6 通过。
- 每轮 SQLite 不变量（`evidence/invariants-round2.json` 及 inspect 输出）：
  `integrity_check=ok`、`foreign_key_check` 0 行、无重复有效授权、撤销授权全部 RevokedAtUtc 落库、
  审计行数（written≈285 行，7,095 事件经 30 秒窗口合并）可解释。

## 清理结果

- 站点 `GwDbLockFix2AcceptGw20260813`/`GwDbLockFix2AcceptBk20260813` 与两个应用池已删除；
  端口 20060/20061/20065 无监听残留（`Get-NetTCPConnection` 计数 0）；无残留锁持有进程；
  未创建证书/计划任务。验收脚本保留在证据目录（未放入 Web 部署目录，ZIP 内无临时数据库/日志/凭据）。

## 已知未测风险

1. **同 AppDomain 内 runtime 重建**：xunit 非托管环境无法构造 `LegacyGatewayRuntime.Current`
   （依赖 HttpRuntime 宿主路径），该路径以代码评审（`Interlocked.CompareExchange` 原子清除 +
   构造函数覆写静态引用）+ 真实 IIS recycle（新 worker 使用全新 dispatcher，旧 worker 消失）覆盖；
   未做单元级 runtime 重建测试。
2. **浏览器状态机为单设备流程**：10.4 的多设备并发用 HTTP 客户端（6 设备）覆盖；headed Chrome
   只验证了完整状态机（11/11）与截图/console/network，未在浏览器内复跑多设备并发。
3. **IIS 验收期间宿主机 CPU 94%**（多个无关 python 进程）：快照/写延迟基线偏高（如并发 40 请求
   33.5 s 的一次异常慢轮），已记录；关键结论（退避不持锁、预算内恢复、短预算 503、计数守恒）不受影响，
   与“不接受稳定 1 秒空闲关键写”不冲突（空闲批准实测 66–2195 ms，均为锁注入/负载所致）。
4. 32/64 位互操作未在 IIS 上重测（x64 平台与既有验收一致）。
5. `GatewayDatabaseBusyException` 序列化/跨 AppDomain 传递未测（单 AppDomain 使用）。

## Definition of Done 核对

- [x] `TryWriteBatch` 所有 retry/yield/wait 均在释放 `DatabaseWriteSyncRoot` 后执行（barrier 测试 + 结构保证）
- [x] 确定性 barrier 测试证明退避期间关键线程可立即取得进程锁（探针 <100 ms）
- [x] 首次 terminal failure 必有日志，限频窗口行为正确（注入时钟测试）
- [x] terminal batch 的失败/丢弃计数准确（与批内条数一致）
- [x] queue full 不留下虚假 merge key（队列满→排空→同键真实入队）
- [x] `Stop(false)` 有界 drain；`Stop(true)` 真正立即取消（<500 ms 无 provider 场景；busy 场景 <1000 ms）
- [x] Stop/Dispose 并发无死锁、无 use-after-dispose（并发线程测试）
- [x] Runtime 清除旧 dispatcher 静态引用并可在同 AppDomain 重建（CompareExchange + IIS recycle）
- [x] `databaseCallMs`/`lastDatabaseCallMs` 含义与实测一致（单元 + IIS 日志）
- [x] 原 42 项测试全部执行通过（55/55）
- [x] 新增 13 项针对性测试全部通过
- [x] 机器 IIS 外部锁矩阵保持通过（250/1100/3200/16000 + JSON 契约）
- [x] 真实 Chrome 状态机无回归（11/11）
- [x] 1,000+ 非关键审计压力（7,095 次）不拖垮关键写（54/54 成功，延迟仅随外部锁增长）
- [x] 每轮 `integrity_check=ok`、无外键违规、无重复有效授权、撤销凭据不可用
- [x] recycle 后健康、会话和数据恢复
- [x] 新候选包从零解压可重编译和部署（55/55）
- [x] 验收资源已清理（站点/应用池/端口/锁进程）
- [x] 正式原包和上一候选包未被覆盖（SHA 复核一致）
