using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Helpers;
using Microsoft.Data.Sqlite;

namespace LocalTodo.Data;

/// <summary>
/// 负责创建和打开 LocalTodo SQLite 数据库连接。
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    private readonly string
        _databaseDirectory;

    public SqliteConnectionFactory()
        : this(
            AppPaths.DatabaseFile,
            pooling: true)
    {
    }

    /// <summary>
    /// 使用指定数据库文件创建连接工厂。
    ///
    /// 正式程序继续通过无参数构造函数使用
    /// AppPaths.DatabaseFile。
    /// 此入口主要用于让自动化测试使用独立的
    /// 临时数据库，避免接触真实用户数据。
    /// </summary>
    public SqliteConnectionFactory(
        string databaseFile,
        bool pooling = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databaseFile);

        string fullDatabaseFile =
            Path.GetFullPath(
                databaseFile);

        _databaseDirectory =
            Path.GetDirectoryName(
                fullDatabaseFile)
            ?? throw new ArgumentException(
                "数据库文件必须包含有效目录。",
                nameof(databaseFile));

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = fullDatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = pooling
        };

        _connectionString = builder.ToString();
    }

    public async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(
            _databaseDirectory);

        SqliteConnection connection =
            new(_connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);

            await ConfigureConnectionAsync(
                connection,
                cancellationToken);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand foreignKeysCommand =
            connection.CreateCommand();

        foreignKeysCommand.CommandText =
            "PRAGMA foreign_keys = ON;";

        await foreignKeysCommand.ExecuteNonQueryAsync(
            cancellationToken);

        using SqliteCommand timeoutCommand =
            connection.CreateCommand();

        timeoutCommand.CommandText =
            "PRAGMA busy_timeout = 5000;";

        await timeoutCommand.ExecuteNonQueryAsync(
            cancellationToken);
    }
}
