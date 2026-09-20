# GatewayDemo.Legacy 数据库锁修复第三轮落地报告

> 依据 `E:\验证页面\temptplan.md`（第三轮）实施。本轮从上一候选包 fixed2 零解压基线出发，
> 只修复 fixed2 独立验收发现的 Stop/Dispose 生命周期竞态（优雅停机无法升级为立即停机），
> 并完整证明数据库锁主修复没有回退。

## 候选包与哈希（三个旧包均未被覆盖）

| 包 | SHA-256 |
|---|---|
| 新候选 `GatewayDemo.Legacy-net472-db-lock-fixed3-20260814.zip` | `F43096A3203DA31815D895EB891E4C289B89BD2239AC197DD5A4916FAB191242` |
| 输入 fixed2 `GatewayDemo.Legacy-net472-db-lock-fixed2-20260813.zip` | `9B60C55F9D27EA91670DE411E348090E977111D6FAB0E9C1C319A5C52F02EEA5`（与计划一致，未覆盖） |
| 上一候选 fixed1 `GatewayDemo.Legacy-net472-db-lock-fixed-20260813.zip` | `6FBE7A00485DDDCEA4F78CC1D4C92EE404878F4929681B7173D8D35CD0850854`（未覆盖） |
| 正式原包 `GatewayDemo.Legacy-net472.zip` | `D35A9804DAE6BE3D55C18B98F5833B98E9364335F56495B60290C0F1BBF5FD9F`（未覆盖） |

主要工作目录与证据：

- 零解压基线：`E:\验证页面\work_db_lock_fix3_20260814_002353`（fixed2 ZIP 全新解压）
- 新 ZIP 复验目录：`E:\验证页面\work_db_lock_fix3_verify_20260814_003957`
- 生命周期探针：`E:\验证页面\output\gateway-db-lock-fixed3-lifecycle-probes-20260814_0112`
- IIS 验收：`E:\验证页面\output\gateway-db-lock-fixed3-acceptance-20260814_0118`
- 修改前哈希记录：`work_db_lock_fix3_20260814_002353\pre-modification-hashes.txt`

## 逐文件变更清单

| 文件 | 变更 |
|---|---|
| `src/GatewayDemo.Legacy.Web/Infrastructure/GatewayBestEffortAuditDispatcher.cs` | 拆分“首次停止所有权”与“立即取消请求”：`Stop(bool)` 先原子取得 `ownsInitialStop`，`immediate` 分支在任何提前 return 之前执行 `RequestImmediateStop()`（幂等：`Volatile.Write(immediateStopRequested,1)` + `immediateStopSignal.Set()`，捕获 `ObjectDisposedException`）；一次性清理（timer 停止、`CompleteAdding`）抽为 `StopCleanupTimerSafely`/`CompleteAddingSafely`，仅首次停止者执行；`JoinConsumerWithinBound(immediate)` 保证任意 `Stop(true)`（含后到的）执行短、有界 join（1,000 ms），重复 graceful stop 幂等返回。新增 internal 只读测试可观测属性 `IsStoppingForTest`/`IsImmediateStopRequestedForTest`（经 `InternalsVisibleTo` 暴露，不扩大 public API）。 |
| `tests/GatewayDemo.Legacy.Tests/AuditDispatcherTests.cs` | 新增 4 项确定顺序测试（见下文 §5）。 |
| `tests/GatewayDemo.Legacy.Tests/BestEffortAuditBackoffTests.cs` | 新增 2 项真实生产取消路径（不注入 delay）的退避期 graceful→immediate 与 Dispose→immediate 测试。 |
| `README.md` | 停机语义段补充：任何一次 `Stop(true)` 都会幂等提出立即取消请求，即使此前已有 `Stop(false)` 或 `Dispose()` 先取得停止状态，后到的 `Stop(true)` 仍会置位取消标志、触发停止信号并执行短而有界的等待。 |
| `GatewayDemo.Legacy.Tests.csproj` | 未修改（未新增测试文件）。 |

未修改：`LegacyGatewayRepository.cs`、`GatewayDatabaseBusyException.cs`、SQLite 连接串与 PRAGMA、`ProxyRequestModule.cs`、`LegacyGatewayRuntime.cs`、`Web.config` 与部署脚本、数据库 schema、APP bootstrap、WebAuthn、授权状态机与页面。

