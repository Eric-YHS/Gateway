# APP 到 WebView 会话交接

网关提供 `POST /Gateway/WebViewHandoff.ashx` 用于已授权 APP 打开任意业务 URL。

## 流程

1. APP 使用已签发的 `X-Gateway-Device-*` 五个 HMAC 请求头，对 JSON body 签名。
2. body 只包含 `siteKey` 和网关内部 `targetPath`，例如 `{"siteKey":"demo","targetPath":"/random-business"}`。
3. 网关校验设备、授权站点和目标路径后返回一次性 `consumeUrl`。
4. APP 让 WebView 打开 `consumeUrl`；网关原子消费票据，设置 HttpOnly WebView Cookie，并重定向到目标路径。
5. 后续 WebView 请求自动携带 `gw_webview_credential`，不需要把 SessionKey、Secret 或设备号放进 URL。

票据默认 60 秒有效且只能消费一次；WebView Cookie 默认 12 小时有效。撤销设备授权会同时使票据和 WebView Cookie 失效。

没有 APP HMAC 凭据的原生请求不会凭 UA 放行，也不会创建浏览器设备。
