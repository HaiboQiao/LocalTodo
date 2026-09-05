using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Services;
using Microsoft.Data.Sqlite;

namespace LocalTodo.Data;

/// <summary>
/// 负责读取和保存 app_settings 表。
/// </summary>
public sealed class AppSettingRepository
{
    private readonly SqliteConnectionFactory
        _connectionFactory;

    private readonly IClock
        _clock;

    public AppSettingRepository(
        SqliteConnectionFactory connectionFactory,
        IClock? clock = null)
    {
        _connectionFactory =
            connectionFactory;

        _clock =
            clock ??
            SystemClock.Instance;
    }

    /// <summary>
    /// 根据设置键读取字符串值。
    /// 设置不存在时返回 null。
    /// </summary>
    public async Task<string?> GetValueAsync(
        string settingKey,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT setting_value
            FROM app_settings
            WHERE setting_key = $settingKey
            LIMIT 1;
            """;

        await using SqliteConnection connection =
            await _connectionFactory.OpenConnectionAsync(
                cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText = sql;

        command.Parameters.AddWithValue(
            "$settingKey",
            settingKey);

        object? result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is null ||
               result == DBNull.Value
            ? null
            : result.ToString();
    }

    /// <summary>
    /// 使用一个数据库连接批量读取多个设置。
    /// 不存在的键不会出现在返回字典中。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>>
        GetValuesAsync(
            IEnumerable<string> settingKeys,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            settingKeys);

        List<string> keys =
            settingKeys
                .Where(
                    key =>
                        !string.IsNullOrWhiteSpace(
                            key))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        Dictionary<string, string> values =
            new(StringComparer.OrdinalIgnoreCase);

        if (keys.Count == 0)
        {
            return values;
        }

        await using SqliteConnection connection =
            await _connectionFactory.OpenConnectionAsync(
                cancellationToken);

        using SqliteCommand command =
            connection.CreateCommand();

        List<string> parameterNames =
            new(keys.Count);

        for (int index = 0;
             index < keys.Count;
             index++)
        {
            string parameterName =
                $"$key{index}";

            parameterNames.Add(
                parameterName);

            command.Parameters.AddWithValue(
                parameterName,
                keys[index]);
        }

        command.CommandText =
            $"""
            SELECT
                setting_key,
                setting_value
            FROM app_settings
            WHERE setting_key IN
                ({string.Join(", ", parameterNames)});
            """;

        using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            values[reader.GetString(0)] =
                reader.GetString(1);
        }

        return values;
    }

    /// <summary>
    /// 批量写入应用设置。
    ///
    /// 设置不存在时新增；
    /// 设置已存在时更新。
    /// </summary>
    public async Task SetValuesAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        if (values.Count == 0)
        {
            return;
        }

        await using SqliteConnection connection =
            await _connectionFactory.OpenConnectionAsync(
                cancellationToken);

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        try
        {
            string updatedAt =
                _clock.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture);

            foreach (
                KeyValuePair<string, string> setting
                in values)
            {
                using SqliteCommand command =
                    connection.CreateCommand();

                command.Transaction =
                    transaction;

                command.CommandText =
                    """
                    INSERT INTO app_settings
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
                    )
                    ON CONFLICT(setting_key)
                    DO UPDATE SET
                        setting_value =
                            excluded.setting_value,

                        updated_at =
                            excluded.updated_at;
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

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
