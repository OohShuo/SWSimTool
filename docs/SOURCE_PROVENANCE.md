# 官方源码与本地修改

SW2MuJoCo 基于 ROS SolidWorks URDF Exporter，保留官方许可证、原作者署名和基线记录。

| 项目 | 值 |
|---|---|
| 官方仓库 | https://github.com/ros/solidworks_urdf_exporter |
| 官方基线提交 | `882169e28952f0d17c87d7eab98826454421aabf` |
| 本地基线标签 | `upstream-baseline-882169e` |
| 本地导入提交 | `4b49e0a` |

仓库最初没有提交历史。从保存的官方源码归档导入了 170 个文件作为本地根提交；这不是官方原始 Git 提交，也不包含官方完整历史。基线后的提交属于本地 SW2025 适配及 SW2MuJoCo 开发。

官方基线已经修复旧 1.6.1 中 Show 后立即 Dispose 的调用。不要把这一项上游修复记为本地独立开发。

```powershell
git log --oneline upstream-baseline-882169e..HEAD
git diff upstream-baseline-882169e HEAD
git diff --stat upstream-baseline-882169e HEAD
```

原官方 README 在 [history/upstream-README.md](history/upstream-README.md)。旧重复源码快照、下载归档和诊断保存在本地 `_archive/workspace-*/local/`，不进入 Git；官方 `examples/` 示例仍受版本控制。

当前生产安装器为 `INSTALL/SW2025-MuJoCo.iss`，使用说明为 [3.2.1 指南](SW2MuJoCo_3.2.1_使用指南.md)。旧构建入口和早期说明仅保留作历史参考。
