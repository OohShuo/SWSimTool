# 官方源码与本地修改

## 官方基线

- 项目：https://github.com/ros/solidworks_urdf_exporter
- 官方提交：`882169e28952f0d17c87d7eab98826454421aabf`
- 本地基线标签：`upstream-baseline-882169e`
- 本地导入提交：`4b49e0a`

仓库原来没有提交历史。本次从先前保存的官方源码归档导入 170 个文件作为根提交；这是本地快照提交，不是官方原始 Git 提交，也不包含官方历史。Git 按当前配置处理文本行尾。官方示例和归档内已有的文件保留在基线中。

## 本地修改

基线之后的提交属于本地工作：SolidWorks 2025 / .NET Framework 4.8 构建适配、零件导出窗体生命周期日志、显式注册脚本、VS2022 解决方案元数据、安装器及中文文档。

注意：官方基线已经删除旧 1.6.1 中 Show 后立即 Dispose 的错误调用。不要将这项上游修复误记为我们独立新增的代码；我们的版本基于已包含该修复的官方快照构建并验证。

```powershell
# 查看本地提交
git log --oneline upstream-baseline-882169e..HEAD

# 查看相对官方快照的全部本地修改
git diff upstream-baseline-882169e HEAD

# 只看文件清单
git diff --stat upstream-baseline-882169e HEAD
```

`dist/` 安装包、`diagnostics/` 本机诊断与测试输出、`INSTALL/tools/` 下载的编译器、`upstream-reference/` 重复快照以及编译缓存均不加入本地源码提交，文件仍保留在磁盘。官方基线已有的 examples 示例仍受版本控制。

此次只进行本地提交，未推送远程。安装包构建入口为 `INSTALL/SW2025.iss`，使用说明见 `SW2URDF_使用指南.md`，修复及验证记录见 `SW2025_SW2URDF_FIX.md`。
