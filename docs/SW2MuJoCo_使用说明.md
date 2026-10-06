# SW2MuJoCo

SolidWorks 2025 的机器人建模与 URDF / MuJoCo 导出插件，基于官方 SolidWorks URDF Exporter，保留原许可证和来源说明。

## 安装与升级

保存工作并关闭所有 SolidWorks 实例，运行 `SW2MuJoCo_SW2025_x64_Setup.exe`。安装器沿用旧版本 AppId、安装目录和 COM 标识，直接升级现有插件。内部程序集仍名为 `SW2URDF.dll`，这是升级兼容标识。

## 配置入口

`工具 → SW2MuJoCo` 下有 URDF 配置、碰撞配置、仿真配置、约束配置、关节配置、导出与预览。旧 `Tools → Export as URDF` 入口移除。

- URDF 配置：原 link 树编辑器；配置零件归属、link 与 joint、坐标系等。保留原来的 Preview and Export 向导，不再放置附着点或碰撞配置入口。
- 碰撞配置：左侧编辑简单几何体和允许碰撞的 link 对，保留拾取、直边驱动尺寸、条件字段和实时增量预览。
- 仿真配置：左侧按附着点、传感器、执行器、求解设置分标签。切换所属 link 后显示对应对象。附着点选择 point 或 frame，再点击拾取并在模型中选参考。point 仅记录位置；frame 记录位置和方向。可选择参考点、顶点、草图点；方向必须使用参考坐标系。
- 传感器：选择已有 frame site。先在“附着点”页创建 frame 并拾取 CAD 坐标系，再到传感器页绑定；传感器页不提供拾取。支持 IMU、TOF 和相机。IMU 输出加速度计与陀螺仪；TOF 沿 +Z 测距；相机朝 -Z，视场角单位为度。noise 固定为 0，噪声由采样层实现。
- 执行器：选择所属 link 的 joint，支持 motor、position、velocity。位置或速度增益按类型显示。转动 joint 用 rad，移动 joint 用 m，力矩 N·m、力 N；控制量与 gear 共同决定执行器映射。
- 约束配置：独立管理 connect、weld、joint。直接选择已有 site 或 joint，归属 link 自动推断；没有顶层所属 link 或拾取入口。weld 使用 frame，connect 使用 point 或 frame。历史 body 约束保留数据和预览，显示只读说明，可明确转换为 site 定义。

附着点的实时预览用球和三轴线段显示，不进入 STL 或改变质量惯量。未完成的附着点可以保存为草稿，导出时检查参考；删除或重命名 site 后需修正引用它的传感器和约束。

## 保存与旧配置迁移

所有设置写入一个 `SW2MuJoCo Configuration` 节点，内部按 SolidWorks Configuration 分开保存。页面切换 Configuration 后停止编辑，应重新打开页面。不同页面保存各自内容，保留其他部分。

旧 URDF 和 MuJoCo 节点兼容读取；读取不修改装配。明确保存配置时，先合并旧数据、写入新节点并回读验证，再移除旧节点。旧 URDF 节点原来跨所有 Configuration 共用，迁移时复制到已有 Configuration；旧仿真节点原有的分配置内容保留。未来新建 Configuration 默认没有 URDF 树，需单独配置。

点击“保存配置到装配”后，还需保存 `.sldasm` 才能落盘。副本或另存装配可能改变 CAD 持久引用，应检查并重新拾取失效参考。

## 导出 URDF

保留原预览、关节和 link 属性向导、URDF Only / URDF and Meshes 导出方式，以及原 ROS 包、CSV 等产物。在最终页面勾选“导出附加配置 (.sim.json)”才会输出侧文件，默认关闭；URDF 格式和 STL 导出不加入仿真扩展，也不减面。关闭附加配置时，不要求仿真草稿完整。

附加文件是已解析到本次 URDF link 坐标的快照，包含 URDF 文件名与 SHA256。修改 URDF 后必须重新导出配套附加配置，否则转换会拒绝不匹配的数据。点的快照仅含 xyz，frame 包含 xyz/rpy；CAD 持久引用仅供装配端重新求值。

## 导出与预览

