# SW2025 / SW2URDF 窗口闪退修复与实测

日期：2026-09-28。结论：**已在本机 SolidWorks Premium 2025 SP1.2 中完成实际菜单操作，Part 窗口稳定打开、关闭重开和导出通过；两 link Assembly 的配置界面及 URDF + STL 导出通过。**

## 1. 问题现象

已安装 SW2URDF 1.6.1（日志 commit `1.6.0-4-g7f85cfe`、程序集 `1.6.7995.38578`）能加载，点击 Tools → Export as URDF 后窗口立即消失，没有异常日志。中文 SW 菜单实际路径为 工具 → Tools → Export as URDF。

## 2. 根本原因与证据

根因是旧版 `SW2URDF.SW.SwAddin.SetupPartExporter()` 主动释放刚显示的非模态窗体：

```csharp
PartExportForm exportForm = new PartExportForm((SldWorks)SwApp);
logger.Info("Showing part");
exportForm.Show();
exportForm.Dispose(); // 1.6.1 中的错误；最新 master 已删除
```

`Show()` 不会等待用户关闭窗口。它返回后立即 `Dispose()` 会销毁窗体及其句柄，不需要抛出异常。因此没有 ERROR、没有 Exception，与用户日志一致。直接 Dispose 也不能指望总是获得正常 Close 路径的 FormClosing/CloseReason；本次增加 Disposed 观察点用于区分。

证据链：

1. 从 ROS 官方 GitHub API 下载最新 master 的完整源码快照，SHA 为 `882169e28952f0d17c87d7eab98826454421aabf`。现有目录不是 Git 仓库，而是 ZIP 解压目录；没有覆盖它。下载快照在 `upstream-reference/`。
2. 修改前，本地 `SwAddin.cs`、`PartExportForm.cs`、`SW2URDF.csproj` 与该快照逐文件 SHA256 相同。
3. 官方 `1.6.1...882169e` 差异显示，`SwAddin.cs` 唯一变化就是移除 `exportForm.Dispose()`。证据 JSON：`diagnostics/upstream-compare.json`。
4. 直接读取本机已安装 `C:\Program Files\SolidWorks Corp\SolidWorks\URDFExporter\SW2URDF.dll` 的 IL，确认实际二进制也存在该调用，不仅是根据源代码推测：

```text
00E2: callvirt Void Show()
00E9: callvirt Void Dispose()
```

完整 IL：`diagnostics/installed-SetupPartExporter.il.txt`，读取脚本：`diagnostics/Inspect-InstalledIL.ps1`。
5. 在原进程中实际点击旧版菜单，窗口未保留，日志仍止于 Showing part。
6. 强类型 COM 探针在 SW2025 上调用原版 `CreateRobotFromActiveModel()`、`PartExportFormLoad()` 均通过；材质数组非空，质量为约 0.0353888855 kg。这排除了该测试模型初始化失败的假设。
7. 新 DLL 在第二个 SW2025 进程中经真实菜单打开，窗口保留；Cancel 后窗口消失，再次菜单打开得到新窗口，随后实际导出成功。

**本问题不是已证实的 GC、DPI、Interop 或 SW2025 特有不兼容。** 局部变量形式在修复后的实际非模态测试中工作正常，没有为了掩盖 Dispose 错误改成 ShowDialog。也未加入全局异常策略、硬编码 HWND 或替换模型计算逻辑。

官方来源：
- https://github.com/ros/solidworks_urdf_exporter
- https://github.com/ros/solidworks_urdf_exporter/compare/1.6.1...882169e28952f0d17c87d7eab98826454421aabf

## 3. 项目与 API 检查

