using System;
using System.Windows;
using System.Windows.Controls;

namespace LocalTodo.Views.Controls;

public partial class TaskDateSelectorView :
    UserControl
{
    public static readonly DependencyProperty
        LabelProperty =
            DependencyProperty.Register(
                nameof(Label),
                typeof(string),
                typeof(TaskDateSelectorView),
                new PropertyMetadata(
                    "截止日期"));

    public static readonly DependencyProperty
        SelectedDateProperty =
            DependencyProperty.Register(
                nameof(SelectedDate),
                typeof(DateTime?),
                typeof(TaskDateSelectorView),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions
                        .BindsTwoWayByDefault));

    public TaskDateSelectorView()
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

    public DateTime? SelectedDate
    {
        get =>
            (DateTime?)GetValue(
                SelectedDateProperty);

        set =>
            SetValue(
                SelectedDateProperty,
                value);
    }
}
