using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Data;

namespace LocalTodo.Services;

/// <summary>
/// 保存和读取桌面小组件的启用状态。
///
/// 这里记录的是用户偏好：
///
/// DesktopTaskList = true
/// 表示用户希望桌面任务列表保持启用，
/// 下次启动程序时自动显示。
///
/// Matrix = true
/// 表示用户希望桌面四象限保持启用，
/// 下次启动程序时自动显示。
///
/// 这和窗口的位置、尺寸是两个不同概念。
/// </summary>
public sealed class DesktopWidgetStateService
{
    private const string
        DesktopTaskListEnabledKey =
            "DesktopWidget.TaskList.Enabled";

    private const string
        MatrixEnabledKey =
            "DesktopWidget.Matrix.Enabled";

    private readonly AppSettingRepository
        _appSettingRepository;

    private bool
        _isLoaded;

    public bool IsDesktopTaskListEnabled
    {
        get;
        private set;
    }

    public bool IsMatrixEnabled
    {
        get;
        private set;
    }

    /// <summary>
    /// 任意桌面小组件启用状态变化时触发。
    ///
    /// 主窗口通过这个事件同步按钮外观。
    /// </summary>
    public event EventHandler?
        StateChanged;

    public DesktopWidgetStateService(
        AppSettingRepository appSettingRepository)
    {
        ArgumentNullException.ThrowIfNull(
            appSettingRepository);

        _appSettingRepository =
            appSettingRepository;
    }

    /// <summary>
    /// 第一次使用前从 app_settings 读取状态。
    ///
    /// 设置不存在时默认 false，
    /// 即第一次安装 LocalTodo 时
    /// 两个桌面小组件都不会自动打开。
    /// </summary>
    public async Task LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isLoaded)
        {
            return;
        }

        string? desktopTaskListValue =
            await _appSettingRepository
                .GetValueAsync(
                    DesktopTaskListEnabledKey,
                    cancellationToken);

        string? matrixValue =
            await _appSettingRepository
                .GetValueAsync(
                    MatrixEnabledKey,
                    cancellationToken);

        IsDesktopTaskListEnabled =
            ParseBoolean(
                desktopTaskListValue);

        IsMatrixEnabled =
            ParseBoolean(
                matrixValue);

        _isLoaded =
            true;

        StateChanged?.Invoke(
            this,
            EventArgs.Empty);
    }

    /// <summary>
    /// 修改桌面任务列表的启用状态。
    /// </summary>
    public async Task
        SetDesktopTaskListEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken = default)
    {
        await LoadAsync(
            cancellationToken);

        if (IsDesktopTaskListEnabled ==
            enabled)
        {
            return;
        }

        await _appSettingRepository
            .SetValuesAsync(
                new Dictionary<string, string>
                {
                    [DesktopTaskListEnabledKey] =
                        enabled.ToString()
                },
                cancellationToken);

        IsDesktopTaskListEnabled =
            enabled;

        StateChanged?.Invoke(
            this,
            EventArgs.Empty);
    }

    /// <summary>
    /// 修改桌面四象限的启用状态。
    /// </summary>
    public async Task
        SetMatrixEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken = default)
    {
        await LoadAsync(
            cancellationToken);

        if (IsMatrixEnabled ==
            enabled)
        {
            return;
        }

        await _appSettingRepository
            .SetValuesAsync(
                new Dictionary<string, string>
                {
                    [MatrixEnabledKey] =
                        enabled.ToString()
                },
                cancellationToken);

        IsMatrixEnabled =
            enabled;

        StateChanged?.Invoke(
            this,
            EventArgs.Empty);
    }

    private static bool ParseBoolean(
        string? value)
    {
        return bool.TryParse(
                   value,
                   out bool parsedValue) &&
               parsedValue;
    }
}
