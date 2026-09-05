# LocalTodo

[![Build](https://github.com/HaiboQiao/LocalTodo/actions/workflows/build.yml/badge.svg)](https://github.com/HaiboQiao/LocalTodo/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4.svg)](#运行环境)

LocalTodo 是一款面向 Windows 的本地离线任务管理应用。它不需要账号或云服务，任务、每周计划、成长记录、备份和日志都保存在应用所在文件夹中，便于携带、备份和迁移。

## 当前版本

- 版本：3.0.0
- 发布日期：2026-09-05
- 数据库结构：v13
- 支持平台：Windows 10/11 x64
- 运行方式：.NET 10 自包含发布，无需另行安装 .NET
- 发布结构：1 个压缩 EXE + 6 个必须独立加载的原生 DLL

## 下载与运行

1. 打开 [GitHub Releases](https://github.com/HaiboQiao/LocalTodo/releases/latest)。
2. 在 **Assets** 中下载 `LocalTodo-3.0.0-win-x64-portable.zip`。
3. 完整解压 ZIP，保留目录中的全部 7 个程序文件。
4. 双击 `LocalTodo.exe`。

也可[直接下载 3.0.0 便携版](https://github.com/HaiboQiao/LocalTodo/releases/download/v3.0.0/LocalTodo-3.0.0-win-x64-portable.zip)。Release 页面末尾由 GitHub 自动提供的 `Source code (zip)` 和 `Source code (tar.gz)` 是源码包，不是可直接运行的软件。

程序目前没有数字签名。Windows 首次运行时可能显示安全提示，请确认文件来自本仓库，并使用根目录的 [`SHA256SUMS.txt`](SHA256SUMS.txt) 校验文件完整性。

## 主要功能

### 任务管理

- 新建、查看、编辑、完成、恢复和删除任务
- 任务标题、说明、截止日期、截止时间、提醒和重点标记
- 持续日期任务：从当前日期至截止日期持续显示在“今天”区域
- 每天、每周、每月、每年和工作日循环任务
- 所有任务、日历、四象限、已完成和垃圾箱视图
- 阳历、农历日期显示和指定日期快速新增
- 主窗口、桌面任务列表和桌面四象限之间的数据同步
- 自动保存、字段级安全合并和多窗口编辑保护
- 系统托盘、开机自启动和单实例运行

### 每周计划

- 七列时间轴安排周一至周日的不同时段
- 拖动计划主体调整星期与时间，拖动上下边缘调整起止时间
- 按住 `Ctrl` 拖动可实时预览并复制计划
- 选中计划后按 `Delete` 删除，点击空白区域取消选中
- 自定义计划颜色，自动阻止同一天中的时间冲突

### 成长记录

- 记录已取得的成果、描述以及开始和完成日期
- 跨年度时间轴浏览和自动滚动
- 拖动成果移动时间段，拖动左右边缘调整起止日期
- 自定义成果分类、配色及显示顺序
- 打开成果详情后直接编辑，点击应用外部自动关闭

### 数据安全与桌面体验

- SQLite 本地数据库、自动备份、完整性检查和日志保留
- 旧数据库自动迁移，迁移前建立安全备份
- 防止重复启动多个实例同时写入同一数据库
- 桌面部件遵循普通窗口层级，不强制置顶或抢占焦点
- 主程序、桌面部件间的任务变更即时同步

## 3.0.0 重点更新

- 新增持续日期任务，并将数据库结构升级至 v13
- 统一所有任务与成果日期控件：整框打开日历、日期只读、支持独立清除
- 修复桌面部件遮挡普通程序、关闭主窗口后部件跳到最前方的问题
- 增加单实例协调，重复启动时激活已有主窗口
- 加强完成、删除和多窗口编辑时的并发一致性
- 完善成长记录分类默认值、分类排序、时间轴拖动和滚动体验
- 完善每周计划的移动、复制、键盘删除和取消选中交互

完整版本记录见 [`CHANGELOG.md`](CHANGELOG.md)。

## 为什么仍有 6 个 DLL

托管程序集、第三方托管依赖和应用资源已经合并并压缩到 `LocalTodo.exe`。剩余 6 个 DLL 是 WPF 图形、输入和 SQLite 必须以原生文件形式直接加载的组件。

如果将它们继续嵌入 EXE，运行时仍要解压到 Windows 临时目录，既不能真正减少运行所需文件，也不符合“全部运行文件留在应用文件夹中”的便携目标。因此，1 个 EXE + 6 个原生 DLL 是当前兼顾目录整洁、稳定性和本地便携性的最小可靠结构。

## 数据位置

首次运行后，程序会在自身目录按需创建数据目录：

```text
LocalTodo-3.0.0-win-x64-portable/
├─ LocalTodo.exe
├─ D3DCompiler_47_cor3.dll
├─ e_sqlite3.dll
├─ PenImc_cor3.dll
├─ PresentationNative_cor3.dll
├─ vcruntime140_cor3.dll
├─ wpfgfx_cor3.dll
├─ Data/
│  └─ localtodo.db
├─ Backups/
└─ Logs/
```

`Data`、`Backups` 和 `Logs` 不纳入版本控制。迁移程序时应复制整个应用文件夹，避免遗漏数据库或备份。

## 从旧版本更新

1. 从系统托盘完全退出 LocalTodo。
2. 备份旧应用目录中的 `Data` 和 `Backups`。
3. 用 3.0.0 的 7 个程序文件替换旧程序文件，保留原有数据目录。
4. 启动新版本并确认任务、每周计划和成长记录正常。

3.0.0 支持将旧版数据库按既有迁移链升级到 v13。升级会先执行完整性检查并创建备份；升级后的 v13 数据库不应再交给只支持旧结构的程序使用。

## 从源码构建

### 运行环境

- Windows 10/11 x64
- .NET 10 SDK
- Visual Studio（可选，需包含 .NET 桌面开发工作负载）

### 构建与测试

```powershell
dotnet restore LocalTodo.slnx
dotnet build LocalTodo.slnx --configuration Release --no-restore
dotnet test tests/LocalTodo.Tests/LocalTodo.Tests.csproj --configuration Release --no-build
```

### 生成便携版

```powershell
dotnet publish src/LocalTodo/LocalTodo.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  /p:PublishProfile=FolderProfile
```

## 项目结构

```text
src/LocalTodo/          WPF 应用、视图模型、服务与数据访问代码
tests/LocalTodo.Tests/  自动化回归测试
.github/workflows/      GitHub Actions 持续集成配置
docs/releases/          GitHub Release 版本说明
```

## 隐私

任务管理、提醒和数据库操作均在本机完成。LocalTodo 不包含账号系统、云同步、遥测或远程数据上传功能。

## 参与贡献与安全问题

- 参与开发前请阅读 [`CONTRIBUTING.md`](CONTRIBUTING.md)。
- 安全问题请按 [`SECURITY.md`](SECURITY.md) 中的方式报告。
- 第三方组件与许可证见 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)。

## 许可证

LocalTodo 使用 [Apache License 2.0](LICENSE) 开源，版权声明见 [`NOTICE`](NOTICE)。
