# SWSimTool 全量现代化总控 Prompt

你正在维护和现代化一个 SolidWorks 2025 插件项目。

当前项目源自 `ros/solidworks_urdf_exporter`，经过长期开发后已经从原始 SW2URDF 演进为面向 SolidWorks → URDF / MuJoCo 的工具。当前产品将完整更名为：

    SWSimTool

本任务不是简单批量重命名，也不是一次性“大重构”。

最终目标：

1. 将产品、程序集、namespace、安装器、COM identity、运行目录等完整迁移为 SWSimTool；
2. 保持现有 SolidWorks 工程中 `SW2MuJoCo Configuration` v2 数据无痕兼容；
3. 整理仓库，使所有可生成的构建、测试和临时产物统一进入 `build/`；
4. 将当前单体式 SolidWorks 插件逐步拆分为现代、通用、可独立测试的架构；
5. 逐步迁移到 SDK-style / PackageReference / 现代 MSBuild 工程体系；
6. 建立清晰的分层测试和 CI；
7. 最后再引入 .NET 8、multi-target、CLI 和 cross-platform Core CI；
8. 全程保持现有生产功能、物理语义、增量构建语义和发布安全性。

---

# 1. 总执行模式：自动推进 + 阶段质量门禁

不要在每个 Phase 后等待用户手动回复“继续”。

默认执行模式：

    Phase N
       ↓
    audit current state
       ↓
    implementation
       ↓
    build
       ↓
    automated tests
       ↓
    regression
       ↓
    self-audit
       ↓
    fix discovered engineering issues
       ↓
    rerun required tests
       ↓
    git diff review
       ↓
    workspace cleanliness check
       ↓
    commit
       ↓
    post-commit audit
       ↓
    PASS ?
       ├── YES → tag/checkpoint → automatically continue Phase N+1
       └── NO  → determine whether it is a normal implementation bug
                    ├── YES → fix → retest → reaudit
                    └── NO  → Human Decision Gate → STOP

Agent 应自行完成：

- 阶段实施；
- 自动测试；
- 回归测试；
- 自审计；
- 修复明确的工程问题；
- 阶段 commit；
- post-commit audit；
- 阶段 tag/checkpoint；
- 在质量门禁通过后自动进入下一阶段。

用户不是每个阶段的人工 CI。

只有满足后文定义的 Human Decision Gate 时才停止询问用户。

如果当前仓库已经完成某 Phase 的部分或全部内容：

    audit current state
        ↓
    compare against phase requirements
        ↓
    implement only missing items

所有判断以当前源码为准。不要因为本 Prompt 的历史描述与当前代码不一致而覆盖当前正确实现。

---

# 2. Human Decision Gate

仅在以下情况 STOP 并请求用户决策：

1. 现有明确需求之间发生实质冲突；
2. 必须改变用户明确指定的 compatibility boundary；
3. 必须改变 RobotModel / MJCF 的物理语义且没有唯一正确方案；
4. 出现无法判断属于 implementation bug 还是 reference/model bug 的新回归；
5. 必须降低测试标准、放宽 tolerance 或删除测试才能继续；
6. SolidWorks / .NET 平台事实导致计划路线不可行，需要在不同架构方案间选择；
7. 必须破坏 frozen compatibility fixture 才能继续；
8. 需要修改真实用户 CAD 数据；
9. 需要执行不可逆或明显高风险的仓库/发布操作；
10. 存在多个具有明显产品行为差异的合理方案，而当前需求无法决定。

以下情况不属于 Human Decision Gate，Agent 应自行修复：

- 编译错误；
- namespace 漏改；
- 测试路径错误；
- installer 少 DLL；
- PackageReference 配置错误；
- Linux/Windows path separator bug；
- nullable/compiler warning；
- 脚本仍引用旧 solution 名；
- 构建产物污染源码树；
- 普通 test failure 且原因可明确定位为本阶段实现错误；
- 文档路径或构建脚本错误。

---

# 3. 当前核心架构背景

当前项目已经形成以下核心生产路径：

    SolidWorks CAD
        ↓
    CadReferenceSnapshot / native CAD snapshot
        ↓
    RobotCoreBuilder / CadRobotCoreBuilder
        ↓
    RobotCoreSnapshot

以及：

    SimulationProject
        ↓
    SimulationConfigBuilder
        ↓
    SimulationConfigSnapshot

然后：

    RobotCoreSnapshot + SimulationConfigSnapshot
                    ↓
             immutable RobotModel
                    ↓
               ExportPlanner
                    ↓
             Asset Preparation
                    ↓
              C# MjcfExporter
                    ↓
             staging package
                    ↓
           MuJoCo validation
                    ↓
             atomic publish

RobotModel 是生产路径中的模型语义真源。

Python Tool Backend 的定位：

    IMeshPreparationService
    IMuJoCoValidationService
    IPreviewService

Python 可以负责：

- mesh preparation / STL processing；
- mesh simplification；
- MuJoCo Python binding compile validation；
- mujoco.viewer preview；
- scientific/reference/parity tests。

Python 不得：

