# GatewayDemo.Legacy SQLite 锁、数据库调用硬预算与请求体中止修复计划（fixed4）

更新时间：2026-08-14  
输入候选：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472-db-lock-fixed3-20260814.zip`  
输入 SHA-256：`F43096A3203DA31815D895EB891E4C289B89BD2239AC197DD5A4916FAB191242`  
独立验收证据：`E:\验证页面\output\gateway-db-lock-fixed3-independent-acceptance-20260814-012014`  
目标候选：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472-db-lock-fixed4-20260814.zip`

---

## 1. 当前结论

fixed3 针对 `GatewayBestEffortAuditDispatcher` 的 Stop/Dispose 升级竞态已经修复并通过独立反证：

- fixed3 硬顺序升级探针：150/150 通过；
- 相同测试源运行 fixed2：0/150，通过失败签名证明探针具备区分力；
- 外部写锁 250/1100/3200/16000 ms 矩阵：4/4 通过；
- JSON 503、IIS recycle 6/6、真实 headed Chrome、最终 SQLite 不变量均通过。

但 fixed3 不能作为最终交付，独立机器 IIS 验收及研发现场日志发现三个 P1 和一个 P3：

1. **P1：读路径打开 SQLite 连接时执行组合 PRAGMA，遇到外部写锁会直接抛 `SQLiteException(Busy)`，最终成为 HTTP 500。**
2. **P1：配置为 2000 ms 的写预算不是硬边界；一笔 provider 调用实际耗时 11210 ms。**
3. **P3：ZIP 内的 `obj/Release/*FileListAbsolute.txt` 泄露生成机绝对路径。**
4. **P1：客户端上传请求体期间断开或应用回收时，`HttpRequest.InputStream` 可抛出内层为 `0x800703E3` 的 `HttpException`；代理只捕获 `WebException`，导致预期的 I/O 取消穿透为全局 HTTP 500。**

本轮只解决这四项，并完整回归 fixed1/fixed2/fixed3 已经修复的授权、移动 APP、审计队列和生命周期行为。不要趁机重写仓储层、切换数据库、替换整个反向代理或改变业务状态机。

---

## 2. 独立复现证据（必须先纳入落地报告）

### 2.1 读路径裸 500

在真实机器 IIS、6 个设备、1100 ms 持锁/400 ms 释放的外部 SQLite 写锁循环中，应用日志出现两次：

```text
GET /proxy/demo/portal
HTTP 500
System.Data.SQLite.SQLiteException
ResultCode=Busy
ErrorCode=5
database is locked
```

关键调用栈：

```text
SQLiteConnectionConfigurator.ConfigureOpenConnection
LegacyGatewayRepository.OpenConnection
LegacyGatewayRepository.HasRevokedAuthorization
ProxyRequestModule.HandleRequest
```

证据：

```text
output\gateway-db-lock-fixed3-independent-acceptance-20260814-012014\evidence\pressure-500-records.json
```

两个 500 均发生在正常 GET 读/鉴权路径，并非故意注入的 503 场景；因此不能解释为“设计内失败”。

### 2.2 写预算被穿透

同一轮压力记录：

```text
source=LegacyGatewayRepository.DatabaseWriteSlow
operation=<CreateOrUpdateRequestAsync>b__0
outcome=succeeded
attempts=1
sqliteBusyCount=0
processLockWaitMs=0
databaseCallMs=11210
lastDatabaseCallMs=11210
elapsedMs=11210
writeBudgetMs=2000
```

证据：

```text
output\gateway-db-lock-fixed3-independent-acceptance-20260814-012014\evidence\pressure-slow-records.json
```

这证明当前代码只在 provider 调用前后检查预算；一旦进入单次同步 SQLite 调用，2000 ms 配置无法中断它。

### 2.3 原压力统计器存在假阴性

原负载调用 `HttpClient.GetAsync()` 后无条件增加 `AuditHits`。`HttpClient` 对 HTTP 500 不会自动抛异常，因此日志虽然有两个 500，脚本仍报告：

```text
auditErrors=0
```

本轮所有负载统计必须按 HTTP 状态码分类，不能再用“是否抛传输异常”代替业务成功。

### 2.4 ZIP 绝对路径

fixed3 ZIP 的以下文件包含至少 130 行生成机路径：

```text
src/**/obj/Release/*.FileListAbsolute.txt
tests/**/obj/Release/*.FileListAbsolute.txt
E:\验证页面\work_db_lock_fix3_20260814_002353\...
```

证据：

```text
output\gateway-db-lock-fixed3-independent-acceptance-20260814-012014\evidence\delivery-hygiene-scan.txt
```

---

## 3. 本轮目标与硬性不变量

### 3.1 目标

1. 正常读路径在 SQLite 外部锁下不得出现 HTTP 500。
2. `Busy/Locked/Interrupt` 必须统一分类、诊断，并按路径选择预算内重试或安全 503。
3. 2000 ms 写预算必须覆盖：进程锁等待、连接打开、连接配置、schema 检查、事务开始、SQL、COMMIT、provider busy wait 和应用退避。
4. 单次 provider 调用必须可以在预算到期时请求取消，不能无限阻塞。
5. 保证“已经 COMMIT 的业务操作不返回失败”，避免客户端重试造成重复批准/撤销。
6. 状态码感知压力至少连续 5 轮无意外 5xx，无预算穿透。
7. fixed3 生命周期修复不得回退。
8. ZIP 不包含 `obj`、绝对路径、运行数据库、日志、TRX、验收密码或临时 IIS 配置。
9. 客户端在请求体上传期间 FIN/RST、取消请求或遇到应用回收时，必须快速终止对应上游请求，不得进入 `Global.Application_Error`，不得伪装成网关 500/502，也不得重试有正文的非幂等请求。

### 3.2 不变量

```text
read path + external writer => success or bounded 503, never raw 500
write wall time <= configured budget + 500 ms scheduling tolerance
no successful COMMIT is reported as failure
no failed/aborted operation leaves partial business state
no retry or cancel while holding DatabaseWriteSyncRoot during sleep
one token hash maps to at most one device
one device/site scope has at most one effective authorization
revoked credentials/session/nonces remain unusable
foreign_key_check returns zero rows
integrity_check returns ok
journal_mode remains wal
client body abort => upstream aborted, no retry, no global 500, no response write
unrelated HttpException/COMException => never swallowed by the abort classifier
```

---

## 4. 先诊断后修改：定位具体阻塞 phase

不要只根据 `databaseCallMs=11210` 猜测是哪条 SQL。先在 copied DB 上增加可测试、结构化的 phase 计时。

### 4.1 必须拆分的 phase

每个数据库尝试至少记录：

```text
connectionCreateMs
connectionOpenMs
configureBusyHandlerMs
configureForeignKeysMs
configureSynchronousMs
configureWalAutocheckpointMs
schemaCheckMs
beginTransactionMs
commandMs
commitMs
rollbackMs
connectionCloseMs
```

写执行器还必须记录：

```text
operation
phase
attempts
sqliteBusyCount
sqliteLockedCount
sqliteInterruptCount
processLockWaitMs
retryDelayMs
databaseCallMs
lastDatabaseCallMs
remainingBudgetBeforeMs
remainingBudgetAfterMs
cancelRequested
cancelObserved
commitCompleted
budgetOverrunAfterCommit
providerResultCode
providerErrorCode
```

