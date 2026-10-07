# 目录整理与测试隔离

生产 Python 后端移至 `runtime/python/`。版本构建、插件注册、指南生成工具分别移至 `tools/build/`、`tools/install/`、`tools/docs/`。安装后的后端目录仍为 `mujoco_backend/`，保持兼容。

测试按用途统一归入 `tests/`，完整分类见 `tests/README.md`。官方测试从生产项目移出至独立 `SW2URDF.Tests` 项目；生产 DLL 不再包含测试类或引用 xUnit、Moq、Castle、MSTest 等测试依赖。安装器改为明确的生产 DLL 白名单，并在升级时清理旧测试依赖。

删除三项过时测试：

| 文件 | 原因 |
|---|---|
| Test-NativeAddin.ps1 | 绑定旧版固定 fixture 和加载入口 |
| Test-CollisionPageFlow.ps1 | 依赖已移除的 URDF 树碰撞入口 |
| Test-CollisionPackage.py | 针对旧网格报告/包结构，现由 backend 和 parity 的发布与碰撞回归覆盖 |

迁移不更改仿真功能、求解参数、动力学容差或用户工程。历史文档保留旧路径作为历史记录；当前文档与测试命令使用新路径。

验证：96 项后端测试、14 项 MuJoCo 编译/动力学差分、35 项工具故障契约、偏好存储、297 项稳定引用、125 项导航、12 项碰撞模式身份、10 项已解析模型、6 项配置构建、4 项生产边界、2 项表单生命周期、3 项发布目录边界均通过。坐标计算/配置存储与 root joint 序列化回归通过。官方测试项目与 runner 编译通过，本轮不执行 CAD/COM 操作。所有 Python 源码及 PowerShell 脚本通过语法检查。
