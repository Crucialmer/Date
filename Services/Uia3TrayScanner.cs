using System;
using System.Runtime.InteropServices;

namespace Date.Services;

/// <summary>
/// 原生 COM UI Automation 3.0 (CUIAutomation8 / UIAutomationCore.dll) 轻量扫描器。
/// 相比托管的 System.Windows.Automation (UIA2)，UIA3 可直接穿透 Windows 11 任务栏 XAML Islands
/// (DesktopWindowContentBridge / InputSite)，精准读取触屏模式与普通模式下真实可见的向上展开箭头 (^)
/// 及托盘叶子图标坐标，绝不被触屏模式下膨胀的 TrayNotifyWnd 隐形留白误导。
/// </summary>
internal static class Uia3TrayScanner
{
    private const int TreeScope_Element = 0x1;
    private const int TreeScope_Descendants = 0x4;
    private const int AutomationElementMode_None = 0;

    private const int UIA_BoundingRectanglePropertyId = 30001;
    private const int UIA_ControlTypePropertyId = 30003;
    private const int UIA_NamePropertyId = 30005;
    private const int UIA_AutomationIdPropertyId = 30011;
    private const int UIA_ClassNamePropertyId = 30012;
    private const int UIA_IsOffscreenPropertyId = 30022;

    private const int UIA_ButtonControlTypeId = 50000;

    private static readonly Guid ClsidCUIAutomation8 = new("e22ad333-b25f-460c-83d0-0581107395c9");
    private static readonly Guid ClsidCUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    internal readonly record struct ScannedTrayItem(
        double LeftPx,
        double WidthPx,
        bool IsChevron);

