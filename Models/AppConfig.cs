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
                if (config != null) return config;
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

    public void Save()
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
