# GatewayDemo.Legacy-net472 全流程真实浏览器使用检查报告

检查目标：`E:\验证页面\deliverables\GatewayDemo.Legacy-net472.zip` 解压后的网关项目  
检查方式：真实 IIS 部署 + Playwright 无头浏览器端到端操作  
检查日期：2026-06-23  
测试脚本：`E:\验证页面\gateway-full-check-playwright.js`

---

## 1. 检查结论

经过真实浏览器全流程验证，**网关核心功能（设备申请、审批、授权、反向代理、管理后台端口隔离、撤销授权）均可正常工作**。最终 Playwright 测试结果：

```text
通过: 16/16, 失败: 0/16
```

但在部署与配置环节发现多个必须由人工介入才能解决的问题，下面按优先级列出。

---

## 2. 发现的问题

### 2.1 【高】部署脚本在中文路径下把 IIS 站点 physicalPath 设为 "0"

- **位置**：`scripts/legacy/setup-demo-sites.ps1` 第 421 行
- **表现**：当项目路径包含中文（如 `E:\验证页面\...`）时，`appcmd add site /physicalPath:$GatewaySitePath` 写入 IIS 的 physicalPath 会变成 `"0"`，导致所有网关请求 404。
- **复现**：部署后直接访问 `http://DESKTOP-79LK104:24150/healthz.ashx` 返回 404。
- ** workaround（已用于本次检查）**：
  ```cmd
  appcmd set vdir "GatewayDemoLegacyFullCheck/" -physicalPath:"E:\验证页面\deliverables\GatewayDemo.Legacy-net472\src\GatewayDemo.Legacy.Web"
  ```
- **修复建议**：
  1. 脚本中改为使用 `Microsoft.Web.Administration` 或 `New-IISSite` 设置 physicalPath，避免 appcmd 对含空格/中文路径的解析问题；
  2. 或者在 `appcmd add site` 中对 `$GatewaySitePath` 用 `""` 包裹；
  3. 脚本最后增加自检：读取 IIS 中站点 virtual directory 的 physicalPath，与 `$GatewaySitePath` 比对，不一致则报错。

### 2.2 【高】部署脚本未正确更新 Web.config 中的 Admin.ManagementPort

- **位置**：`scripts/legacy/setup-demo-sites.ps1` 第 433 行 `Set-AppSetting -Key "Admin.ManagementPort"`
- **表现**：脚本执行后，`Web.config` 中的 `Admin.ManagementPort` 仍为默认值 `24051`，而 IIS 站点实际绑定到 `24151`。结果：在管理端口访问 `/admin` 时被 `ProxyRequestModule` 判定为“非管理端口上的管理请求”，返回 404。
- **复现**：部署后直接访问 `http://DESKTOP-79LK104:24151/admin` 返回 404。
- **workaround（已用于本次检查）**：手工编辑 `Web.config`：
  ```xml
  <add key="Admin.ManagementPort" value="24151" />
  ```
  然后回收应用池。
- **修复建议**：
  1. 检查 `Save-XmlUtf8` 是否真正写回文件；确认 `$webConfig` 变量在 `Set-AppSetting` 后保存；
  2. 脚本最后读取 Web.config 中的 `Admin.ManagementPort` 与 `$AdminPort` 比对，不一致则报错；
  3. 考虑将管理端口隔离逻辑改为“仅允许管理端口访问 /admin”，而不是同时要求请求端口必须等于配置端口（当前逻辑已如此，但配置必须正确）。

### 2.3 【高】源码中的 gateway-sites.json 使用占位上游地址

- **位置**：`src/GatewayDemo.Legacy.Web/App_Data/gateway-sites.json`
- **表现**：默认配置中 `UpstreamBaseUrl` 为 `http://replace-with-erp-host/`，`EntryPath` 为 `default.aspx`，`HostNames` 为空数组。如果用户未通过脚本部署、或脚本因上述 bug 未能正常写入，网关会把授权后的请求代理到不存在的主机，出现 `Bad Gateway: NameResolutionFailure`。
- **workaround（已用于本次检查）**：手工改为：
  ```json
  {
    "UpstreamBaseUrl": "http://DESKTOP-79LK104:24155/mock/erp-main/",
    "EntryPath": "portal",
    "HostNames": ["DESKTOP-79LK104:24150", "DESKTOP-79LK104:24151"]
  }
  ```
- **修复建议**：
  1. 在源码中把 `UpstreamBaseUrl` 改为相对示例或空字符串，并在启动时给出明确错误；
  2. 脚本在 `-UpstreamBaseUrl` 为空时自动指向 mock 后端（当前脚本已有此逻辑，但需确保脚本被正确执行）；
  3. 在文档中强调必须先运行 `setup-demo-sites.ps1` 或手动配置 `gateway-sites.json`。

### 2.4 【中】静态资源 `/assets/*` 需要设备授权，匿名访问会被拦截

