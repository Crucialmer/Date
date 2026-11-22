using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Date.Services;

public class TaskbarLocator
{
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int GWL_EXSTYLE = -20;
    public const int GWL_HWNDPARENT = -8;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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

    /// <summary>
    /// 在 OnSourceInitialized 阶段将窗口所有权挂载至任务栏（Shell_TrayWnd）并应用无激活样式
    /// </summary>
    public static void ApplyWindowStyles(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // 1. 设置免激活与无独立任务栏图标样式
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        // 2. 将任务栏 Shell_TrayWnd 设为宿主窗口 (Owner)
        // 彻底根除“闪退”隐患：当按 Win+D (显示桌面) 时，宿主为任务栏的窗口绝不会被系统最小化隐藏！
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero)
        {
            try
            {
                new WindowInteropHelper(window).Owner = taskbar;
            }
            catch
            {
                if (IntPtr.Size == 8)
                    SetWindowLongPtr(hwnd, GWL_HWNDPARENT, taskbar);
                else
                    SetWindowLong(hwnd, GWL_HWNDPARENT, taskbar.ToInt32());
            }
        }

        // 3. 锁定置顶于任务栏之上
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>
    /// 精确计算悬浮胶囊在任务栏系统托盘左侧的位置与上下居中
    /// </summary>
    public static (double X, double Y, double TaskbarHeight)? CalculatePosition(Window window, int offsetLeft)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return null;

            var source = PresentationSource.FromVisual(window);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out RECT taskbarRect))
            {
                double fallbackX = SystemParameters.WorkArea.Right - window.ActualWidth - 220 - offsetLeft;
                double fallbackY = SystemParameters.WorkArea.Bottom + (SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Bottom - window.ActualHeight) / 2.0;
                return (fallbackX, fallbackY, 48);
            }

            double taskbarLeftDip = taskbarRect.Left / dpiX;
            double taskbarRightDip = taskbarRect.Right / dpiX;
            double taskbarTopDip = taskbarRect.Top / dpiY;
            double taskbarHeightDip = (taskbarRect.Bottom - taskbarRect.Top) / dpiY;

            // 定位托盘左边缘
            IntPtr tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            double trayLeftInDip;

            if (tray != IntPtr.Zero && GetWindowRect(tray, out RECT trayRect) &&
                trayRect.Left > taskbarRect.Left && trayRect.Left < taskbarRect.Right && (trayRect.Right - trayRect.Left) > 10)
            {
                trayLeftInDip = trayRect.Left / dpiX;
            }
            else
            {
                trayLeftInDip = taskbarRightDip - 230;
            }

            double windowWidth = window.ActualWidth > 0 ? window.ActualWidth : 160;
            double windowHeight = window.ActualHeight > 0 ? window.ActualHeight : 30;

            // X 轴吸附
            double targetX = trayLeftInDip - windowWidth - offsetLeft;
            targetX = Math.Max(taskbarLeftDip, Math.Min(targetX, taskbarRightDip - windowWidth - 10));

            // Y 轴绝对数学上下居中（无论小任务栏还是标准任务栏，彻底消除单侧偏置）
            double targetY = taskbarTopDip + (taskbarHeightDip - windowHeight) / 2.0;

            return (targetX, targetY, taskbarHeightDip);
        }
        catch
        {
            return null;
        }
    }
}
