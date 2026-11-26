using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Date.Models;

public class AppConfig
{
    public string DateFormat { get; set; } = "yyyy年M月d日";
    public string DayOfWeekFormat { get; set; } = "dddd";

    // 是否在星期几右侧显示实时时间 (HH:mm)
    public bool ShowTime { get; set; } = true;
    public string TimeFormat { get; set; } = "HH:mm";

    // "Auto", "Dark", "Light"
    public string ThemeMode { get; set; } = "Auto";

    // 距离托盘区域左侧的像素间距
    public int OffsetLeft { get; set; } = 8;

    // 是否启动时自动检查更新
    public bool CheckUpdatesOnStartup { get; set; } = true;

    // 是否开机自启动
    public bool AutoStart { get; set; } = false;

    public DateTime? LastUpdateCheck { get; set; } = null;

    public string GitHubRepo { get; set; } = "Crucialmer/Date";

    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config != null)
                {
                    config.Sanitize();
                    return config;
                }
            }
        }
        catch
        {
            // 读取异常时回退到默认设置
        }

        var defaultConfig = new AppConfig();
        defaultConfig.Save();
        return defaultConfig;
    }

    private void Sanitize()
    {
        if (string.IsNullOrWhiteSpace(DateFormat)) DateFormat = "yyyy年M月d日";
        if (string.IsNullOrWhiteSpace(DayOfWeekFormat)) DayOfWeekFormat = "dddd";
        if (string.IsNullOrWhiteSpace(TimeFormat)) TimeFormat = "HH:mm";
        if (string.IsNullOrWhiteSpace(GitHubRepo)) GitHubRepo = "Crucialmer/Date";

        if (!ThemeMode.Equals("Auto", StringComparison.OrdinalIgnoreCase) &&
            !ThemeMode.Equals("Dark", StringComparison.OrdinalIgnoreCase) &&
            !ThemeMode.Equals("Light", StringComparison.OrdinalIgnoreCase))
        {
            ThemeMode = "Auto";
        }
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, JsonOptions);
            string tempPath = ConfigPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, ConfigPath, overwrite: true);
        }
        catch
        {
            try
            {
                string json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // 绿色软件，忽略保存瞬时异常
            }
        }
    }
}
