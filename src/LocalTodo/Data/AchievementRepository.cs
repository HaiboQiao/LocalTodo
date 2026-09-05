using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Models;
using Microsoft.Data.Sqlite;

namespace LocalTodo.Data;

/// <summary>
/// 成果记录的数据访问层。
/// </summary>
public sealed class AchievementRepository
{
    private const string DateFormat =
        "yyyy-MM-dd";

    private readonly SqliteConnectionFactory
        _connectionFactory;

    public AchievementRepository(
        SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory =
            connectionFactory;
    }

    public async Task<IReadOnlyList<AchievementRecord>>
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
                achievement.id,
                achievement.title,
                achievement.details,
                achievement.category,
                achievement.category_key,
                achievement.category_id,
                COALESCE(category.name, '其他'),
                COALESCE(category.color_hex, '#7C8598'),
                achievement.cycle,
                achievement.status,
                achievement.progress_percent,
                achievement.period_start,
                achievement.period_end,
                achievement.completed_on,
                achievement.created_at,
                achievement.updated_at
            FROM achievement_records AS achievement
            LEFT JOIN achievement_categories AS category
                ON category.id = achievement.category_id
            ORDER BY
                COALESCE(
                    achievement.completed_on,
                    achievement.period_end,
                    achievement.period_start) DESC,
                achievement.updated_at DESC;
            """;

        List<AchievementRecord> records = [];

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            records.Add(
                new AchievementRecord
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Details = reader.GetString(2),
                    Category =
                        ParseCategory(reader.GetString(4)),
                    CategoryId = reader.IsDBNull(5)
                        ? AchievementCategoryDefinition.OtherCategoryId
                        : reader.GetString(5),
                    CategoryName = reader.GetString(6),
                    CategoryColor = reader.GetString(7),
                    Cycle =
                        (AchievementCycle)reader.GetInt32(8),
                    Status =
                        (AchievementStatus)reader.GetInt32(9),
                    ProgressPercent = reader.GetInt32(10),
                    PeriodStart = ParseDate(
                        reader.GetString(11)),
                    PeriodEnd = reader.IsDBNull(12)
                        ? null
                        : ParseDate(reader.GetString(12)),
                    CompletedOn = reader.IsDBNull(13)
                        ? null
                        : ParseDate(reader.GetString(13)),
                    CreatedAt = ParseTimestamp(
                        reader.GetString(14)),
                    UpdatedAt = ParseTimestamp(
                        reader.GetString(15))
                });
        }

        return records;
    }

    public async Task UpsertAsync(
        AchievementRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using SqliteConnection connection =
            await _connectionFactory.OpenConnectionAsync(
                cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO achievement_records
            (
                id,
                title,
                details,
                category,
                category_key,
                category_id,
                cycle,
                status,
                progress_percent,
                period_start,
                period_end,
                completed_on,
                created_at,
                updated_at
            )
            VALUES
            (
                $id,
                $title,
                $details,
                $category,
                $categoryKey,
                $categoryId,
                $cycle,
                $status,
                $progressPercent,
                $periodStart,
                $periodEnd,
                $completedOn,
                $createdAt,
                $updatedAt
            )
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                details = excluded.details,
                category = excluded.category,
                category_key = excluded.category_key,
                category_id = excluded.category_id,
                cycle = excluded.cycle,
                status = excluded.status,
                progress_percent = excluded.progress_percent,
                period_start = excluded.period_start,
                period_end = excluded.period_end,
                completed_on = excluded.completed_on,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$details", record.Details);
        command.Parameters.AddWithValue(
            "$category",
            GetLegacyCategoryValue(record.Category));
        command.Parameters.AddWithValue(
            "$categoryKey",
            record.Category.ToString());
        command.Parameters.AddWithValue(
            "$categoryId",
            string.IsNullOrWhiteSpace(record.CategoryId)
                ? AchievementCategoryDefinition.OtherCategoryId
                : record.CategoryId);
        command.Parameters.AddWithValue(
            "$cycle",
            (int)record.Cycle);
        command.Parameters.AddWithValue(
            "$status",
            (int)record.Status);
        command.Parameters.AddWithValue(
            "$progressPercent",
            record.ProgressPercent);
        command.Parameters.AddWithValue(
            "$periodStart",
            FormatDate(record.PeriodStart));
        command.Parameters.AddWithValue(
            "$periodEnd",
            record.PeriodEnd.HasValue
                ? FormatDate(record.PeriodEnd.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$completedOn",
            record.CompletedOn.HasValue
                ? FormatDate(record.CompletedOn.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$createdAt",
            FormatTimestamp(record.CreatedAt));
        command.Parameters.AddWithValue(
            "$updatedAt",
            FormatTimestamp(record.UpdatedAt));

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
            DELETE FROM achievement_records
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static string FormatDate(
        DateTime value) =>
        value.Date.ToString(
            DateFormat,
            CultureInfo.InvariantCulture);

    private static DateTime ParseDate(
        string value) =>
        DateTime.ParseExact(
            value,
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None);

    private static string FormatTimestamp(
        DateTimeOffset value) =>
        value.ToString(
            "O",
            CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(
        string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static AchievementCategory ParseCategory(
        string value) =>
        Enum.TryParse(
            value,
            ignoreCase: true,
            out AchievementCategory category) &&
        Enum.IsDefined(category)
            ? category
            : AchievementCategory.Other;

    private static int GetLegacyCategoryValue(
        AchievementCategory category) =>
        category switch
        {
            AchievementCategory.Work => 1,
            AchievementCategory.Learning => 2,
            AchievementCategory.Health => 3,
            AchievementCategory.Life => 4,
            AchievementCategory.Project => 5,
            _ => 0
        };
}
