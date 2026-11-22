using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Date.Models;
using Date.Services;
using MediaColor = System.Windows.Media.Color;

namespace Date;

public partial class MainWindow : Window
{
    private readonly AppConfig _config;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _positionTimer;
    private bool _isDarkMode = true;

    // 深色模式配色 (macOS 26 Dark Vibrancy)
    private static readonly SolidColorBrush DarkBg = new(MediaColor.FromArgb(0x40, 0x1A, 0x1A, 0x1E));
    private static readonly SolidColorBrush DarkBorder = new(MediaColor.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkDateFg = new(MediaColor.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkDivider = new(MediaColor.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkBadgeBg = new(MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkDayFg = new(MediaColor.FromArgb(0xC0, 0xEE, 0xEE, 0xF2));

    // 浅色模式配色 (macOS 26 Light Vibrancy)
    private static readonly SolidColorBrush LightBg = new(MediaColor.FromArgb(0x75, 0xF5, 0xF5, 0xF7));
    private static readonly SolidColorBrush LightBorder = new(MediaColor.FromArgb(0x20, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightDateFg = new(MediaColor.FromArgb(0xEE, 0x1D, 0x1D, 0x1F));
    private static readonly SolidColorBrush LightDivider = new(MediaColor.FromArgb(0x1F, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightBadgeBg = new(MediaColor.FromArgb(0x14, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightDayFg = new(MediaColor.FromArgb(0xCC, 0x50, 0x50, 0x55));

    public MainWindow(AppConfig config)
    {
        _config = config;
        InitializeComponent();

        _clockTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (s, e) => UpdateDateTimeText();

        _positionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _positionTimer.Tick += (s, e) => UpdatePosition();

        Loaded += MainWindow_Loaded;
        MouseRightButtonUp += MainWindow_MouseRightButtonUp;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        TaskbarLocator.ApplyWindowStyles(this);

        // 挂载消息钩子以响应系统显示器与分辨率变化
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }

        // 初始化主题
        ThemeService.ThemeChanged += OnThemeChanged;
        ApplyTheme(ThemeService.IsDarkMode());

        UpdateDateTimeText();
        UpdatePosition();

        _clockTimer.Start();
        _positionTimer.Start();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DISPLAYCHANGE = 0x007E;
        const int WM_SETTINGCHANGE = 0x001A;

        if (msg == WM_DISPLAYCHANGE || msg == WM_SETTINGCHANGE)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(UpdatePosition));
        }

        return IntPtr.Zero;
    }

    private void OnThemeChanged(bool isDarkMode)
    {
        Dispatcher.Invoke(() => ApplyTheme(isDarkMode));
    }

    public void ApplyTheme(bool isDarkMode)
    {
        _isDarkMode = isDarkMode;

        if (_isDarkMode)
        {
            PillContainer.Background = DarkBg;
            PillContainer.BorderBrush = DarkBorder;
            TxtDate.Foreground = DarkDateFg;
            Divider.Background = DarkDivider;
            DayBadge.Background = DarkBadgeBg;
            TxtDayOfWeek.Foreground = DarkDayFg;
            PillShadow.Color = MediaColor.FromRgb(0x00, 0x00, 0x00);
            PillShadow.Opacity = 0.3;
        }
        else
        {
            PillContainer.Background = LightBg;
            PillContainer.BorderBrush = LightBorder;
            TxtDate.Foreground = LightDateFg;
            Divider.Background = LightDivider;
            DayBadge.Background = LightBadgeBg;
            TxtDayOfWeek.Foreground = LightDayFg;
            PillShadow.Color = MediaColor.FromRgb(0x60, 0x60, 0x60);
            PillShadow.Opacity = 0.15;
        }
    }

    private void UpdateDateTimeText()
    {
        DateTime now = DateTime.Now;
        string dateText = now.ToString(_config.DateFormat);
        string dayText = now.ToString(_config.DayOfWeekFormat);

        if (TxtDate.Text != dateText) TxtDate.Text = dateText;
        if (TxtDayOfWeek.Text != dayText) TxtDayOfWeek.Text = dayText;
    }

    public void UpdatePosition()
    {
        var pos = TaskbarLocator.CalculatePosition(this, _config.OffsetLeft);
        if (pos.HasValue)
        {
            if (Math.Abs(this.Left - pos.Value.X) > 0.5) this.Left = pos.Value.X;
            if (Math.Abs(this.Top - pos.Value.Y) > 0.5) this.Top = pos.Value.Y;
        }
    }

    private void MainWindow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 右键悬浮小胶囊时，呼出系统托盘的上下文菜单，方便用户管理
        App.ShowContextMenu();
    }
}