修改前关键文件 SHA-256（fixed2 基线）：

- `GatewayBestEffortAuditDispatcher.cs`：`E56DF48A82AE3228FC8443286D22E3B2B1724873DB7764D6FCC18BA231FED3BD`
- `AuditDispatcherTests.cs`：`FC57003F55E89A6027222DD10AFA27DABAB01053389CF6FF587CA29F81E06577`
- `BestEffortAuditBackoffTests.cs`：`4677000C690D2B6BD045A06D8B60E42D732B6EE3507E6A9648AE84BB76103D17`

## Stop 状态转换说明（fixed3）

状态仍只有 `stopping`/`immediateStopRequested`/`disposed` 三个 int 标志，未引入复杂枚举。语义分离如下：

1. `Stop(any)`：`Interlocked.Exchange(ref stopping,1)` 原子阻止新入队；返回值只决定**谁拥有一次性停机清理**（timer 停止、`CompleteAdding`）与首次 graceful drain 等待。
2. `immediate == true`：无条件执行 `RequestImmediateStop()`——该步骤位于任何提前 return 之前，因此无论 `stopping` 是否已被另一次 `Stop(false)`/`Dispose`/`Stop(true)` 置位，flag 与 signal 都必然被提出。
3. 后到的 `Stop(true)`：不拥有一次性清理，但执行短、有界 join（1,000 ms），与“立即停止返回后不再启动新写”的返回语义一致；后到的 `Stop(false)` 幂等直接返回。
4. 消费者所有等待点（flush 聚合、进程锁让出、SQLite busy 退避、重排冷却）都观察同一取消源（`immediateStopRequested`/`immediateStopSignal`），立即停机到达后当前批按 `DropAsShutdown` 丢弃、不再发起下一次 SQLite 写、不再启动新批次或新清理轮次。
5. `Dispose()` 仍先设置 `disposed` 再调用 `Stop(false)`；只有消费者退出后才释放 queue/signal/timer；`RequestImmediateStop` 对已释放 signal 捕获 `ObjectDisposedException` 保持幂等，因此 `Dispose` 进行中与完成后的重复 `Stop(true)` 均不抛异常、不 use-after-dispose。

## 为什么 fixed2 的 55 项测试漏掉该顺序

`StopAndDisposeConcurrentCallsAreSafe` 同时启动 4 个 stop/dispose 线程，线程启动顺序随机：任一 `Stop(true)` 先赢时缺陷不暴露；即使 `Stop(false)` 先赢，该测试也只断言“不超时、不抛异常、计数最终守恒”，没有断言后到 `Stop(true)` 是否把 `immediateStopRequested` 置 1、signal 是否真正打断进行中的优雅排空。因此 fixed2 自带测试无法抓到固定顺序 `Stop(false)→Stop(true)` 的升级缺失。

## 新增测试名称与确定同步方法

新增 6 项（总数 55 → 61）：

| 测试 | 同步方法 |
|---|---|
| `AuditDispatcherTests.GracefulStopCanBeUpgradedToImmediateDuringBusyBackoff` | 测试线程持有 `DatabaseWriteSyncRoot` 使消费者进入注入退避 barrier；`WaitUntil(IsStoppingForTest)` 证明 `Stop(false)` 已先取得停止状态；`Assert.False(gracefulReturned.Wait(0))` 证明其尚未返回；随后 `Stop(true)` 断言 flag、<1000 ms、graceful 调用在升级后 <1000 ms 提前返回、`WrittenCount==0`、停机丢弃=1、计数守恒、integrity ok。 |
| `AuditDispatcherTests.DisposeInProgressCanBeUpgradedByImmediateStop` | 同上屏障固定 `Dispose()` 先进入其内部 `Stop(false)`；断言 flag、双方有界返回、无 `ObjectDisposedException`、结束后重复 Stop/Dispose 幂等。 |
| `AuditDispatcherTests.RepeatedImmediateStopAfterGracefulIsIdempotent` | 固定 `Stop(false)` 先赢后 8 线程并发 `Stop(true)`，循环 50 次；每线程 <1 s 返回、flag=1、消费者退出、计数守恒、无异常。 |
| `AuditDispatcherTests.ImmediateStopAfterCompletedDisposeIsNoThrow` | 完全 Dispose 后重复 `Stop(true)/Stop(false)/Dispose` 与 `TryEnqueue` 不抛异常、不重启线程、`WrittenCount` 不增长。 |
| `BestEffortAuditBackoffTests.GracefulStopUpgradedToImmediateDuringRealSqliteBusyBackoff` | 真实外部 SQLite `BEGIN IMMEDIATE` + 真实生产 `immediateStopSignal.Wait` 退避路径（不注入 delay）；`IsStoppingForTest` 固定 graceful 先赢后 `Stop(true)`，断言 flag、<1000 ms、0 写入、停机丢弃、integrity ok。 |
| `BestEffortAuditBackoffTests.DisposeUpgradedToImmediateDuringRealSqliteBusyBackoff` | 同上真实路径的 Dispose→immediate 版本，另断言重复调用幂等。 |

