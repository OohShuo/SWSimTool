# MuJoCo 扩展第一版

本次新增的是初版源码与独立编译输出，未替换之前的稳定安装包。2026-10-02 已使用全新测试装配体验证完整 URDF/STL/JSON 导出、独立配置窗口保存与恢复、本地 MuJoCo 3.14.0 转换及 Viewer。PropertyManager 配置按钮显示问题已修复并编译，桌面锁屏导致最终入口复测待完成。详情见验证报告。

## 文件与配置

- 装配体项目：保存在 .sldasm 内的 MuJoCo Simulation Configuration (v1) Attribute 节点，按 Configuration 区分，包含附着点、执行器、传感器、闭链和碰撞配置。旧模型.SLDASM.sw2urdf.json作为迁移来源保留，当前配置的装配内记录优先。修改后需保存装配文件。
- 导出结果：原有 URDF/网格，以及同目录的 `<机器人名称>.sim.json`。点仅含 `xyz`，坐标系含 `xyz` 与 `rpy`。单位是米、弧度。
- 配套 JSON 包含 URDF SHA256，后端检查是否匹配。修改 URDF 后须重新导出配套文件。
- 装配内节点支持多个 SolidWorks Configuration。附着 site 来源仍为参考点、参考坐标系；碰撞几何体编辑另支持顶点、草图点和几何面，具体定义方式见 MuJoCo碰撞配置_使用说明.md。
- CAD 参考使用持久 ID，失效时要求重新绑定，不猜测同名特征。失效条目可删除并重新添加。修改 link 名称时配置页同步更新归属；导出预览页再次改名后需要检查引用。

## 插件操作

1. 保存装配体，打开原有 Export as URDF 配置页。
2. 在 link 树中选择目标 link，点击 **附着点 / MuJoCo 配置...**。
3. 在来源列表选择 SolidWorks 参考点或坐标系，填写唯一名称，添加到当前 link。
4. 在 Actuator 页添加配置，通过右侧属性表填写 joint、类型与范围。支持 `motor`、`position`、`velocity`，后两者的 gain 分别为 kp、kv；motor 使用 gear。
5. 在 Sensor / Camera 页添加配置。支持 `imu`（加速度计和陀螺仪）、`tof`（基础射线 rangefinder）、`camera`（垂直视场角）。这些条目均需引用 frame；ToF 沿 +Z，相机朝 -Z。
6. Equality 页填写 `connect` 或 `weld` 和两端附着名称。connect 可用点；weld 需要 frame。首次测试建议先只添加 site，再逐个添加约束。
7. 保存附加配置，随后按原有流程导出 URDF 和网格。Preview and Export 窗口恢复原有布局，不包含 MuJoCo 按钮。
8. 点击 SolidWorks 工具栏的 **MuJoCo 工具**，或 Tools → MuJoCo 工具 → MuJoCo 工具。无需打开零件或装配体，也无需再次进入导出流程。
9. 在独立窗口填写本地 Python 命令或 python.exe 路径。选择 URDF 后自动填写同名 `.sim.json` 和 `<机器人名>_mjcf/<机器人名>.xml`；附加配置及输出位置可以手动调整。
10. 点击 **保存 MJCF** 生成文件，或 **转换并预览** 生成后启动 viewer。若已有 MJCF，选择“已有 MJCF”并点击 **预览已有 MJCF**，此时不需要 URDF 或 JSON。

工具窗口以非模态方式打开，可以重复使用。运行状态与 Python 日志直接显示在窗口中，viewer 关闭后恢复操作。运行期间禁止重复启动；关闭工具窗口不会终止已经打开的 viewer。日志写入 MJCF 同目录的 `.mujoco.log`。配置自动保存到 `%LOCALAPPDATA%\SW2URDF\mujoco-tools.json`，包含 Python 路径与最近输入、输出路径，适用于所有工程。

工具栏采用 SolidWorks 原生 CommandGroup，支持拖动与自定义。若工具栏曾被隐藏，可在工具栏右键菜单或 Tools → Customize 中启用“MuJoCo 工具”；Tools 菜单仍提供入口。

## Python 环境与命令行

MuJoCo 及其依赖由用户配置的本地 Python 运行环境提供。插件不创建环境、不安装包、不固定 MuJoCo 版本。选择 Python 3.10+ 环境中的 python.exe；后端检查依赖和所需接口，并记录实际 Python 路径及 MuJoCo 版本。requirements.txt 仅供依赖参考。build/mujoco-venv 是开发测试环境，不是插件运行环境约束。

```powershell
# 使用已配置好的本地 Python 环境
.venv\Scripts\python.exe mujoco_backend\convert.py --check-environment
.venv\Scripts\python.exe mujoco_backend\convert.py --urdf D:\输出\robot\urdf\robot.urdf --config D:\输出\robot\urdf\robot.sim.json --output D:\输出\robot\urdf\robot.mjcf.xml
# 同一命令末尾加 --preview，可打开 MuJoCo viewer。
.venv\Scripts\python.exe mujoco_backend\convert.py --mjcf D:\输出\robot\urdf\robot.mjcf.xml --preview
```

转换前先复制网格到独立的 *_mjcf/*_meshes 包，超限 STL 在副本上减面。全部网格处理完成后，保留改写相对网格路径的 URDF 和更新哈希的 JSON，再使用 MjSpec 编译并添加附加对象。成功后发布整个包；原 URDF、JSON 和网格保持不变。详见 STL超限减面_使用说明.md。

## 当前范围与验证

- 插件 .NET Framework 4.8 / x64 编译通过。
- 10 个 Python 测试通过（2026-10-03），包含已有 MJCF 独立预览、非法参数组合、URDF 树错误，以及：MJCF 编译和 100 步仿真、固定子 link 上的 site 世界位置与旋转、输入 URDF 字节保持一致、输入与配套文件哈希不匹配时拒绝、错误范围时保留旧输出、无方向点不可用于需要方向的传感器、中文网格路径与移动后加载。
- 独立 SolidWorks API 测试已验证真实参考点与坐标系的持久引用解析、局部位置、点不含方向、项目 JSON 保存，以及附加数据导出前后 URDF 字节一致。
- 尚未完成通过实际 SolidWorks 菜单点击配置、导出配套 JSON、启动 viewer 的完整流程验收。CAD 测试脚本已改为显式接收本次从空白模板新建的测试目录，不复用已有工程；模板和 SolidWorks API 路径仍需按机器配置。
- 第一版 noise 必须为 0。本后端没有实现采样噪声注入；cutoff 是输出裁剪值，不是 ToF 量程。真实 ToF、扫描雷达、雷达回波和可配置采样噪声需进一步增加采样后端。
- 相机当前配置 fovy，尚未接入图像输出、分辨率设置和记录功能。闭链当前使用基础 connect/weld，没有提供求解参数 UI。

```powershell
python -m unittest discover -s mujoco_backend -p test_convert.py -v
```

原有 URDF 写入函数和机器人 link/joint 数据模型未加入任何附加元素。新增内容集中在 `SW2URDF/Simulation`、配置窗体及 `mujoco_backend`。


## 中文路径与 MJCF 网格

后端通过 Python 读取 STL 字节并使用 MuJoCo 内存资源，避免 Windows 原生文件接口的中文路径问题。新版 MJCF 使用相对路径引用包内 *_meshes 目录，移动或分享时须一起复制。原始 URDF 和网格不改写。Windows 下手动加载含中文路径的 MJCF，可使用后端的 load_mjcf(path) 函数；它通过 Python 读取 XML 和配套网格再编译。