- 从当前工程导出 MJCF：打开已配置的装配，选择输出路径。插件自动在临时目录生成原 URDF 包，按实际导出的 link 坐标解析仿真配置，再转换；无需先手动导出 URDF。导出期间不要切换装配或修改 Configuration。
- 从本地 URDF 导出 MJCF：选择已有 URDF，可选配套 `.sim.json`，不需要打开 SW 工程。没有附加文件时，仅转换 URDF 模型，默认排除内部 link 碰撞，保留环境碰撞。
- 两者均支持“导出并预览 MJCF”，转换验证成功后使用本地 MuJoCo viewer。关闭 viewer 后可继续操作。
- 预览已有 MJCF：直接选择 XML，插件不写入或修改模型文件。

最终新建目录只有：

```text
模型_mjcf/
  模型.xml
  meshes/
    mesh_0000.stl
    ...
```

要求 URDF 网格为 STL；visual 与 collision 的不同源文件分别保留，相同源文件可以复用。复制或减面完成后才交给 MuJoCo。中间 URDF、JSON 放在临时目录，结束后清理；原输入不修改。

STL 减面设置支持 PyMeshLab、fast-simplification、Blender，沿用本地 Python 与 Blender 路径。小于设定阈值的 STL 字节不变，超过阈值才减面；MuJoCo STL 面数必须在 1–200000 以内。

导出前检查名称、CAD 参考、link/joint/site 对应关系、参数范围和碰撞对。错误会停止转换并显示具体原因。转换在临时目录验证完成后才发布，失败保留原结果；清理前一次 XML 引用的旧 STL，保留其他用户文件。遇到同名且内容不同的无归属 STL 时拒绝覆盖。旧版输出中的 URDF、JSON、日志不会被自动删除，建议为首次新版导出选新目录。

Python、MuJoCo 和减面依赖由本地环境提供，不在插件内硬编码或安装。日志显示在界面，使用“保存诊断信息”手动导出；不会自动在 MJCF 目录产生日志。


## 刚性机器人求解设置（2.1）

进入 `仿真配置 → 求解设置`，点击“应用刚性机器人预设”，保存配置并重新导出 MJCF。旧配置不自动启用；不启用且没有局部覆盖时，保留原 MuJoCo 求解参数。

- 全局：步长 0.001 s、Newton、elliptic、100 次最大迭代、容差 1e-9、impratio 10、noslip_iterations 0。可以单独调整数字；Newton / elliptic 固定，refsafe 保持开启。
- 默认闭链：solref `0.005 1`、solimp `0.99 0.99 0.001 0.5 2`。在独立约束配置条目勾选“单独设置求解参数”可覆盖 connect / weld / joint 参数；取消勾选恢复继承。
- 默认接触：solref `0.003 1`、solimp `0.99 0.995 0.001 0.5 2`、margin 0.001 m、condim 3。这套接触参数比上述闭链设置更硬。
- 进入 `碰撞配置 → 碰撞对`，选中允许碰撞的 link 对，勾选“单独设置求解参数”。可将 margin 改为 0.002 m，或调整其他接触参数；同一 link 对的所有 geom pair 使用该覆盖值。未覆盖的 pair 与环境接触继承默认值。

所有数值分字段输入，不需要用空格拼接。求解参数保存在统一装配配置和可选 `.sim.json` 中；普通 URDF / STL 不改变。新增字段不改变已有侧文件的读取方式。

margin 是提前接触距离，可能导致紧邻零件提前受力或形成间隙，不能保证高速物体不穿过。solimp width 是阻抗变化宽度，不是允许穿透量。Noslip 发现慢滑后可试 2；不是 connect 修正器。时间常数低于两倍步长时，后端提示 refsafe 会限制有效值。

预览使用标准 MuJoCo viewer，不输出周期性的 Diagnostics 日志，也不安装诊断控制回调；启动信息、转换警告和错误提示仍保留。

这套参数是调试起点。若误差仍大，先排除不合理内部碰撞、过约束、异常惯量或过大驱动力，再降低步长、调整闭链 / 接触时间常数；不要仅增加迭代数。

专属字段随类型显示：position 显示 kp，velocity 显示 kv，motor 不显示增益；camera 显示 fovy，IMU / TOF 显示 cutoff。全局求解参数仅启用时显示，局部求解参数仅勾选单独设置时显示。

## 关节配置（2.3）

