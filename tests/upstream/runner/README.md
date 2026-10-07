# 官方测试运行器

构建 `tests/upstream/SWSimTool.Tests.csproj` 和此目录的 `TestRunner.csproj`，指定本地 `SolidWorksDir`。产物位于 `build/upstream-tests/`，runner 在其 `runner/` 子目录；默认发现上一级 `SWSimTool.Tests.dll`。

运行 `TestRunner.exe TestVersioning` 可按名称筛选。这些测试使用 SolidWorks，仅在隔离空会话与官方示例上手动运行。会话已有文档时拒绝启动。生产插件不包含这些测试或 xUnit/Moq 依赖。