- 重新决定 link/joint topology；
- 重新解释 stable ID；
- 重新决定 site/equality semantics；
- 重新决定 inertia semantics；
- 重新生成一套独立的 production RobotModel；
- 作为 Native C# MJCF exporter 的 silent fallback。

核心原则：

    C# Core owns SWSimTool domain semantics.
    Python Tool Backend only provides external tool/runtime capabilities.

---

# 4. 永久系统不变量

这些约束优先级高于所有现代化目标。任何 Phase 破坏其中任一关键约束，应修复或触发 Human Decision Gate，而不是继续堆叠重构。

## 4.1 URDF

普通 URDF 导出行为不得因为现代化发生非预期改变。

URDF 是真实支持的领域功能。禁止因为历史名称 SW2URDF 而删除或机械替换所有 `URDF` 字样。

同样，`MuJoCo` 是真实领域术语，不应因产品更名而消失。

## 4.2 CAD 持久化兼容边界

SWSimTool 是一个新的插件产品。

不要求兼容：

- 旧安装器；
- 旧 COM 注册；
- 旧 LocalAppData；
- 旧 cache/log；
- 旧 standalone sidecar；
- 旧 `MuJoCo Simulation Configuration (v1)`；
- 旧 URDF-only Attribute。

但必须继续兼容现有 SolidWorks CAD 文件中的：

    "SW2MuJoCo Configuration"

且兼容目标明确限定为：

    Document.version == 2

当前该 Attribute 中保存 JSON Document，至少包含：

    version
    configurations

每个 configuration 包含：

    configuration_id
    configuration_name
    urdf_xml
    urdf_version
    simulation

不要因为产品更名而直接把持久化 Attribute 改成 `"SWSimTool Configuration"`。

将 `"SW2MuJoCo Configuration"` 明确视为：

    persistent CAD storage identifier / file-format ABI

而不是当前产品显示名称。

## 4.3 DataContract XML ABI

当前 CAD storage 的 `urdf_xml` 可能包含：

    xmlns="http://schemas.datacontract.org/2004/07/SW2URDF"

即使 CLR namespace 从：

    SW2URDF.*

迁移为：

    SWSimTool.*

也必须确保旧 XML 正确反序列化。

审计：

- `ConfigurationSerialization`；
- persisted URDF model types；
- `DataContract`；
- `CollectionDataContract`；
- `DataContractSerializer`。

不要假设 CLR namespace rename 不影响 serialized namespace。

目标：

    CLR namespace:
        SWSimTool.*

    persistent XML namespace:
        http://schemas.datacontract.org/2004/07/SW2URDF

旧 serialization namespace 可以作为显式 compatibility constant 保留。

## 4.4 Stable ID

以下对象 identity 必须由 stable ID 表示，而不是 name：

    Link
    Joint
    Site
    Collision
    Contact
    Equality
    Actuator
    Sensor
    ConstantForce
    Spring

原则：

    Stable ID = identity
    Name      = editable/export name

已有 ID 如果不能解析：

    ERROR

不得自动绑定到同名的新对象。

rename：

    name changes
    ID unchanged

delete + recreate same name：

    new object must get new ID

保存/重新打开不得无故重新生成 ID。

## 4.5 RobotModel

RobotModel 保持 immutable snapshot 思路。

至少保持：

    RobotModel
    ├── SchemaVersion
    ├── SourceIdentity
    ├── RobotCore
    │   ├── Links
    │   ├── Joints
    │   └── MeshSources
    └── SimulationModel
        ├── Sites
        ├── CollisionGeometry
        ├── ContactRules
        ├── Equalities
        ├── Actuators
        ├── Sensors
        ├── ConstantForces
        ├── Springs
        └── SolverOptions

单位保持 SI：

    m / kg / s / rad

transform/frame semantics 必须明确。

full symmetric inertia tensor 语义不得退化。

## 4.6 Incremental export

永久保持 timestep-only 等纯 MuJoCo 参数修改：

    geometry query       = 0
    STL export           = 0
    mesh simplification  = 0
    MJCF generation      = 1
    MuJoCo validation    = 1

不得因项目拆分、cache 重构、CLI 或 runtime 迁移破坏。

## 4.7 发布安全

保持：

    staging
      ↓
    MuJoCo validation
      ↓
    atomic publish

MuJoCo compile validation 失败：

    不得覆盖上一份有效 package

所有最终发布 MJCF 必须通过官方 MuJoCo compile validation。

## 4.8 Reference / parity

Reference path 是测试基础设施，不等于 production compatibility path。

不得因删除 legacy compatibility 而删除必要的：

- Python reference tests；
- analytical tests；
- parity tests；
- dynamics tests。

如果存在真实 closed-loop/reference discrepancy：

    保留为 known difference

不得通过放宽 tolerance 伪装 PASS。

## 4.9 测试数据安全

不得使用真实用户 SolidWorks 工程做破坏性测试。

SolidWorks integration test 只能使用：

- 新建测试装配体；
- 明确测试 fixture；
- 测试工程副本。

## 4.10 工作目录

最终要求：

    所有可重新生成的文件
        ↓
    build/