### 4.2 基线探针

在改 PRAGMA 前，对四条语句分别运行，不得继续作为一条 SQL batch：

```sql
PRAGMA busy_timeout = 50;
PRAGMA foreign_keys = ON;
PRAGMA synchronous = NORMAL;
PRAGMA wal_autocheckpoint = 1000;
```

矩阵：

1. 无外锁；
2. `BEGIN IMMEDIATE` 持锁 250 ms；
3. 持锁 1100 ms；
4. 持锁 5000 ms；
5. 新物理连接，`Pooling=False`；
6. 连接池首次连接；
7. 池复用连接；
8. IIS recycle 后首个连接；
9. 32 个连接并行打开。

每个 phase 保存耗时、SQLite result code 和是否需要数据库锁。落地报告必须写明 fixed3 的两次 500 究竟卡在哪一条 PRAGMA；不能只写“已拆分所以修复”。

---

## 5. 解决 P1：重新设计连接初始化和 PRAGMA

### 5.1 禁止每次打开连接执行组合 PRAGMA batch

删除当前模式：

```csharp
command.CommandText = @"
PRAGMA foreign_keys = ON;
PRAGMA synchronous = NORMAL;
PRAGMA wal_autocheckpoint = 1000;
PRAGMA busy_timeout = 50;";
command.ExecuteNonQuery();
```

原因：

- 无法知道哪条语句返回 BUSY；
- `busy_timeout` 在最后设置，之前的 prepare/step 不受其保护；
- `PRAGMA busy_timeout` 会替换该连接已有 busy handler，不能同时依赖多个机制；
- `wal_autocheckpoint=1000` 本来就是 SQLite WAL 的默认阈值，热路径重复设置无收益；
- 在纯读鉴权路径执行可产生锁竞争的配置语句，会把本应可并发的 WAL 读取变成 500 风险。

### 5.2 配置职责分层

#### A. 数据库级、只在初始化互斥内执行

```sql
PRAGMA journal_mode = WAL;
```

要求：

- 只在应用启动/显式数据库初始化中执行；
- 使用独立 5000 ms 初始化预算；
- 不能在请求路径自动切换 journal mode；
- 初始化成功后缓存 schema/version 状态。

#### B. 每个物理连接需要的选项

优先用 `SQLiteConnectionStringBuilder` 的强类型属性设置，并对 System.Data.SQLite 1.0.118 的实际行为写组件测试：

```text
Foreign Keys=True
Synchronous=Normal（使用该 provider 的准确属性名）
BusyTimeout=50
DefaultTimeout=0
DefaultMaximumSleepTime=0
PrepareRetries=0
StepRetries=0
```

要求：

- coding agent 必须通过该版本 `System.Data.SQLite.xml`/类型反射确认属性名，不能手写未经验证的连接串键；
- 连接打开后查询 `PRAGMA foreign_keys`、`PRAGMA synchronous`、`PRAGMA busy_timeout` 验证实际值；
- 验证池复用后仍保持一致；
- 只能选择一种 busy handler 配置方式。若连接串已设置 `BusyTimeout`，不要再无条件执行 `PRAGMA busy_timeout` 覆盖它。

#### C. `wal_autocheckpoint`

默认保持 1000 页即可，不在每个连接上重复设置。若产品必须显式配置：

- 只在受控初始化或明确的后台 checkpoint 组件中配置；
- 查询并记录最终值；
- 不允许请求线程触发 FULL/RESTART/TRUNCATE checkpoint；
- PASSIVE checkpoint 也必须有独立低优先级预算。

SQLite 官方说明：自动 checkpoint 默认阈值为 1000 页，达到阈值的 COMMIT 可能承担 checkpoint 成本；如需避免请求线程偶发慢写，可禁用自动 checkpoint 并由后台低优先级线程在空闲期执行 PASSIVE，但本轮只有在证据确认 11.2 秒来自 checkpoint 时才允许改变策略。

### 5.3 schema 检查移出每次 OpenConnection

当前每次 `OpenConnection()` 后调用 `GatewayDatabaseInitializer.HasCurrentSchema(connection)`，属于请求热路径额外 prepare/read。

改为：

1. `Application_Start`/runtime 初始化时完成 schema 校验；
2. 成功后以 AppDomain 级不可变状态记录 schema 版本；
3. 请求路径仍检查数据库文件是否存在，但不重复遍历 schema；
4. healthz 可以报告缓存版本和一次显式只读校验结果；
5. IIS recycle 会重新执行初始化，因此数据库被替换/损坏不会永久隐藏；
6. 若系统确实支持运行中替换 DB，必须设计显式 reload，而不是每个请求自行修复。

### 5.4 读路径统一 BUSY 转换

新增或重构统一的 `ExecuteDatabaseRead`：

```text
ReadBudgetMs：建议 500 ms，可配置，最大不得超过 WriteBudgetMs
BusyTimeoutMs：默认 50 ms
最多尝试：按剩余预算计算
退避：10/25/50/100 ms + 小抖动
退避期间不持有写进程锁
```

覆盖所有请求路径读取：

- `HasRevokedAuthorization`；
- 浏览器 Cookie/恢复凭据验证；
- 移动 APP SessionKey/HMAC/nonce 验证；
- 设备状态、授权站点查询；
- 管理台快照；
- bootstrap 必要读取（匿名 bootstrap 原则上应为零数据库访问）；
- ProxyRequestModule 进入上游前的授权读取。

分类规则：

```text
Busy/Locked + 预算仍有剩余 => 有界重试
Busy/Locked + 预算耗尽 => GatewayDatabaseBusyException
Interrupt + 本次预算 guard 已请求取消 => GatewayDatabaseBusyException
Interrupt + 非本次 guard => 保留原异常并记录来源
Corrupt/NotADb/IoErr/Full/ReadOnly => 不得伪装成 busy，按真实故障处理
```

HTTP 契约：

- 普通 HTML：503、`Retry-After: 1`、安全中文提示；
- Ajax/JSON：503、合法 JSON、不含 SQLite/堆栈/文件路径；
- 移动 APP 契约：保持原生 APP 可识别格式，但状态必须是临时数据库繁忙，不得误报“设备尚未获批”或“会话已失效”；
- 鉴权读失败时 fail closed，不得绕过授权进入上游；
- 任何 Busy/Locked 都不得落到 Global 500。

---

## 6. 解决 P1：让 2000 ms 写预算覆盖单次 provider 调用

### 6.1 预算定义

`Gateway.Database.WriteBudgetMs=2000` 定义为从业务写调用进入执行器到明确结束的总 wall-clock 预算，覆盖：

```text
等待 DatabaseWriteSyncRoot
connection.Open
连接初始化/校验
BEGIN IMMEDIATE
所有 SQL prepare/step
COMMIT 或 ROLLBACK
BusyTimeout
应用层 retry delay
连接收尾（仅诊断，不允许无限等待）
```

HTTP 允许 500 ms 宿主调度/序列化容差，验收门槛为：

```text
写相关 HTTP wall time <= WriteBudgetMs + 500 ms
```

不能把 11.2 秒“最终成功”解释为合格。

### 6.2 使用 provider 支持的跨线程取消

交付包所带 `System.Data.SQLite.xml` 明确说明：

