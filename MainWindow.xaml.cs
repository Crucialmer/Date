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

    // macOS 26 高对比度深色模式配色 (Obsidian Vibrancy Glass)
    private static readonly SolidColorBrush DarkBg = new(MediaColor.FromArgb(0xD0, 0x1E, 0x1E, 0x22));
    private static readonly SolidColorBrush DarkBorder = new(MediaColor.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkDateFg = new(MediaColor.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkDivider = new(MediaColor.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkBadgeBg = new(MediaColor.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkBadgeBorder = new(MediaColor.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkDayFg = new(MediaColor.FromRgb(0xF5, 0xF5, 0xF7));

    // macOS 26 高对比度浅色模式配色 (Crystal Milk Vibrancy Glass)
    private static readonly SolidColorBrush LightBg = new(MediaColor.FromArgb(0xEA, 0xF6, 0xF6, 0xF9));
    private static readonly SolidColorBrush LightBorder = new(MediaColor.FromArgb(0x30, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightDateFg = new(MediaColor.FromRgb(0x11, 0x11, 0x13));
    private static readonly SolidColorBrush LightDivider = new(MediaColor.FromArgb(0x28, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightBadgeBg = new(MediaColor.FromArgb(0x18, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightBadgeBorder = new(MediaColor.FromArgb(0x20, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightDayFg = new(MediaColor.FromRgb(0x2C, 0x2C, 0x2E));

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 核心修复：必须在 SourceInitialized 阶段（窗口呈现前）设置无激活与工具窗口样式
        // 彻底解决之前在 Loaded 中改样式导致 Windows 11 DWM 判定异常引起的“闪退”问题
        TaskbarLocator.ApplyWindowStyles(this);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged += OnThemeChanged;
        ApplyTheme(ThemeService.IsDarkMode());

        UpdateDateTimeText();
        UpdateLayout();
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
            DayBadge.BorderBrush = DarkBadgeBorder;
            TxtDayOfWeek.Foreground = DarkDayFg;
            PillShadow.Color = MediaColor.FromRgb(0x00, 0x00, 0x00);
            PillShadow.Opacity = 0.35;
        }
        else
        {
            PillContainer.Background = LightBg;
            PillContainer.BorderBrush = LightBorder;
            TxtDate.Foreground = LightDateFg;
            Divider.Background = LightDivider;
            DayBadge.Background = LightBadgeBg;
            DayBadge.BorderBrush = LightBadgeBorder;
            TxtDayOfWeek.Foreground = LightDayFg;
            PillShadow.Color = MediaColor.FromRgb(0x50, 0x50, 0x50);
            PillShadow.Opacity = 0.18;
        }
    }

    private void UpdateDateTimeText()
    {
        try
        {
            DateTime now = DateTime.Now;
            string dateText = now.ToString(_config.DateFormat);
            string dayText = now.ToString(_config.DayOfWeekFormat);

            if (TxtDate.Text != dateText) TxtDate.Text = dateText;
            if (TxtDayOfWeek.Text != dayText) TxtDayOfWeek.Text = dayText;
        }
        catch { }
    }

    public void UpdatePosition()
    {
        try
        {
            var pos = TaskbarLocator.CalculatePosition(this, _config.OffsetLeft);
            if (pos.HasValue)
            {
                if (Math.Abs(this.Left - pos.Value.X) > 0.5) this.Left = pos.Value.X;
                if (Math.Abs(this.Top - pos.Value.Y) > 0.5) this.Top = pos.Value.Y;
            }
        }
        catch { }
    }

    private void MainWindow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        App.ShowContextMenu();
    }
}