源码目录不得残留：

    bin/
    obj/
    packages/
    TestResults/
    __pycache__/
    .pytest_cache/
    generated STL
    generated MJCF
    generated reports
    generated logs
    generated installer
    temporary SolidWorks documents

最终目标：

    Remove-Item -Recurse -Force build

即可删除所有生成物并恢复纯源码工作区。

---

# 5. 最终目标目录

最终方向：

SWSimTool/
├── src/
│   ├── SWSimTool.Core/
│   ├── SWSimTool.Application/
│   ├── SWSimTool.Infrastructure/
│   ├── SWSimTool.SolidWorks/
│   └── SWSimTool.Cli/
│
├── tests/
│   ├── SWSimTool.Core.Tests/
│   ├── SWSimTool.Application.Tests/
│   ├── SWSimTool.Infrastructure.Tests/
│   ├── SWSimTool.Compatibility.Tests/
│   ├── SWSimTool.SolidWorks.IntegrationTests/
│   ├── fixtures/
│   └── parity/
│
├── runtime/
│   └── python/
│
├── tools/
│   ├── build/
│   ├── test/
│   └── install/
│
├── installer/
│   └── install.iss
│
├── docs/
├── examples/
│
├── build/                         # ignored/generated
│   ├── bin/
│   ├── obj/
│   ├── logs/
│   ├── reports/
│   ├── test-results/
│   ├── test-work/
│   ├── native-parity/
│   ├── integration/
│   ├── runtime-release/
│   └── dist/
│
├── Directory.Build.props
├── Directory.Build.targets
├── SWSimTool.sln
├── build.ps1
├── .gitignore
├── README.md
└── LICENSE

这是最终方向，不要求早期 Phase 为了目录形式提前创建无意义的空项目。

---

# 6. Phase 0 — 建立现代化前基线

## 目标

在 rename/refactor 前冻结当前正确行为。

如果当前代码已经完成这一阶段，则审计并补缺，不重复制造 baseline。

## 任务

建立功能矩阵：

    URDF export
    Native MJCF export
    mesh preparation
    simplification
    validation
    preview
    incremental export
    cache
    site
    collision
    contact
    equality
    actuator
    sensor
    constant force
    spring
    solver
    SolidWorks persistence
    installer
    COM registration

创建冻结的：

    tests/fixtures/sw2mujoco-v2/

compatibility fixture。

fixture 至少覆盖：

    multiple links
    multiple joints
    renamed object
    site
    collision
    contact
    equality
    actuator
    sensor
    constant force
    spring
    custom solver
    link_modes_by_id

该 fixture 应由旧格式/当前兼容源生成后冻结，后续 SWSimTool 不得为了让测试通过而改写 golden fixture。

保存语义基准。不要把 XML 字节完全一致作为唯一标准。

比较：

    hierarchy
    transforms
    mass
    COM
    full inertia
    joint axis
    limits
    collision
    site
    equality
    actuator
    sensor
    forces
    springs
    solver

运行：

    L0 Model Validity
    L1 MuJoCo compile
    L2A Analytical
    L2B Reference parity
    L3 dynamics
    incremental
    Python Tool Backend fault matrix
    SolidWorks integration
    installer validation

明确记录 known differences。

## 验收

    BASELINE PASS

建议 commit：

    test(baseline): freeze pre-SWSimTool compatibility and regression baseline

建议 tag：

    pre-swsimtool-modernization

通过自动质量门禁后进入 Phase 1。

---

# 7. Phase 1 — 完整更名为 SWSimTool

## 目标

产品、代码、安装、运行身份完整变成 SWSimTool。

但保留：

    SW2MuJoCo Configuration v2
    old DataContract serialization namespace

作为 CAD compatibility ABI。

## 7.1 文件身份

按当前仓库实际情况迁移：

    SW2URDF.sln       → SWSimTool.sln
    SW2URDF/          → SWSimTool/
    SW2URDF.csproj    → SWSimTool.csproj
    SW2URDF.ruleset   → SWSimTool.ruleset

## 7.2 CLR identity

生产代码：

    namespace SW2URDF.* → SWSimTool.*
    using SW2URDF.*     → SWSimTool.*

同步修改 XAML：

    x:Class
    clr-namespace

Assembly：

    SW2URDF.dll → SWSimTool.dll

## 7.3 SolidWorks UI identity

改为 SWSimTool：

    add-in title
    command group
    menu
    dialog title
    status text
    log filename
    about information

## 7.4 新 COM identity

SWSimTool 是新插件。

生成新的 CLSID。

不得继续使用旧 SW2URDF/SW2MuJoCo CLSID。

同步：

    SwAddin
    registration scripts
    installer
    COM validation tests

## 7.5 新 Installer identity

生成新的 Inno Setup AppId。

目标：

    AppName=SWSimTool
    DefaultDirName=...\SWSimTool
    OutputBaseFilename=SWSimTool_...

SWSimTool installer：

- 不升级旧 SW2MuJoCo；
- 不卸载旧 SW2MuJoCo；
- 不清理旧产品文件。

原则上允许旧插件与 SWSimTool 并存安装。

