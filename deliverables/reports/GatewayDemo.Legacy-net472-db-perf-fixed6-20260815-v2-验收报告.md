# GatewayDemo.Legacy-net472 fixed6 数据库性能优化验收报告

验收日期：2026-08-15  
候选包：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472-db-perf-fixed6-20260815-v2.zip`  
候选包 SHA-256：`530C9BABE4C54C34F5A2DA8039A87A34CDE98E7795FE1A906EF6C072EB331D78`

## 结论

fixed6 候选包通过本轮数据库优化验收，可以覆盖正式压缩包。候选包在全新解压目录完成 131/131 回归，并在真实机器 IIS、真实 headed Chromium、真实 SQLite 外部写锁下完成验证。临时 IIS 站点、应用池、锁进程、端口和浏览器会话已清理。

## 自动化和迁移验证

- 全新解压候选包 Release 回归：131/131 通过，0 失败、0 跳过。
- 工作目录基线：122/122 通过；fixed6 新增/迁移定向测试 9/9 通过。
- v10→v11 原位迁移：业务行数保持不变；新增索引恢复；旧审计索引移除；schema marker=11；`integrity_check=ok`；`foreign_key_check=0`。
- ZIP 卫生：234 条目；无 `obj`、数据库/WAL/SHM、日志、TRX、临时性能探针或输出目录条目。

证据：`E:\验证页面\output\gateway-fixed6-acceptance-20260815_002846\fresh-test-results-v2\fixed6-fresh-extract-131.trx`。

## 性能对比

相同探针、20 次取样、相同业务数据规模，对比 fixed5（Web 1.0.34.0）与 fixed6（Web 1.0.35.0）：

| 有效授权行数 | 指标 | fixed5 | fixed6 | 加速 |
|---:|---|---:|---:|---:|
| 1,000 | p50 | 29 ms | 21 ms | 27.6% |
| 1,000 | p95 | 35 ms | 24 ms | 31.4% |
| 1,000 | p99 | 35 ms | 26 ms | 25.7% |
| 10,000 | p50 | 203 ms | 152 ms | 25.1% |
| 10,000 | p95 | 216 ms | 174 ms | 19.4% |
| 10,000 | p99 | 228 ms | 179 ms | 21.5% |

原始数据：`E:\验证页面\output\gateway-fixed6-acceptance-20260815_002846\benchmark-fixed*.txt`。

## 真实 IIS 验收

部署站点使用唯一端口：网关 48820、管理台 48821、Mock 上游 48825；单进程应用池。验证内容如下：

- 管理台快照在持续外部 SQLite 写锁下仍返回 200，证明只读快照不再获取写保留锁。
- 2 秒外部锁下，真实管理台“创建并签发凭据”操作成功，锁释放后页面继续正常工作。
- 60 秒外部锁下，同一真实按钮操作返回 503，耗时约 2.1 秒；响应带 `Retry-After: 1`，页面显示“数据库当前繁忙，请稍后重试”，不泄露内部异常。
- 503 日志记录 `GatewayDatabaseBusyException`、SQLite `Busy/5`，随后维护任务和业务请求恢复。
- 回收应用池后重新登录管理台、读取 dashboard、访问上游登录页均恢复。

证据：`E:\验证页面\output\gateway-fixed6-acceptance-20260815_002846\db-check.txt`、`logs-tail.txt`。

## 真实 headed Chromium 验收

使用 Playwright headed Chromium，独立用户和管理员会话完成：

1. 新浏览器设备登记并显示“设备尚未获批，请先提交访问申请”；
2. 可见表单提交申请；
3. 管理台可见申请并批准；
4. 浏览器进入真实 Mock ERP 登录页；
5. 管理台撤销授权后，浏览器回到网关复核页；
6. 重新申请、重新批准后再次进入 ERP 登录页；
7. 20 个同一真实 Chromium 上下文并发 GET 全部返回 200，p50 238 ms、p95 292 ms、最大 310 ms；
8. 390×844 移动视口下登录页无水平溢出（scrollWidth=clientWidth=390）。

截图：`E:\验证页面\output\playwright\fixed6-erp-login.png`。

Nonce 重放验证也通过：同一 APP HMAC 请求第一次返回 200，原样重放返回 401 `gateway_app_auth_failed`，应用日志原因为“APP 请求疑似重放”。

## 说明与边界

本轮用的 Mock ERP 登录页在 root-entry 演示配置下将表单提交地址生成成 `/login`，该路径在网关演示路由中返回 404；因此本轮把“真实到达上游登录页”作为数据库优化验收点，并将“提交 Mock ERP 登录表单”单独记录为演示上游路径问题。该现象与 fixed6 数据库优化代码无关，未被静默忽略。

另外，为验证凭据绑定保护，曾用 PowerShell 非浏览器 UA 复用浏览器 Cookie，系统按设计记录 browser-credential-mismatch 并要求重新审核；这不是性能回归。

## 清理

`GatewayFixed6Verify`、`MockFixed6Verify` 站点及两个应用池已删除；48820/48821/48825 无 LISTENING 残留；SQLite 锁进程和 headed Chromium 会话均已关闭。正式包已覆盖为 fixed6 候选内容；覆盖前原包保存在 `E:\验证页面\output\GatewayDemo.Legacy-net472-before-fixed6-20260815.zip`，候选包和本报告继续作为审计证据。
