# SWSimTool

SolidWorks 2025 插件：配置机器人 URDF 树、碰撞几何、关节、site、传感器、执行器、闭链约束、恒力和弹簧，导出 URDF / MJCF，并使用本地 MuJoCo 预览。

源码版本为 **3.2.1**，基于 ROS SolidWorks URDF Exporter。原作者署名、官方基线见 [源码来源](docs/SOURCE_PROVENANCE.md)，许可证见 [LICENSE](LICENSE)。安装包由源码构建生成。

## 安装与使用

Windows 上安装 SolidWorks 2025 x64 和 .NET Framework 4.8。关闭 SolidWorks 后运行安装包，在加载项中启用 SWSimTool。MJCF 工具使用用户提供的 Python、MuJoCo 及可选减面工具。

- [安装与环境准备](docs/INSTALL.md)
- [当前使用指南](docs/SWSimTool_3.2.1_使用指南.md)
- [文档导航](docs/README.md)

安装包生成到 `build/dist/SWSimTool_3.2.1_SW2025_x64_Setup.exe`，旁边有 `.sha256`。离线 HTML 指南生成到 `build/docs/` 并随安装包提供；这些构建产物不进入 Git。

## 主要目录

| 路径 | 用途 |
|---|---|
| `src/`、`SWSimTool.sln` | Core、Application、Infrastructure 和 SolidWorks 宿主 |
| `runtime/python/` | 生产 Python 工具后端，安装时部署为 `mujoco_backend/` |
| `tools/` | 构建、注册和指南生成工具 |
| `tests/` | 单元、兼容、差分、UI 与隔离 CAD 测试及 fixture |
| `docs/` | 使用、安装、架构、后端契约和源码来源 |
| `examples/` | 官方示例 |
| `build/` | 编译、中间文件、日志、测试输出和交付载荷，不进入 Git |

产品入口只有 SolidWorks 插件（net48）。Core、Application 和 Infrastructure 同时面向 net48/net8.0；net8 测试宿主用于 Windows/Linux 可复用 API 验证，不是独立产品。架构见 [Architecture](docs/Architecture.md)。

## 从源码构建

在 Windows 上准备 VS2022 MSBuild/.NET 桌面开发工具、.NET Framework 4.8 targeting pack、支持 .NET 8 的 SDK 和 SolidWorks 2025 API 程序集。构建会恢复 NuGet 依赖；测试需要 Python/MuJoCo，安装器需要 Inno Setup 6.5+。

```powershell
.\build.ps1 -SolidWorksDir 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS'
.\build.ps1 -Test -SolidWorksDir 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS' -Python 'C:\Python\python.exe'
.\build.ps1 -Package -SolidWorksDir 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS' -Python 'C:\Python\python.exe'
.\build.ps1 -Installer -SolidWorksDir 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS' -Python 'C:\Python\python.exe' -ISCC 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
.\build.ps1 -Clean
```

默认 restore/build；`-Test` 增加回归；`-Package` 在测试后组装并校验 `build/runtime-release/`；`-Installer` 再生成安装包，不运行安装。也可设置 `SOLIDWORKS_DIR` 或指定 `-MSBuild`。

明确跳过回归时使用 `-Package -SkipTests` 或 `-Installer -SkipTests`，仅构建生产工程，保留载荷检查并记录测试 `NOT RUN`。`-SkipTests` 不能与 `-Test` 或 `-Clean` 同用。

编译/中间文件位于 `build/bin/<project>/<configuration>/<framework>/` 和 `build/obj/<project>/<configuration>/<framework>/`，日志在 `build/logs/`。`-Clean` 只清理指定输出目录，保留 `build/tools/`、worktrees 和未分类文件，不穿越链接或内嵌 Git 工作区；不要用删除整个 `build/` 代替它。

测试环境、分层入口及 Linux 可复用层运行方法集中在 [tests/README.md](tests/README.md)。仓库没有 GitHub Actions 工作流。
