# LocalTodo

LocalTodo 是一款面向 Windows 的本地离线待办应用。它专注于任务管理，不依赖云服务，不需要登录账号，任务数据库、备份和日志都保存在应用所在文件夹内。

## 当前版本

- 版本：2.0.0
- 平台：Windows x64
- 数据库结构：v8
- 发布方式：.NET 10.0.10 自包含、压缩单文件发布
- 发布日期：2026-08-22

## 下载与运行

1. 下载或克隆本仓库。
2. 保留 `LocalTodo-2.0.0-win-x64-portable` 文件夹中的全部 7 个文件。
3. 双击 [LocalTodo.exe](./LocalTodo-2.0.0-win-x64-portable/LocalTodo.exe) 启动。

本版本已经包含所需的 .NET 运行时，不需要另外安装 .NET。发布文件没有数字签名，Windows 第一次运行时可能显示安全提示；请确认文件来自本仓库并核对下方 SHA-256。

## 主要功能

- 新建、查看、编辑、完成、恢复和删除任务
- 标题、说明、截止日期、具体时间、重点星标和四象限分类
- 到点提醒以及 5/15/30 分钟、1 小时、4 小时、1 天提前提醒
- 每天、每周、每月、每年和工作日循环
- 所有任务、已完成、垃圾箱、日历和四象限视图
- 日历中的阳历、农历显示和指定日期快速新增
- 主窗口、桌面任务列表和桌面四象限之间的数据同步
- 桌面窗口位置、尺寸和启用状态保存
- 系统托盘、关闭主窗口后驻留和开机自启动
- 多窗口编辑的并发保护、字段级安全合并和自动保存
- 手动备份、数据库完整性检查和延迟恢复

## 为什么仍有 6 个 DLL

应用的托管程序集、依赖和资源已经合并、压缩进 `LocalTodo.exe`。目录中剩余的 6 个 DLL 是 WPF 图形、输入组件和 SQLite 数据库需要直接加载的原生库。

把这些原生库继续塞入 EXE 会导致程序启动时将它们解压到用户临时目录，产生程序文件夹之外的运行文件。因此当前 7 文件结构是同时满足“目录尽量精简”和“真正便携、不向 AppData 或临时目录散落程序文件”的安全方案。

## 数据位置

第一次启动后，程序会在自身文件夹中创建：

```text
LocalTodo-2.0.0-win-x64-portable/
├─ LocalTodo.exe
├─ 6 个必需的原生 DLL
├─ Data/
│  └─ localtodo.db
├─ Backups/
└─ Logs/
```

迁移应用时请复制整个 `LocalTodo-2.0.0-win-x64-portable` 文件夹。开机自启动只使用当前 Windows 用户的 Run 注册项，不会把程序文件复制到系统目录。

## 更新与备份

更新前请完全退出 LocalTodo，并备份整个程序文件夹。替换程序文件时务必保留 `Data`、`Backups` 和 `Logs`。数据库升级会在迁移前自动创建并验证备份。

程序内“数据维护”页面支持：

- 创建并验证数据库备份
- 执行 SQLite 完整性检查
- 验证恢复文件，并在下一次完整启动时安全恢复

## 文件校验

`LocalTodo.exe` SHA-256：

```text
3DB676B3270A378E6A177255450354CB1CF9AA50FE55AAD9D4432FD19D2DAD8D
```

发布目录共 7 个文件，合计 82,592,476 字节。完整校验值：

```text
A05F99734F7C4822FEFC12B367AF21FD0976ED6608752FB1E1E80B6ECE7ECBBB  D3DCompiler_47_cor3.dll
B7385D722C83FB52142A00477A726723745916D22A555711EE89834C1111FB2E  e_sqlite3.dll
3DB676B3270A378E6A177255450354CB1CF9AA50FE55AAD9D4432FD19D2DAD8D  LocalTodo.exe
E6A8BDB6A49F6BD8B3A5CC6B4DBC460588C0E58B08A613E2C2A32FA5875592DD  PenImc_cor3.dll
7382F135C559FBA317F73FBEAF9B84A4F34E760207FD6F31225698B260E26623  PresentationNative_cor3.dll
D5E4D9A3E835FA679450145D6A7D94E36573A509317111904D9B3712C30D9066  vcruntime140_cor3.dll
C6BE4D13F18A371C34E2C721FC077595A903B1B0D8DEF4C51DBE44EC7ECEC2B7  wpfgfx_cor3.dll
```

## 隐私

LocalTodo 的任务管理和数据库操作均在本地完成，不包含云同步或账号系统。提醒功能由本机定时检查完成。

## 许可证

本项目使用 [Apache License 2.0](./LICENSE)。
