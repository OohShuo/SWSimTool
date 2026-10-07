# 测试目录

| 目录 | 内容 |
|---|---|
| core | 坐标计算、持久化、URDF 序列化 |
| identity | stable ID、名称生命周期、CAD configuration 身份 |
| model | 已解析模型和仿真配置构建 |
| ui | 导航、表单关闭和配置生命周期 |
| architecture | 生产/参考边界与发布载荷检查 |
| backend | Python 后端单元和故障回归 |
| tools | Python 工具进程、协议、取消/超时及偏好存储 |
| parity | MuJoCo 编译语义和动力学差分 |
| reference | 测试专用 Python 参考生成器及 CAD 差分 |
| incremental | 增量验收与 warm/cold 比较 |
| performance | 导出性能基准 |
| integration | CAD 和安装集成测试 |
| support/cad | 隔离 CAD fixture 的创建、加载、关闭 |
| manual | 手动界面检查和指南演示辅助 |
| upstream | 从官方源码拆出的独立 xUnit 项目与 runner |

先构建生产插件到 `build/runtime-release/`。PowerShell 回归默认读取这个载荷；可使用 `-Payload` 显式选择。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/identity/Test-StableReferenceAudit.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/architecture/Test-ProductionBoundary.ps1
python -m unittest discover -s tests/backend -p "test_*.py"
python tests/parity/run.py
python tests/architecture/Test-ReleaseLayout.py
```

`tests/core/CoreTests.csproj`、`tests/parity/CandidateRunner.csproj` 和 `tests/upstream/SW2URDF.Tests.csproj` 是独立测试构建入口。测试依赖不会进入生产 DLL 或安装包。

CAD、官方及安装集成测试不会随上述纯回归自动运行。需要 SolidWorks 的测试仅可在独立空会话和明确指定的隔离 fixture 上执行，不得使用用户已有工程；官方 runner 会在会话已有文档时拒绝运行。