入口为 `工具 → SW2MuJoCo → 关节配置`，与 URDF / 碰撞 / 仿真配置并列。选择所属 joint，显示父、子 link。各 joint 独立保存，不共享参数。位置、轴向沿用 URDF。阻尼、干摩擦、附加惯量采用 actuator 引用相关的默认值，手动值优先，显式 0 有效；其余参数留空沿用原配置。

- 类型：inherit 沿用，hinge 转动，slide 移动；只支持原 URDF 的 revolute / continuous / prismatic。改变转动与移动类型必须明确选择无限位或自定义限位，避免角度与距离混用。固定、floating、planar 和 ball 不提供单自由度覆盖。
- 位置与轴向：只从 URDF 树及 CAD 参考求值，不提供覆盖输入或拾取；旧 pos / axis 及其参考覆盖不再应用。
- damping / frictionloss / armature：无 actuator 引用时默认 0.001 / 0.001 / 0；有任意 actuator 引用时默认 0.01 / 0.01 / 0.001，多个 actuator 不累加默认值。hinge 单位分别为 N·m·s/rad、N·m、kg·m²；slide 单位为 N·s/m、N、kg。
- 页面直接显示实际默认值和默认 / 手动标记。手动修改后单独保存，清空输入恢复默认，离开输入框时显示默认数字。新增、删除、改绑 actuator 后重新进入关节页即可看到新默认值，导出时也重新计算；手动值保持不变。
- 限位：inherit 沿用、none 不限位、custom 自定义。上下限分字段，初值沿用原限位，缺少时要求填写。所有角度输入使用 rad，移动使用 m。限位提前量 margin 留空不覆盖，可直接填写 0 或其他值。
- 自定义限位求解参数只在启用限位时出现，勾选后展开。默认 solreflimit `0.003 1`、solimplimit `0.99 0.995 0.001 0.5 2`；各数字独立输入，宽度单位随关节为 rad / m。保留 refsafe，时间常数过小时提示有效值受步长限制。
- 弹簧：inherit 不覆盖、off 明确关闭、custom 自定义。custom 显示刚度和零力平衡位置，刚度初值 0，不会悄悄添加弹簧。平衡位置初值为已配置 ref 或 0。第一阶段不提供自动 springdamper，避免覆盖手动 damping / stiffness。
- ref：直接输入，留空不覆盖；定义初始装配姿态对应的关节坐标值，不移动装配。
- 干摩擦大于 0 时，提供自定义摩擦求解参数；默认沿用 MuJoCo，勾选后才显示数字。
- 根基座：inherit 沿用原转换、fixed 固定、floating 添加六自由度基座。默认 inherit，常规固定根保持固定。浮动根必须具有有效质量和惯量，插件不猜测或补写它们。

实时预览展示所选关节轴向和限位示意，相关数据改变时更新缓存几何体；不会移动装配或运行物理仿真。转动限位以圆弧示意，移动限位以轴线上点示意。该示意不是机械挡块实体。

`仿真配置 → 执行器` 新增“高级：限制所属关节的合计驱动力”。勾选后分别填写下限和上限，不预填力矩。数据按 joint 保存，同一 joint 的多个 actuator 共享，不同 joint 独立。它限制 gear 映射后的合计驱动力，与单个执行器的 forcerange 分开。

保存到装配后还需保存 .sldasm 并重新导出 MJCF。关节配置和总驱动力限幅存于统一配置节点及可选 .sim.json；普通 URDF / STL 输出不改变。从工程新导出的侧文件带 joint_defaults=true，应用新的默认物理参数；已有手动值保留。历史本地 .sim.json 没有 joint_defaults 时保留原物理参数行为。旧位置与轴向覆盖始终忽略。

关节参数区完整滚动，参考坐标值 ref 等底部配置可到达。类型、限位、弹簧和高级求解开关只更新所需字段，保留无关输入控件及滚动位置；实时预览仍在有效更改后更新。

## 约束配置（2.4）

所有等式约束只在“约束配置”编辑，不加入关节配置页面。名称、启用状态和单独求解参数各条目独立保存，取消单独设置时继承全局 / MuJoCo 默认。字段按类型和绑定方式显示。

