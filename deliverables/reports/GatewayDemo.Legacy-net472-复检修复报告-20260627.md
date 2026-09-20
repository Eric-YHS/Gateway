# GatewayDemo.Legacy-net472 复检修复报告

检查日期：2026-06-27

## 结论

- 已重新处理交付包：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip`
- 原包已备份：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.before-reprocess-20260627.zip`
- 最终包从 zip 重新解压、构建、部署到 IIS 后，真实浏览器回归 `18/18` 通过。
- 最终 zip 内无 `obj`、PDB、SQLite 运行库数据库、浏览器检查脚本、浏览器产物。
- 最终 zip 保持默认配置：管理端口 `5051`，默认 mock 上游 `localhost:5055`，未写入本次复检端口 `31350/31351/31355`。

## 真实验证环境

- 最终 zip 解压目录：`E:\验证页面\_verify_gateway_reprocess_zip_20260627`
- IIS 网关：`http://localhost:31350`
- IIS 管理后台：`http://localhost:31351`
- IIS mock 后端：`http://localhost:31355`
- 浏览器产物：`E:\验证页面\_verify_gateway_reprocess_zip_20260627\browser-artifacts-finalzip-20260627`
- 浏览器结果：`Summary: 18/18 checks passed`

## 覆盖的真实操作

- 未授权桌面浏览器进入业务入口并提交申请。
- 管理后台登录、查看待审批、审批通过、更新授权。
- 已审批浏览器进入上游登录页并完成登录。
- 根路径资源加载和 MIME 校验：CSS、JS、SVG。
- VUE/SPA 类路径 `/jvs-aps-ui` 直接访问无 IIS 500。
- SPA history 刷新 `/jvs-aps-ui/reports/2026` 和内部 API ping。
- 外部 API allowlist 代理 `/api/ping`。
- WebSocket 代理 echo。
- 移动端 `/` 根探测返回网关 JSON，而不是桌面应用页。
- 移动端 `/user/login` 保持 JSON 响应并进入审批流。
- 管理后台展示移动端元数据。
- `gateway-sites.json` 热更新生效并恢复。
- 浏览器控制台无错误/警告；回归流中无 HTTP 4xx/5xx。

## 修复与通用化

- 修复 `/jvs-aps-ui` 被 IIS defaultDocument 重复配置打成 500.19 的问题，并让 SPA 根路径和 history 路由稳定落到 mock SPA。
- 修复移动端根路径探测 `/` 被当作桌面业务入口的问题，移动设备根探测优先返回 `GatewayReady` JSON。
- 抽出 `GatewayPathUtility`，统一处理旧路径标记、root path 构造/剥离、候选路径去重、白名单匹配、通配匹配和路径拆分，减少转发、配置、授权、外部 API、移动端路径判断里的特殊写法。
- 抽出移动端字段读取逻辑，统一从 form、JSON、UserToken、PostData 里读取同义字段，减少不同移动请求格式导致的漏判。
- 移除旧构建链不兼容写法，包括 `LangVersion 7.3`、`out var`、`out _`、空传播调用及 catch 内 await 相关写法。

## 最终复查

- 最终 zip 文件数：`133`
- staging 源文件数：`112`
- 禁止项匹配数：`0`
- 兼容性模式匹配数：`0`
- 最终解压目录构建：通过
- 真实浏览器回归：`18/18` 通过
