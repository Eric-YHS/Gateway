# GatewayDemo.Legacy-net472 全面检查与新 bug 搜寻报告（2026-08-27）

- 被测包：`deliverables/GatewayDemo.Legacy-net472.zip`（2026-08-25 12:07，md5 `a73c038e8cdf9b43ba58b9fffe93a751`）
- 方式：**全新解压 → 清理全部旧 IIS 站点（140+）→ 源码重新构建 → 提权部署 → Playwright 真实浏览器 + Android 9 模拟器真实 APK（com.kuaipu.easy 4.6.41）实操**，非模拟推断。
- 部署实例：`GatewayAudit0827`（业务 60500 / 管理 60501，监听 `*`）、`MockAudit0827`（60505）、HTTPS 443 `gateway.kuaipu.com.cn`（证书 409F…2410）。
- 证据目录：`E:\验证页面\_audit_20260827\`（截图 shots/、脚本、日志、transcript）。

## 一、实测全部通过的项目

浏览器（Chromium 真实操作，27+12 项断言）：
- healthz 双端口 200；POST healthz → 405 + `Allow: GET, HEAD`；管理端口 `/` → 404；业务端口 `/Admin` → 404；未知站点 → 404。
- 设备申请：错误企业口令 → 403「企业口令不正确」；缺 CSRF 提交被拒；正确提交 → 待审核。
- 管理端：错误密码 401；登录成功；审批缺 CSRF 被拒；批准成功（`notice=approved`）。
- 审批后 `/portal` 自动放行 → mock 登录页；mock 错误密码提示、正确密码进门户页；`/proxy/demo/api/ping` 返回 `proxiedBy:demo`；mock 登出回登录页；管理端撤销授权（`notice=revoked`）后业务侧重新被拦截；管理端登出正常。
- Host 校验：未配置 Host 一律 421 Misdirected Request（符合设计）。

安卓真实 APP（模拟器 kp-test，系统 CA 已装入，设备 hosts `10.0.2.2 gateway.kuaipu.com.cn`）：
- HTTP 账套 `http://10.0.2.2:60500/`：blur 后出现企业 ID 字段 → 填 demo/mock-user → 登录自动创建移动设备申请（DEV-260827-ED44E8）→ 管理端批准 → 重试登录成功进入工作台 → 登录后业务 API（`/WCFService/PostBus.ashx/DoAction/*`）全部 200。
- HTTPS 账套 `https://gateway.kuaipu.com.cn/`：TLS 无任何证书警告（WebView 与 APK 均信任），云端预登录 `POST /plat/api/app/account/login`（443）→ 200 `code=0`，随后 `/user/login` 成功进入工作台。重启 APP 后账套地址正确记忆为 `https://gateway.kuaipu.com.cn/proxy/d…`。
- 云端预登录负向：错误用户 → `code=1`；浏览器 UA POST 该路径 → 401（不放行）。

其他：
- 包内单元测试：`dotnet test` **166/166 全部通过**（net472，xunit）。
- 匿名云预登录路径**有**速率限制：实测 260 个并发 POST，前 240 个 200，之后 429（UnsafeRequestRateLimit 生效）——推翻了静态审查中"无节流"的担忧。

## 二、新发现的问题（按严重度）

### 中

1. **会话亲和 Cookie 只在会话创建时签发一次，30 分钟绝对过期、不滑动续期**
   `ManagedReverseProxy.cs`（`SetUpstreamAffinityCookie`/`RememberSessionEndpoint`/`SelectEndpointIndex`）：`gw_upstream_affinity` 的 `Expires = 签发时 + 30min`，而内存 map 是 30 分钟**闲置滑动**过期，两者语义不一致。活跃超过 30 分钟的会话丢弃 cookie 后，若网关进程回收（内存 map 丢失），端点选择回退到 `hash(sessionId)`，与首请求按 `ip:X` 选中的端点可能不同 → 上游 InProc 会话掉线——即该机制声称要防的问题在 30 分钟保护窗外依旧存在。建议命中有效亲和时滑动重签 cookie。

### 低

