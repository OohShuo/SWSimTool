# SW2MuJoCo 3.2.1 Tool Backend 契约

## 生产边界

C# 负责 CAD/URDF 导入、RobotModel、所有仿真语义、完整 MJCF 生成、资产规划和整包发布。Python 的三个接口只接受文件路径、网格偏好、执行上下文；不接受 RobotModel、SimulationProject 或 COM 对象。

| 接口 | 输入 | 输出 / 责任 |
|---|---|---|
| IMeshPreparationService | 原始 STL ID/路径/哈希、输出映射、暂存目录、减面偏好 | 校验结果映射、输出哈希、binary STL 长度和面数预算；返回不可变计数 |
| IMuJoCoValidationService | 已完整生成的 XML | 官方 MuJoCo compile 成功/失败，不修改 XML |
| IPreviewService | 已有 XML | 官方 viewer 的进程生命周期；不生成或修复模型 |

ToolContext 包含本地 Python、export ID、日志回调、CancellationToken 和 timeout。ToolResult 区分 EnvironmentMissing、DependencyMissing、InvalidInput、InputChanged、BudgetExceeded、ToolFailed、ValidationFailed，以及 Start、Cancelled、Timeout、Protocol，保留退出码与最多 64 KiB 日志尾部。状态失败不自动改用另一个后端或生成器。

Mesh 默认限时 10 分钟，compile 2 分钟；交互 viewer 不自动超时，仍接受取消。工具窗口关闭会请求取消并等待清理完成后关闭，不改窗口布局。底层 Windows Job Object 拥有 Python 及其子进程树；取消、超时、正常结束后均清理剩余自有子进程。建立所有权失败时拒绝继续。

协议文件只在自有 work 目录生成，finally 清理。输入 mesh/settings 哈希在工具前后核对；XML 和准备后的 mesh 在验证后再次核对。编译及核对成功、未取消时才整包原子发布。失败/取消保留此前有效包，最终目录仍仅有 XML 与 meshes/*.stl。

Python reference 留在仓库用于显式差分测试，安装包不含 convert.py。生产 UI 不调用 PythonBackend 的 reference 生成器；输出路径策略独立于该类。普通 URDF 导出及可选 sidecar 兼容入口保留。

## 可重复验证

先构建 tests/parity/CandidateRunner.csproj，再运行：

```powershell
build/native-parity/SW2MuJoCo.CandidateRunner.exe --tooltest D:/Softwaves/python/python.exe tests/parity/fake_tool.py
python tests/parity/run.py
python -m unittest discover -s mujoco_backend -p 'test_*.py'
powershell -File scripts/Test-ToolFormLifecycle.ps1 -Payload 3.2.1-final
```

Windows 宿主存在重复 Path/PATH 环境项时，测试入口通过 Python subprocess 显式传递 dict(os.environ)；测试使用独立暂存目录和偏好，不依赖用户已有文件。fake_tool.py 只用于构造协议/进程故障，预览测试不打开 viewer。

35 项工具契约测试包含 UTF-8/中文路径、stderr/exit、缺失 runtime/script、缺 MuJoCo/选定减面依赖、无法满足面数预算、损坏错误协议、预先及运行中取消、timeout、子进程清理、非法映射/哈希/计数/JSON/STL、输入及设置被改动、官方 valid/invalid compile、成功发布、失败和发布前取消回滚。两项未显示窗口的 Form 生命周期检查覆盖关闭请求取消和空闲关闭保存。

这不替代 UI 视觉与 viewer 体验验收。本轮不使用 computer-use，不打开用户现有 SolidWorks 工程。
