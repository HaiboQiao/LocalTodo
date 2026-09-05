using System;
using System.IO;

namespace LocalTodo.Helpers;

public static class AppPaths
{
    private const string SolutionFileName = "LocalTodo.sln";
    private const string AlternativeSolutionFileName = "LocalTodo.slnx";

    public static string RootDirectory { get; } =
        ResolveRootDirectory();

    public static string DataDirectory =>
        Path.Combine(RootDirectory, "Data");

    public static string LogDirectory =>
        Path.Combine(RootDirectory, "Logs");

    public static string BackupDirectory =>
        Path.Combine(RootDirectory, "Backups");

    public static string DatabaseFile =>
        Path.Combine(DataDirectory, "localtodo.db");

    /// <summary>
    /// 已通过校验、等待下次完整启动时应用的恢复副本。
    ///
    /// 文件仍位于程序根目录下的 Data 文件夹，
    /// 不会写入 AppData 或其他系统目录。
    /// </summary>
    public static string PendingRestoreFile =>
        Path.Combine(
            DataDirectory,
            "localtodo.pending-restore.db");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }

    private static string ResolveRootDirectory()
    {
#if DEBUG
        string? solutionDirectory = FindSolutionDirectory();

        if (!string.IsNullOrWhiteSpace(solutionDirectory))
        {
            return solutionDirectory;
        }
#endif

        return AppContext.BaseDirectory;
    }

    private static string? FindSolutionDirectory()
    {
        DirectoryInfo? currentDirectory =
            new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            string slnFile = Path.Combine(
                currentDirectory.FullName,
                SolutionFileName);

            string slnxFile = Path.Combine(
                currentDirectory.FullName,
                AlternativeSolutionFileName);

            if (File.Exists(slnFile) || File.Exists(slnxFile))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }
}
