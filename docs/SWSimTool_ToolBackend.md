# C# 与 Python 工具契约

C# 负责 CAD/URDF 导入、RobotModel、仿真语义、完整 MJCF 生成、资产规划和整包发布。Python 是一次性工具后端，只接收路径、网格偏好和执行上下文，不接收 RobotModel、SimulationProject 或 COM 对象。

| Application 接口 | 输入 | 责任 |
|---|---|---|
| `IMeshPreparationService` | STL ID/路径/哈希、输出映射、work/staging、减面偏好 | 准备网格，校验映射、哈希、binary STL 长度与预算，返回不可变计数 |
| `IMuJoCoValidationService` | 完整 XML 路径 | 官方 MuJoCo 编译，不修改 XML |
| `IPreviewService` | 已有 XML 路径 | viewer 生命周期，不生成或修复模型 |

`ToolContext` 包含 Python、export ID、日志回调、取消令牌和超时。`ToolResult` 保留退出码与最多 64 KiB 日志尾部，区分 EnvironmentMissing、DependencyMissing、InvalidInput、InputChanged、BudgetExceeded、ToolFailed、ValidationFailed、Start、Cancelled、Timeout 和 Protocol。失败不自动换后端。

网格工具限时 10 分钟，编译 2 分钟；交互 viewer 无自动超时但可取消。Windows 用 Job Object、Linux 用独立会话/进程组拥有工具进程树；启动所有权失败即报错。正常根进程退出后也清理剩余自有后代，避免重定向管道一直占用。

协议文件只存在于自有工作目录。工具前后核对输入/设置哈希，验证后核对准备的网格，发布前再次检查源数据和取消。关闭工具窗口请求取消并等待清理。发布前失败/取消保留旧包；发布后预览失败保留新包。输出包仅包含 XML 和 `meshes/*.stl`，不交付协议、缓存或参考产物。

生产源码位于 `runtime/python/`，安装到 `mujoco_backend/`。`native_support.py` 调用网格准备、编译和 viewer；`simplify_stl.py`、`mesh_cache.py`、`incremental.py`、`solver.py` 提供配套功能。本地环境提供 Python/MuJoCo，运行时 requirements 不固定 MuJoCo 版本；选定减面工具需另行准备。

测试参考生成器在 `tests/reference/`，只用于显式差分，不进入安装包，生产不能回退到它。普通 URDF 和可选 sidecar、本地 URDF 导入为独立支持路径。协议故障、进程清理和物理差分的运行方法统一见 [tests/README](../tests/README.md)。
