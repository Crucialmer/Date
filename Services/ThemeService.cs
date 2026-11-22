using System;
using Microsoft.Win32;

namespace Date.Services;

public class ThemeService
{
    public static event Action<bool>? ThemeChanged; // bool isDarkMode

    private static string _currentMode = "Auto";

    public static void Initialize(string mode)
    {
        _currentMode = mode;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Window)
        {
            if (_currentMode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                ThemeChanged?.Invoke(IsSystemDarkMode());
            }
        }
    }

    public static void SetMode(string mode)
    {
        _currentMode = mode;
        ThemeChanged?.Invoke(IsDarkMode());
    }

    public static bool IsDarkMode()
    {
        if (_currentMode.Equals("Dark", StringComparison.OrdinalIgnoreCase))
            return true;
        if (_currentMode.Equals("Light", StringComparison.OrdinalIgnoreCase))
            return false;

        return IsSystemDarkMode();
    }

    public static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                // Windows 11 任务栏通常跟随 SystemUsesLightTheme
                object? systemUsesLight = key.GetValue("SystemUsesLightTheme");
                if (systemUsesLight is int val)
                {
                    return val == 0;
                }
            }
        }
        catch
        {
            // 读取失败默认返回深色模式（Win11 默认任务栏基调）
        }
        return true;
    }
}
