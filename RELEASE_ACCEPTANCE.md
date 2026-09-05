# LocalTodo 3.0.0 发布验收记录

- 验收日期：2026-09-05
- 目标平台：Windows 10/11 x64
- 运行方式：.NET 10 自包含、压缩单文件主体
- 数据库结构：v13

## 构建与测试

- 自动化测试：21 项全部通过
- Release 构建：通过
- 编译警告：0
- 编译错误：0
- EXE 文件版本：3.0.0.0
- EXE 产品版本：3.0.0

## 发布结构

发布目录为 `LocalTodo-3.0.0-win-x64-portable`，只包含以下 7 个必要程序文件：

- `LocalTodo.exe`
- `D3DCompiler_47_cor3.dll`
- `e_sqlite3.dll`
- `PenImc_cor3.dll`
- `PresentationNative_cor3.dll`
- `vcruntime140_cor3.dll`
- `wpfgfx_cor3.dll`

未包含 PDB、JSON 配置、语言资源目录、开发数据库、日志或备份文件。

## 发布文件核对

- 文件数量：7
- 子目录数量：0
- 总大小：82,696,682 字节
- SHA-256：已记录于 `SHA256SUMS.txt`
- EXE 数字签名：未签名，README 已提供明确提示

## 运行验收

- 在独立临时目录启动 3.0.0 便携版：通过
- `--startup` 后台启动模式：通过
- 全新 `Data/localtodo.db` 初始化：通过
- `Logs` 日志生成：通过
- 启动日志确认“LocalTodo 启动完成”：通过
- 验收进程与临时运行数据：已清理

## 数据兼容性

- 当前数据库迁移链目标版本：v13
- v12 → v13 只增加 `is_continuous` 字段，旧任务默认关闭持续日期
- 既有任务日期、提醒、循环、完成状态和成长记录不在该迁移中修改
- 数据库升级前会先执行完整性检查并建立安全备份
- v13 数据库不应再交给仅支持旧结构的程序使用

## 最终交付检查

- 指定发布目录：`D:\Works\LocalTodo-Release`
- GitHub 仓库：<https://github.com/HaiboQiao/LocalTodo>
- 默认分支：`main`
- 版本标签：`v3.0.0`
- 发布目录内容与 `SHA256SUMS.txt`：一致
- 本地发布提交与远端 `main`：上传后使用远端引用复核
