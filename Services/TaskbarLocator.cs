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
    /// 在 OnSourceInitialized 阶段应用窗口样式（避免窗口先显示后再改样式导致闪退）
    /// </summary>
    public static void ApplyWindowStyles(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        // 锁定置顶于任务栏之上
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>
    /// 精确计算悬浮胶囊在任务栏系统托盘左侧的位置（自适应至最高 8K 分辨率及多倍 DPI 缩放）
    /// </summary>
    public static (double X, double Y)? CalculatePosition(Window window, int offsetLeft)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return null;

            var source = PresentationSource.FromVisual(window);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            double windowWidth = window.ActualWidth > 0 ? window.ActualWidth : (window.Width > 0 ? window.Width : 160);
            double windowHeight = window.ActualHeight > 0 ? window.ActualHeight : (window.Height > 0 ? window.Height : 32);

            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out RECT taskbarRect))
            {
                // 无法获取任务栏时回退到主屏幕工作区右下角
                double fallbackX = SystemParameters.WorkArea.Right - windowWidth - 220 - offsetLeft;
                double fallbackY = SystemParameters.WorkArea.Bottom + (SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Bottom - windowHeight) / 2;
                return (fallbackX, fallbackY);
            }

            double taskbarLeftDip = taskbarRect.Left / dpiX;
            double taskbarRightDip = taskbarRect.Right / dpiX;
            double taskbarTopDip = taskbarRect.Top / dpiY;
            double taskbarHeightDip = (taskbarRect.Bottom - taskbarRect.Top) / dpiY;

            // 尝试定位 TrayNotifyWnd
            IntPtr tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            double trayLeftInDip;

            if (tray != IntPtr.Zero && GetWindowRect(tray, out RECT trayRect) &&
                trayRect.Left > taskbarRect.Left && trayRect.Left < taskbarRect.Right && (trayRect.Right - trayRect.Left) > 10)
            {
                trayLeftInDip = trayRect.Left / dpiX;
            }
            else
            {
                // Win11 XAML 任务栏预估托盘宽度 (预留约 210~260 DIP)
                trayLeftInDip = taskbarRightDip - 230;
            }

            // 计算目标 X 坐标并进行安全边界保护（防止飘出屏幕）
            double targetX = trayLeftInDip - windowWidth - offsetLeft;
            targetX = Math.Max(taskbarLeftDip, Math.Min(targetX, taskbarRightDip - windowWidth - 10));

            // 垂直居中于任务栏
            double targetY = taskbarTopDip + Math.Max(2, (taskbarHeightDip - windowHeight) / 2);

            return (targetX, targetY);
        }
        catch
        {
            return null;
        }
    }
}
