# 仓库目录约定

本仓库同时保留 .NET Framework 4.7.2 主线、现代 .NET 对照实现、部署工具和历史交付材料。为避免验证产物再次堆到根目录，新增文件按以下约定放置。

| 路径 | 用途 |
| --- | --- |
| `src/` | 产品源码；当前维护主线是 `GatewayDemo.Legacy.*` |
| `tests/` | 自动化测试和可复现用例 |
| `scripts/` | 可重复使用的部署、诊断和验证脚本 |
| `sql/` | 数据库初始化及辅助 SQL |
| `docs/` | 架构、指南、历史记录和验证报告 |
| `delivery/` | 随交付包提供的说明文件 |
| `deliverables/` | 当前正式交付包和对外报告；历史包与打包中间目录不纳入 Git |
| `packages/` | Legacy 工程直接引用的 NuGet/二进制依赖 |
| `tools/` | 本机第三方工具链和仓库自有辅助脚本；大型 SDK 不纳入 Git |

## 本地产物

验收运行、截图、日志、解压副本、临时修复目录和工具状态均为可再生或历史材料，不属于源码。新的运行输出统一写到 `output/` 或相应测试工具指定的输出目录，这些路径已加入 `.gitignore`。

2026-08-30 整理前遗留在根目录的材料没有删除，已移动到 `.archive/2026-08-30-cleanup/`。该目录不纳入 Git；如需追溯旧脚本中的绝对路径或恢复某次验证现场，可在其中按 `generated-runs/`、`workspaces/`、`root-evidence/`、`packages/` 和 `private-reference/` 查找。

## 根目录准入

根目录只放解决方案入口、容器配置、依赖清单、总览文档和少量一键启动入口。一次性脚本放到 `scripts/diagnostics/legacy-root/`，报告放到 `docs/reports/archive/`，随手记录放到 `docs/notes/archive/`，不要再直接堆到根目录。
