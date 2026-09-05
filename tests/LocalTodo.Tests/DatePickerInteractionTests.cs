using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalTodo.Helpers;
using LocalTodo.Views.Controls;

namespace LocalTodo.Tests;

[Collection("WPF date controls")]
public sealed class DatePickerInteractionTests(DatePickerTestHost host) : IClassFixture<DatePickerTestHost>
{
    [Fact]
    public void EntireDateAreaIsClickableAndTextIsReadOnly() => RunOnSta(() =>
    {
        DatePicker picker = CreatePicker();
        // 未显示的测试 HWND 提供真实 PresentationSource，使 WPF 的鼠标命中测试生效。
        // 不显示窗口、不切换焦点，也不操作用户正在运行的 LocalTodo。
        using HwndSource surface = new(new HwndSourceParameters("LocalTodo.DatePickerTest")
        {
            WindowStyle = unchecked((int)0x80000000),
            PositionX = -32000,
            PositionY = -32000,
            Width = 200,
            Height = 100
        });
        surface.RootVisual = picker;
        Layout(picker);
        Button openButton = Part<Button>(picker, "PART_Button");
        DatePickerTextBox textBox = Part<DatePickerTextBox>(picker, "PART_TextBox");

        Assert.True(textBox.IsReadOnly);
        Assert.False(textBox.IsHitTestVisible);
        Assert.False(textBox.Focusable);
        Assert.False(textBox.IsTabStop);
        Assert.True(openButton.IsTabStop);
        Assert.True(openButton.Focusable);
        Assert.False(textBox.AllowDrop);

        // 包含左侧留白、日期文字区和原来小日历图标所在的位置。
        foreach (Point point in new[] { new Point(2, 2), new Point(30, 19), new Point(119, 19) })
        {
            Assert.True(ReferenceEquals(openButton, HitButton(picker, point)),
                $"命中失败 {point}：日期框 {picker.RenderSize} / 可见 {picker.IsVisible}；按钮 {openButton.RenderSize} / 可见 {openButton.IsVisible} / 点击 {openButton.IsHitTestVisible}");
        }

        picker.SelectedDate = new DateTime(2026, 9, 5);
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        Layout(picker);
        Assert.Same(openButton, HitButton(picker, new Point(30, 19)));
        Assert.Same(Part<Button>(picker, "ClearDateButton"), HitButton(picker, new Point(119, 19)));
        Assert.NotEmpty(textBox.Text);
        Assert.True(textBox.ActualWidth >= textBox.ExtentWidth,
            $"日期文字被裁切：可用 {textBox.ActualWidth}，需要 {textBox.ExtentWidth}");
    });

