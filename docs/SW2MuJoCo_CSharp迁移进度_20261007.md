# C# 原生 MJCF 迁移：第一阶段实现与验收

日期：2026-10-07。

## 本轮结论

现有 2.8 增量更改已分内容提交到本地：

- `540b7f2`：增量缓存、计数、严格 timestep 场景与回归测试。
- `942b208`：2.8 打包信息及验证文档。

随后新增 C# 候选模型、生成器及差分测试。新代码尚未切换正式导出后端，不是迁移全部完成的声明。现有 Python 生成路径、普通 URDF 导出和 UI 布局保持原状。

## 增量 V1 原生验收

`scripts/Verify-NativeIncrementalV1.ps1` 在初始为空的独立 SolidWorks 实例中新建两个长方体零件和一个装配。若实例含已有文档立即拒绝。测试文件、环境缓存全部位于工作区 `build/native-incremental-<guid>/`。本轮未打开、使用或修改用户现有工程。

| 阶段 | geometry query 计数 | STL export | source build | mesh preparation | mesh simplification | MJCF generation | MuJoCo validation |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 首次完整导出 | 0 | 2 | 1 | 2 | 0 | 1 | 1 |
| 仅 timestep 0.001→0.002 | 0 | 0 | 0 | 0 | 0 | 1 | 1 |
| 清测试缓存后的全量导出 | 0 | 2 | 1 | 2 | 0 | 1 | 1 |

geometry query 是现有参考解析 instrumentation 的计数，不是拦截所有 SolidWorks COM 调用的全局计数。增量同时断言 source build=0；首次及冷缓存步骤 source build=1，并真实生成两份 STL。新建长方体低于减面阈值，首次减面=0；高面数的首次减面与增量零减面由已有五组合成验收覆盖。

`scripts/Compare-NativeIncremental.py` 将实际增量与全量包分别交给本地 MuJoCo 3.14.0 编译，比较模型语义、质量矩阵、受力、关节状态及 1/10/100 步动力学，全部通过。结果保存为 `build/native-incremental-v1.json`。

结合 96 项后端回归、125 项模拟 SW/UI 检查和此前恒力/弹簧/site/equality 的五组增量对照，当前增量 V1 的原生工程验收缺口已补上。原生装配未覆盖所有复杂机构；该结论不能泛化为所有 CAD 工程均无风险。

## 已实现的候选架构

```text
已解析、已局部化的 CAD 内存对象 → SolidWorksRobotModelBuilder ─┐
                                                          ├→ RobotCoreSnapshot
本地 URDF → UrdfRobotModelImporter ─────────────────────────┘
                                                           + SimulationConfigSnapshot
                                                           ↓
                                                     immutable RobotModel
                                                           + PreparedAssets / ExportContext
                                                           ↓
                                                     C# MjcfExporter
```

模型核心不保存 COM 对象或 XML。double 精度 Vector3d、Quaterniond、RigidTransform 和完整对称惯量类型定义坐标语义。`AFromB` 表示 `pA=R*pB+t`；惯量在质心处定义，并转换到 link 坐标系。集合和 RGBA 数据防御性复制；改变 solver 构建新 snapshot/model 并复用 core，旧模型保持不变。

CAD 适配器直接提取当前已经解析完成的内存 Robot，不生成或解析临时 URDF，也不查询 CAD 或处理 STL。它尚未替代工程导出中的完整 CAD 构建/网格阶段。

候选 writer 覆盖：基础 link/fixed/revolute/continuous/prismatic、质量惯量、简单几何和网格、site、关节物理覆盖、执行器、IMU/TOF/相机、connect/weld/joint equality、接触过滤与代理、恒力、弹簧和 solver。

## 验证

`tests/parity/` 包含独立候选 exe、持久化极小 URDF fixtures、编译语义与动力学比较器。9 项 unittest（含多个子场景）通过，机器报告记录 27 个已完成的验证场景/记录；另有无效模型拒绝及比较器错误检出测试。此数字不是 27 个完全独立测试函数。

