using System;
using Microsoft.Win32;

namespace Date.Services;

public static class ThemeService
{
    public static event Action<bool>? ThemeChanged; // bool isDarkMode

    private static string _currentMode = "Auto";
    private static bool _isInitialized;
    private static bool _lastNotifiedDarkMode;

    public static void Initialize(string mode)
    {
        _currentMode = mode;
        _lastNotifiedDarkMode = IsDarkMode();

        if (!_isInitialized)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _isInitialized = true;
        }
    }

    public static void Shutdown()
    {
        if (_isInitialized)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _isInitialized = false;
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Window)
        {
            if (_currentMode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                bool isDark = IsSystemDarkMode();
                if (isDark != _lastNotifiedDarkMode)
                {
                    _lastNotifiedDarkMode = isDark;
                    ThemeChanged?.Invoke(isDark);
                }
            }
        }
    }

    public static void SetMode(string mode)
    {
        _currentMode = mode;
        bool isDark = IsDarkMode();
        _lastNotifiedDarkMode = isDark;
        ThemeChanged?.Invoke(isDark);
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
            if (key?.GetValue("SystemUsesLightTheme") is int val)
            {
                return val == 0;
            }
        }
        catch
        {
            // 读取失败默认返回深色模式（Win11 默认任务栏基调）
        }
        return true;
    }
}
