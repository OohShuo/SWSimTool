# 架构与维护

## 分层与入口

| 项目 | 目标 | 职责 |
|---|---|---|
| `SWSimTool.Core` | net48 / net8.0 | 不可变机器人和仿真快照、SI 变换、完整惯量、配置记录与身份/依赖校验 |
| `SWSimTool.Application` | net48 / net8.0 | 仿真组装、源数据协调、导出计划、工具契约与流水线 |
| `SWSimTool.Infrastructure` | net48 / net8.0 | MJCF 写入、URDF/sidecar 导入、序列化、缓存文件、Python 进程与整包发布 |
| `SWSimTool.SolidWorks` | net48 | 唯一产品宿主：Addin、菜单、PropertyManager、COM/CAD/STL、文档属性与会话 |

依赖按宿主 → Infrastructure/Application/Core、Infrastructure → Application/Core、Application → Core 组织。Core 不含 COM、UI、进程或文件系统实现。SDK 项目使用 PackageReference，版本由 `Version.props` 决定；输出集中到 `build/`。SolidWorks COM 程序集为 `SWSimTool.dll`。

RobotModel 是内部物理语义模型，不是用户文件协议；测试宿主不对外发布。不能通过新增 CLI、RobotModel JSON 协议或常驻 Python 服务解决插件问题。net8/Linux 验证仅适用于可复用层。

## 导出流程

```text
SolidWorks 文档 + 活动 Configuration + 已确认的 URDF 业务树
 → 延迟 CAD 参考快照与已解析数据
 → CadRobotCoreBuilder + SimulationConfigBuilder
 → 不可变 RobotModel（Core + Simulation 快照）
 → 源数据协调、资产规划、C# 完整 MJCF 生成
 → STL 准备及 staging
 → 官方 MuJoCo 编译验证
 → 输入哈希、会话和取消检查
 → 整包原子发布
 → 可选的已有文件预览
```

Application 的 `NativeExportPipeline` 经 `IMeshPreparationService`、`IMuJoCoValidationService`、`IPreviewService`、`IAssetStore`、`IPackageStore` 和 `IMjcfWriter` 调用实现。Infrastructure 的 `NativeBackend` 组装这些服务。生产 CAD 接口不能选择参考生成器，生产失败不能回退到 Python XML 生成。

URDF 配置入口编辑业务树；直接“导出 URDF”和原配置页导出按钮共用 `AssemblyExportPreparation`。计算设置来自业务数据。导出向导使用独立副本，编辑仅影响本次输出，取消及完成导出均不隐式保存工程配置。校验失败保留当前输入、节点和选择，以便重试。

包验证后才发布。发布前失败/取消保留旧包；发布成功后的状态登记或预览失败不回滚新包。`MuJoCoToolsForm` 在发布后停止 CAD 会话监测，再按捕获的绝对 XML 路径启动文件预览；文档切换/关闭不应使已发布文件的预览失效。登记失败单独记录并尝试使源/MJCF 状态和缓存失效，清理失败也记录原始异常。

## 稳定引用与兼容存储

名称用于显示与导出，Stable ID 用于绑定。重命名保留 ID；删除后同名新建是不同对象，不能接替旧绑定。已有 ID 失效时明确报错，不按名称重试。仅旧版无 ID 记录允许在独立候选上按唯一名称迁移；重复 ID/名称必须报告，不能去重或覆盖。

CAD 引用使用持久引用，不依赖标题、路径或托管 COM 包装对象哈希。活动文档比较使用 COM identity。延迟快照保存已解析数据和稳定引用，不长期持有 Face/Edge/Component。

`IDocumentStore` 传输不透明配置字符串。文档 envelope 显式要求 v2，保留冻结的 URDF DataContract 命名空间和 schema。当前节点为 `SWSimTool Configuration`，兼容旧 `SW2MuJoCo Configuration` v2；新旧节点同时出现视为冲突。配置按 SolidWorks Configuration 存储，替换仅作用于当前条目。

写入包含回读校验和失败恢复。旧节点迁移与完整替换先备份原始存储，再验证候选；不能通过清空配置隐藏错误。内部备份用于事务恢复，不是模型导入协议。新产品不导入旧外部 sidecar、偏好和缓存。

## 草稿、会话与重建

