# SWSimTool

SolidWorks 2025 插件：配置 URDF 树、碰撞几何、关节、传感器、执行器、闭链约束、恒力和弹簧，导出 URDF / MJCF，并使用本地 MuJoCo 预览。

当前交付版本：**3.2.1**。基于 ROS SolidWorks URDF Exporter，保留原作者署名与许可证；官方基线和本地更改见 [源码来源](docs/SOURCE_PROVENANCE.md)。原官方 README 保留在 [历史目录](docs/history/upstream-README.md)。

## 安装与使用

本地最终安装包：`build/dist/SWSimTool_3.2.1_SW2025_x64_Setup.exe`，旁边的 `.sha256` 用于校验。安装前关闭 SolidWorks。安装包不加入 Git；从源码获取仓库后需自行构建。

- 离线 HTML 指南由构建生成到 `build/docs/` 并随安装包提供
- [Markdown 使用指南](docs/SWSimTool_3.2.1_使用指南.md)
- [文档导航](docs/README.md)
- [3.2.1 稳定引用审计](docs/SWSimTool_3.2.1稳定引用审计.md)

SWSimTool 使用独立安装与 COM 身份。新旧插件允许并存安装；建议仅启用一个插件编辑同一工程配置。工程内的 `SW2MuJoCo Configuration` v2 配置可继续使用；旧缓存、偏好与外部附加配置不导入。

Python、MuJoCo 和可选减面工具由本地环境提供，插件不附带 Python 环境。URDF 导出可选附加配置；MJCF 交付目录只包含 XML 与 `meshes/*.stl`。

## 工作目录

| 路径 | 用途 |
|---|---|
| `src/`、`SWSimTool.sln` | Core、Application、Infrastructure、SolidWorks 宿主源码和解决方案 |
| `runtime/python/` | 生产 Python 后端（安装时部署为 `mujoco_backend/`） |
| `tools/build/`、`tools/install/`、`tools/docs/` | 构建、注册和生成指南 |
| `tests/` | 按内容分类的回归、差分、CAD 集成和官方测试 |
| `docs/` | 当前使用指南、审计和开发说明 |
| `docs/history/` | 旧版指南与阶段记录，保留在 Git 中 |
| `build/dist/` | 当前安装包及校验文件，不进入 Git |
| `build/runtime-release/` | 当前已验证插件的构建载荷 |
| `build/bin/CandidateRunner/Release/net48/` | 差分测试运行器 |
| `build/` 中的报告与隔离 fixture | 最终验收证据，不进入 Git |
| `_archive/` | 旧安装包、旧构建、实验脚本、备份和诊断，本地归档，不进入 Git |
| `build/tools/` | 本地安装器编译工具；NuGet 使用标准全局缓存，不进入 Git |
| `examples/` | 官方示例 |

归档迁移清单为 `_archive/workspace-*/manifest.json`。需要旧资源时按清单查找；当前生产构建不依赖归档目录。历史文档中的旧 `build/`、`dist/` 路径对应归档前的位置。

## 构建与验证

需要 Visual Studio 2022 的 .NET 桌面开发工具、.NET Framework 4.8 targeting pack、支持 .NET 8 的 SDK、SolidWorks 2025 API 程序集。构建入口会恢复 NuGet 依赖；安装器还需要 Inno Setup 6.5+。Python 与 MuJoCo 用于自动化测试和运行时工具。

```powershell
.\build.ps1 -SolidWorksDir 'D:\sw\sw2025\SOLIDWORKS'
.\build.ps1 -Test -SolidWorksDir 'D:\sw\sw2025\SOLIDWORKS' -Python 'D:\Softwaves\python\python.exe'
.\build.ps1 -Package -SolidWorksDir 'D:\sw\sw2025\SOLIDWORKS' -Python 'D:\Softwaves\python\python.exe'
.\build.ps1 -Installer -SolidWorksDir 'D:\sw\sw2025\SOLIDWORKS' -Python 'D:\Softwaves\python\python.exe' -ISCC 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
.\build.ps1 -Clean
```

默认执行 restore/build；`-Test` 增加回归测试；`-Package` 在测试通过后组装并验证 `build/runtime-release/`；`-Installer` 继续生成 `build/dist/` 的安装包。可指定 `-MSBuild` 或通过 `SOLIDWORKS_DIR` 设置 SDK 位置。

本地只构建安装包、不构建测试工程及运行回归测试时，使用 `-Installer -SkipTests`（也支持 `-Package -SkipTests`）。保留载荷结构和源码目录检查，不要求已有测试凭据；`build/reports/package-Release.json` 将测试状态记录为 `NOT RUN`。默认打包路径仍要求完整测试，不生成虚假的 PASS 凭据。`-SkipTests` 不能与 `-Test` 或 `-Clean` 同用。

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 \
  -Installer -SkipTests \
  -SolidWorksDir 'D:\sw\sw2025\SOLIDWORKS' \
  -Python 'D:\Softwaves\python\python.exe' \
  -ISCC 'D:\solidworks_urdf_exporter-master\build\tools\InnoSetup\ISCC.exe'
```

`-Clean` 只删除 `build/` 下明确的输出目录：`bin`、`obj`、`logs`、`reports`、`test-results`、`test-work`、`native-parity`、`native-parity-obj`、`integration`、`dist`、`runtime-release`、`docs`。保留 `tools`、`worktrees`、未分类的历史缓存和文件；不穿越链接或删除内嵌 Git 工作区。`test-work` 中遇到访问权限或文件占用问题时保留临时文件并警告，不阻止后续构建；主要产物清理失败及安全保护触发仍报错。不提升权限或重置 ACL。

编译与中间文件分别在 `build/bin/<project>/<configuration>/<framework>/` 和 `build/obj/<project>/<configuration>/<framework>/`。日志在 `build/logs/`。安装器 `INSTALL/install.iss` 只读取已组装载荷。HTML 指南从 Markdown 和图片生成，不提交生成文件。

测试说明见 [tests/README.md](tests/README.md)。真实 CAD 增量与保存重开门禁只创建自有 fixture，须在没有已有 SolidWorks 会话时运行；不属于普通无 CAD 测试命令。

[Python Tool Backend 边界](docs/SWSimTool_ToolBackend.md) · [许可证](LICENSE)

构建验证与 CI 的分层入口见 [测试说明](docs/Testing.md)。

产品仅提供 SolidWorks 插件入口（net48）；Core、Application 和 Infrastructure 保留 net48/net8.0 双目标，现代 .NET 测试宿主仅用于自动化验证，不对外发布。

可复用层已在 Windows 和实际 Linux 环境通过三种语言区域的验证：31 个模型、155 次跨平台编译语义与动力学比较，保持原有容差。架构见 [Architecture](docs/Architecture.md)，运行方法及验收范围见 [Testing](docs/Testing.md)。GitHub 工作流已配置，本地等价验证已执行；远程工作流与 GUI 视觉验收尚未执行。
