# SW2MuJoCo

SolidWorks 2025 的机器人建模与 URDF / MuJoCo 导出插件，基于官方 SolidWorks URDF Exporter，保留原许可证和来源说明。

## 安装与升级

保存工作并关闭所有 SolidWorks 实例，运行 `SW2MuJoCo_SW2025_x64_Setup.exe`。安装器沿用旧版本 AppId、安装目录和 COM 标识，直接升级现有插件。内部程序集仍名为 `SW2URDF.dll`，这是升级兼容标识。

## 配置入口

`工具 → SW2MuJoCo` 下有 URDF 配置、碰撞配置、仿真配置、导出与预览。旧 `Tools → Export as URDF` 入口移除。

- URDF 配置：原 link 树编辑器；配置零件归属、link 与 joint、坐标系等。保留原来的 Preview and Export 向导，不再放置附着点或碰撞配置入口。
- 碰撞配置：左侧编辑简单几何体和允许碰撞的 link 对，保留拾取、直边驱动尺寸、条件字段和实时增量预览。
- 仿真配置：左侧按附着点、传感器、执行器、闭链约束分标签。切换所属 link 后显示对应对象。附着点选择 point 或 frame，再点击拾取并在模型中选参考。point 仅记录位置；frame 记录位置和方向。可选择参考点、顶点、草图点；方向必须使用参考坐标系。
- 传感器：选择 frame site，支持已实现的 IMU、TOF 和相机。IMU 输出加速度计与陀螺仪；TOF 沿 +Z 测距；相机朝 -Z，视场角为度。noise 当前必须为 0，噪声由采样层实现。扫描雷达未在本轮新增。
- 执行器：选择所属 link 的 joint，支持 motor、position、velocity。位置或速度增益按类型显示。转动 joint 用 rad，移动 joint 用 m，力矩 N·m、力 N；控制量与 gear 共同决定执行器映射。
- 闭链约束：connect 引用两个附着点，weld 引用两个 frame，须属于不同 link。列表显示全部约束，方便配置跨 link 连接。

附着点的实时预览用球和三轴线段显示，不进入 STL 或改变质量惯量。未完成的附着点可以保存为草稿，导出时检查参考；删除或重命名 site 后需修正引用它的传感器和约束。

## 保存与旧配置迁移

所有设置写入一个 `SW2MuJoCo Configuration` 节点，内部按 SolidWorks Configuration 分开保存。页面切换 Configuration 后停止编辑，应重新打开页面。不同页面保存各自内容，保留其他部分。

旧 URDF 和 MuJoCo 节点兼容读取；读取不修改装配。明确保存配置时，先合并旧数据、写入新节点并回读验证，再移除旧节点。旧 URDF 节点原来跨所有 Configuration 共用，迁移时复制到已有 Configuration；旧仿真节点原有的分配置内容保留。未来新建 Configuration 默认没有 URDF 树，需单独配置。

点击“保存配置到装配”后，还需保存 `.sldasm` 才能落盘。副本或另存装配可能改变 CAD 持久引用，应检查并重新拾取失效参考。

## 导出 URDF

保留原预览、关节和 link 属性向导、URDF Only / URDF and Meshes 导出方式，以及原 ROS 包、CSV 等产物。在最终页面勾选“导出附加配置 (.sim.json)”才会输出侧文件，默认关闭；URDF 格式和 STL 导出不加入仿真扩展，也不减面。关闭附加配置时，不要求仿真草稿完整。

附加文件是已解析到本次 URDF link 坐标的快照，包含 URDF 文件名与 SHA256。修改 URDF 后必须重新导出配套附加配置，否则转换会拒绝不匹配的数据。点的快照仅含 xyz，frame 包含 xyz/rpy；CAD 持久引用仅供装配端重新求值。

## 导出与预览

- 从当前工程导出 MJCF：打开已配置的装配，选择输出路径。插件自动在临时目录生成原 URDF 包，按实际导出的 link 坐标解析仿真配置，再转换；无需先手动导出 URDF。导出期间不要切换装配或修改 Configuration。
- 从本地 URDF 导出 MJCF：选择已有 URDF，可选配套 `.sim.json`，不需要打开 SW 工程。没有附加文件时，仅转换 URDF 模型，默认排除内部 link 碰撞，保留环境碰撞。
- 两者均支持“导出并预览 MJCF”，转换验证成功后使用本地 MuJoCo viewer。关闭 viewer 后可继续操作。
- 预览已有 MJCF：直接选择 XML，插件不写入或修改模型文件。

最终新建目录只有：

```text
模型_mjcf/
  模型.xml
  meshes/
    mesh_0000.stl
    ...
```

要求 URDF 网格为 STL；visual 与 collision 的不同源文件分别保留，相同源文件可以复用。复制或减面完成后才交给 MuJoCo。中间 URDF、JSON 放在临时目录，结束后清理；原输入不修改。

STL 减面设置支持 PyMeshLab、fast-simplification、Blender，沿用本地 Python 与 Blender 路径。小于设定阈值的 STL 字节不变，超过阈值才减面；MuJoCo STL 面数必须在 1–200000 以内。

导出前检查名称、CAD 参考、link/joint/site 对应关系、参数范围和碰撞对。错误会停止转换并显示具体原因。转换在临时目录验证完成后才发布，失败保留原结果；清理前一次 XML 引用的旧 STL，保留其他用户文件。遇到同名且内容不同的无归属 STL 时拒绝覆盖。旧版输出中的 URDF、JSON、日志不会被自动删除，建议为首次新版导出选新目录。

Python、MuJoCo 和减面依赖由本地环境提供，不在插件内硬编码或安装。日志显示在界面，使用“保存诊断信息”手动导出；不会自动在 MJCF 目录产生日志。
