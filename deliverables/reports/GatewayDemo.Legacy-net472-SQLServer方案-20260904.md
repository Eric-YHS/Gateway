# GatewayDemo.Legacy-net472：SQL Server 支持与请求链路减负方案

日期：2026-09-04。状态：初始方案记录；实施结果与实测数据见同目录的《GatewayDemo.Legacy-net472-SQLServer实施与性能对比-20260904》。

核查对象：[GatewayDemo.Legacy-net472.zip](E:/验证页面/deliverables/GatewayDemo.Legacy-net472.zip)，文件时间为 2026-08-25 12:07:14，大小 16,767,624 字节。

SHA-256：`93F36C1AFDA36A055921A449CE3BB208193AE23DDA408EDA1FBED38DCDF5C3DE`。

本文件保留首次核查时的设计依据。后续已在隔离副本中完成 SQL Server provider、授权快照与后台写入治理，并以同一 Mock 上游完成对照压测；最终结果见实施报告。

## 建议直接回复研发

> 可以支持 SQL Server，我看过当前交付包了，需要补齐 SQL Server 的存储实现和数据迁移，不能只改连接字符串。
>
> 您提到的数据库争用确实有实现上的依据：心跳和普通审计虽然已做后台队列，但仍与关键授权操作共用 SQLite 的写锁；另外，已授权请求目前仍会多次查库，授权缓存实际上没有启用。
>
> 我建议增加 SQLite / SQL Server 可配置切换，同时优化已授权请求：把凭据、设备状态、授权及站点范围的读取合并，心跳和普通日志统一限频、合并、批量写入，请求不等待这些非关键数据落库。批准、撤销、凭据变更及 HMAC 防重放仍保留必要的事务和一致性校验，避免为了少查库而导致撤销不及时。
>
> SQL Server 可以作为并发写入较多场景的部署选项，但实际提升需要同条件压测确认。移动端识别单独验证有效凭据恢复、任意业务入口及撤销重申请，换数据库本身不能解决身份识别问题。这轮先给方案，后续按这个方向实施并验证。

## 已确认的实现情况

所有代码链接均指向本轮从指定 ZIP 解出的副本。

| 核查项 | 证据与判断 |
| --- | --- |
| 当前是否已支持 SQL Server | [LegacyGatewayConfiguration.cs:140](E:/验证页面/artifacts/sqlserver-plan-20260904/package/src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayConfiguration.cs:140) 固定使用 `Sqlite`；运行时、仓储和后台任务大量依赖 `SQLiteConnection`。有选项字段不等于有可运行的 SQL Server provider。 |
| 授权缓存是否生效 | [LegacyGatewayRuntime.cs:132](E:/验证页面/artifacts/sqlserver-plan-20260904/package/src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRuntime.cs:132) 的 `GetCachedAuthorization` 直接调用查询函数。注释明确说明：为避免其他 IIS Worker 撤销或缩小授权后，本地缓存继续放行，每次从库读取。失效方法目前为空。 |
| 已授权请求是否仍查库 | [ProxyRequestModule.cs:692](E:/验证页面/artifacts/sqlserver-plan-20260904/package/src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs:692) 每次检查外部审批结果，再读有效授权。授权查询还会加载站点范围；此前的浏览器凭据、设备信息或旧 APP SessionKey 解析也会查询。次数依凭据通道及状态分支而变，不能称为“只查一次”。 |
| 心跳是不是同步写 | [LegacyGatewayRepository.cs:3270](E:/验证页面/artifacts/sqlserver-plan-20260904/package/src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRepository.cs:3270) 的授权心跳只入队并返回已完成 Task；设备、旧 APP 身份及会话的 `Touch` 方法也已入队。请求处有 `GetResult()`，不代表该方法正在同步等待数据库提交。 |
| 两个 Dispatcher 是否争用资源 | [GatewayHeartbeatDispatcher.cs:481](E:/验证页面/artifacts/sqlserver-plan-20260904/package/src/GatewayDemo.Legacy.Web/Infrastructure/GatewayHeartbeatDispatcher.cs:481) 和 [GatewayBestEffortAuditDispatcher.cs:662](E:/验证页面/artifacts/sqlserver-plan-20260904/package/src/GatewayDemo.Legacy.Web/Infrastructure/GatewayBestEffortAuditDispatcher.cs:662) 都先取数据库文件 lease，再取 `DatabaseWriteSyncRoot`，持锁完成批事务。 |
| 是否影响关键写入 | [DatabaseWriteCoordinator.cs:37](E:/验证页面/artifacts/sqlserver-plan-20260904/package/src/GatewayDemo.Legacy.Web/Infrastructure/DatabaseWriteCoordinator.cs:37) 对 `<数据库路径>.write.lock` 独占打开；[LegacyGatewayRepository.cs:3691](E:/验证页面/artifacts/sqlserver-plan-20260904/package/src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRepository.cs:3691) 的关键写入也取这把锁。后台任务取不到锁会让出，但先取得锁后仍可能让其他写入等待，且没有严格的关键操作优先调度。 |
| 是否还有其他后台写入 | [LegacyGatewayRepository.cs:1676](E:/验证页面/artifacts/sqlserver-plan-20260904/package/src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRepository.cs:1676) 的普通设备观测更新另走 `ThreadPool.QueueUserWorkItem`，没有统一进入心跳 Dispatcher。另有维护清理与激活通知任务，需要一起梳理。 |
| 是否能让所有已授权请求都不写库 | [LegacyGatewayRepository.cs:2650](E:/验证页面/artifacts/sqlserver-plan-20260904/package/src/GatewayDemo.Legacy.Web/Infrastructure/LegacyGatewayRepository.cs:2650) 在 HMAC 请求中事务性消费 nonce，防止多个进程重复接受同一签名请求。此类安全写入不能作为普通心跳延后或丢弃。 |