| 项目 | 实际结果 |
|---|---|
| SolidWorks | `D:\sw\sw2025\SOLIDWORKS\SLDWORKS.exe`，API Revision 33.1.2，64 位 |
| Interop | `SolidWorks.Interop.sldworks`、`swconst`、`swpublished` 从 `$(SolidWorksDir)` 本地 DLL 引用；本机文件版本 33.1.2.4 |
| API redist | 本机 `D:\sw\sw2025\SOLIDWORKS\api\redist` 也存在对应 DLL；没有盲目替换 |
| NuGet/GAC | 三个 SW Interop 使用本地 HintPath，不是 NuGet 包；本次用 SolidWorksDir 明确指向 SW2025，不依赖 GAC 寻找 |
| SpecificVersion | 三个 Interop 未显式指定，也未在 Include 锁定程序集版本；solidworkstools 显式 False |
| EmbedInteropTypes | 三个 Interop 均 True，保留；没有证据要求改为 False |
| CPU | Debug/Release x64 为 x64；Prefer32Bit=False。原项目还含 AnyCPU/x86 配置，SW2025 构建请选择 x64 |
| Framework | 原项目 4.5.2；本机该目录只有 XML，缺少引用程序集，MSBuild 报 MSB3644。本次改为本机完整安装的 4.8；CLR v4.0.30319，已在 SW2025 实测加载 |
| 配置文件 | SW2URDF 项目没有 App.config；TestRunner 有 app.config。没有修改 SolidWorks 宿主配置 |
| 注册标识 | ComVisible(true)，GUID `{65c9fc17-6a74-45a3-8f84-55185900275d}`；类 `SW2URDF.SW.SwAddin`，无显式 ProgId 特性 |
| 注册流程 | ComRegisterFunction 写 HKLM SOFTWARE\SolidWorks\Addins\{GUID} 和 HKCU Software\SolidWorks\AddInsStartup\{GUID}；ComUnregisterFunction 删除对应键 |
| HWND | SW2025 本机强类型 `IFrame.GetHWndx64()` 返回 Int64；官方 API 标明该方法供 64 位早绑定使用。本次不需要 owner 更改即可通过测试 |
| UI | 构造器 InitializeComponent + ExportHelper；Load 创建 robot 并填充控件；原版仅 Finish/Cancel 主动 Close。AutoScaleMode.Font，未发现与本故障有关的资源/DPI 错误 |

最新 master 还包含 3DXML、关节 mimic、限位和 RPY 等上游修改。本任务没有再改动这些模块，也未改 URDF 数据结构、STL 导出、坐标变换或惯量算法。若只在 1.6.1 上最小回移，本问题核心修复仅为删除 Dispose 那一行。

## 4. 本地修改内容

| 文件 / 函数 | 修改前 → 修改后 | 原因 |
|---|---|---|
| `SW2URDF/SW/SwAddin.cs` / SetupPartExporter | 最新 master 已无 Dispose；增加非模态生命周期注释，以及 Show 返回的 Visible/IsDisposed 日志 | 防止回归，并明确显示状态 |
| `SW2URDF/UI/PartExportForm.cs` | 添加构造、Load begin/complete、Load 异常完整对象、Shown、FormClosing、FormClosed、Disposed 日志 | 不改变导出算法即可观察初始化与销毁；log4net 的异常对象包含类型、消息、栈及 inner exception |
| `SW2URDF/SW2URDF.csproj` | v4.5.2 → v4.8；StartProgram 使用 SolidWorksDir；关闭 RegisterForComInterop 和旧 post-build RegAsm | 匹配本机 VS2022 目标包，构建与管理员注册分离 |
| `TestRunner/TestRunner.csproj` | net452 → net48 | 与插件统一框架 |
| `scripts/UpdateVersionInfo.ps1` | 无 Git 目录时仍调用 git → 使用 source-archive；有 Git 时按仓库路径读取版本 | ZIP 源码可正常构建，避免错误宣称 Git 提交 |
| `scripts/Register-SW2025.ps1` | 新增显式 64 位注册入口 | 不在普通构建阶段修改注册表；提供旧 DLL 回切入口 |

