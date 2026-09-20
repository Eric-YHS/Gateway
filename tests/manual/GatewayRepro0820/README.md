# GatewayRepro0820

2026-08-20 会话问题的手工复现脚本。脚本默认把 JSON、日志和截图写到当前目录；可通过 `OUT_DIR`（或个别脚本支持的 `OUT_PATH` / `DB_PATH`）覆盖输入输出路径。

这些脚本依赖本机 IIS、Playwright、WebView 调试端口或特定测试站点，不属于默认自动化测试套件。