```text
SQLiteCommand.Cancel(): Not implemented
SQLiteConnection.Cancel(): 可从任意线程调用，使当前数据库操作尽早中止；
前提是调用时连接保持打开，且 Cancel 返回前连接不能关闭。
```

因此不要使用 `SQLiteCommand.Cancel()`；实现 `SQLiteConnectionBudgetGuard`（名称可调整）：

1. 构造时接收已打开的 `SQLiteConnection`、绝对 deadline、operation/phase；
2. 使用 `Timer` 或专用 watchdog，在 deadline 到达时调用 `SQLiteConnection.Cancel()`；
3. `Cancel()` 回调只设置标志并中断 provider，不执行日志写库或任何重入数据库操作；
4. guard 的 `Dispose()` 先停止计时器，再等待已经开始的 callback 完成；
5. guard 必须在 `SQLiteConnection.Dispose/Close` 之前 Dispose，避免关闭与 Cancel 并发；
6. callback、Dispose、异常和 Stop(true) 必须幂等；
7. 不允许用 `Thread.Abort`；
8. 不允许把同步 SQLite 工作丢到后台 Task，超时后任其继续写库；
9. 不允许超时返回 503 后让后台事务稍后 COMMIT。

推荐结构：

```csharp
using (var connection = OpenConnectionWithBudget(context))
using (var guard = SQLiteConnectionBudgetGuard.Arm(connection, context.Deadline))
using (var transaction = BeginWriteTransaction(connection, context))
{
    // SQL
    transaction.Commit();
    context.MarkCommitted();
}
```

必须静态检查全部 27 个写事务/写调用，保证没有写路径漏挂 guard。可通过统一 helper 减少遗漏，但不要用不透明 ThreadStatic 隐藏生命周期。

### 6.3 connection.Open 的特殊处理

`SQLiteConnection.Cancel()` 不能安全取消尚未打开的连接。因此：

1. `connection.Open()` 单独计时并记录 `connectionOpenMs`；
2. 连接串 busy/retry 配置必须确保 Open 不进行长时间 provider 重试；
3. Open 返回后若预算已耗尽，立即关闭并抛 busy，不进入业务事务；
4. 不要为 Open 创建一个超时后遗弃的后台线程；
5. 若真实测试仍证明 Open 可超过预算，必须评估 `Pooling=False`、连接池预热或 provider/native 版本能力，不能用日志掩盖；
6. 验收要求 cold AppDomain + 外锁场景的 `connectionOpenMs` 最大值也在预算内。

### 6.4 CommandTimeout 语义

System.Data.SQLite 的 `CommandTimeout` 单位是秒，`0` 通常表示无限，不是“立即失败”。因此：

- 删除把 `CommandTimeout=0` 当作毫秒短预算的注释或假设；
- Busy/Locked 主要由 50 ms native busy handler + 应用总预算控制；
- 对可能长查询，使用 connection-level Cancel guard；
- 若设置 CommandTimeout，只能按剩余预算向上取整为至少 1 秒，并明确它是第二层保护，不替代总预算；
- 单测必须覆盖 prepare、step、commit，而不是只测一个 ExecuteNonQuery。

### 6.5 超时与 COMMIT 竞态

必须区分以下结果：

1. **取消发生在 COMMIT 前，事务回滚成功**：返回 503；
2. **COMMIT 明确成功，随后才观察到 deadline**：返回业务成功，同时输出 `DatabaseWriteBudgetOverrunAfterCommit`；不得返回 503 诱导重复提交；
3. **COMMIT 结果不确定**：根据业务幂等键查询最终状态后决定响应；不能盲目重试；
4. **取消后 rollback 也失败**：关闭连接、禁止回池、记录高优先级错误，并通过一致性查询确认状态。

批准、撤销、申请、凭据轮换必须有天然幂等键或唯一约束，重复请求不能生成两个有效授权。

### 6.6 重试算法

保留 fixed2 已通过的锁外退避原则：

```text
remaining = deadline - now
provider busy <= min(50 ms, remaining)
retry delay = min(25/100/225/300 ms + jitter, remaining)
所有 delay 在 Monitor.Exit 后执行
进入下一次尝试前再次检查 remaining
```

禁止：

- 持有 `DatabaseWriteSyncRoot` 睡眠；
- 单次 provider timeout 大于剩余总预算；
- 达到预算后再开始新 attempt；
- 把 `TimeoutException`、`Interrupt`、`Busy` 混成 500；
- 在重试中重复插入关键审计或重复业务副作用。

---

## 7. checkpoint 与 11.2 秒离群点

SQLite 官方文档说明：WAL 默认约 1000 页触发自动 checkpoint，触发阈值的 COMMIT 可能比其它 COMMIT 慢；`synchronous=NORMAL` 下 checkpoint 承担同步 I/O。

本轮处理顺序：

1. 先用 phase 诊断确认 11210 ms 是否发生在 `connection.Open`、PRAGMA、BEGIN、SQL 或 COMMIT；
2. 只有确认是 checkpoint 后，才允许调整 checkpoint 策略；
3. 若调整，建议：
   - 请求连接不执行手工 checkpoint；
   - 后台单消费者在低优先级、无关键写时执行 PASSIVE；
   - 每轮有短预算，BUSY 即让出；
   - 不在请求高峰执行 FULL/RESTART/TRUNCATE；
   - 记录 WAL 页数、checkpointed 页数、busy 结果和耗时；
4. WAL 不能无限增长；设告警阈值并在验收中跑足够写入观察收敛；
5. 不得为了降低延迟把 `synchronous` 改成 OFF。

参考官方资料：

- SQLite WAL：https://www.sqlite.org/wal.html
- PRAGMA：https://www.sqlite.org/pragma.html
- busy timeout：https://sqlite.org/c3ref/busy_timeout.html
- busy handler：https://www.sqlite.org/c3ref/busy_handler.html

---

## 8. 诊断事件要求

### 8.1 新增/完善事件

```text
DatabaseConnectionPhaseSlow
DatabaseReadLockRecovered
DatabaseReadBudgetExceeded
DatabaseWriteLockRecovered
DatabaseWriteBudgetExceeded
DatabaseWriteBudgetOverrunAfterCommit
DatabaseProviderCancelRequested
DatabaseProviderCancelObserved
DatabaseProviderCancelFailed
DatabaseCheckpointRound
```

### 8.2 日志规则

- 第一条失败必须记录；
- 同 source/operation/phase 后续可按 5 秒窗口压缩；
- 压缩不能丢失总次数、first/last timestamp、最大值；
- 不把 expected 503 记录为 Global 500；
- 不记录连接串、数据库绝对路径、SessionKey、HMAC、Cookie、管理员密码；
- 日志写入不得再次依赖当前被锁数据库形成递归故障；
- phase 诊断写文件失败不得影响主业务响应。

---

## 9. 单元和组件测试

### 9.1 PRAGMA/连接配置（至少 20 项）

1. 四条 PRAGMA 不再以 batch 执行；
2. busy handler 在任何可能访问数据库的配置前已生效；
3. `foreign_keys=1`；
4. `synchronous=NORMAL`；
5. `busy_timeout=50`；
6. `journal_mode=wal`；
7. `wal_autocheckpoint` 为预期值；
8. 新物理连接正确；
9. 池复用连接正确；
10. 并行 32 连接正确；
11. 250/1100/5000 ms 外锁下逐条 PRAGMA 有明确结果；
12. 读连接不会因连接配置抛未转换 Busy；
13. schema 校验只在 runtime 初始化执行一次；
14. recycle 后重新校验；
15. 缺库仍 fail closed；
16. schema 版本错误仍 fail closed；
17. 连接配置异常释放连接；
18. 故障连接不返回池；
19. BusyTimeout 只有一个配置来源；
20. 默认配置与 README 一致。

