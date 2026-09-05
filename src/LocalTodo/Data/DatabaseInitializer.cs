using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;
using Microsoft.Data.Sqlite;

namespace LocalTodo.Data;

/// <summary>
/// 创建并升级 LocalTodo 数据库结构。
/// </summary>
public sealed class DatabaseInitializer
{
    public const int CurrentSchemaVersion =
        13;

    private readonly SqliteConnectionFactory
        _connectionFactory;

    private readonly bool
        _enableApplicationSideEffects;

    private readonly string?
        _backupDirectory;

    private readonly ILocalTimeService
        _localTimeService;

    private readonly IClock
        _clock;

    public DatabaseInitializer(
        SqliteConnectionFactory connectionFactory,
        IClock? clock = null,
        ILocalTimeService? localTimeService = null)
        : this(
            connectionFactory,
            enableApplicationSideEffects: true,
            backupDirectory:
                AppPaths.BackupDirectory,
            localTimeService:
                localTimeService,
            clock:
                clock)
    {
    }

    /// <summary>
    /// 测试专用入口。
    ///
    /// false 时仍完整执行数据库创建和迁移，
    /// 但不创建正式程序的 Data/Logs 目录，
    /// 也不向正式日志写入测试初始化记录。
    /// </summary>
    internal DatabaseInitializer(
        SqliteConnectionFactory connectionFactory,
        bool enableApplicationSideEffects,
        string? backupDirectory = null,
        ILocalTimeService? localTimeService = null,
        IClock? clock = null)
    {
        _connectionFactory =
            connectionFactory;

        _enableApplicationSideEffects =
            enableApplicationSideEffects;

        _backupDirectory =
            string.IsNullOrWhiteSpace(
                backupDirectory)
                ? null
                : Path.GetFullPath(
                    backupDirectory);

        _localTimeService =
            localTimeService ??
            LocalTimeService.System;

        _clock =
            clock ??
            SystemClock.Instance;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (_enableApplicationSideEffects)
        {
            AppPaths.EnsureDirectories();
        }

        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        int databaseVersion =
            await GetUserVersionAsync(
                connection,
                cancellationToken);

        if (databaseVersion >
            CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"数据库结构版本 {databaseVersion} " +
                $"高于当前程序支持的版本 " +
                $"{CurrentSchemaVersion}。");
        }

        string? migrationBackupFile =
            null;

        if (databaseVersion <
                CurrentSchemaVersion &&
            await HasUserTablesAsync(
                connection,
                cancellationToken))
        {
            migrationBackupFile =
                await CreateMigrationBackupAsync(
                    connection,
                    databaseVersion,
                    cancellationToken);

            if (_enableApplicationSideEffects &&
                !string.IsNullOrWhiteSpace(
                    migrationBackupFile))
            {
                AppLog.Information(
                    "数据库升级前备份完成。" +
                    $"路径：{migrationBackupFile}");
            }
        }

        if (databaseVersion == 0)
        {
            await CreateVersionTwoAsync(
                connection,
                cancellationToken);

            databaseVersion =
                2;
        }
        else if (databaseVersion == 1)
        {
            await MigrateVersionOneToTwoAsync(
                connection,
                cancellationToken);

            databaseVersion =
                2;
        }

        if (databaseVersion == 2)
        {
            await MigrateVersionTwoToThreeAsync(
                connection,
                cancellationToken);

            databaseVersion =
                3;
        }

        if (databaseVersion == 3)
        {
            await MigrateVersionThreeToFourAsync(
                connection,
                cancellationToken);

            databaseVersion =
                4;
        }

        if (databaseVersion == 4)
        {
            await MigrateVersionFourToFiveAsync(
                connection,
                cancellationToken);

            databaseVersion =
                5;
        }

        if (databaseVersion == 5)
        {
            await MigrateVersionFiveToSixAsync(
                connection,
                cancellationToken);

            databaseVersion =
                6;
        }

        if (databaseVersion == 6)
        {
            await MigrateVersionSixToSevenAsync(
                connection,
                cancellationToken);

            databaseVersion =
                7;
        }

        if (databaseVersion == 7)
        {
            await MigrateVersionSevenToEightAsync(
                connection,
                cancellationToken);

            databaseVersion =
                8;
        }

