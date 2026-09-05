using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LocalTodo.Data;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// 保存和恢复 LocalTodo 窗口的位置与尺寸。
/// </summary>
public sealed class WindowPlacementService
{
    private const string MainWindowKeyPrefix =
        "Window.Main";

    private const string MatrixWindowKeyPrefix =
        "Window.Matrix";

    private const string
    DesktopTaskListWindowKeyPrefix =
        "Window.DesktopTaskList";

    // 主窗口尺寸，与 MainWindow.xaml 中保持一致。

    private const double MainMinimumWidth =
        1100;

    private const double MainMinimumHeight =
        700;

    private const double MainDefaultWidth =
        1360;

    private const double MainDefaultHeight =
        860;

    // 桌面四象限窗口尺寸。

    private const double MatrixMinimumWidth =
        820;

    private const double MatrixMinimumHeight =
        560;

    private const double MatrixDefaultWidth =
        1040;

    private const double MatrixDefaultHeight =
        720;

    // 桌面任务列表窗口尺寸。

    private const double
        DesktopTaskListMinimumWidth =
            360;

    private const double
        DesktopTaskListMinimumHeight =
            360;

    private const double
        DesktopTaskListDefaultWidth =
            520;

    private const double
        DesktopTaskListDefaultHeight =
            720;

    private readonly AppSettingRepository
        _appSettingRepository;

    public WindowPlacementService(
        AppSettingRepository appSettingRepository)
    {
        _appSettingRepository =
            appSettingRepository;
    }

    /// <summary>
    /// 读取主窗口上次保存的位置与尺寸。
    /// </summary>
    public Task<WindowPlacement>
        LoadMainWindowPlacementAsync(
            CancellationToken cancellationToken = default)
    {
        return LoadPlacementAsync(
            MainWindowKeyPrefix,
            MainMinimumWidth,
            MainMinimumHeight,
            MainDefaultWidth,
            MainDefaultHeight,
            cancellationToken);
    }

    /// <summary>
    /// 保存主窗口的位置与尺寸。
    /// </summary>
    public Task SaveMainWindowPlacementAsync(
        WindowPlacement placement,
        CancellationToken cancellationToken = default)
    {
        return SavePlacementAsync(
            MainWindowKeyPrefix,
            placement,
            MainMinimumWidth,
            MainMinimumHeight,
            MainDefaultWidth,
            MainDefaultHeight,
            cancellationToken);
    }

    /// <summary>
    /// 读取桌面四象限窗口上次保存的位置与尺寸。
    /// </summary>
    public Task<WindowPlacement>
        LoadMatrixWindowPlacementAsync(
            CancellationToken cancellationToken = default)
    {
        return LoadPlacementAsync(
            MatrixWindowKeyPrefix,
            MatrixMinimumWidth,
            MatrixMinimumHeight,
            MatrixDefaultWidth,
            MatrixDefaultHeight,
            cancellationToken);
    }

    /// <summary>
    /// 保存桌面四象限窗口的位置与尺寸。
    /// </summary>
    public Task SaveMatrixWindowPlacementAsync(
        WindowPlacement placement,
        CancellationToken cancellationToken = default)
    {
        return SavePlacementAsync(
            MatrixWindowKeyPrefix,
            placement,
            MatrixMinimumWidth,
            MatrixMinimumHeight,
            MatrixDefaultWidth,
            MatrixDefaultHeight,
            cancellationToken);
    }

    /// <summary>
    /// 读取桌面任务列表上次保存的位置与尺寸。
    /// </summary>
    public Task<WindowPlacement>
        LoadDesktopTaskListWindowPlacementAsync(
            CancellationToken cancellationToken = default)
    {
        return LoadPlacementAsync(
            DesktopTaskListWindowKeyPrefix,
            DesktopTaskListMinimumWidth,
            DesktopTaskListMinimumHeight,
            DesktopTaskListDefaultWidth,
            DesktopTaskListDefaultHeight,
            cancellationToken);
    }

    /// <summary>
    /// 保存桌面任务列表的位置与尺寸。
    /// </summary>
    public Task SaveDesktopTaskListWindowPlacementAsync(
        WindowPlacement placement,
        CancellationToken cancellationToken = default)
    {
        return SavePlacementAsync(
            DesktopTaskListWindowKeyPrefix,
            placement,
            DesktopTaskListMinimumWidth,
            DesktopTaskListMinimumHeight,
            DesktopTaskListDefaultWidth,
            DesktopTaskListDefaultHeight,
            cancellationToken);
    }