### 9.2 BudgetGuard（至少 20 项）

1. deadline 前完成不调用 Cancel；
2. deadline 到达恰好调用一次 connection Cancel；
3. Cancel callback 与 Dispose 并发不关闭竞态；
4. Dispose 等待已开始 callback；
5. 重复 Dispose 幂等；
6. 重复 cancel 幂等；
7. cancel 后 SQLite Interrupt 正确映射；
8. 非 guard 产生的 Interrupt 不被误分类；
9. COMMIT 前取消回滚；
10. COMMIT 后 deadline 返回成功并记录 overrun；
11. rollback 失败关闭连接；
12. budget=1 ms；
13. budget=2000 ms；
14. budget 极大值有上限；
15. 配置为 0 的既有语义保持明确；
16. Stopwatch 使用单调时间；
17. 进程锁等待计入预算；
18. retry delay 计入预算；
19. provider phase 累计值与 wall time 守恒；
20. 不产生后台孤儿任务/线程。

### 9.3 真实 provider 组件测试

必须使用交付包同版本 System.Data.SQLite/native interop，不得只用 fake：

1. 长递归 CTE/大查询被 `SQLiteConnection.Cancel()` 中断；
2. 外部 `BEGIN IMMEDIATE` 16 秒时，写请求约 2 秒结束；
3. 1100/400 ms 周期锁至少 100 次写，max 不超过 2500 ms；
4. 5000 ms 外锁下 1000 次读，没有裸 500 等价异常；
5. Pooling=True/False 各跑；
6. Cancel 后连接不复用或经验证安全复用；
7. 数据无部分写；
8. `integrity_check=ok`；
9. 外键 0；
10. 无重复有效授权。

### 9.4 fixed3 回归

- 原 61 项全部通过；
- Stop(false)→Stop(true) 100 次；
- Dispose→Stop(true) 50 次；
- fixed4 150/150；
- 不需要重新制造 fixed2 包，但必须保留 fixed2 0/150 的既有反证证据；
- 审计队列锁外退避、首条日志、立即停止、merge key、累计/末次 databaseCall 语义均不得回退。

---

## 10. 真实机器 IIS 验收

### 10.1 环境

- 只部署新 fixed4 ZIP 全新解压目录；
- 机器 IIS、真实 w3wp、管理员高完整性令牌；
- 唯一站点名、应用池名和端口；
- 只使用交付 mock backend；
- 只使用 copied test DB；
- 应用池默认 `maxProcesses=1`；
- 禁止访问生产 ERP、生产数据库和正式域名；
- 保存 IIS 配置、PID、AppDomain、组件版本、SQLite/native 版本。

### 10.2 cold AppDomain 读锁专项

独立验收中的 500 出现在 w3wp 启动约 27 秒后，因此必须覆盖冷启动：

每轮：

1. recycle 应用池；
2. 等待 healthz；
3. 启动 1100/400 ms 外锁循环；
4. 立即由 6 个独立设备并发访问代理入口；
5. 至少 1000 次状态码感知请求；
6. 保存每个 HTTP 状态、延迟、requestId 和对应日志；
7. 检查数据库不变量。

至少 6 轮。门槛：

```text
HTTP 500 = 0
unhandled SQLite Busy/Locked = 0
允许的 503 必须有 Retry-After 和安全响应
没有鉴权绕过
```

### 10.3 外锁矩阵

对读路径和写路径分别跑：

| 外锁 | 预期 |
|---|---|
| 250 ms | 预算内恢复，不得 500 |
| 1100 ms | 预算内恢复，不得 500 |
| 3200 ms | 写路径约 2 秒返回 503；读路径成功或有界 503 |
| 16000 ms | 不等待满锁，约 2 秒返回 503 |

所有 503：

```text
Retry-After: 1
HTML/JSON/mobile 契约正确
无 SQLite/异常/堆栈/绝对路径
```

### 10.4 状态码感知压力

修正负载统计器：

```csharp
var status = (int)response.StatusCode;
statusCounts.AddOrUpdate(status, 1, ...);
if (status >= 500) serverErrors++;
if (status == 503 && scenarioExpectedBusy) expectedBusy++;
```

每轮：

- 6 个独立设备；
- 1100 ms 持锁/400 ms 释放；
- 至少 5000 个非关键代理/审计请求；
- 54 个关键写（申请、批准、撤销各 18）；
- 保存 p50/p95/p99/max；
- 保存全部 5xx 的 body/requestId；
- 保存 phase max 和 budget overrun 数；
- 压力结束后等待队列 drain，再记录线程/句柄；
- 检查数据库不变量和审计计数等式。

至少连续 5 轮，总计至少：

```text
25000 非关键请求
270 关键写
```

门槛：

```text
unexpected HTTP 5xx = 0
raw HTTP 500 = 0
critical success = 100%
audit/other p95 <= 250 ms
critical p95 <= 1600 ms（1100 ms 外锁条件）
critical max <= 2500 ms
database phase max <= 2000 ms + 500 ms tolerance
budgetOverrunAfterCommit = 0
orphan background work = 0
```

若默认安全限流干扰，允许只修改临时 IIS 部署的：

```text
Gateway.RequestRateLimit.PermitLimit
Gateway.RequestRateLimit.UnsafePermitLimit
Gateway.AccessRequest.PermitLimit
```

必须保存原值和临时值；不能改候选默认配置来“通过”压力。清理时删除整个临时部署。

### 10.5 多进程专项

尽管正式默认 `maxProcesses=1`，仍需验证进程内锁不能掩盖跨进程 SQLite 行为：

方案任选其一，但必须写入报告：

1. 两个独立 IIS 应用指向同一 copied DB；或
2. 临时 web garden `maxProcesses=2`，明确仅用于测试。

验证：

- Worker A 持续读取/代理；
- Worker B 申请、批准、撤销；
- 外部进程周期持锁；
- 至少 2000 读 + 100 关键写；
- 保存每个响应对应 PID/AppDomain；
- 无 500、无重复授权、无失效凭据恢复；
- 两个 worker 都能在锁释放后恢复。

### 10.6 recycle 与立即停机

- 常规 recycle 6 轮；
- 压力中 recycle 6 轮；
- Stop(false) 后 Stop(true) 升级；
- Dispose 后 Stop(true) 升级；
- 每轮确认 best-effort 队列按策略 drain/drop；
- 不出现 I/O aborted 未处理 500；
- 重启后可继续批准/撤销；
- 每轮数据库不变量通过。

---

## 11. 真实 headed Chrome 回归

必须使用 Chrome headed，不以 HTTP 脚本代替：

1. 新浏览器设备创建；
2. 提交访问申请；
3. 管理员真实页面登录；
4. 管理台看到正确路径 `/portal`；
5. 批准；
6. 设备进入 mock ERP 登录页；
7. 管理员撤销；
8. 旧 Cookie 重放被拒；
9. 设备进入复核/重新申请页；
10. 重新申请；
11. 再批准；
12. 再次进入 mock ERP。