保留少量生命周期日志，没有安装全局 Application.ThreadException 或 AppDomain 异常处理器，没有切换宿主的异常处理策略。ShowDialog 的临时对照试验未做：已安装二进制中明确的 Dispose 指令，加上移除后真实菜单验证，已足以确认原因。

## 5. SW2025 编译方法

安装 VS2022 的 .NET 桌面开发工作负载及 .NET Framework 4.8 Developer Pack。使用 **Developer PowerShell for VS2022**：

```powershell
git clone https://github.com/ros/solidworks_urdf_exporter.git
cd solidworks_urdf_exporter
git checkout 882169e28952f0d17c87d7eab98826454421aabf
# 将本次修订的文件应用到克隆目录；当前工作区已经包含修订。
$env:SolidWorksDir = 'D:\sw\sw2025\SOLIDWORKS'
msbuild SW2URDF.sln /t:Restore /p:RestorePackagesConfig=true /p:Configuration=Debug /p:Platform=x64
msbuild SW2URDF.sln /t:Build /p:Configuration=Debug /p:Platform=x64
```

在 VS 中构建：从设置了上述环境变量的 Developer PowerShell 启动 `devenv SW2URDF.sln`，选择 Debug / x64，将 SW2URDF 设为启动项目，Restore NuGet Packages 后 Build。若 VS 已经打开，需要重启 VS 以继承环境变量。其他机器修改 SolidWorksDir 为实际安装目录即可。

本次验证命令对 SW2URDF 项目显式传入 SolutionDir、SolidWorksDir；为保留清晰的诊断版本标签，禁用了 PreBuildEvent，生成的 AssemblyInformationalVersion 为 `882169e-sw2025-diagnostics`。正常 VS 构建会运行已修复的版本脚本，在 ZIP 目录显示 `source-archive`。

实测：SW2URDF Debug x64 编译成功，0 error；5 条既有分析相关 warning（CA1820、CA1824 的重复生成阶段诊断以及 FxCop 弃用提示）。TestRunner net48/x64 单独 restore/build 也成功。没有运行会操作其他用户文档的整个旧测试套件。

输出：`SW2URDF\bin\x64\Debug\SW2URDF.dll`，依赖 DLL 和 PDB 位于同目录。不要仅移动 SW2URDF.dll 而丢弃依赖。

本次实测 DLL：
- AssemblyVersion：`1.6.9767.21140`
- SHA256：`8CFDD275CF08E92EF7F281B3DD1CA4ADB0A41FAF91B5AA4C78A2E839824D1DD9`

## 6. 注册与加载

**当前机器已经完成新 DLL 注册**，CodeBase 指向当前工作区的 Debug x64 输出；旧安装目录未覆盖。请不要移动或删除这个输出目录，否则注册路径失效。

通常先保存文档并退出 SolidWorks，再在管理员 PowerShell 执行：

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" /codebase 'D:\solidworks_urdf_exporter-master\SW2URDF\bin\x64\Debug\SW2URDF.dll'
```

也可在当前工作区的管理员 PowerShell 运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Register-SW2025.ps1
```

必须使用 Framework64，不能用 Framework 下的 32 位 RegAsm。/codebase 使未安装进 GAC 的插件按完整路径加载。HKLM/COM 注册需要管理员权限；F5/普通编译不自动注册。若提权使用另一管理员账户，HKCU 启动项会属于该账户，应在实际用户的 SolidWorks 插件对话框勾选 SW2URDF。

启动 SW2025，在 工具 → 插件 中检查 SW2URDF 的活动和启动选项。注册表 CodeBase 的更新不能替换已经加载到现有进程的旧 .NET 程序集，旧进程必须重新启动后才采用新插件。

本次为保留用户原始未保存零件，原进程没有退出；在独立 SW2025 测试进程中显式 LoadAddIn 新路径（返回 0），随后通过真实菜单完成验证。当前原进程仍可能运行旧 DLL，请保存原始文档后重新启动日常使用的 SolidWorks。

