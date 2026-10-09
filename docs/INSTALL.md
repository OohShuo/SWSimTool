# 安装与环境准备

## 运行环境

SWSimTool 的产品宿主为 Windows 上的 SolidWorks 2025 x64，需要 .NET Framework 4.8。Ubuntu/Linux 仅用于可复用层的开发验证，不能运行 SolidWorks 插件。

普通 URDF 导出不需要 Python。MJCF 导出、编译验证与预览需要本地 Python 和 MuJoCo。插件不附带 Python，不自动安装依赖；在工具窗口填写已准备好的 Python 命令或可执行文件路径。

```powershell
& 'C:\Python\python.exe' -m pip install -r runtime/python/requirements.txt
```

上述路径用于源码工作区；安装后依赖清单位于插件目录的 `mujoco_backend/requirements.txt`。减面按所选工具额外准备 `fast-simplification`、`pymeshlab` 或 Blender 可执行文件。选定工具缺失时会报错，不自动切换。MuJoCo viewer 还需要可用的图形环境。

## 安装与升级

1. 保存工作并关闭全部 SolidWorks 会话。
2. 运行 `SWSimTool_3.2.1_SW2025_x64_Setup.exe`，按安装器提示允许管理员权限。
3. 重新打开 SolidWorks，在加载项中启用 SWSimTool；入口在 **工具 → SWSimTool**。

默认安装目录为 `C:\Program Files\SWSimTool`。同一产品升级可直接安装。安装包不随 Git 提供，源码构建方法见根 README。源码版本、安装包版本与实际加载的 DLL 可能不同，排查时应核对路径及文件哈希。

## 从 SW2MuJoCo 切换

SWSimTool 使用独立的 COM 身份、安装器 AppId 和安装目录。安装器不会升级或卸载旧产品；允许并存安装，编辑同一工程时只启用一个插件。

工程内的 `SW2MuJoCo Configuration` v2 节点可以读取。显式保存或重建配置时，经备份和写入校验迁移为 `SWSimTool Configuration`；迁移前先保存装配文件。其他 SolidWorks Configuration 条目保留。旧偏好、缓存和外部附加配置不会导入，需重新指定 Python、Blender 等路径。

配置确认写入装配内存后，还需保存 `.sldasm`。具体编辑、取消、重建和导出操作见当前使用指南。

## 卸载与手工注册

关闭全部 SolidWorks 会话，再使用 Windows 已安装应用中的 SWSimTool 卸载入口。卸载 SWSimTool 不等于卸载旧产品。

开发调试可在管理员 64 位 PowerShell 中运行 `tools/install/Register-SW2025.ps1`，默认注册工作区 `build/runtime-release/SWSimTool.dll`。`-Rollback` 将注册切回已安装的 `C:\Program Files\SWSimTool\SWSimTool.dll`，不是卸载命令。仅在关闭 SolidWorks 且目标 DLL 已准备好时使用。

安装目录的 `docs/` 提供 Markdown 和离线 HTML 指南。工具出错时先检查本地 Python 路径、依赖和窗口日志，可用“保存诊断信息…”导出日志。