并行启动外锁 1100/400 ms 循环再完整跑一次。门槛：

```text
状态机 12/12
console error 0
非预期 network failure 0
HTTP 500 0
数据库繁忙时显示临时繁忙，不误报设备/会话状态
```

保存：

- 每个关键状态截图；
- snapshot；
- console；
- network 请求/状态；
- 设备编号变化；
- requestId 与服务端日志对应关系。

---

## 12. 移动 APP 回归

本轮虽然不改移动业务，但数据库 Busy 映射会影响其响应契约，因此至少覆盖：

1. 原生 APP UA bootstrap，无 action 和合法 action；
2. 未批准申请；
3. 批准后登录；
4. SessionKey/HMAC 正常访问；
5. 撤销后旧凭据重放；
6. 重新申请/重新批准；
7. 旧 SessionKey 返回 session expired/revoked；
8. 新 SessionKey 正常；
9. 3200/16000 ms 外锁时返回临时数据库繁忙；
10. 不误报“设备尚未获批”或“移动 APP 登录会话已失效”。

若有真实 APK/AVD，必须复跑真实 APK；如果 APK 确实不可获得，报告必须把“协议级验证”和“原生控件未测”明确区分，不能写成真实 APK 已通过。

---

## 13. 每轮 SQLite 不变量

基线、PRAGMA 矩阵、外锁矩阵、每轮压力、多进程、recycle、浏览器和移动回归后都要执行，不得只在最终执行：

1. `PRAGMA integrity_check;` 返回 `ok`；
2. `PRAGMA foreign_key_check;` 返回 0 行；
3. `PRAGMA journal_mode;` 返回 `wal`；
4. token hash 重复 0；
5. recovery token hash 重复 0；
6. 设备/站点重复有效授权 0；
7. 无有效授权设备的可用 SessionKey 0；
8. 无有效授权设备的可用 HMAC/APP 凭据 0；
9. 已批准授权缺设备 0；
10. 已批准申请缺设备 0；
11. 授权站点孤儿 0；
12. 成功申请/批准/撤销数与行变化一致；
13. 取消/503 操作没有部分业务写；
14. WAL/SHM 生命周期符合预期；
15. 审计满足：`produced = persisted + merged + droppedByCapacity + droppedByShutdown + requeuedOutstanding`。

每轮保存 JSON 和 copied DB 一致性快照。数据库快照应使用 SQLite backup API 或停池后连同 WAL/SHM 正确处理，不能运行中只复制 `.db` 文件。

---

## 14. ZIP 打包卫生修复

### 14.1 使用洁净 staging

流程：

1. 从 fixed3 全新解压或受控源码目录构建；
2. Release build/test 完成；
3. 新建洁净 staging；
4. 按白名单复制源码、README、脚本、packages 和 Web 运行必需的 `bin`；
5. 不复制任何 `obj`；
6. tests 只复制源码、csproj 和必要配置，不复制 `bin/obj/TestResults`；
7. 从 staging 生成 ZIP；
8. 对 ZIP 条目和文本内容做二次扫描；
9. 全新解压 ZIP 再 build/test/deploy。

### 14.2 ZIP 禁止项

```text
**/obj/**
**/TestResults/**
*.trx
*.db
*.db-wal
*.db-shm
*.log
*.jsonl
本机绝对路径（[A-Za-z]:\）
DESKTOP-/WIN- 机器名
work_db_lock_fix*
output/gateway-*
临时 IIS 端口/站点名
验收管理员密码
锁持有器 ready flag
截图
AI 审查/验收措辞
```

`bin` 是可运行交付所需，可以保留，但必须扫描其旁边的 `.config/.xml/.pdb`：

- 不交付包含本机源码路径的 PDB；
- `.xml` 文档可保留，但不得包含生成机路径；
- 不要为了卫生删除 Web 运行所需 DLL/native interop。

### 14.3 扫描结果门槛

```text
absolute path hits = 0
machine name hits = 0
temporary credential hits = 0
database/log/TRX hits = 0
obj entries = 0
```

报告必须列出扫描规则、命中数和 ZIP 条目总数，不能只写“卫生扫描通过”。

---

## 15. 预计修改文件

具体以实际根因为准，但预计至少涉及：

```text
src/GatewayDemo.Legacy.Web/Infrastructure/SQLiteConnectionConfigurator.cs
src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRepository.cs
src/GatewayDemo.Legacy.Web/Infrastructure/GatewayDatabaseBusyException.cs
src/GatewayDemo.Legacy.Web/Infrastructure/GlobalError/HTTP 503 转换相关文件
src/GatewayDemo.Legacy.Web/Infrastructure/数据库诊断相关文件
src/GatewayDemo.Legacy.Web/Infrastructure/ManagedReverseProxy.cs
src/GatewayDemo.Legacy.Web/Infrastructure/GatewayErrorLogger.cs（仅当实现断流日志限频需要）
src/GatewayDemo.Legacy.Web/Web.config
src/GatewayDemo.Legacy.Web/GatewayDemo.Legacy.Web.csproj
tests/GatewayDemo.Legacy.Tests/*
scripts/legacy/setup-demo-sites.ps1
README.md
```

若新增 `SQLiteConnectionBudgetGuard.cs`、`DatabaseOperationContext.cs` 或 phase 诊断 DTO，应加入 csproj，并确保 net472/C# 7.3 可编译。不要引入只有 .NET Core 才有的 API。

---

## 16. 实施顺序

1. 固化 fixed3 哈希和独立失败证据；
2. 给现有代码加 phase 诊断，先复现数据库 500/11210 ms；
3. 拆 PRAGMA，确认具体 BUSY 语句；
4. 调整强类型连接串和初始化职责；
5. schema 检查移出请求热路径；
6. 实现统一读预算/Busy 转换；
7. 实现 connection-level budget guard；
8. 审核全部读写调用点；
9. 增加请求体读取阶段标识与 `0x800703E3` 窄分类器；
10. 实现请求体中止后的上游 Abort、无响应写入、无重试和低噪声诊断；
11. 补单元/真实 provider/请求体断流测试；
12. 跑原 61 项和 Stop 升级 150 次回归；
13. 全新 ZIP 部署真实机器 IIS；
14. 跑 cold AppDomain、锁矩阵、5 轮状态码感知压力；
15. 跑请求体 FIN/RST/慢传/并发/回收矩阵；
16. 跑多进程、recycle、headed Chrome、移动契约；
17. 每轮数据库不变量；
18. 洁净 staging 打包；
19. ZIP 全新解压重建并重复核心验收；
20. 清理临时 IIS、端口、锁进程和 ready flag；
21. 输出落地报告与完整 SHA-256。

---

## 17. 交付物

生成新候选，不得覆盖 fixed3 或此前包：

```text
E:\验证页面\deliverables\GatewayDemo.Legacy-net472-db-lock-fixed4-20260814.zip
E:\验证页面\deliverables\GatewayDemo.Legacy-net472-db-lock-fixed4-20260814-落地报告.md
```

必须复核并报告以下 SHA-256 未变化：