    /// <summary>
    /// 扫描指定 Shell_TrayWnd（及其嵌套的 XAML Island）内真实可见的系统托盘叶子锚点。
    /// 优先直接锁定向上展开箭头 (^)；若所有图标均已展开而无 ^ 按钮，则锁定最左侧真实可见托盘图标。
    /// </summary>
    public static bool TryFindLeftmostTrayAnchor(
        IntPtr taskbarHwnd,
        TaskbarLocator.RECT taskbarRect,
        double dpiScale,
        out ScannedTrayItem result)
    {
        result = default;
        if (taskbarHwnd == IntPtr.Zero) return false;

        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        if (taskbarWidth <= 200) return false;

        IUIAutomation? uia = null;
        IUIAutomationCondition? trueCond = null;
        IUIAutomationCondition? controlViewCond = null;
        IUIAutomationCacheRequest? cacheReq = null;

        try
        {
            uia = CreateUia3Instance();
            if (uia == null) return false;

            if (uia.CreateTrueCondition(out trueCond) != 0 || trueCond == null)
                return false;

            if (uia.CreateCacheRequest(out cacheReq) != 0 || cacheReq == null)
                return false;

            cacheReq.AddProperty(UIA_BoundingRectanglePropertyId);
            cacheReq.AddProperty(UIA_ControlTypePropertyId);
            cacheReq.AddProperty(UIA_NamePropertyId);
            cacheReq.AddProperty(UIA_AutomationIdPropertyId);
            cacheReq.AddProperty(UIA_ClassNamePropertyId);
            cacheReq.AddProperty(UIA_IsOffscreenPropertyId);
            cacheReq.put_TreeScope(TreeScope_Element);
            cacheReq.put_AutomationElementMode(AutomationElementMode_None);

            if (uia.get_ControlViewCondition(out controlViewCond) == 0 && controlViewCond != null)
            {
                cacheReq.put_TreeFilter(controlViewCond);
            }

            bool hasChevron = false;
            ScannedTrayItem bestChevron = default;
            bool hasNotifyIcon = false;
            ScannedTrayItem bestNotifyIcon = default;
            bool hasOtherTray = false;
            ScannedTrayItem bestOtherTray = default;

            // 先扫描 Shell_TrayWnd 全树；若未找到 ^ 按钮（部分 Win11 24H2+ 需直连 XAML Island 宿主），补充扫描子桥接窗口
            bool foundPrimary = ScanHwndSubtree(uia, taskbarHwnd, taskbarRect, dpiScale, trueCond, cacheReq,
                ref hasChevron, ref bestChevron, ref hasNotifyIcon, ref bestNotifyIcon, ref hasOtherTray, ref bestOtherTray);

            if (!hasChevron)
            {
                IntPtr bridge = TaskbarLocator.FindWindowEx(taskbarHwnd, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
                while (bridge != IntPtr.Zero)
                {
                    ScanHwndSubtree(uia, bridge, taskbarRect, dpiScale, trueCond, cacheReq,
                        ref hasChevron, ref bestChevron, ref hasNotifyIcon, ref bestNotifyIcon, ref hasOtherTray, ref bestOtherTray);
                    bridge = TaskbarLocator.FindWindowEx(taskbarHwnd, bridge, "Windows.UI.Composition.DesktopWindowContentBridge", null);
                }
            }

            if (!foundPrimary && !hasChevron && !hasNotifyIcon && !hasOtherTray)
                return false;

            // 锚点优先级：
            // 1. 若存在可见的 ^ 向上展开箭头，在 Windows 11 中它永远位于托盘最左侧，直接锁定 ^（彻底免疫左侧任何触屏/隐形留白元素）
            //    仅当存在明确位于 ^ 左侧的真实 NotifyItemIcon（如第三方 Shell 修改）时才兼容左侧图标。
            if (hasChevron)
            {
                if (hasNotifyIcon && bestNotifyIcon.LeftPx < bestChevron.LeftPx - 4.0 * dpiScale && bestNotifyIcon.WidthPx <= 52.0 * dpiScale)
                {
                    result = bestNotifyIcon;
                }
                else
                {
                    result = bestChevron;
                }
                return true;
            }

            // 2. 若所有托盘图标均外显而无 ^ 按钮，取最左侧真实可见托盘图标/系统托盘按钮
            if (hasNotifyIcon && hasOtherTray)
            {
                result = bestNotifyIcon.LeftPx <= bestOtherTray.LeftPx ? bestNotifyIcon : bestOtherTray;
                return true;
            }
            if (hasNotifyIcon)
            {
                result = bestNotifyIcon;
                return true;
            }
            if (hasOtherTray)
            {
                result = bestOtherTray;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseCom(cacheReq);
            ReleaseCom(controlViewCond);
            ReleaseCom(trueCond);
            ReleaseCom(uia);
        }
    }

    private static bool ScanHwndSubtree(
        IUIAutomation uia,
        IntPtr targetHwnd,
        TaskbarLocator.RECT taskbarRect,
        double dpiScale,
        IUIAutomationCondition trueCond,
        IUIAutomationCacheRequest cacheReq,
        ref bool hasChevron,
        ref ScannedTrayItem bestChevron,
        ref bool hasNotifyIcon,
        ref ScannedTrayItem bestNotifyIcon,
        ref bool hasOtherTray,
        ref ScannedTrayItem bestOtherTray)
    {
        IUIAutomationElement? rootEl = null;
        IUIAutomationElementArray? elements = null;

        try
        {
            if (uia.ElementFromHandle(targetHwnd, out rootEl) != 0 || rootEl == null)
                return false;

            if (rootEl.FindAllBuildCache(TreeScope_Descendants, trueCond, cacheReq, out elements) != 0 || elements == null)
                return false;

            if (elements.get_Length(out int count) != 0 || count <= 0)
                return false;

            int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
            double minValidLeft = taskbarRect.Left + taskbarWidth * 0.35;
            double maxLeafWidthPx = Math.Min(taskbarWidth * 0.25, 180.0 * dpiScale);
            double minLeafSizePx = 8.0 * dpiScale;

            for (int i = 0; i < count; i++)
            {
                IUIAutomationElement? el = null;
                try
                {
                    if (elements.GetElement(i, out el) != 0 || el == null)
                        continue;

                    if (TryReadBool(el, UIA_IsOffscreenPropertyId, out bool isOffscreen) && isOffscreen)
                        continue;

                    if (!TryReadRect(el, out double left, out double top, out double width, out double height))
                        continue;

                    if (width < minLeafSizePx || height < minLeafSizePx || width > maxLeafWidthPx)
                        continue;

                    double right = left + width;
                    double bottom = top + height;
                    if (bottom < taskbarRect.Top - 6 || top > taskbarRect.Bottom + 6)
                        continue;

                    if (left < minValidLeft || right > taskbarRect.Right + 8)
                        continue;

                    string cls = ReadString(el, UIA_ClassNamePropertyId);
                    string aid = ReadString(el, UIA_AutomationIdPropertyId);
                    string name = ReadString(el, UIA_NamePropertyId);
                    int controlType = ReadInt(el, UIA_ControlTypePropertyId);

                    var kind = ClassifyTrayElement(cls, aid, name, controlType, width / dpiScale);
                    if (kind == TrayElementKind.None)
                        continue;

                    var candidate = new ScannedTrayItem(left, width, IsChevron: kind == TrayElementKind.Chevron);

                    switch (kind)
                    {
                        case TrayElementKind.Chevron:
                            if (!hasChevron
                                || candidate.LeftPx < bestChevron.LeftPx - 1.0
                                || (Math.Abs(candidate.LeftPx - bestChevron.LeftPx) <= 4.0 * dpiScale && candidate.WidthPx < bestChevron.WidthPx))
                            {
                                bestChevron = candidate;
                                hasChevron = true;
                            }
                            break;

                        case TrayElementKind.NotifyIcon:
                            if (!hasNotifyIcon || candidate.LeftPx < bestNotifyIcon.LeftPx)
                            {
                                bestNotifyIcon = candidate;
                                hasNotifyIcon = true;
                            }
                            break;

                        case TrayElementKind.OtherSystemTray:
                            if (!hasOtherTray || candidate.LeftPx < bestOtherTray.LeftPx)
                            {
                                bestOtherTray = candidate;
                                hasOtherTray = true;
                            }
                            break;
                    }
                }
                catch { }
                finally
                {
                    ReleaseCom(el);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseCom(elements);
            ReleaseCom(rootEl);
        }
    }

    private enum TrayElementKind
    {
        None,
        Chevron,
        NotifyIcon,
        OtherSystemTray
    }

    private static TrayElementKind ClassifyTrayElement(string cls, string aid, string name, int controlType, double widthDip)
    {
        // 严格剔除一切宽容器与布局栈，杜绝触屏模式下的大容器透明边距干扰
        if (cls.Contains("Stack", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("Frame", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("Container", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("Panel", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("Grid", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("Bridge", StringComparison.OrdinalIgnoreCase))
        {
            return TrayElementKind.None;
        }

        // 1. 精准匹配 Windows 11 / 10 向上展开箭头 (Chevron / 显示隐藏的图标)
        if (cls.Equals("SystemTray.ChevronIconView", StringComparison.OrdinalIgnoreCase)
            || aid.Contains("Chevron", StringComparison.OrdinalIgnoreCase)
            || name.Contains("隐藏的图标", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Show hidden icons", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Notification Chevron", StringComparison.OrdinalIgnoreCase)
            || name.Contains("通知 V 形", StringComparison.OrdinalIgnoreCase)
            || name.Contains("溢出", StringComparison.OrdinalIgnoreCase))
        {
            return TrayElementKind.Chevron;
        }

        // 2. 精准匹配用户托盘应用图标
        if (cls.Equals("SystemTray.NotifyIconView", StringComparison.OrdinalIgnoreCase)
            || aid.Equals("NotifyItemIcon", StringComparison.OrdinalIgnoreCase))
        {
            return TrayElementKind.NotifyIcon;
        }

        // 3. 精准匹配系统托盘核心按钮（触屏键盘、输入法、快速设置、时钟等），仅接受按钮级叶子元素
        if (aid.Equals("SystemTrayIcon", StringComparison.OrdinalIgnoreCase)
            || aid.Contains("InputIndicator", StringComparison.OrdinalIgnoreCase)
            || aid.Contains("TouchKeyboard", StringComparison.OrdinalIgnoreCase)
            || aid.Contains("PenWorkspace", StringComparison.OrdinalIgnoreCase)
            || aid.Contains("Clock", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("SystemTray.NormalButton", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("SystemTray.OmniButton", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("SystemTray.IconView", StringComparison.OrdinalIgnoreCase))
        {
            if (controlType == UIA_ButtonControlTypeId || widthDip <= 140.0)
                return TrayElementKind.OtherSystemTray;
        }

        return TrayElementKind.None;
    }

    private static IUIAutomation? CreateUia3Instance()
    {
        try
        {
            Type? type8 = Type.GetTypeFromCLSID(ClsidCUIAutomation8);
            if (type8 != null && Activator.CreateInstance(type8) is IUIAutomation uia8)
                return uia8;
        }
        catch { }

        try
        {
            Type? type = Type.GetTypeFromCLSID(ClsidCUIAutomation);
            if (type != null && Activator.CreateInstance(type) is IUIAutomation uia)
                return uia;
        }
        catch { }

        return null;
    }

    private static bool TryReadRect(IUIAutomationElement el, out double left, out double top, out double width, out double height)
    {
        left = top = width = height = 0;
        if (el.GetCachedPropertyValue(UIA_BoundingRectanglePropertyId, out object? val) == 0
            && val is double[] arr && arr.Length == 4)
        {
            left = arr[0];
            top = arr[1];
            width = arr[2];
            height = arr[3];
            return !double.IsNaN(left) && !double.IsNaN(width) && width > 0 && height > 0;
        }
        return false;
    }

    private static bool TryReadBool(IUIAutomationElement el, int propId, out bool result)
    {
        result = false;
        if (el.GetCachedPropertyValue(propId, out object? val) == 0 && val is bool b)
        {
            result = b;
            return true;
        }
        return false;
    }

    private static string ReadString(IUIAutomationElement el, int propId)
    {
        return el.GetCachedPropertyValue(propId, out object? val) == 0 && val is string s
            ? s
            : string.Empty;
    }

    private static int ReadInt(IUIAutomationElement el, int propId)
    {
        return el.GetCachedPropertyValue(propId, out object? val) == 0 && val is int i
            ? i
            : 0;
    }

    private static void ReleaseCom(object? obj)
    {
        if (obj != null && Marshal.IsComObject(obj))
        {
            try { Marshal.ReleaseComObject(obj); } catch { }
        }
    }

    // ─── COM UIA3 接口定义（严格对齐 Windows 11 26100 SDK UIAutomationClient.h vtable 顺序） ───

    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCondition { }

    [ComImport, Guid("b32a92b5-bc25-4078-9c08-d7ee95c48e03"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCacheRequest
    {
        [PreserveSig] int AddProperty(int propertyId);
        [PreserveSig] int AddPattern(int patternId);
        [PreserveSig] int Clone(out IUIAutomationCacheRequest clonedRequest);
        [PreserveSig] int get_TreeScope(out int scope);
        [PreserveSig] int put_TreeScope(int scope);
        [PreserveSig] int get_TreeFilter(out IUIAutomationCondition filter);
        [PreserveSig] int put_TreeFilter(IUIAutomationCondition filter);
        [PreserveSig] int get_AutomationElementMode(out int mode);
        [PreserveSig] int put_AutomationElementMode(int mode);
    }

    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElementArray
    {
        [PreserveSig] int get_Length(out int length);
        [PreserveSig] int GetElement(int index, out IUIAutomationElement element);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        [PreserveSig] int SetFocus();
        [PreserveSig] int GetRuntimeId(out IntPtr runtimeId);
        [PreserveSig] int FindFirst(int scope, IUIAutomationCondition condition, out IUIAutomationElement found);
        [PreserveSig] int FindAll(int scope, IUIAutomationCondition condition, out IUIAutomationElementArray found);
        [PreserveSig] int FindFirstBuildCache(int scope, IUIAutomationCondition condition, IUIAutomationCacheRequest cacheRequest, out IUIAutomationElement found);
        [PreserveSig] int FindAllBuildCache(int scope, IUIAutomationCondition condition, IUIAutomationCacheRequest cacheRequest, out IUIAutomationElementArray found);
        [PreserveSig] int BuildUpdatedCache(IUIAutomationCacheRequest cacheRequest, out IUIAutomationElement updatedElement);
        [PreserveSig] int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? retVal);
        [PreserveSig] int GetCurrentPropertyValueEx(int propertyId, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue, [MarshalAs(UnmanagedType.Struct)] out object? retVal);
        [PreserveSig] int GetCachedPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? retVal);
    }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        [PreserveSig] int CompareElements(IUIAutomationElement el1, IUIAutomationElement el2, [MarshalAs(UnmanagedType.Bool)] out bool areSame);
        [PreserveSig] int CompareRuntimeIds(IntPtr runtimeId1, IntPtr runtimeId2, [MarshalAs(UnmanagedType.Bool)] out bool areSame);
        [PreserveSig] int GetRootElement(out IUIAutomationElement root);
        [PreserveSig] int ElementFromHandle(IntPtr hwnd, out IUIAutomationElement element);
        [PreserveSig] int ElementFromPoint(long pt, out IUIAutomationElement element);
        [PreserveSig] int GetFocusedElement(out IUIAutomationElement element);
        [PreserveSig] int GetRootElementBuildCache(IUIAutomationCacheRequest cacheRequest, out IUIAutomationElement root);
        [PreserveSig] int ElementFromHandleBuildCache(IntPtr hwnd, IUIAutomationCacheRequest cacheRequest, out IUIAutomationElement element);
        [PreserveSig] int ElementFromPointBuildCache(long pt, IUIAutomationCacheRequest cacheRequest, out IUIAutomationElement element);
        [PreserveSig] int GetFocusedElementBuildCache(IUIAutomationCacheRequest cacheRequest, out IUIAutomationElement element);
        [PreserveSig] int CreateTreeWalker(IUIAutomationCondition condition, out IntPtr walker);
        [PreserveSig] int get_ControlViewWalker(out IntPtr walker);
        [PreserveSig] int get_ContentViewWalker(out IntPtr walker);
        [PreserveSig] int get_RawViewWalker(out IntPtr walker);
        [PreserveSig] int get_RawViewCondition(out IUIAutomationCondition condition);
        [PreserveSig] int get_ControlViewCondition(out IUIAutomationCondition condition);
        [PreserveSig] int get_ContentViewCondition(out IUIAutomationCondition condition);
        [PreserveSig] int CreateCacheRequest(out IUIAutomationCacheRequest cacheRequest);
        [PreserveSig] int CreateTrueCondition(out IUIAutomationCondition newCondition);
    }
}