    private async Task<WindowPlacement>
        LoadPlacementAsync(
            string keyPrefix,
            double minimumWidth,
            double minimumHeight,
            double defaultWidth,
            double defaultHeight,
            CancellationToken cancellationToken)
    {
        WindowPlacement defaultPlacement =
            CreateDefaultPlacement(
                minimumWidth,
                minimumHeight,
                defaultWidth,
                defaultHeight);

        string leftKey =
            $"{keyPrefix}.Left";

        string topKey =
            $"{keyPrefix}.Top";

        string widthKey =
            $"{keyPrefix}.Width";

        string heightKey =
            $"{keyPrefix}.Height";

        IReadOnlyDictionary<string, string> values =
            await _appSettingRepository.GetValuesAsync(
            [
                leftKey,
                topKey,
                widthKey,
                heightKey
            ],
                cancellationToken);

        values.TryGetValue(
            leftKey,
            out string? leftValue);

        values.TryGetValue(
            topKey,
            out string? topValue);

        values.TryGetValue(
            widthKey,
            out string? widthValue);

        values.TryGetValue(
            heightKey,
            out string? heightValue);

        WindowPlacement savedPlacement =
            new(
                ParseDouble(
                    leftValue,
                    defaultPlacement.Left),

                ParseDouble(
                    topValue,
                    defaultPlacement.Top),

                ParseDouble(
                    widthValue,
                    defaultPlacement.Width),

                ParseDouble(
                    heightValue,
                    defaultPlacement.Height));

        return NormalizePlacement(
            savedPlacement,
            minimumWidth,
            minimumHeight,
            defaultWidth,
            defaultHeight);
    }

    private async Task SavePlacementAsync(
        string keyPrefix,
        WindowPlacement placement,
        double minimumWidth,
        double minimumHeight,
        double defaultWidth,
        double defaultHeight,
        CancellationToken cancellationToken)
    {
        WindowPlacement normalizedPlacement =
            NormalizePlacement(
                placement,
                minimumWidth,
                minimumHeight,
                defaultWidth,
                defaultHeight);

        Dictionary<string, string> values =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [$"{keyPrefix}.Left"] =
                    FormatDouble(
                        normalizedPlacement.Left),

                [$"{keyPrefix}.Top"] =
                    FormatDouble(
                        normalizedPlacement.Top),

                [$"{keyPrefix}.Width"] =
                    FormatDouble(
                        normalizedPlacement.Width),

                [$"{keyPrefix}.Height"] =
                    FormatDouble(
                        normalizedPlacement.Height)
            };

        await _appSettingRepository.SetValuesAsync(
            values,
            cancellationToken);
    }

    private static WindowPlacement
        CreateDefaultPlacement(
            double minimumWidth,
            double minimumHeight,
            double defaultWidth,
            double defaultHeight)
    {
        Rect workArea =
            SystemParameters.WorkArea;

        double width =
            Math.Min(
                defaultWidth,
                Math.Max(
                    minimumWidth,
                    workArea.Width));

        double height =
            Math.Min(
                defaultHeight,
                Math.Max(
                    minimumHeight,
                    workArea.Height));

        double left =
            workArea.Left +
            Math.Max(
                0,
                (workArea.Width - width) / 2);

        double top =
            workArea.Top +
            Math.Max(
                0,
                (workArea.Height - height) / 2);

        return NormalizePlacement(
            new WindowPlacement(
                left,
                top,
                width,
                height),
            minimumWidth,
            minimumHeight,
            defaultWidth,
            defaultHeight);
    }

    private static WindowPlacement
        NormalizePlacement(
            WindowPlacement placement,
            double minimumWidth,
            double minimumHeight,
            double defaultWidth,
            double defaultHeight)
    {
        double virtualLeft =
            SystemParameters.VirtualScreenLeft;

        double virtualTop =
            SystemParameters.VirtualScreenTop;

        double virtualWidth =
            SystemParameters.VirtualScreenWidth;

        double virtualHeight =
            SystemParameters.VirtualScreenHeight;

        if (!double.IsFinite(virtualWidth) ||
            virtualWidth <= 0)
        {
            virtualWidth =
                1920;
        }

        if (!double.IsFinite(virtualHeight) ||
            virtualHeight <= 0)
        {
            virtualHeight =
                1080;
        }

        double maximumWidth =
            Math.Max(
                minimumWidth,
                virtualWidth);

        double maximumHeight =
            Math.Max(
                minimumHeight,
                virtualHeight);

        double width =
            NormalizeDimension(
                placement.Width,
                minimumWidth,
                maximumWidth,
                defaultWidth);

        double height =
            NormalizeDimension(
                placement.Height,
                minimumHeight,
                maximumHeight,
                defaultHeight);

        double maximumLeft =
            virtualLeft +
            virtualWidth -
            width;

        double maximumTop =
            virtualTop +
            virtualHeight -
            height;

        if (maximumLeft < virtualLeft)
        {
            maximumLeft =
                virtualLeft;
        }

        if (maximumTop < virtualTop)
        {
            maximumTop =
                virtualTop;
        }

        double left =
            NormalizeCoordinate(
                placement.Left,
                virtualLeft,
                maximumLeft);

        double top =
            NormalizeCoordinate(
                placement.Top,
                virtualTop,
                maximumTop);

        return new WindowPlacement(
            left,
            top,
            width,
            height);
    }

    private static double NormalizeDimension(
        double value,
        double minimum,
        double maximum,
        double fallback)
    {
        if (!double.IsFinite(value))
        {
            value =
                fallback;
        }

        return Math.Clamp(
            value,
            minimum,
            maximum);
    }

    private static double NormalizeCoordinate(
        double value,
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(value))
        {
            return minimum;
        }

        return Math.Clamp(
            value,
            minimum,
            maximum);
    }

    private static double ParseDouble(
        string? value,
        double fallback)
    {
        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double result) &&
            double.IsFinite(result))
        {
            return result;
        }

        return fallback;
    }

    private static string FormatDouble(
        double value)
    {
        return value.ToString(
            "R",
            CultureInfo.InvariantCulture);
    }
}
