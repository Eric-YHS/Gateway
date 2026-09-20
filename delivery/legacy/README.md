# Legacy 网关兼容版交付说明

这份交付包面向 `VS2019 + ASP.NET + .NET Framework 4.7.2` 环境，目标是提供一套符合现有技术栈的兼容版工程，而不是继续依赖 `.NET 10 + ASP.NET Core`。

## 交付内容

- `GatewayDemo.Legacy.sln`
- `src/GatewayDemo.Legacy.Core`
- `src/GatewayDemo.Legacy.Web`
- `src/MockBusinessBackend.Legacy.Web`
- `packages/Stub.System.Data.SQLite.Core.NetFramework.1.0.118.0`
- `scripts/legacy/build-legacy.ps1`
- `scripts/legacy/package-legacy.ps1`
- `sql/legacy/GatewayDemo.SqlServer2005.sql`

## 当前状态

- 已兼容 `VS2019 / .NET Framework 4.7.2`
- 交付包已包含编译后的 `bin` 目录，可直接按 IIS 站点方式部署
- 已内置 SQLite，不要求客户额外安装数据库
- 已实现浏览器设备识别、申请、审批、授权、审计
- 已实现管理后台独立入口
- 已实现自包含的 `/proxy/{siteKey}` 反向代理链路
- 当前 APP 主方向是让现有官方 APP 也走统一认证网关
- 已保留自定义 APP 设备凭据签发、HMAC 校验、防重放与撤销失效，作为可选增强项
- 已验证代理会透传 `Authorization` 和自定义业务请求头，不会影响后端现有 API 头机制
- 已验证登录页、根路径图片、CSS、JS、表单提交、登录成功跳转都能随代理路径自动改写
- 已补一个同样基于 `net472` 的配套业务后端工程，整套交付包不再依赖 `.NET 10`
- 当前源码默认已将 `erp-main` 指向快普易系列体验站 `https://j.kuaipu.com.cn:2027/`
- 已按体验站真实链路验证：`/default.aspx -> User/PCLogin -> ashx/winCheck.ashx -> /WebEnterprise/newmain.aspx`
- 已支持通过 profile 机制在上游"公网体验站"和"本地演示站"之间切换，详见 [本地演示站接入说明.md](./本地演示站接入说明.md)
- 切换脚本：`scripts/legacy/set-gateway-profile.ps1`
- 前置检查脚本：`scripts/legacy/check-local-demo-prereqs.ps1`

## 当前需求边界

这份兼容版交付默认按仓库根目录的 [需求边界说明.md](../../需求边界说明.md) 理解：

- 主需求：给现有官方 APP 套网关
- 次需求：浏览器和 APP 都走统一认证
- 非当前主需求：开放给第三方 APP 的自定义接入协议

## 说明

- 默认交付模式为“自包含 managed proxy”，优先保证使用方可以直接打开、编译和部署
- 如果客户现场后续希望切到 `IIS ARR + URL Rewrite`，可以保留同样的站点结构再做替换
- 当前兼容版的主口径是让现有官方 APP 通过网关访问，同时对后端现有 `Authorization` / 自定义 API 头保持透传
- 仓库里保留的 `AppKey / AppSecret / HMAC` 页面与能力，当前应理解为可选增强项，而不是现有官方 APP 的主用方式
- `src/MockBusinessBackend.Legacy.Web` 现在主要用于本地回归验证，不是体验站联调的必需依赖

详细部署方式见 [部署说明.md](./部署说明.md)。
如需将 legacy 网关对接本地演示站点，请先阅读 [本地演示站接入说明.md](./本地演示站接入说明.md)。
