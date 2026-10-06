# SW2MuJoCo 2.4 界面与 site 引用验证

本轮更新：传感器和约束仅选择已有 site；拾取集中到附着点定义。约束页无顶层 link 和 CAD 拾取框，site/joint 归属自动推断。旧 body 约束保留数据、转换和预览，UI 提供明确的 site 迁移操作。

site 使用稳定 ID，旧名称引用自动建立 ID。名称改变同步传感器及约束端点。拒绝空名、重复名称、重复 ID；删除后的已知 ID 不会自动绑定新建同名 site，后端导出明确报错且保留原输出。

传感器、执行器、约束按类型切换字段，合计驱动力开关和求解预设保留已有输入控件。滚动保存支持内层及外层滚动容器。碰撞、关节、仿真及约束保持实时缓存预览。原生 URDF 配置使用原 PropertyManager 和树滚动，没有新增表单重建或改变导出流程。

验证结果：

- Release x64 编译通过，DLL 版本 2.4.0.0；保留项目既有静态分析警告。
- MuJoCo 3.14.0 / 本地 Python：63 项后端测试通过。
- 独立 WinForms / 模拟 CAD 接口：85 项检查通过，包含动态字段、控件身份保留、滚动位置、230×270 仿真标签底部、约束/关节底部、site 改名、旧配置迁移、删除与同名替换。
- 安装包生成成功，包含新增 sites.py。
- 未连接 SolidWorks、未访问或修改任何用户现有 SW 工程。真实 CAD 拾取和原生 PropertyManager 宿主环境尚未现场复测。

验证日志保存在 build/ui-refresh-build.log、build/ui-refresh-backend.log、build/ui-refresh-tests.log、build/ui-refresh-setup.log。

普通 URDF/STL 和 MJCF 的输出目录约定不变。升级安装前保存工作并关闭 SolidWorks。本轮只完成实现和打包，未自动安装、提交 Git 或推送远程。
