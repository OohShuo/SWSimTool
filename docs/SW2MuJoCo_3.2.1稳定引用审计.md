# SW2MuJoCo 3.2.1 最终稳定引用审计

审计日期：2026-10-07。范围为现有配置的身份、持久化、缓存和生产模型构建，不增加仿真功能，不更改界面布局或动力学比较容差。没有使用 computer-use，也没有打开用户已有 SolidWorks 工程。

## 身份清单

| 配置对象或引用 | 保存的权威 identity | 生命周期与构建策略 |
|---|---|---|
| SolidWorks Configuration 中的配置容器 | `configuration_id = swcfg:<Configuration.GetID()>` | 名称仅显示；改名跟随 ID；删除后的条目保留 unresolved；同名新建配置不继承旧树或仿真配置 |
| URDF link / joint | 各自的 `StableId` | 序列化、克隆保留 ID；新建得到新 ID；父子关系最终转换为 link ID，模型验证拒绝断开的拓扑 |
| link 所属 CAD 组件 | `SWComponentPIDs`；原配置还保存 `SWMainComponentPID` | PID 解析不按名称重找；生产来源构建遇到缺失组件立即失败，不能仅用剩余组件生成模型 |
| URDF joint 的显式坐标系 / 轴 | `CoordinateReference` / `AxisReference` 中的 FeatureId、可选 ComponentId | CAD 持久引用；名称由当前对象刷新；PID 失效不尝试同名特征。自动生成参考仍属于自动生成策略；fixed/floating 的无效闲置轴不参与解析 |
| URDF mimic | `SourceJointId` | 改名刷新输出名；删除、同名重建保持原 ID 并报错；普通 URDF 与原生 MJCF 构建都验证 |
| 附着点 / frame site | 自身 `id`，所属 `link_id`，CAD `source_pid`、可选 `component_pid` | 自身改名通过 ID 刷新引用；附加配置导出保留 link_id；模型、sidecar 和预览入口均不把失效 link ID 换成同名 link |
| sensor（含 IMU / ToF） | 自身 `id`、`site_id` | 所属 link 从 site 推断；site 删除后 unresolved；同名新 site 不重绑 |
| actuator | 自身 `id`、`joint_id` | joint 改名跟随；删除后拒绝。关节编辑器选择与默认物理参数判断也优先使用 joint ID |
| joint 物理设置 / joint 总驱动力限幅 | `joint_id` | 不依赖名称定位当前关节；删除后的配置不得显示到同名新关节上 |
| connect / weld 的 site 绑定 | 自身 `id`、`site1_id`、`site2_id` | 两端分别验证；site 改名与保存重开保持绑定 |
| connect / weld 的 body 绑定 | 自身 `id`、`body1_id`、`body2_id` | 两端分别验证；允许的空 body2 表示 world，并非悬空 ID |
| joint equality | 自身 `id`、`joint1_id`、`joint2_id` | 两端分别验证；允许的空 joint2 保持该约束的原有语义 |
| 两 site 恒力（pull / push）与弹簧 | 自身 `id`、`site1_id`、`site2_id` | 两端分别验证；禁用的条目也不允许携带悬空引用进入 RobotModel |
| link 碰撞模式 | `link_modes_by_id` | 旧 link_modes 只迁移一次；运行时不存在按名称重映射 |
| 碰撞简单几何体 | 自身 `id`、`link_id`；各拾取与尺寸引用的 CAD PID、可选组件 PID | 所属 link ID 与参考 PID 分开验证；参考失效时拒绝，标签不用于重绑 |
| 允许碰撞对 | `link1_id`、`link2_id` | 两端分别验证；任何旧 ID 失效都拒绝 |
| solver、base mode、mesh processing 等全局选项 | 所属配置容器 ID；字段为数值或策略 | 不引用可删除的 link/joint/site，不存在按名称重绑；内容哈希仍参与缓存判定 |
| 本地 URDF 与配套侧文件 | URDF 定义名是首次导入身份；新版侧文件 identities 与各 `*_id` 优先 | URDF 文件本身没有 CAD 生命周期 ID。已有侧文件 ID 失效时不按名称回退；输入/网格由内容哈希保护 |

不使用的旧字段和当前绑定方式不需要的参数不作为有效引用解析；切换绑定方式后，只对当前使用的对象要求存在。几何体尺寸、求解参数、数值 pose 本身是值，不是身份。