2. **部署脚本不校验/不规范化 `-GatewayListenAddress "0.0.0.0"`**（真实复现 2 次：0825 的 59910、本次 60500）
   WAS 报「绑定未包含任何有效地址」并停用整个站点（`0.0.0.0` 不是合法 IIS 绑定地址，`*` 才是）。脚本直到最后健康检查才失败，留下建好的坏站点和已改写的 ACL。建议映射为 `*` 或显式拒绝。

3. **非回环监听时 HostNames 只生成 `DisplayHost:Port` 一条，部署自检却用豁免 Host 校验的 healthz**
   结果：脚本报告部署成功，但浏览器用 `127.0.0.1`/`localhost`/LAN IP 访问全部 421。建议自检加一个经过 Host 规则的真实路径，或在报告中提示 HostNames 白名单。

4. **部署脚本重写站点目录 ACL（关继承，仅 AppPool RX + Administrators/SYSTEM）**
   解压 zip 的普通用户随即失去对自己源码目录的读权限（本次实测复现）。README 只说"授予应用池身份"，未说明会剥离既有用户权限；对在用户目录下解压部署的场景不友好。

5. **云预登录路径匹配实为 EndsWith 后缀语义，与"exact"注释不符**
   `LegacyAppCompatibilityPolicy.cs:82-86`：`/proxy/demo/plat/api/app/account/login` 等后缀变体也会匿名放行。后缀必含完整 pattern 且上游最终裁决，默认配置无实际危害；建议改为站点相对路径精确匹配或修正注释。

6. **`IsNativeAppHealthProbeRequest` 新纳入 `MobileUserAgentMarkers` 的配置 footgun**
   若管理员把 `Android` 等宽标记配进该列表，普通手机浏览器 GET `/` 会收到裸 `gateway-ready` JSON 而非站点内容。默认配置无影响；建议 Normalize 时对已知宽泛标记告警。

7. **SessionAffinity 每请求在全局锁内做两次 LINQ 全表修剪（最多 8192 次迭代 + 2 次分配）**
   无泄漏/死锁，属高并发下锁保持与 GC 压力；4096 容量驱逐可被反复匿名触发新会话灌满（受害面仅限 affinity cookie 已过期的客户端）。建议修剪降频。

8. **写锁 fail-fast 化把锁竞争误记为「预算耗尽」**
   `LegacyGatewayRepository.cs:3707`：进程内锁抢不到立即抛"预算耗尽"异常，诊断语义失真；仅单进程多库文件场景有实际行为变化。建议加独立诊断原因码。

### 测试基建

9. `tests/GatewayDemo.Legacy.Tests` 是 SDK 风格（Microsoft.NET.Sdk）net472 项目，README 只声明需要 VS Build Tools——纯 BuildTools 的 MSBuild 无法解析该 SDK（实测报错 MSB4236），需另装 .NET SDK 才能跑测试。建议在 README 测试章节注明。

## 三、澄清（确认为非 bug）

- **0825 验收中"HTTPS 账套失败"系误判**：APK 的 `isInnerIPFn`（app-service.js）只对 10/8、172.16/12、192.168/16、127/8 的内网 **IP** 账套地址显示"企业 ID"字段；填 https 域名走公有云模式，本无第二字段。本次 HTTPS 全流程（含云端预登录）已在真实 APK 上通过。
- APP 登录后向 `gateway.kuaipu.com.cn/push/app/device/set` 得 401：这是测试拓扑把云主机劫持到本网关所致，本网关本就不该放行推送注册；生产上该主机是真实云服务，不经过本网关。
- APP 工作台「,您好!」缺名字：mock `/user/login` 响应不携带显示名字段，属 mock 展示瑕疵，非网关问题。
- 宿主 hosts 遗留的 `10.0.2.2 gateway.kuaipu.com.cn`（0825 会话所加）对宿主浏览器有害（解析到不可达地址）；已改为 `127.0.0.1`，模拟器侧改用设备内 `/system/etc/hosts`（已验证生效）。

## 四、结论

包的核心链路（设备申请/审批、浏览器代理、移动端 APP 全流程 HTTP+HTTPS、云端预登录、速率限制、单元测试）在真实浏览器和真实 APK 上均验证通过，未发现阻塞性缺陷。建议优先处理 #1（会话亲和 cookie 不续期）和 #2/#3（部署脚本在非法监听地址与 Host 白名单下的假成功），其余为低优先级打磨项。