`ConfigurationEditingContext.Tree` 是业务 `Link`；`ConfigurationPageDraft` 为每个页面创建独立工作副本和显示树。同一文档只允许一个活动编辑页。确认先收集并校验候选，再提交；取消丢弃未提交工作副本。已经显式保存的改动不会因随后取消而撤销。

树快照应保持正确父子关系，不能共享可变 TreeNode。验证、指纹和序列化不能修改已接受输入；旧 ID 的补齐只能在候选副本上执行。拖动先校验目标，插入失败恢复原位置。初始化失败、关闭和取消均释放事件、计时器、预览、控件及页面所有权，关闭后的回调必须被拒绝。

`ConfigurationSession` 绑定文档、活动 SW Configuration、配置实例与 generation。节点删除/替换、配置切换、文档关闭及 undo/redo 使旧页面、导出任务和缓存失效，旧草稿不能复活已删除节点。提交后刷新失败由 `ConfigurationCommitResult` 返回已提交警告；不能当作未保存而重复提交。

“工具 → SWSimTool → 重建配置”收集当前完整有效草稿，包括活动页面尚未确认输入，在独立候选上校验身份/依赖、清除派生源数据并重新解析 CAD，再完整替换当前条目。保留合法 ID、CAD 引用及其他 SW 配置条目，不从旧存储补回已删除字段，不自动绑定悬空 ID。替换后旧页面/模型/任务/缓存失效；后续关闭或重开失败仍属于已提交警告。删除 link/site 先计算依赖清理计划，用户取消或失败时保留原数据。

## 原生 UI 与物理语义

参数使用原生 PropertyManager 控件，URDF 树有独立自定义区域。点击、键盘和滚轮遵循 SolidWorks 默认行为；原生选择回调写入页面副本，轮询不能覆盖尚未处理的用户选择。控件按绑定、列表或业务值变化更新，保留重入和会话检查。修复动作仅在引用确实失效时显示，失效对象保留可选。

保留拓扑、ID、坐标系、质心和完整惯量。关节物理参数绑定真实 URDF joint；connect/weld 绑定 site，joint equality 绑定 joint。显式零值区别于继承；正 URDF effort 映射为关节合计驱动力范围，保留 gear 语义。不新增 MuJoCo 速度钳制或隐藏控制器。

URDF 限位编辑允许可选空值，但原生 CAD 模型构建对 revolute/prismatic 要求上下限。不能把“空白即无限制”当成通用规则；continuous 表示无限转动。修改限位语义前需同时核对 UI、URDF 校验/输出和原生构建器。

## 缓存与平台边界

Dirty 标记只是提示，正确性由指纹约束。源、网格源、处理参数和 MJCF 配置分别失效；仅求解设置变化不应触发 CAD 源重建。变换后只有证明几何未变才复用网格；几何修订不明确时使原始网格失效。面数预算、后端或版本变化可复用原始 STL，但使处理产物失效。生产与测试参考缓存来源分离。

增量导出跳过昂贵上游步骤，仍生成完整确定性 MJCF、编译并原子发布；不能补丁式更新 XML 或残留已删除对象。完整导出后只改 timestep 的目标计数为 geometry/STL/simplification 各 0，MJCF/validation 各 1。清空缓存的完整导出应保持等价编译物理，缓存不能成为唯一事实来源。

Windows 路径比较忽略大小写，Unix 使用 ordinal；不能误删 Unix 上大小写不同的文件。整包发布拒绝链接根目录/内容。Windows 用 Job Object 拥有 Python 子进程，Linux 使用独立会话/进程组；正常退出、超时及取消均清理自有后代，不终止无关进程。

## 维护入口

宿主入口为 `SW/SwAddin.cs`，草稿/会话在 `Simulation/ConfigurationEditingContext.cs`、`ConfigurationSession.cs` 和 `UI/ConfigurationPageDraft.cs`。存储、重建、CAD 源协调在宿主 `Simulation/`；原生参数适配在 `UI/NativeParameterFields.cs`、`NativeEditorPresenter.cs` 和 `NativeJointEditor.cs`。partial 类需跨文件追踪。

构建见 [根 README](../README.md)，工具协议见 [后端契约](SWSimTool_ToolBackend.md)，所有测试方法见 [tests/README](../tests/README.md)。历史验证只适用于对应提交/环境；编译通过不能证明动力学正确，mock UI 不能证明真实 SW 行为。运行时问题需比较构建、载荷、安装和已加载 DLL 的路径/哈希。
