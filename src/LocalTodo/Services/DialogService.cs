using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// 统一管理基础确认对话框。
/// </summary>
public sealed class DialogService
{
    /// <summary>
    /// 页面切换、窗口隐藏或退出时，编辑内容无法保存。
    ///
    /// 默认选择“否”，只有用户明确确认才放弃输入。
    /// </summary>
    public bool ConfirmDiscardPendingChanges(
        string actionDescription,
        string failureMessage)
    {
        string reason =
            string.IsNullOrWhiteSpace(
                failureMessage)
                ? "存在无法保存的编辑内容。"
                : failureMessage;

        MessageBoxResult result =
            MessageBox.Show(
                $"暂时无法{actionDescription}。\n\n" +
                $"原因：{reason}\n\n" +
                "选择“否”可返回并修正内容；" +
                "选择“是”将明确放弃所有未保存修改并继续。",
                "存在未保存的修改",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        return result ==
            MessageBoxResult.Yes;
    }

    /// <summary>
    /// 普通删除：
    /// 任务移动到垃圾箱，之后仍然可以恢复。
    /// </summary>
    public bool ConfirmTaskDeletion(
        string taskTitle)
    {
        MessageBoxResult result =
            MessageBox.Show(
                $"确定删除任务“{taskTitle}”吗？\n\n" +
                "任务将移动到垃圾箱，之后仍然可以恢复。",
                "删除任务",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

        return result ==
            MessageBoxResult.Yes;
    }

    /// <summary>
    /// 根据任务当前状态决定应该提供哪种删除方式。
    ///
    /// 规则：
    ///
    /// 1. 普通任务：
    ///    普通删除确认。
    ///
    /// 2. Pending 循环任务：
    ///    删除当前周期 / 删除整个周期 / 取消。
    ///
    /// 3. Completed 循环任务：
    ///    它只是历史周期，
    ///    只能删除这一条历史记录或取消，
    ///    绝对不能通过历史记录停止当前/未来循环。
    /// </summary>
    public TaskDeleteChoice
        GetTaskDeleteChoice(
            TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        /*
         * ==============================
         * 普通任务
         * ==============================
         */
        if (task.RepeatType ==
            TaskRepeatType.None)
        {
            return ConfirmTaskDeletion(
                    task.Title)
                ? TaskDeleteChoice
                    .DeleteSingleTask
                : TaskDeleteChoice
                    .Cancel;
        }

        /*
         * ==============================
         * 已完成循环历史周期
         * ==============================
         *
         * 已完成这一期在完成时，
         * 下一期通常已经生成。
         *
         * 所以这里绝对不能再提供：
         * “删除整个周期”。
         */
        if (task.Status ==
            TodoStatus.Completed)
        {
            return
                ConfirmCompletedRecurringOccurrenceDeletion(
                    task.Title)
                    ? TaskDeleteChoice
                        .DeleteCurrentOccurrence
                    : TaskDeleteChoice
                        .Cancel;
        }

        /*
         * ==============================
         * Pending 循环活动期
         * ==============================
         *
         * 只有当前未完成的活动期，
         * 才拥有停止整个循环系列的权限。
         */
        return ShowPendingRecurringTaskDeleteDialog(
            task.Title);
    }

    /// <summary>
    /// 已完成循环历史周期的删除确认。
    ///
    /// 这里只会返回：
    /// DeleteCurrentOccurrence 或 Cancel。
    /// </summary>
    private static bool
        ConfirmCompletedRecurringOccurrenceDeletion(
            string taskTitle)
    {
        MessageBoxResult result =
            MessageBox.Show(
                $"确定删除已完成的循环周期" +
                $"“{taskTitle}”吗？\n\n" +
                "这里只会删除这一条已完成历史记录，" +
                "不会影响当前或未来的循环任务。",
                "删除已完成周期",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

        return result ==
            MessageBoxResult.Yes;
    }

    /// <summary>
    /// Pending 循环任务专用三选项窗口。
    ///
    /// 三个按钮精确对应：
    /// 删除当前周期 / 删除整个周期 / 取消。
    ///
    /// 这里直接在 DialogService 中构造小窗口，
    /// 不依赖额外 XAML 文件。
    /// </summary>
    private static TaskDeleteChoice
        ShowPendingRecurringTaskDeleteDialog(
            string taskTitle)
    {
        TaskDeleteChoice selectedChoice =
            TaskDeleteChoice.Cancel;

        Window dialog =
            new()
            {
                Title =
                    "删除循环任务",

                Width =
                    430,

                SizeToContent =
                    SizeToContent.Height,

                ResizeMode =
                    ResizeMode.NoResize,

                WindowStartupLocation =
                    WindowStartupLocation
                        .CenterOwner,

                ShowInTaskbar =
                    false,

                Background =
                    System.Windows.Media.Brushes.White
            };

        Grid root =
            new()
            {
                Margin =
                    new Thickness(
                        20)
            };

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        TextBlock title =
            new()
            {
                Text =
                    "删除循环任务",

                FontSize =
                    18,

                FontWeight =
                    FontWeights.SemiBold
            };

        Grid.SetRow(
            title,
            0);

        root.Children.Add(
            title);

        TextBlock description =
            new()
            {
                Margin =
                    new Thickness(
                        0,
                        12,
                        0,
                        18),

                Text =
                    $"“{taskTitle}”是循环任务。\n" +
                    "请选择删除方式：",

                TextWrapping =
                    TextWrapping.Wrap,

                Foreground =
                    System.Windows.Media.Brushes.DimGray
            };

        Grid.SetRow(
            description,
            1);

        root.Children.Add(
            description);

        Grid buttons =
            new();

        buttons.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        buttons.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        10)
            });

        buttons.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        buttons.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        10)
            });

        buttons.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        Button deleteCurrentButton =
            new()
            {
                Height =
                    38,

                Content =
                    "删除当前周期",

                Cursor =
                    System.Windows.Input
                        .Cursors.Hand
            };

        deleteCurrentButton.Click +=
            (_, _) =>
            {
                selectedChoice =
                    TaskDeleteChoice
                        .DeleteCurrentOccurrence;

                dialog.DialogResult =
                    true;
            };

        Grid.SetColumn(
            deleteCurrentButton,
            0);

        buttons.Children.Add(
            deleteCurrentButton);

        Button deleteEntireButton =
            new()
            {
                Height =
                    38,

                Content =
                    "删除整个周期",

                Cursor =
                    System.Windows.Input
                        .Cursors.Hand,

                Foreground =
                    System.Windows.Media.Brushes.Firebrick
            };

        deleteEntireButton.Click +=
            (_, _) =>
            {
                selectedChoice =
                    TaskDeleteChoice
                        .DeleteEntireSeries;

                dialog.DialogResult =
                    true;
            };

        Grid.SetColumn(
            deleteEntireButton,
            2);

        buttons.Children.Add(
            deleteEntireButton);

        Button cancelButton =
            new()
            {
                Height =
                    38,

                Content =
                    "取消",

                Cursor =
                    System.Windows.Input
                        .Cursors.Hand
            };

        cancelButton.Click +=
            (_, _) =>
            {
                selectedChoice =
                    TaskDeleteChoice.Cancel;

                dialog.DialogResult =
                    false;
            };

        Grid.SetColumn(
            cancelButton,
            4);

        buttons.Children.Add(
            cancelButton);

        Grid.SetRow(
            buttons,
            2);

        root.Children.Add(
            buttons);

        dialog.Content =
            root;

        Window? owner =
            Application.Current?
                .Windows
                .OfType<Window>()
                .FirstOrDefault(
                    window =>
                        window.IsActive)
            ??
            Application.Current?
                .MainWindow;

        if (owner is not null &&
            !ReferenceEquals(
                owner,
                dialog))
        {
            dialog.Owner =
                owner;
        }

        dialog.ShowDialog();

        return selectedChoice;
    }

    /// <summary>
    /// 永久删除：
    /// 记录会从数据库中真正移除，无法恢复。
    /// </summary>
    public bool ConfirmPermanentTaskDeletion(
        string taskTitle)
    {
        MessageBoxResult result =
            MessageBox.Show(
                $"确定永久删除任务“{taskTitle}”吗？\n\n" +
                "永久删除后无法恢复。",
                "永久删除任务",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        return result ==
            MessageBoxResult.Yes;
    }

    /// <summary>
    /// 确认永久删除每周安排或成果记录。
    /// </summary>
    public bool ConfirmRecordDeletion(
        string recordType,
        string title)
    {
        MessageBoxResult result =
            MessageBox.Show(
                $"确定删除{recordType}“{title}”吗？\n\n" +
                "删除后无法恢复。",
                $"删除{recordType}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

        return result ==
            MessageBoxResult.Yes;
    }

}