- connect：选择两个已有 site（point 或 frame），须属于不同 link；初始点不重合时仿真会拉到一起。
- weld：选择两个已有 frame，使位置与方向重合，方向由各 site 的 CAD 坐标系定义。torquescale 单独显示，默认 1 m，允许 0。
- joint：选单自由度关节一及可选关节二，分别填写 a0…a4，默认 0 / 1 / 0 / 0 / 0。公式为 y-y0 = a0 + a1(x-x0) + … + a4(x-x0)^4，其中 x0/y0 是初始模型的参考关节坐标。关节二留空时固定 y=y0+a0。只支持 hinge / slide；不会生成新运动关节，也没有独立 frictionloss / damping。

预览为 CAD 中的几何示意，不运行仿真或移动装配。site 模式显示两个端点、frame 方向和间距连线；body connect 显示连接点；body weld 显示当前与期望坐标系，长轴为期望、短轴为当前。joint 耦合没有独立空间端点，配置公式即可。只有相关数据改变才更新预览，使用已有几何缓存。

普通 URDF / STL 的导出行为和文件不改变。MJCF 输出仍只有 XML 与 meshes/STL，保留减面、从工程或本地导出、导出并预览及预览已有 MJCF。

## 界面和 site 引用（2.4）

类型、标定方式和高级开关仅切换相关字段；无关输入控件保留，滚动位置尽量保持，字段收起时仅在内容缩短超过当前滚动范围时夹回有效范围。仿真、约束、关节和碰撞页保留实时增量预览。原生 URDF 树页沿用原交互和原生滚动。

site 使用稳定内部 ID，名称用于显示与 MJCF 导出。改名同步传感器及约束引用，不改变 CAD 参考；空名、重复名会报错。旧配置按名称建立 ID 引用。删除 site 后相关条目标记失效，导出报错，必须明确重新选择；新建同名 site 不会自动接管已删除 site 的引用。

历史 body 约束仍可转换和预览，参数原样保留；新版 UI 只提供“改用 site 定义”的明确迁移操作，转换后重新选择两个 site。此操作不会替用户创建 CAD 点或坐标系。

## 性能与缓存（2.5）

- STL 超限减面结果按源文件 SHA256、面数预算、后端及版本、算法版本复用。缓存位于 `%LOCALAPPDATA%\SW2MuJoCo\mesh-cache-v1`，不放入 MJCF 包。改变源 STL、预算、后端或算法时重新处理。缓存命中仍校验哈希、二进制结构及面数；损坏时重算。写缓存失败不阻止正常导出。
- 缓存按最近使用时间清理，默认约 512 MiB；可关闭插件后删除缓存目录，下次重新生成。高级环境变量 `SW2MUJOCO_MESH_CACHE` 指定独立缓存目录；后端 mesh settings 的 `CacheEnabled=false` 可跳过缓存。
- 不超限 STL 保持原内容；原 URDF/STL 不修改。MJCF 最终仍只包含 XML 和 meshes/STL。更新包使用同卷硬链接暂存，不能硬链接时复制；写入前解除链接。失败保留旧包，不覆盖无关文件。
- CAD 源候选和碰撞几何求值按配置、模型更新戳复用。尺寸、参考或位姿更改仍更新实时预览；只改物理参数不重复更新无关 CAD 预览。新增“刷新 CAD 参考”按钮可强制失效缓存；它不会替代 SolidWorks 的重建命令，若 CAD 参考本身尚未重建，请先重建装配。
- 配置读取仍检查 Attribute 原文；原文不变时跳过重复树校验，返回独立草稿，外部编辑、撤销和切换配置按当前数据重新读取。保存仍校验全部配置并逐字回读，失败仍回滚。
- 默认关闭性能日志。设置环境变量 `SW2MUJOCO_PROFILE=1` 后，Python 输出 mesh_read、mesh_prepare、simplify、mujoco_compile、package_validation、publish 阶段耗时；C# 通过 Trace 输出 CAD 求值、候选扫描、配置解析及保存耗时。正常预览不增加 Diagnostics 周期日志。

本轮仍启动独立 Python 进程，也仍编译、验证完整 MJCF；没有引入常驻 worker、Robot IR 或跳过 CAD 导出的完整增量流程。从工程导出依旧先生成临时 URDF/STL，再转换。仅配置变化的减面结果可复用，但源网格读取和 MuJoCo 编译仍会发生。

