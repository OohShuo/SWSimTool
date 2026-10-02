# SW2URDF 中文使用指南（SolidWorks 2025 修复版）

适用安装包：`SW2URDF_SW2025_x64_Setup.exe`。本机验证环境：SolidWorks Premium 2025 SP1.2，64 位，.NET Framework 4.8。

本插件将 SolidWorks 模型导出为 URDF 机器人描述和网格文件。单个零件可导出为一个刚体；装配体可以配置为多个 link（连杆/刚体）及连接它们的 joint（关节）。导出完成并不等于已经配置好控制器或仿真环境。

## 1. 安装和启用

1. 保存正在编辑的模型，关闭**所有** SolidWorks 窗口。
2. 运行 `dist\SW2URDF_SW2025_x64_Setup.exe`，允许管理员权限，按提示安装。已有旧版本可直接覆盖升级，不必先卸载。
3. 启动 SolidWorks，打开 **工具 → 插件（Add-Ins）**，找到 SW2URDF，勾选当前启用；希望每次自动加载时，也勾选启动选项。
4. 打开已保存的零件或装配体。在本机中文界面中，导出入口是 **工具 → Tools → Export as URDF**；其他语言界面可寻找 `Export as URDF` 命令。

安装器会检查 .NET Framework 4.8 并完成 64 位 COM 注册。安装普通使用版不需要 Visual Studio，也不需要手动执行注册脚本。若安装时使用了另一管理员账户，应在日常账户的 SolidWorks 插件列表中手动启用。

## 2. 导出前准备模型

先保存模型副本，在副本上做导出配置。插件可能创建参考几何体、保存配置并使模型变为已修改状态。

- 为实体赋予合理材质/密度，并用质量属性检查质量和重心。缺失或错误的密度会影响导出惯量。
- 装配体中的相关零件应完整加载；发现遗漏时，检查抑制、轻化和缺失引用。
- 使用简洁名称，例如 `my_robot`、`base_link`、`arm_link`、`joint1`。包名优先使用小写字母、数字和下划线，名称保持唯一。
- 第一次导出建议使用新的空目录，例如 `D:\URDF_Output`，方便识别结果并避免覆盖上次文件。

**一个 link 表示一个刚体，不一定对应一个 SolidWorks 零件。** 螺栓、壳体、电机外壳若始终一起运动，可分配到同一个 link；会发生相对运动的部分通常分配到不同 link。不要把同一个零件重复分配给多个 link。

## 3. 快速上手：导出一个零件

适合先验证安装是否成功，或只需要一个物体模型的情况。

1. 打开并保存 `.SLDPRT` 零件。
2. 点击 **Export as URDF**，打开 `SolidWorks Part to URDF Link Exporter` 导出窗口。
3. 在 **Save Directory** 中指定输出路径，例如 `D:\URDF_Output\cube`。虽然字段名是目录，代码会将最后一段 `cube` 作为包名；**不要在末尾添加 `.urdf`**。
4. 检查质量、惯量、Visual（显示几何）和 Collision（碰撞几何）参数。首次试用可保留自动计算值。
5. 选择网格精度；`Fine` 会增加细节和文件大小。曲面复杂时应在外观与体积之间取舍。
6. `Rotate global origin to make Z-axis vertical` 用于调整全局方向；根据模型实际朝向选择，导出后再检查，避免重复旋转。
7. 点击 **Finish** 完成；**Cancel** 取消。

结果通常包含：

```text
cube/
  urdf/<模型名称>.urdf
  meshes/cube.STL
  manifest.xml
```

URDF 文件名可能保留 SolidWorks 文档名称，不一定是 `cube.urdf`。本版零件流程沿用上游实现，生成 `manifest.xml`；装配体流程则生成 `package.xml`。不要仅凭零件导出的目录结构，就将它当作完整可构建的 ROS 包。

## 4. 装配体导出：建立机器人结构

### 4.1 先确定 link 树

以底座和一个转动机械臂为例：

```text
base_link（底座、固定在底座上的零件）
└─ joint1（关节）
   └─ arm_link（机械臂及固定在机械臂上的零件）
```

根 link 没有父关节；每个子 link 通过一个关节连接到父 link。普通 URDF 采用树结构，不能直接保留闭环机构的全部约束。SolidWorks 配合关系也不能替代逐项检查 URDF 的关节类型和运动轴。

