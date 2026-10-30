# CleanC

CleanC 是一个使用 .NET 10 和 WPF 编写的 Windows 磁盘分析与清理助手。它读取 WizTree 导出的 CSV 文件，快速重建 C 盘容量构成、目录树、最大文件和分级清理建议，不再重复进行耗时的全盘文件系统扫描。

> 当前版本为 `v0.6.0`。分析过程只读取 CSV；真正清理时会再次检查文件是否存在、大小是否改变、是否属于受保护路径，并永久删除用户确认的文件。

## 使用方法

1. 在 WizTree 中扫描 C 盘。
2. 使用 **File → Export to CSV File** 导出标准 CSV（保留 `File Name`、`Size`、`Modified`、`Attributes` 列）。
3. 在 CleanC 中点击“选择 CSV”，然后点击“导入并分析”。
4. 检查“清理建议”：文件会按来源或目录成组显示；只默认勾选最安全的临时文件和可重建缓存，其余项目由用户选择。
5. 核对永久删除警告后执行清理。

## 已实现功能

- 后台解析 WizTree CSV，不阻塞界面，可随时取消
- 兼容带引号、逗号文件名及中英文常见列名
- 从文件记录重新聚合目录容量，避免 CSV 目录行重复计数
- 最大 2,000 个文件排行
- 最大目录排行和占用百分比
- 清理建议分为“建议清理”和“需要确认”
- 默认建议：超过 3 天的用户临时文件、超过 7 天的 Windows 临时文件、旧应用崩溃报告、浏览器/应用缓存、缩略图缓存、旧诊断日志、着色器缓存和旧 Codex 安装暂存目录
- 手动确认：Chrome 离线模型、NVIDIA/Logitech 旧更新包、系统崩溃转储、开发工具下载缓存、个人文档、聊天记录、旧下载、安装包、个人媒体和超大文件
- 默认只勾选用户/Windows 临时文件、着色器缓存、浏览器/应用缓存和缩略图缓存；其他类别均不默认勾选
- 清理分类按“可重建缓存 → 安装包/更新缓存 → 个人内容”排序，优先展示对正常使用影响较小的项目
- 清理项按应用来源或缓存根目录分组选择，不需要逐个勾选文件
- 每次清理后，本次已提交的项目和空分类会从当前列表移除；重新导入 CSV 才会再次显示未成功删除的文件
- 删除前最终确认，所选文件直接永久删除，不经过 Windows 回收站
- 系统关键路径硬保护，跳过符号链接和重解析点
- 清理前检测文件占用；被占用或权限不足的文件静默跳过，并在结束时一次性汇总，不再逐个弹出系统错误窗口

## 安全边界

CleanC 不会自动删除任何文件。只有用户勾选并通过最终确认后才会执行清理。以下路径及其子目录会被保护：

- `Windows\System32`、`Windows\WinSxS`、`Windows\Installer`
- `Program Files`、`Program Files (x86)`、绝大多数 `ProgramData`
- `System Volume Information`、`Recovery`、`$Recycle.Bin`
- `pagefile.sys`、`hiberfil.sys`、`swapfile.sys` 等系统文件
- 任意 `skills` 目录，以及 `.agents\skills`、`.codex\skills`、`.codex\plugins` 中的 Skill/插件功能文件

清理操作只永久删除经过复核的普通文件，不会递归删除目录。永久删除无法从回收站恢复，请先核对分组和路径。

`ProgramData` 中只有以下明确识别的旧更新缓存可以进入“需要确认”列表：

- NVIDIA App 的 `UpdateFramework\ota-artifacts`（仅识别至少 90 天未修改的文件）
- Logitech G HUB 的 `cache`（仅识别至少 30 天未修改的文件）

Logitech G HUB 的 `depots` 可能包含当前或可回退的程序版本，始终按受保护路径处理，不会进入清理列表。

## 清理规则原则

| 分类 | 条件 | 默认选择 |
| --- | --- | --- |
| 用户临时文件 | 位于用户 `AppData\Local\Temp` 且超过 3 天 | 是 |
| Windows 临时文件 | 位于 `Windows\Temp` 且超过 7 天 | 是 |
| 着色器缓存 | DirectX、NVIDIA、AMD、Intel 或应用 ShaderCache，超过 3 天 | 是 |
| 浏览器缓存 | 主流浏览器 Cache、Code Cache、GPUCache 等，超过 7 天 | 是 |
| 应用缓存 | 用户 AppData 中明确命名的缓存目录，超过 14 天 | 是 |
| 残留安装暂存 | 旧 `codex-runtime-install-*` 目录，超过 14 天 | 否 |
| 离线模型/更新包 | Chrome 本地模型；NVIDIA 更新包超过 90 天；LGHUB `cache` 超过 30 天 | 否 |
| 系统转储 | Windows LiveKernelReports 或 MEMORY.DMP，超过 30 天 | 否 |
| 开发缓存 | pip、npm、NuGet、Gradle、Cargo 等，超过 30 天 | 否 |
| 个人内容 | 仅识别 Documents、Desktop、Downloads、OneDrive 等个人资料库 | 否 |

规则不会把 `.cache`、`.codex` 等工具内部的 Markdown、CSV 文件误列为个人文档。
目录名中的 `cache` 不会覆盖功能保护：Skill 和插件目录中的 README、`SKILL.md`、脚本、规则及资源文件均不会进入清理列表，也会在永久删除前再次拦截。

## 开发环境

- Windows 10/11 x64
- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0)

```powershell
dotnet restore CleanC.slnx --ignore-failed-sources
dotnet build CleanC.slnx --configuration Release --no-restore
dotnet run --project tests\CleanC.Core.Tests\CleanC.Core.Tests.csproj --configuration Release
dotnet run --project tests\CleanC.App.SmokeTests\CleanC.App.SmokeTests.csproj --configuration Release
dotnet run --project src\CleanC.App\CleanC.App.csproj --configuration Release
```

## 生成发布文件

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

输出目录为 `artifacts\CleanC-win-x64`。当前发布包依赖目标电脑安装 .NET 10 Desktop Runtime。后续版本会提供自包含安装包。

## 项目结构

```text
src/CleanC.Core       WizTree CSV 导入、规则、模型和路径安全逻辑
src/CleanC.App        WPF 界面和永久删除清理服务
tests/CleanC.Core.Tests  无第三方依赖的核心测试程序
tests/CleanC.App.SmokeTests  WPF 资源、XAML 与布局冒烟测试
scripts               构建和发布脚本
```

## 后续计划

- 矩形树图（Treemap）
- 扩展名统计和筛选
- CSV 结果对比
- 可编辑的规则配置与规则预览
- 代码签名和自包含安装包

## 免责声明

磁盘清理具有数据丢失风险。请在删除前核对路径，并为重要数据建立备份。项目按 MIT License 提供，不对误删或数据损失承担责任。