```text
fixed3 F43096A3203DA31815D895EB891E4C289B89BD2239AC197DD5A4916FAB191242
fixed2 9B60C55F9D27EA91670DE411E348090E977111D6FAB0E9C1C319A5C52F02EEA5
fixed1 6FBE7A00485DDDCEA4F78CC1D4C92EE404878F4929681B7173D8D35CD0850854
正式原包 D35A9804DAE6BE3D55C18B98F5833B98E9364335F56495B60290C0F1BBF5FD9F
```

---

## 18. 落地报告必填内容

1. fixed3 输入和 fixed4 输出完整哈希；
2. 真实根因：具体哪条 PRAGMA/哪个 provider phase 返回 Busy；
3. 11210 ms 被拆到哪个 phase；
4. 连接配置最终策略和每项实际 PRAGMA 查询值；
5. BudgetGuard 生命周期和 Cancel/Close 竞态证明；
6. COMMIT 前后 deadline 的响应语义；
7. 全部修改文件和关键代码位置；
8. 单元/组件测试数量和 TRX；
9. fixed4 150/150 Stop 升级回归；
10. cold AppDomain 6 轮；
11. 读/写锁矩阵；
12. 状态码感知压力 5 轮逐轮数据，不只给汇总；
13. 所有 HTTP 5xx 明细；
14. p50/p95/p99/max 和所有 phase max；
15. 多进程 PID/AppDomain 证据；
16. recycle、浏览器、移动契约结果；
17. 每轮数据库不变量；
18. ZIP 条目和内容卫生扫描规则/命中数；
19. 资源清理结果；
20. `0x800703E3` 的现场栈与 fixed3 源码缺口对照；
21. 请求体断流各场景的 IIS 日志、应用日志、上游终止耗时及资源回落证据；
22. 已知未测风险，不能笼统写“无风险”。

---

## 19. 新增现场问题：请求体读取被中止（0x800703E3）

### 19.1 结论

该问题仍存在于 fixed3，且不需要依赖异常文案猜测：研发截图中的组件版本为：

```text
Web=1.0.34.0
Core=1.0.14.0
SQLite=3.42.0
```

与 fixed3 机器 IIS 日志及候选包程序集版本一致。截图调用栈为：

```text
System.Web.HttpException (0x80004005)
  -> System.Runtime.InteropServices.COMException (0x800703E3)
     IIS7WorkerRequest.ReadEntityCoreSync
     HttpRequest.GetEntireRawContent
     HttpRequest.GetInputStream
     ManagedReverseProxy.WriteRequestBody
     ManagedReverseProxy.Proxy
     ProxyRequestModule.HandleRequest
```

`0x800703E3` 是 `HRESULT_FROM_WIN32(ERROR_OPERATION_ABORTED)`，低 16 位 Win32 错误码为 995。其含义是正在进行的 I/O 因客户端断开、请求取消、工作线程退出或应用回收而被中止。这里的“远程主机”是向 IIS 上传正文的下游客户端，不是业务上游站点。

本机审查 fixed3 ZIP 中的源码确认：

```text
ManagedReverseProxy.cs:136  注册 ClientDisconnectedToken -> request.Abort()
ManagedReverseProxy.cs:139  WriteRequestBody(context, request)
ManagedReverseProxy.cs:145  仅 catch (WebException)
ManagedReverseProxy.cs:1552 首次访问 context.Request.InputStream.CanRead
ManagedReverseProxy.cs:1568 获取上游 request.GetRequestStream()
ManagedReverseProxy.cs:1570 context.Request.InputStream.CopyTo(upstreamStream)
```

当前实现能处理“断开令牌先到、`HttpWebRequest.GetRequestStream` 抛 `WebExceptionStatus.RequestCanceled`”的竞态，但处理不了另一种时序：IIS 正在 `ReadEntityCoreSync` 读取客户端正文时先抛 `HttpException -> COMException(0x800703E3)`。后者不是 `WebException`，会直接穿透代理层。

本地预检查证据保存在：

```text
E:\验证页面\output\gateway-io-abort-fixed3-reproduction-20260814-114138
```

该预检查在真实机器 IIS 中命中了现有已处理分支 `ManagedReverseProxy.ClientDisconnected / WebExceptionStatus.RequestCanceled`，断流后 healthz 仍为 200；没有把随机竞态次数继续堆到“碰巧出现”截图分支。根据用户后续要求，本轮结论采用“研发同版本现场栈 + fixed3 源码异常类型缺口”的确定性检查，不把未命中的本地随机用例写成成功复现。coding agent 落地后必须按第 24 节用可重复的受控 stream 测试和真实 IIS 矩阵同时证明修复。

### 19.2 当前错误行为

穿透后 `Global.Application_Error` 会：

1. 把默认响应改成 HTTP 500；
2. 以 `Global.Application_Error` 写完整异常；
3. 尝试渲染并向已断开的客户端写错误页；
4. 写响应失败时再记录 `Global.Application_Error.ResponseWrite`。

这会把预期的客户端取消误报为服务器故障，并可能产生二次日志。客户端通常已经离线，看不到这个 500，但错误监控、IIS 统计和研发判断会被污染；如果对应上游请求没有及时 Abort，还会占用上游连接和 w3wp 线程。

### 19.3 边界

本项修复的是“请求体读取阶段的精确 I/O 中止”。不要做以下扩大化处理：

- 不按中文或英文异常消息匹配；
- 不吞掉全部 `HttpException`、`COMException`、`IOException` 或 `OperationCanceledException`；
- 不在 `Global.Application_Error` 全局忽略所有 `0x800703E3`；
- 不把客户端断开改写为 500、502、504 或伪造 499 响应；
- 不对 POST/PUT/PATCH/DELETE 正文做自动重试；
- fixed4 不顺带把整个代理改成异步栈或更换 `HttpWebRequest`。

---

## 20. 解决 0x800703E3：阶段受限的精确分类

### 20.1 纯分类器

在 `ManagedReverseProxy` 中增加可单测的纯函数，例如：

```csharp
internal static bool ContainsOperationAbortedHResult(Exception exception)
```

实现要求：

1. 沿 `InnerException` 向下检查，设深度上限（例如 16），防止异常对象异常嵌套；
2. 精确匹配 `unchecked((int)0x800703E3)`；
3. 可识别截图中的“外层 `HttpException(0x80004005)`、内层 `COMException(0x800703E3)`”；
4. 可识别直接抛出的同 HRESULT `COMException`；
5. 不使用 `Message.Contains(...)`；
6. `0x800703E2`、`0x800703E4`、普通 995 字符串、HTTP 413、数据库 Interrupt 等必须返回 false。

建议常量：

```csharp
private const int ErrorOperationAborted = 995;
private const int HResultOperationAborted = unchecked((int)0x800703E3);
```

`ErrorOperationAborted` 用于诊断展示，实际分类必须核对完整 HRESULT，不能只比较低 16 位。

### 20.2 分类必须同时满足阶段条件

仅 HRESULT 相同仍不够。必须把正文转发拆出明确阶段：

```text
acquire-client-input-stream
acquire-upstream-request-stream
copy-client-body
close-upstream-request-stream
await-upstream-response
copy-upstream-response
```

只有以下条件同时成立才走“客户端请求体已中止”分支：

```text
stage in { acquire-client-input-stream, copy-client-body }
AND exception chain contains 0x800703E3
```