## 7.6 新 LocalAppData

全部新运行状态使用：

    %LOCALAPPDATA%\SWSimTool

包括：

    cache
    mesh cache
    settings
    logs

不迁移旧 LocalAppData。

## 7.7 CAD compatibility

保留：

    "SW2MuJoCo Configuration"

只支持：

    version == 2

若代码仍存在：

    LegacySimulationConfigImporter
    MuJoCo Simulation Configuration (v1)
    old URDF attribute migration
    old sidecar migration

先审计用途。

如果确认只服务不再支持的旧格式，可以删除。

不要删除 reference/parity infrastructure。

## 7.8 DataContract ABI

确保旧：

    http://schemas.datacontract.org/2004/07/SW2URDF

在 CLR namespace 改为 SWSimTool 后仍可读取。

优先显式固定 serialization schema。

## 7.9 Stable ID

验证：

    old CAD load
    rename
    save
    reopen

stable IDs 不变。

验证：

    delete A
    create same-name B

B 不得继承 A 的 ID。

## 7.10 测试

至少：

- Build；
- existing unit/regression；
- frozen CAD compatibility fixture；
- Native MJCF export；
- MuJoCo compile；
- save/reopen；
- timestep-only incremental invariant；
- installer；
- COM registration；
- ordinary URDF regression。

compatibility fixture 应验证：

    URDF tree
    SimulationProject
    stable IDs
    link modes
    site
    collision
    contact
    equality
    actuator
    sensor
    force
    spring
    solver

## 7.11 Legacy-name audit

运行：

    git grep -n -i "SW2URDF"
    git grep -n -i "SW2MuJoCo"
    git grep -n -i "sw2mujoco"
    git grep -n -i "URDFExporter"

逐条分类。

允许残留：

- persistence compatibility constants；
- DataContract schema；
- compatibility fixtures/tests；
- SOURCE_PROVENANCE；
- LICENSE/copyright；
- genuine historical attribution。

其他当前产品身份迁移为 SWSimTool。

## 7.12 commits

建议：

    refactor(identity): rename product and assembly to SWSimTool
    refactor(com): establish independent SWSimTool add-in identity
    refactor(installer): establish independent SWSimTool installation identity
    feat(compat): preserve SW2MuJoCo Configuration v2 document compatibility
    test(compat): verify seamless SW2MuJoCo v2 project reopening
    docs(rename): update product documentation to SWSimTool

tag：

    swsimtool-rename-complete

post-commit audit PASS 后自动进入 Phase 2。

---

# 8. Phase 2 — 工作目录与构建产物整理

本阶段不要拆 Core/Application。

## 目标

    所有 generated artifacts → build/

## 8.1 build tree

建立：

    build/
    ├── bin/
    ├── obj/
    ├── logs/
    ├── reports/
    ├── test-results/
    ├── test-work/
    ├── native-parity/
    ├── integration/
    ├── runtime-release/
    └── dist/

## 8.2 C# output

统一：

    BaseOutputPath
    BaseIntermediateOutputPath

按：

    project
    configuration
    target framework

隔离。

禁止多个项目共享同一 obj。

## 8.3 Test output

所有：

    test logs
    reports
    generated XML
    generated STL
    temporary SW documents
    parity artifacts

进入 `build/`。

## 8.4 Python

源码树禁止产生：

    __pycache__
    .pytest_cache

必要时设置：

    PYTHONDONTWRITEBYTECODE=1

Python test cache/output 进入 `build/`。

## 8.5 Release payload

建立：

    tools/build/AssembleRelease.ps1

或等价脚本。

职责：

    compiled binaries
        +
    runtime/python
        +
    docs
        +
    LICENSE
        ↓
    build/runtime-release/

## 8.6 Installer

Installer 只消费：

    build/runtime-release/

禁止直接从：

    src/
    runtime/
    docs/
    arbitrary bin/

拼装产品。

## 8.7 Unified build entry

根目录：

    build.ps1

至少支持：

    .\build.ps1
    .\build.ps1 -Test
    .\build.ps1 -Package
    .\build.ps1 -Installer
    .\build.ps1 -Clean

语义：

default:
    restore
    build

-Test:
    restore
    build
    tests

-Package:
    restore
    build
    tests
    assemble
    payload validation

-Installer:
    Package
    installer
    installer validation

-Clean:
    remove generated build tree

## 8.8 Clean workspace test

自动检查：

    src/**/bin          forbidden
    src/**/obj          forbidden
    tests/**/bin        forbidden
    tests/**/obj        forbidden
    __pycache__         forbidden
    .pytest_cache       forbidden
    generated logs outside build forbidden

## 8.9 验收

从 clean checkout：

    .\build.ps1 -Installer

生成：

    build/dist/SWSimTool_*_Setup.exe

然后：

    .\build.ps1 -Clean

恢复纯源码工作树。

完整 regression 通过。

建议 commits：

    build: centralize generated artifacts under build directory
    build: add unified build and clean entry points
    build: separate compiler outputs from release payload
    build(installer): consume verified runtime-release payload only
    test(build): enforce clean workspace after builds

