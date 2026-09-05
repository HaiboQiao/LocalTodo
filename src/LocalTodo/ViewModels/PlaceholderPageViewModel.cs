using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalTodo.ViewModels;

/// <summary>
/// 尚未开发页面的占位内容。
/// </summary>
public sealed class PlaceholderPageViewModel :
    ObservableObject
{
    public string Title { get; }

    public string Message { get; }

    public PlaceholderPageViewModel(
        string title,
        string message)
    {
        Title =
            title;

        Message =
            message;
    }
}
