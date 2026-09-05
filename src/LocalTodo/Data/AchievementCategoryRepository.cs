using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Models;
using Microsoft.Data.Sqlite;

namespace LocalTodo.Data;

/// <summary>
/// 成长成果分类的数据访问层。
/// </summary>
public sealed class AchievementCategoryRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public AchievementCategoryRepository(
        SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AchievementCategoryDefinition>>
        GetAllAsync(
            CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory.OpenConnectionAsync(
                cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                name,
                color_hex,
                sort_order,
                is_builtin
            FROM achievement_categories
            ORDER BY sort_order, name COLLATE NOCASE;
            """;

        List<AchievementCategoryDefinition> result = [];
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                new AchievementCategoryDefinition
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    ColorHex = reader.GetString(2),
                    SortOrder = reader.GetInt32(3),
                    IsBuiltIn = reader.GetInt32(4) != 0
                });
        }

        return result;
    }

    public async Task UpsertAsync(
        AchievementCategoryDefinition category,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory.OpenConnectionAsync(
                cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO achievement_categories
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
            (
                $id,
                $name,
                $colorHex,
                $sortOrder,
                $isBuiltIn,
                $now,
                $now
            )
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                color_hex = excluded.color_hex,
                sort_order = excluded.sort_order,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", category.Id);
        command.Parameters.AddWithValue("$name", category.Name);
        command.Parameters.AddWithValue("$colorHex", category.ColorHex);
        command.Parameters.AddWithValue("$sortOrder", category.SortOrder);
        command.Parameters.AddWithValue("$isBuiltIn", category.IsBuiltIn ? 1 : 0);
        command.Parameters.AddWithValue(
            "$now",
            now.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 在单个事务内更新全部分类的显示顺序。
    /// </summary>
    public async Task UpdateSortOrdersAsync(
        IReadOnlyList<string> orderedCategoryIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory.OpenConnectionAsync(
                cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        try
        {
            for (int index = 0;
                 index < orderedCategoryIds.Count;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE achievement_categories
                    SET
                        sort_order = $sortOrder,
                        updated_at = $now
                    WHERE id = $id;
                    """;
                command.Parameters.AddWithValue(
                    "$sortOrder",
                    (index + 1) * 10);
                command.Parameters.AddWithValue(
                    "$now",
                    now.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue(
                    "$id",
                    orderedCategoryIds[index]);

                int affected =
                    await command.ExecuteNonQueryAsync(cancellationToken);

                if (affected != 1)
                {
                    throw new InvalidOperationException(
                        "分类列表已发生变化，请重新打开分类管理后再试。");
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 删除分类前，将使用它的成果统一归入“其他”。
    /// 整个过程在同一事务中完成，不会产生孤立成果。
    /// </summary>
    public async Task DeleteAndReassignAsync(
        string categoryId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connectionFactory.OpenConnectionAsync(
                cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        try
        {
            using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText =
                    """
                    UPDATE achievement_records
                    SET
                        category_id = 'builtin-other',
                        category = 0,
                        category_key = 'Other'
                    WHERE category_id = $categoryId;
                    """;
                update.Parameters.AddWithValue("$categoryId", categoryId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText =
                    """
                    DELETE FROM achievement_categories
                    WHERE
                        id = $categoryId
                        AND id <> 'builtin-other';
                    """;
                delete.Parameters.AddWithValue("$categoryId", categoryId);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