注入退避回调显式观察 `IsImmediateStopRequestedForTest`（不依赖 `Thread.Sleep` 猜测调度），因此这些测试在 fixed2 旧实现上必然失败、在 fixed3 上必然通过。

## 独立生命周期探针对照（§8）

验收专用探针工程（固定顺序 + 反射检查 `stopping`/`immediateStopRequested`，源码对两个候选完全一致）：

| 目标 | 结果 | 证据 |
|---|---|---|
| fixed3 硬顺序 Stop 升级探针 ×100 | **100/100 通过** | `fixed3-probes.trx` |
| fixed3 Dispose 升级探针 ×50 | **50/50 通过**（合计 150/150，44 s） | `fixed3-probes.trx` |
| fixed2 同源负向探针 ×100 + ×50 | **150/150 失败**，失败信息全部为 `Assert.Equal() Failure: Expected: 1; Actual: 0`（与 fixed2 独立验收 `stop-upgrade-probe-v2` 的缺陷签名完全一致） | `fixed2-negative-probes.trx` |

验收门槛核对：100/100 通过；immediate flag 恒为 1；`Stop(true)` 全部 <1000 ms；graceful 调用在升级后 <1000 ms 返回；无 `ObjectDisposedException`；无计数丢失。未通过删除断言、放宽上限或改变调用顺序使测试变绿；负向探针证明同一探针在 fixed2 上稳定失败。

## 构建与全量测试（两轮）

工具链：`dotnet 10.0.201`；`MSBuild 17.14.23.42201`（`E:\Visual Studio BuildTools\MSBuild\Current\Bin\MSBuild.exe`）。

第一轮（工作目录，fixed2 零解压）：

- `MSBuild.exe src\GatewayDemo.Legacy.Web\GatewayDemo.Legacy.Web.csproj /t:Build /p:Configuration=Release /p:Platform=AnyCPU` → 退出码 0，0 error。
- `dotnet test tests\GatewayDemo.Legacy.Tests\GatewayDemo.Legacy.Tests.csproj -c Release` → **总数 61，通过 61，失败 0，跳过 0**，耗时 2 m 3 s。
- TRX：`work_db_lock_fix3_20260814_002353\test-output\round1-full.trx`。

第二轮（新候选 ZIP 全新解压，防漏打文件）：

- 全新解压 `work_db_lock_fix3_verify_20260814_003957` → Release rebuild → **总数 61，通过 61，失败 0，跳过 0**，耗时 2 m 8 s。
- TRX：`work_db_lock_fix3_verify_20260814_003957\test-output\fresh-extract.trx`。

编译/分析警告：0 error；32 条既有风格警告（2×CS0618 过时 SQLite BeginTransaction 重载 + 30×xUnit1031 阻塞 Task 风格提示），与 fixed2 同源，无新增错误类别。

fixed2 既有 55 项测试全部继续通过（61 = 55 + 6 新增），锁外退避 barrier、立即停机打断 busy 退避、优雅停机时限、首条失败日志/限频、LastFailure 追踪、终止性失败计数、队列满无假 merge key、并发生产者计数守恒、databaseCallMs 累计/末次、清理 stop 语义等修复均无回退。