- **位置**：`src/GatewayDemo.Legacy.Web/Infrastructure/ProxyRequestModule.cs`（`IsAnonymousAllowedRequest`）
- **表现**：`gateway-sites.json` 的 `AnonymousAllowedPaths` 默认只包含 `/sys.ashx*`、`/ashx/sys.ashx*`、`/datacenter/*` 等，未包含 `/assets/*`。因此未授权设备访问 `/assets/mock.css` 会被重定向到网关申请页（302）。
- **这是设计如此还是缺陷？** 代码注释明确说明：静态资源不再全局匿名，需要站点显式列入 `AnonymousAllowedPaths`。但从“demo 开箱即用”角度看，mock 后端的 CSS/JS 属于公共资源，应允许匿名访问。
- **修复建议**：在 demo 配置中把 `/assets/*` 加入 `AnonymousAllowedPaths`，或在文档中说明需要显式配置。

### 2.5 【中】网关首页对授权设备会自动重定向到目标入口

- **位置**：`src/GatewayDemo.Legacy.Web/Gateway/Default.aspx.cs` 第 118-125 行
- **表现**：设备获批后访问 `/Gateway/Default.aspx` 不会停留在“已授权”提示页，而是直接 `Response.Redirect(target)` 到 `/portal`（或配置的 `EntryPath`）。
- **影响**：如果上游是真实 ERP，这通常是期望行为；但如果 `EntryPath` 配置为 `default.aspx` 而上游不可用，用户会立刻看到 Bad Gateway，无法再在网关页看到“已批准”状态。
- **修复建议**：
  1. 保留自动跳转，但在跳转前增加一次“目标可达”探测，若上游不可达则停留在网关页并显示警告；
  2. 或增加一个查询参数（如 `?preview=1`）让管理员/测试人员可以查看授权状态而不跳转。

### 2.6 【低】Playwright 测试中 localhost 与主机名混用会导致站点解析异常

- **位置**：测试脚本最初使用 `http://localhost:24150/...`，而 `gateway-sites.json` 的 `HostNames` 配置为 `DESKTOP-79LK104:24150`。
- **表现**：当 Host 头为 `localhost:24150` 时，`TryResolveSiteFromHost` 无法命中任何站点，请求落入 root-site fallback。在特定场景下（例如带有 `gw_last_proxy_site` cookie），`/gateway` 可能被当作上游路径处理，返回 Bad Gateway。
- **修复建议**：
  1. 测试脚本统一使用主机名（已修复）；
  2. 文档中提醒用户访问网关时必须使用 `HostNames` 中配置的地址，或把 `localhost:<port>` 也加入 `HostNames`。

---

## 3. 已验证通过的场景

| # | 场景 | 结果 |
|---|------|------|
| 1 | 健康检查 `/healthz.ashx` | PASS |
| 2 | 网关首页 `/gateway` 显示访问申请 | PASS |
| 3 | 未授权访问 `/portal` 被拦截并重定向到申请页 | PASS |
| 4 | 提交设备申请并生成设备编号/标识 | PASS |
| 5 | 业务端口访问 `/admin` 返回 404 | PASS |
| 6 | 管理后台独立端口登录 | PASS |
| 7 | 管理员审批设备申请 | PASS |
| 8 | 授权后访问网关页自动重定向到后端 | PASS |
| 9 | 授权后 `/portal` 被代理到 mock 后端 | PASS |
| 10 | mock 后端登录后进入真实门户页 | PASS |
| 11 | 静态资源 `/assets/mock.css` 在授权后放行 | PASS |
| 12 | 匿名路径 `/proxy/2027/sys.ashx` 不返回网关拦截页 | PASS |
| 13 | 不存在站点返回 404 | PASS |
| 14 | 设备 Cookie `gw_device_credential` 已设置 | PASS |
| 15 | 撤销授权后再次访问 `/portal` 被拦截 | PASS |
| 16 | mock 后端独立可用 | PASS |

---

## 4. 修复后的关键配置

### Web.config
```xml
<add key="Admin.ManagementPort" value="24151" />
```

### gateway-sites.json
```json
{
  "Key": "2027",
  "UpstreamBaseUrl": "http://DESKTOP-79LK104:24155/mock/erp-main/",
  "EntryPath": "portal",
  "ExposeLegacyAppAtRoot": true,
  "HostNames": ["DESKTOP-79LK104:24150", "DESKTOP-79LK104:24151"]
}
```

---

## 5. 优先级修复清单

1. **最高**：修复 `setup-demo-sites.ps1` 在中文路径下设置 physicalPath 为 `"0"` 的问题。
2. **最高**：修复 `setup-demo-sites.ps1` 中 `Admin.ManagementPort` 未正确写入 `Web.config` 的问题。
3. **高**：在源码或文档中明确 `gateway-sites.json` 必须配置真实的 `UpstreamBaseUrl` 和 `HostNames`，或让脚本在失败时显式报错。
4. **中**：评估是否将 `/assets/*` 加入 demo 的 `AnonymousAllowedPaths`。
5. **中**：为 `Gateway/Default.aspx` 的授权后自动跳转增加上游可达性保护或预览开关。
6. **低**：文档提醒使用与 `HostNames` 一致的地址访问网关。

---

## 6. 备注

- 本次检查过程中为了验证端到端流程，手工修正了 `Web.config` 的 `Admin.ManagementPort` 和 `gateway-sites.json` 的上游/入口/主机名配置。这些修正是部署脚本在正常运行时本应自动完成的。
- 重新打包交付前，建议在 **含中文路径** 的环境中完整运行一次 `setup-demo-sites.ps1`，并执行 `gateway-full-check-playwright.js` 确认 16/16 通过。