`acquire-upstream-request-stream` 的 `WebExceptionStatus.RequestCanceled` 继续走现有 `ManagedReverseProxy.ClientDisconnected` 分支；上游响应读取、数据库、文件日志或其他处理阶段出现相同 HRESULT，不能被本分类器吞掉。

最稳妥的落点是在 `WriteRequestBody` 内部包住所有 `context.Request.InputStream` 访问，而不是在最外层加一个无阶段的 `catch (Exception)`。

### 20.3 不把 `Response.IsClientConnected` 作为必要条件

可以把以下值写入诊断：

```text
clientDisconnectTokenSignaled
responseIsClientConnected
applicationStopping
```

但不能要求 `Response.IsClientConnected == false` 才分类。原因是：

- 断开状态传播存在竞态；
- 应用池回收时连接状态可能仍显示 true；
- 查询 `IsClientConnected` 自身也可能抛 `HttpException`。

阶段 + 精确 HRESULT 才是决定条件；连接状态只用于解释来源。

---

## 21. 解决 0x800703E3：终止上游且不制造二次错误

### 21.1 正文复制结果显式化

把 `WriteRequestBody` 从无返回值改为可表达结果的内部结构，例如：

```text
Completed
NoBody
ClientBodyAborted
RejectedTooLarge
```

`ClientBodyAborted` 至少携带：

```text
exception
stage
bytesForwarded
declaredContentLength
isChunked
clientDisconnectTokenSignaled
elapsedMs
```

不要用异常作为正常控制流跨越整个代理；在请求体方法内精确识别后返回结果，代理入口收到 `ClientBodyAborted` 时直接结束。

### 21.2 手工复制循环

用固定大小缓冲区（建议 64 KiB）替代不可观测的 `Stream.CopyTo`：

```text
read client stream
if EOF before declared Content-Length => let IIS/provider semantics decide; do not invent success
write upstream stream
increment bytesForwarded only after successful write
repeat
```

这样既能记录断开前已转发字节数，也能对未知长度/分块正文执行 100 MiB 的运行时上限。已有 `Content-Length` 预检查保留；未知长度累计超过上限时走现有 413 语义，不得误分类为客户端断开。

fixed4 的必要修复是“正确处理取消”，不是强制切换 `GetBufferlessInputStream()`。`GetBufferlessInputStream` 与 `InputStream`/`Form`/`Files` 的访问顺序存在互斥和兼容风险；除非 coding agent 能提供 JSON、form-urlencoded、multipart、二进制上传和原生 APP 全部差分证据，否则本轮保留现有 `InputStream` 获取语义。

### 21.3 Abort/Dispose 顺序

命中 `ClientBodyAborted` 后：

1. 通过幂等 helper 调用 `HttpWebRequest.Abort()`；
2. 安全关闭上游请求流；关闭异常不得覆盖原始 `0x800703E3`；
3. 现有 `ClientDisconnectedToken` 注册仍保留，并允许与本分支并发调用 Abort；
4. 外层 `finally` 仍释放 cancellation registration 和可能存在的 `HttpWebResponse`；
5. 返回 `ManagedReverseProxy.Proxy`，由 `ProxyRequestModule` 现有代码调用 `CompleteRequest()`；
6. 不调用 `WriteBadGateway`；
7. 不调用 `Response.Clear/Write/Flush/End`，不尝试给已经断开的客户端发送错误页；
8. 不进入幂等重试逻辑，更不能重放带正文的非幂等请求。

上游 Abort 必须在识别后立即执行，不能等待日志落盘。日志失败也不能阻止 Abort。

### 21.4 保留原始异常

如果异常不满足精确分类，必须使用裸 `throw;` 保留原调用栈，继续走原来的失败路径。不得：

```csharp
throw ex;
```

不得在 `finally` 中抛出新的 Dispose 异常覆盖正文读取异常。必要时先记录 dispose failure 为附属诊断，但主异常和分类结果保持不变。

### 21.5 不建议的全局兜底

不要只改 `Global.Application_Error`。全局层不知道异常发生在请求体、上游响应还是其他组件，按 HRESULT 全局清除错误可能掩盖真正的数据丢失。

如果需要防御性兜底，只允许在代理读取正文前写入一个请求级 stage 标记，并在 `finally` 中清除；全局层必须同时核对该 stage 和完整 HRESULT。即使增加兜底，主要处理仍必须位于 `ManagedReverseProxy`，且测试要证明非代理 handler 抛相同 HRESULT 仍会成为 500。

---

## 22. 请求体中止诊断与日志限流

### 22.1 新事件

新增结构化来源：

```text
ManagedReverseProxy.ClientRequestBodyAborted
```

字段至少包括：

```text
requestId
method
safePath
siteKey
stage
hresult=0x800703E3
win32Error=995
declaredContentLength
bytesForwarded
isChunked
clientDisconnectTokenSignaled
responseIsClientConnected
applicationStopping
upstreamAborted=true
retryAttempted=false
responseCommitted=false
transportOutcome=client-request-body-aborted
elapsedMs
```

不要把响应默认值 `200 OK` 当作真实结果。客户端已经断开，没有可观察的 HTTP 响应，应显式记录 `responseCommitted=false/transportOutcome=...`。也不要为了日志人为设置 499；IIS/ASP.NET 并没有统一的 499 契约。

### 22.2 事件不是服务器错误

调用日志 helper 时必须使用 `markRequestAsFailed=false`，防止 EndRequest 再写一条“未记录 500”。该事件属于传输取消诊断，不应触发业务错误告警；但第一条必须可见，便于关联研发现场。

### 22.3 防止断流刷盘

攻击者可以批量建立上传后断开，因此不能为每次中止同步写完整堆栈。要求：

- 每个 `source + siteKey + stage` 的第一个事件立即记录；
- 后续事件在固定窗口（建议 5 秒）聚合；
- 聚合记录包含 `suppressedCount`、`firstUtc`、`lastUtc`；
- key 数量有上限和过期清理，不能形成无界字典；
- 计数满足 `observed = detailedLogged + aggregatedSuppressed`；
- 限流器不得访问 SQLite，不得使用审计写锁，不得延迟上游 Abort。

不要把完整 Cookie、SessionKey、HMAC、正文片段或上游凭据写入该日志。

---

## 23. 0x800703E3 单元与组件测试

### 23.1 分类器测试（至少 12 项）

必须覆盖：

1. 截图同构：`HttpException(0x80004005) -> COMException(0x800703E3)` 为 true；
2. 直接 `COMException(0x800703E3)` 为 true；
3. 多层包装仍为 true；
4. null 为 false；
5. 普通 `HttpException(500)` 为 false；
6. `HttpException(413)` 为 false；
7. `COMException(0x800703E2)` 为 false；
8. `COMException(0x800703E4)` 为 false；
9. Message 含“0x800703E3”但 HRESULT 不匹配为 false；
10. `WebExceptionStatus.RequestCanceled` 不由该 HRESULT 分类器冒充处理；
11. 超过深度上限安全返回；
12. 读取正文以外 stage 即使 HRESULT 匹配也不得吞掉。

### 23.2 正文复制测试（至少 10 项）

使用可控输入流、上游流和 Abort spy：