## 机器 IIS 部署（§9）

- 高完整性管理员令牌 `S-1-16-12288`（`DESKTOP-79LK104\Eric`，Administrator=True）。
- 新站点/新应用池/新端口：`CodexDbLockFixed3Gw20260814`（业务 48720 / 管理台 48721）、`CodexDbLockFixed3Bk20260814`（mock 后端 48725）；应用池 `maxProcesses=1` + `disallowOverlappingRotation=true`；System.Web/w3wp；新候选全新解压部署 + 本地 mock upstream；新测试 SQLite 库（未复用 fixed2 验收库）。
- 部署自检 healthz=200、admin=200、backend=200。证据：`evidence/machine-iis-deploy.json`/`machine-iis-deploy.txt`。

## 外部 SQLite 写锁矩阵（§9.2，独立进程 BEGIN IMMEDIATE + 真实批准写路径）

| 外锁时长 | 结果 | 判定 |
|---:|---|---|
| 250 ms | 302，923 ms | 通过（预算内恢复） |
| 1,100 ms | 302，1,131 ms | 通过（预算内恢复） |
| 3,200 ms | 503，2,160 ms，`Retry-After: 1` | 通过（未等待完整外锁） |
| 16,000 ms | 503，2,135 ms，`Retry-After: 1` | 通过（不等 16 秒） |

日志诊断与 fixed2 语义一致：`DatabaseWriteLockRecovered`（attempts=2, sqliteBusyCount=1, databaseCallMs=160 ≥ lastDatabaseCallMs=28）；`DatabaseWriteBudgetExceeded`（attempts=7, sqliteBusyCount=7, retryDelayMs=1128, databaseCallMs=814, lastDatabaseCallMs=128, elapsedMs=2004 ≈ 2000 预算）；`processLockWaitMs=0`（无 25/100/225 ms 进程锁阶梯回退）。证据：`evidence/lock-matrix-results.json`。

## JSON 503 契约（§9.2）

5 秒外锁 + `Accept: application/json` + `X-Requested-With: XMLHttpRequest` 的真实写请求：

- 503，2,175 ms；`Content-Type: application/json; charset=utf-8`；`Retry-After: 1`；JSON 可解析；无 SQLite/异常类型/堆栈/源码路径/连接串泄露。
- 正文：`{"ok":false,"message":"数据库当前繁忙，请稍后重试。","requestId":"…"}`。
- 证据：`evidence/json-503-result.json`。

## 审计压力与关键写（§9.3）

在 1,100 ms 持锁/400 ms 释放循环外锁下，6 设备并发 + 关键操作循环：

- **5,000 次非关键审计请求，0 错误**；延迟 p50=65 ms、p95=120 ms、max=1,461 ms。
- **关键写 54/54 成功**（申请 18 / 批准 18 / 撤销 18，全部 302），0 失败；p50=80 ms、p95=1,450 ms、max=1,488 ms，延迟完全由 1,100 ms 外锁 busy→重试→恢复构成，无额外进程锁阶梯。
- 审计消费者 busy 耗尽诊断 `audit-queue-busy-exhausted`：`attempts=3; batchItems=1; elapsedMs=76; queueDepth=1; requeued=1; dropped=0`——重排成功、无静默丢弃。
- w3wp 压力期 80 线程/950 句柄，停止压力后回落到 67 线程/826 句柄，无无界上涨；无 HTTP 500。

限流说明：默认 `Gateway.RequestRateLimit.PermitLimit=1200`/`UnsafePermitLimit=240`/`Gateway.BrowserDevice.FirstContactPermitLimit=30`。首轮压力在默认新设备首触限流下产生 12 个设计内 429（同一 IP 反复创建新设备），该轮不作为数据库结论；随后仅在临时验收部署把三项限流临时提高到 1,000,000/1,000,000/100,000（原值与临时值记录于 `evidence/pressure-rate-limit-config.json`），最终专用压力轮如上 0 错误。候选 ZIP 内 `Web.config` 仍为默认值 1200/240/30（已复核），整个临时部署在清理时删除。

证据：`evidence/audit-pressure-results.json`（最终轮）、`pressure-final.log`、`evidence/application-logs/`。

