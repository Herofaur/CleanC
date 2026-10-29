# CleanC

CleanC 是一个使用 .NET 10 和 WPF 编写的 Windows 磁盘分析与清理助手。它借鉴 WizTree 的信息组织方式，扫描指定磁盘或目录后展示容量构成、目录树、最大文件和分级清理建议。

> 当前版本为 `v0.1`。扫描器使用普通文件系统 API，优先保证安全和兼容性，速度还没有达到直接读取 NTFS MFT 的 WizTree 水平。

## 已实现功能

- 后台扫描，不阻塞界面，可随时取消
- 目录容量递归聚合和目录树展示
- 最大 2,000 个文件排行
- 最大目录排行和占用百分比
- 清理建议分为“建议清理”和“需要确认”
- 识别用户/Windows 临时文件、崩溃转储、浏览器缓存、旧下载、旧安装包和超大文件
- 建议项默认勾选，个人文件类项目默认不勾选
- 删除前最终确认，所有文件进入 Windows 回收站
- 系统关键路径硬保护，跳过符号链接和重解析点
- 对权限不足、文件占用和扫描错误做容错处理

## 安全边界

CleanC 不会自动删除任何文件。只有用户勾选并通过最终确认后才会执行清理。以下路径及其子目录会被保护：

- `Windows\System32`、`Windows\WinSxS`、`Windows\Installer`
- `Program Files`、`Program Files (x86)`、`ProgramData`
- `System Volume Information`、`Recovery`、`$Recycle.Bin`
- `pagefile.sys`、`hiberfil.sys`、`swapfile.sys` 等系统文件

清理操作只处理普通文件并发送到回收站，不会递归删除目录。

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
src/CleanC.Core       扫描、规则、模型和路径安全逻辑
src/CleanC.App        WPF 界面和回收站清理服务
tests/CleanC.Core.Tests  无第三方依赖的核心测试程序
tests/CleanC.App.SmokeTests  WPF 资源、XAML 与布局冒烟测试
scripts               构建和发布脚本
```

## 后续计划

- NTFS MFT 高速扫描模式
- 矩形树图（Treemap）
- 扩展名统计和筛选
- 扫描结果缓存与对比
- Windows 服务日志、更新缓存等更多规则
- 代码签名和自包含安装包

## 免责声明

磁盘清理具有数据丢失风险。请在删除前核对路径，并为重要数据建立备份。项目按 MIT License 提供，不对误删或数据损失承担责任。
