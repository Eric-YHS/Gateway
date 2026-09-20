# GatewayDemo.Legacy net472：WebAuthn 与 HTTPS 识别说明

更新日期：2026-07-15

## 本次修改

- 增加标准 WebAuthn 设备凭据的注册和验证页面：`/Gateway/WebAuthn.aspx`。
- 同时兼容两种 HTTPS 环境：
  - IIS 直接接收 HTTPS：`Request.IsSecureConnection=true`、`HTTPS=on/1` 或 `SERVER_PORT_SECURE=1`。
  - 入口网关终止 HTTPS：仅当来源属于可信代理时，接受 `X-Forwarded-Proto: https`（IIS 中对应 `HTTP_X_FORWARDED_PROTO=https`）。
- WebAuthn 默认关闭，升级后不会改变现有公测的设备申请、审批、Cookie、移动端和代理流程。

## 推荐上线顺序

1. 先保持 `Gateway.WebAuthn.Enabled=false`、`Gateway.WebAuthn.Required=false` 完成原流程回归。
2. 小范围试用时设置 `Gateway.WebAuthn.Enabled=true`、`Gateway.WebAuthn.Required=false`，让已审批设备注册凭据。
3. 覆盖足够设备后再按需设置 `Gateway.WebAuthn.Required=true`。强制模式只约束浏览器入口，现有移动端兼容入口保持原逻辑。

入口网关终止 HTTPS 时，还要将它的实际来源 IP 加入 `Gateway.TrustedProxyAddresses` 或 `Gateway.TrustedProxyCidrs`。不要对任意客户端信任转发协议头。

## 数据库影响

数据库架构由 v4 升到 v5，只新增独立表 `GatewayWebAuthnCredentials` 及其索引，不修改或重写原有设备、申请、审批和审计记录。升级前会生成并校验数据库备份；关闭 WebAuthn 后，原业务流程不会读取该凭据表。

## 已完成验证

- .NET Framework 4.7.2 Release 构建通过。
- 旧 v4 数据库迁移到 v5 后，原设备和申请记录数量保持一致。
- 使用浏览器虚拟认证器完成真实 WebAuthn 注册与断言。
- 验证了 `HTTPS=on` 且无 `X-Forwarded-Proto` 的直接 HTTPS 场景。
- 验证了可信代理下 `HTTP_X_FORWARDED_PROTO=https`、`HTTPS=off` 的 HTTPS 卸载场景。
- 验证了 WebAuthn 默认关闭时，原申请页面和门户流程保持可用。
- 依赖漏洞审计未发现已知易受攻击包。