## 真实 headed Chrome 状态机（§10）

真实 Chrome（headed，非 HTTP 模拟）完成 12/12：

1. 新设备访问 `/portal` 显示申请表；2. 填写企业/申请人/电话/原因并提交；3. 独立管理浏览器登录并批准；4. 设备到达 mock ERP 登录页（标题“ERP 主站 - 登录”）；5. 管理台撤销；6. 原设备再访问被导向网关自己的“访问申请”页（不能再访问上游）；7. 重新申请；8. 重新批准；9. 恢复 mock ERP 登录页；10-12. 设备 console error 0、network failure 0、管理台 console error 0。

证据：`evidence/browser/`（8 张 PNG 截图、关键请求状态 `key-responses.json`）、`evidence/browser-state-machine-results.json`。

## IIS recycle（§9.4，6 轮）

每轮：创建待审申请 → 40 次非关键审计命中 → `appcmd recycle apppool` → healthz 200 → 回收前待审申请仍存在 → 回收后批准 302 → 撤销授权供下一轮继续申请。**6/6 全部通过**（healthy、pendingRequestSurvived、approval=302、revoked 均真；每轮记录旧/新 worker）。证据：`evidence/recycle-result.json`/`recycle-console.txt`。

## 每轮数据库不变量（§11）

检查脚本覆盖：`integrity_check`、`foreign_key_check`、journal_mode=wal、token hash 唯一（浏览器凭据 + 恢复令牌）、设备/站点作用域最多一个有效授权、无有效授权设备不得留有可用旧 SessionKey/APP 凭据、已批准授权/申请引用的设备存在、授权站点引用的授权存在。五轮检查全部通过：

| 轮次 | 结果 | 证据 |
|---|---|---|
| 部署后基线 | 通过（全空库） | `invariants-0-baseline.json` |
| 锁矩阵 + JSON 契约后 | 通过 | `invariants-1-lockmatrix-json.json` |
| 压力后 | 通过（37 申请、36 撤销、1 有效授权，行数与操作数吻合） | `invariants-2-pressure.json` |
| recycle 后 | 通过 | `invariants-3-recycle.json` |
| 浏览器后（最终） | 通过（51 申请、49 撤销、2 有效授权，与 18+18+18 压力、12 轮 recycle、浏览器 2 申请/2 批准/1 撤销的逐行对应一致） | `invariants-4-browser-final.json` |

操作成功数 → 数据库行变化可逐项对应（提交 302 数 = 新增申请行数；批准 302 数 = 状态翻转为已批准；撤销 302 数 = `RevokedAtUtc` 落库数）。审计行数受 30 秒同键合并窗口约束（5,000+ 事件 → 280 行），合并语义与 fixed2 一致、无静默丢失（`requeued=1; dropped=0`，非关键请求 0 错误）。

## 资源清理结果（§12）

- 站点/应用池 `CodexDbLockFixed3Gw20260814`、`CodexDbLockFixed3Bk20260814` 及两个池已删除（list 计数 0）。
- 端口 48720/48721/48725 监听数 0。
- 锁持有进程与 `codex-fixed3-*.flag` 临时文件已清理；本次浏览器会话结束后无新增 Chrome 残留（本机存在的历史 Chrome 进程均早于本次验收）。
- 证据：`evidence/iis-cleanup.json`、`evidence/iis-state-before-cleanup.json`。

## ZIP 交付与卫生（§12）

- 新候选 ZIP 长度 21,715,836 字节，SHA-256 见上；打包后再次零解压复验 61/61。
- ZIP 内不含：验收数据库/WAL/SHM、运行日志（含修复 fixed2 包内泄漏的 `tests\bin\...\App_Data\logs\*.jsonl`）、TRX/测试运行输出、本机绝对路径记录、临时管理员密码、本次 IIS 站点配置、锁持有/UAC 脚本、验收输出目录、截图或 AI 审查措辞（扫描 0 命中）。
- 测试源码保留在包内（沿用现有包约定）；TRX 不随包交付（与 fixed2 一致）。

## Definition of Done 核对

