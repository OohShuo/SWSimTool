# 运行时后端

`python/` 只包含生产工具和依赖声明，安装时部署为插件旁的 `mujoco_backend/`。Python 负责网格准备、官方 MuJoCo 编译验证和 viewer，完整 MJCF 由 C# 生成。

用户提供本地 Python、MuJoCo 和所选减面工具；插件不附带或自动安装环境。测试、参考生成器和构建脚本不进入运行时后端。

职责和工具协议见 [后端契约](../docs/SWSimTool_ToolBackend.md)，环境准备见 [安装说明](../docs/INSTALL.md)。
