# 参与贡献

感谢你愿意改进 LocalTodo。

## 开发环境

- Windows 10/11 x64
- .NET 10 SDK
- Visual Studio（可选，需安装 .NET 桌面开发工作负载）

## 建议流程

1. Fork 本仓库并从 `main` 创建功能分支。
2. 保持修改范围清晰，不提交 `Data`、`Backups`、`Logs`、`bin` 或 `obj`。
3. 对功能修改补充或更新自动化测试。
4. 提交前执行：

   ```powershell
   dotnet restore LocalTodo.slnx
   dotnet build LocalTodo.slnx --configuration Release --no-restore
   dotnet test tests/LocalTodo.Tests/LocalTodo.Tests.csproj --configuration Release --no-build
   ```

5. 在 Pull Request 中说明问题、解决方式、验证结果以及任何数据库兼容影响。

## 设计原则

- 保持本地离线和便携数据目录，不引入隐式云依赖。
- 不改变未在需求范围内的既有功能。
- 数据库迁移必须向前兼容，并在破坏性修改前建立安全备份。
- 界面保持简洁、清晰并支持 Windows DPI 缩放。
- 对完成、删除、循环任务和多窗口同步等关键路径优先增加回归测试。

## 许可证

提交贡献即表示你同意按照本项目的 Apache License 2.0 对贡献内容进行许可。