tag：

    swsimtool-clean-build-layout

post-commit audit PASS 后进入 Phase 3。

---

# 9. Phase 3 — 架构分层

现在才允许拆项目。

## 目标

第一版：

    SWSimTool.Core
    SWSimTool.Application
    SWSimTool.Infrastructure
    SWSimTool.SolidWorks

只移动职责，不改变业务语义。

不要升级 .NET 8。

不要为了形式主义创建大量项目。

## 9.1 Core

放：

    RobotModel
    Link
    Joint
    Stable IDs
    transforms/math
    inertia
    simulation domain model
    domain validation primitives

禁止引用：

    SolidWorks.Interop
    WPF
    WinForms
    registry
    Python
    Process
    concrete filesystem
    installer

## 9.2 Application

放：

    RobotCoreBuilder orchestration
    SimulationConfigBuilder
    ProjectSourceBuilder
    ExportPlanner
    export workflow
    commands/use cases

按真实边界定义：

    IMeshPreparationService
    IMuJoCoValidationService
    IPreviewService
    IDocumentStore
    ICadSource
    IAssetStore

不要为每个 class 创建 interface。

## 9.3 Infrastructure

放：

    MjcfExporter
    PythonToolBackend
    filesystem implementation
    cache
    asset preparation
    process runner
    non-SolidWorks serialization infrastructure

若后续确有价值，可再拆：

    SWSimTool.Export.Mjcf

但本阶段避免过度拆分。

## 9.4 SolidWorks

只保留：

    SwAddin
    CommandManager
    UI
    PropertyManagerPage
    SolidWorks COM adapter
    CAD snapshot acquisition
    STL export adapter
    SolidWorks Attribute transport
    document lifecycle

目标：

    SolidWorks COM
        ↓
    snapshot / DTO
        ↓
    Application/Core

COM object 不应长期穿透进入 Core。

## 9.5 Persistence boundary

区分：

    persistence semantics
    SolidWorks transport

例如：

    SolidWorks Attribute
        ↓
    string
        ↓
    common persistence serializer

JSON/DataContract schema semantics 不应不必要依赖 COM。

## 9.6 Architecture tests

至少：

    Core !→ SolidWorks
    Core !→ Infrastructure
    Application !→ SolidWorks
    reusable layers !→ UI

## 9.7 Regression

完整运行：

    unit
    analytical
    parity
    dynamic
    incremental
    Tool Backend
    CAD v2 compatibility
    SolidWorks COM
    installer

特别验证 timestep-only invariant。

建议 commits：

    refactor(core): extract domain model into SWSimTool.Core
    refactor(app): extract application workflows and service contracts
    refactor(infra): isolate exporters tooling cache and filesystem services
    refactor(sw): reduce SolidWorks project to host and CAD adapters
    test(architecture): enforce project dependency boundaries

tag：

    swsimtool-layered-architecture

post-commit audit PASS 后进入 Phase 4。

---

# 10. Phase 4 — 现代 .NET 工程格式

## 目标

    SDK-style csproj
    PackageReference
    Directory.Build.props
    Directory.Build.targets
    centralized version/config
    deterministic build

暂时不要求整个产品 net8。

## 10.1 SDK-style

优先迁：

    Core
    Application
    Infrastructure
    Tests

SolidWorks host 最后处理。

## 10.2 PackageReference

迁移：

    packages.config
    ../packages/...

到：

    PackageReference

最终仓库不依赖 repository-local `packages/`。

## 10.3 Directory.Build.props

集中：

    output paths
    LangVersion
    deterministic build
    common metadata
    version
    warning policy

Nullable 按项目逐步迁移，不要一次制造海量无关 diff。

## 10.4 Version

建立单一版本真源：

    AssemblyVersion
    FileVersion
    InformationalVersion
    ProductVersion
    installer version
    release filename

## 10.5 Reproducibility

验证：

    clean build A
    clean build B

至少：

    payload file set identical
    version metadata identical
    generated MJCF deterministic

## 10.6 Tests

验证：

    clean restore
    no repository packages/
    command-line build without Visual Studio GUI
    deterministic MJCF
    payload dependency completeness
    full regression

建议 commits：

    build(dotnet): migrate reusable projects to SDK-style format
    build(nuget): replace packages.config with PackageReference
    build(config): centralize common MSBuild properties
    build(version): centralize product version metadata
    test(build): verify clean reproducible package construction

tag：

    swsimtool-modern-build-system

post-commit audit PASS 后进入 Phase 5。

---

# 11. Phase 5 — 测试体系与 CI 分层

## 目标

形成：

    Fast
    Medium
    SolidWorks Integration
    Release

四级测试。

## 11.1 Fast

每次 commit：

    Core unit
    Application
    serialization
    MJCF generation
    analytical
    stable ID

## 11.2 Medium

PR/merge：

    Python Tool Backend
    MuJoCo compile
    parity
    dynamics
    incremental/cache
    fault injection

## 11.3 SolidWorks Integration

Windows + SolidWorks：

    COM registration
    add-in load
    CAD snapshot
    mass/inertia
    STL export
    Attribute persistence
    compatibility fixture

