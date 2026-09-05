using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LocalTodo.Helpers;

/// <summary>
/// 为整框点击式 DatePicker 补充清空和键盘入口；日期选择仍交给原生 DatePicker。
/// 不拦截日历内的键盘事件，也不改变 SelectedDate 的业务绑定。
/// </summary>
public static class DatePickerInteraction
{
    public static readonly RoutedUICommand ClearDateCommand = new(
        "清除日期", nameof(ClearDateCommand), typeof(DatePickerInteraction));

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DatePickerInteraction),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DatePicker picker)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            picker.CommandBindings.Add(new CommandBinding(ClearDateCommand, ClearDate, CanClearDate));
            picker.PreviewKeyDown += OnPreviewKeyDown;
            picker.SelectedDateChanged += OnSelectedDateChanged;
        }
        else
        {
            picker.PreviewKeyDown -= OnPreviewKeyDown;
            picker.SelectedDateChanged -= OnSelectedDateChanged;
            for (int index = picker.CommandBindings.Count - 1; index >= 0; index--)
            {
                CommandBinding binding = picker.CommandBindings[index];
                if (binding.Command == ClearDateCommand)
                {
                    picker.CommandBindings.RemoveAt(index);
                }
            }
        }
    }

    private static void OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e) =>
        CommandManager.InvalidateRequerySuggested();

    private static void CanClearDate(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = sender is DatePicker { IsEnabled: true, SelectedDate: not null };
        e.Handled = true;
    }

    private static void ClearDate(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is DatePicker { IsEnabled: true } picker)
        {
            picker.SetCurrentValue(DatePicker.IsDropDownOpenProperty, false);
            // SetCurrentValue 保留 TwoWay 绑定，后续仍能重新选择、保存日期。
            picker.SetCurrentValue(DatePicker.SelectedDateProperty, null);
            if (picker.Template?.FindName("PART_Button", picker) is Button button)
            {
                button.Focus();
            }
        }

        e.Handled = true;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DatePicker { IsEnabled: true } picker ||
            picker.Template?.FindName("PART_Button", picker) is not Button button ||
            !button.IsKeyboardFocused)
        {
            return;
        }

        // 只处理日期主按钮。清除按钮和弹出日历继续使用各自的原生键盘行为。
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool openCalendar = key == Key.F4 ||
            (key == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt) ||
            (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None);

        if (openCalendar)
        {
            picker.SetCurrentValue(DatePicker.IsDropDownOpenProperty, true);
            e.Handled = true;
        }
        // Space 由原生 Button.Click 打开，避免同时处理导致开关两次。
    }
}
