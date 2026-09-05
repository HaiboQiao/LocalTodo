using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Models;
using LocalTodo.Services;
using Microsoft.Data.Sqlite;

namespace LocalTodo.Data;

/// <summary>
/// 负责 tasks 表的数据访问。
/// </summary>
public sealed class TaskRepository
{
    private const string SelectTaskColumnsSql =
        """
        SELECT
            id,
            title,
            description,
            status,
            priority,
            is_important,
            is_continuous,
            due_at,
            due_local_at,
            has_due_time,
            reminder_enabled,
            reminder_minutes_before,
            repeat_type,
            recurrence_series_id,
            recurrence_anchor_month,
            recurrence_anchor_day,
            reminder_delivered_at,
            quadrant_mode,
            manual_quadrant,
            created_at,
            updated_at,
            completed_at,
            deleted_at,
            revision
        FROM tasks
        """;

    private const string InsertTaskSql =
        """
        INSERT INTO tasks
        (
            id,
            title,
            description,
            status,
            priority,
            is_important,
            is_continuous,
            due_at,
            due_local_at,
            has_due_time,
            reminder_enabled,
            reminder_minutes_before,
            repeat_type,
            recurrence_series_id,
            recurrence_anchor_month,
            recurrence_anchor_day,
            reminder_delivered_at,
            quadrant_mode,
            manual_quadrant,
            created_at,
            updated_at,
            completed_at,
            deleted_at,
            revision
        )
        VALUES
        (
            $id,
            $title,
            $description,
            $status,
            $priority,
            $isImportant,
            $isContinuous,
            $dueAt,
            $dueLocalAt,
            $hasDueTime,
            $reminderEnabled,
            $reminderMinutesBefore,
            $repeatType,
            $recurrenceSeriesId,
            $recurrenceAnchorMonth,
            $recurrenceAnchorDay,
            $reminderDeliveredAt,
            $quadrantMode,
            $manualQuadrant,
            $createdAt,
            $updatedAt,
            $completedAt,
            $deletedAt,
            $revision
        );
        """;

    private const string UpdateTaskSql =
        """
        UPDATE tasks
        SET
            title = $title,
            description = $description,
            status = $status,
            priority = $priority,
            is_important = $isImportant,
            is_continuous = $isContinuous,
            due_at = $dueAt,
            due_local_at = $dueLocalAt,
            has_due_time = $hasDueTime,
            reminder_enabled = $reminderEnabled,
            reminder_minutes_before =
                $reminderMinutesBefore,
            repeat_type = $repeatType,
            recurrence_series_id = $recurrenceSeriesId,
            recurrence_anchor_month = $recurrenceAnchorMonth,
            recurrence_anchor_day = $recurrenceAnchorDay,
            reminder_delivered_at = $reminderDeliveredAt,
            quadrant_mode = $quadrantMode,
            manual_quadrant = $manualQuadrant,
            updated_at = $updatedAt,
            completed_at = $completedAt,
            revision = revision + 1
        WHERE
            id = $id
            AND is_deleted = 0
            AND revision = $expectedRevision;
        """;

    private const string SoftDeleteTaskSql =
        """
        UPDATE tasks
        SET
            is_deleted = 1,
            deleted_at = $updatedAt,
            updated_at = $updatedAt,
            revision = revision + 1
        WHERE
            id = $id
            AND is_deleted = 0
            AND revision = $expectedRevision;
        """;

    private readonly SqliteConnectionFactory
        _connectionFactory;

    private readonly ILocalTimeService
        _localTimeService;

    public TaskRepository(
        SqliteConnectionFactory connectionFactory,
        ILocalTimeService? localTimeService = null)
    {
        _connectionFactory =
            connectionFactory;

        _localTimeService =
            localTimeService ??
            LocalTimeService.System;
    }

    /// <summary>
    /// 根据完成状态读取未删除任务。
    /// </summary>
    public async Task<IReadOnlyList<TaskItem>>
        GetTasksAsync(
            TodoStatus status,
            CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            SelectTaskColumnsSql +
            Environment.NewLine +
            """
            WHERE
                is_deleted = 0
                AND status = $status
            """ +
            Environment.NewLine +
            $"ORDER BY {GetOrderBySql(status)};";

        command.Parameters.AddWithValue(
            "$status",
            (int)status);

        return await ReadTasksAsync(
            command,
            cancellationToken);
    }