    [Fact]
    public void CalendarSelectionAndClearPreserveTwoWayBinding() => RunOnSta(() =>
    {
        DatePicker picker = CreatePicker();
        DateSource source = new() { Value = new DateTime(2026, 9, 5) };
        picker.SetBinding(DatePicker.SelectedDateProperty, new Binding(nameof(DateSource.Value))
        {
            Source = source,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        Popup popup = Part<Popup>(picker, "PART_Popup");
        Calendar calendar = Assert.IsType<Calendar>(popup.Child);

        Part<Button>(picker, "PART_Button").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(picker.IsDropDownOpen);
        Assert.False(popup.StaysOpen);
        Assert.Same(picker, popup.PlacementTarget);
        picker.IsDropDownOpen = false;

        DateTime nextDate = new(2027, 1, 12);
        calendar.SelectedDate = nextDate;
        Assert.Equal(nextDate, picker.SelectedDate);
        Assert.Equal(nextDate, source.Value);
        Assert.True(DatePickerInteraction.ClearDateCommand.CanExecute(null, picker));

        DatePickerInteraction.ClearDateCommand.Execute(null, picker);
        Assert.Null(picker.SelectedDate);
        Assert.Null(source.Value);
        Assert.True(BindingOperations.IsDataBound(picker, DatePicker.SelectedDateProperty));
        Assert.False(DatePickerInteraction.ClearDateCommand.CanExecute(null, picker));
        Assert.Equal(Visibility.Collapsed, Part<Button>(picker, "ClearDateButton").Visibility);
        Assert.Equal(Visibility.Visible, Part<TextBlock>(picker, "DatePlaceholder").Visibility);

        calendar.SelectedDate = nextDate.AddDays(1);
        Assert.Equal(nextDate.AddDays(1), source.Value);
        source.Value = nextDate.AddMonths(1);
        Assert.Equal(source.Value, picker.SelectedDate);
        Assert.Equal(source.Value, calendar.SelectedDate);
    });

    [Fact]
    public void DisabledAndBlackoutDatesKeepNativeRestrictions() => RunOnSta(() =>
    {
        DatePicker picker = CreatePicker();
        DateTime blocked = new(2026, 9, 6);
        picker.BlackoutDates.Add(new CalendarDateRange(blocked));
        Calendar calendar = Assert.IsType<Calendar>(Part<Popup>(picker, "PART_Popup").Child);
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.SelectedDate = blocked);

        picker.SelectedDate = blocked.AddDays(1);
        picker.IsEnabled = false;
        Assert.False(Part<Button>(picker, "PART_Button").IsEnabled);
        Assert.False(DatePickerInteraction.ClearDateCommand.CanExecute(null, picker));
        picker.IsDropDownOpen = true;
        Assert.False(picker.IsDropDownOpen);
        Assert.Equal(blocked.AddDays(1), picker.SelectedDate);
    });

    [Fact]
    public void SharedTaskSelectorUsesNewStyleWithoutLosingItsBinding() => RunOnSta(() =>
    {
        TaskDateSelectorView view = new()
        {
            Width = 124,
            SelectedDate = new DateTime(2026, 9, 5)
        };
        view.Measure(new Size(124, 100));
        view.Arrange(new Rect(0, 0, 124, 100));
        view.UpdateLayout();
        DatePicker picker = Assert.IsType<DatePicker>(((StackPanel)view.Content).Children[1]);
        Assert.True(Part<DatePickerTextBox>(picker, "PART_TextBox").IsReadOnly);

        DatePickerInteraction.ClearDateCommand.Execute(null, picker);
        Assert.Null(view.SelectedDate);
        DateTime selected = new(2026, 10, 20);
        Assert.IsType<Calendar>(Part<Popup>(picker, "PART_Popup").Child).SelectedDate = selected;
        Assert.Equal(selected, view.SelectedDate);
        Assert.True(BindingOperations.IsDataBound(picker, DatePicker.SelectedDateProperty));
    });

    [Fact]
    public void TogglingBehaviorDoesNotAccumulateCommandBindings() => RunOnSta(() =>
    {
        DatePicker picker = CreatePicker();
        Assert.Single(picker.CommandBindings.Cast<System.Windows.Input.CommandBinding>());
        DatePickerInteraction.SetIsEnabled(picker, false);
        Assert.Empty(picker.CommandBindings);
        DatePickerInteraction.SetIsEnabled(picker, true);
        Assert.Single(picker.CommandBindings.Cast<System.Windows.Input.CommandBinding>());
    });

    [Theory]
    [InlineData("CalendarViewResources.xaml")]
    [InlineData("MatrixViewResources.xaml")]
    [InlineData("DesktopTaskListViewResources.xaml")]
    public void PageResourcesCannotRestoreEditableDateText(string resourceFile) => RunOnSta(() =>
    {
        DatePicker picker = CreatePicker();
        picker.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/LocalTodo;component/Views/Resources/{resourceFile}", UriKind.Relative)
        });
        Layout(picker);
        Assert.True(Part<DatePickerTextBox>(picker, "PART_TextBox").IsReadOnly);
        Assert.False(Part<DatePickerTextBox>(picker, "PART_TextBox").IsHitTestVisible);
        Assert.True(DatePickerInteraction.GetIsEnabled(picker));
    });

    [Theory]
    [InlineData(1.0, 13, "zh-CN")]
    [InlineData(1.25, 13, "zh-CN")]
    [InlineData(1.5, 13, "zh-CN")]
    [InlineData(2.0, 13, "zh-CN")]
    [InlineData(1.0, 14, "en-US")]
    [InlineData(1.25, 14, "en-US")]
    [InlineData(1.5, 14, "en-US")]
    [InlineData(2.0, 14, "en-US")]
    public void DateTextFitsCompactFieldAtCommonDpi(double scale, int fontSize, string language) => RunOnSta(() =>
    {
        DatePicker picker = CreatePicker();
        VisualTreeHelper.SetRootDpi(picker, new DpiScale(scale, scale));
        picker.FontSize = fontSize;
        picker.Language = XmlLanguage.GetLanguage(language);
        picker.SelectedDate = new DateTime(2026, 12, 31);
        Layout(picker);
        DatePickerTextBox textBox = Part<DatePickerTextBox>(picker, "PART_TextBox");
        Assert.True(textBox.ActualWidth >= textBox.ExtentWidth,
            $"{scale:P0} / {language} 日期被裁切：可用 {textBox.ActualWidth}，需要 {textBox.ExtentWidth}");
        Assert.True(textBox.ActualHeight >= textBox.ExtentHeight);

        // 可选输出实际 WPF 渲染图，便于人工检查；默认测试不生成图片。
        string? previewFolder = Environment.GetEnvironmentVariable("LOCALTODO_DATE_PICKER_PREVIEW");
        if (!string.IsNullOrEmpty(previewFolder))
        {
            Directory.CreateDirectory(previewFolder);
            RenderTargetBitmap bitmap = new((int)Math.Ceiling(124 * scale), (int)Math.Ceiling(38 * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(picker);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream file = File.Create(Path.Combine(previewFolder, $"date-{scale * 100:0}-{language}.png"));
            encoder.Save(file);
        }
    });

    private static DatePicker CreatePicker()
    {
        DatePicker picker = new()
        {
            Style = (Style)Application.Current.FindResource(typeof(DatePicker)),
            Language = XmlLanguage.GetLanguage("zh-CN"),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            Width = 124,
            Height = 38
        };
        Layout(picker);
        return picker;
    }

    private static T Part<T>(DatePicker picker, string name) where T : class =>
        Assert.IsType<T>(picker.Template.FindName(name, picker));

    private static void Layout(DatePicker picker)
    {
        picker.ApplyTemplate();
        picker.Measure(new Size(124, 38));
        picker.Arrange(new Rect(0, 0, 124, 38));
        picker.UpdateLayout();
    }

    private static Button? HitButton(DatePicker picker, Point point)
    {
        DependencyObject? hit = picker.InputHitTest(point) as DependencyObject;
        while (hit is not null && hit is not Button)
        {
            hit = VisualTreeHelper.GetParent(hit);
        }
        return hit as Button;
    }

    private void RunOnSta(Action action) => host.Run(action);

    private sealed class DateSource : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(DateTime?), typeof(DateSource));

        public DateTime? Value
        {
            get => (DateTime?)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }
}