## 本轮发现并修复的缺口

1. mimic 原来保存源 joint 名称，改为保存 SourceJointId，并在 URDF、CAD core 和 RobotModel 边界验证。
2. 缺失的旧名称引用原来可能在后续同名对象出现时再次迁移。首次迁移未找到时现在保存 unresolved ID，后续不再按名称重试。保存树时先用旧树迁移旧配置，再处理当前树，避免删除重建时错误迁移到新对象。
3. typed RobotModel 构造原来没有对所有扩展引用统一验证。现在构造阶段拒绝所有当前有效绑定中的 dangling ID 和冲突名称；不再等到 XML 写出才发现。
4. URDF joint 的 CAD 坐标系和轴原来使用特征名。现在保存 CAD PID，刷新改名后的显示名称，删除后同名重建不会重新绑定。
5. sidecar、预览、碰撞几何和关节 UI 的少数路径仍依赖显示名。现在用权威 ID 校验；sidecar 不再丢弃附件的 link_id。
6. 保存容器原来使用 SolidWorks Configuration 名称作为 identity。现在保存 Configuration.GetID，旧条目明确迁移一次；删除配置后同名新建不继承旧数据。
7. 原生来源加载组件 PID 后只记录缺失组件、仍可能继续构建。现在缺失组件在 RobotModel 构建前明确失败，不能静默减少几何。

## 验证证据

`tests/identity/Test-StableReferenceAudit.ps1`：297 项通过。24 组引用端点分别验证迁移、创建保存重开、改名保存重开、直接 typed RobotModel 注入 dangling ID、真实缓存 Store/Find 与冷构建、删除、同名重建再保存重开、冷暖拒绝原 ID。另覆盖 mimic、碰撞模式、缺失旧名称、CAD configuration、sidecar/preview、CAD PID 和组件丢失。缓存测试使用自己的 GUID 目录。

`tests/identity/Test-CadConfigurationIdentity.ps1`：16 项真实 COM 检查通过。新建空装配，创建 configuration A、保存重开、改名保存重开、删除 A、同名创建 B、再保存重开；确认 SW GetID 保持、B 的 ID 不同且不继承 A 的配置。仅关闭本脚本创建的装配。

`tests/incremental/Verify-NativeIncrementalV1.ps1 -NativeOnly -IdentityLifecycle -CadReferenceLifecycle`：新建四 link 装配，真实保存关闭重开，验证 link 改名、CAD configuration 改名，以及坐标系/轴的改名、删除同名重建。配置和参考改名后的暖缓存、清空隔离缓存后的冷导出均成功；旧 link / 坐标系 / 轴身份在同名重建后仍然失效。

`tests/incremental/Compare-NativeIncremental.py`：上述暖缓存与冷导出的 MuJoCo 编译语义、1/10/100 步动力学比较通过；改名后的模型与改名前物理模型一致。沿用原严格容差。timestep-only 仍为 geometry query / STL export / mesh simplification / MJCF generation / MuJoCo validation = **0 / 0 / 0 / 1 / 1**。

现有回归：14 模型差分、96 Python 后端、125 导航、12 碰撞模式身份、10 已有身份、6 配置快照、4 production/reference 边界、35 工具故障契约、2 Form 生命周期通过。生产入口没有 reference fallback；失败/取消保护旧输出包。

日志与机器报告在 `build/stable-audit-*.log`、`build/stable-reference-audit-directory.txt`、`build/configuration-identity-directory.txt`、`build/native-production-v1.json`。报告和缓存不进入安装包。

## 适用边界

已经具有 ID 的引用：ID 不存在就是 unresolved/error，绝不按名称重绑。旧格式没有 ID 时只能基于当前保存树或当前 CAD 唯一对象做一次迁移，无法推断迁移之前发生过的历史删除；迁移后的 ID 才提供持续身份保证。

本轮真实 COM 覆盖装配保存容器、link、坐标系和轴；各类点/面/边及组件失效路径由共同 PID 解析代码静态审查与故障注入测试覆盖，不宣称对每种 CAD 拓扑编辑做过桌面演示。未重做 UI 视觉或 viewer 体验验收。

3.0 已记录的真实闭链 Python 序列化 reference 的 L3 精度差异不在本轮范围内，未通过放宽容差掩盖。当前稳定引用范围未发现剩余按名称重绑路径，3.2.1 可按这一范围封板；后续新增配置对象须加入此矩阵。