    /// <summary>
    /// 读取垃圾箱中的所有软删除任务。
    /// </summary>
    public async Task<IReadOnlyList<TaskItem>>
        GetDeletedTasksAsync(
            CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            SelectTaskColumnsSql +
            Environment.NewLine +
            """
            WHERE is_deleted = 1
            ORDER BY
                deleted_at DESC,
                created_at DESC;
            """;

        return await ReadTasksAsync(
            command,
            cancellationToken);
    }

    /// <summary>
    /// 读取指定日期范围内、未删除且具有截止日期的任务。
    ///
    /// 范围使用左闭右开：
    /// startLocal <= due_local_at < endLocal。
    /// </summary>
    public async Task<IReadOnlyList<TaskItem>>
        GetTasksByDueRangeAsync(
            DateTimeOffset startUtc,
            DateTimeOffset endUtc,
            CancellationToken cancellationToken = default)
    {
        if (endUtc <= startUtc)
        {
            throw new ArgumentException(
                "日历任务查询的结束时间必须晚于开始时间。");
        }

        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            SelectTaskColumnsSql +
            Environment.NewLine +
            """
        WHERE
            is_deleted = 0
            AND due_local_at IS NOT NULL
            AND due_local_at >= $startLocal
            AND due_local_at < $endLocal
        ORDER BY
            due_local_at ASC,
            status ASC,
            is_important DESC,
            created_at ASC;
        """;

        command.Parameters.AddWithValue(
            "$startLocal",
            FormatLocalDateTime(
                _localTimeService
                    .ToLocalDateTime(
                        startUtc)));

        command.Parameters.AddWithValue(
            "$endLocal",
            FormatLocalDateTime(
                _localTimeService
                    .ToLocalDateTime(
                        endUtc)));

        return await ReadTasksAsync(
            command,
            cancellationToken);
    }

    /// <summary>
    /// 只读取当前真正到期、尚未投递的提醒任务。
    /// 提醒时间等于截止时间减去提前提醒分钟数。
    /// </summary>
    public async Task<IReadOnlyList<TaskItem>>
        GetDueRemindersAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            SelectTaskColumnsSql +
            Environment.NewLine +
            """
            WHERE
                is_deleted = 0
                AND status = 0
                AND reminder_enabled = 1
                AND has_due_time = 1
                AND reminder_delivered_at IS NULL
                AND due_local_at IS NOT NULL
                AND julianday(due_local_at) -
                    (reminder_minutes_before / 1440.0)
                    <= julianday($nowLocal) + 1.0
            ORDER BY
                due_local_at ASC,
                created_at ASC;
            """;

        command.Parameters.AddWithValue(
            "$nowLocal",
            FormatLocalDateTime(
                _localTimeService
                    .ToLocalDateTime(
                        now)));

        IReadOnlyList<TaskItem> candidates =
            await ReadTasksAsync(
                command,
                cancellationToken);

