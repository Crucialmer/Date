using System;
using System.Runtime.InteropServices;
using System.Windows;
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

    // 缓存最后一次有效托盘与任务栏坐标，彻底防止系统重绘时的瞬间塌陷与坐标突跳
    private static double _lastKnownGoodTrayLeftDip = -1;
    private static double _lastKnownGoodTaskbarLeftDip;
    private static double _lastKnownGoodTaskbarRightDip = -1;
    private static double _lastKnownGoodTaskbarTopDip = -1;
    private static double _lastKnownGoodTaskbarHeightDip = 48;

    /// <summary>
    /// 重置位置缓存（用于分辨率改变、DPI 变更或多显示器切换时重新测量）
    /// </summary>
    public static void ResetCache()
    {
        _lastKnownGoodTrayLeftDip = -1;
        _lastKnownGoodTaskbarLeftDip = 0;
        _lastKnownGoodTaskbarRightDip = -1;
        _lastKnownGoodTaskbarTopDip = -1;
        _lastKnownGoodTaskbarHeightDip = 48;
    }

    /// <summary>
    /// 在 OnSourceInitialized 阶段应用无激活工具窗口样式、挂载任务栏宿主以豁免“显示桌面”，并锁定最高层置顶
    /// </summary>
    public static void ApplyWindowStyles(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // 1. 设置免激活与无独立任务栏图标样式 (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        // 2. 将任务栏 Shell_TrayWnd 设为 Win32 宿主 (GWL_HWNDPARENT)
        // Windows Explorer 执行“显示桌面 (Win+D)”时会豁免宿主为 Shell_TrayWnd 的窗口，永不最小化
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero)
        {
            if (IntPtr.Size == 8)
                SetWindowLongPtr(hwnd, GWL_HWNDPARENT, taskbar);
            else
                SetWindowLong(hwnd, GWL_HWNDPARENT, taskbar.ToInt32());
        }

        // 3. 锁定置顶于最高层
        EnsureVisibleAndTopmost(window);
    }

    /// <summary>
    /// 零开销按需置顶检测：仅当窗口不可见、失去 TOPMOST 或被任务栏遮挡时才调用 Win32 重排，避免每秒无谓触发 DWM 重绘
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

    /// <summary>
    /// 沿 Z 序向上轻量遍历，检查任务栏 (Shell_TrayWnd) 是否位于本窗口之上
    /// </summary>
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
    /// 强制确保胶囊窗口可见并位于 TOPMOST 顶层（用于系统广播、解锁、唤醒、隐藏拦截等即时恢复）
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
    /// 仅探测当前任务栏高度（DIP），用于在计算最终窗口坐标前先确定标准/紧凑排版尺寸
    /// </summary>
    public static double GetTaskbarHeightDip(Window window)
    {
        try
        {
            var source = PresentationSource.FromVisual(window);
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero && GetWindowRect(taskbar, out RECT rect) && (rect.Right - rect.Left) > 100)
            {
                double heightDip = (rect.Bottom - rect.Top) / dpiY;
                _lastKnownGoodTaskbarHeightDip = heightDip;
                return heightDip;
            }
        }
        catch { }

        return _lastKnownGoodTaskbarHeightDip;
    }

    /// <summary>
    /// 精确计算悬浮胶囊在任务栏系统托盘左侧的位置与上下居中。
    /// visualRightMarginDip 用于抵消外层阴影留白，确保视觉胶囊边缘精确贴合 offsetLeft。
    /// </summary>
    public static (double X, double Y, double TaskbarHeight)? CalculatePosition(
        Window window,
        int offsetLeft,
        double visualRightMarginDip = 0)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return null;

            var source = PresentationSource.FromVisual(window);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            RECT taskbarRect = default;
            bool hasTaskbar = taskbar != IntPtr.Zero
                              && GetWindowRect(taskbar, out taskbarRect)
                              && (taskbarRect.Right - taskbarRect.Left) > 100;

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
                return (fallbackX, fallbackY, 48);
            }

            // 定位托盘左边缘
            IntPtr tray = taskbar != IntPtr.Zero
                ? FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null)
                : IntPtr.Zero;
            double trayLeftInDip;

            if (tray != IntPtr.Zero
                && GetWindowRect(tray, out RECT trayRect)
                && trayRect.Left > taskbarRect.Left
                && trayRect.Left < taskbarRect.Right
                && (trayRect.Right - trayRect.Left) > 10)
            {
                trayLeftInDip = trayRect.Left / dpiX;
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

            // X 轴吸附（补偿右侧阴影留白，使可见胶囊边缘与托盘间距严格等于 offsetLeft）
            double targetX = trayLeftInDip - windowWidth + visualRightMarginDip - offsetLeft;
            targetX = Math.Max(taskbarLeftDip, Math.Min(targetX, taskbarRightDip - windowWidth - 4));

            // Y 轴绝对数学上下居中（阴影留白上下对称，胶囊光学中心严格居中）
            double targetY = taskbarTopDip + (taskbarHeightDip - windowHeight) / 2.0;

            return (targetX, targetY, taskbarHeightDip);
        }
        catch
        {
            return null;
        }
    }
}
