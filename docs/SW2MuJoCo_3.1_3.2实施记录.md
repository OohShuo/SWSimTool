# SW2MuJoCo 3.1 / 3.2 实施记录

## 授权与范围

3.1 收口模型依赖，3.2 收口 Python Tool Backend。两轮均不用 computer-use，使用纯数据测试、后端测试和 SolidWorks COM 新建装配集成验证。不得打开或修改用户现有工程；不改 UI 布局、存储格式或插件名称。

## 分阶段提交

1. 固定 3.0 回归基线及工具行为。
2. SimulationProject 直接构建不可变 SimulationConfigSnapshot，去掉工程路径侧文件桥；旧格式兼容留在 Legacy importer。
3. 配置树和惰性 CAD 快照直接构建 RobotCoreSnapshot，去掉工程路径 URDF.Robot 桥。
4. 拆分源构建、规划、资产准备、验证、发布，协调器只编排。
5. 3.1 完整验收及安装包。
6. 3.2 抽取 mesh preparation / MuJoCo validation / preview 三个工具接口，统一请求、结果、错误、取消、超时及清理。
7. 3.2 工具契约、集成及安装验证，生成最终交付。

每阶段验证通过后单独提交。稳定 ID 审计贯穿模型迁移：已有 ID 失效不按名字重新绑定，旧格式名称解析只在显式兼容边界进行。

## 硬验收

- timestep-only 为 geometry/STL/simplification/generation/validation = 0/0/0/1/1。
- 清缓存全量与增量语义及动力学一致，保留理论/解析、reference 差分、参数破坏反例。
- 现有全部仿真功能保留；普通 URDF 输出不变；MJCF 只有 XML 和 meshes/*.stl。
- 官方 MuJoCo 验证成功后整包原子发布，失败不覆盖上一有效产物。
- C# 是唯一正式 MJCF 语义来源；Python 只承担 mesh、官方验证、preview，禁止静默修复或 generator fallback。
- 参考 XML 序列化容差与独立正确性容差分开；真实闭链差异不能以放宽容差掩盖。
- 无界面人工验收：界面显示、滚动、键鼠操作和 viewer 视觉体验不声明通过。

## 阶段 1 基线

使用 3.0 的候选测试 14 项和 Python 后端 96 项建立基线，日志保存在 build/3.1-baseline-*.log。本阶段不启动 SolidWorks，不调用桌面自动化。

## 阶段 2：直接仿真快照

领域快照仅接受类型化参数并复制数组/集合；JSON/字典解析全部移入兼容导入器。新增 SimulationConfigBuilder 和纯数据 ResolvedSimulationGeometry，工程 native 路径不生成或重读 robot.sim.json。缓存分别保存已解析的附着点/碰撞几何与核心快照，timestep 变化只重建配置快照。

验证：14 项候选回归通过；6 项直接配置测试覆盖所有扩展族新旧路径字节一致、快照不可变和失效 joint/site/link ID 拒绝。COM 新建四 link 装配通过 initial / timestep-only / 清缓存 full；增量保持 0/0/0/1/1，全量与增量严格语义和 1/10/100 步动力学一致，测试目录无中间 URDF 或 .sim.json。测试工程位于 build/native-incremental-7dddce42295447dc8b8ffb8d57414896，未打开用户工程。

## 阶段 3：直接 CAD 核心及身份审计

新增 CadRobotCoreBuilder，直接从配置树、共享惰性 CAD 快照和短生命周期质量属性测量生成 RobotCoreSnapshot。工程 native 路径不创建 URDF.Robot；配置树仅作为持久用户输入。原 URDF 构建和导出不变，旧核心适配器只用于明确的参考验证。

惯量产品符号转换在 CAD 边界完成；质量、COM、惯量分别使用短生命周期测量，避免 SW getter 重置坐标状态。解析物理属性必须在 STL 隐藏组件前完成。严格比较捕捉并修复了全零惯量与质心偏移，未通过放宽容差绕过。

同一新建四 link 装配的直接 CAD 与旧适配器核心通过严格 L2 和 1/10/100 步 L3；timestep-only 0/0/0/1/1、冷缓存严格语义/动力学一致继续通过。目录为 build/native-incremental-88748ea408c8484299d87c8577b34d55。125 项非桌面配置/导航检查、10 项身份/纯数据检查及 6 项类型化配置检查通过。

保存归一化以已有 ID 为准，不依据冲突名称重绑；显式下拉改选清除旧 ID 后在保存边界建立新绑定。历史 body equality 的空 body2（world）继续兼容。

## 阶段 4：协调职责拆分

ProjectSourceBuilder 负责源快照，NativeAssetPlanner 负责资产映射与输入哈希，NativeExportWorkspace 管理自有暂存目录，NativeExportPipeline 编排完整生成、工具调用、验证和原子发布。生产入口不回退到 Python 生成器。

14 项候选差分、125 项非桌面配置/导航回归通过。新建 COM 装配 build/native-incremental-533f1bff780b43fd9bdd3be45ca3a9e6 通过直接 CAD 核心差分、0/0/0/1/1 增量计数、冷缓存严格语义和 1/10/100 步动力学一致性；无中间 URDF/JSON。

## 阶段 5：3.1 交付

版本 3.1.0.0 Release 构建及 Inno Setup 打包通过。SolidWorks 关闭时静默安装成功，安装 DLL 与构建 SHA256 相同，64 位 COM 注册指向安装目录。安装包为 dist/SW2MuJoCo_3.1_SW2025_x64_Setup.exe。此项验证安装/注册，不声明 UI 视觉验收。

## 阶段 6：3.2 工具边界

新增三个 Tool Backend 接口和类型化请求/结构化结果。Python 不进入模型语义；mesh 结果的 ID、映射、哈希、binary STL 及预算严格核对。统一生命周期加入取消、超时、限长 UTF-8 日志和 Windows Job 自有进程树清理。关闭导出窗口请求取消并清理后再关闭。输出路径策略移出 Python reference 类，生产没有生成器 fallback。

27 项工具契约、2 项未显示窗口的关闭生命周期、14 项候选差分和 96 项后端回归通过；125 项导航、10 项身份、6 项配置快照检查继续通过。COM 新建目录 build/native-incremental-2088b1f690b44cdbaba9dcbf27cf06f0 通过上游 0/0/0、生成/验证 1/1，以及冷缓存严格语义和 1/10/100 步动力学比较。完整契约见 SW2MuJoCo_ToolBackend.md。
