using System;
using Microsoft.Win32;

namespace LocalTodo.Services;

/// <summary>
/// 管理 LocalTodo 的 Windows 当前用户开机自启动。
///
/// 实现方式：
///
/// HKEY_CURRENT_USER
/// \Software
/// \Microsoft
/// \Windows
/// \CurrentVersion
/// \Run
///
/// 开启时写入：
///
/// LocalTodo = "LocalTodo.exe 的完整路径"
///
/// 关闭时删除 LocalTodo 项。
/// </summary>
public sealed class StartupService
{
    /// <summary>
    /// Windows 当前用户启动项注册表路径。
    /// </summary>
    private const string
        RunRegistryKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// LocalTodo 在 Windows 启动项中的名称。
    /// </summary>
    private const string
        StartupValueName =
            "LocalTodo";

    /// <summary>
    /// Windows 开机自启动 LocalTodo 时使用的
    /// 专用命令行参数。
    ///
    /// 普通用户手动启动 LocalTodo.exe 时
    /// 不包含这个参数，因此仍然正常打开主窗口。
    /// </summary>
    public const string
        StartupArgument =
            "--startup";

    /// <summary>
    /// 判断当前这份 LocalTodo
    /// 是否已经正确注册为开机自启动。
    ///
    /// 不仅检查有没有 LocalTodo 项，
    /// 还会检查里面记录的 exe 路径
    /// 是否就是当前运行的 LocalTodo.exe。
    ///
    /// 这样以后程序安装目录发生改变时，
    /// 不会错误显示为“已开启”。
    /// </summary>
    public bool IsEnabled()
    {
        string expectedCommand =
            BuildStartupCommand();

        using RegistryKey? runKey =
            Registry.CurrentUser
                .OpenSubKey(
                    RunRegistryKeyPath,
                    writable: false);

        if (runKey is null)
        {
            return false;
        }

        string? registeredCommand =
            runKey.GetValue(
                StartupValueName)
            as string;

        if (string.IsNullOrWhiteSpace(
                registeredCommand))
        {
            return false;
        }

        return string.Equals(
            registeredCommand.Trim(),
            expectedCommand,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 开启 LocalTodo 开机自启动。
    ///
    /// 如果以前已经存在 LocalTodo 项，
    /// 会直接更新成当前正在运行的 exe 路径。
    /// </summary>
    public void Enable()
    {
        string startupCommand =
            BuildStartupCommand();

        using RegistryKey runKey =
            Registry.CurrentUser
                .CreateSubKey(
                    RunRegistryKeyPath,
                    writable: true)
            ?? throw new InvalidOperationException(
                "无法打开 Windows 当前用户启动项注册表。");

        runKey.SetValue(
            StartupValueName,
            startupCommand,
            RegistryValueKind.String);
    }

    /// <summary>
    /// 关闭 LocalTodo 开机自启动。
    ///
    /// 如果注册表里本来没有 LocalTodo，
    /// 也不会报错。
    /// </summary>
    public void Disable()
    {
        using RegistryKey? runKey =
            Registry.CurrentUser
                .OpenSubKey(
                    RunRegistryKeyPath,
                    writable: true);

        runKey?.DeleteValue(
            StartupValueName,
            throwOnMissingValue: false);
    }

    /// <summary>
    /// 获取当前 LocalTodo.exe 的完整启动命令。
    ///
    /// 使用双引号包围路径，
    /// 避免安装目录包含空格时启动失败。
    /// </summary>
    private static string
        BuildStartupCommand()
    {
        string? executablePath =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(
                executablePath))
        {
            throw new InvalidOperationException(
                "无法获取当前 LocalTodo.exe 的程序路径。");
        }

        return
            $"\"{executablePath}\" " +
            StartupArgument;
    }

    /// <summary>
    /// 如果当前用户保存的是旧版本的
    /// LocalTodo 开机启动命令：
    ///
    /// "LocalTodo.exe"
    ///
    /// 则自动升级成：
    ///
    /// "LocalTodo.exe" --startup
    ///
    /// 只升级“当前这一个 exe 的旧格式”，
    /// 不修改其他程序或其他 LocalTodo 路径。
    /// </summary>
    public void
        UpgradeLegacyRegistrationIfNeeded()
    {
        string? executablePath =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(
                executablePath))
        {
            return;
        }

        string legacyCommand =
            $"\"{executablePath}\"";

        string currentCommand =
            BuildStartupCommand();

        using RegistryKey? runKey =
            Registry.CurrentUser
                .OpenSubKey(
                    RunRegistryKeyPath,
                    writable: true);

        if (runKey is null)
        {
            return;
        }

        string? registeredCommand =
            runKey.GetValue(
                StartupValueName)
            as string;

        if (string.IsNullOrWhiteSpace(
                registeredCommand))
        {
            return;
        }

        /*
         * 只有注册表当前保存的正好是：
         *
         * "当前 LocalTodo.exe"
         *
         * 才认为这是旧版本 LocalTodo
         * 自己留下来的启动项。
         *
         * 不碰其他路径和其他未知内容。
         */
        if (!string.Equals(
                registeredCommand.Trim(),
                legacyCommand,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        runKey.SetValue(
            StartupValueName,
            currentCommand,
            RegistryValueKind.String);
    }
}
