using System;
using System.Runtime.InteropServices;

namespace Date.Services;

/// <summary>
/// 基于 Win32 SetWinEventHook 的轻量级任务栏托盘变动监听器。
/// 仅绑定到 Shell_TrayWnd 所属进程与线程，在托盘图标增减、折叠箭头移动或重排时即时触发回调，零轮询开销。
/// </summary>
public sealed class TaskbarTrayMonitor : IDisposable
{
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_HIDE = 0x8003;
    private const uint EVENT_OBJECT_REORDER = 0x8004;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;

    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private readonly Action _onTrayChanged;
    private readonly WinEventDelegate _eventCallback; // 持有强引用防止被 GC 回收
    private IntPtr _hookStructure;
    private IntPtr _hookLocation;
    private bool _disposed;

    public TaskbarTrayMonitor(Action onTrayChanged)
    {
        _onTrayChanged = onTrayChanged;
        _eventCallback = OnWinEvent;
    }

    /// <summary>
    /// 挂载（或重新挂载）至当前任务栏 Shell_TrayWnd 所属线程
    /// </summary>
    public void Attach()
    {
        if (_disposed) return;

        Detach();

        IntPtr taskbar = TaskbarLocator.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return;

        uint threadId = GetWindowThreadProcessId(taskbar, out uint processId);
        if (processId == 0) return;

        // 监听任务栏子元素创建/销毁/显隐/重排（捕获托盘图标新增、退出、折叠箭头出现/隐藏）
        _hookStructure = SetWinEventHook(
            EVENT_OBJECT_CREATE,
            EVENT_OBJECT_REORDER,
            IntPtr.Zero,
            _eventCallback,
            processId,
            threadId,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        // 监听任务栏元素坐标变化（捕获托盘展开/收起及 XAML 动画位移）
        _hookLocation = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE,
            EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero,
            _eventCallback,
            processId,
            threadId,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    public void Detach()
    {
        if (_hookStructure != IntPtr.Zero)
        {
            UnhookWinEvent(_hookStructure);
            _hookStructure = IntPtr.Zero;
        }

        if (_hookLocation != IntPtr.Zero)
        {
            UnhookWinEvent(_hookLocation);
            _hookLocation = IntPtr.Zero;
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (_disposed || hwnd == IntPtr.Zero) return;

        // 对 LOCATIONCHANGE 放行窗口与 XAML 控件对象，过滤无关光标/滚动等噪声
        if (eventType == EVENT_OBJECT_LOCATIONCHANGE && idObject < OBJID_WINDOW)
        {
            return;
        }

        _onTrayChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }
}
