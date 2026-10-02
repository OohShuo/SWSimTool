# MuJoCo 扩展第一版

本次新增的是初版源码与独立编译输出，未替换之前的稳定安装包。2026-10-02 已使用全新测试装配体验证完整 URDF/STL/JSON 导出、独立配置窗口保存与恢复、本地 MuJoCo 3.14.0 转换及 Viewer。PropertyManager 配置按钮显示问题已修复并编译，桌面锁屏导致最终入口复测待完成。详情见验证报告。

## 文件与配置

- 装配体项目：`模型.SLDASM.sw2urdf.json`，位于装配体旁。保存 CAD 特征持久引用、所属 link、执行器、传感器、闭链及 Python 路径。
- 导出结果：原有 URDF/网格，以及同目录的 `<机器人名称>.sim.json`。点仅含 `xyz`，坐标系含 `xyz` 与 `rpy`。单位是米、弧度。
- 配套 JSON 包含 URDF SHA256，后端检查是否匹配。修改 URDF 后须重新导出配套文件。
- 第一版一个项目文件对应一个 SolidWorks 配置，配置不匹配时拒绝复用。支持的 CAD 来源是参考几何体中的参考点、参考坐标系，以及已加载组件中的这些特征；尚未支持直接选择顶点或草图点。
- CAD 参考使用持久 ID，失效时要求重新绑定，不猜测同名特征。失效条目可删除并重新添加。修改 link 名称时配置页同步更新归属；导出预览页再次改名后需要检查引用。

## 插件操作

1. 保存装配体，打开原有 Export as URDF 配置页。
2. 在 link 树中选择目标 link，点击 **附着点 / MuJoCo 配置...**。
3. 在来源列表选择 SolidWorks 参考点或坐标系，填写唯一名称，添加到当前 link。
4. 在 Actuator 页添加配置，通过右侧属性表填写 joint、类型与范围。支持 `motor`、`position`、`velocity`，后两者的 gain 分别为 kp、kv；motor 使用 gear。
5. 在 Sensor / Camera 页添加配置。支持 `imu`（加速度计和陀螺仪）、`tof`（基础射线 rangefinder）、`camera`（垂直视场角）。这些条目均需引用 frame；ToF 沿 +Z，相机朝 -Z。
6. Equality 页填写 `connect` 或 `weld` 和两端附着名称。connect 可用点；weld 需要 frame。首次测试建议先只添加 site，再逐个添加约束。
7. 填写 Python 可执行文件完整路径，保存配置；随后按原有流程导出 URDF 和网格。
8. 在装配体导出预览窗口的底部使用 **导出 MJCF...** 或 **MuJoCo 预览...**，选择已经生成的 URDF；同目录须有配套 `.sim.json`。

导出后原有窗口会关闭，若需要使用底部转换按钮，可重新打开导出流程。后端通过独立进程运行，结束后显示结果，并在 URDF 同目录写入 `.mujoco.log`。预览进程在 viewer 关闭后结束。

## Python 环境与命令行

MuJoCo 及其依赖由用户配置的本地 Python 运行环境提供。插件不创建环境、不安装包、不固定 MuJoCo 版本。选择 Python 3.10+ 环境中的 python.exe；后端检查依赖和所需接口，并记录实际 Python 路径及 MuJoCo 版本。requirements.txt 仅供依赖参考。build/mujoco-venv 是开发测试环境，不是插件运行环境约束。

```powershell
# 使用已配置好的本地 Python 环境
.venv\Scripts\python.exe mujoco_backend\convert.py --check-environment
.venv\Scripts\python.exe mujoco_backend\convert.py --urdf D:\输出\robot\urdf\robot.urdf --config D:\输出\robot\urdf\robot.sim.json --output D:\输出\robot\urdf\robot.mjcf.xml
# 同一命令末尾加 --preview，可打开 MuJoCo viewer。
```

转换器只修改内存中的 URDF 副本，解析 package:// 网格路径，关闭固定刚体合并，再使用 MjSpec 导出 MJCF 并添加附加对象。最终 MJCF 编译通过后才替换输出文件，输入 URDF、JSON 和网格保持不变。输出引用相对路径的配套 sw2urdf_assets_* 网格副本，移动到其他目录或机器时与 MJCF 一起复制。

## 当前范围与验证

- 插件 .NET Framework 4.8 / x64 编译通过。
- 6 个 Python 测试通过：MJCF 编译和 100 步仿真、固定子 link 上的 site 世界位置与旋转、输入 URDF 字节保持一致、输入与配套文件哈希不匹配时拒绝、错误范围时保留旧输出、无方向点不可用于需要方向的传感器、中文网格路径与移动后加载。
- 独立 SolidWorks API 测试已验证真实参考点与坐标系的持久引用解析、局部位置、点不含方向、项目 JSON 保存，以及附加数据导出前后 URDF 字节一致。
- 尚未完成通过实际 SolidWorks 菜单点击配置、导出配套 JSON、启动 viewer 的完整流程验收。CAD 测试脚本已改为显式接收本次从空白模板新建的测试目录，不复用已有工程；模板和 SolidWorks API 路径仍需按机器配置。
- 第一版 noise 必须为 0。本后端没有实现采样噪声注入；cutoff 是输出裁剪值，不是 ToF 量程。真实 ToF、扫描雷达、雷达回波和可配置采样噪声需进一步增加采样后端。
- 相机当前配置 fovy，尚未接入图像输出、分辨率设置和记录功能。闭链当前使用基础 connect/weld，没有提供求解参数 UI。

```powershell
python -m unittest discover -s mujoco_backend -p test_convert.py -v
```

原有 URDF 写入函数和机器人 link/joint 数据模型未加入任何附加元素。新增内容集中在 `SW2URDF/Simulation`、配置窗体及 `mujoco_backend`。


## 中文路径与 MJCF 网格

后端通过 Python 读取 STL 字节并使用 MuJoCo 内存资源，避免 Windows 原生文件接口的中文路径问题。MJCF 使用相对路径引用旁边的 sw2urdf_assets_* 目录，移动或分享时须一起复制。原始 URDF 和网格不改写。Windows 下手动加载含中文路径的 MJCF，可使用后端的 load_mjcf(path) 函数；它通过 Python 读取 XML 和配套网格再编译。
