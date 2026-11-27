using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;

namespace Date.Services;

public static class TaskbarLocator
{
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int GWL_EXSTYLE = -20;
    public const int GWL_HWNDPARENT = -8;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;

    public const int SW_SHOWNOACTIVATE = 4;
    private const uint GW_HWNDPREV = 3;
    private const uint ABM_GETSTATE = 0x00000004;
    private const int ABS_AUTOHIDE = 0x0000001;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    // 缓存最后一次有效托盘与任务栏坐标，防止系统重绘时的瞬间塌陷与坐标突跳
    private static double _lastKnownGoodTrayLeftDip = -1;
    private static double _lastKnownGoodTaskbarLeftDip;
    private static double _lastKnownGoodTaskbarRightDip = -1;
    private static double _lastKnownGoodTaskbarTopDip = -1;
    private static double _lastKnownGoodTaskbarHeightDip = 48;

    // Windows 11 XAML 托盘（含向上展开箭头 ChevronIconView 与 NotifyIconView）真实物理左边界缓存
    private static readonly object UiaCacheLock = new();
    private static double _uiaTrayLeftPx = double.NaN;
    private static bool _uiaFoundChevron;
    private static long _uiaLastUpdatedTick;
    private static int _uiaProbeInFlight;

    private static readonly System.Windows.Automation.Condition UiaTrayQueryCondition = new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
        new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"),
        new PropertyCondition(AutomationElement.AutomationIdProperty, "NotifyItemIcon"),
        new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.ChevronIconView"),
        new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NotifyIconView"),
        new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.IconView"),
        new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.OmniButton"),
        new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NormalButton"));

    /// <summary>
    /// 重置位置缓存（用于分辨率改变、DPI 变更、任务栏重启或多显示器切换时重新测量）
    /// </summary>
    public static void ResetCache()
    {
        _lastKnownGoodTrayLeftDip = -1;
        _lastKnownGoodTaskbarLeftDip = 0;
        _lastKnownGoodTaskbarRightDip = -1;
        _lastKnownGoodTaskbarTopDip = -1;
        _lastKnownGoodTaskbarHeightDip = 48;

        lock (UiaCacheLock)
        {
            _uiaTrayLeftPx = double.NaN;
            _uiaFoundChevron = false;
            _uiaLastUpdatedTick = 0;
        }
    }