## 11.4 Release

发布前：

    clean checkout
    restore
    build
    all required tests
    assemble payload
    validate payload
    build installer
    install
    register
    launch/load add-in where automation is reliable
    open compatibility fixture
    export MJCF
    MuJoCo compile
    uninstall
    verify cleanup

## 11.5 Fault tests

继续保留：

    invalid XML
    missing Python
    missing MuJoCo
    missing support script
    missing simplifier
    invalid protocol
    stderr/exit
    timeout
    cancellation
    child process cleanup
    invalid STL
    path escape
    duplicate asset ID
    budget exceeded

## 11.6 Reports

全部：

    build/reports/
    build/test-results/
    build/logs/

CI 可上传这些 artifacts。

## 11.7 Release gate

发布 package 必须依赖 mandatory validation PASS。

建议 commits：

    test: classify regression suites by execution environment
    ci: add fast portable test pipeline
    ci: add Windows SolidWorks integration pipeline
    ci: add release package validation pipeline
    test(release): enforce validated payload before installer creation

tag：

    swsimtool-ci-baseline

post-commit audit PASS 后进入 Phase 6。

---

# 12. Phase 6 — .NET 8 / Multi-target / CLI

现在才允许引入现代 runtime。

禁止直接全插件：

    net48 → net8

## 12.1 Compatibility audit

审计：

    System APIs
    third-party packages
    serialization
    reflection
    AppDomain
    Windows-only API
    filesystem assumptions
    process assumptions

## 12.2 Target framework strategy

根据实际审计结果选择，例如：

方案 A：

    Core/Application:
        netstandard2.0

    SolidWorks:
        net48

    CLI:
        net8.0

方案 B：

    reusable libraries:
        net48;net8.0

    SolidWorks:
        net48

    CLI:
        net8.0

不要为了“现代”强行选择某个 TFM。

目标：

    SolidWorks host remains reliable
    reusable core supports modern .NET

若选择本身涉及实质产品/维护权衡且当前代码事实无法给出明显最佳方案，可触发 Human Decision Gate。

## 12.3 Platform abstraction

Core 中禁止：

    C:\ hardcoding
    Registry
    Environment.SpecialFolder assumptions
    SolidWorks
    Windows-only process behavior

通过 Application/Infrastructure ports 隔离。

## 12.4 CLI

新增：

    src/SWSimTool.Cli/

target：

    net8.0

第一版只暴露真正通用能力。

可根据现有独立模型输入能力提供：

    swsimtool validate ...
    swsimtool inspect ...
    swsimtool export ...

不要为了 CLI 复制 SolidWorks CAD 逻辑。

依赖：

    SolidWorks ─┐
                ├→ Application → Core
    CLI ────────┘

禁止 CLI 复制 exporter/core semantics。

## 12.5 Cross-target equivalence

相同 canonical RobotModel：

    net48
    net8

比较：

    hierarchy
    transforms
    mass
    inertia
    joints
    site
    collision
    equality
    actuator
    sensor
    forces
    springs
    solver
    MJCF compile result

要求 semantic equivalence。

## 12.6 Tests

新增：

    net48 Core tests
    net8 Core tests
    cross-target parity
    CLI smoke
    CLI failure handling
    Unicode paths
    paths with spaces
    Windows paths
    Linux paths where applicable

建议 commits：

    build(core): introduce modern target framework support
    build(core): multi-target reusable SWSimTool libraries
    feat(cli): add SWSimTool command-line host
    test(targets): verify net48 and net8 semantic equivalence

tag：

    swsimtool-net8-cli

post-commit audit PASS 后进入 Phase 7。

---

# 13. Phase 7 — Cross-platform Core CI 与最终封板

## 目标

证明：

    Core
    Application
    MJCF
    CLI

已经真正脱离：

    SolidWorks
    COM
    Windows-only assumptions

## 13.1 CI matrix

Windows：

    net48
    net8
    Python/MuJoCo
    SolidWorks integration

Linux：

    net8 Core
    Application
    MJCF
    Python/MuJoCo
    parity
    CLI

macOS 当前不是必须目标。

## 13.2 Cross-platform semantic tests

同一 fixture：

    Windows net8
    Linux net8

输出必须 semantic equivalent。

重点：

    path separators
    case sensitivity
    newline
    UTF-8
    culture
    floating-point formatting
    temporary directories
    process invocation

## 13.3 Culture tests

至少：

    en-US
    zh-CN
    de-DE

MJCF 数字必须 invariant culture。

例如：

    1.23

不得输出：

    1,23

## 13.4 Determinism

相同 canonical input：

    Windows
    Linux

生成 semantically identical RobotModel/MJCF。

若 exporter 已定义 deterministic XML ordering，可进一步要求 canonical XML 一致。

## 13.5 Clean repository gate

fresh checkout：

    restore
    build
    test
    package

然后验证所有 generated artifacts 只位于：

    build/

源码目录保持干净。

## 13.6 Final release pipeline

