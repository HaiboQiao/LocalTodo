using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LocalTodo.Helpers;

/// <summary>
/// 清理 LocalTodo 自己生成的按日日志。
///
/// 默认同时限制保存天数和总大小，且始终保留当天日志。
/// </summary>
internal static class LogRetentionPolicy
{
    public const int DefaultRetentionDays =
        30;

    public const long DefaultMaximumTotalBytes =
        20L * 1024L * 1024L;

    public static void Apply(
        string logDirectory,
        DateTime localNow,
        int retentionDays = DefaultRetentionDays,
        long maximumTotalBytes = DefaultMaximumTotalBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            logDirectory);

        if (retentionDays < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionDays));
        }

        if (maximumTotalBytes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumTotalBytes));
        }

        if (!Directory.Exists(logDirectory))
        {
            return;
        }

        DateTime firstRetainedDate =
            localNow.Date.AddDays(
                -(retentionDays - 1));

        string currentLogFileName =
            $"LocalTodo-{localNow:yyyy-MM-dd}.log";

        List<LogFile> retainedFiles =
            [];

        foreach (string file in Directory.EnumerateFiles(
                     logDirectory,
                     "LocalTodo-*.log",
                     SearchOption.TopDirectoryOnly))
        {
            string fileName =
                Path.GetFileName(file);

            bool isCurrentLog =
                string.Equals(
                    fileName,
                    currentLogFileName,
                    StringComparison.OrdinalIgnoreCase);

            if (!TryGetLogDate(
                    fileName,
                    out DateTime logDate))
            {
                continue;
            }

            if (!isCurrentLog &&
                logDate < firstRetainedDate)
            {
                File.Delete(file);
                continue;
            }

            retainedFiles.Add(
                new LogFile(
                    file,
                    logDate,
                    new FileInfo(file).Length,
                    isCurrentLog));
        }

        long totalBytes =
            retainedFiles.Sum(
                file => file.Length);

        foreach (LogFile file in retainedFiles
                     .Where(file => !file.IsCurrent)
                     .OrderBy(file => file.Date))
        {
            if (totalBytes <= maximumTotalBytes)
            {
                break;
            }

            File.Delete(file.Path);
            totalBytes -= file.Length;
        }
    }

    private static bool TryGetLogDate(
        string fileName,
        out DateTime logDate)
    {
        const string prefix =
            "LocalTodo-";

        const string suffix =
            ".log";

        logDate =
            default;

        if (!fileName.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(
                suffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string dateText =
            fileName[
                prefix.Length..
                ^suffix.Length];

        return DateTime.TryParseExact(
            dateText,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out logDate);
    }

    private sealed record LogFile(
        string Path,
        DateTime Date,
        long Length,
        bool IsCurrent);
}
