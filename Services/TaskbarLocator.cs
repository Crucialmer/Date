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

    public readonly record struct PlacementResult(
        int WindowLeftPx,
        int WindowTopPx,
        double TaskbarHeightDip,
        bool IsTaskbarAutoHidden);

    private readonly record struct TrayAnchorPx(
        double LeftPx,
        double WidthPx,
        bool IsChevron,
        bool IsUiaLeaf);

    // 缓存最后一次有效任务栏物理几何，防止 Explorer 重绘瞬间塌陷
    private static bool _hasLastGoodTaskbar;
    private static RECT _lastGoodTaskbarRect;
    private static double _lastGoodTaskbarScale = 1.0;
    private static double _lastGoodTaskbarHeightDip = 48.0;
    private static TrayAnchorPx? _lastGoodAnchor;

    // Windows 11 XAML 托盘最左真实按钮（向上展开箭头 ^ 或最左可见图标）物理像素缓存
    private static readonly object UiaCacheLock = new();
    private static TrayAnchorPx? _uiaAnchor;
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
        _hasLastGoodTaskbar = false;
        _lastGoodTaskbarRect = default;
        _lastGoodTaskbarScale = 1.0;
        _lastGoodTaskbarHeightDip = 48.0;
        _lastGoodAnchor = null;

        lock (UiaCacheLock)
        {
            _uiaAnchor = null;
            _uiaLastUpdatedTick = 0;
        }
    }

    /// <summary>
    /// 在后台线程通过 UI Automation 批量缓存扫描 Windows 11 XAML 托盘最左侧真实叶子按钮（精确涵盖向上展开箭头 ^ 与全部可见托盘图标）。
    /// 彻底排除 SystemTray.Stack / Frame 等含透明留白的布局容器，确保跨设备坐标完全一致。
    /// </summary>
    public static void TriggerAsyncTrayBoundaryProbe(Action onBoundaryChanged)
    {
        if (Interlocked.CompareExchange(ref _uiaProbeInFlight, 1, 0) != 0)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                if (TryProbeXamlTrayAnchorPx(out TrayAnchorPx probed))
                {
                    bool changed;
                    lock (UiaCacheLock)
                    {
                        changed = !_uiaAnchor.HasValue
                                  || Math.Abs(_uiaAnchor.Value.LeftPx - probed.LeftPx) >= 0.8
                                  || Math.Abs(_uiaAnchor.Value.WidthPx - probed.WidthPx) >= 0.8
                                  || _uiaAnchor.Value.IsChevron != probed.IsChevron;
                        _uiaAnchor = probed;
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

    private static bool TryProbeXamlTrayAnchorPx(out TrayAnchorPx anchor)
    {
        anchor = default;

        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out RECT taskbarRect))
            return false;

        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        if (taskbarWidth <= 200) return false;

        double dpiScale = GetTaskbarDpiScale(taskbar);

        // 仅接受位于任务栏右侧合理区域的真实叶子托盘按钮（过滤整栏或托盘栈级宽容器）
        double minValidLeft = taskbarRect.Left + taskbarWidth * 0.35;
        double maxLeafWidthPx = Math.Min(taskbarWidth * 0.25, 180.0 * dpiScale);
        double minLeafSizePx = 8.0 * dpiScale;

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
        TrayAnchorPx best = default;

        for (int i = 0; i < elements.Count; i++)
        {
            try
            {
                var cached = elements[i].Cached;
                if (cached.IsOffscreen) continue;

                Rect rect = cached.BoundingRectangle;
                if (rect.IsEmpty || rect.Width < minLeafSizePx || rect.Height < minLeafSizePx || rect.Width > maxLeafWidthPx)
                    continue;

                if (rect.Bottom < taskbarRect.Top - 6 || rect.Top > taskbarRect.Bottom + 6)
                    continue;

                if (rect.Left < minValidLeft || rect.Right > taskbarRect.Right + 8)
                    continue;

                string cls = cached.ClassName ?? string.Empty;
                string aid = cached.AutomationId ?? string.Empty;
                string name = cached.Name ?? string.Empty;

                if (!IsSystemTrayLeafItem(cls, aid, name, out bool isChevron))
                    continue;

                // 若两个元素左边界几乎重合（如外层 Button 与内层 ChevronIconView），优先保留标记为 Chevron 或尺寸更标准的控件
                if (!foundAny
                    || rect.Left < best.LeftPx - 0.5
                    || (Math.Abs(rect.Left - best.LeftPx) <= 0.5 && isChevron && !best.IsChevron))
                {
                    best = new TrayAnchorPx(rect.Left, rect.Width, isChevron, IsUiaLeaf: true);
                    foundAny = true;
                }
            }
            catch
            {
                // 跳过遍历期间瞬间消失的托盘图标
            }
        }

        if (foundAny && best.LeftPx < taskbarRect.Right)
        {
            anchor = best;
            return true;
        }

        return false;
    }

    private static bool IsSystemTrayLeafItem(string cls, string aid, string name, out bool isChevron)
    {
        isChevron = false;

        // 1. 排除任何包含隐形布局边距的容器类（Stack / Frame / Container / Panel / Grid）
        if (cls.Contains("Stack", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("Frame", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("Container", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("Panel", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("Grid", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 2. Windows 11 向上展开箭头（Chevron）精准识别
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

        // 3. Windows 11 XAML 真实托盘叶子按钮/图标
        if (cls.StartsWith("SystemTray.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 4. 通用托盘叶子 AutomationId（SystemTrayIcon / NotifyItemIcon / InputIndicator）
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
    /// 兜底方案（仅在 UIA 不可用时使用）：结合 Win32 TrayNotifyWnd 及可见子 Button 获取最左物理像素
    /// </summary>
    private static bool TryGetWin32TrayAnchorPx(IntPtr taskbar, RECT taskbarRect, out TrayAnchorPx anchor)
    {
        anchor = default;
        if (taskbar == IntPtr.Zero) return false;

        IntPtr tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray == IntPtr.Zero || !GetWindowRect(tray, out RECT trayRect))
            return false;

        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        double minValidLeft = taskbarRect.Left + taskbarWidth * 0.35;

        // 优先查找经典独立 Chevron Button 子窗口，避免 TrayNotifyWnd 左侧隐形边距干扰
        IntPtr chevronBtn = FindWindowEx(tray, IntPtr.Zero, "Button", null);
        if (chevronBtn != IntPtr.Zero && IsWindowVisible(chevronBtn) && GetWindowRect(chevronBtn, out RECT btnRect))
        {
            int bw = btnRect.Right - btnRect.Left;
            int bh = btnRect.Bottom - btnRect.Top;
            if (bw >= 8 && bh >= 8 && btnRect.Left >= minValidLeft && btnRect.Right <= taskbarRect.Right + 4)
            {
                anchor = new TrayAnchorPx(btnRect.Left, bw, IsChevron: true, IsUiaLeaf: false);
                return true;
            }
        }

        bool found = false;
        double bestLeft = double.MaxValue;
        double bestWidth = 0;

        if (trayRect.Left >= minValidLeft && trayRect.Left < taskbarRect.Right && (trayRect.Right - trayRect.Left) > 10)
        {
            bestLeft = trayRect.Left;
            bestWidth = trayRect.Right - trayRect.Left;
            found = true;
        }

        EnumChildWindows(tray, (child, _) =>
        {
            if (IsWindowVisible(child) && GetWindowRect(child, out RECT cr))
            {
                int cw = cr.Right - cr.Left;
                int ch = cr.Bottom - cr.Top;
                if (cw >= 8 && ch >= 8 && cr.Left >= minValidLeft && cr.Left < bestLeft && cr.Right <= taskbarRect.Right + 4)
                {
                    bestLeft = cr.Left;
                    bestWidth = cw;
                    found = true;
                }
            }
            return true;
        }, IntPtr.Zero);

        if (found)
        {
            anchor = new TrayAnchorPx(bestLeft, bestWidth, IsChevron: false, IsUiaLeaf: false);
            return true;
        }

        return false;
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

    private static double GetTaskbarDpiScale(IntPtr taskbar)
    {
        try
        {
            if (taskbar != IntPtr.Zero)
            {
                uint dpi = GetDpiForWindow(taskbar);
                if (dpi >= 96 && dpi <= 960)
                    return dpi / 96.0;
            }
        }
        catch { }

        return _lastGoodTaskbarScale > 0 ? _lastGoodTaskbarScale : 1.0;
    }

    private static (double DpiX, double DpiY) GetWindowDpiScale(Window window, double fallbackScale)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget != null)
        {
            return (source.CompositionTarget.TransformToDevice.M11,
                    source.CompositionTarget.TransformToDevice.M22);
        }
        return (fallbackScale, fallbackScale);
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
                double scale = GetTaskbarDpiScale(taskbar);
                double heightDip = (rect.Bottom - rect.Top) / scale;
                _lastGoodTaskbarHeightDip = heightDip;
                return heightDip;
            }
        }
        catch { }

        return _lastGoodTaskbarHeightDip;
    }

    /// <summary>
    /// 全物理像素坐标空间精确计算并定位悬浮胶囊：
    /// 1) 优先直接锚定 UIA 真实 XAML 托盘叶子按钮（^ 展开箭头或最左可见图标），不受 Win32 TrayNotifyWnd 跨设备隐形留白干扰；
    /// 2) 对不同 Windows 11 版本/触控模式的 ^ 按钮宽度（24/32/40 DIP）按居中字形做光学归一化，实现多设备视觉间距完全一致；
    /// 3) 直接使用视觉胶囊本体在窗口内的精确物理偏移 + Win32 SetWindowPos 定位，消除跨 DPI/分数缩放往返换算误差。
    /// </summary>
    public static PlacementResult? CalculateAndApplyPosition(
        Window window,
        FrameworkElement visualPill,
        int offsetLeftDip,
        bool allowMove)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return null;

            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            RECT taskbarRect = default;
            bool hasTaskbar = taskbar != IntPtr.Zero
                              && GetWindowRect(taskbar, out taskbarRect)
                              && (taskbarRect.Right - taskbarRect.Left) > 100;

            double taskbarScale;
            if (hasTaskbar)
            {
                taskbarScale = GetTaskbarDpiScale(taskbar);
                _hasLastGoodTaskbar = true;
                _lastGoodTaskbarRect = taskbarRect;
                _lastGoodTaskbarScale = taskbarScale;
                _lastGoodTaskbarHeightDip = (taskbarRect.Bottom - taskbarRect.Top) / taskbarScale;
            }
            else if (_hasLastGoodTaskbar)
            {
                taskbarRect = _lastGoodTaskbarRect;
                taskbarScale = _lastGoodTaskbarScale;
            }
            else
            {
                return null;
            }

            bool isAutoHidden = hasTaskbar && IsTaskbarCurrentlyAutoHidden(taskbar, taskbarRect);
            double taskbarHeightDip = (taskbarRect.Bottom - taskbarRect.Top) / taskbarScale;
            bool isCompact = taskbarHeightDip < 40.0;

            // 1. 选取最高精度托盘锚点：
            //    优先使用 UIA 实测的 XAML 真实叶子按钮（^ 展开箭头 / 最左可见图标），彻底屏蔽 TrayNotifyWnd 跨设备不一致的隐形左侧留白；
            //    仅当 UIA 尚未就绪或不可用（如 Win10 / Explorer 重启中）时回退至 Win32 锚点。
            TrayAnchorPx? anchor = null;
            lock (UiaCacheLock)
            {
                if (_uiaAnchor.HasValue && (Environment.TickCount64 - _uiaLastUpdatedTick) < 10_000)
                {
                    anchor = _uiaAnchor.Value;
                }
            }

            if (!anchor.HasValue && hasTaskbar && TryGetWin32TrayAnchorPx(taskbar, taskbarRect, out TrayAnchorPx win32Anchor))
            {
                anchor = win32Anchor;
            }

            if (anchor.HasValue)
            {
                _lastGoodAnchor = anchor.Value;
            }
            else if (_lastGoodAnchor.HasValue)
            {
                anchor = _lastGoodAnchor.Value;
            }
            else
            {
                anchor = new TrayAnchorPx(taskbarRect.Right - 230.0 * taskbarScale, 32.0 * taskbarScale, IsChevron: false, IsUiaLeaf: false);
            }

            // 2. 光学字形距离归一化（跨设备/跨缩放/跨按钮宽度的核心一致化公式）：
            //    标准 48 DIP 任务栏中，32 DIP 宽的 ^ 按钮内部左侧自带约 10 DIP 透明留白（字形宽 ~12 DIP 居中）；
            //    紧凑小任务栏按 0.85 等比收缩间距保持比例统一。
            double effectiveOffsetDip = isCompact ? offsetLeftDip * 0.85 : offsetLeftDip;
            double targetVisualPillRightPx = ComputeNormalizedPillRightPx(anchor.Value, effectiveOffsetDip, taskbarScale);

            // 3. 精确测量可见胶囊本体 (PillContainer) 在 MainWindow 内的物理像素坐标
            var (winDpiX, winDpiY) = GetWindowDpiScale(window, taskbarScale);
            double pillWidthDip = visualPill.ActualWidth > 0 ? visualPill.ActualWidth : 230;
            double pillHeightDip = visualPill.ActualHeight > 0 ? visualPill.ActualHeight : 32;

            System.Windows.Point pillTopLeftDip = visualPill.TranslatePoint(new System.Windows.Point(0, 0), window);
            double pillRightOffsetPx = (pillTopLeftDip.X + pillWidthDip) * winDpiX;
            double pillCenterYOffsetPx = (pillTopLeftDip.Y + pillHeightDip / 2.0) * winDpiY;

            // 4. 在统一物理屏幕像素空间计算窗口精确左上角
            int targetWinLeftPx = (int)Math.Round(targetVisualPillRightPx - pillRightOffsetPx);

            int winWidthPx = GetWindowRect(hwnd, out RECT currentRect) && (currentRect.Right - currentRect.Left) > 0
                ? (currentRect.Right - currentRect.Left)
                : (int)Math.Round((window.ActualWidth > 0 ? window.ActualWidth : 250) * winDpiX);

            int minWinLeftPx = taskbarRect.Left;
            int maxWinLeftPx = taskbarRect.Right - winWidthPx - (int)Math.Round(4.0 * taskbarScale);
            targetWinLeftPx = Math.Clamp(targetWinLeftPx, minWinLeftPx, Math.Max(minWinLeftPx, maxWinLeftPx));

            // 垂直光学居中：扣除 Windows 11 任务栏顶部 1 个物理像素分隔线，让胶囊本体光学中心与任务栏内腔中线严格重合
            int taskbarInnerTopPx = taskbarRect.Top + (taskbarHeightDip >= 30.0 ? 1 : 0);
            double taskbarCenterYPx = (taskbarInnerTopPx + taskbarRect.Bottom) / 2.0;
            int targetWinTopPx = (int)Math.Round(taskbarCenterYPx - pillCenterYOffsetPx);

            // 5. 直接通过 Win32 SetWindowPos 原子移动（零 WPF Left/Top 跨 DPI 往返换算误差）
            if (allowMove)
            {
                bool needsMove = !GetWindowRect(hwnd, out RECT cur)
                                 || Math.Abs(cur.Left - targetWinLeftPx) >= 1
                                 || Math.Abs(cur.Top - targetWinTopPx) >= 1;
                if (needsMove)
                {
                    SetWindowPos(hwnd, HWND_TOPMOST, targetWinLeftPx, targetWinTopPx, 0, 0,
                        SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }

            return new PlacementResult(targetWinLeftPx, targetWinTopPx, taskbarHeightDip, isAutoHidden);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 将不同 Windows 11 版本 / 触控密度 / DPI 下宽度各异（24~40 DIP）的 ^ 展开按钮或托盘图标，
    /// 归一化为统一的“胶囊右缘到可见符号（^）的光学间距”，同时严格保护按钮自身的悬停热区不重叠。
    /// </summary>
    private static double ComputeNormalizedPillRightPx(TrayAnchorPx anchor, double offsetDip, double scale)
    {
        double widthDip = anchor.WidthPx / scale;

        if (anchor.IsChevron && widthDip is >= 16.0 and <= 56.0)
        {
            // ^ 符号自身宽约 12 DIP 且居中于按钮内；标准 32 DIP 按钮左侧内边距 = (32 - 12)/2 = 10 DIP
            double glyphLeftPx = anchor.LeftPx + (anchor.WidthPx / 2.0) - (6.0 * scale);
            double desiredGlyphGapPx = (offsetDip + 10.0) * scale;
            double byGlyphPx = glyphLeftPx - desiredGlyphGapPx;

            // 热区安全保护：即便遇到 40+ DIP 超宽触控按钮，胶囊右缘也至少远离按钮热区边界 minHitboxGapDip
            double minHitboxGapPx = Math.Max(6.0, offsetDip - 4.0) * scale;
            double maxByHitboxPx = anchor.LeftPx - minHitboxGapPx;

            return Math.Min(byGlyphPx, maxByHitboxPx);
        }

        if (anchor.IsUiaLeaf && widthDip is >= 16.0 and <= 56.0)
        {
            // 无展开箭头时，最左为可见托盘图标（符号宽约 16 DIP 居中，标准 32 DIP 左侧内边距 = 8 DIP）
            double glyphLeftPx = anchor.LeftPx + (anchor.WidthPx / 2.0) - (8.0 * scale);
            double desiredGlyphGapPx = (offsetDip + 8.0) * scale;
            double byGlyphPx = glyphLeftPx - desiredGlyphGapPx;

            double minHitboxGapPx = Math.Max(6.0, offsetDip - 4.0) * scale;
            double maxByHitboxPx = anchor.LeftPx - minHitboxGapPx;

            return Math.Min(byGlyphPx, maxByHitboxPx);
        }

        return anchor.LeftPx - (offsetDip * scale);
    }
}
