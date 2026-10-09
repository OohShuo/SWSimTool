# 测试运行

测试只使用自有临时数据或冻结 fixture，不修改用户工程。mock UI、编译和物理差分各有不同范围；通过某个入口不表示真实 SolidWorks、viewer 或安装已验收。仓库没有 GitHub Actions 工作流。

## 环境与总入口

Windows 插件回归需要 VS2022 MSBuild、net48 targeting pack、.NET 8 SDK、SolidWorks 2025 API 程序集和 Python。无需启动 SolidWorks 的总入口：

```powershell
.\build.ps1 -Test -SolidWorksDir 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS' -Python 'C:\Python\python.exe'
```

此入口构建插件及测试工程后调用 `tools/build/RunTests.ps1`。MJCF/网格测试另需 MuJoCo、NumPy、fast-simplification 和 PyMeshLab；生产可选的两种 Python 减面工具在完整后端测试中均需准备。Linux PyMeshLab 的 CPU 网格插件可能需要 `libopengl0`。测试环境须单独记录具体依赖版本，运行时依赖不等于冻结测试环境。

## 分层运行

Windows 下使用 `tools/build/RunSuite.ps1 -Tier <层级> -Python <Python路径>`，默认 Fast。

| 层级 | 环境与范围 |
|---|---|
| Fast | .NET SDK、net48 runtime、Python；可复用工程、领域自检、架构与构建/凭据边界 |
| Medium | Fast 加 MuJoCo/网格工具；跨目标与物理差分、后端缓存、工具协议/进程故障 |
| SolidWorksIntegration | 独立空闲 Windows/SW2025；完整回归、隔离 CAD、增量与配置保存重开 |
| Release | 上述环境及管理员权限、Inno Setup；交付载荷、安装/COM 加载/保存/卸载 |

Fast/Medium 是子集，不包含全部当前 UI/身份测试。后两层可能启动 SolidWorks 或执行安装/卸载，需要明确授权。已有 SolidWorks 会话应先退出，只使用新建或复制的自有 fixture；不能通过杀进程绕过保护。

`build.ps1 -Package/-Installer` 默认运行回归并校验源码/载荷哈希凭据。`-SkipTests` 显式跳过测试并记录 `NOT RUN`，不伪造凭据；仍校验交付结构。生成安装器不等于安装验收。

## 定向入口

先构建与源码匹配的生产插件到 `build/bin/SWSimTool.SolidWorks/Release/net48/`；PowerShell 测试按各脚本的 `-Payload` 参数选择载荷，不使用过期 DLL。

```powershell
powershell.exe -NoProfile -File tests/identity/Test-StableReferenceAudit.ps1
powershell.exe -NoProfile -File tests/identity/Test-ConfigurationRebuild.ps1
powershell.exe -NoProfile -File tests/ui/Test-NativePresentation.ps1
powershell.exe -NoProfile -File tests/ui/Test-NativeJointPage.ps1
powershell.exe -NoProfile -File tests/ui/Test-UrdfExportRetry.ps1
powershell.exe -NoProfile -File tests/ui/Test-ToolFormLifecycle.ps1
powershell.exe -NoProfile -File tests/compatibility/Test-V2Persistence.ps1
```

Python 入口按所需依赖与构建产物选择：

```sh
python -B -m unittest discover -s tests/backend -p 'test_*.py'
python -B tests/parity/run.py
python -B tests/architecture/Test-LayeredArchitecture.py
python -B tests/architecture/Test-NativeParameterSurface.py
python -B tests/architecture/Test-UrdfEntryPoints.py
python -B tests/architecture/Test-ReleaseLayout.py
```

`Test-ReleaseLayout.py` 需要已组装的 `build/runtime-release/`。差分 runner 来自 `tests/parity/CandidateRunner.csproj`；工具故障可在构建后用以下 Windows 命令单独运行：

```powershell
.\build\bin\CandidateRunner\Release\net48\SWSimTool.CandidateRunner.exe --tooltest 'C:\Python\python.exe' tests/tools/fake_tool.py
```

## Windows/Linux 可复用层

在具备 .NET SDK、Python/MuJoCo 和网格依赖的任一平台运行：

```sh
python -B tests/platform/run.py
```

脚本构建 net8 CandidateRunner，运行 API/存储/路径/进程/后端与 en-US、zh-CN、de-DE 物理差分。Linux 不构建或运行 SolidWorks 宿主。`--culture` 可选单一语言区域，`--no-build` 复用已构建宿主，`--skip-backend` 缩小范围；缩小运行不能当作完整平台验证。

输出在 `build/reports/platform/<windows|linux>/`。重复运行前归档该次运行生成的旧 culture 目录；脚本拒绝陈旧目录。收集两平台全部三种 culture 输出到同一目录后运行：

```sh
python -B tests/platform/compare.py
```

比较要求六组完整产物、冻结存储字段等价、相同 XML 结构/引用与既有编译语义和多步动力学容差。同一平台的 culture XML 保持一致；跨平台末位数值差异由编译和动力学比较判定。不能放宽容差或修改 fixture 来通过。

直接运行后端测试时，可将 `SWSIMTOOL_CACHE` 和 `SWSIMTOOL_MESH_CACHE` 指向 `build/test-work/` 下独立目录，避免依赖用户生产缓存。分层/平台入口已设置隔离缓存。

## 目录与输出

| 目录 | 内容 |
|---|---|
| `core/`、`upstream/` | 数学、配置、序列化和保留的上游测试；CAD runner 独立 |
| `identity/`、`compatibility/`、`fixtures/` | 稳定引用、生命周期和冻结输入 |
| `model/`、`ui/` | 模型组装、编辑/取消/重试、原生回调与页面生命周期 |
| `backend/`、`tools/` | 网格、缓存、依赖/预算/协议故障、取消/超时/进程清理 |
| `parity/`、`reference/` | 官方 MuJoCo 编译语义/动力学与显式测试参考生成器 |
| `platform/`、`targets/` | net48/net8、OS/culture、大小写和链接边界 |
| `incremental/`、`performance/`、`integration/`、`support/cad/` | 增量、性能、自有 CAD 和安装集成 |
| `architecture/`、`manual/` | 分层/构建/载荷约束及手动界面辅助 |

独立测试工程为 `core/CoreTests.csproj`、`parity/CandidateRunner.csproj`、`upstream/SWSimTool.Tests.csproj` 和 `upstream/runner/TestRunner.csproj`。测试依赖不进入安装包。

产物统一位于 `build/`：`bin/`、`obj/`、`logs/`、`reports/`、`test-work/`、`test-results/`、`native-parity/` 和 `integration/`。记录每次检查的提交、环境、载荷哈希、具体场景及 PASS/FAIL/NOT RUN/BLOCKED；旧日志不能证明新改动已验证。测试输入、冻结 fixture 和比较标准不属于可删除的旧报告。
