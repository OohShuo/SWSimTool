# URDF 导出重试及可选限幅参数

本轮针对代码与自动化测试；不打开 SolidWorks，不安装插件，不操作用户 CAD。

## 根因及修改

- `ButtonJointNextClick` 原先在校验前清空 `previouslySelectedNode`。首次失败后直接编辑并重试，会跳过输入写回。
- `TryAdvanceJointPage` 每次先核对编辑节点与当前选择，再提交控件值和校验。失败保持编辑节点、选择和输入；成功交接页面后清空关节编辑节点。
- link 页独立使用 `selectedLinkNode`，避免 Next/Previous/AfterSelect 共用一个指针而向错误对象写入。
- Previous 返回时明确选择关节，重新建立编辑上下文。空指针但存在选中关节或显示输入时返回明确诊断。
- 缺项诊断包含 joint 名称及 `limit.upper` 等字段路径；验证在独立关节副本上进行。
- effort、velocity 留空允许导出；空值在编辑草稿中保持空。实际写出 URDF 的 limit 元素时沿用旧版零占位，整个可选组为空的 continuous 不写 limit。
- revolute/prismatic 的位置上下限仍需完整填写。负值、非有限数及反向范围仍拒绝。

## MJCF 范围及语义

- 当前原生导出器已经将位置上下限映射为 `limited/range`，将正值 effort 映射为 joint 总执行器驱动力限幅 `actuatorfrclimited/actuatorfrcrange`。
- 本插件的空/零 effort 不启用该限幅；这不是所有 URDF 使用方均把零视作无限制的保证。
- 独立关节设置中的显式范围及总驱动力配置可覆盖继承参数，应与树配置区别检查。
- MuJoCo joint 不提供 URDF velocity 对应的原生硬限速属性。用户已明确不实现运行时限速；velocity 保留为 URDF 参数，此次未引入 qvel 裁剪、控制器或额外阻尼，没有声称速度上限已在 MJCF 中自动执行。

## 验证结果

- 插件 Release/net48 构建：PASS。
- `tests/ui/Test-UrdfExportRetry.ps1`：PASS。真实 WinForms 输入和选择回调，SolidWorks 接口为 Mock；覆盖连续失败、直接重试、其他字段、URDF 新值、关节切换、Previous 及空编辑指针诊断。
- `tests/model/Test-UrdfLimitMapping.ps1`：PASS。9 个 continuous/revolute/prismatic × 空/零/正 effort 模型，从业务树到 RobotModel/MJCF。
- `tests/model/verify_limit_mapping.py`：PASS。MuJoCo 3.14.0 编译并检查位置范围及总驱动力限幅，无预览窗口。
- 原有配置生命周期、纯快照、保存失败和关节限位测试：PASS。
- v2 持久化兼容：19 项 PASS。
- URDF 入口静态检查：PASS。
- 真实 SolidWorks GUI、真实 CAD 导出、安装验收及本轮完整跨平台/动力学差分：NOT RUN。
- 冻结 fixture 和原有动力学容差未修改。
