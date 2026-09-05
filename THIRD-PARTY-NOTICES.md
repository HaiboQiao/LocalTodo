# 第三方组件说明

LocalTodo 基于 .NET 和 WPF，并通过 NuGet 使用以下直接依赖。具体权利和义务以各组件随附许可证为准。

| 组件 | 使用版本 | 许可证 | 项目地址 |
| --- | ---: | --- | --- |
| .NET / WPF | 10 | MIT | <https://github.com/dotnet/wpf> |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | <https://github.com/CommunityToolkit/dotnet> |
| Microsoft.Data.Sqlite | 10.0.10 | MIT | <https://github.com/dotnet/efcore> |
| Microsoft.Extensions.DependencyInjection | 10.0.10 | MIT | <https://github.com/dotnet/runtime> |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | Apache-2.0 | <https://github.com/ericsink/SQLitePCL.raw> |
| SQLite | 随 SQLitePCLRaw 提供 | Public Domain | <https://www.sqlite.org/copyright.html> |

完整的传递依赖及精确解析版本可在执行 `dotnet restore` 后查看 `obj/project.assets.json`，或执行：

```powershell
dotnet list src/LocalTodo/LocalTodo.csproj package --include-transitive
```

本文件不替代任何第三方许可证文本。