1. 空正文返回 `NoBody`；
2. 正常小正文逐字节一致；
3. 多缓冲区正文 hash 一致；
4. 输入流第 N 次 Read 抛截图同构异常，返回 `ClientBodyAborted`；
5. Abort 恰好产生一次外部效果（内部可多次幂等调用）；
6. `bytesForwarded` 只计算成功写入上游的字节；
7. 上游 Write 异常不误报为客户端读取中止；
8. Dispose 异常不覆盖原始读取异常；
9. 未知长度超过上限返回 413 路径；
10. 非目标异常保持原栈重新抛出。

### 23.3 日志限流测试

使用注入时钟和 barrier，不用 `Sleep` 猜时序：

- 第一条必记录；
- 同 key 5 秒内聚合；
- 不同 site/stage 互不压缩；
- 窗口后重新记录；
- key 上限与过期清理有效；
- 并发计数守恒；
- 日志写入失败不影响 Abort；
- 日志内容不含 Cookie、授权头和正文。

---

## 24. 真实机器 IIS 请求体断流验收

### 24.1 环境与前置

必须使用管理员提升后的机器级 IIS 和真实 `w3wp.exe`，不能用纯单元 fake 或 IIS Express 代替。使用 fixed4 ZIP 全新解压目录，新建隔离站点、应用池、端口、数据库和日志目录；先通过真实浏览器创建并批准一个设备，再从该设备上下文发起代理请求，确保确实进入 `ManagedReverseProxy.WriteRequestBody`，不能被设备门禁提前 401/403。

mock upstream 需要记录：

```text
requestStarted
headersReceived
bodyBytesReceived
bodyCompleted
connectionAborted
completedAtUtc
```

每个用例同时保留客户端结果、网关应用日志、IIS W3C 日志、mock 记录和 w3wp PID/AppDomain。

### 24.2 正常差分基线

先比较直连 mock 与经网关的完整请求，至少覆盖：

```text
POST application/json
POST application/x-www-form-urlencoded
POST multipart/form-data
PUT application/octet-stream
PATCH application/json
DELETE with body
64 KiB / 1 MiB / 16 MiB body
Content-Length body
chunked body（若当前产品明确支持）
```

比较 method、path/query、Content-Type、正文长度、SHA-256、上游响应 status/headers/body。正常请求不能因断流修复发生语义变化。

### 24.3 断流矩阵

使用真实 TCP 客户端或可控上传进程，至少执行：

| 场景 | 操作 | 期望 |
|---|---|---|
| header-only FIN | 发完声明大正文的 header 后正常关闭 | 无 Global 500，上游快速终止 |
| header-only RST | header 后 Linger=0 关闭 | 同上 |
| mid-body FIN | 发 64 KiB/1 MiB 后关闭 | 命中正文中止或 IIS 在应用前拒绝，两者均无应用 500 |
| mid-body RST | 发 64 KiB/1 MiB 后复位 | 同上 |
| slow upload kill | 持续慢传 5-10 秒后杀客户端进程 | 命中正文读取阶段，Abort 上游 |
| incomplete chunk | 发一个 chunk 后不发终止 chunk并断开 | 无全局 500，不伪造完整正文 |
| Expect 100 | 收到/等待 100-continue 后中止 | 无全局 500 |
| complete then disconnect | 完整上传、上游延迟响应时断开 | 走现有 ClientDisconnected 分支，不回退 |
| recycle mid-upload | 正文读取中回收应用池 | 无错误页二次写入、无孤儿上游连接 |
| shutdown mid-upload | 停止站点/应用池 | 有界退出，恢复后健康 |

每个非回收场景至少 20 次；慢传/RST 要有并发 50 和并发 100 两档，用 barrier 同时断开，以扩大竞态窗口。直接 mock 端也运行对应控制组，区分 IIS 自身行为与网关行为。

### 24.4 硬断言

```text
Global.Application_Error containing 0x800703E3 = 0
Global.Application_Error.ResponseWrite caused by disconnected upload = 0
ManagedReverseProxy.WriteBadGateway for client body abort = 0
non-idempotent retry count = 0
upstream abort latency p95 <= 1000 ms
post-case healthz = 200
normal POST after each batch = success and hash match
```

IIS W3C 日志出现 `sc-win32-status=995`、64 等客户端传输状态可以接受，但必须与客户端中止用例关联；不能把“客户端没有收到响应”统计成网关返回 500。验收脚本必须分别统计：收到的 HTTP 状态、传输异常、IIS 状态和应用事件。

### 24.5 资源回落

在 100 并发断流前、峰值、结束后 30 秒记录：

```text
w3wp thread count
handle count
private bytes / working set
active upstream connections
ASP.NET requests executing/queued（可取得时）
```

结束后线程、句柄和连接必须回落到基线容差内；不得出现每轮单调上涨。之后执行一次完整申请/批准/撤销状态机和 SQLite 不变量，证明传输异常没有污染授权数据。

### 24.6 回收专项说明

回收可能在日志 flush 前终止旧 AppDomain，因此不能仅以“没有日志”判成功。必须联合使用：

- 旧/新 w3wp PID 与 AppDomain；
- IIS W3C/FREB（如启用）；
- mock upstream 的连接关闭时间；
- 回收完成后的 healthz 和正常代理请求；
- 端口连接与进程句柄回落。

回收期间可以没有客户端响应，但不能有旧上游连接长期存活，也不能在新 AppDomain 中重复执行已中止的非幂等请求。

---

## 25. Definition of Done

只有以下全部满足，fixed4 才可提交独立验收：

```text
[ ] fixed3 的两个 GET 500 根因已精确定位并有回归测试
[ ] 请求路径不再执行组合 PRAGMA batch
[ ] Busy handler 在任何可能访问数据库的 phase 前生效
[ ] 读路径 Busy/Locked 不再成为 HTTP 500
[ ] 2000 ms 写预算覆盖单次 provider 调用
[ ] SQLiteConnection.Cancel guard 通过并发/竞态测试
[ ] 没有超时返回后后台继续 COMMIT
[ ] 已 COMMIT 操作不会返回失败
[ ] 状态码感知压力连续 5 轮 unexpected 5xx=0
[ ] 关键写 max<=2500 ms，budget overrun=0
[ ] cold AppDomain + 外锁 6 轮无 500
[ ] 多进程无 500/重复授权/失效凭据恢复
[ ] 原 61 项测试全部通过
[ ] Stop 升级 fixed4 150/150
[ ] recycle 常规与压力中均通过
[ ] headed Chrome 状态机 12/12，console/network 0 异常
[ ] 移动 APP 协议未误报设备/会话状态
[ ] 每轮 SQLite 不变量全部通过
[ ] ZIP obj=0、绝对路径=0、数据库/日志/TRX=0
[ ] `0x800703E3` 只在请求体读取阶段按精确 HRESULT 链分类
[ ] 请求体 FIN/RST/取消/回收均不进入 Global.Application_Error
[ ] 断流后上游请求在 1 秒内被 Abort，且不重试非幂等请求
[ ] 断流路径不写 500/502 错误页，正常客户端请求语义不变
[ ] 其他 HttpException/COMException 仍按原错误路径处理
[ ] 并发断流后 w3wp 线程、句柄、上游连接回落且健康检查正常
[ ] fixed4 从 ZIP 全新解压后可 Release build/test/IIS deploy
[ ] fixed3/fixed2/fixed1/正式原包哈希未变化
[ ] 临时 IIS/端口/锁进程/flag 全部清理
```

任何一项没有原始证据，都不能写“全部 DoD 达成”。
