using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Models;
using Microsoft.Data.Sqlite;

namespace LocalTodo.Data;

/// <summary>
/// 每周固定时间安排的数据访问层。
/// </summary>
public sealed class WeeklyPlanRepository
{
    private readonly SqliteConnectionFactory
        _connectionFactory;

    public WeeklyPlanRepository(
        SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory =
            connectionFactory;
    }

    public async Task<IReadOnlyList<WeeklyPlanItem>>
        GetAllAsync(
            CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory.OpenConnectionAsync(
                cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                id,
                day_of_week,
                start_minutes,
                end_minutes,
                title,
                description,
                color_key,
                created_at,
                updated_at
            FROM weekly_plan_items
            ORDER BY
                day_of_week,
                start_minutes,
                end_minutes,
                title;
            """;

        List<WeeklyPlanItem> items = [];

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            items.Add(
                new WeeklyPlanItem
                {
                    Id = reader.GetString(0),
                    Day = (WeeklyDay)reader.GetInt32(1),
                    StartMinutes = reader.GetInt32(2),
                    EndMinutes = reader.GetInt32(3),
                    Title = reader.GetString(4),
                    Description = reader.GetString(5),
                    Color = WeeklyPlanColorStorage.FromStorageKey(
                        reader.GetString(6)),
                    CreatedAt = ParseTimestamp(
                        reader.GetString(7)),
                    UpdatedAt = ParseTimestamp(
                        reader.GetString(8))
                });
        }

        return items;
    }

    public async Task UpsertAsync(
        WeeklyPlanItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await using SqliteConnection connection =
            await _connectionFactory.OpenConnectionAsync(
                cancellationToken);

        using SqliteCommand command =
            CreateUpsertCommand(
                connection,
                transaction: null,
                item);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task DeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory.OpenConnectionAsync(
                cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            DELETE FROM weekly_plan_items
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue(
            "$id",
            id);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static DateTimeOffset ParseTimestamp(
        string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static SqliteCommand CreateUpsertCommand(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        WeeklyPlanItem item)
    {
        SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO weekly_plan_items
            (
                id,
                day_of_week,
                start_minutes,
                end_minutes,
                title,
                description,
                color_key,
                created_at,
                updated_at
            )
            VALUES
            (
                $id,
                $day,
                $startMinutes,
                $endMinutes,
                $title,
                $description,
                $colorKey,
                $createdAt,
                $updatedAt
            )
            ON CONFLICT(id) DO UPDATE SET
                day_of_week = excluded.day_of_week,
                start_minutes = excluded.start_minutes,
                end_minutes = excluded.end_minutes,
                title = excluded.title,
                description = excluded.description,
                color_key = excluded.color_key,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue(
            "$id",
            item.Id);
        command.Parameters.AddWithValue(
            "$day",
            (int)item.Day);
        command.Parameters.AddWithValue(
            "$startMinutes",
            item.StartMinutes);
        command.Parameters.AddWithValue(
            "$endMinutes",
            item.EndMinutes);
        command.Parameters.AddWithValue(
            "$title",
            item.Title);
        command.Parameters.AddWithValue(
            "$description",
            item.Description);
        command.Parameters.AddWithValue(
            "$colorKey",
            item.Color.ToStorageKey());
        command.Parameters.AddWithValue(
            "$createdAt",
            item.CreatedAt.ToString(
                "O",
                CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$updatedAt",
            item.UpdatedAt.ToString(
                "O",
                CultureInfo.InvariantCulture));

        return command;
    }
}
