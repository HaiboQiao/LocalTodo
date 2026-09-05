using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Helpers;
using LocalTodo.Services;
using Microsoft.Data.Sqlite;

namespace LocalTodo.Data;

/// <summary>
/// 提供数据库完整性检查、手动备份和延迟恢复。
///
/// 所有由程序创建的文件都位于 LocalTodo 根目录下的
/// Data 或 Backups 文件夹。
/// </summary>
public sealed class DatabaseMaintenanceService
{
    private readonly SqliteConnectionFactory
        _connectionFactory;

    private readonly string
        _databaseFile;

    private readonly string
        _backupDirectory;

    private readonly string
        _pendingRestoreFile;

    private readonly IClock
        _clock;

    private readonly ILocalTimeService
        _localTimeService;

    private readonly bool
        _enableApplicationSideEffects;

    public string BackupDirectory =>
        _backupDirectory;

    public DatabaseMaintenanceService(
        SqliteConnectionFactory connectionFactory,
        IClock? clock = null,
        ILocalTimeService? localTimeService = null)
        : this(
            connectionFactory,
            AppPaths.DatabaseFile,
            AppPaths.BackupDirectory,
            AppPaths.PendingRestoreFile,
            clock,
            localTimeService,
            enableApplicationSideEffects: true)
    {
    }

