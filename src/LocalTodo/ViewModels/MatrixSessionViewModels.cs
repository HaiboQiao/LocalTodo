using LocalTodo.Services;

namespace LocalTodo.ViewModels;

/// <summary>
/// 主窗口内四象限页面的独立编辑会话。
/// </summary>
public sealed class MainMatrixSessionViewModel :
    MatrixViewModel
{
    public MainMatrixSessionViewModel(
        MatrixTaskStore taskStore,
        QuadrantService quadrantService,
        TaskService taskService,
        DialogService dialogService)
        : base(
            taskStore,
            quadrantService,
            taskService,
            dialogService,
            TaskChangeSource.MainMatrix)
    {
    }
}

/// <summary>
/// 桌面四象限窗口的独立编辑会话。
/// </summary>
public sealed class DesktopMatrixSessionViewModel :
    MatrixViewModel
{
    public DesktopMatrixSessionViewModel(
        MatrixTaskStore taskStore,
        QuadrantService quadrantService,
        TaskService taskService,
        DialogService dialogService)
        : base(
            taskStore,
            quadrantService,
            taskService,
            dialogService,
            TaskChangeSource.DesktopMatrix)
    {
    }
}
