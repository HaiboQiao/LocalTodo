using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace LocalTodo.Views.Controls;

public partial class TaskValueSelectorView :
    UserControl
{
    public static readonly DependencyProperty
        LabelProperty =
            DependencyProperty.Register(
                nameof(Label),
                typeof(string),
                typeof(TaskValueSelectorView),
                new PropertyMetadata(
                    string.Empty));

    public static readonly DependencyProperty
        ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable),
                typeof(TaskValueSelectorView));

    public static readonly DependencyProperty
        DisplayMemberPathProperty =
            DependencyProperty.Register(
                nameof(DisplayMemberPath),
                typeof(string),
                typeof(TaskValueSelectorView),
                new PropertyMetadata(
                    "Title"));

    public static readonly DependencyProperty
        SelectedValuePathProperty =
            DependencyProperty.Register(
                nameof(SelectedValuePath),
                typeof(string),
                typeof(TaskValueSelectorView),
                new PropertyMetadata(
                    "Value"));

    public static readonly DependencyProperty
        SelectedValueProperty =
            DependencyProperty.Register(
                nameof(SelectedValue),
                typeof(object),
                typeof(TaskValueSelectorView),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions
                        .BindsTwoWayByDefault));

    public TaskValueSelectorView()
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

    public string SelectedValuePath
    {
        get =>
            (string)GetValue(
                SelectedValuePathProperty);

        set =>
            SetValue(
                SelectedValuePathProperty,
                value);
    }

    public object? SelectedValue
    {
        get =>
            GetValue(
                SelectedValueProperty);

        set =>
            SetValue(
                SelectedValueProperty,
                value);
    }
}
