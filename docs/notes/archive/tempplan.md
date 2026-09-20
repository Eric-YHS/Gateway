# GatewayDemo.Legacy 修复实施与验收计划

## 1. 文档目的

本文档交给 coding agent 落地修改，我负责修改后的独立验收。

被测交付包：

- 原包：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`
- SHA-256：`3EF8B0D37BBBA4EF00B4C3ACD02C137FD32AC1F3087190C0D7B03C00EC5F42DD`
- 首轮八项报告：`E:\验证页面\output\gateway-repro-20260812-1215\复现报告.md`
- 机器 IIS 与 APK 补充报告：`E:\验证页面\output\gateway-machine-iis-20260813-0100\机器IIS与原生APK补充复现报告.md`
- APK 原始响应汇总：`E:\验证页面\output\gateway-machine-iis-20260813-0100\evidence\apk-runtime-responses.json`

本计划的核心原则：只修复确认缺陷，不把正常的授权状态提示误修成放行逻辑。

## 2. 结论与修改范围

### 2.1 确认需要修复的缺陷

真实 Android APK 在登录按钮触发后，业务登录前先发送：

```http
GET /ashx/sys.ashx?action=version HTTP/1.1
User-Agent: Mozilla/5.0 (...; wv) ... uni-app Html5Plus/1.0 (Immersed/24.0)
```

这个启动探测按客户端时序本来就不会携带 `DeviceImei`、`MachineId`、`DeviceNo` 或 `SessionKey`。当前交付包却在真正的 `/user/login` 之前阻断它，APK 原生弹窗显示：

```text
移动 APP 请求缺少可识别的 DeviceImei、MachineId 或 DeviceNo。
```

因此真实 APK 无法自然进入“登录请求 → 自动创建设备申请 → 管理员审批”的流程。

证据：

- `E:\验证页面\output\gateway-machine-iis-20260813-0100\evidence\apk-native-missing-device.png`
- 当前配置：`src\GatewayDemo.Legacy.Web\App_Data\gateway-sites.json`
- 关键代码：
  - `src\GatewayDemo.Legacy.Web\Infrastructure\ProxyRequestModule.cs`
  - `src\GatewayDemo.Legacy.Web\Infrastructure\DeviceCredentialService.cs`
  - `src\GatewayDemo.Legacy.Web\Infrastructure\LegacyAppCompatibilityPolicy.cs`
  - `src\GatewayDemo.Legacy.Web\Infrastructure\MobileClientDetector.cs`
  - `src\GatewayDemo.Legacy.Core\Options\LegacyAppProfileOptions.cs`

### 2.2 第 6、8 项不是待修缺陷，但必须加回归保护

第 6 项“设备尚未获批”是新设备申请后的正常阻断状态。必须保持：

- 未批准设备不能访问受保护业务。
- 原生 APP 返回移动 APP JSON 契约，而不是浏览器 HTML 申请页。
- 后台必须产生且展示完整待审申请，包括准确目标路径。

第 8 项“移动 APP 登录会话已失效”是撤销、重新申请并重新批准后，旧 `SessionKey` 仍被客户端重放时的正常安全响应。必须保持：

- 重新批准不能复活撤销前的旧 SessionKey。
- 旧 SessionKey 返回 `session_expired` 并要求重新登录。
- 新登录成功后只能使用新签发会话。

### 2.3 不在本次修复范围内

除非 coding agent 在修改中引入回归，否则不要改动以下现有行为：

- 第 1 项：撤销响应、重复撤销响应、仅设备号识别、撤销 HMAC 格式，现包验收通过。
- 第 2 项：管理后台申请路径已正常显示。
- 第 3 项：SQLite 锁竞争下有慢写，但最终成功；本次没有确认数据库异常缺陷。
- 第 4 项：上游业务 403 属正常透传；未复现静态资源误拦截。
- 第 5 项：毫秒配置语义正确。
- 第 7 项：客户端断连已正确收口为 `ClientDisconnected`。

## 3. 根因分析

当前代码实际上已经定义了 bootstrap 语义：

```text
BootstrapPathPatterns = /sys.ashx, /ashx/sys.ashx
BootstrapActions = version, fileversion, tenant_name
VersionActions = version, fileversion
```

`ProxyRequestModule` 也在正式解析设备凭据之前执行：

```csharp
if (LegacyAppCompatibilityPolicy.TryHandleCompatibilityRequest(context, site))
{
    context.ApplicationInstance.CompleteRequest();
    return;
}
```

之后还会计算：

```csharp
credentialService.ShouldAllowAnonymousLegacyBootstrapRequest(context)
```

问题在于当前交付配置中：

```json
"NativeAppUserAgentMarkers": ["KuaipuNativeApp/"],
"NativeAppPathPatterns": ["/user/*", "*/WCFService/*", "/ashx/*"]
```

真实 APK 的 UA 不是 `KuaipuNativeApp/`，而是 `uni-app Html5Plus/1.0`。同时 `NativeAppPathPatterns` 是非空集合；`MobileClientDetector.GetClientEntryType` 要求 UA 命中后还必须命中这些路径。理论上 `/ashx/sys.ashx` 应命中，但现场结果表明 bootstrap 描述符没有在最早兼容处理阶段稳定成为可匿名请求，最终进入 `GetOrCreateContext` 的“原生 APP 无设备标识”异常分支。

可能的具体触发点包括：

1. `TryHandleCompatibilityRequest` 仅在 `LegacyAppVersionResponse` 非空时本地短路；为空时返回 `false`。
2. 随后的 `ShouldAllowAnonymousLegacyBootstrapRequest` 依赖 `DescribeLegacyAppRequest` 对 UA、路径、action 的再次组合识别；这段识别容易受 `NativeAppPathPatterns`、大小写、路径映射或请求导航分类影响。
3. 当前配置把整个 `/ashx/*` 放进原生路径集合，既掩盖 bootstrap 与受保护 ASHX 接口的边界，也容易诱导直接将整个目录匿名化的错误修法。

coding agent 必须先用自动化测试固定实际失败点，再选择最小修改；不要只在 `gateway-sites.json` 中把整个 `/ashx/*` 加到 `AnonymousAllowedPaths`。

## 4. 修复目标和安全不变量

修复后的系统必须同时满足：

1. 真实原生 UA 对精确 bootstrap 路径执行允许动作时，无需任何设备凭据即可完成。
2. 只允许 `GET` 或 `HEAD`；`POST`、`PUT`、上传或带业务正文的请求不能借 bootstrap 绕过授权。
3. 只允许精确路径 `/sys.ashx`、`/ashx/sys.ashx`，以及系统明确支持的站点根映射等价路径。
4. 只允许动作 `version`、`fileversion`、`tenant_name`；未知 action 不得匿名放行。
5. query 参数名和 action 值应按既有兼容策略大小写不敏感；不能因额外无害 query 参数退化为设备异常，但也不能让额外参数改变动作语义。
6. `/ashx/*` 中其他接口仍需现有授权；尤其上传、业务查询、删除/更新接口不得匿名。
7. 普通桌面浏览器、普通移动浏览器、微信内置浏览器不能仅凭访问该路径被提升为原生 APP 设备。
8. bootstrap 请求不得创建 managed device、访问申请、授权、恢复 cookie或 SQLite 审计噪声。
9. bootstrap 上游若返回外域或登录重定向，继续执行现有的匿名 redirect 抑制策略，不能成为开放重定向。
10. 未审批、撤销、重复撤销、仅设备号、重新批准后旧会话等状态机行为保持不变。

## 5. 推荐解决方案

### 5.1 首选：建立单一、显式的 bootstrap 判定入口

建议在 `LegacyAppCompatibilityPolicy` 中增加或重构一个无副作用的统一判定方法，例如：

```csharp
public static bool IsAnonymousBootstrapRequest(
    HttpContext context,
    GatewaySiteOptions site,
    out LegacyBootstrapKind kind)
```

该方法只负责判定，不读取请求正文、不创建设备、不触碰数据库。判断顺序建议为：

1. `context`、`Request`、`site`、profile 均有效且 profile 已启用。
2. HTTP 方法只能是 `GET` 或 `HEAD`。
3. 请求不得带正文：`ContentLength == 0`、`TotalBytes == 0`、无 `Transfer-Encoding`。
4. 路径必须命中 `BootstrapPathPatterns`，不要用宽泛的 `NativeAppPathPatterns` 决定 bootstrap 所有权。
5. `action` 必须命中 `BootstrapActions`；是否兼容空 action 应由一个明确配置项控制。根据实测 APK，`action=version` 已足够，默认建议不匿名接受空 action。
6. UA 必须命中原生运行时 marker 或站点配置的专用 marker。真实 APK 至少需要识别：`uni-app`、`html5plus`；这些已存在于 `NativeRuntimeOnlyMarkers`。
7. 对 `Sec-Fetch-Mode:navigate` 的 WebView 请求不能无条件排除。bootstrap 是原生 APP 启动 API，应由方法、路径、action、UA 四项共同收窄，而不是仅因被识别为 document navigation 就失败。

`TryHandleCompatibilityRequest` 与 `ShouldAllowAnonymousLegacyBootstrapRequest` 必须共同调用这一单一判定方法，禁止各自重复一套稍有差异的条件。

### 5.2 响应方式

两种模式都要支持：

#### 模式 A：本地兼容响应

若站点配置了：

```json
"LegacyAppVersionResponse": "6.26.0"
```

则网关直接返回：

```http
HTTP/1.1 200 OK
Content-Type: text/plain; charset=utf-8
Cache-Control: no-store

6.26.0
```

`HEAD` 返回同状态和内容类型，但没有响应正文。

#### 模式 B：匿名代理到上游

若本地响应为空，则允许这一个精确 bootstrap 请求以 `device=null` 代理到配置上游。继续设置匿名 bootstrap 上下文，限制上游重定向，并保留超时、响应大小和错误分类。

不要通过 `AnonymousAllowedPaths: ["/ashx/sys.ashx"]` 作为核心逻辑，因为该列表不能表达 action 和 HTTP 方法约束；如果为兼容部署仍保留这项配置，也必须在代码中叠加精确 action/method 校验。

### 5.3 调整配置边界

建议将示例配置中的：

```json
"NativeAppPathPatterns": ["/user/*", "*/WCFService/*", "/ashx/*"]
```

改为更精确的受保护入口，至少不要靠 `/ashx/*` 来声明 bootstrap：

```json
"NativeAppPathPatterns": [
  "/user/*",
  "*/WCFService/*",
  "/ashx/ChatUploadImg.ashx"
]
```

具体上传端点应以交付 README 和真实 ERP 接口清单为准。`BootstrapPathPatterns` 单独负责 `/ashx/sys.ashx`。

如果实际 ERP 还有其他原生 ASHX 接口，不应简单删除覆盖；应逐个加入显式路径或保留 `/ashx/*` 仅用于“原生请求分类”，但匿名 bootstrap 判定仍必须独立且严格。

### 5.4 避免重复解析请求

`DescribeLegacyAppRequest` 可能解析 `Request.Form` 或 JSON。bootstrap 为 GET/HEAD，不应触发正文解析。建议：

- 在 `HttpContext.Items` 中缓存 `LegacyAppRequestDescriptor`。
- `TryHandleCompatibilityRequest`、`ShouldAllowAnonymousLegacyBootstrapRequest` 和后续设备解析复用同一描述符。
- descriptor 缓存必须是单请求生命周期，不得跨请求共享。

这既消除识别分歧，也降低 System.Web 输入流被提前消费的风险。

### 5.5 诊断信息

给 bootstrap 请求记录结构化但低噪声的诊断：

```text
clientEntryType=native-app
bootstrap=true
bootstrapKind=version|tenant-name
bootstrapDisposition=local-response|anonymous-upstream|rejected
matchedPathPattern=/ashx/sys.ashx
action=version
```

不要记录设备密钥、SessionKey、密码或完整 query。正常 bootstrap 不应写入错误日志。

## 6. Coding agent 的实施步骤

1. 不直接修改原 ZIP。在新目录解压并记录新旧 SHA-256。
2. 完整阅读 README、解决方案和相关配置。
3. 先添加失败测试，证明真实 UA + `/ashx/sys.ashx?action=version` 当前错误进入设备凭据分支。
4. 实现统一的 bootstrap 判定方法。
5. 让本地兼容响应和匿名上游响应复用同一判定。
6. 缩窄或明确示例 `NativeAppPathPatterns`，并更新 README 的配置说明。
7. 增加状态机回归测试，覆盖未审批、审批、撤销、重复撤销、仅设备号、重新批准、旧 SessionKey。
8. Release 编译整个 `GatewayDemo.Legacy.sln`。
9. 创建新的 ZIP；不得包含测试数据库、日志、绝对本机路径、真实账号、APK、截图、`bin/obj` 临时垃圾或本计划文件，除非交付规范明确要求。
10. 提供变更摘要、测试命令、测试结果、新 ZIP 路径和 SHA-256，交给我做独立验收。

## 7. 自动化测试矩阵

如果项目目前没有测试工程，建议新增一个 .NET Framework 兼容测试工程，或提取纯判定函数以进行单元测试。至少覆盖：

### 7.1 应匿名允许

| 方法 | 路径 | action | UA | 期望 |
|---|---|---|---|---|
| GET | `/ashx/sys.ashx` | `version` | 真实 `uni-app Html5Plus/1.0` | 200，无设备创建 |
| HEAD | `/ashx/sys.ashx` | `version` | 同上 | 200，无正文 |
| GET | `/sys.ashx` | `fileversion` | 同上 | 200 |
| GET | `/ashx/Sys.ashx` | `tenant_name` | 同上 | 配置响应或安全代理 |
| GET | `/ASHX/SYS.ASHX` | `VERSION` | 同上 | 大小写兼容 |

### 7.2 必须拒绝匿名或走普通浏览器流程

| 方法 | 路径/action | UA | 期望 |
|---|---|---|---|
| POST | `/ashx/sys.ashx?action=version` | 原生 UA | 不得匿名 |
| GET | `/ashx/sys.ashx?action=delete` | 原生 UA | 不得匿名 |
| GET | `/ashx/other.ashx?action=version` | 原生 UA | 不得匿名 |
| GET | `/ashx/ChatUploadImg.ashx` | 原生 UA、无凭据 | 不得匿名 |
| GET | `/ashx/sys.ashx?action=version` | 桌面 Chrome UA | 不得被提升为原生 APP |
| GET | 同上 | 普通 Android Chrome UA | 走移动浏览器语义，不得创建设备/授权 |
| GET | 同上 | 微信 UA | 不得误判原生 APP |
| GET | `/ashx/sys.ashx?action=version` + 非零正文/Transfer-Encoding | 原生 UA | 不得匿名 |

### 7.3 状态机回归

必须覆盖下列完整序列：

```text
New APK
  -> BootstrapAllowed
  -> LoginRequested
  -> RequestPending
  -> Approved
  -> LoginSucceeded(SessionKey-A)
  -> Revoked
  -> Reapplied
  -> Reapproved
  -> Replay SessionKey-A => SessionExpired
  -> FreshLogin => SessionKey-B
```

每一步验证 UI、HTTP、SQLite 和日志。核心不变量：

- bootstrap 不创建任何设备或申请。
- 第一次 `/user/login` 才创建设备和待审申请。
- 未批准前不能到达上游登录。
- 审批后才能拿到 SessionKey-A。
- 撤销后 SessionKey-A 永久失效。
- 重申请/重新批准不会复活 SessionKey-A。
- 新登录只签发 SessionKey-B，且 A、B 不相等。

## 8. 第 1、2、6、8 项专项回归标准

### 8.1 撤销响应

撤销后的旧 SessionKey 请求必须返回：

```text
外层兼容状态：HTTP 200
X-Gateway-Original-Status: 403
X-Gateway-Reason: authorization_revoked
GatewayReason: authorization_revoked
GatewayMessage: 非空撤销提示
ReapplyRequired: true
ReloginRequired: true
ClientNotificationSuppressed: false
DoActionResult.MessageList: 非空
```

连续至少 3 次请求，每次都必须有独立 requestId 和完整 `GatewayMessage`。

仅携带 `DeviceImei/MachineId/DeviceNo`、不携带旧 SessionKey 时，仍应识别同一撤销设备并返回同一撤销语义。

### 8.2 申请路径

管理后台必须显示：

- 首次登录申请：`/user/login`
- 受保护接口触发的重申请：实际接口路径，例如 `/WCFService/PostBus.ashx`
- 授权卡片中的申请路径与审批记录一致

不得只显示站点、端口或 `-`。

### 8.3 未审批提示

真实 APK 新设备提交登录后：

```text
GatewayBlocked: true
Code: 403
Msg/Message: 移动 APP 设备尚未获批，请先在网关后台完成审批。
```

同时后台存在一条待审申请。此行为验收为通过，不能改成直接登录成功。

### 8.4 重新批准后的旧会话

旧 SessionKey 必须返回：

```text
外层兼容状态：HTTP 200
X-Gateway-Original-Status: 401
X-Gateway-Reason: session_expired
GatewayReason: session_expired
GatewayMessage: 移动 APP 登录会话已失效，请重新登录。
ReapplyRequired: false
ReloginRequired: true
ClientNotificationSuppressed: false
```

此行为验收为通过，不能改成继续代理到上游。

## 9. Coding agent 自测环境

建议使用独立机器 IIS，而不是只跑 IIS Express：

- 每轮创建唯一站点名、应用池、端口和工作目录。
- UAC 提升后确认 `High Mandatory Level (S-1-16-12288)`。
- 确认站点由真实 `w3wp.exe` 承载。
- 使用交付包内 mock backend，不访问真实 ERP。
- 原始 ZIP、APK 和测试数据库保持隔离。

真实 APK：

- 文件：`E:\验证页面\artifacts\kuaipu-app-android_4.6.41.apk`
- SHA-256：`34CAFED0457E04F095C56F1F0F67E247A9B39EDDC0A0674D1B780CA24A588F84`
- 包名：`com.kuaipu.easy`
- 版本：`4.6.41 (40641)`
- 已知真实 UA 包含 `uni-app Html5Plus/1.0`。

APK 还依赖快普外部云端租户激活 API。本地网关测试不应把外部 SaaS 成败算作交付包结果。coding agent 至少要做到：

- 未经注入的真实登录按钮不再卡在版本探测的设备缺失错误。
- 若外部 SaaS 无法激活，使用明确记录的 APK 进程内测试钩子继续本地 `/user/login`，并说明绕过边界。
- 不得用桌面 curl 结果冒充 APK 原生弹窗验收。

## 10. 我将执行的独立验收

coding agent 提交新 ZIP 后，我将从零开始：

1. 校验 ZIP 哈希、清点内容、确认原包未被覆盖。
2. 新目录解压，Release 重编译，保存完整编译日志。
3. 通过 UAC 创建全新的机器 IIS 站点/应用池/端口，确认真实 `w3wp.exe`。
4. 安装或重置 Android 9 AVD 中的真实 APK。
5. 使用真实登录按钮验证 `/ashx/sys.ashx?action=version` 不再弹出设备字段缺失错误。
6. 验证 bootstrap 没有在 SQLite 中创建设备、申请或授权。
7. 用真实 Chrome 登录管理后台，完成申请、审批、撤销、重新审批。
8. 用 APK 完成未审批弹窗、审批后登录、撤销弹窗、仅设备号、连续三次撤销、旧 SessionKey 重放。
9. 负向测试 POST、未知 action、其他 ASHX、普通移动浏览器和静态资源，确认没有扩大匿名面。
10. 复跑第 3、4、5、7 项关键烟测，防止本次路由修改影响 SQLite、403、超时单位和断连处理。
11. 恢复配置，检查健康状态，删除仅由本轮创建的 IIS 资源和 AVD 运行实例。

## 11. 最终验收门槛

只有全部满足才判定通过：

- 原生 APK 登录按钮不再出现 bootstrap 设备标识缺失弹窗。
- bootstrap 得到正确版本/租户响应，且不创建设备或申请。
- `/user/login` 仍需审批，未审批绝不透传上游。
- 申请路径在真实 Chrome 后台可见。
- 审批后登录成功。
- 撤销响应原生弹窗正常，所有消息字段完整。
- 仅设备号识别正确。
- 连续三次撤销响应不清空 `GatewayMessage`。
- 重新批准后旧 SessionKey 返回 `session_expired`，不能复活。
- 非 bootstrap 的 `/ashx/*` 没有匿名绕过。
- 普通浏览器/移动浏览器没有被误识别为原生 APP。
- Release 编译成功，健康检查正常，SQLite integrity check 为 `ok`。
- 新 ZIP 不含测试污染和敏感凭据。

任何一项失败，coding agent 应提供最短失败序列、HTTP 请求/响应、相关日志、数据库状态和具体代码位置，不得只写“本地无法复现”。
