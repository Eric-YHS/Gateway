# GatewayDemo.Legacy-net472 SQL Server 实施与性能对比

日期：2026-09-04。本文件前半部分记录修复前一轮结果；后续复核已修复问题并覆盖交付包，最新结果见 [复核修复与性能对比](GatewayDemo.Legacy-net472-SQLServer复核修复与性能对比-20260904.md)。

历史候选包：[GatewayDemo.Legacy-net472-sqlserver-20260904-final.zip](E:/验证页面/.archive/deliverables-legacy-zips-20260904/GatewayDemo.Legacy-net472-sqlserver-20260904-final.zip)。当前覆盖后的交付包是 `E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`，SHA-256：`36157B7161BAD678B972645AFC0D263349138BA848B7E0E3D7AE235FF6AF5875`。

实现重点：

- 已授权浏览器请求将凭据、设备、有效授权和站点范围合并为一次授权快照读取，请求内复用，撤销或状态写入后自动失效。
- 心跳、审计、设备观测统一进入有界队列，限频、合并、批量后台写入，不让普通观测写入阻塞转发。
- SQL Server 模式移除 SQLite 文件锁；读写使用事务、RCSI/SNAPSHOT、连接池和有界超时，死锁只对确定可重试阶段有限重试。
- HMAC 防重放 nonce 仍是必要的原子写入；最终源码使用 `READ COMMITTED` 降低 nonce 写入的范围锁竞争，唯一索引保证并发下只能成功一次。

历史候选包验证结果：Release 构建成功；完整 .NET Framework 回归 `177/177` 通过；SQL Server 专项测试 `8/8` 通过；SQL Server Express 健康检查为 `200` 且数据库状态为 `ok`。当前覆盖包的最终验证和吞吐以《复核修复与性能对比》为准。

压测使用同一台 Windows 主机、同一 Mock 上游、128 个浏览器设备和 128 个 HMAC 客户端，HTTP/1.1、关闭限流，5 秒预热后每个场景 3 次×10 秒；成功响应同时校验状态码、响应正文和 `X-Perf-Upstream`。吞吐为三次正式轮次成功请求总数除以总耗时，延迟只统计内容校验成功的请求。

| 场景 | 并发 | 原版 SQLite 吞吐 | 优化 SQLite 吞吐 | SQL Server 吞吐 | 原版 p95 | 优化 SQLite p95 | SQL Server p95 | SQL Server 错误 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 浏览器 | 1 | 91.21 | 212.77 | 520.50 | 14.46 ms | 5.42 ms | 2.21 ms | 0 |
| 浏览器 | 16 | 169.32 | 432.77 | 1549.14 | 174.91 ms | 58.59 ms | 11.76 ms | 0 |
| 浏览器 | 64 | 170.80 | 423.01 | 1446.15 | 489.19 ms | 189.59 ms | 88.84 ms | 0 |
| 混合 80% GET/20% HMAC POST | 1 | 72.71 | 124.44 | 293.78 | 24.44 ms | 19.97 ms | 3.51 ms | 14/8893 |
| 混合 80% GET/20% HMAC POST | 16 | 129.12 | 199.34 | 843.46 | 196.66 ms | 122.14 ms | 34.79 ms | 50/25574 |
| 混合 80% GET/20% HMAC POST | 64 | 128.69 | 200.83 | 719.87 | 629.69 ms | 379.17 ms | 574.17 ms | 82/22326 |

与原版相比，优化 SQLite 在浏览器并发 16/64 时吞吐约提升 2.5 倍、p95 降低约 61%~67%；SQL Server 浏览器吞吐约提升 7.5~9.1 倍，p95 降低约 82%~93%。混合流量下 SQL Server 吞吐约提升 4.6~6.5 倍；64 并发仍有少量 503，主要集中在约 0.5~0.6 秒的数据库读预算边界。该数据来自单机 SQL Server Express，不能直接外推生产容量；上线前应按现场数据库版本、网络延迟、Worker 数量和 HMAC 占比复测。

说明：上表是修复前一轮候选站点的历史压测。修复后的并发 16 新结果和 UAC 回收验证见最新复核报告；最终 ZIP 已包含修复源码和程序集。

证据目录：

- 性能汇总：[comparison.json](E:/验证页面/artifacts/sqlserver-perf-20260904/evidence/comparison.json)、[comparison.csv](E:/验证页面/artifacts/sqlserver-perf-20260904/evidence/comparison.csv)
- 完整回归：[candidate-regression-final.trx](E:/验证页面/artifacts/sqlserver-perf-20260904/evidence/tests/candidate-regression-final.trx)
- SQL Server 专项回归：[sqlserver-postpatch.trx](E:/验证页面/artifacts/sqlserver-perf-20260904/evidence/tests/sqlserver-postpatch.trx)
- 部署健康：[sqlite-health.json](E:/验证页面/artifacts/sqlserver-perf-20260904/evidence/sqlite-health.json)、[sqlserver-health.json](E:/验证页面/artifacts/sqlserver-perf-20260904/evidence/sqlserver-health.json)

部署 SQL Server 的最小步骤：先用 `scripts\legacy\initialize-sqlserver.ps1` 创建或初始化独立数据库，再执行 `setup-demo-sites.ps1 -StorageProvider SqlServer -SqlServerConnectionString <连接串>`。生产切换前停止 SQLite 写入，使用 `migrate-sqlite-to-sqlserver.ps1` 做一致快照迁移和行数/摘要校验；数据库异常时不会自动回退到旧 SQLite。