最终：

    git checkout
        ↓
    Restore
        ↓
    Build Core
        ↓
    Build Application
        ↓
    Build Infrastructure
        ↓
    Fast Tests
        ↓
    MuJoCo / Parity / Dynamics
        ↓
    Build SolidWorks Host
        ↓
    SolidWorks Integration
        ↓
    SW2MuJoCo v2 Compatibility
        ↓
    Assemble
        ↓
    build/runtime-release/
        ↓
    Payload Validation
        ↓
    Installer
        ↓
    Installation Validation
        ↓
    build/dist/SWSimTool_x.y.z_SW2025_x64_Setup.exe

开发者最终主要入口：

    .\build.ps1 -Installer

建议 commits：

    ci: add Linux core and CLI validation
    test(platform): verify cross-platform model and MJCF equivalence
    test(culture): enforce invariant serialization
    test(workspace): enforce generated-artifact isolation
    docs: document final SWSimTool architecture and build workflow

最终 tag：

    swsimtool-modernization-complete

---

# 14. 阶段自动审计要求

每个 Phase 在 commit 前必须 self-audit。

commit 后必须针对新的 HEAD 再执行 post-commit audit。

如果 post-commit audit 发现明确工程问题：

    add corrective commit
        ↓
    rerun tests
        ↓
    repeat audit

不要为了保持“一个漂亮 commit”而隐藏问题或强行 rewrite history。

每阶段建议生成：

    build/reports/modernization/phase-N-*.md

最终生成：

    build/reports/modernization/final-audit.md

这些属于生成报告，不提交 Git，除非项目明确决定保存某份人工维护的正式审计文档。

报告至少记录：

    HEAD before
    HEAD after

    files added
    files modified
    files moved
    files deleted

    build result
    test counts/results
    compatibility result
    incremental result
    MuJoCo compile result
    SolidWorks integration result
    installer result
    workspace cleanliness
    legacy-name audit
    known issues
    skipped tests and reasons
    conclusion

结论只能是：

    PASS
    PARTIAL
    BLOCKED

只有 PASS 才能自动进入下一 Phase。

PARTIAL/BLOCKED 必须根据 Human Decision Gate 判断：

- 普通工程问题 → Agent 自行修复；
- 真正决策问题 → STOP 请求用户。

---

# 15. GUI / 人工视觉验收

不要假装自动化测试覆盖了视觉体验。

如果当前工具环境无法可靠完成 GUI 视觉验证，应明确记录：

    Automated SolidWorks integration: PASS/FAIL/SKIPPED
    Manual visual acceptance: NOT PERFORMED

以下项目可以累计到最终人工 smoke checklist，而不必阻塞与其无关的 Core 重构：

    [ ] SWSimTool menu 显示正确
    [ ] icon 正常
    [ ] configuration UI 布局正常
    [ ] 中文显示正常
    [ ] scrolling/layout 正常
    [ ] old CAD project UI 数据正常
    [ ] MuJoCo viewer 交互体验正常
    [ ] installer 页面/显示名称正常

但是如果自动化证据已经表明功能本身损坏，则不能以“等待人工 GUI 验收”为由继续。

---

# 16. Git / 提交纪律

每个 Phase：

    implementation
        ↓
    tests
        ↓
    self-audit
        ↓
    fixes
        ↓
    full required retest
        ↓
    commit(s)
        ↓
    post-commit audit
        ↓
    tag/checkpoint
        ↓
    next Phase

不要把整个现代化过程 squash 成一个巨大提交。

不要把无关全仓库格式化与功能修改混在一起。

文件移动尽量保持内容不变，以便 Git 识别 rename。

若可能：

    move commit
        ↓
    modification commit

保持历史可追踪。

不得擅自删除用户未提交的现有工作。

开始任务时首先检查：

    git status
    branch
    HEAD

若发现与本任务无关的用户未提交修改：

    不覆盖、不 reset、不删除。

如果它们会实质阻碍任务且无法安全隔离，触发 Human Decision Gate。

---

# 17. 测试优先级 / 阻塞条件

以下为 P0：

    CAD v2 无法读取
    stable ID 改变
    ordinary URDF export broken
    Native MJCF cannot compile
    validation failure overwrites valid output
    SolidWorks cannot load add-in
    installer cannot install/register
    timestep-only triggers CAD/STL rebuild
    production silently falls back to legacy Python model backend

任何 P0 新回归都必须阻止继续下一 Phase。

P1：

    cache invalidation regression
    parity regression
    Tool Backend cancellation/timeout regression
    dirty workspace
    installer payload missing dependencies

P2：

    docs mismatch
    non-critical warning increase
    optional CLI ergonomics

优先修复高等级问题。

---

# 18. 最终架构要求

最终依赖方向：

                         SWSimTool.SolidWorks
                                │
                                ▼
                       SWSimTool.Application
                                │
                                ▼
                          SWSimTool.Core

Infrastructure 通过清晰边界提供：

    filesystem
    Python Tool Backend
    MuJoCo validation
    cache
    mesh preparation
    serialization infrastructure
    MJCF export implementation

CLI：

    SWSimTool.Cli
          │
          ▼
    SWSimTool.Application
          │
          ▼
      SWSimTool.Core

