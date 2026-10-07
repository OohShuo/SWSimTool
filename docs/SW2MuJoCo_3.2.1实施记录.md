# SW2MuJoCo 3.2.1 稳定 ID 与边界加固

3.1/3.2 已完成。本轮不改 RobotModel/MJCF/Tool Backend 主体架构，不增加仿真功能，不修改比较容差或 UI 布局。不用 computer-use；COM 验证只使用新建隔离装配，不打开用户已有工程。

分阶段验证并提交：基线；碰撞模式 Stable ID 化；显式 production/reference 入口；工具故障契约；COM/缓存/保存重开身份生命周期；安装与文档交付。

原生 timestep-only 必须保持 geometry/STL/simplification/generation/validation = 0/0/0/1/1，冷暖导出保持既有严格语义与动力学容差。现有 ID 失效禁止按名称重绑；旧名称仅在明确兼容边界一次迁移。

基线 HEAD 为 f4eb803，已有更改已提交且工作区干净。3.2 最终安装 DLL 与构建相同，COM 加载、新建空装配、四 link 增量验收和清缓存严格比较通过，记录位于 build/3.2-final-*.log；本轮另外重跑纯数据及后端基线。

## 阶段 2：碰撞模式身份

增加 link_modes_by_id 和明确迁移标记；link_modes 仅作旧名称输入/侧文件投影。MigrateLegacyLinkModesByName 在旧保存树上唯一解析，RemapLinkModesByStableId 不按名称转移。删除 ID 保留 unresolved，native 或新版侧文件导出明确失败。UI 选择仍显示原名称，写入 ID；普通 URDF sidecar 同时保留兼容名称投影和权威 ID 映射。

连续纯数据生命周期覆盖创建、保存重开、重命名、冷暖快照、删除、同名重建、再保存重开、失效 ID 拒绝，以及侧文件 ID 优先和空 ID 映射不回退。12 项检查、10 项已有身份、6 项配置快照、125 项配置导航及 14 项模型差分通过。日志 build/3.2.1-identity-lifecycle.log 等。未调整任何比较容差。

## 阶段 3：入口与缓存来源

ProjectExport 唯一 public 构造固定为 native，reference 仅能显式调用 ForReferenceTests；source builder 对外分别提供 BuildNative/BuildReference，无公开模式开关。缓存记录 nativeSource，不能跨 reference/native 复用核心。4 项边界检查、125 项导航及新建装配的 0/0/0/1/1 验收通过，冷暖严格语义和 1/10/100 步动力学比较通过。新建装配目录 build/native-incremental-7ba959f805264f45a5350d2068933ab7。

## 阶段 4：工具故障契约

保留接口/进程结构，仅补充结构化错误协议及分类：EnvironmentMissing、DependencyMissing、InvalidInput、InputChanged、BudgetExceeded、ToolFailed、ValidationFailed，以及既有 Timeout/Cancelled/Protocol。官方 compile 不接受的 XML 单独归类；无结构化诊断的异常退出仍保存退出码/stderr。无法达到面数预算抛出明确异常而非猜测错误文本。C# 对输出面数独立检查。

35 项工具检查及 96 项后端回归通过。缺 MuJoCo/fast-simplification/PyMeshLab 由隔离测试进程拦截模块导入复现，不卸载或修改用户依赖；减面无效、超预算、设置变动、损坏错误协议、超时/取消、进程树清理、验证失败保护旧包均覆盖。日志 build/3.2.1-tools.log、build/3.2.1-backend.log。

## 阶段 5：COM 持久化与缓存

新建四 link 装配 build/native-incremental-78dad41fb6c148f18dd8e381bd1819fe，在真实 SLDASM 上保存/关闭/重开，rename 后再次保存重开，碰撞模式仍绑定原 ID。删除后同名重建得到新 ID，保存重开后不继承旧模式；旧 ID 保留未解析状态，冷构建明确拒绝。没有打开任何用户工程，没有使用 computer-use。

该装配 timestep-only 保持 0/0/0/1/1；改名后的暖缓存与清空隔离缓存后的完整导出，用 MuJoCo 比较编译语义及 1/10/100 步动力学，沿用既有严格容差。恒力、弹簧、site、IMU、actuator、connect 和简单碰撞体都保留在同一验收模型中。日志 build/3.2.1-identity-cad.log、build/3.2.1-identity-compare.log。
