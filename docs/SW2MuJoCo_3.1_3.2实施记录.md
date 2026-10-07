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
