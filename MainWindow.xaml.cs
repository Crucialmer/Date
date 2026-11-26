using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Date.Models;
using Date.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaPoint = System.Windows.Point;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace Date;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static readonly int WM_TASKBARCREATED = TaskbarLocator.RegisterWindowMessage("TaskbarCreated");

    private readonly AppConfig _config;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _positionTimer;
    private DispatcherTimer? _startupTimer;
    private HwndSource? _hwndSource;
    private ContextMenu? _contextMenu;

    private bool _isDarkMode = true;
    private bool _isClosing;
    private bool _isStartingUp = true;
    private bool? _isCompactTaskbar;

    // ─── macOS 26 Liquid Glass 深色材质 (Obsidian Liquid Glass，全部冻结零开销) ───
    private static readonly LinearGradientBrush DarkPillBg;
    private static readonly LinearGradientBrush DarkPillBorder;
    private static readonly LinearGradientBrush DarkGlassSheen;
    private static readonly SolidColorBrush DarkHoverOverlay;
    private static readonly SolidColorBrush DarkDateFg;
    private static readonly LinearGradientBrush DarkBadgeBg;
    private static readonly LinearGradientBrush DarkBadgeBorder;
    private static readonly SolidColorBrush DarkDayFg;

    private static readonly LinearGradientBrush DarkMenuBg;
    private static readonly LinearGradientBrush DarkMenuBorder;
    private static readonly LinearGradientBrush DarkMenuSheen;
    private static readonly SolidColorBrush DarkMenuFg;
    private static readonly SolidColorBrush DarkMenuSecondaryFg;
    private static readonly SolidColorBrush DarkMenuHover;
    private static readonly SolidColorBrush DarkMenuSeparator;
    private static readonly SolidColorBrush DarkAccent;
    private static readonly SolidColorBrush DarkVersionPillBg;

    // ─── macOS 26 Liquid Glass 浅色材质 (Crystal Liquid Glass，全部冻结零开销) ───
    private static readonly LinearGradientBrush LightPillBg;
    private static readonly LinearGradientBrush LightPillBorder;
    private static readonly LinearGradientBrush LightGlassSheen;
    private static readonly SolidColorBrush LightHoverOverlay;
    private static readonly SolidColorBrush LightDateFg;
    private static readonly LinearGradientBrush LightBadgeBg;
    private static readonly LinearGradientBrush LightBadgeBorder;
    private static readonly SolidColorBrush LightDayFg;

    private static readonly LinearGradientBrush LightMenuBg;
    private static readonly LinearGradientBrush LightMenuBorder;
    private static readonly LinearGradientBrush LightMenuSheen;
    private static readonly SolidColorBrush LightMenuFg;
    private static readonly SolidColorBrush LightMenuSecondaryFg;
    private static readonly SolidColorBrush LightMenuHover;
    private static readonly SolidColorBrush LightMenuSeparator;
    private static readonly SolidColorBrush LightAccent;
    private static readonly SolidColorBrush LightVersionPillBg;

    // SF-Symbol 风格矢量图标路径（13x13 坐标系，全部冻结复用）
    private static readonly Geometry IconAutoTheme = FreezeGeometry("M6.5,1.5 A5,5 0 1,0 6.5,11.5 A5,5 0 1,0 6.5,1.5 M6.5,1.5 L6.5,11.5");
    private static readonly Geometry IconDarkTheme = FreezeGeometry("M10.4,8.2 A4.5,4.5 0 1,1 5.3,2.5 A3.5,3.5 0 0,0 10.4,8.2 Z");
    private static readonly Geometry IconLightTheme = FreezeGeometry("M6.5,4.1 A2.4,2.4 0 1,0 6.5,8.9 A2.4,2.4 0 1,0 6.5,4.1 M6.5,1.3 L6.5,2.4 M6.5,10.6 L6.5,11.7 M1.3,6.5 L2.4,6.5 M10.6,6.5 L11.7,6.5 M2.9,2.9 L3.7,3.7 M9.3,9.3 L10.1,10.1 M10.1,2.9 L9.3,3.7 M3.7,9.3 L2.9,10.1");
    private static readonly Geometry IconAutoStart = FreezeGeometry("M7.3,1.5 L3.2,7.0 L6.5,7.0 L5.7,11.5 L9.8,6.0 L6.5,6.0 Z");
    private static readonly Geometry IconClock = FreezeGeometry("M6.5,1.5 A5,5 0 1,0 6.5,11.5 A5,5 0 1,0 6.5,1.5 M6.5,3.6 L6.5,6.5 L8.7,7.8");
    private static readonly Geometry IconRefresh = FreezeGeometry("M10.7,5.4 A4.5,4.5 0 1,0 10.4,8.6 M10.9,2.4 L10.9,5.6 L7.8,5.6");
    private static readonly Geometry IconShieldCheck = FreezeGeometry("M6.5,1.5 L10.7,3.2 L10.7,6.4 C10.7,9.0 8.9,10.7 6.5,11.6 C4.1,10.7 2.3,9.0 2.3,6.4 L2.3,3.2 Z M4.6,6.5 L6.0,7.9 L8.7,5.1");
    private static readonly Geometry IconFolder = FreezeGeometry("M1.6,3.5 C1.6,2.9 2.0,2.5 2.6,2.5 L5.1,2.5 L6.3,3.9 L10.4,3.9 C11.0,3.9 11.4,4.3 11.4,4.9 L11.4,9.5 C11.4,10.1 11.0,10.5 10.4,10.5 L2.6,10.5 C2.0,10.5 1.6,10.1 1.6,9.5 Z");
    private static readonly Geometry IconExit = FreezeGeometry("M6.5,1.6 L6.5,6.1 M3.7,3.5 A4.3,4.3 0 1,0 9.3,3.5");

    static MainWindow()
    {
        // ── 深色 Liquid Glass ──
        DarkPillBg = CreateFrozenGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0xD8, 0x2B, 0x2B, 0x33), 0.0),
            (MediaColor.FromArgb(0xDF, 0x1D, 0x1D, 0x23), 0.48),
            (MediaColor.FromArgb(0xE4, 0x17, 0x17, 0x1D), 1.0));

        DarkPillBorder = CreateFrozenGradient(
            new MediaPoint(0.15, 0), new MediaPoint(0.85, 1),
            (MediaColor.FromArgb(0x6E, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 0.45),
            (MediaColor.FromArgb(0x3A, 0xFF, 0xFF, 0xFF), 1.0));

        DarkGlassSheen = CreateFrozenGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));

        DarkHoverOverlay = CreateFrozenBrush(MediaColor.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        DarkDateFg = CreateFrozenBrush(MediaColor.FromRgb(0xFF, 0xFF, 0xFF));
        DarkDayFg = CreateFrozenBrush(MediaColor.FromRgb(0xF2, 0xF2, 0xF7));

        DarkBadgeBg = CreateFrozenGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x12, 0xFF, 0xFF, 0xFF), 1.0));

        DarkBadgeBorder = CreateFrozenGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x36, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1.0));

        DarkMenuBg = CreateFrozenGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0xEC, 0x26, 0x26, 0x2D), 0.0),
            (MediaColor.FromArgb(0xF0, 0x1A, 0x1A, 0x20), 1.0));

        DarkMenuBorder = CreateFrozenGradient(
            new MediaPoint(0.1, 0), new MediaPoint(0.9, 1),
            (MediaColor.FromArgb(0x5A, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x20, 0xFF, 0xFF, 0xFF), 0.5),
            (MediaColor.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 1.0));

        DarkMenuSheen = CreateFrozenGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x16, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));

        DarkMenuFg = CreateFrozenBrush(MediaColor.FromRgb(0xF5, 0xF5, 0xF7));
        DarkMenuSecondaryFg = CreateFrozenBrush(MediaColor.FromArgb(0x99, 0xEB, 0xEB, 0xF5));
        DarkMenuHover = CreateFrozenBrush(MediaColor.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        DarkMenuSeparator = CreateFrozenBrush(MediaColor.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        DarkAccent = CreateFrozenBrush(MediaColor.FromRgb(0x0A, 0x84, 0xFF));
        DarkVersionPillBg = CreateFrozenBrush(MediaColor.FromArgb(0x26, 0x0A, 0x84, 0xFF));

        // ── 浅色 Liquid Glass ──
        LightPillBg = CreateFrozenGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0xF5, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0xEE, 0xF4, 0xF5, 0xF9), 0.52),
            (MediaColor.FromArgb(0xE8, 0xEB, 0xED, 0xF4), 1.0));

        LightPillBorder = CreateFrozenGradient(
            new MediaPoint(0.15, 0), new MediaPoint(0.85, 1),
            (MediaColor.FromArgb(0xEE, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x22, 0x00, 0x00, 0x00), 0.45),
            (MediaColor.FromArgb(0x32, 0x00, 0x00, 0x00), 1.0));

        LightGlassSheen = CreateFrozenGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x78, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));

        LightHoverOverlay = CreateFrozenBrush(MediaColor.FromArgb(0x0A, 0x00, 0x00, 0x00));
        LightDateFg = CreateFrozenBrush(MediaColor.FromRgb(0x11, 0x11, 0x14));
        LightDayFg = CreateFrozenBrush(MediaColor.FromRgb(0x2C, 0x2C, 0x30));

        LightBadgeBg = CreateFrozenGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x0D, 0x00, 0x00, 0x00), 0.0),
            (MediaColor.FromArgb(0x06, 0x00, 0x00, 0x00), 1.0));

        LightBadgeBorder = CreateFrozenGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0xCC, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x16, 0x00, 0x00, 0x00), 1.0));

        LightMenuBg = CreateFrozenGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0xF6, 0xFB, 0xFB, 0xFD), 0.0),
            (MediaColor.FromArgb(0xF0, 0xF1, 0xF2, 0xF7), 1.0));

        LightMenuBorder = CreateFrozenGradient(
            new MediaPoint(0.1, 0), new MediaPoint(0.9, 1),
            (MediaColor.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x1E, 0x00, 0x00, 0x00), 0.5),
            (MediaColor.FromArgb(0x2C, 0x00, 0x00, 0x00), 1.0));

        LightMenuSheen = CreateFrozenGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));

        LightMenuFg = CreateFrozenBrush(MediaColor.FromRgb(0x14, 0x14, 0x18));
        LightMenuSecondaryFg = CreateFrozenBrush(MediaColor.FromArgb(0x99, 0x3C, 0x3C, 0x43));
        LightMenuHover = CreateFrozenBrush(MediaColor.FromArgb(0x12, 0x00, 0x00, 0x00));
        LightMenuSeparator = CreateFrozenBrush(MediaColor.FromArgb(0x18, 0x00, 0x00, 0x00));
        LightAccent = CreateFrozenBrush(MediaColor.FromRgb(0x00, 0x7A, 0xFF));
        LightVersionPillBg = CreateFrozenBrush(MediaColor.FromArgb(0x18, 0x00, 0x7A, 0xFF));
    }

    private static SolidColorBrush CreateFrozenBrush(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateFrozenGradient(
        MediaPoint start,
        MediaPoint end,
        params (MediaColor Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }
        brush.Freeze();
        return brush;
    }

    private static Geometry FreezeGeometry(string pathData)
    {
        var geometry = Geometry.Parse(pathData);
        geometry.Freeze();
        return geometry;
    }

    public MainWindow(AppConfig config)
    {
        _config = config;
        InitializeComponent();

        _clockTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) =>
        {
            bool layoutChanged = UpdateDateTimeText();
            if (!_isStartingUp && !_isClosing)
            {
                if (layoutChanged)
                {
                    UpdateLayout();
                    UpdatePosition();
                }
                else
                {
                    TaskbarLocator.EnsureVisibleAndTopmostIfNeeded(this);
                }
            }
        };

        _positionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _positionTimer.Tick += (_, _) => UpdatePosition();

        Loaded += MainWindow_Loaded;
        MouseRightButtonUp += MainWindow_MouseRightButtonUp;
        PillContainer.MouseEnter += (_, _) => AnimateHover(1.0, 140);
        PillContainer.MouseLeave += (_, _) => AnimateHover(0.0, 180);
    }

    private void AnimateHover(double targetOpacity, int durationMs)
    {
        var anim = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        HoverOverlay.BeginAnimation(OpacityProperty, anim);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        TaskbarLocator.ApplyWindowStyles(this);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            _hwndSource = source;
            _hwndSource.AddHook(WndProc);
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        TaskbarLocator.ResetCache();
        _isCompactTaskbar = null;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, UpdatePosition);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }
    }

    public void PrepareClose()
    {
        _isClosing = true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _clockTimer.Stop();
        _positionTimer.Stop();
        _startupTimer?.Stop();

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        Microsoft.Win32.SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        ThemeService.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged += OnThemeChanged;
        ApplyTheme(ThemeService.IsDarkMode());

        Microsoft.Win32.SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;

        UpdateDateTimeText();
        UpdatePosition();

        _clockTimer.Start();

        // 开机防跳动静默沉淀机制：开机 2 分钟内静默等待托盘图标定型后再一次精确归位淡入
        bool isSystemBoot = Environment.TickCount64 < 120_000;
        int delayMs = isSystemBoot ? 3800 : 320;

        _startupTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(delayMs)
        };
        _startupTimer.Tick += (_, _) =>
        {
            _startupTimer.Stop();
            if (_isClosing) return;

            _isStartingUp = false;
            UpdatePosition();
            TaskbarLocator.EnsureVisibleAndTopmost(this);

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (_, _) =>
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = 1.0;
            };
            BeginAnimation(OpacityProperty, fadeIn);

            _positionTimer.Start();
        };
        _startupTimer.Start();
    }

    private void SystemEvents_SessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock && !_isClosing)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                if (_isClosing) return;
                TaskbarLocator.EnsureVisibleAndTopmost(this);
                UpdatePosition();
            });
        }
    }

    private void SystemEvents_PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume && !_isClosing)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                if (_isClosing) return;
                TaskbarLocator.EnsureVisibleAndTopmost(this);
                UpdatePosition();
            });
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DISPLAYCHANGE = 0x007E;
        const int WM_SETTINGCHANGE = 0x001A;
        const int WM_DWMCOMPOSITIONCHANGED = 0x031E;
        const int WM_SHOWWINDOW = 0x0018;
        const int WM_WINDOWPOSCHANGING = 0x0046;
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_MINIMIZE = 0xF020;

        // 1. 任务栏重启广播通知（重新挂载宿主并重置定位缓存）
        if (WM_TASKBARCREATED != 0 && msg == WM_TASKBARCREATED)
        {
            TaskbarLocator.ApplyWindowStyles(this);
            TaskbarLocator.ResetCache();
            _isCompactTaskbar = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, UpdatePosition);
        }

        // 2. 阻止系统或桌面切换尝试隐藏胶囊窗口（如 Win+D / 显示桌面）
        if (msg == WM_SHOWWINDOW && wParam == IntPtr.Zero && !_isClosing)
        {
            handled = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                if (!_isClosing)
                {
                    TaskbarLocator.EnsureVisibleAndTopmost(this);
                }
            });
            return IntPtr.Zero;
        }

        // 3. 拦截并阻止窗口最小化系统命令（64位安全无溢出）
        if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_MINIMIZE)
        {
            handled = true;
            return IntPtr.Zero;
        }

        // 4. 拦截窗口位置及 Z 序变动：防御 Win+D 隐藏、-32000 最小化位移与任务栏压制
        if (msg == WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero && !_isClosing)
        {
            try
            {
                var wp = Marshal.PtrToStructure<TaskbarLocator.WINDOWPOS>(lParam);

                if ((wp.flags & TaskbarLocator.SWP_HIDEWINDOW) != 0)
                {
                    wp.flags &= ~TaskbarLocator.SWP_HIDEWINDOW;
                    wp.flags |= TaskbarLocator.SWP_SHOWWINDOW;
                }

                if (wp.x <= -30000 || wp.y <= -30000)
                {
                    wp.flags |= TaskbarLocator.SWP_NOMOVE | TaskbarLocator.SWP_NOSIZE;
                }

                wp.hwndInsertAfter = TaskbarLocator.HWND_TOPMOST;
                wp.flags |= TaskbarLocator.SWP_NOACTIVATE;
                wp.flags &= ~TaskbarLocator.SWP_NOZORDER;

                Marshal.StructureToPtr(wp, lParam, false);
            }
            catch { }
        }

        // 5. 显示设置、系统设置或 DWM 合成重置时重测位置
        if (msg is WM_DISPLAYCHANGE or WM_SETTINGCHANGE or WM_DWMCOMPOSITIONCHANGED)
        {
            if (msg == WM_DISPLAYCHANGE)
            {
                TaskbarLocator.ResetCache();
                _isCompactTaskbar = null;
            }
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                if (_isClosing) return;
                TaskbarLocator.EnsureVisibleAndTopmost(this);
                UpdatePosition();
            });
        }

        return IntPtr.Zero;
    }

    private void OnThemeChanged(bool isDarkMode)
    {
        if (_isClosing) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isClosing) ApplyTheme(isDarkMode);
        });
    }

    public void ApplyTheme(bool isDarkMode)
    {
        _isDarkMode = isDarkMode;

        if (_isDarkMode)
        {
            PillContainer.Background = DarkPillBg;
            PillContainer.BorderBrush = DarkPillBorder;
            GlassSheen.Background = DarkGlassSheen;
            HoverOverlay.Background = DarkHoverOverlay;
            TxtDate.Foreground = DarkDateFg;
            DayBadge.Background = DarkBadgeBg;
            DayBadge.BorderBrush = DarkBadgeBorder;
            TxtDayOfWeek.Foreground = DarkDayFg;
            TxtTime.Foreground = DarkDateFg;
            PillShadow.Color = MediaColor.FromRgb(0x00, 0x00, 0x00);
            PillShadow.Opacity = 0.24;

            Resources["MenuBgBrush"] = DarkMenuBg;
            Resources["MenuBorderBrush"] = DarkMenuBorder;
            Resources["MenuSheenBrush"] = DarkMenuSheen;
            Resources["MenuItemFgBrush"] = DarkMenuFg;
            Resources["MenuSecondaryFgBrush"] = DarkMenuSecondaryFg;
            Resources["MenuItemHoverBrush"] = DarkMenuHover;
            Resources["MenuSeparatorBrush"] = DarkMenuSeparator;
            Resources["MenuAccentBrush"] = DarkAccent;
            Resources["MenuVersionPillBgBrush"] = DarkVersionPillBg;
        }
        else
        {
            PillContainer.Background = LightPillBg;
            PillContainer.BorderBrush = LightPillBorder;
            GlassSheen.Background = LightGlassSheen;
            HoverOverlay.Background = LightHoverOverlay;
            TxtDate.Foreground = LightDateFg;
            DayBadge.Background = LightBadgeBg;
            DayBadge.BorderBrush = LightBadgeBorder;
            TxtDayOfWeek.Foreground = LightDayFg;
            TxtTime.Foreground = LightDateFg;
            PillShadow.Color = MediaColor.FromRgb(0x40, 0x45, 0x55);
            PillShadow.Opacity = 0.12;

            Resources["MenuBgBrush"] = LightMenuBg;
            Resources["MenuBorderBrush"] = LightMenuBorder;
            Resources["MenuSheenBrush"] = LightMenuSheen;
            Resources["MenuItemFgBrush"] = LightMenuFg;
            Resources["MenuSecondaryFgBrush"] = LightMenuSecondaryFg;
            Resources["MenuItemHoverBrush"] = LightMenuHover;
            Resources["MenuSeparatorBrush"] = LightMenuSeparator;
            Resources["MenuAccentBrush"] = LightAccent;
            Resources["MenuVersionPillBgBrush"] = LightVersionPillBg;
        }
    }

    private bool UpdateDateTimeText()
    {
        bool widthMightChange = false;
        try
        {
            DateTime now = DateTime.Now;
            string dateText = now.ToString(_config.DateFormat);
            string dayText = now.ToString(_config.DayOfWeekFormat);
            string timeText = now.ToString(_config.TimeFormat);

            if (TxtDate.Text != dateText)
            {
                if (TxtDate.Text.Length != dateText.Length) widthMightChange = true;
                TxtDate.Text = dateText;
            }

            if (TxtDayOfWeek.Text != dayText)
            {
                if (TxtDayOfWeek.Text.Length != dayText.Length) widthMightChange = true;
                TxtDayOfWeek.Text = dayText;
            }

            if (_config.ShowTime)
            {
                if (TxtTime.Visibility != Visibility.Visible)
                {
                    TxtTime.Visibility = Visibility.Visible;
                    widthMightChange = true;
                }
                if (TxtTime.Text != timeText)
                {
                    if (TxtTime.Text.Length != timeText.Length) widthMightChange = true;
                    TxtTime.Text = timeText;
                }
            }
            else if (TxtTime.Visibility != Visibility.Collapsed)
            {
                TxtTime.Visibility = Visibility.Collapsed;
                widthMightChange = true;
            }
        }
        catch { }

        return widthMightChange;
    }

    private void ApplyTaskbarDensity(bool isCompact)
    {
        if (_isCompactTaskbar == isCompact) return;
        _isCompactTaskbar = isCompact;

        if (isCompact)
        {
            PillContainer.CornerRadius = new CornerRadius(11.5);
            PillContainer.Margin = new Thickness(5, 2, 5, 2);
            GlassSheen.CornerRadius = new CornerRadius(10.5, 10.5, 6, 6);
            GlassSheen.Height = 10;
            HoverOverlay.CornerRadius = new CornerRadius(10.5);
            ContentStack.Margin = new Thickness(10, 1.5, 10, 1.5);
            DayBadge.CornerRadius = new CornerRadius(7);
            DayBadge.Padding = new Thickness(6, 1, 6, 1);
            DayBadge.Margin = new Thickness(6.5, 0, 0, 0);
            TxtTime.Margin = new Thickness(6.5, 0, 0, 0);
            TxtDate.FontSize = 12.5;
            TxtDayOfWeek.FontSize = 12.5;
            TxtTime.FontSize = 12.5;
            PillShadow.BlurRadius = 6;
        }
        else
        {
            PillContainer.CornerRadius = new CornerRadius(14);
            PillContainer.Margin = new Thickness(6, 4, 6, 4);
            GlassSheen.CornerRadius = new CornerRadius(13, 13, 8, 8);
            GlassSheen.Height = 14;
            HoverOverlay.CornerRadius = new CornerRadius(13);
            ContentStack.Margin = new Thickness(13, 3.5, 13, 3.5);
            DayBadge.CornerRadius = new CornerRadius(8.5);
            DayBadge.Padding = new Thickness(7.5, 1.5, 7.5, 1.5);
            DayBadge.Margin = new Thickness(8, 0, 0, 0);
            TxtTime.Margin = new Thickness(8, 0, 0, 0);
            TxtDate.FontSize = 14.0;
            TxtDayOfWeek.FontSize = 14.0;
            TxtTime.FontSize = 14.0;
            PillShadow.BlurRadius = 8;
        }

        UpdateLayout();
    }

    public void UpdatePosition()
    {
        if (_isClosing) return;

        try
        {
            // 先依据任务栏高度应用标准/紧凑排版，再以最新 ActualWidth/ActualHeight 精确计算坐标
            double taskbarHeight = TaskbarLocator.GetTaskbarHeightDip(this);
            ApplyTaskbarDensity(taskbarHeight < 40);

            var pos = TaskbarLocator.CalculatePosition(this, _config.OffsetLeft, PillContainer.Margin.Right);
            if (pos.HasValue)
            {
                if (Math.Abs(Left - pos.Value.X) > 0.8) Left = pos.Value.X;
                if (Math.Abs(Top - pos.Value.Y) > 0.6) Top = pos.Value.Y;
            }

            if (!_isStartingUp)
            {
                TaskbarLocator.EnsureVisibleAndTopmostIfNeeded(this);
                if (Opacity < 1.0)
                {
                    Opacity = 1.0;
                }
            }
        }
        catch { }
    }

    private void MainWindow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        OpenMacContextMenu();
    }

    private static Viewbox CreateMenuIcon(Geometry geometry, MediaBrush strokeBrush)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = strokeBrush,
            StrokeThickness = 1.25,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            SnapsToDevicePixels = false
        };

        var canvas = new Canvas { Width = 13, Height = 13 };
        canvas.Children.Add(path);

        return new Viewbox
        {
            Width = 13.5,
            Height = 13.5,
            Stretch = Stretch.Uniform,
            Child = canvas
        };
    }

    public void OpenMacContextMenu()
    {
        if (_isClosing) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }

        _contextMenu = Resources["MacContextMenu"] as ContextMenu;
        if (_contextMenu == null) return;

        _contextMenu.Items.Clear();

        var headerStyle = Resources["MacMenuHeaderStyle"] as Style;
        var itemStyle = Resources["MacMenuItemStyle"] as Style;
        var separatorStyle = Resources["MacSeparatorStyle"] as Style;
        MediaBrush iconBrush = _isDarkMode ? DarkMenuSecondaryFg : LightMenuSecondaryFg;
        MediaBrush accentBrush = _isDarkMode ? DarkAccent : LightAccent;
        MediaBrush versionPillBg = _isDarkMode ? DarkVersionPillBg : LightVersionPillBg;

        // macOS 26 精致应用标题栏 (Date + 版本微胶囊)
        var headerPanel = new DockPanel { LastChildFill = false };
        var appNameTxt = new TextBlock
        {
            Text = "Date",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var versionBadge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = versionPillBg,
            Padding = new Thickness(6, 1.5, 6, 1.5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"v{UpdateService.CurrentVersion.ToString(3)}",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = accentBrush
            }
        };
        DockPanel.SetDock(appNameTxt, Dock.Left);
        DockPanel.SetDock(versionBadge, Dock.Right);
        headerPanel.Children.Add(appNameTxt);
        headerPanel.Children.Add(versionBadge);

        _contextMenu.Items.Add(new MenuItem { Header = headerPanel, Style = headerStyle });
        _contextMenu.Items.Add(new Separator { Style = separatorStyle });

        // 外观模式
        var autoItem = CreateActionItem(
            "跟随系统 (Auto)",
            IconAutoTheme,
            iconBrush,
            itemStyle,
            isChecked: _config.ThemeMode.Equals("Auto", StringComparison.OrdinalIgnoreCase),
            onClick: () => SetTheme("Auto"));

        var darkItem = CreateActionItem(
            "深色模式 (Dark)",
            IconDarkTheme,
            iconBrush,
            itemStyle,
            isChecked: _config.ThemeMode.Equals("Dark", StringComparison.OrdinalIgnoreCase),
            onClick: () => SetTheme("Dark"));

        var lightItem = CreateActionItem(
            "浅色模式 (Light)",
            IconLightTheme,
            iconBrush,
            itemStyle,
            isChecked: _config.ThemeMode.Equals("Light", StringComparison.OrdinalIgnoreCase),
            onClick: () => SetTheme("Light"));

        _contextMenu.Items.Add(autoItem);
        _contextMenu.Items.Add(darkItem);
        _contextMenu.Items.Add(lightItem);
        _contextMenu.Items.Add(new Separator { Style = separatorStyle });

        // 显示实时时间
        var showTimeItem = CreateActionItem(
            "显示实时时间 (时:分)",
            IconClock,
            iconBrush,
            itemStyle,
            isChecked: _config.ShowTime,
            onClick: () =>
            {
                _config.ShowTime = !_config.ShowTime;
                _config.Save();
                UpdateDateTimeText();
                UpdateLayout();
                UpdatePosition();
            });

        // 开机自启动
        var autoStartItem = CreateActionItem(
            "开机自启动",
            IconAutoStart,
            iconBrush,
            itemStyle,
            isChecked: AutoStartService.IsAutoStartEnabled(),
            onClick: () =>
            {
                bool next = !AutoStartService.IsAutoStartEnabled();
                if (AutoStartService.SetAutoStart(next))
                {
                    _config.AutoStart = next;
                    _config.Save();
                }
            });

        _contextMenu.Items.Add(showTimeItem);
        _contextMenu.Items.Add(autoStartItem);
        _contextMenu.Items.Add(new Separator { Style = separatorStyle });

        // 检查更新
        var updateItem = CreateActionItem(
            "检查更新...",
            IconRefresh,
            iconBrush,
            itemStyle,
            onClick: async () => await UpdateService.CheckForUpdatesAsync(_config, isManual: true));

        // 启动时自动检查更新
        var autoUpdateItem = CreateActionItem(
            "启动时自动检查更新",
            IconShieldCheck,
            iconBrush,
            itemStyle,
            isChecked: _config.CheckUpdatesOnStartup,
            onClick: () =>
            {
                _config.CheckUpdatesOnStartup = !_config.CheckUpdatesOnStartup;
                _config.Save();
            });

        // 打开程序目录
        var folderItem = CreateActionItem(
            "打开程序目录",
            IconFolder,
            iconBrush,
            itemStyle,
            onClick: () =>
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
            });

        _contextMenu.Items.Add(updateItem);
        _contextMenu.Items.Add(autoUpdateItem);
        _contextMenu.Items.Add(folderItem);
        _contextMenu.Items.Add(new Separator { Style = separatorStyle });

        // 退出
        var exitItem = CreateActionItem(
            "退出 Date",
            IconExit,
            iconBrush,
            itemStyle,
            onClick: App.ExitApp);
        _contextMenu.Items.Add(exitItem);

        _contextMenu.PlacementTarget = this;
        _contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _contextMenu.IsOpen = true;
    }

    private static MenuItem CreateActionItem(
        string header,
        Geometry iconGeometry,
        MediaBrush iconBrush,
        Style? style,
        Action onClick,
        bool isChecked = false)
    {
        var item = new MenuItem
        {
            Header = header,
            Icon = CreateMenuIcon(iconGeometry, iconBrush),
            IsChecked = isChecked,
            Style = style
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void SetTheme(string mode)
    {
        _config.ThemeMode = mode;
        _config.Save();
        ThemeService.SetMode(mode);
    }
}
