using System;
using System.IO;
using System.Text;
using LocalTodo.Services;

namespace LocalTodo.Helpers;

public static class AppLog
{
    private static readonly object SyncRoot = new();

    private static bool
        _retentionApplied;

    public static void Information(string message)
    {
        Write("INFO", message, null);
    }

    public static void Error(
        string message,
        Exception exception)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(
        string level,
        string message,
        Exception? exception)
    {
        try
        {
            AppPaths.EnsureDirectories();

            DateTime localNow =
                LocalTimeService.System
                    .ToLocalDateTime(
                        SystemClock.Instance.UtcNow);

            string logFile = Path.Combine(
                AppPaths.LogDirectory,
                $"LocalTodo-{localNow:yyyy-MM-dd}.log");

            StringBuilder builder = new();

            builder.Append(
                $"{localNow:yyyy-MM-dd HH:mm:ss.fff} ");

            builder.Append($"[{level}] ");
            builder.AppendLine(message);

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (SyncRoot)
            {
                if (!_retentionApplied)
                {
                    try
                    {
                        LogRetentionPolicy.Apply(
                            AppPaths.LogDirectory,
                            localNow);
                    }
                    catch
                    {
                        // 旧日志无法清理时仍必须写入本次日志。
                    }
                    finally
                    {
                        _retentionApplied =
                            true;
                    }
                }

                File.AppendAllText(
                    logFile,
                    builder.ToString(),
                    Encoding.UTF8);
            }
        }
        catch
        {
            // 日志写入失败不能阻止主程序启动。
        }
    }
}
