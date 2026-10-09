# 源码来源与署名

SWSimTool 基于 ROS SolidWorks URDF Exporter。原作者为 **Stephen Brawner**（brawner@gmail.com），上游支持者包括 PickNik Consulting、Verb Surgical、Open Robotics 和 Willow Garage。保留源码中的原作者声明与 [MIT LICENSE](../LICENSE)（Copyright 2020 Stephen Brawner）；分发时应同时保留许可证和版权声明。

| 项目 | 值 |
|---|---|
| 官方仓库 | https://github.com/ros/solidworks_urdf_exporter |
| 官方基线提交 | `882169e28952f0d17c87d7eab98826454421aabf` |
| 原工作区基线标签 | `upstream-baseline-882169e`（当前 clone 未包含该标签） |
| 本地导入提交 | `4b49e0a6d7280b98cbe277d2f3be50e10d5208e8` |

本地根提交导入保存的官方源码归档，不包含官方完整 Git 历史，不是官方原始提交。基线后的提交属于本地 SW2025 适配和 SWSimTool 开发。官方基线已经修复 Show 后立即 Dispose 的调用，不能记为本地原创修复。

```sh
git log --oneline 4b49e0a6d7280b98cbe277d2f3be50e10d5208e8..HEAD
git diff 4b49e0a6d7280b98cbe277d2f3be50e10d5208e8 HEAD
```

原官方 README 可从本地导入提交查看：

```sh
git show 4b49e0a6d7280b98cbe277d2f3be50e10d5208e8:README.md
```

官方 `examples/` 示例继续保留。第三方引用由各项目 PackageReference 声明，文档清理不改变依赖及其署名。当前安装器为 `INSTALL/install.iss`，使用说明为 [当前指南](SWSimTool_3.2.1_使用指南.md)。
