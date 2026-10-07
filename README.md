# SW2MuJoCo

SolidWorks 2025 插件：配置 URDF 树、碰撞几何、关节、传感器、执行器、闭链约束、恒力和弹簧，导出 URDF / MJCF，并使用本地 MuJoCo 预览。

当前交付版本：**3.2.1**。基于 ROS SolidWorks URDF Exporter，保留原作者署名与许可证；官方基线和本地更改见 [源码来源](docs/SOURCE_PROVENANCE.md)。原官方 README 保留在 [历史目录](docs/history/upstream-README.md)。

## 安装与使用

本地最终安装包：`dist/SW2MuJoCo_3.2.1_SW2025_x64_Setup.exe`，旁边的 `.sha256` 用于校验。安装前关闭 SolidWorks。安装包不加入 Git；从源码获取仓库后需自行构建。

- [离线图文使用指南](docs/SW2MuJoCo_3.2.1_使用指南.html)
- [Markdown 使用指南](docs/SW2MuJoCo_3.2.1_使用指南.md)
- [文档导航](docs/README.md)
- [3.2.1 稳定引用审计](docs/SW2MuJoCo_3.2.1稳定引用审计.md)

Python、MuJoCo 和可选减面工具由本地环境提供，插件不附带 Python 环境。URDF 导出可选附加配置；MJCF 交付目录只包含 XML 与 `meshes/*.stl`。

## 工作目录

| 路径 | 用途 |
|---|---|
| `SW2URDF/`、`SW2URDF.sln` | 插件源码和解决方案；内部项目名保留兼容性 |
| `runtime/python/` | 生产 Python 后端（安装时部署为 `mujoco_backend/`） |
| `tools/build/`、`tools/install/`、`tools/docs/` | 构建、注册和生成指南 |
| `tests/` | 按内容分类的回归、差分、CAD 集成和官方测试 |
| `docs/` | 当前使用指南、审计和开发说明 |
| `docs/history/` | 旧版指南与阶段记录，保留在 Git 中 |
| `dist/` | 当前安装包及校验文件，不进入 Git |
| `build/runtime-release/` | 当前已验证插件的构建载荷 |
| `build/native-parity/` | 差分测试运行器 |
| `build/` 中的报告与隔离 fixture | 最终验收证据，不进入 Git |
| `_archive/` | 旧安装包、旧构建、实验脚本、备份和诊断，本地归档，不进入 Git |
| `packages/`、`INSTALL/tools/` | NuGet 依赖与本地安装器编译工具，不进入 Git |
| `examples/` | 官方示例 |

归档迁移清单为 `_archive/workspace-*/manifest.json`。需要旧资源时按清单查找；当前生产构建不依赖归档目录。历史文档中的旧 `build/`、`dist/` 路径对应归档前的位置。

## 构建与验证

需要 Visual Studio 2022 的 .NET 桌面开发工具、.NET Framework 4.8、SolidWorks 2025 API 程序集和已还原的 NuGet 包。使用 MSBuild 构建 `SW2URDF/SW2URDF.csproj`，设置 `Configuration=Release`、`Platform=x64`、`SolutionDir` 为仓库绝对路径、`SolidWorksDir` 为 SolidWorks 安装目录，以及 `OutputPath` 为仓库下的 `build/runtime-release/`。

生产安装器入口为 `INSTALL/install.iss`，默认读取 `build/runtime-release/`；可用 Inno Setup 的 `/DPayload=<绝对路径>` 指定其他载荷。旧安装入口可通过 Git 历史追溯。

当前身份与界面回归可运行以下脚本，必要时显式传入 `-Payload runtime-release`：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/identity/Test-StableReferenceAudit.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/ui/Test-CollisionNavigation.ps1 -Payload runtime-release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/architecture/Test-ProductionBoundary.ps1 -Payload runtime-release
```

MuJoCo 编译语义和动力学差分见 `tests/parity/run.py`；后端回归见 `tests/backend/test_*.py`。COM 验证使用 `tests/incremental/Verify-NativeIncrementalV1.ps1 -Payload runtime-release -NativeOnly -IdentityLifecycle -CadReferenceLifecycle`，须在没有已有 SolidWorks 文档的独立测试环境运行。

[Python Tool Backend 边界](docs/SW2MuJoCo_ToolBackend.md) · [许可证](LICENSE)
