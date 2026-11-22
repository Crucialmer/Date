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

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static void ApplyWindowStyles(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    public static (double X, double Y)? CalculatePosition(Window window, int offsetLeft)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var source = PresentationSource.FromVisual(window);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            // 如果无法获取任务栏句柄（例如备用环境），回退到工作区右下角
            double fallbackX = SystemParameters.WorkArea.Right - window.Width - 180 - offsetLeft;
            double fallbackY = SystemParameters.WorkArea.Bottom + (SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Bottom - window.Height) / 2;
            return (fallbackX, fallbackY);
        }

        if (!GetWindowRect(taskbar, out RECT taskbarRect))
        {
            return null;
        }

        // 尝试定位 TrayNotifyWnd
        IntPtr tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        double trayLeftInDip;

        if (tray != IntPtr.Zero && GetWindowRect(tray, out RECT trayRect))
        {
            trayLeftInDip = trayRect.Left / dpiX;
        }
        else
        {
            // Windows 11 XAML 任务栏有时 TrayNotifyWnd 嵌套或延迟，估算右侧预留托盘宽度 (~210 DIP)
            double taskbarRightDip = taskbarRect.Right / dpiX;
            trayLeftInDip = taskbarRightDip - 210;
        }

        double taskbarTopDip = taskbarRect.Top / dpiY;
        double taskbarHeightDip = (taskbarRect.Bottom - taskbarRect.Top) / dpiY;

        double targetX = trayLeftInDip - window.ActualWidth - offsetLeft;
        double targetY = taskbarTopDip + (taskbarHeightDip - window.ActualHeight) / 2;

        return (targetX, targetY);
    }
}