回切旧版本（仅在需要时）：管理员 PowerShell 执行 `scripts/Register-SW2025.ps1 -Rollback`，然后重启 SolidWorks。旧 DLL 为 `C:\Program Files\SolidWorks Corp\SolidWorks\URDFExporter\SW2URDF.dll`。更精确的原始注册备份在 `diagnostics/registry-backup/`；原用户启动项在备份时不存在。

## 7. Visual Studio 调试

- Debug / x64，Debug 属性 Start external program 为 `$(SolidWorksDir)\SLDWORKS.exe`（本次已改 StartProgram）。
- 先完成显式注册，再 F5 启动。确认 SW2URDF 活动/启动勾选；否则从工具插件启用。
- 或 Debug → Attach to Process → 选择实际测试用的 SLDWORKS.exe PID，代码类型为 Managed (.NET Framework 4.x)。多个 SolidWorks 进程时不要附加错进程。
- 在 Modules 中确认 SW2URDF.dll 的完整路径、版本以及匹配 PDB。
- 断点：SetupPartExporter、PartExportForm 构造函数、OnLoad / PartExportFormLoad、OnShown、OnFormClosing、OnFormClosed；需要观察直接 Dispose 时对 Disposed 日志行下断点。
- Exception Settings 可勾选 CLR 异常的 Thrown，在真实抛出点中断；不要通过修改 SolidWorks.exe.config 改变整个宿主的行为。

通常日志在 `%HOMEDRIVE%%HOMEPATH%\sw2urdf_logs\sw2urdf.log`。旧 logger 使用固定文件名，多个 SW/探针进程同时写入会发生文件锁及轮转冲突。本次双进程验证未将混合/旧 export.log 当成新生命周期的完整证据；以实际窗口操作、安装 DLL IL 和新生成导出文件为准。单实例重启后再收集新日志更可靠。

## 8. 验证结果

| 测试 | 结果与证据 |
|---|---|
| 原版 Part 菜单 | 复现窗口消失；已安装 DLL IL 明确 Show 后 Dispose |
| SW2025 简单拉伸块 Part 打开 | 通过，使用用户当前 SW2025 新建拉伸块的测试副本，实际菜单打开并持续可见 |
| 关闭并重开 | 通过，Cancel 后不残留导出窗口；再开得到新窗口，无 ObjectDisposedException 弹窗 |
| Part URDF/STL 导出 | 通过，`diagnostics/exports/cube/`：URDF 和 cube.STL；原版 Part 流程输出 manifest.xml，不是 Assembly 的 package.xml，未改变格式 |
| 简单 Assembly | 通过，SW2025 API 新建两零件装配体 two_link.SLDASM，base_link → link1，固定关节；预存配置用于可重复验证 |
| Assembly 菜单 / PropertyManager | 通过，实际点击菜单，配置树显示两个 link |
| AssemblyExportForm | 通过，Preview and Export 打开，joint 与 link 参数正常填充，Next 可用 |
| Assembly URDF + meshes | 通过，实际点击 Export URDF and Meshes，保存到 diagnostics/exports/robot |
| 文件检查 | 通过，XML 可解析，2 link / 1 fixed joint；所有 package://robot/meshes 引用对应文件存在 |
| STL 检查 | 两个二进制 STL 各 684 字节 / 12 三角面，满足 84 + 50 × 面数，均非空 |
| package.xml | 可解析，包名 robot |

最终文件：

```text
diagnostics/exports/robot/
  package.xml
  CMakeLists.txt
  urdf/robot.urdf
  urdf/robot.csv
  meshes/base_link.STL
  meshes/link1.STL
  config/joint_names_robot.yaml
  launch/display.launch
  launch/gazebo.launch
```

机器可读检查摘要：`diagnostics/export-validation.txt`。测试模型位于 `diagnostics/test-models/`。本次验证范围是上述简单 Part 和两 link 固定关节 Assembly，不代表对所有关节类型、大型装配体或 ROS/Gazebo 运行时做了完整兼容性认证。