        if (databaseVersion == 8)
        {
            await MigrateVersionEightToNineAsync(
                connection,
                cancellationToken);

            databaseVersion =
                9;
        }

        if (databaseVersion == 9)
        {
            await MigrateVersionNineToTenAsync(
                connection,
                cancellationToken);

            databaseVersion =
                10;
        }

        if (databaseVersion == 10)
        {
            await MigrateVersionTenToElevenAsync(
                connection,
                cancellationToken);

            databaseVersion =
                11;
        }

        if (databaseVersion == 11)
        {
            await MigrateVersionElevenToTwelveAsync(
                connection,
                cancellationToken);

            databaseVersion =
                12;
        }

        if (databaseVersion == 12)
        {
            await MigrateVersionTwelveToThirteenAsync(
                connection,
                cancellationToken);

            databaseVersion =
                13;
        }

        if (databaseVersion !=
            CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"无法将数据库从版本 " +
                $"{databaseVersion} 升级到版本 " +
                $"{CurrentSchemaVersion}。");
        }

        /*
         * 性能索引不改变数据结构语义，
         * 每次启动以 IF NOT EXISTS 方式补齐。
         * 这样已有数据库无需仅为索引再迁移版本。
         */
        await EnsurePerformanceIndexesAsync(
            connection,
            cancellationToken);

        if (_enableApplicationSideEffects)
        {
            AppLog.Information(
                $"数据库初始化完成。结构版本：" +
                $"{databaseVersion}；路径：" +
                $"{AppPaths.DatabaseFile}");
        }
    }

    private static async Task<bool>
        HasUserTablesAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT EXISTS
            (
                SELECT 1
                FROM sqlite_master
                WHERE
                    type = 'table'
                    AND name NOT LIKE 'sqlite_%'
            );
            """;

        object? result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return Convert.ToInt32(
            result,
            CultureInfo.InvariantCulture) == 1;
    }

    private async Task<string?>
        CreateMigrationBackupAsync(
            SqliteConnection sourceConnection,
            int sourceVersion,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                _backupDirectory))
        {
            return null;
        }

        await EnsureDatabaseIntegrityAsync(
            sourceConnection,
            cancellationToken);

        Directory.CreateDirectory(
            _backupDirectory);

        string timestamp =
            _localTimeService
                .ToLocalDateTime(
                    _clock.UtcNow)
                .ToString(
                "yyyyMMdd-HHmmssfff",
                CultureInfo.InvariantCulture);

        string fileStem =
            $"localtodo-before-schema-v{sourceVersion}" +
            $"-to-v{CurrentSchemaVersion}-{timestamp}";

        string backupFile =
            GetAvailableBackupFile(
                _backupDirectory,
                fileStem);

        string temporaryFile =
            Path.Combine(
                _backupDirectory,
                $".{Path.GetFileName(backupFile)}" +
                $".{Guid.NewGuid():N}.tmp");

        try
        {
            SqliteConnectionStringBuilder builder =
                new()
                {
                    DataSource =
                        temporaryFile,

                    Mode =
                        SqliteOpenMode.ReadWriteCreate,

                    Pooling =
                        false
                };

            await using (
                SqliteConnection backupConnection =
                    new(builder.ToString()))
            {
                await backupConnection.OpenAsync(
                    cancellationToken);

                cancellationToken
                    .ThrowIfCancellationRequested();

                sourceConnection.BackupDatabase(
                    backupConnection);

                await EnsureDatabaseIntegrityAsync(
                    backupConnection,
                    cancellationToken);
            }

            File.Move(
                temporaryFile,
                backupFile);

            return backupFile;
        }
        catch
        {
            TryDeleteFile(
                temporaryFile);

            throw;
        }
    }

    private static async Task
        EnsureDatabaseIntegrityAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            "PRAGMA quick_check;";

        object? result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        string? checkResult =
            Convert.ToString(
                result,
                CultureInfo.InvariantCulture);

        if (!string.Equals(
                checkResult,
                "ok",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "数据库完整性检查失败，已停止结构升级。" +
                $"检查结果：{checkResult ?? "无结果"}");
        }
    }

    private static string GetAvailableBackupFile(
        string backupDirectory,
        string fileStem)
    {
        string candidate =
            Path.Combine(
                backupDirectory,
                $"{fileStem}.db");

        int suffix =
            1;

        while (File.Exists(candidate))
        {
            candidate =
                Path.Combine(
                    backupDirectory,
                    $"{fileStem}-{suffix}.db");

            suffix++;
        }

        return candidate;
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
            /*
             * 清理未完成的临时备份失败时，
             * 不能覆盖真正的迁移或备份异常。
             */
        }
    }

    private static async Task<int>
        GetUserVersionAsync(
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

    private async Task
        CreateVersionTwoAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            foreach (string statement in
                     GetVersionTwoSchemaStatements())
            {
                await ExecuteStatementAsync(
                    connection,
                    transaction,
                    statement,
                    cancellationToken);
            }

            await InsertDefaultSettingsAsync(
                connection,
                transaction,
                cancellationToken);

            await SetUserVersionAsync(
                connection,
                transaction,
                2,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task
        MigrateVersionOneToTwoAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        await SetForeignKeysEnabledAsync(
            connection,
            enabled: false,
            cancellationToken);

        try
        {
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            try
            {
                foreach (string statement in
                         GetVersionOneToTwoMigrationStatements())
                {
                    await ExecuteStatementAsync(
                        connection,
                        transaction,
                        statement,
                        cancellationToken);
                }

                await RemoveLegacySettingsAsync(
                    connection,
                    transaction,
                    cancellationToken);

                await SetUserVersionAsync(
                    connection,
                    transaction,
                    2,
                    cancellationToken);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        finally
        {
            await SetForeignKeysEnabledAsync(
                connection,
                enabled: true,
                CancellationToken.None);
        }
    }

    private static IReadOnlyList<string>
        GetVersionTwoSchemaStatements()
    {
        return
        [
            """
            CREATE TABLE IF NOT EXISTS tasks
            (
                id TEXT PRIMARY KEY NOT NULL,

                title TEXT NOT NULL,

                description TEXT
                    NOT NULL
                    DEFAULT '',

                status INTEGER
                    NOT NULL
                    DEFAULT 0
                    CHECK (status IN (0, 1)),

                priority INTEGER
                    NOT NULL
                    DEFAULT 0
                    CHECK (priority BETWEEN 0 AND 3),

                is_important INTEGER
                    NOT NULL
                    DEFAULT 0
                    CHECK (is_important IN (0, 1)),

                due_at TEXT NULL,

                quadrant_mode INTEGER
                    NOT NULL
                    DEFAULT 0
                    CHECK (quadrant_mode IN (0, 1)),

                manual_quadrant INTEGER NULL
                    CHECK
                    (
                        manual_quadrant IS NULL
                        OR manual_quadrant
                            BETWEEN 1 AND 4
                    ),

                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                completed_at TEXT NULL,

                is_deleted INTEGER
                    NOT NULL
                    DEFAULT 0
                    CHECK (is_deleted IN (0, 1))
            );
            """,

            """
            CREATE INDEX IF NOT EXISTS
                idx_tasks_active_due
            ON tasks
            (
                is_deleted,
                status,
                due_at
            );
            """,

            """
            CREATE INDEX IF NOT EXISTS
                idx_tasks_updated_at
            ON tasks
            (
                updated_at
            );
            """,

            """
            CREATE TABLE IF NOT EXISTS app_settings
            (
                setting_key TEXT
                    PRIMARY KEY
                    COLLATE NOCASE,

                setting_value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """
        ];
    }

    private static IReadOnlyList<string>
        GetVersionOneToTwoMigrationStatements()
    {
        return
        [
            """
            DROP TABLE IF EXISTS tasks_v2;
            """,

            """
            CREATE TABLE tasks_v2
            (
                id TEXT PRIMARY KEY NOT NULL,

                title TEXT NOT NULL,

                description TEXT
                    NOT NULL
                    DEFAULT '',

                status INTEGER
                    NOT NULL
                    DEFAULT 0
                    CHECK (status IN (0, 1)),

                priority INTEGER
                    NOT NULL
                    DEFAULT 0
                    CHECK (priority BETWEEN 0 AND 3),

                is_important INTEGER
                    NOT NULL
                    DEFAULT 0
                    CHECK (is_important IN (0, 1)),

                due_at TEXT NULL,

                quadrant_mode INTEGER
                    NOT NULL
                    DEFAULT 0
                    CHECK (quadrant_mode IN (0, 1)),

                manual_quadrant INTEGER NULL
                    CHECK
                    (
                        manual_quadrant IS NULL
                        OR manual_quadrant
                            BETWEEN 1 AND 4
                    ),

                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                completed_at TEXT NULL,

                is_deleted INTEGER
                    NOT NULL
                    DEFAULT 0
                    CHECK (is_deleted IN (0, 1))
            );
            """,

            """
            INSERT INTO tasks_v2
            (
                id,
                title,
                description,
                status,
                priority,
                is_important,
                due_at,
                quadrant_mode,
                manual_quadrant,
                created_at,
                updated_at,
                completed_at,
                is_deleted
            )
            SELECT
                id,
                title,
                description,
                status,
                priority,
                is_important,
                due_at,
                quadrant_mode,
                manual_quadrant,
                created_at,
                updated_at,
                completed_at,
                is_deleted
            FROM tasks;
            """,

            """
            DROP TABLE tasks;
            """,

            """
            ALTER TABLE tasks_v2
            RENAME TO tasks;
            """,

            """
            CREATE INDEX idx_tasks_active_due
            ON tasks
            (
                is_deleted,
                status,
                due_at
            );
            """,

            """
            CREATE INDEX idx_tasks_updated_at
            ON tasks
            (
                updated_at
            );
            """,

            """
            DROP TABLE IF EXISTS notes;
            """
        ];
    }

    private async Task
        InsertDefaultSettingsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
    {
        Dictionary<string, string> defaultSettings =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["UrgencyThresholdDays"] =
                    "2"
            };

        string updatedAt =
            _clock.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture);

        foreach (KeyValuePair<string, string>
                 setting in defaultSettings)
        {
            using SqliteCommand command =
                connection.CreateCommand();

            command.Transaction =
                transaction;

            command.CommandText =
                """
                INSERT OR IGNORE INTO app_settings
                (
                    setting_key,
                    setting_value,
                    updated_at
                )
                VALUES
                (
                    $key,
                    $value,
                    $updatedAt
                );
                """;

            command.Parameters.AddWithValue(
                "$key",
                setting.Key);

            command.Parameters.AddWithValue(
                "$value",
                setting.Value);

            command.Parameters.AddWithValue(
                "$updatedAt",
                updatedAt);

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    private static async Task
        RemoveLegacySettingsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            """
            DELETE FROM app_settings
            WHERE setting_key IN
            (
                'DefaultListName',
                'PortableMode',
                'Theme'
            );
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task
        ExecuteStatementAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string statement,
            CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            statement;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task
        SetUserVersionAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int version,
            CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            $"PRAGMA user_version = {version};";

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task
        SetForeignKeysEnabledAsync(
            SqliteConnection connection,
            bool enabled,
            CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            enabled
                ? "PRAGMA foreign_keys = ON;"
                : "PRAGMA foreign_keys = OFF;";

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task
    MigrateVersionTwoToThreeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            IReadOnlyList<string> statements =
            [
                """
            ALTER TABLE tasks
            ADD COLUMN has_due_time INTEGER
                NOT NULL
                DEFAULT 0
                CHECK (has_due_time IN (0, 1));
            """,

            """
            ALTER TABLE tasks
            ADD COLUMN reminder_enabled INTEGER
                NOT NULL
                DEFAULT 0
                CHECK (reminder_enabled IN (0, 1));
            """,

            """
            ALTER TABLE tasks
            ADD COLUMN repeat_type INTEGER
                NOT NULL
                DEFAULT 0
                CHECK (repeat_type BETWEEN 0 AND 5);
            """,

            """
            ALTER TABLE tasks
            ADD COLUMN reminder_delivered_at TEXT NULL;
            """,

            """
            CREATE INDEX IF NOT EXISTS
                idx_tasks_pending_reminders
            ON tasks
            (
                is_deleted,
                status,
                reminder_enabled,
                due_at
            );
            """
            ];

            foreach (string statement
                     in statements)
            {
                await ExecuteStatementAsync(
                    connection,
                    transaction,
                    statement,
                    cancellationToken);
            }

            await SetUserVersionAsync(
                connection,
                transaction,
                3,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task
    MigrateVersionThreeToFourAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            await ExecuteStatementAsync(
                connection,
                transaction,
                """
            ALTER TABLE tasks
            ADD COLUMN reminder_minutes_before INTEGER
                NOT NULL
                DEFAULT 0
                CHECK (reminder_minutes_before >= 0);
            """,
                cancellationToken);

            await SetUserVersionAsync(
                connection,
                transaction,
                4,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();

            throw;
        }
    }

    private static async Task
    MigrateVersionFourToFiveAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            await ExecuteStatementAsync(
                connection,
                transaction,
                """
            ALTER TABLE tasks
            ADD COLUMN recurrence_series_id TEXT NULL;
            """,
                cancellationToken);

            await ExecuteStatementAsync(
                connection,
                transaction,
                """
            CREATE INDEX IF NOT EXISTS
                idx_tasks_recurrence_series
            ON tasks
            (
                recurrence_series_id,
                is_deleted,
                status
            );
            """,
                cancellationToken);

            await SetUserVersionAsync(
                connection,
                transaction,
                5,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();

            throw;
        }
    }

    private static async Task
    MigrateVersionFiveToSixAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            await ExecuteStatementAsync(
                connection,
                transaction,
                """
            ALTER TABLE tasks
            ADD COLUMN revision INTEGER
                NOT NULL
                DEFAULT 0
                CHECK (revision >= 0);
            """,
                cancellationToken);

            await SetUserVersionAsync(
                connection,
                transaction,
                6,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();

            throw;
        }
    }

    private async Task MigrateVersionSixToSevenAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            IReadOnlyList<string> statements =
            [
                """
                ALTER TABLE tasks
                ADD COLUMN due_local_at TEXT NULL;
                """,

                """
                ALTER TABLE tasks
                ADD COLUMN recurrence_anchor_month INTEGER NULL
                    CHECK
                    (
                        recurrence_anchor_month IS NULL
                        OR recurrence_anchor_month BETWEEN 1 AND 12
                    );
                """,

                """
                ALTER TABLE tasks
                ADD COLUMN recurrence_anchor_day INTEGER NULL
                    CHECK
                    (
                        recurrence_anchor_day IS NULL
                        OR recurrence_anchor_day BETWEEN 1 AND 31
                    );
                """,

                """
                DROP INDEX IF EXISTS idx_tasks_calendar_due;
                """,

                """
                DROP INDEX IF EXISTS idx_tasks_due_reminders;
                """
            ];

            foreach (string statement in statements)
            {
                await ExecuteStatementAsync(
                    connection,
                    transaction,
                    statement,
                    cancellationToken);
            }

            await BackfillLocalDueAndAnchorsAsync(
                connection,
                transaction,
                cancellationToken);

            await SetUserVersionAsync(
                connection,
                transaction,
                7,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task BackfillLocalDueAndAnchorsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        List<DueMigrationRow> rows =
            [];

        using (SqliteCommand readCommand =
               connection.CreateCommand())
        {
            readCommand.Transaction =
                transaction;

            readCommand.CommandText =
                """
                SELECT id, due_at, repeat_type
                FROM tasks
                WHERE due_at IS NOT NULL;
                """;

            using SqliteDataReader reader =
                await readCommand.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                       cancellationToken))
            {
                DateTimeOffset instant =
                    DateTimeOffset.Parse(
                        reader.GetString(1),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind);

                DateTime localDateTime =
                    _localTimeService
                        .ToLocalDateTime(
                            instant);

                rows.Add(
                    new DueMigrationRow(
                        reader.GetString(0),
                        localDateTime,
                        reader.GetInt32(2)));
            }
        }

        foreach (DueMigrationRow row in rows)
        {
            int? anchorMonth =
                row.RepeatType ==
                    (int)TaskRepeatType.Yearly
                    ? row.LocalDateTime.Month
                    : null;

            int? anchorDay =
                row.RepeatType ==
                    (int)TaskRepeatType.Monthly ||
                row.RepeatType ==
                    (int)TaskRepeatType.Yearly
                    ? row.LocalDateTime.Day
                    : null;

            using SqliteCommand updateCommand =
                connection.CreateCommand();

            updateCommand.Transaction =
                transaction;

            updateCommand.CommandText =
                """
                UPDATE tasks
                SET
                    due_local_at = $dueLocalAt,
                    recurrence_anchor_month = $anchorMonth,
                    recurrence_anchor_day = $anchorDay
                WHERE id = $id;
                """;

            updateCommand.Parameters.AddWithValue(
                "$id",
                row.Id);

            updateCommand.Parameters.AddWithValue(
                "$dueLocalAt",
                row.LocalDateTime.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fffffff",
                    CultureInfo.InvariantCulture));

            updateCommand.Parameters.AddWithValue(
                "$anchorMonth",
                anchorMonth.HasValue
                    ? anchorMonth.Value
                    : DBNull.Value);

            updateCommand.Parameters.AddWithValue(
                "$anchorDay",
                anchorDay.HasValue
                    ? anchorDay.Value
                    : DBNull.Value);

            await updateCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    private sealed record DueMigrationRow(
        string Id,
        DateTime LocalDateTime,
        int RepeatType);

    /// <summary>
    /// v8 将“内容最后修改时间”和“进入垃圾箱时间”分离。
    ///
    /// 旧垃圾箱记录无法还原更早的真实删除事件，只能把旧流程在删除时
    /// 写入的 updated_at 一次性回填为 deleted_at；此后恢复任务只更新
    /// updated_at 并清空 deleted_at。
    /// </summary>
    private static async Task
        MigrateVersionSevenToEightAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            IReadOnlyList<string> statements =
            [
                """
                ALTER TABLE tasks
                ADD COLUMN deleted_at TEXT NULL;
                """,

                """
                UPDATE tasks
                SET deleted_at = updated_at
                WHERE
                    is_deleted = 1
                    AND deleted_at IS NULL;
                """,

                """
                CREATE INDEX IF NOT EXISTS
                    idx_tasks_deleted_at
                ON tasks
                (
                    is_deleted,
                    deleted_at
                );
                """
            ];

            foreach (string statement in statements)
            {
                await ExecuteStatementAsync(
                    connection,
                    transaction,
                    statement,
                    cancellationToken);
            }

            await SetUserVersionAsync(
                connection,
                transaction,
                8,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// v9 增加每周计划和成果记录。
    ///
    /// 两个模块使用独立表，不改变原有 tasks 表和任务业务语义。
    /// </summary>
    private static async Task
        MigrateVersionEightToNineAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            IReadOnlyList<string> statements =
            [
                """
                CREATE TABLE IF NOT EXISTS weekly_plan_items
                (
                    id TEXT PRIMARY KEY NOT NULL,
                    day_of_week INTEGER NOT NULL
                        CHECK (day_of_week BETWEEN 1 AND 7),
                    start_minutes INTEGER NOT NULL
                        CHECK (start_minutes BETWEEN 0 AND 1439),
                    end_minutes INTEGER NOT NULL
                        CHECK (end_minutes BETWEEN 1 AND 1440),
                    title TEXT NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    color_hex TEXT NOT NULL DEFAULT '#2563EB',
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    CHECK (end_minutes > start_minutes)
                );
                """,

                """
                CREATE INDEX IF NOT EXISTS idx_weekly_plan_day_time
                ON weekly_plan_items
                (
                    day_of_week,
                    start_minutes,
                    end_minutes
                );
                """,

                """
                CREATE TABLE IF NOT EXISTS achievement_records
                (
                    id TEXT PRIMARY KEY NOT NULL,
                    title TEXT NOT NULL,
                    details TEXT NOT NULL DEFAULT '',
                    category INTEGER NOT NULL DEFAULT 0
                        CHECK (category BETWEEN 0 AND 5),
                    cycle INTEGER NOT NULL DEFAULT 0
                        CHECK (cycle BETWEEN 0 AND 6),
                    status INTEGER NOT NULL DEFAULT 0
                        CHECK (status BETWEEN 0 AND 2),
                    progress_percent INTEGER NOT NULL DEFAULT 0
                        CHECK (progress_percent BETWEEN 0 AND 100),
                    period_start TEXT NOT NULL,
                    period_end TEXT NULL,
                    completed_on TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    CHECK
                    (
                        period_end IS NULL
                        OR period_end >= period_start
                    )
                );
                """,

                """
                CREATE INDEX IF NOT EXISTS idx_achievements_status_period
                ON achievement_records
                (
                    status,
                    period_start,
                    updated_at
                );
                """
            ];

            foreach (string statement in statements)
            {
                await ExecuteStatementAsync(
                    connection,
                    transaction,
                    statement,
                    cancellationToken);
            }

            await SetUserVersionAsync(
                connection,
                transaction,
                9,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// v10 将每周计划颜色改为稳定的语义键。
    /// 旧 color_hex 列继续保留，避免重建表带来的数据风险；
    /// 新代码只读取和写入 color_key。
    /// </summary>
    private static async Task
        MigrateVersionNineToTenAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            bool colorKeyColumnExists =
                await WeeklyPlanColorKeyColumnExistsAsync(
                    connection,
                    transaction,
                    cancellationToken);

            if (!colorKeyColumnExists)
            {
                await ExecuteStatementAsync(
                    connection,
                    transaction,
                    """
                    ALTER TABLE weekly_plan_items
                    ADD COLUMN color_key TEXT NOT NULL DEFAULT 'Blue'
                        CHECK
                        (
                            color_key IN
                            (
                                'Blue',
                                'Green',
                                'Teal',
                                'Purple',
                                'Pink',
                                'Orange',
                                'Yellow',
                                'Gray'
                            )
                        );
                    """,
                    cancellationToken);

                await ExecuteStatementAsync(
                    connection,
                    transaction,
                    """
                    UPDATE weekly_plan_items
                    SET color_key =
                        CASE UPPER(TRIM(color_hex))
                            WHEN '#059669' THEN 'Green'
                            WHEN '#0891B2' THEN 'Teal'
                            WHEN '#7C3AED' THEN 'Purple'
                            WHEN '#DC2626' THEN 'Pink'
                            WHEN '#EA580C' THEN 'Orange'
                            WHEN '#475569' THEN 'Gray'
                            ELSE 'Blue'
                        END;
                    """,
                    cancellationToken);
            }

            await SetUserVersionAsync(
                connection,
                transaction,
                10,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task<bool>
        WeeklyPlanColorKeyColumnExistsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            "PRAGMA table_info(weekly_plan_items);";

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            if (string.Equals(
                    reader.GetString(1),
                    "color_key",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// v11 为成长记录增加稳定的分类键。
    ///
    /// 旧 category 整数列继续保留，避免重建成果表；
    /// 新代码只使用 category_key 表达八类成长成果。
    /// </summary>
    private static async Task
        MigrateVersionTenToElevenAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            bool categoryKeyColumnExists =
                await AchievementCategoryKeyColumnExistsAsync(
                    connection,
                    transaction,
                    cancellationToken);

            if (!categoryKeyColumnExists)
            {
                await ExecuteStatementAsync(
                    connection,
                    transaction,
                    """
                    ALTER TABLE achievement_records
                    ADD COLUMN category_key TEXT NOT NULL DEFAULT 'Other'
                        CHECK
                        (
                            category_key IN
                            (
                                'Skill',
                                'Project',
                                'Learning',
                                'Work',
                                'Life',
                                'Health',
                                'Breakthrough',
                                'Other'
                            )
                        );
                    """,
                    cancellationToken);

                await ExecuteStatementAsync(
                    connection,
                    transaction,
                    """
                    UPDATE achievement_records
                    SET category_key =
                        CASE category
                            WHEN 1 THEN 'Work'
                            WHEN 2 THEN 'Learning'
                            WHEN 3 THEN 'Health'
                            WHEN 4 THEN 'Life'
                            WHEN 5 THEN 'Project'
                            ELSE 'Other'
                        END;
                    """,
                    cancellationToken);
            }

            await SetUserVersionAsync(
                connection,
                transaction,
                11,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task<bool>
        AchievementCategoryKeyColumnExistsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            "PRAGMA table_info(achievement_records);";

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            if (string.Equals(
                    reader.GetString(1),
                    "category_key",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// v12 将固定成果分类升级为用户可维护的本地分类表。
    /// 原有 category/category_key 继续保留作旧版本兼容；
    /// category_id 指向新的分类标识，迁移不会改动成果内容。
    /// </summary>
    private static async Task
        MigrateVersionElevenToTwelveAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            await ExecuteStatementAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS achievement_categories
                (
                    id TEXT PRIMARY KEY NOT NULL,
                    name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    color_hex TEXT NOT NULL,
                    sort_order INTEGER NOT NULL DEFAULT 0,
                    is_builtin INTEGER NOT NULL DEFAULT 0
                        CHECK (is_builtin IN (0, 1)),
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                """,
                cancellationToken);

            await ExecuteStatementAsync(
                connection,
                transaction,
                """
                INSERT OR IGNORE INTO achievement_categories
                (
                    id,
                    name,
                    color_hex,
                    sort_order,
                    is_builtin,
                    created_at,
                    updated_at
                )
                VALUES
                    ('builtin-skill', '技能成长', '#4F6BED', 10, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('builtin-project', '项目成果', '#7357E6', 20, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('builtin-learning', '学习成果', '#35A77B', 30, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('builtin-work', '工作成果', '#4B6B9A', 40, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('builtin-life', '生活体验', '#E58A45', 50, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('builtin-health', '健康成长', '#3B82F6', 60, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('builtin-breakthrough', '个人突破', '#D05A8A', 70, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('builtin-other', '其他', '#7C8598', 80, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                """,
                cancellationToken);

            bool categoryIdColumnExists =
                await AchievementCategoryIdColumnExistsAsync(
                    connection,
                    transaction,
                    cancellationToken);

            if (!categoryIdColumnExists)
            {
                await ExecuteStatementAsync(
                    connection,
                    transaction,
                    """
                    ALTER TABLE achievement_records
                    ADD COLUMN category_id TEXT NULL;
                    """,
                    cancellationToken);
            }

            await ExecuteStatementAsync(
                connection,
                transaction,
                """
                UPDATE achievement_records
                SET category_id =
                    CASE category_key
                        WHEN 'Skill' THEN 'builtin-skill'
                        WHEN 'Project' THEN 'builtin-project'
                        WHEN 'Learning' THEN 'builtin-learning'
                        WHEN 'Work' THEN 'builtin-work'
                        WHEN 'Life' THEN 'builtin-life'
                        WHEN 'Health' THEN 'builtin-health'
                        WHEN 'Breakthrough' THEN 'builtin-breakthrough'
                        ELSE 'builtin-other'
                    END
                WHERE
                    category_id IS NULL
                    OR TRIM(category_id) = '';
                """,
                cancellationToken);

            await ExecuteStatementAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS
                    idx_achievements_category_period
                ON achievement_records
                (
                    category_id,
                    period_start,
                    completed_on
                );
                """,
                cancellationToken);

            await SetUserVersionAsync(
                connection,
                transaction,
                12,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task<bool>
        AchievementCategoryIdColumnExistsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            "PRAGMA table_info(achievement_records);";

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(
                    reader.GetString(1),
                    "category_id",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// v13 增加持续日期任务标记。
    /// 旧任务默认关闭，原有日期、提醒、循环和完成状态均保持不变。
    /// </summary>
    private static async Task
        MigrateVersionTwelveToThirteenAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            await ExecuteStatementAsync(
                connection,
                transaction,
                """
                ALTER TABLE tasks
                ADD COLUMN is_continuous INTEGER
                    NOT NULL
                    DEFAULT 0
                    CHECK (is_continuous IN (0, 1));
                """,
                cancellationToken);

            await SetUserVersionAsync(
                connection,
                transaction,
                13,
                cancellationToken);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task
        EnsurePerformanceIndexesAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<string> statements =
        [
            """
            CREATE INDEX IF NOT EXISTS
                idx_tasks_calendar_due
            ON tasks
            (
                is_deleted,
                due_local_at
            );
            """,

            """
            CREATE INDEX IF NOT EXISTS
                idx_tasks_due_reminders
            ON tasks
            (
                is_deleted,
                status,
                reminder_enabled,
                has_due_time,
                reminder_delivered_at,
                due_local_at,
                reminder_minutes_before
            );
            """,

            """
            CREATE INDEX IF NOT EXISTS
                idx_tasks_deleted_at
            ON tasks
            (
                is_deleted,
                deleted_at
            );
            """
        ];

        foreach (string statement in statements)
        {
            using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
                statement;

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }
}