- [x] `Stop(false)` 先赢后，`Stop(true)` 仍将 immediate flag 置为 1 并 Set signal（单元测试 + 探针 100/100）
- [x] `Dispose()` 先进入 graceful stop 后，`Stop(true)` 仍可升级立即取消（单元测试 + 探针 50/50）
- [x] 后到的 `Stop(true)` 使用短、有界 join，而不是直接返回（`JoinConsumerWithinBound(true)`，1000 ms 上限）
- [x] graceful 调用在升级后随消费者退出提前返回（barrier 测试断言 <1000 ms，而非完整 graceful deadline）
- [x] 无 provider 场景 `Stop(true)` <500 ms（既有 `ImmediateShutdownDoesNotDeadlock` 收紧断言继续通过）；busy/provider 场景 <1,000 ms（新增测试 + 探针）
- [x] immediate 返回后不再启动新 SQLite 写、重排或清理轮次（结构检查 + `WrittenCount` 停止增长断言）
- [x] 重复/并发 Stop/Dispose 不死锁、不抛异常、不 use-after-dispose（50 次循环 ×8 线程 + 既有并发测试 + Dispose 后重复调用测试）
- [x] 所有审计记录计数守恒（新增测试断言 enqueued+requeued ≥ written+depth+failed+shutdown，停机丢弃逐条可解释）
- [x] fixed2 独立失败探针保持能稳定抓到旧缺陷（同源探针 fixed2 150/150 失败，`Expected: 1; Actual: 0`）
- [x] fixed3 graceful→immediate 探针 100/100 通过
- [x] fixed3 Dispose→immediate 探针 50/50 通过
- [x] 新增至少 4 项确定性测试，总测试数至少 59（新增 6 项，总数 61），全部通过
- [x] 新 ZIP 从零解压可 Release rebuild，全部测试再次通过（61/61）
- [x] fixed2 已通过的锁外退避、失败日志、计时和 merge key 修复无回退（既有 55 项全通过 + IIS 日志诊断复核）
- [x] 机器 IIS 外锁矩阵 4/4 通过
- [x] JSON 503 契约通过且不泄露内部信息
- [x] 至少 1,000 次非关键请求与至少 50 次关键写并发通过（5,000 次 0 错误 + 54/54）
- [x] 真实 headed Chrome 授权状态机通过，console/network 无非预期错误（12/12）
- [x] IIS recycle 后健康、数据保留、业务可继续（6/6）
- [x] 每轮数据库一致性和授权不变量通过（5 轮）
- [x] fixed3 为新文件，三个旧 ZIP 哈希均未改变
- [x] 临时 IIS 站点、应用池、端口、浏览器和锁进程全部清理

## 已知未测风险

1. **同 AppDomain 内 runtime 重建**：与 fixed2 相同，xunit 非托管环境无法构造 `LegacyGatewayRuntime.Current`（依赖 HttpRuntime 宿主路径）；本轮未改 `LegacyGatewayRuntime.cs`，该路径延续 fixed2 的代码评审 + 真实 IIS recycle 覆盖，未做单元级 runtime 重建测试。
2. **浏览器状态机为单设备流程**：多设备并发用 HTTP 客户端（6 设备压力）覆盖；headed Chrome 只验证了完整状态机（12/12）与截图/console/network，未在浏览器内复跑多设备并发。
3. **Dispose 升级路径的极端竞态窗口**：`RequestImmediateStop` 的 `Set()` 与 Dispose 释放 signal 之间的窗口以捕获 `ObjectDisposedException` 处理；该竞态窗口极小且被 50 次 Dispose 探针反复穿越未出现未处理异常，但理论上未对“Set 与 Dispose 同时发生”的内存顺序做独立压力证明。
4. **审计行数与事件数只能事后解释**：30 秒同键合并窗口使审计行数必然小于事件数；进程内计数（written/shutdownDropped/requeued）无法在 w3wp 退出后外部核对，以 0 错误、日志诊断（requeued/dropped）与五轮 integrity 作为证据，未做进程内计数器外部审计。
5. **32/64 位互操作**：x64 平台与既有验收一致，未在 32 位重测。
6. **`GatewayDatabaseBusyException` 序列化/跨 AppDomain**：单 AppDomain 使用，未测（与 fixed2 相同）。
