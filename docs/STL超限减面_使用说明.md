# MJCF 导出前的 STL 超限减面

入口：**MuJoCo 工具 → STL 减面设置…**。设置作用于“保存 MJCF”和“转换并预览”。SW 导出 URDF / STL 阶段不再减面，已导出的旧 URDF 包也可直接使用。

1. 选择原始 URDF 和配套 `.sim.json`。
2. 在减面设置中启用超限减面，填写每个 STL 的上限（4 到 200,000），选择 PyMeshLab、fast-simplification 或 Blender。
3. Blender 后端还需提供本地 blender.exe。所有后端使用 MuJoCo 工具页面选择的本地 Python；旧减面设置中的独立 Python 字段不再使用。
4. 点击“保存 MJCF”或“转换并预览”。无需重新导出 SW 工程。

默认输出位置是原 URDF 旁边的 `<机器人名>_mjcf/<机器人名>.xml`。手动选择的输出目录如果不以 `_mjcf` 结尾，会自动在该目录下创建 `<机器人名>_mjcf`。

```text
robot_mjcf/
├── robot.xml
├── robot.urdf
├── robot.sim.json
├── mesh-report.json
└── robot_meshes/
    ├── mesh_0000.stl
    └── mesh_0001.stl
```

## 处理顺序

1. 校验原 URDF 与 JSON 的文件名和 SHA256，并检查 joint/link 树及附加配置。
2. 解析所有网格引用，按实际文件去重；不同路径的同名文件生成不同编号。网格子目录名称使用 ASCII，便于 MuJoCo 在 Windows 下访问。
3. 未超限 STL 原样复制；超限 STL 在新包的临时目录中减面。检查实际面数，超限时重试；全部 STL 符合当前解码器 1 到 200,000 面要求后才继续。
4. 保存 URDF 副本，只改网格路径及 MuJoCo compiler 配置；副本使用包内相对网格路径。配套 JSON 同步更新副本 URDF 的文件名与 SHA256。
5. MuJoCo 加载这份 URDF 的内容与处理后的网格字节，加入 site、actuator、sensor、闭链等附加对象，生成并编译最终 MJCF。
6. 全部成功后发布整个包，保留中间 URDF 和 JSON。已有输出包通过临时目录构建和目录切换更新；预处理或编译失败时原包保留，原始输入始终不修改。旧包中无关文件在成功更新时也保留。

`mesh-report.json` 记录原网格引用、输出相对路径、减面前后面数和是否修改。运行日志显示在窗口，进程结束后写入 MJCF 同目录的 `.mujoco.log`。移动或分享时复制整个 `_mjcf` 文件夹。

## 环境与设置

减面设置保存在 `%LOCALAPPDATA%\SW2URDF\mesh-export.json`，延续旧设置以便升级。MuJoCo 工具的 Python 路径保存在独立用户级设置中。插件不安装依赖，也不分发减面软件。

- PyMeshLab：所选 Python 中安装 pymeshlab（依赖包含 NumPy）。
- fast-simplification：所选 Python 中安装 fast-simplification（依赖包含 NumPy）。
- Blender：所选 Python 中有 NumPy，并提供 Blender；算法在 Blender 内置 Python 中执行。

上限按每个 STL 而非机器人总面数计算。减面会改变几何细节；基本检查不保证孔洞、薄壁和所有细小组件保持原貌，也不等同于碰撞几何优化。CAD 工程、原始 STL、joint/site 配置和质量惯性参数不修改。

## 验证（2026-10-03）

23 项后端测试通过，覆盖三种减面后端、原文件字节不变、同名网格与重复引用、保留 URDF/JSON/hash、整包移动加载、失败保留旧输出和无关文件。524,288 面的新建 STL 先减到 50,000 面，再成功编译为 MuJoCo 模型。插件进程调用及用户设置恢复验证通过；未使用用户已有 SW 工程。