### 4.2 在配置页面分配零件

1. 激活已保存的 `.SLDASM` 装配体，点击 **Export as URDF**。若出现保存提示，先保存。
2. 在左侧 **Configure and Organize Links** 配置页面中选择根节点，将 **Link Name** 设为 `base_link`。
3. 点击 **Link Components** 选择框，在图形区域或模型树中选取属于底座的零件。
4. 在 **Link Tree** 中右击父节点，选择 **Add Child Link** 添加子 link；也可以用 **Number of child links** 设置数量。
5. 选中新节点，将名称设为 `arm_link`，在 **Link Components** 中选择机械臂所属零件。
6. 对每个子 link 检查 **Parent Link**、**Joint Name**、**Joint Type**、**Reference Coordinate System** 和 **Reference Axis**。逐个选择树节点并核对归属，避免零件遗漏。

删除错误节点可使用右键 **Remove**。更改层级后要重新检查父子关系和坐标。

### 4.3 设置参考坐标系和轴

全局坐标系用于确定机器人的整体参考方向；关节参考坐标系用于定义关节原点与朝向；参考轴用于指定关节运动方向。

在 SolidWorks 中可通过参考几何体功能建立坐标系和轴，再在插件相应下拉框中选择。建议给特征起易识别的名称，例如 `joint1_origin`、`joint1_axis`。若新建特征未出现在列表中，保存模型并重新进入导出流程。

对转动关节，将原点放在预期转轴位置，参考轴沿真实转轴。若后续手动填写 `Axis x/y/z`，数值表达的是**关节坐标系内**的方向；例如 `0 0 1` 表示沿该坐标系的 Z 轴，不必然等于装配体全局 Z 轴。

### 4.4 预览并检查参数

点击 **Preview and Export...** 进入装配体导出窗口，逐项检查关节参数，再用 **Next** 进入 link 参数页面。此处主要是参数预览，不应替代导出后的三维运动检查。

| Joint Type | 含义 | 常见用途 |
|---|---|---|
| `fixed` | 无相对运动 | 固定支架、相机安装座 |
| `revolute` | 有角度限位的转动 | 机械臂关节 |
| `continuous` | 不设位置上下限的连续转动 | 车轮 |
| `prismatic` | 沿轴直线移动 | 滑台、伸缩机构 |
| `planar` / `floating` | 平面/空间自由运动 | 需要下游工具支持的特殊模型 |

本修复版实际验证了简单零件和两个 link 的 `fixed` 装配体导出；其他类型在界面中可选，但本次未逐一完成运动仿真测试。

常见数值单位如下，不能直接把 SolidWorks 显示的毫米或角度数字原样填入：

| 参数 | 单位或意义 |
|---|---|
| Origin x/y/z、移动关节限位 | 米（m） |
| Roll/Pitch/Yaw、转动关节限位 | 弧度（rad） |
| Mass | 千克（kg） |
| Inertia | kg·m² |
| Velocity | 转动为 rad/s；移动为 m/s |
| Effort | 转动为 N·m；移动为 N |

