# LocalTodo

LocalTodo 是一款面向 Windows 的本地离线待办应用。它不依赖云服务，也不需要登录账号；任务数据库、备份和日志均保存在应用所在文件夹中，便于携带、备份和迁移。

## 当前版本

- 版本：2.0.0
- 发布日期：2026-08-28
- 支持平台：Windows 10/11 x64
- 运行方式：.NET 10 自包含发布，无需另行安装 .NET
- 发布结构：压缩单文件主体 + 6 个必要原生运行库

## 下载与运行

1. 下载或克隆本仓库。
2. 打开 `LocalTodo-2.0.0-win-x64-portable` 文件夹。
3. 保留文件夹中的全部 7 个程序文件。
4. 双击 `LocalTodo.exe` 启动。

程序目前没有数字签名。Windows 第一次运行时可能显示安全提示，请确认文件来自本仓库，并可使用本文后面的 SHA-256 校验值验证文件完整性。

## 主要功能

- 新建、查看、编辑、完成、恢复和删除任务
- 任务标题、说明、截止日期、截止时间和重点标记
- 四象限分类和独立四象限视图
- 所有任务、日历、已完成和垃圾箱视图
- 到期提醒及多种提前提醒时间
- 每天、每周、每月、每年和工作日循环任务
- 阳历、农历日期显示和指定日期快速新增
- 主窗口、桌面任务列表和桌面四象限之间的数据同步
- 自动保存、多窗口编辑保护和字段级安全合并
- 系统托盘、关闭主窗口后驻留和开机自启动
- 桌面窗口位置、尺寸和启用状态保存
- 本地数据库、自动备份和安全恢复机制

## 发布目录为什么仍有 6 个 DLL

LocalTodo 的托管程序集、第三方托管依赖和应用资源已经合并并压缩到 `LocalTodo.exe` 中。剩余 6 个 DLL 是 WPF 图形、输入和 SQLite 数据库必须直接加载的原生库。

如果继续把这些原生库强行放入 EXE，程序运行时仍需将其解压到 Windows 临时目录，从而在应用文件夹之外产生程序文件。当前的 1 个 EXE + 6 个 DLL 是兼顾以下要求后的最精简方案：

- 不要求用户安装 .NET
- 所有程序文件集中在同一文件夹
- 不向 AppData 或临时目录长期分散程序文件
- 保持 WPF 和 SQLite 稳定运行

## 数据位置

首次运行后，LocalTodo 会按需在程序目录中创建运行数据文件夹：

```text
LocalTodo-2.0.0-win-x64-portable/
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

`Data`、`Backups` 和 `Logs` 不会上传到 GitHub。迁移程序时，请复制整个应用文件夹，确保任务数据与程序一起迁移。

## 更新建议

更新前请：

1. 完全退出 LocalTodo，包括系统托盘中的进程。
2. 备份原应用目录中的 `Data` 和 `Backups`。
3. 替换 7 个程序文件，但保留原有运行数据目录。
4. 启动新版本并确认任务数据正常。

## 文件校验

发布目录共 7 个程序文件，合计 82,593,666 字节。

```text
A05F99734F7C4822FEFC12B367AF21FD0976ED6608752FB1E1E80B6ECE7ECBBB  D3DCompiler_47_cor3.dll
B7385D722C83FB52142A00477A726723745916D22A555711EE89834C1111FB2E  e_sqlite3.dll
A937EF60DF9702937B0B93BC607E1BCEB8F94A0B3968D7F0A62DFEBB6F26175F  LocalTodo.exe
E6A8BDB6A49F6BD8B3A5CC6B4DBC460588C0E58B08A613E2C2A32FA5875592DD  PenImc_cor3.dll
7382F135C559FBA317F73FBEAF9B84A4F34E760207FD6F31225698B260E26623  PresentationNative_cor3.dll
D5E4D9A3E835FA679450145D6A7D94E36573A509317111904D9B3712C30D9066  vcruntime140_cor3.dll
C6BE4D13F18A371C34E2C721FC077595A903B1B0D8DEF4C51DBE44EC7ECEC2B7  wpfgfx_cor3.dll
```

PowerShell 校验示例：

```powershell
Get-FileHash .\LocalTodo-2.0.0-win-x64-portable\LocalTodo.exe -Algorithm SHA256
```

## 隐私

LocalTodo 的任务管理、提醒和数据库操作均在本机完成，不包含账号系统、云同步或远程数据上传功能。

## 许可证

本项目使用 [Apache License 2.0](./LICENSE)。版权声明参见 [NOTICE](./NOTICE)。