    /// <summary>
    /// 在后台线程通过 UI Automation 批量缓存扫描 Windows 11 XAML 托盘真实最左边界（精确涵盖向上展开箭头 ^ 与全部可见托盘图标）。
    /// 使用单飞防重入与 AutomationElementMode.None，绝不阻塞 WPF UI 线程、不持有跨进程 COM 句柄。
    /// </summary>
    public static void TriggerAsyncTrayBoundaryProbe(Action onBoundaryChanged)
    {
        if (Interlocked.CompareExchange(ref _uiaProbeInFlight, 1, 0) != 0)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                if (TryProbeXamlTrayLeftPx(out double probedLeftPx, out bool foundChevron))
                {
                    bool changed;
                    lock (UiaCacheLock)
                    {
                        changed = double.IsNaN(_uiaTrayLeftPx) || Math.Abs(_uiaTrayLeftPx - probedLeftPx) >= 1.0;
                        _uiaTrayLeftPx = probedLeftPx;
                        _uiaFoundChevron = foundChevron;
                        _uiaLastUpdatedTick = Environment.TickCount64;
                    }

                    if (changed)
                    {
                        onBoundaryChanged();
                    }
                }
            }
            catch
            {
                // UIA 在资源管理器重启等极端瞬间可能抛 COMException，静默忽略并由 Win32 兜底
            }
            finally
            {
                Interlocked.Exchange(ref _uiaProbeInFlight, 0);
            }
        });
    }

    private static bool TryProbeXamlTrayLeftPx(out double minLeftPx, out bool foundChevron)
    {
        minLeftPx = double.MaxValue;
        foundChevron = false;

        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out RECT taskbarRect))
            return false;

        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        if (taskbarWidth <= 200) return false;

        // 仅接受位于任务栏右侧合理区域的托盘项，绝不误匹配左侧开始菜单/搜索/居中应用图标
        double minValidLeft = taskbarRect.Left + taskbarWidth * 0.35;
        double maxItemWidth = taskbarWidth * 0.40;

        var cacheRequest = new CacheRequest
        {
            TreeScope = TreeScope.Element,
            TreeFilter = Automation.ControlViewCondition,
            AutomationElementMode = AutomationElementMode.None
        };
        cacheRequest.Add(AutomationElement.ClassNameProperty);
        cacheRequest.Add(AutomationElement.AutomationIdProperty);
        cacheRequest.Add(AutomationElement.NameProperty);
        cacheRequest.Add(AutomationElement.BoundingRectangleProperty);
        cacheRequest.Add(AutomationElement.IsOffscreenProperty);

        var taskbarEl = AutomationElement.FromHandle(taskbar);
        if (taskbarEl == null) return false;

        AutomationElementCollection elements;
        using (cacheRequest.Activate())
        {
            elements = taskbarEl.FindAll(TreeScope.Descendants, UiaTrayQueryCondition);
        }

        bool foundAny = false;
        for (int i = 0; i < elements.Count; i++)
        {
            try
            {
                var el = elements[i];
                var cached = el.Cached;

                if (cached.IsOffscreen) continue;

                Rect rect = cached.BoundingRectangle;
                if (rect.IsEmpty || rect.Width < 6 || rect.Height < 6 || rect.Width > maxItemWidth)
                    continue;

                if (rect.Bottom < taskbarRect.Top - 6 || rect.Top > taskbarRect.Bottom + 6)
                    continue;

                if (rect.Left < minValidLeft || rect.Right > taskbarRect.Right + 8)
                    continue;

                string cls = cached.ClassName ?? string.Empty;
                string aid = cached.AutomationId ?? string.Empty;
                string name = cached.Name ?? string.Empty;

                if (!IsSystemTrayItem(cls, aid, name, out bool isChevron))
                    continue;

                if (isChevron) foundChevron = true;

                if (rect.Left < minLeftPx)
                {
                    minLeftPx = rect.Left;
                    foundAny = true;
                }
            }
            catch
            {
                // 跳过遍历期间瞬间消失的托盘图标
            }
        }

        return foundAny && minLeftPx < taskbarRect.Right;
    }

    private static bool IsSystemTrayItem(string cls, string aid, string name, out bool isChevron)
    {
        isChevron = false;

        // 1. Windows 11 向上展开箭头（Chevron）精准识别
        if (cls.Equals("SystemTray.ChevronIconView", StringComparison.OrdinalIgnoreCase)
            || aid.Contains("Chevron", StringComparison.OrdinalIgnoreCase)
            || name.Contains("隐藏的图标", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Show hidden icons", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Notification Chevron", StringComparison.OrdinalIgnoreCase)
            || name.Contains("溢出", StringComparison.OrdinalIgnoreCase))
        {
            isChevron = true;
            return true;
        }

        // 2. Windows 11 XAML 托盘子项（剔除整栏级宽容器，仅保留真实可见托盘按钮/图标）
        if (cls.StartsWith("SystemTray.", StringComparison.OrdinalIgnoreCase))
        {
            if (cls.Contains("Frame", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("Container", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }

        // 3. 通用托盘 AutomationId（SystemTrayIcon / NotifyItemIcon / InputIndicator / Clock 等）
        if (aid.Equals("SystemTrayIcon", StringComparison.OrdinalIgnoreCase)
            || aid.Equals("NotifyItemIcon", StringComparison.OrdinalIgnoreCase)
            || aid.Contains("TrayIcon", StringComparison.OrdinalIgnoreCase)
            || aid.Contains("InputIndicator", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 结合 Win32 TrayNotifyWnd 及其所有可见子窗口（含经典 Button 折叠箭头）获取 Win32 层最左物理像素
    /// </summary>
    private static bool TryGetWin32TrayLeftPx(IntPtr taskbar, RECT taskbarRect, out double leftPx)
    {
        leftPx = double.MaxValue;
        if (taskbar == IntPtr.Zero) return false;

        IntPtr tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray == IntPtr.Zero || !GetWindowRect(tray, out RECT trayRect))
            return false;

        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        double minValidLeft = taskbarRect.Left + taskbarWidth * 0.35;

        bool found = false;
        if (trayRect.Left >= minValidLeft && trayRect.Left < taskbarRect.Right && (trayRect.Right - trayRect.Left) > 10)
        {
            leftPx = trayRect.Left;
            found = true;
        }

        // 同时枚举 TrayNotifyWnd 的可见子窗口（如独立 Button 箭头），防止子控件向左超出父窗口边界
        double childMinLeft = leftPx;
        EnumChildWindows(tray, (child, _) =>
        {
            if (IsWindowVisible(child) && GetWindowRect(child, out RECT cr))
            {
                int cw = cr.Right - cr.Left;
                int ch = cr.Bottom - cr.Top;
                if (cw >= 8 && ch >= 8 && cr.Left >= minValidLeft && cr.Left < childMinLeft && cr.Right <= taskbarRect.Right + 4)
                {
                    childMinLeft = cr.Left;
                }
            }
            return true;
        }, IntPtr.Zero);

        if (childMinLeft < double.MaxValue)
        {
            leftPx = childMinLeft;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// 在 OnSourceInitialized 阶段应用无激活工具窗口样式、挂载任务栏宿主以豁免“显示桌面”，并锁定最高层置顶
    /// </summary>
    public static void ApplyWindowStyles(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        // 将任务栏 Shell_TrayWnd 设为 Win32 宿主 (GWL_HWNDPARENT) 以彻底豁免 Win+D 显示桌面
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero)
        {
            if (IntPtr.Size == 8)
                SetWindowLongPtr(hwnd, GWL_HWNDPARENT, taskbar);
            else
                SetWindowLong(hwnd, GWL_HWNDPARENT, taskbar.ToInt32());
        }

        EnsureVisibleAndTopmost(window);
    }

    /// <summary>
    /// 零开销按需置顶检测：仅当窗口不可见、失去 TOPMOST 或被任务栏遮挡时才调用 Win32 重排
    /// </summary>
    public static void EnsureVisibleAndTopmostIfNeeded(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            bool isVisible = IsWindowVisible(hwnd);
            bool isTopmost = (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

            if (!isVisible || !isTopmost || IsCoveredByTaskbar(hwnd))
            {
                if (!isVisible)
                {
                    ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                }
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }
        catch { }
    }

    private static bool IsCoveredByTaskbar(IntPtr hwnd)
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return false;

        IntPtr cursor = GetWindow(hwnd, GW_HWNDPREV);
        int guard = 0;
        while (cursor != IntPtr.Zero && guard++ < 64)
        {
            if (cursor == taskbar)
                return true;
            cursor = GetWindow(cursor, GW_HWNDPREV);
        }
        return false;
    }

    /// <summary>
    /// 强制确保胶囊窗口可见并位于 TOPMOST 顶层
    /// </summary>
    public static void EnsureVisibleAndTopmost(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            if (!IsWindowVisible(hwnd))
            {
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            }
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        catch { }
    }

    /// <summary>
    /// 获取任务栏所在显示器的精确 DPI 缩放比（支持多屏混合 DPI，回退至 WPF PresentationSource）
    /// </summary>
    private static (double DpiX, double DpiY) GetEffectiveTaskbarDpi(Window window, IntPtr taskbar)
    {
        try
        {
            if (taskbar != IntPtr.Zero)
            {
                uint dpi = GetDpiForWindow(taskbar);
                if (dpi >= 96 && dpi <= 960)
                {
                    double scale = dpi / 96.0;
                    return (scale, scale);
                }
            }
        }
        catch { }

        var source = PresentationSource.FromVisual(window);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        return (dpiX, dpiY);
    }

    /// <summary>
    /// 检测任务栏是否开启了“自动隐藏”且当前处于完全收起状态
    /// </summary>
    private static bool IsTaskbarCurrentlyAutoHidden(IntPtr taskbar, RECT taskbarRect)
    {
        try
        {
            var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
            int state = SHAppBarMessage(ABM_GETSTATE, ref abd).ToInt32();
            if ((state & ABS_AUTOHIDE) == 0) return false;

            IntPtr monitor = MonitorFromWindow(taskbar, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return false;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref mi)) return false;

            // 底部任务栏收起时其 Top 贴近或超出显示器底边；顶部任务栏收起时其 Bottom 贴近顶边
            return taskbarRect.Top >= mi.rcMonitor.Bottom - 6
                || taskbarRect.Bottom <= mi.rcMonitor.Top + 6;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 仅探测当前任务栏高度（DIP），用于在计算最终窗口坐标前先确定标准/紧凑排版尺寸
    /// </summary>
    public static double GetTaskbarHeightDip(Window window)
    {
        try
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero && GetWindowRect(taskbar, out RECT rect) && (rect.Right - rect.Left) > 100)
            {
                var (_, dpiY) = GetEffectiveTaskbarDpi(window, taskbar);
                double heightDip = (rect.Bottom - rect.Top) / dpiY;
                _lastKnownGoodTaskbarHeightDip = heightDip;
                return heightDip;
            }
        }
        catch { }

        return _lastKnownGoodTaskbarHeightDip;
    }

    /// <summary>
    /// 双层融合（Windows 11 XAML UIA 真实托盘/展开箭头 + Win32 TrayNotifyWnd）精确计算悬浮胶囊位置。
    /// 始终以最靠左的真实托盘边界为准，扣除 Win11 顶部 1px 分隔线实现光学上下居中，并感知任务栏自动隐藏状态。
    /// </summary>
    public static (double X, double Y, double TaskbarHeight, bool IsTaskbarAutoHidden)? CalculatePosition(
        Window window,
        int offsetLeft,
        double visualRightMarginDip = 0)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return null;

            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            var (dpiX, dpiY) = GetEffectiveTaskbarDpi(window, taskbar);
            RECT taskbarRect = default;
            bool hasTaskbar = taskbar != IntPtr.Zero
                              && GetWindowRect(taskbar, out taskbarRect)
                              && (taskbarRect.Right - taskbarRect.Left) > 100;

            bool isAutoHidden = hasTaskbar && IsTaskbarCurrentlyAutoHidden(taskbar, taskbarRect);
            double taskbarLeftDip, taskbarRightDip, taskbarTopDip, taskbarHeightDip;

            if (hasTaskbar)
            {
                taskbarLeftDip = taskbarRect.Left / dpiX;
                taskbarRightDip = taskbarRect.Right / dpiX;
                taskbarTopDip = taskbarRect.Top / dpiY;
                taskbarHeightDip = (taskbarRect.Bottom - taskbarRect.Top) / dpiY;

                _lastKnownGoodTaskbarLeftDip = taskbarLeftDip;
                _lastKnownGoodTaskbarRightDip = taskbarRightDip;
                _lastKnownGoodTaskbarTopDip = taskbarTopDip;
                _lastKnownGoodTaskbarHeightDip = taskbarHeightDip;
            }
            else if (_lastKnownGoodTaskbarRightDip > 0)
            {
                taskbarLeftDip = _lastKnownGoodTaskbarLeftDip;
                taskbarRightDip = _lastKnownGoodTaskbarRightDip;
                taskbarTopDip = _lastKnownGoodTaskbarTopDip;
                taskbarHeightDip = _lastKnownGoodTaskbarHeightDip;
            }
            else
            {
                double fallbackX = SystemParameters.WorkArea.Right - window.ActualWidth - 220 - offsetLeft + visualRightMarginDip;
                double fallbackY = SystemParameters.WorkArea.Bottom
                                   + (SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Bottom - window.ActualHeight) / 2.0;
                return (fallbackX, fallbackY, 48, false);
            }

            // 读取 Win32 托盘左边界
            double win32TrayLeftPx = double.NaN;
            bool hasWin32 = hasTaskbar && TryGetWin32TrayLeftPx(taskbar, taskbarRect, out win32TrayLeftPx);

            // 读取最新的 UIA XAML 托盘（含向上展开箭头 ^）左边界
            double uiaTrayLeftPx;
            bool uiaFresh;
            bool uiaFoundChevron;
            lock (UiaCacheLock)
            {
                uiaTrayLeftPx = _uiaTrayLeftPx;
                uiaFoundChevron = _uiaFoundChevron;
                uiaFresh = !double.IsNaN(uiaTrayLeftPx) && (Environment.TickCount64 - _uiaLastUpdatedTick) < 10_000;
            }

            double trayLeftInDip;
            if (uiaFresh && hasWin32)
            {
                double effectivePx = (uiaFoundChevron && (uiaTrayLeftPx - win32TrayLeftPx) > 120)
                    ? uiaTrayLeftPx
                    : Math.Min(uiaTrayLeftPx, win32TrayLeftPx);

                trayLeftInDip = effectivePx / dpiX;
                _lastKnownGoodTrayLeftDip = trayLeftInDip;
            }
            else if (uiaFresh)
            {
                trayLeftInDip = uiaTrayLeftPx / dpiX;
                _lastKnownGoodTrayLeftDip = trayLeftInDip;
            }
            else if (hasWin32)
            {
                trayLeftInDip = win32TrayLeftPx / dpiX;
                _lastKnownGoodTrayLeftDip = trayLeftInDip;
            }
            else if (_lastKnownGoodTrayLeftDip > 0)
            {
                trayLeftInDip = _lastKnownGoodTrayLeftDip;
            }
            else
            {
                trayLeftInDip = taskbarRightDip - 230;
            }

            double windowWidth = window.ActualWidth > 0 ? window.ActualWidth : 180;
            double windowHeight = window.ActualHeight > 0 ? window.ActualHeight : 36;

            // X 轴吸附（补偿右侧阴影留白，使可见胶囊右缘与最左托盘/向上箭头间距严格等于 offsetLeft）
            double targetX = trayLeftInDip - windowWidth + visualRightMarginDip - offsetLeft;
            targetX = Math.Max(taskbarLeftDip, Math.Min(targetX, taskbarRightDip - windowWidth - 4));

            // Y 轴光学上下居中（扣除 Windows 11 任务栏顶部 1px 分隔线，与任务栏内腔视觉中心严格对齐）
            double topBorderDip = taskbarHeightDip >= 30 ? (1.0 / dpiY) : 0.0;
            double targetY = taskbarTopDip + topBorderDip + (taskbarHeightDip - topBorderDip - windowHeight) / 2.0;

            return (targetX, targetY, taskbarHeightDip, isAutoHidden);
        }
        catch
        {
            return null;
        }
    }
}
