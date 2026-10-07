# SW2MuJoCo 3.2.1 稳定 ID 与边界加固

3.1/3.2 已完成。本轮不改 RobotModel/MJCF/Tool Backend 主体架构，不增加仿真功能，不修改比较容差或 UI 布局。不用 computer-use；COM 验证只使用新建隔离装配，不打开用户已有工程。

分阶段验证并提交：基线；碰撞模式 Stable ID 化；显式 production/reference 入口；工具故障契约；COM/缓存/保存重开身份生命周期；安装与文档交付。

原生 timestep-only 必须保持 geometry/STL/simplification/generation/validation = 0/0/0/1/1，冷暖导出保持既有严格语义与动力学容差。现有 ID 失效禁止按名称重绑；旧名称仅在明确兼容边界一次迁移。

基线 HEAD 为 f4eb803，已有更改已提交且工作区干净。3.2 最终安装 DLL 与构建相同，COM 加载、新建空装配、四 link 增量验收和清缓存严格比较通过，记录位于 build/3.2-final-*.log；本轮另外重跑纯数据及后端基线。