    internal DatabaseMaintenanceService(
        SqliteConnectionFactory connectionFactory,
        string databaseFile,
        string backupDirectory,
        string pendingRestoreFile,
        IClock? clock = null,
        ILocalTimeService? localTimeService = null,
        bool enableApplicationSideEffects = false)
    {
        ArgumentNullException.ThrowIfNull(
            connectionFactory);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            databaseFile);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            backupDirectory);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            pendingRestoreFile);

        _connectionFactory =
            connectionFactory;

        _databaseFile =
            Path.GetFullPath(databaseFile);

        _backupDirectory =
            Path.GetFullPath(backupDirectory);

        _pendingRestoreFile =
            Path.GetFullPath(pendingRestoreFile);

        _clock =
            clock ?? SystemClock.Instance;

        _localTimeService =
            localTimeService ?? LocalTimeService.System;

        _enableApplicationSideEffects =
            enableApplicationSideEffects;
    }

    /// <summary>
    /// 对当前数据库执行完整的 SQLite integrity_check。
    /// </summary>
    public async Task CheckIntegrityAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        await EnsureIntegrityAsync(
            connection,
            cancellationToken);
    }

    /// <summary>
    /// 使用 SQLite 在线备份 API 创建一致性快照，
    /// 并在文件落盘前再次验证快照。
    /// </summary>
    public async Task<string> CreateManualBackupAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(
            _backupDirectory);

        await using SqliteConnection sourceConnection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        await EnsureIntegrityAsync(
            sourceConnection,
            cancellationToken);

        string backupFile =
            GetAvailableBackupFile(
                "localtodo-manual");

        await WriteSnapshotAsync(
            sourceConnection,
            backupFile,
            cancellationToken);

        return backupFile;
    }

    /// <summary>
    /// 验证用户选择的数据库，然后把独立快照放入 Data。
    /// 当前运行中的数据库不会被覆盖；真正恢复发生在下次启动、
    /// 任何业务连接建立之前。
    /// </summary>
    public async Task<string> StageRestoreAsync(
        string backupFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            backupFile);

        string sourceFile =
            Path.GetFullPath(backupFile);

        if (!File.Exists(sourceFile))
        {
            throw new FileNotFoundException(
                "所选备份文件不存在。",
                sourceFile);
        }

        await ValidateRestorableDatabaseAsync(
            sourceFile,
            cancellationToken);

        string? dataDirectory =
            Path.GetDirectoryName(
                _pendingRestoreFile);

        if (string.IsNullOrWhiteSpace(
                dataDirectory))
        {
            throw new InvalidOperationException(
                "待恢复文件没有有效目录。");
        }

        Directory.CreateDirectory(
            dataDirectory);

        string temporaryFile =
            GetTemporaryFile(
                dataDirectory,
                "pending-restore");

        try
        {
            await CopyDatabaseAsync(
                sourceFile,
                temporaryFile,
                cancellationToken);

            await PrepareRestoreCandidateAsync(
                temporaryFile,
                cancellationToken);

            File.Move(
                temporaryFile,
                _pendingRestoreFile,
                overwrite: true);

            return _pendingRestoreFile;
        }
        catch
        {
            TryDeleteFile(temporaryFile);
            throw;
        }
    }

    /// <summary>
    /// 在应用初始化数据库之前应用已经准备好的恢复副本。
    ///
    /// 覆盖前会先生成当前数据库的一致性安全快照；替换使用同目录
    /// 临时文件和原子文件切换，成功后才删除待恢复副本。
    /// </summary>
    public async Task<DatabaseRestoreResult>
        ApplyPendingRestoreAsync(
            CancellationToken cancellationToken = default)
    {
        if (!File.Exists(
                _pendingRestoreFile))
        {
            return DatabaseRestoreResult.NotApplied;
        }

        await ValidateRestorableDatabaseAsync(
            _pendingRestoreFile,
            cancellationToken);

        string? safetyBackupFile =
            null;

        if (File.Exists(_databaseFile))
        {
            Directory.CreateDirectory(
                _backupDirectory);

            try
            {
                await using SqliteConnection sourceConnection =
                    await _connectionFactory
                        .OpenConnectionAsync(
                            cancellationToken);

                await EnsureIntegrityAsync(
                    sourceConnection,
                    cancellationToken);

                safetyBackupFile =
                    GetAvailableBackupFile(
                        "localtodo-before-restore");

                await WriteSnapshotAsync(
                    sourceConnection,
                    safetyBackupFile,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                /*
                 * 恢复的一个重要用途就是替换已损坏的当前数据库。
                 * 当前库无法通过 SQLite 检查时，不能因此阻止一个已经
                 * 验证通过的恢复副本。这里保留原文件及可能存在的 WAL/
                 * SHM 作为未验证救援副本，再继续恢复。
                 */
                SqliteConnection.ClearAllPools();

                safetyBackupFile =
                    GetAvailableBackupFile(
                        "localtodo-before-restore-unverified");

                File.Copy(
                    _databaseFile,
                    safetyBackupFile);

                CopyCompanionFileIfPresent(
                    _databaseFile,
                    safetyBackupFile,
                    "-wal");

                CopyCompanionFileIfPresent(
                    _databaseFile,
                    safetyBackupFile,
                    "-shm");

                if (_enableApplicationSideEffects)
                {
                    AppLog.Error(
                        "当前数据库未通过恢复前完整性检查，" +
                        "已保存未验证救援副本并继续应用已验证备份。",
                        exception);
                }
            }
        }

        string? dataDirectory =
            Path.GetDirectoryName(
                _databaseFile);

        if (string.IsNullOrWhiteSpace(
                dataDirectory))
        {
            throw new InvalidOperationException(
                "数据库文件没有有效目录。");
        }

        Directory.CreateDirectory(
            dataDirectory);

        string replacementFile =
            GetTemporaryFile(
                dataDirectory,
                "restore-replacement");

        try
        {
            await CopyDatabaseAsync(
                _pendingRestoreFile,
                replacementFile,
                cancellationToken);

            await PrepareRestoreCandidateAsync(
                replacementFile,
                cancellationToken);

            SqliteConnection.ClearAllPools();

            if (File.Exists(_databaseFile))
            {
                File.Replace(
                    replacementFile,
                    _databaseFile,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(
                    replacementFile,
                    _databaseFile);
            }

            TryDeleteFile(
                _databaseFile + "-wal");

            TryDeleteFile(
                _databaseFile + "-shm");

            TryDeleteFile(
                _pendingRestoreFile);

            return new DatabaseRestoreResult(
                true,
                safetyBackupFile);
        }
        catch
        {
            TryDeleteFile(replacementFile);
            throw;
        }
    }

    private async Task WriteSnapshotAsync(
        SqliteConnection sourceConnection,
        string targetFile,
        CancellationToken cancellationToken)
    {
        string? targetDirectory =
            Path.GetDirectoryName(targetFile);

        if (string.IsNullOrWhiteSpace(
                targetDirectory))
        {
            throw new InvalidOperationException(
                "备份文件没有有效目录。");
        }

        string temporaryFile =
            GetTemporaryFile(
                targetDirectory,
                "backup");

        try
        {
            await using SqliteConnection targetConnection =
                CreateConnection(
                    temporaryFile,
                    SqliteOpenMode.ReadWriteCreate);

            await targetConnection.OpenAsync(
                cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            sourceConnection.BackupDatabase(
                targetConnection);

            await EnsureIntegrityAsync(
                targetConnection,
                cancellationToken);

            await targetConnection.CloseAsync();

            File.Move(
                temporaryFile,
                targetFile);
        }
        catch
        {
            TryDeleteFile(temporaryFile);
            throw;
        }
    }

    private static async Task CopyDatabaseAsync(
        string sourceFile,
        string targetFile,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection sourceConnection =
            CreateConnection(
                sourceFile,
                SqliteOpenMode.ReadOnly);

        await using SqliteConnection targetConnection =
            CreateConnection(
                targetFile,
                SqliteOpenMode.ReadWriteCreate);

        await sourceConnection.OpenAsync(
            cancellationToken);

        await targetConnection.OpenAsync(
            cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        sourceConnection.BackupDatabase(
            targetConnection);

        await EnsureIntegrityAsync(
            targetConnection,
            cancellationToken);
    }

    private static async Task ValidateRestorableDatabaseAsync(
        string databaseFile,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection =
            CreateConnection(
                databaseFile,
                SqliteOpenMode.ReadOnly);

        await connection.OpenAsync(
            cancellationToken);

        await EnsureIntegrityAsync(
            connection,
            cancellationToken);

        int schemaVersion =
            await ReadSchemaVersionAsync(
                connection,
                cancellationToken);

        if (schemaVersion < 1 ||
            schemaVersion >
                DatabaseInitializer.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"备份的数据库结构版本 {schemaVersion} " +
                "不受当前程序支持。");
        }

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT EXISTS
            (
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = 'tasks'
            );
            """;

        object? result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        if (Convert.ToInt32(
                result,
                CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidOperationException(
                "所选文件不是可恢复的 LocalTodo 数据库。");
        }
    }

    /// <summary>
    /// 在独立副本上完整执行迁移链，再核对当前仓储真正依赖的表和列。
    /// 因此格式伪装成 SQLite、版本号伪造或无法迁移的备份都会在覆盖
    /// 正式数据库之前被拒绝。
    /// </summary>
    private async Task PrepareRestoreCandidateAsync(
        string databaseFile,
        CancellationToken cancellationToken)
    {
        SqliteConnectionFactory candidateFactory =
            new(
                databaseFile,
                pooling: false);

        DatabaseInitializer initializer =
            new(
                candidateFactory,
                enableApplicationSideEffects:
                    false,
                backupDirectory:
                    null,
                localTimeService:
                    _localTimeService,
                clock:
                    _clock);

        await initializer.InitializeAsync(
            cancellationToken);

        await using SqliteConnection connection =
            CreateConnection(
                databaseFile,
                SqliteOpenMode.ReadOnly);

        await connection.OpenAsync(
            cancellationToken);

        await EnsureIntegrityAsync(
            connection,
            cancellationToken);

        int schemaVersion =
            await ReadSchemaVersionAsync(
                connection,
                cancellationToken);

        if (schemaVersion !=
            DatabaseInitializer.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                "恢复候选数据库没有升级到当前结构版本。");
        }

        string[] requiredTaskColumns =
        [
            "id",
            "title",
            "description",
            "status",
            "priority",
            "is_important",
            "is_continuous",
            "due_at",
            "due_local_at",
            "has_due_time",
            "reminder_enabled",
            "reminder_minutes_before",
            "repeat_type",
            "recurrence_series_id",
            "recurrence_anchor_month",
            "recurrence_anchor_day",
            "reminder_delivered_at",
            "quadrant_mode",
            "manual_quadrant",
            "created_at",
            "updated_at",
            "completed_at",
            "is_deleted",
            "revision",
            "deleted_at"
        ];

        foreach (string column in requiredTaskColumns)
        {
            using SqliteCommand columnCommand =
                connection.CreateCommand();

            columnCommand.CommandText =
                """
                SELECT EXISTS
                (
                    SELECT 1
                    FROM pragma_table_info('tasks')
                    WHERE name = $column
                );
                """;

            columnCommand.Parameters.AddWithValue(
                "$column",
                column);

            object? columnExists =
                await columnCommand.ExecuteScalarAsync(
                    cancellationToken);

            if (Convert.ToInt32(
                    columnExists,
                    CultureInfo.InvariantCulture) != 1)
            {
                throw new InvalidOperationException(
                    $"恢复候选数据库缺少 tasks.{column}。");
            }
        }

        using SqliteCommand settingsCommand =
            connection.CreateCommand();

        settingsCommand.CommandText =
            """
            SELECT EXISTS
            (
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = 'app_settings'
            );
            """;

        object? settingsTableExists =
            await settingsCommand.ExecuteScalarAsync(
                cancellationToken);

        if (Convert.ToInt32(
                settingsTableExists,
                CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidOperationException(
                "恢复候选数据库缺少 app_settings 表。");
        }
    }

    private static async Task EnsureIntegrityAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            "PRAGMA integrity_check;";

        using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        string? firstFailure =
            null;

        int resultCount =
            0;

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            resultCount++;

            string result =
                reader.GetString(0);

            if (!string.Equals(
                    result,
                    "ok",
                    StringComparison.OrdinalIgnoreCase))
            {
                firstFailure ??=
                    result;
            }
        }

        if (resultCount != 1 ||
            firstFailure is not null)
        {
            throw new InvalidOperationException(
                "数据库完整性检查失败。" +
                $"检查结果：{firstFailure ?? "无有效结果"}");
        }
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            "PRAGMA user_version;";

        object? result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return Convert.ToInt32(
            result,
            CultureInfo.InvariantCulture);
    }

    private string GetAvailableBackupFile(
        string prefix)
    {
        string timestamp =
            _localTimeService
                .ToLocalDateTime(
                    _clock.UtcNow)
                .ToString(
                    "yyyyMMdd-HHmmssfff",
                    CultureInfo.InvariantCulture);

        string candidate =
            Path.Combine(
                _backupDirectory,
                $"{prefix}-{timestamp}.db");

        int suffix =
            1;

        while (File.Exists(candidate))
        {
            candidate =
                Path.Combine(
                    _backupDirectory,
                    $"{prefix}-{timestamp}-{suffix}.db");

            suffix++;
        }

        return candidate;
    }

    private static SqliteConnection CreateConnection(
        string databaseFile,
        SqliteOpenMode mode)
    {
        SqliteConnectionStringBuilder builder =
            new()
            {
                DataSource =
                    databaseFile,

                Mode =
                    mode,

                Pooling =
                    false
            };

        return new SqliteConnection(
            builder.ToString());
    }

    private static string GetTemporaryFile(
        string directory,
        string purpose)
    {
        return Path.Combine(
            directory,
            $".localtodo-{purpose}-{Guid.NewGuid():N}.tmp");
    }

    private static void TryDeleteFile(
        string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // 清理辅助文件失败不能覆盖原始数据库异常。
        }
    }

    private static void CopyCompanionFileIfPresent(
        string sourceDatabaseFile,
        string targetDatabaseFile,
        string suffix)
    {
        string sourceFile =
            sourceDatabaseFile + suffix;

        if (!File.Exists(sourceFile))
        {
            return;
        }

        File.Copy(
            sourceFile,
            targetDatabaseFile + suffix);
    }
}

public sealed record DatabaseRestoreResult(
    bool Applied,
    string? SafetyBackupFile)
{
    public static DatabaseRestoreResult NotApplied { get; } =
        new(false, null);
}
