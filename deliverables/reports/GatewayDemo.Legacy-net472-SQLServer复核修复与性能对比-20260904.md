# GatewayDemo.Legacy-net472 SQL Server 复核修复与性能对比

最终交付包：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`；SHA-256：`36157B7161BAD678B972645AFC0D263349138BA848B7E0E3D7AE235FF6AF5875`。

本次复核确认原报告中的 SQL Server 初始化脚本兼容性、授权回退同步读取、后台 dispatcher 瞬态错误处理和共享槽位串行等问题仍存在，已完成修复。恢复凭据开关关闭时会跳过恢复 Cookie 的读写；SQLite 部署默认预算统一为写 2000ms、读 500ms、最多 8 次；心跳、审计和维护路径在 SQL Server 下使用独立后台槽位和 SQL Server 写预算；授权触摸只入队，不阻塞当前转发请求。

SQL Server 初始化脚本已在本机 SQL Express 创建独立数据库并初始化 18 张表。完整 net472 回归测试 173 项通过、6 项因未配置现场 SQL Server 集成环境跳过；新增 SQL Server 并发槽位与瞬态错误测试 2/2 通过。候选 IIS 站点应用池回收后健康检查为 200，最终程序集已替换并完成健康检查。

并发 16、相同 3 轮 × 10 秒压测，所有请求均成功：

| 场景 | 修复前 SQL Server | 修复后 SQL Server | 提升 |
| --- | ---: | ---: | ---: |
| 浏览器请求吞吐 | 1,549.14 req/s | 2,052.59 req/s | +32.5% |
| 混合请求吞吐 | 843.46 req/s | 1,738.58 req/s | +106.1% |

修复后数据来自最终程序集对应的隔离 IIS 站点和 SQL Express，压测机、网关、数据库及零延迟上游在同一台 Windows 主机；并发 16，5 秒预热后 3 轮 × 10 秒，全部请求返回 200；结果用于版本前后对比，不代表生产容量上限。原始测量见 `E:/验证页面/artifacts/sqlserver-issue-audit-20260904/candidate-measurements-final3`。