覆盖：

- 结构、姿态、完整惯量矩阵、关节轴/限位/物理参数、初始参考状态。
- 按对象名称和 DOF 映射比较质量矩阵、广义力、qpos/qvel/qacc、执行器输出、传感器、tendon、世界位姿。
- contact exclude 和显式 pair 的对象绑定及参数。
- 实际接触/初始穿透/非零接触响应。
- 编译后的网格顶点转换到共同世界坐标，不重复应用 mesh 编译补偿。
- 独立 90° 坐标/惯量旋转、质心、平行轴加速度及缩放网格解析结果。
- 人为破坏惯量或阻尼后，比较器必须失败。
- C# 快照不可变性、零轴、不可实现惯量、路径逃逸拒绝。
- 纯 CAD 数据适配器四项检查。

旧路径 `MjSpec.to_xml()` 会缩短部分参数精度。旋转惯量样例中参考旋转矩阵最大差异约 `2.75e-7`；因此该参考文件专项使用静态 `2e-6`、100 步动态 `3e-6` 的显式容差。另将未经过 XML 序列化的 MuJoCo URDF 编译模型与 C# 模型比较，静态 `1e-9`、动态 `2e-7`，推进 1000 步通过。独立解析检查没有采用放宽后的参考容差。

## 当前边界与下一阶段

这仍是候选实现，以下工作尚未完成，不应默认启用：

1. 当前 SimulationConfigSnapshot 对 site 使用强类型，其他扩展暂时通过私有、不可外部修改的旧配置 JSON 桥接；需要迁移成完整的强类型 immutable 仿真记录。
2. 旧 CAD 树没有稳定 link/joint UUID；适配器目前以名称作为身份。正式持久化迁移前需要稳定 ID 和名称变更映射。
3. CAD 适配器依赖既有解析/局部化服务；尚未完成 RobotCore 的 CAD snapshot/cache 接入和实际工程影子导出。
4. candidate runner 的 mesh copy 只供 fixture 使用，未接入生产减面、资产缓存、预算检查和整包发布。
5. 未覆盖 mimic、planar、纹理、全部 floating 输入语义、非法配置矩阵、固定子树折叠。mimic/planar/纹理明确拒绝；不能静默 fallback。未来 fixed folding 需聚合惯性与物理坐标映射。
6. comparator 当前针对保持相同关节表示的模型；DOF 是排列映射。坐标基改变需单独实现速度、力、质量矩阵基变换。
7. 动态 fixture 的 canonical integration 输入覆盖当前无 activation/mocap 的模型；其他状态分量和接触冲量/能量等物理不变量需随新增 fixture 扩展。
8. 需完成 L0 全扩展引用验证、旧格式完整兼容、生产 compile→整包发布、故障回滚，以及两个输入路径全功能影子验收，才能切换默认 C# backend。

完整迁移继续遵循：不可变模型；强类型坐标；L2A 独立正确性与 L2B 参考兼容性并行；L3 映射后的动力学；不静默 fallback；完整 XML 重生成；缓存不进入产物；成功后整包发布；普通 URDF 保留原实现。

## 复现

```powershell
# 使用 Visual Studio MSBuild 构建纯 C# 候选 runner（无 SolidWorks 依赖）
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' tests/parity/CandidateRunner.csproj /t:Build /p:Configuration=Release
& 'D:\Softwaves\python\python.exe' tests/parity/run.py

# 原生 SW：只新建测试工程，不使用已有文档
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-NativeIncrementalV1.ps1 -Payload native-candidate
& 'D:\Softwaves\python\python.exe' scripts/Compare-NativeIncremental.py
```

候选报告：`build/native-parity-report.json`；日志：`build/native-parity-tests.log`。完整插件构建通过，有仓库既有 analyzer 警告，非零错误构建没有被忽略。
