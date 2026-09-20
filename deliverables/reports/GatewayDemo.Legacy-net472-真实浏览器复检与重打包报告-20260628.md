# GatewayDemo.Legacy-net472 真实浏览器复检与重打包报告

检查时间：2026-06-28

## 处理结果

- 已重新处理并覆盖：`deliverables/GatewayDemo.Legacy-net472.zip`
- 覆盖前备份：`deliverables/GatewayDemo.Legacy-net472.before-codex-fullcheck-20260628-153044.zip`
- 新 zip SHA256：`51E81EF17E98D1B3A4C2A33D353AE76FE050FC242013E018977C311F87DC1AA5`
- 备份 zip SHA256：`64EE2E15B32F91C09EA11737E8BAD566F92D4142E4ED02D38CA3760E7C3BC6D9`

## 真实浏览器复检

最终 zip 重新解压后，部署到 IIS 临时端口 `35650/35651/35655`，使用 Playwright Chromium 真实浏览器执行完整流程：

- 访问申请提交、后台登录、审批通过。
- ERP 上游登录，门户、报表、运维点击与报表页刷新。
- 静态资源 CSS/JS/SVG 实际加载，`mock.js` 浏览器标记确认。
- JVS/Vue history 路由 `/jvs-aps-ui/reports?tab=daily` 和浏览器内 fetch API。
- 移动 Web 视口下申请、审批、进入上游页面。
- 移动 APP 登录请求首次拦截生成审批、后台审批、二次请求成功。
- `/proxy/2027/portal` 兼容入口。

最终浏览器报告：`_codex_real_fullcheck_20260628_145502/browser-evidence-final-zip/real-browser-report.json`

结果：`failures=0`，HTTP 状态汇总 `200=49, 301=1, 302=10`，无 `4xx/5xx`，无 console error，共 22 张截图证据。

## 修复与通用化

- `LegacyGatewayConfiguration.LoadSites` 已从只接受站点数组，扩展为同时兼容站点数组和单站点对象。
- 目的：避免运维或脚本热更新单站点时，把 `gateway-sites.json` 从数组误写成对象后，网关加载为空站点列表。
- README 已补充说明：推荐使用站点数组；单站点对象会被兼容读取。
- 在最终解压部署上已真实验证：不重启 IIS，将 `gateway-sites.json` 临时写成单站点对象后，网关下一次请求能自动热更新读取；恢复数组后也正常。

## 备注

部署验证时 Windows IIS-WebSockets 功能仍处于未启用或 pending restart 状态；脚本已提示需要启用 IIS WebSocket Protocol 并重启 Windows 后再依赖 WebSocket 代理。这是当前机器系统功能状态，不是 zip 内源码打包缺失。
