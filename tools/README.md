# 构建与维护工具

| 入口 | 用途 |
|---|---|
| 根 `build.ps1` | restore/build、回归、载荷组装、安装器与安全清理 |
| `build/RunTests.ps1`、`build/RunSuite.ps1` | 完整回归及分层运行 |
| `build/AssembleRelease.ps1` | 凭据校验及 `build/runtime-release/` 组装 |
| `build/UpdateVersionInfo.ps1` | 构建时生成版本信息 |
| `install/Register-SW2025.ps1` | 管理员手工注册；默认载荷为根 `build/runtime-release/` |
| `docs/Build-Guide.py` | 从当前 Markdown 和图片生成独立离线 HTML |

从仓库根目录生成指南：

```sh
python3 -B tools/docs/Build-Guide.py
```

默认读取 `Version.props`，输出 `build/docs/SWSimTool_<版本>_使用指南.html`，图片嵌入文件。指南源文件保留对应版本名，满足载荷和安装器引用。安装器只读取已组装载荷。

构建参数见 [根 README](../README.md)，注册说明见 [安装文档](../docs/INSTALL.md)，测试入口见 [tests/README](../tests/README.md)。这些工具不属于生产 Python 后端。
