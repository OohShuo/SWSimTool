# 配置保存、修复入口与 URDF 导出收口

日期：2026-10-09。工作分支：main。

## 范围与约束

保留 RobotModel、原生 MJCF、Python Tool Backend、Stable ID、CAD v2 兼容和既有物理比较容差。本轮仅修改代码并执行 Windows 构建、静态检查、托管控件及 mock CAD 自动化验证。没有打开 SolidWorks、连接其 COM 进程、安装插件或修改用户 CAD 工程。

## 根因与修复

1. 保存写入完成后仍可能在 session 刷新、共享草稿更新和引用同步中抛异常，被页面误报为未保存。现在预先验证引用同步计划并准备业务副本，提交后的失败返回 `ConfigurationCommitResult` 警告，使旧页面和任务失效。调用页面关闭路径不进入未保存重试；页内保存按钮禁用失效编辑器。
2. 重建替换已经通过回读验证，关闭和重开回调失败仍被当作重建失败。现在替换提交和后续处理分离，分别收集警告，保留备份路径，不再次替换。
3. SaveTree 无条件标记 Source/MJCF。现在比较规范化业务树及仿真内容，树未变化时不重复解析 CAD 树参考，仅按实际 solver、site、actuator 等内容变化标记 Dirty。
4. NodeIdentity 的 COM 包装对象哈希无法证明 CAD 身份。现在只接受非空 CAD 持久引用；不可获取时明确拒绝编辑/导出。查找持久引用不再要求 Attribute 包装对象具有相同的托管引用。
5. 碰撞和仿真附着点页面无条件显示重新绑定按钮，碰撞页面另有无条件清理按钮。两处均按真实失效 Stable ID 显示；失效对象保留在列表并加诊断，合法替代 link 才能启用绑定，清理仍通过依赖计划及确认执行。
6. Joint.Limit 只能在导出窗口编辑。现在树页面直接编辑同一字段，保存和重开保留数值；隐藏字段保留输入。revolute 使用 rad/N·m/rad/s，prismatic 使用 m/N/m/s，continuous 只显示力矩和速度。数值校验先完成再赋值。fixed 不输出 limit，continuous 不输出上下限，输出和关节校验不修改原对象。
7. 导出 URDF 菜单调用了配置页面。现在菜单和原按钮共同调用 `AssemblyExportPreparation`，组件解析、输入检查及计算设置均不依赖页面控件。零件入口独立保留。
8. 原导出窗口取消也会写回工程。现在它使用独立树副本，取消和完成导出均不隐式保存工程配置；编辑只作用于本次产物。附加仿真信息的解析也使用本次导出副本。计算选项保存为可选 `urdf_export` 业务设置，新设置默认计算惯量、几何及运动学，不自动覆盖手工限位；用户可选择从 CAD 配合计算限位。
9. 原按钮在保存组件 PID 之前捕获导出副本。现在先收集完整页面输入及 PID，再捕获副本并关闭；保存已提交但刷新失败时不继续导出。

收集页面输入允许更新本页工作副本，包括派生字段；失败保留该副本，保证不提前提交共享草稿或旧保存数据。旧节点兼容、完整条目替换、其他 SW 配置条目保留和禁止同名重新绑定的规则维持不变。

## 阶段提交

- `cbb1023`：保存/重建提交结果和刷新失败边界。
- `aa9f5d9`：Dirty 分类、持久节点身份及 mock 场景。
- `e629901`：各现存页面的失效引用修复入口条件显示。
- `02355b4`：树页面限位及独立导出工作副本。
- `c5af18e`：共享导出准备服务、直接菜单、业务计算设置及回归。
- `5aa8575`：捕获组件引用的顺序修复。
- `c87ae62`：只读关节校验。

## 自动化验收

最终代码提交：`c87ae62`。最终提交后回归结果和 DLL SHA256 将在本报告末尾记录。

- Test-ConfigurationRebuild：提交前写入失败保留原保存和输入；提交后 SetSaveFlag 异常返回已提交警告；旧页面重试被拒绝；关闭回调失败结果；删除重建、取消、重复打开、克隆、非法拖动和页面初始化重试。
- Joint.Limit 专项：隐藏输入保留、树序列化重开、输出类型规则、非法数值不部分赋值、导出副本不共享关节、业务计算设置复制和校验不变性。
- Test-CollisionNavigation：正常页隐藏修复动作、失效几何体仍可选、合法替代启用；现有仿真、关节、约束、滚动和条件字段回归。
- timestep-only：通过 `ConfigurationPageDraft.Flush → AttachmentService.Save → SaveTree` 保存，Dirty 仅为 Solver/MJCF；mock CAD 保存及缓存项目导出合计 geometry/STL/source build 计数不增加。
- 工具后端另行验证 cached mesh preparation/simplification 为 0、MJCF generation/validation 各 1，并执行实际 MuJoCo 编译和失败发布保护。该结果不冒充真实 SolidWorks 端到端 `0/0/0/1/1` 验收。
- Test-UrdfEntryPoints：静态证明菜单和原按钮共用不依赖控件的服务，先收集再捕获，取消/导出路径不隐式保存配置。
- v2 兼容测试原来的 JSON 字符串比较发现字典键顺序变化。实际解析后的全部内容严格相等，测试改用规范化内容哈希；没有修改冻结 fixture 或物理容差。
- 原有 backend、物理语义/动力学差分、net48/net8.0、Stable ID、事务替换、迁移、原子发布和源码清洁门禁继续运行。

## 未执行与剩余验收

- NOT RUN：真实 SolidWorks 页面交接、持久引用获取失败的实际 COM 行为、组件解析及完整 CAD 导出。
- NOT RUN：真实页面窄宽度布局、原生 PropertyManager 滚动、取消及重建后的界面回调行为。
- NOT RUN：安装、COM 加载、卸载和 Linux 执行。
- 没有生成或安装新的安装包，没有推送远程。自动化通过不能替代上述 GUI/CAD 验收。

## 最终结果

- 最终代码提交 `c87ae62f667096eb7cbdf427daf68948360980a1` 的提交后完整门禁：PASS，退出码 0。
- 命令：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Test -SolidWorksDir 'D:\sw\sw2025\SOLIDWORKS' -Python 'D:\Softwaves\python\python.exe'`。
- `build/native-parity-report.json` 和 `build/reports/target-parity-net8.json`：各 14 组 PASS，failures=0，errors=0。MuJoCo 3.14.0。
- Stable reference audit：305 项；工具故障契约：39 项；v2 兼容：19 项。
- 补充仿真附着点按钮测试：正常隐藏、失效项可选、合法目标启用、无目标禁用，PASS。日志 `build/logs/repair-actions-final.log`。
- 编译候选 DLL：`build/bin/SWSimTool.SolidWorks/Release/net48/SWSimTool.dll`，FileVersion=3.2.1.0，ProductVersion=3.2.1+swsimtool-modernization-no-cli-26-gc87ae62。
- DLL SHA256：`F7FC5836F131C75B5427BDF5F82D7C2D6C05AD4B78F85E7B869CEF14FE5D5182`。
- 冻结的 parity/reference/v2 fixture 没有改动；未修改动力学比较容差。
- 上述 DLL 是构建候选文件，未核对或替换当前 SolidWorks 已加载的 DLL。