// Application 的资源解析是全局的，不与数据库/视图模型测试并行运行。
[CollectionDefinition("WPF date controls", DisableParallelization = true)]
public sealed class DatePickerTestCollection;

/// <summary>
/// 使用普通 WPF Application 加载实际样式，不启动 LocalTodo.App、托盘、数据库或用户窗口。
/// </summary>
public sealed class DatePickerTestHost : IDisposable
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;

    public DatePickerTestHost()
    {
        TaskCompletionSource<Dispatcher> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            try
            {
                Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/LocalTodo;component/Resources/Colors.xaml", UriKind.Relative)
                });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/LocalTodo;component/Resources/Styles.xaml", UriKind.Relative)
                });
                ready.SetResult(app.Dispatcher);
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                ready.TrySetException(exception);
            }
        }) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _dispatcher = ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
    }

    public void Run(Action action)
    {
        Exception? error = null;
        _dispatcher.Invoke(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
        }, DispatcherPriority.Normal, CancellationToken.None, TimeSpan.FromSeconds(30));
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    public void Dispose()
    {
        _dispatcher.Invoke(() =>
        {
            Application.Current.Shutdown();
            _dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        },
            DispatcherPriority.Normal, CancellationToken.None, TimeSpan.FromSeconds(10));
        _thread.Join(TimeSpan.FromSeconds(30));
    }
}