SolidWorks 和 CLI 不得各自维护一套 domain/export semantics。

---

# 19. 禁止事项

禁止：

1. 无阶段边界地一次性重写整个项目；
2. 为追求“现代”直接全仓库切 net8；
3. 为 Clean Architecture 形式主义创建大量无实际价值的项目；
4. 把 Python Tool Backend 变成模型语义层；
5. Python silent fallback 接管 Native MJCF production path；
6. 为通过 parity 随意放宽 tolerance；
7. 产品更名破坏 `SW2MuJoCo Configuration v2`；
8. CLR namespace rename 破坏旧 DataContract XML；
9. 自动重新生成已有 stable ID；
10. 用 name 替代 stable ID 做当前数据绑定；
11. 删除 URDF 功能；
12. 删除有价值的 Python reference/parity tests；
13. 使用真实用户工程做破坏性测试；
14. installer 从源码目录到处收集文件；
15. 在 src/tests/runtime 等源码目录留下生成文件；
16. 把 build/cache/log/test output 提交 Git；
17. 把 rename、architecture、runtime upgrade、build migration 混成不可审计的大提交；
18. 某 Phase 质量门禁失败后仍继续下一 Phase；
19. 未审计当前源码就假设某功能存在或不存在；
20. 为目录“漂亮”改变已验证业务行为；
21. 隐瞒 skipped/failed tests；
22. 声称执行了实际上无法执行的 SolidWorks GUI/环境验证；
23. 擅自 reset/revert/delete 用户已有未提交工作。

---

# 20. 最终成功标准

完成后必须满足：

## A. 产品身份

    SWSimTool

## B. 独立产品 identity

    new COM CLSID
    new Installer AppId
    new install directory
    new LocalAppData

## C. CAD compatibility

旧工程：

    SW2MuJoCo Configuration v2
        ↓
    SWSimTool
        ↓
    seamless load

无需重新配置。

## D. Core architecture

    Core independent of SolidWorks
    Application independent of SolidWorks
    SolidWorks is an adapter/host

## E. Python

    tool backend only
    no production model-semantics duplication

## F. Build

所有生成物：

    build/

## G. Release

Installer only consumes:

    build/runtime-release/

## H. Modern .NET

Reusable core supports modern .NET。

SolidWorks host 可以保留可靠集成所需 framework。

## I. CLI

CLI shares Application/Core。

无重复模型/export semantics。

## J. Cross-platform

Core/MJCF/CLI 在依赖允许范围内可于 Windows/Linux 自动测试。

## K. Regression

保持：

    URDF
    Native MJCF
    stable IDs
    incremental export
    Python tooling
    MuJoCo validation
    parity
    dynamics
    SolidWorks integration

---

# 21. 最终人工验收边界

Agent 应尽量自动完成所有可机器验证内容。

用户最终只需要主要进行一次人工 smoke/visual acceptance，例如：

    install final SWSimTool
        ↓
    open a COPY of a real old project
        ↓
    inspect configuration UI
        ↓
    export URDF
        ↓
    export MJCF
        ↓
    open MuJoCo viewer
        ↓
    inspect UI/menu/icon/layout
        ↓
    uninstall/reinstall smoke test

这些人工项目应在 final audit 中明确列出，而不是在每个 Phase 都要求用户确认。

---

# 22. 开始执行时

首先：

1. 检查 `git status`；
2. 检查 current branch / HEAD；
3. 审计当前目录结构；
4. 审计 solution / csproj；
5. 搜索：
       SW2URDF
       SW2MuJoCo
       sw2mujoco
       URDFExporter
6. 检查 build/test/install scripts；
7. 检查 `SimulationStorage` / persistence schema；
8. 检查 DataContract serialization；
9. 检查 stable ID implementation；
10. 检查 regression tests；
11. 判断当前源码实际上已经完成到哪个 Phase；
12. 生成初始审计摘要；
13. 从“尚未完成的最早 Phase”开始；
14. 自动按阶段质量门禁推进；
15. 只有触发 Human Decision Gate 时才 STOP 请求用户。

如果当前 Agent 已经在执行 rename：

    audit current Phase 1 state
        ↓
    finish missing Phase 1 work
        ↓
    full Phase 1 validation
        ↓
    commit
        ↓
    post-commit audit
        ↓
    tag if PASS
        ↓
    automatically continue Phase 2

---

# 23. 核心工程原则

整个迁移始终遵守：

    behavior before structure
    compatibility before cleanup
    explicit semantics before convenience
    stable identity before editable names
    validated package before publish
    reusable core before new hosts
    clean build boundaries before framework upgrades
    automated quality gates before human interruption
    incremental migration before big-bang rewrite

最终目标不是“看起来现代”。

最终目标是：

    一个行为稳定、
    数据兼容边界明确、
    SolidWorks 宿主足够薄、
    核心可独立测试和复用、
    构建可重复、
    工作目录干净、
    发布流程可靠、
    Agent 可以通过自动质量门禁持续验证、
    并可逐步运行于现代 .NET 和跨平台环境的 SWSimTool。