例如 100 mm = 0.1 m，90° ≈ 1.5708 rad。`revolute` 的 Lower/Upper 分别为角度下限/上限；`prismatic` 为位移下限/上限。速度和力/力矩上限应填写设备实际允许值，不要把零值当成“无限制”。关节单位与限位可参照 [ROS 关节状态说明](https://docs.ros.org/en/melodic/api/pr2_mechanism_model/html/classpr2__mechanism__model_1_1JointState.html) 和 [URDF 关节限位读取实现](https://docs.ros.org/en/ros2_packages/jazzy/api/joint_limits/generated/program_listing_file_include_joint_limits_joint_limits_urdf.hpp.html)。

在 link 页面检查质量、惯量、显示与碰撞几何。不要为了通过提示而把惯量随意清零。首次建议选择 **STL (grayscale)**；STL 网格本身不保存彩色纹理。本版也提供 3DXML，但使用它之前需确认后续工具支持。

### 4.5 导出文件

1. 首次导出点击 **Export URDF and Meshes...**。
2. 若出现错误，按提示修正对应 link/joint；警告也应确认原因后再继续。
3. 在保存对话框中选择父目录 `D:\URDF_Output`，名称输入 `my_robot`，不要添加 `.urdf`。
4. 插件将生成 `D:\URDF_Output\my_robot` 包目录。

**Export URDF Only...** 只适合网格已经存在且模型几何未变化的情况。首次使用或修改零件形状、位置后，应重新导出网格。

## 5. 输出目录怎么看

装配体导出通常包括以下文件，名称以实际输出为准：

```text
my_robot/
  package.xml
  CMakeLists.txt
  urdf/
    <机器人名称>.urdf
    <机器人名称>.csv
  meshes/
    base_link.STL
    arm_link.STL
  config/
    joint_names_my_robot.yaml
  launch/
    display.launch
    gazebo.launch
```

URDF 描述 link、joint、质量惯量及网格引用；STL 保存几何形状；CSV 可用于后续 **Load Configuration...** 读取配置。移动结果时应保留整个包目录，不要只复制 `.urdf` 文件。

读取 CSV 后，页面可能显示 **Compute Mass and Inertia**、**Compute Visual and Collision**、**Compute Joint Kinematics**、**Compute Joint Limits**。这些选项控制是否重新计算对应数据；模型改变后应按需要重新计算，但也要避免覆盖刻意手工设定的参数。

生成的 `.launch` 文件沿用 ROS 1 风格。**它们不能直接作为 ROS 2 启动文件运行。** 使用 ROS 2 时，需要在目标工作区配置包、资源安装和启动文件。插件也不会自动完成控制器、驱动器或现代 Gazebo 的全部仿真配置。

## 6. 导出完成后的检查

先核对文件，再在目标 ROS/RViz 或仿真环境中检查外观和运动：

1. `urdf` 目录有非空 URDF，`meshes` 有对应网格文件。
2. URDF 中每条 `package://包名/meshes/文件名.STL` 都能对应到实际文件；转移到 Linux 时注意大小写。
3. link 数量、父子关系和 joint 类型符合设计，没有重复名称。
4. 模型尺寸、朝向和重心合理；模型放大或缩小约 1000 倍时优先检查单位。
5. 转动关节围绕正确位置运动，正方向和限位正确；若方向反了，检查参考轴和关节坐标系。

完整仿真还需检查碰撞模型、惯量和控制参数。已经导出文件，只能证明导出步骤完成。

## 7. 常见问题

| 现象 | 处理方法 |
|---|---|
| 插件列表中找不到 SW2URDF | 保存并关闭全部 SolidWorks，重新运行安装包；检查安装中的注册错误提示 |
| 插件存在但没有导出入口 | 勾选当前启用，打开零件或装配体，再查看工具菜单 |
| 导出窗口仍然一闪而过 | 确认所有旧 SolidWorks 进程已退出后重启；运行中的进程不会因覆盖安装而更换已加载 DLL |
| 安装器提示 SolidWorks 正在运行 | 保存并退出所有实例；仍有后台进程时确认文档已保存再处理，不要直接结束未保存模型的进程 |
| 装配体导出缺少零件 | 检查每个 link 的 Link Components、零件抑制/加载状态和父子结构 |
| 质量或惯量异常 | 检查实体、材质密度、质量属性和 link 所包含零件 |
| 导出了 URDF，但看不到网格 | 确认使用了 Export URDF and Meshes，复制了完整目录，并让目标环境能找到 package:// 对应包 |
| 转轴或位置不正确 | 检查关节参考坐标系、参考轴、Origin 和 Axis；不要只改显示几何偏移来掩盖关节错误 |
| 修改模型后导出没有反映变化 | 重新核对配置，按需启用计算选项，并重新导出 URDF 和网格 |

排错日志通常在 `%HOMEDRIVE%%HOMEPATH%\sw2urdf_logs\sw2urdf.log`。收集日志时尽量仅运行一个 SolidWorks 实例，避免多实例争用日志文件。记录触发步骤、模型类型和报错内容，比只提供“导出失败”更有助于定位。

## 8. 本项目中的参考文件

- 安装程序：`dist\SW2URDF_SW2025_x64_Setup.exe`
- 实测零件输出：`diagnostics\exports\cube`
- 实测两 link 装配体输出：`diagnostics\exports\robot`
- 测试装配体：`diagnostics\test-models\two_link.SLDASM`
- 修复原理、编译与诊断记录：`docs\SW2025_SW2URDF_FIX.md`

本指南按本地修复版的界面和代码整理。旧修复记录中的工作区注册路径是开发测试时的状态；普通安装后的加载位置以安装器注册的目录为准。
