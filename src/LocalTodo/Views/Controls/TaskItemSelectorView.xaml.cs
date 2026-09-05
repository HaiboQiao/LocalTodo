using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace LocalTodo.Views.Controls;

public partial class TaskItemSelectorView :
    UserControl
{
    public static readonly DependencyProperty
        LabelProperty =
            DependencyProperty.Register(
                nameof(Label),
                typeof(string),
                typeof(TaskItemSelectorView),
                new PropertyMetadata(
                    string.Empty));

    public static readonly DependencyProperty
        ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable),
                typeof(TaskItemSelectorView));

    public static readonly DependencyProperty
        DisplayMemberPathProperty =
            DependencyProperty.Register(
                nameof(DisplayMemberPath),
                typeof(string),
                typeof(TaskItemSelectorView),
                new PropertyMetadata(
                    "Title"));

    public static readonly DependencyProperty
        SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(object),
                typeof(TaskItemSelectorView),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions
                        .BindsTwoWayByDefault));

    public TaskItemSelectorView()
    {
        InitializeComponent();
    }

    public string Label
    {
        get =>
            (string)GetValue(
                LabelProperty);

        set =>
            SetValue(
                LabelProperty,
                value);
    }

    public IEnumerable? ItemsSource
    {
        get =>
            (IEnumerable?)GetValue(
                ItemsSourceProperty);

        set =>
            SetValue(
                ItemsSourceProperty,
                value);
    }

    public string DisplayMemberPath
    {
        get =>
            (string)GetValue(
                DisplayMemberPathProperty);

        set =>
            SetValue(
                DisplayMemberPathProperty,
                value);
    }

    public object? SelectedItem
    {
        get =>
            GetValue(
                SelectedItemProperty);

        set =>
            SetValue(
                SelectedItemProperty,
                value);
    }
}