        return candidates
            .Where(
                task =>
                    task.DueAt.HasValue &&
                    _localTimeService
                        .ResolveLocalDateTime(
                            LocalDueDateTime.GetWallClock(
                                task.DueAt.Value))
                        .AddMinutes(
                            -task.ReminderMinutesBefore) <=
                    now)
            .ToArray();
    }

    /// <summary>
    /// 按 ID 读取当前未删除任务。
    /// </summary>
    public async Task<TaskItem?> GetActiveTaskByIdAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            SelectTaskColumnsSql +
            Environment.NewLine +
            """
            WHERE
                id = $id
                AND is_deleted = 0;
            """;

        command.Parameters.AddWithValue(
            "$id",
            taskId);

        return await ReadSingleTaskAsync(
            command,
            cancellationToken);
    }

    /// <summary>
    /// 仅更新详情编辑器真正修改的字段，并使用 Revision 做
    /// compare-and-swap。返回 null 表示版本冲突或任务已删除。
    /// </summary>
    public async Task<TaskItem?>
        TryUpdateEditableFieldsAsync(
            TaskItem candidate,
            TaskEditFields changedFields,
            long expectedRevision,
            bool resetReminderDelivery,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (changedFields ==
            TaskEditFields.None)
        {
            throw new ArgumentException(
                "没有需要保存的任务字段。",
                nameof(changedFields));
        }

        List<string> assignments =
            [];

        if (changedFields.HasFlag(
                TaskEditFields.Title))
        {
            assignments.Add(
                "title = $title");
        }

        if (changedFields.HasFlag(
                TaskEditFields.Description))
        {
            assignments.Add(
                "description = $description");
        }

        if (changedFields.HasFlag(
                TaskEditFields.Schedule))
        {
            assignments.Add(
                "is_continuous = $isContinuous");

            assignments.Add(
                "due_at = $dueAt");

            assignments.Add(
                "due_local_at = $dueLocalAt");

            assignments.Add(
                "has_due_time = $hasDueTime");

            assignments.Add(
                "reminder_enabled = $reminderEnabled");

            assignments.Add(
                "reminder_minutes_before = " +
                "$reminderMinutesBefore");

            if (resetReminderDelivery)
            {
                assignments.Add(
                    "reminder_delivered_at = NULL");
            }
        }

        if (changedFields.HasFlag(
                TaskEditFields.Repeat))
        {
            assignments.Add(
                "repeat_type = $repeatType");

            assignments.Add(
                "recurrence_series_id = " +
                "$recurrenceSeriesId");
        }

        if (changedFields.HasFlag(
                TaskEditFields.Schedule) ||
            changedFields.HasFlag(
                TaskEditFields.Repeat))
        {
            assignments.Add(
                "recurrence_anchor_month = " +
                "$recurrenceAnchorMonth");

            assignments.Add(
                "recurrence_anchor_day = " +
                "$recurrenceAnchorDay");
        }

        if (changedFields.HasFlag(
                TaskEditFields.IsImportant))
        {
            assignments.Add(
                "is_important = $isImportant");
        }

        if (changedFields.HasFlag(
                TaskEditFields.Quadrant))
        {
            assignments.Add(
                "priority = $priority");

            assignments.Add(
                "quadrant_mode = $quadrantMode");

            assignments.Add(
                "manual_quadrant = $manualQuadrant");
        }

        assignments.Add(
            "updated_at = $updatedAt");

        assignments.Add(
            "revision = revision + 1");

        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        using (SqliteCommand command =
               connection.CreateCommand())
        {
            command.Transaction =
                transaction;

            command.CommandText =
                "UPDATE tasks" +
                Environment.NewLine +
                "SET" +
                Environment.NewLine +
                "    " +
                string.Join(
                    "," +
                    Environment.NewLine +
                    "    ",
                    assignments) +
                Environment.NewLine +
                "WHERE" +
                Environment.NewLine +
                "    id = $id" +
                Environment.NewLine +
                "    AND is_deleted = 0" +
                Environment.NewLine +
                "    AND revision = $expectedRevision;";

            AddCommonParameters(
                command,
                candidate);

            command.Parameters.AddWithValue(
                "$expectedRevision",
                expectedRevision);

            int affectedRows =
                await command.ExecuteNonQueryAsync(
                    cancellationToken);

            if (affectedRows != 1)
            {
                transaction.Rollback();

                return null;
            }
        }

        TaskItem? persistedTask;

        using (SqliteCommand readCommand =
               connection.CreateCommand())
        {
            readCommand.Transaction =
                transaction;

            readCommand.CommandText =
                SelectTaskColumnsSql +
                Environment.NewLine +
                """
                WHERE
                    id = $id
                    AND is_deleted = 0;
                """;

            readCommand.Parameters.AddWithValue(
                "$id",
                candidate.Id);

            persistedTask =
                await ReadSingleTaskAsync(
                    readCommand,
                    cancellationToken);
        }

        if (persistedTask is null)
        {
            transaction.Rollback();

            throw new InvalidOperationException(
                $"保存后无法重新读取任务：{candidate.Id}");
        }

        transaction.Commit();

        return persistedTask;
    }

    /// <summary>
    /// 新增任务。
    /// </summary>
    public async Task InsertAsync(
        TaskItem task,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        await InsertAsync(
            connection,
            transaction: null,
            task,
            cancellationToken);
    }

    /// <summary>
    /// 保存任务可编辑字段。
    /// </summary>
    public async Task UpdateAsync(
        TaskItem task,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        await UpdateAsync(
            connection,
            transaction: null,
            task,
            cancellationToken);

        task.Revision++;
    }

    /// <summary>
    /// 将任务标记为软删除。
    /// </summary>
    public async Task SoftDeleteAsync(
        TaskItem task,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        await SoftDeleteAsync(
            connection,
            transaction: null,
            task.Id,
            task.Revision,
            updatedAt,
            cancellationToken);

        task.Revision++;

        task.UpdatedAt =
            updatedAt;

        task.DeletedAt =
            updatedAt;
    }

    /// <summary>
    /// 在一个事务中完成当前循环期并创建下一期。
    /// </summary>
    public async Task CompleteRecurringTaskAsync(
        TaskItem completedTask,
        TaskItem nextTask,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        await UpdateAsync(
            connection,
            transaction,
            completedTask,
            cancellationToken);

        await InsertAsync(
            connection,
            transaction,
            nextTask,
            cancellationToken);

        transaction.Commit();

        completedTask.Revision++;
    }

    /// <summary>
    /// 在一个事务中让当前循环期退出系列、进入垃圾箱，
    /// 并在需要时创建下一期。
    /// </summary>
    public async Task DeleteCurrentRecurringOccurrenceAsync(
        TaskItem task,
        DateTimeOffset deletedAt,
        TaskItem? nextTask,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        await UpdateAsync(
            connection,
            transaction,
            task,
            cancellationToken);

        await SoftDeleteAsync(
            connection,
            transaction,
            task.Id,
            task.Revision + 1,
            deletedAt,
            cancellationToken);

        if (nextTask is not null)
        {
            await InsertAsync(
                connection,
                transaction,
                nextTask,
                cancellationToken);
        }

        transaction.Commit();

        task.Revision +=
            2;

        task.DeletedAt =
            deletedAt;
    }

    /// <summary>
    /// 在一个事务中软删除当前活动期及同系列的其他未完成期。
    /// </summary>
    public async Task SoftDeletePendingRecurrenceSeriesAsync(
        TaskItem selectedTask,
        string? recurrenceSeriesId,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        await SoftDeleteAsync(
            connection,
            transaction,
            selectedTask.Id,
            selectedTask.Revision,
            deletedAt,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(
                recurrenceSeriesId))
        {
            using SqliteCommand command =
                connection.CreateCommand();

            command.Transaction =
                transaction;

            command.CommandText =
                """
                UPDATE tasks
                SET
                    is_deleted = 1,
                    deleted_at = $updatedAt,
                    updated_at = $updatedAt,
                    revision = revision + 1
                WHERE
                    id <> $selectedTaskId
                    AND is_deleted = 0
                    AND status = $pendingStatus
                    AND recurrence_series_id =
                        $recurrenceSeriesId;
                """;

            command.Parameters.AddWithValue(
                "$selectedTaskId",
                selectedTask.Id);

            command.Parameters.AddWithValue(
                "$updatedAt",
                FormatDate(deletedAt));

            command.Parameters.AddWithValue(
                "$pendingStatus",
                (int)TodoStatus.Pending);

            command.Parameters.AddWithValue(
                "$recurrenceSeriesId",
                recurrenceSeriesId);

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }

        transaction.Commit();

        selectedTask.Revision++;

        selectedTask.UpdatedAt =
            deletedAt;

        selectedTask.DeletedAt =
            deletedAt;
    }

    /// <summary>
    /// 将垃圾箱中的任务恢复为普通任务。
    ///
    /// 只修改 is_deleted 和 updated_at，原来的完成状态、
    /// 象限、日期、标题和说明全部保留。
    /// </summary>
    public async Task RestoreDeletedAsync(
        TaskItem task,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            UPDATE tasks
            SET
                is_deleted = 0,
                deleted_at = NULL,
                updated_at = $updatedAt,
                revision = revision + 1
            WHERE
                id = $id
                AND is_deleted = 1
                AND revision = $expectedRevision;
            """;

        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            sql;

        command.Parameters.AddWithValue(
            "$id",
            task.Id);

        command.Parameters.AddWithValue(
            "$updatedAt",
            FormatDate(updatedAt));

        command.Parameters.AddWithValue(
            "$expectedRevision",
            task.Revision);

        int affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affectedRows != 1)
        {
            throw new TaskConcurrencyException(
                task.Id);
        }

        task.Revision++;

        task.UpdatedAt =
            updatedAt;

        task.DeletedAt =
            null;
    }

    /// <summary>
    /// 从数据库永久删除垃圾箱任务。
    /// </summary>
    public async Task PermanentlyDeleteAsync(
        TaskItem task,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            DELETE FROM tasks
            WHERE
                id = $id
                AND is_deleted = 1
                AND revision = $expectedRevision;
            """;

        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            sql;

        command.Parameters.AddWithValue(
            "$id",
            task.Id);

        command.Parameters.AddWithValue(
            "$expectedRevision",
            task.Revision);

        int affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affectedRows != 1)
        {
            throw new TaskConcurrencyException(
                task.Id);
        }
    }

    /// <summary>
    /// 标记当前任务的这一期提醒已经发送。
    ///
    /// 不修改 updated_at，
    /// 避免单纯发送提醒导致任务重新排序。
    /// </summary>
    public async Task<bool>
        MarkReminderDeliveredAsync(
            TaskItem task,
            DateTimeOffset deliveredAt,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        const string sql =
            """
        UPDATE tasks
        SET
            reminder_delivered_at =
                $deliveredAt,
            revision = revision + 1
        WHERE
            id = $id
            AND is_deleted = 0
            AND status = 0
            AND reminder_enabled = 1
            AND reminder_delivered_at IS NULL
            AND due_local_at IS $expectedDueLocalAt
            AND has_due_time = $expectedHasDueTime
            AND reminder_minutes_before =
                $expectedReminderMinutesBefore;
        """;

        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            sql;

        command.Parameters.AddWithValue(
            "$id",
            task.Id);

        command.Parameters.AddWithValue(
            "$expectedDueLocalAt",
            task.DueAt.HasValue
                ? LocalDueDateTime.FormatForDatabase(
                    task.DueAt.Value)
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$expectedHasDueTime",
            task.HasDueTime
                ? 1
                : 0);

        command.Parameters.AddWithValue(
            "$expectedReminderMinutesBefore",
            task.ReminderMinutesBefore);

        command.Parameters.AddWithValue(
            "$deliveredAt",
            FormatDate(
                deliveredAt));

        int affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        return affectedRows == 1;
    }

    private async Task<TaskItem?>
        ReadSingleTaskAsync(
            SqliteCommand command,
            CancellationToken cancellationToken)
    {
        using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return ReadTask(reader);
    }

    private async Task<IReadOnlyList<TaskItem>>
        ReadTasksAsync(
            SqliteCommand command,
            CancellationToken cancellationToken)
    {
        using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        List<TaskItem> tasks =
            [];

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            tasks.Add(
                ReadTask(reader));
        }

        return tasks;
    }

    private static string GetOrderBySql(
        TodoStatus status)
    {
        if (status ==
            TodoStatus.Completed)
        {
            return
                """
                completed_at DESC,
                updated_at DESC
                """;
        }

        return
            """
            CASE
                WHEN due_local_at IS NULL THEN 1
                ELSE 0
            END ASC,

            due_local_at ASC,
            is_important DESC,
            priority DESC,
            created_at DESC
            """;
    }

    private async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        TaskItem task,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            InsertTaskSql;

        AddCommonParameters(
            command,
            task);

        command.Parameters.AddWithValue(
            "$createdAt",
            FormatDate(task.CreatedAt));

        command.Parameters.AddWithValue(
            "$revision",
            task.Revision);

        command.Parameters.AddWithValue(
            "$deletedAt",
            ToDatabaseValue(task.DeletedAt));

        int affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "新增任务失败，数据库未写入任务记录。");
        }
    }

    private async Task UpdateAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        TaskItem task,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            UpdateTaskSql;

        AddCommonParameters(
            command,
            task);

        command.Parameters.AddWithValue(
            "$expectedRevision",
            task.Revision);

        int affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affectedRows != 1)
        {
            throw new TaskConcurrencyException(
                    task.Id);
        }
    }

    private static async Task SoftDeleteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string taskId,
        long expectedRevision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            SoftDeleteTaskSql;

        command.Parameters.AddWithValue(
            "$id",
            taskId);

        command.Parameters.AddWithValue(
            "$updatedAt",
            FormatDate(updatedAt));

        command.Parameters.AddWithValue(
            "$expectedRevision",
            expectedRevision);

        int affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affectedRows != 1)
        {
            throw new TaskConcurrencyException(
                    taskId);
        }
    }

    private void AddCommonParameters(
        SqliteCommand command,
        TaskItem task)
    {
        command.Parameters.AddWithValue(
            "$id",
            task.Id);

        command.Parameters.AddWithValue(
            "$title",
            task.Title);

        command.Parameters.AddWithValue(
            "$description",
            task.Description);

        command.Parameters.AddWithValue(
            "$status",
            (int)task.Status);

        command.Parameters.AddWithValue(
            "$priority",
            (int)task.Priority);

        command.Parameters.AddWithValue(
            "$isImportant",
            task.IsImportant ? 1 : 0);

        command.Parameters.AddWithValue(
            "$isContinuous",
            task.IsContinuous ? 1 : 0);

        command.Parameters.AddWithValue(
            "$dueAt",
            ToDueDatabaseValue(task.DueAt));

        command.Parameters.AddWithValue(
            "$dueLocalAt",
            task.DueAt.HasValue
                ? LocalDueDateTime.FormatForDatabase(
                    task.DueAt.Value)
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$hasDueTime",
            task.HasDueTime ? 1 : 0);

        command.Parameters.AddWithValue(
            "$reminderEnabled",
            task.ReminderEnabled ? 1 : 0);

        command.Parameters.AddWithValue(
            "$reminderMinutesBefore",
            task.ReminderMinutesBefore);

        command.Parameters.AddWithValue(
            "$repeatType",
            (int)task.RepeatType);

        command.Parameters.AddWithValue(
            "$recurrenceSeriesId",
            task.RecurrenceSeriesId
                is null
                    ? DBNull.Value
                    : task.RecurrenceSeriesId);

        command.Parameters.AddWithValue(
            "$recurrenceAnchorMonth",
            task.RecurrenceAnchorMonth.HasValue
                ? task.RecurrenceAnchorMonth.Value
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$recurrenceAnchorDay",
            task.RecurrenceAnchorDay.HasValue
                ? task.RecurrenceAnchorDay.Value
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$reminderDeliveredAt",
            ToDatabaseValue(
                task.ReminderDeliveredAt));

        command.Parameters.AddWithValue(
            "$quadrantMode",
            (int)task.QuadrantMode);

        command.Parameters.AddWithValue(
            "$manualQuadrant",
            task.ManualQuadrant.HasValue
                ? (int)task.ManualQuadrant.Value
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$updatedAt",
            FormatDate(task.UpdatedAt));

        command.Parameters.AddWithValue(
            "$completedAt",
            ToDatabaseValue(task.CompletedAt));
    }

    private TaskItem ReadTask(
        SqliteDataReader reader)
    {
        return new TaskItem
        {
            Id =
                GetString(reader, "id"),

            Title =
                GetString(reader, "title"),

            Description =
                GetString(reader, "description"),

            Status =
                (TodoStatus)GetInt32(
                    reader,
                    "status"),

            Priority =
                (TaskPriority)GetInt32(
                    reader,
                    "priority"),

            IsImportant =
                GetInt32(
                    reader,
                    "is_important") == 1,

            IsContinuous =
                GetInt32(
                    reader,
                    "is_continuous") == 1,

            DueAt =
                GetLocalDueAt(reader),

            HasDueTime =
                GetInt32(
                    reader,
                    "has_due_time") == 1,

            ReminderEnabled =
                GetInt32(
                    reader,
                    "reminder_enabled") == 1,

            ReminderMinutesBefore =
                GetInt32(
                    reader,
                    "reminder_minutes_before"),

            RepeatType =
                (TaskRepeatType)GetInt32(
                    reader,
                    "repeat_type"),

            RecurrenceSeriesId =
                GetNullableString(
                    reader,
                    "recurrence_series_id"),

            RecurrenceAnchorMonth =
                GetNullableInt32(
                    reader,
                    "recurrence_anchor_month"),

            RecurrenceAnchorDay =
                GetNullableInt32(
                    reader,
                    "recurrence_anchor_day"),

            ReminderDeliveredAt =
                GetNullableDate(
                    reader,
                    "reminder_delivered_at"),

            QuadrantMode =
                (QuadrantMode)GetInt32(
                    reader,
                    "quadrant_mode"),

            ManualQuadrant =
                GetNullableQuadrant(
                    reader,
                    "manual_quadrant"),

            CreatedAt =
                GetDate(
                    reader,
                    "created_at"),

            UpdatedAt =
                GetDate(
                    reader,
                    "updated_at"),

            CompletedAt =
                GetNullableDate(
                    reader,
                    "completed_at"),

            DeletedAt =
                GetNullableDate(
                    reader,
                    "deleted_at"),

            Revision =
                GetInt64(
                    reader,
                    "revision")
        };
    }

    private static string GetString(
        SqliteDataReader reader,
        string columnName)
    {
        int ordinal =
            reader.GetOrdinal(columnName);

        return reader.GetString(ordinal);
    }

    private static string? GetNullableString(
        SqliteDataReader reader,
        string columnName)
    {
        int ordinal =
            reader.GetOrdinal(columnName);

        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }

    private static int GetInt32(
        SqliteDataReader reader,
        string columnName)
    {
        int ordinal =
            reader.GetOrdinal(columnName);

        return Convert.ToInt32(
            reader.GetValue(ordinal),
            CultureInfo.InvariantCulture);
    }

    private static int? GetNullableInt32(
        SqliteDataReader reader,
        string columnName)
    {
        int ordinal =
            reader.GetOrdinal(columnName);

        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToInt32(
            reader.GetValue(ordinal),
            CultureInfo.InvariantCulture);
    }

    private static long GetInt64(
        SqliteDataReader reader,
        string columnName)
    {
        int ordinal =
            reader.GetOrdinal(columnName);

        return Convert.ToInt64(
            reader.GetValue(ordinal),
            CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset GetDate(
        SqliteDataReader reader,
        string columnName)
    {
        string value =
            GetString(
                reader,
                columnName);

        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    private static DateTimeOffset?
        GetNullableDate(
            SqliteDataReader reader,
            string columnName)
    {
        string? value =
            GetNullableString(
                reader,
                columnName);

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    private DateTimeOffset? GetLocalDueAt(
        SqliteDataReader reader)
    {
        string? localValue =
            GetNullableString(
                reader,
                "due_local_at");

        if (!string.IsNullOrWhiteSpace(
                localValue))
        {
            return _localTimeService
                .ResolveLocalDateTime(
                    LocalDueDateTime
                        .ParseDatabaseValue(
                            localValue));
        }

        DateTimeOffset? legacyInstant =
            GetNullableDate(
                reader,
                "due_at");

        return legacyInstant.HasValue
            ? _localTimeService
                .ResolveLocalDateTime(
                    _localTimeService
                        .ToLocalDateTime(
                            legacyInstant.Value))
            : null;
    }

    private static QuadrantType?
        GetNullableQuadrant(
            SqliteDataReader reader,
            string columnName)
    {
        int ordinal =
            reader.GetOrdinal(columnName);

        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        int value =
            Convert.ToInt32(
                reader.GetValue(ordinal),
                CultureInfo.InvariantCulture);

        return (QuadrantType)value;
    }

    private static string FormatDate(
        DateTimeOffset value)
    {
        return value
            .ToUniversalTime()
            .ToString(
                "O",
                CultureInfo.InvariantCulture);
    }

    private static string FormatLocalDateTime(
        DateTime value)
    {
        return DateTime.SpecifyKind(
                value,
                DateTimeKind.Unspecified)
            .ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fffffff",
                CultureInfo.InvariantCulture);
    }

    private object ToDueDatabaseValue(
        DateTimeOffset? value)
    {
        if (!value.HasValue)
        {
            return DBNull.Value;
        }

        DateTimeOffset resolved =
            _localTimeService
                .ResolveLocalDateTime(
                    LocalDueDateTime.GetWallClock(
                        value.Value));

        return FormatDate(
            resolved);
    }

    private static object ToDatabaseValue(
        DateTimeOffset? value)
    {
        return value.HasValue
            ? FormatDate(value.Value)
            : DBNull.Value;
    }

    /// <summary>
    /// 读取指定循环系列中所有尚未删除的未完成任务。
    ///
    /// 正常情况下应该只有一个，
    /// 但返回列表可以兼容之前出现过的循环链分叉问题。
    /// </summary>
    public async Task<IReadOnlyList<TaskItem>>
        GetPendingTasksByRecurrenceSeriesIdAsync(
            string recurrenceSeriesId,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                recurrenceSeriesId))
        {
            return [];
        }

        await using SqliteConnection connection =
            await _connectionFactory
                .OpenConnectionAsync(
                    cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            SelectTaskColumnsSql +
            Environment.NewLine +
            """
        WHERE
            is_deleted = 0
            AND status = $pendingStatus
            AND recurrence_series_id =
                $recurrenceSeriesId
        ORDER BY
            due_local_at ASC,
            created_at ASC;
        """;

        command.Parameters.AddWithValue(
            "$pendingStatus",
            (int)TodoStatus.Pending);

        command.Parameters.AddWithValue(
            "$recurrenceSeriesId",
            recurrenceSeriesId);

        return await ReadTasksAsync(
            command,
            cancellationToken);
    }
}
