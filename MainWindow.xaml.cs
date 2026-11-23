using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Date.Models;
using Date.Services;
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace Date;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly AppConfig _config;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _positionTimer;
    private bool _isDarkMode = true;
    private ContextMenu? _contextMenu;

    // macOS 26 高对比度深色模式配色 (Obsidian Vibrancy Glass - 极轻盈微透星期徽章)
    private static readonly SolidColorBrush DarkBg = new(MediaColor.FromArgb(0xD8, 0x1E, 0x1E, 0x22));
    private static readonly SolidColorBrush DarkBorder = new(MediaColor.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkDateFg = new(MediaColor.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkDivider = new(MediaColor.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkBadgeBg = new(MediaColor.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkBadgeBorder = new(MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkDayFg = new(MediaColor.FromRgb(0xF5, 0xF5, 0xF7));

    // macOS 26 高对比度浅色模式配色 (Crystal Milk Vibrancy Glass - 极浅通透星期徽章)
    private static readonly SolidColorBrush LightBg = new(MediaColor.FromArgb(0xF0, 0xF6, 0xF6, 0xF9));
    private static readonly SolidColorBrush LightBorder = new(MediaColor.FromArgb(0x30, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightDateFg = new(MediaColor.FromRgb(0x11, 0x11, 0x13));
    private static readonly SolidColorBrush LightDivider = new(MediaColor.FromArgb(0x28, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightBadgeBg = new(MediaColor.FromArgb(0x09, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightBadgeBorder = new(MediaColor.FromArgb(0x12, 0x00, 0x00, 0x00));
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

        // 挂载至任务栏宿主、无激活工具窗口样式，彻底消灭闪退
        TaskbarLocator.ApplyWindowStyles(this);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
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

        // 核心优化：开机启动防跳动静默沉淀机制
        // 若系统刚开机（开机未满 2 分钟，Environment.TickCount64 < 120,000），说明各种开机自启软件（杀软、声卡、网盘等）正在陆续抢占托盘
        // 此阶段保持 Opacity=0 静默等待 3.8 秒，待所有托盘图标定型后一次性精确归位并丝滑淡入，彻底消灭开机反复跳动
        bool isSystemBoot = Environment.TickCount64 < 120_000;
        int delayMs = isSystemBoot ? 3800 : 350;

        var startupTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(delayMs)
        };
        startupTimer.Tick += (s, ev) =>
        {
            startupTimer.Stop();

            // 托盘图标全部就绪后，执行最终高精度归位
            UpdatePosition();

            // 丝滑淡入显现
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeIn);

            // 启动日常定时位置校验
            _positionTimer.Start();
        };
        startupTimer.Start();
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
            // 胶囊配色
            PillContainer.Background = DarkBg;
            PillContainer.BorderBrush = DarkBorder;
            TxtDate.Foreground = DarkDateFg;
            Divider.Background = DarkDivider;
            Divider2.Background = DarkDivider;
            DayBadge.Background = DarkBadgeBg;
            DayBadge.BorderBrush = DarkBadgeBorder;
            TxtDayOfWeek.Foreground = DarkDayFg;
            TxtTime.Foreground = DarkDateFg;
            PillShadow.Color = MediaColor.FromRgb(0x00, 0x00, 0x00);
            PillShadow.Opacity = 0.20;

            // 右键菜单深色自适应
            Resources["MenuBgBrush"] = new SolidColorBrush(MediaColor.FromArgb(0xEB, 0x1E, 0x1E, 0x22));
            Resources["MenuBorderBrush"] = new SolidColorBrush(MediaColor.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            Resources["MenuItemFgBrush"] = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0xFF, 0xFF));
            Resources["MenuItemHoverBrush"] = new SolidColorBrush(MediaColor.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            Resources["MenuSeparatorBrush"] = new SolidColorBrush(MediaColor.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
        }
        else
        {
            // 胶囊配色
            PillContainer.Background = LightBg;
            PillContainer.BorderBrush = LightBorder;
            TxtDate.Foreground = LightDateFg;
            Divider.Background = LightDivider;
            Divider2.Background = LightDivider;
            DayBadge.Background = LightBadgeBg;
            DayBadge.BorderBrush = LightBadgeBorder;
            TxtDayOfWeek.Foreground = LightDayFg;
            TxtTime.Foreground = LightDateFg;
            PillShadow.Color = MediaColor.FromRgb(0x50, 0x50, 0x50);
            PillShadow.Opacity = 0.10;

            // 右键菜单浅色自适应
            Resources["MenuBgBrush"] = new SolidColorBrush(MediaColor.FromArgb(0xF2, 0xF6, 0xF6, 0xF9));
            Resources["MenuBorderBrush"] = new SolidColorBrush(MediaColor.FromArgb(0x30, 0x00, 0x00, 0x00));
            Resources["MenuItemFgBrush"] = new SolidColorBrush(MediaColor.FromRgb(0x11, 0x11, 0x13));
            Resources["MenuItemHoverBrush"] = new SolidColorBrush(MediaColor.FromArgb(0x15, 0x00, 0x00, 0x00));
            Resources["MenuSeparatorBrush"] = new SolidColorBrush(MediaColor.FromArgb(0x18, 0x00, 0x00, 0x00));
        }
    }

    private void UpdateDateTimeText()
    {
        try
        {
            DateTime now = DateTime.Now;
            string dateText = now.ToString(_config.DateFormat);
            string dayText = now.ToString(_config.DayOfWeekFormat);
            string timeText = now.ToString(_config.TimeFormat);

            if (TxtDate.Text != dateText) TxtDate.Text = dateText;
            if (TxtDayOfWeek.Text != dayText) TxtDayOfWeek.Text = dayText;

            if (_config.ShowTime)
            {
                if (Divider2.Visibility != Visibility.Visible) Divider2.Visibility = Visibility.Visible;
                if (TxtTime.Visibility != Visibility.Visible) TxtTime.Visibility = Visibility.Visible;
                if (TxtTime.Text != timeText) TxtTime.Text = timeText;
            }
            else
            {
                if (Divider2.Visibility != Visibility.Collapsed) Divider2.Visibility = Visibility.Collapsed;
                if (TxtTime.Visibility != Visibility.Collapsed) TxtTime.Visibility = Visibility.Collapsed;
            }
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
                // 小任务栏适配：当任务栏高度小于 40DIP 时（常见 32px / 36px 小任务栏）
                if (pos.Value.TaskbarHeight < 40)
                {
                    PillContainer.Padding = new Thickness(10, 1.5, 10, 1.5);
                    TxtDate.FontSize = 12.0;
                    TxtDayOfWeek.FontSize = 12.0;
                    TxtTime.FontSize = 12.0;
                    Divider.Height = 11;
                    Divider2.Height = 11;
                }
                else
                {
                    PillContainer.Padding = new Thickness(13, 3, 13, 3);
                    TxtDate.FontSize = 13.5;
                    TxtDayOfWeek.FontSize = 13.5;
                    TxtTime.FontSize = 13.5;
                    Divider.Height = 13;
                    Divider2.Height = 13;
                }

                // 防震荡微漂移阈值
                if (Math.Abs(this.Left - pos.Value.X) > 1.0) this.Left = pos.Value.X;
                if (Math.Abs(this.Top - pos.Value.Y) > 0.8) this.Top = pos.Value.Y;
            }
        }
        catch { }
    }

    private void MainWindow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        OpenMacContextMenu();
    }

    public void OpenMacContextMenu()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetForegroundWindow(hwnd);

        _contextMenu = Resources["MacContextMenu"] as ContextMenu;
        if (_contextMenu == null) return;

        _contextMenu.Items.Clear();

        var itemStyle = Resources["MacMenuItemStyle"] as Style;
        var separatorStyle = Resources["MacSeparatorStyle"] as Style;

        // 标题项
        var titleItem = new MenuItem
        {
            Header = $"Date (v{UpdateService.CurrentVersion.ToString(3)})",
            IsEnabled = false,
            Style = itemStyle
        };
        _contextMenu.Items.Add(titleItem);
        _contextMenu.Items.Add(new Separator { Style = separatorStyle });

        // 外观选项
        var autoItem = new MenuItem
        {
            Header = "跟随系统 (Auto)",
            IsChecked = _config.ThemeMode.Equals("Auto", StringComparison.OrdinalIgnoreCase),
            Style = itemStyle
        };
        autoItem.Click += (s, e) => SetTheme("Auto");

        var darkItem = new MenuItem
        {
            Header = "深色模式 (Dark)",
            IsChecked = _config.ThemeMode.Equals("Dark", StringComparison.OrdinalIgnoreCase),
            Style = itemStyle
        };
        darkItem.Click += (s, e) => SetTheme("Dark");

        var lightItem = new MenuItem
        {
            Header = "浅色模式 (Light)",
            IsChecked = _config.ThemeMode.Equals("Light", StringComparison.OrdinalIgnoreCase),
            Style = itemStyle
        };
        lightItem.Click += (s, e) => SetTheme("Light");

        _contextMenu.Items.Add(autoItem);
        _contextMenu.Items.Add(darkItem);
        _contextMenu.Items.Add(lightItem);
        _contextMenu.Items.Add(new Separator { Style = separatorStyle });

        // 开机自启动
        var autoStartItem = new MenuItem
        {
            Header = "开机自启动",
            IsChecked = AutoStartService.IsAutoStartEnabled(),
            Style = itemStyle
        };
        autoStartItem.Click += (s, e) =>
        {
            bool next = !AutoStartService.IsAutoStartEnabled();
            if (AutoStartService.SetAutoStart(next))
            {
                _config.AutoStart = next;
                _config.Save();
            }
        };
        _contextMenu.Items.Add(autoStartItem);
        _contextMenu.Items.Add(new Separator { Style = separatorStyle });

        // 显示实时时间
        var showTimeItem = new MenuItem
        {
            Header = "显示实时时间 (时:分)",
            IsChecked = _config.ShowTime,
            Style = itemStyle
        };
        showTimeItem.Click += (s, e) =>
        {
            _config.ShowTime = !_config.ShowTime;
            _config.Save();
            UpdateDateTimeText();
            UpdateLayout();
            UpdatePosition();
        };
        _contextMenu.Items.Add(showTimeItem);
        _contextMenu.Items.Add(new Separator { Style = separatorStyle });

        // 检查更新
        var updateItem = new MenuItem
        {
            Header = "检查更新...",
            Style = itemStyle
        };
        updateItem.Click += async (s, e) =>
        {
            await UpdateService.CheckForUpdatesAsync(_config, isManual: true);
        };
        _contextMenu.Items.Add(updateItem);

        // 打开程序目录
        var folderItem = new MenuItem
        {
            Header = "打开程序目录",
            Style = itemStyle
        };
        folderItem.Click += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppContext.BaseDirectory,
                    UseShellExecute = true
                });
            }
            catch { }
        };
        _contextMenu.Items.Add(folderItem);
        _contextMenu.Items.Add(new Separator { Style = separatorStyle });

        // 退出
        var exitItem = new MenuItem
        {
            Header = "退出",
            Style = itemStyle
        };
        exitItem.Click += (s, e) => App.ExitApp();
        _contextMenu.Items.Add(exitItem);

        _contextMenu.PlacementTarget = this;
        _contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _contextMenu.IsOpen = true;
    }

    private void SetTheme(string mode)
    {
        _config.ThemeMode = mode;
        _config.Save();
        ThemeService.SetMode(mode);
    }
}