当前配置已包含：心跳队列容量 4096、批大小 100、刷新间隔 100 ms、同一合并键写入间隔 30 秒；审计队列容量 2048、批大小 50、刷新间隔 250 ms。见 [Web.config:55](E:/验证页面/artifacts/sqlserver-plan-20260904/package/src/GatewayDemo.Legacy.Web/Web.config:55)。心跳限频按各 AppDomain 的合并键进行，并不是整个部署“每台设备全局只写一次”；不同类型记录及不同 Worker 仍会产生写入。

读预算默认 500 ms、关键写预算默认 2000 ms，是各存储操作的预算，不等于整个请求的数据库等待上限。多个存储调用可能累计等待，需设计请求级总预算。

SQLite 的 WAL 可以让读写并行，但同一数据库仍只有一个写者；所以不能断言“心跳一写就必然堵住所有 SELECT”，也不能把异步队列理解为完全消除了资源争用。上述代码证明存在争用路径，尚不能证明它就是研发现场延迟的唯一或主要来源。[SQLite 官方 WAL 说明](https://www.sqlite.org/wal.html)

历史性能报告针对其他 SHA-256 的候选包，且部分指标来自管理台快照；不能拿来代替本包已授权转发路径的基准，更不能推算 SQL Server 提升倍数。

## 推荐设计

### 1. 首期保持授权一致性，把常态请求收敛到一次授权快照读取

适用范围：已批准、凭据有效、设备环境没有触发复核、没有登录/注销/凭据恢复等状态变更的普通浏览器和旧 APP 请求。

目标流程：解析凭据及当前请求信号 → 一次存储往返读取一致的授权快照 → 内存判断设备状态、凭据代次、站点范围 → 转发；非关键观测数据只更新有界队列。

- 合并凭据映射、设备状态、有效授权和站点权限查询；优先使用单条查询的一致视图。若需要多条语句，必须明确事务快照语义，不能拼出新旧状态混合的授权结果。
- 同一请求内复用已经加载的设备上下文，去掉无状态变化时的重复读取。
- 外部审批在审批提交时完成授权变更并记录事件；若现有集成仍直接改表，保留等价同步机制及可测的生效时限，不能直接删掉 `ProcessExternalDecision`。
- 将同步数据库 I/O 外包给 `Task.Run` 后立即等待并不能减少 I/O 或释放请求线程；SQL Server 实现采用可取消的真正异步调用，并接入 System.Web 的异步请求流程。
- 使用请求级数据库总预算，包含连接池等待、连接建立、命令及重试；预算耗尽给出受控结果，不无限拖住请求线程。

首期不直接恢复一个长 TTL 的跨请求授权缓存。保留一次查询，是为了使撤销提交后启动的新授权判定能看到已撤销状态。已进入转发阶段的在途请求如何处理，另外定义边界。

如果后续仍需把命中请求降到零数据库读取，再引入带版本的授权快照、跨 Worker 失效通知、漏通知补偿和新鲜度上限；缓存键及内容须覆盖凭据代次、设备状态、站点权限和配置版本。单纯本地缓存、JWT 或“后台每隔几秒刷新”都会引入撤销窗口，不能默认与现有行为等价。数据库故障时，也不能无限使用旧授权继续放行。

HMAC 请求单独计量：凭据/授权读取可以合并，但 nonce 原子消费仍属于必要写入。没有具备同等跨进程一致性和持久性的替代存储前，不改变它。

### 2. 将非关键写入统一治理，减少对授权数据的干扰

- 收口两个 Dispatcher 和独立线程池中的非关键观测更新，统一容量、限频、批量大小、重试预算及指标；不把信号变更导致的复核、凭据变更等安全状态更新降级为普通观测。
- 心跳沿用按键合并，只保留最新观测。先保留当前 30 秒间隔作为对照基线，再按在线状态展示需求评估调大；不得让异步心跳成为授权有效性的唯一依据。
- 观测表与授权/凭据表分离：LastSeen 等统计信息尽量不反复 UPDATE 授权判断需要读取的行。跨 Worker 的时间更新应取较新值，并检查设备/会话仍有效，防止旧队列覆盖新值或复活已撤销会话。
- SQLite 模式下协调后台写入，按小批次、短持锁时间主动让出，优先服务关键操作；每进程各有一个消费者并不等于跨进程只有一个写者。拆到不同表也不能消除 SQLite 的数据库级单写者限制。
- SQL Server 模式下移除 SQLite 专用文件锁和全局写锁，使用数据库事务及行级并发控制；后台并发和连接池使用量受限，给授权查询和管理操作保留资源。
- 普通诊断日志可合并、采样、延期，队列满时记录丢弃计数，不能回退到请求线程同步写库。要求可靠保留的审计使用事务内审计或可靠 outbox，不静默丢弃。

### 3. 增加完整的 SQL Server provider

建议保留 SQLite 作为轻量部署选项，增加 SQL Server 作为并发写入较多场景的选项。以下配置名是拟议设计，当前包不能直接使用：`Storage.Provider = Sqlite | SqlServer`，并分别提供连接配置。

| 工作包 | 必须覆盖的内容 |
| --- | --- |
| 存储边界 | 配置解析、连接工厂、仓储、事务、初始化/版本升级、后台审计/心跳、健康检查、维护任务。现有 `IGatewayRepository` 不覆盖全部功能，需要补齐实际边界。 |
| 安全及身份数据 | 浏览器凭据与恢复令牌、APP 凭据和 nonce、旧 APP 多身份映射/会话/撤销墓碑/通知去重、WebAuthn、申请与授权、激活令牌及通知 outbox。 |
| SQL 适配 | 重写 `LIMIT`、`INSERT OR IGNORE/REPLACE`、`PRAGMA`、SQLite 系统表、部分索引和触发器；明确 GUID、UTC 时间、布尔值及字符串长度/排序规则映射。不能仅替换连接类型。 |
| 并发正确性 | 保留唯一设备凭据、唯一有效授权、唯一待审申请、nonce 唯一性、引用完整性；并发批准/撤销/签发使用事务与条件更新。重试范围只包含可安全重放的操作，提交结果不明确时按操作 ID 查证。 |
| SQL Server 专用行为 | 明确参数类型和索引；限定事务范围与连接池；处理死锁、超时、断线及取消。评估 `READ_COMMITTED_SNAPSHOT` 降低读写阻塞，同时监控版本存储；不能用 `NOLOCK` 读取授权。 |
| 配置与运维 | SQL Server 模式不得继续打开 SQLite 或尝试本地数据库自愈；连接串故障不得自动切回旧 SQLite，以免使用过时授权。数据库升级是受控步骤，不在首个业务请求里做大规模迁移。 |

本包 schema v11 有 18 张表。工作区遗留的 `sql/legacy/GatewayDemo.SqlServer2005.sql` 只有 9 张表，且不在该 ZIP 中，不能作为当前完整建库脚本。需要按现有模型重新生成并校验升级脚本。SQL Server 2005 不能因为旧脚本的文件名就被视为已支持目标。

.NET Framework 4.7.2 本身不是接入 SQL Server 的障碍；SqlClient 有支持该框架的版本。驱动、数据库最低版本、Windows/TLS 兼容性应按实际部署环境确定。[微软 SqlClient 平台支持](https://learn.microsoft.com/en-us/sql/connect/ado-net/introduction-microsoft-data-sqlclient-namespace?view=sql-server-ver17)

SQL Server 的行版本读取可以降低读写互相阻塞，但不会消除写写冲突、长事务、热点行和数据库网络往返；是否启用 RCSI 应连同查询一致性一起验证。[微软事务锁与行版本指南](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide?view=sql-server-ver17)

### 4. 移动端识别作为独立验收项

当前包已经有按签名凭据、已知 SessionKey、设备映射和恢复 Cookie 恢复上下文的路径，见 [DeviceCredentialService.cs:139](E:/验证页面/artifacts/sqlserver-plan-20260904/package/src/GatewayDemo.Legacy.Web/Infrastructure/DeviceCredentialService.cs:139)。仍需用研发现场的失败样例确认哪一段不匹配，不能从“换数据库”推导它会修复。

验收至少包括：已授权 APP 从任意业务路径首次访问、免登录恢复、登录与改密区分、SessionKey 轮换、Cookie 缺失、并发恢复、撤销后携旧会话访问及重新申请。UA 负责客户端分类，实际授权必须结合可校验的凭据或已登记映射及设备状态；不能仅因 UA 相同就视为同一已授权设备。

## 实施及迁移顺序

1. 固定本包基线，在隔离环境记录浏览器/旧 APP/HMAC 三类已授权请求的存储调用次数、数据库耗时和转发延迟，并采集锁持有者、队列深度、批次大小及等待时间。
2. 补齐存储契约、SQL Server schema/provider 与完整功能回归，先让 SQL Server 模式具备等价行为。
3. 合并授权快照读取、统一非关键写入、拆分热点观测数据；分别对 SQLite 和 SQL Server 做对照，区分减少调用带来的收益与数据库更换的收益。
4. 以数据副本演练迁移。导出必须取得一致的 SQLite 快照，不能只复制活跃主库而漏掉 WAL。迁移保留设备 ID、凭据哈希及代次、授权范围、旧会话与撤销记录等；加密数据还须验证解密所需密钥和配置可用，不只比对行数。
5. 正式切换使用维护窗口停止旧网关及后台写入，导出、导入、校验后切 provider，再开放流量。保留切换前数据库和配置。SQL Server 接受新写入后，回退必须处理新增申请、撤销及凭据变更，不能直接切回旧 SQLite。

优先完成双 provider 和请求减负，不把额外部署 Redis、消息服务或大规模缓存改造作为首期前置条件。

## 验收方案与需要落实的环境参数

| 场景 | 验收重点 |
| --- | --- |
| 常态授权访问 | 浏览器与旧 APP 无状态变化请求目标为一次授权快照存储往返、零同步非关键写入；HMAC nonce 写入单列。统计实际查询/写入数，不只看 HTTP 200。 |
| 并发与容量 | 同机/同数据/同上游比较直连上游、当前 ZIP、优化 SQLite、优化 SQL Server；分别测试同一设备高并发和多设备并发。记录吞吐、p50/p95/p99、数据库往返、CPU/磁盘/连接池和队列。并发阶梯按现场峰值设置。 |
| 心跳、审计及清理压力 | 同时运行授权转发、普通审计突发、心跳刷新、管理批准/撤销及清理；非关键队列不拖住请求或无限增长，关键安全操作不丢失。 |
| 跨进程授权一致性 | 至少两个 IIS Worker：A 撤销或缩小权限，提交后 B 的新授权判定不得使用旧状态放行；覆盖回收、启动、重连和缓存失效。 |
| 安全事务 | 同一 HMAC nonce 并发只接受一次；并发审批/撤销后无重复有效授权；旧会话不能因后台心跳恢复；所有成功操作可核对持久化结果。 |
| 故障与恢复 | SQLite 外部写锁、SQL Server 阻塞/死锁/断连、连接池耗尽、后台队列满；请求等待有上限，恢复后无错误重放或授权倒退。 |
| 数据迁移 | 全表行数、关键键/哈希、授权范围、唯一约束、引用关系及凭据恢复一致；演练回退边界。 |
| 移动 APP | 用脱敏请求样例及真实终端验证任意入口、免登录恢复、会话轮换和撤销重申请，不以桌面移动视口替代真实 APP。 |

当前尚缺现场 SQL Server 版本/版本类型、部署位置及网络延迟，IIS Worker/服务器数量，活跃设备数与峰值 QPS，HMAC 请求占比，审计保留量及转发延迟目标。实施前把这些参数与研发落实即可；它们不妨碍本次确认“可增加 SQL Server 支持”的方向，但在它们及同条件压测缺失时，不承诺提升倍数或“完全不受数据库影响”。