## 两点作用力（2.6）

入口：工具 → SW2MuJoCo → 仿真配置 → 两点作用力。

1. 先在“附着点”定义两个 site，并各自拾取 CAD 参考。point 和 frame 都可，只使用位置。
2. 在“两点作用力”点击添加，填写名称；选择端点一、端点二的已有 site。所属 link 自动显示，不需要顶层 link 选择或 CAD 拾取。
3. 类型 pull 表示拉近，push 表示推开。输入非负力大小，单位 N。初值 0，启用初值为开启；0 N 不施力。勾选关闭则不生成对应恒力 tendon/actuator。
4. 保存配置到装配，再保存 .sldasm；重新导出 MJCF 或导出并预览。

两个 site 必须不同且属于不同 link。力大小恒定，方向实时沿两端连线，分别作用于两端 site，大小相等、方向相反。作用点偏离质心时产生相应力矩。它不限制距离，不创建运动关节，也不是 connect 约束或弹簧。

CAD 实时预览仅显示选中非零启用条目的两点连线示意，不运行仿真；改变力值、类型或端点不会重建整张表单。初始重合的两端在非零启用时无法导出；运动中也应避免两点重合，因为连线方向无定义。

配置保存于统一装配节点，并随可选 .sim.json 导出；site 改名自动同步，删除后的 ID 不会被同名新 site 接管。旧配置不含 site_forces 时自动按空列表读取。普通 URDF/STL 不变，MJCF 最终仍只有 XML 和引用的 meshes/STL（没有网格时只生成 XML）。

后端生成两 site 的 spatial tendon 与 general actuator：gear=1，gainprm=0，affine bias 的常数项为 pull 的 -F 或 push 的 +F，长度和速度项为 0；不添加 tendon 弹簧、阻尼或长度限位。恒力与 ctrl 值无关，不需要 Python 每步回调，标准 viewer 打开 XML 后即生效。生成名称分别为 sw2mujoco_tendon_<配置名> 和 sw2mujoco_force_<配置名>。

每条启用配置会新增一个 general actuator，因此 model.nu 增加；这个控制槽的输入被忽略。外部控制脚本应按 actuator 名称定位关节执行器，避免假定所有控制槽都对应电机。原 joint 执行器的配置和按 actuator 引用选择的关节默认物理参数不变。

## 两 site 弹簧（2.7）

入口：仿真配置 → 两点作用力，添加条目并选择类型 spring。原有 pull / push 恒力功能保留。

- 端点：选择两个属于不同 link 的已有 site，point/frame 均可，只使用位置；无需额外拾取。
- 刚度 stiffness：单位 N/m，默认 0。
- 阻尼 damping：单位 N·s/m，默认 0。
- 自然长度：initial 取导出模型初始姿态下两 site 的距离；custom 时才显示自然长度输入，单位 m。首次自定义字段为 0.1 m，应根据实际弹簧填写。
- 启用：默认开启；关闭时不生成该条目。刚度和阻尼均为 0 时不施力。

沿两端连线，作用于端点二的标量力为 `-k(L-L0)-c*dL/dt`，端点一受等大反向力。拉伸产生拉力，压缩产生推力，偏心安装可产生力矩。当前是双向线性弹簧，未提供仅拉伸或仅压缩模式。例如 k=1000 N/m、c=2 N·s/m 可作为演示输入，不是所有机构的通用默认值。

initial 在导出时计算实际初始距离并写为数值 springlength，不受另一套关节弹簧参考姿态影响；初始弹性力为零。custom 可设置预拉伸或预压缩。有效弹簧两端初始重合会阻止导出，运动中也应避免重合。

后端生成原生 spatial tendon，使用 stiffness、damping、springlength，无长度限位、干摩擦或额外 actuator。无需 Python 每步回调，标准 MuJoCo viewer 即可运行。弹簧不会增加 model.nu。MJCF 仍只输出 XML 和引用的 meshes/STL；普通 URDF/STL 不变。

CAD 实时预览仍是两点连线示意，不绘制弹簧线圈或运行物理仿真。类型和自然长度方式只更新相关字段，保留输入、无关控件和滚动位置。配置存于统一装配节点及可选 sim.json，复用稳定 site ID；保存配置后仍须保存装配并重新导出 MJCF。
