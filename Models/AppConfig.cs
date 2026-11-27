using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Date.Models;

public class AppConfig
{
    private const int LatestConfigVersion = 3;

    // 默认 0 以便反序列化不含此字段的旧版 config.json 时准确触发一次性迁移
    public int ConfigVersion { get; set; } = 0;

    public string DateFormat { get; set; } = "yyyy年M月d日";
    public string DayOfWeekFormat { get; set; } = "dddd";

    // 是否在星期几右侧显示实时时间 (HH:mm)
    public bool ShowTime { get; set; } = true;
    public string TimeFormat { get; set; } = "HH:mm";

    // "Auto", "Dark", "Light"
    public string ThemeMode { get; set; } = "Auto";

    // 距离托盘区域（向上展开箭头 ^）左侧的逻辑间距（DIP，默认 0，跨分辨率与跨缩放比自动等比统一）
    public int OffsetLeft { get; set; } = 0;

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
                    bool migrated = config.SanitizeAndMigrate();
                    if (migrated) config.Save();
                    return config;
                }
            }
        }
        catch
        {
            // 读取异常时回退到默认设置
        }

        var defaultConfig = new AppConfig { ConfigVersion = LatestConfigVersion };
        defaultConfig.Save();
        return defaultConfig;
    }

    private bool SanitizeAndMigrate()
    {
        bool changed = false;

        if (string.IsNullOrWhiteSpace(DateFormat)) { DateFormat = "yyyy年M月d日"; changed = true; }
        if (string.IsNullOrWhiteSpace(DayOfWeekFormat)) { DayOfWeekFormat = "dddd"; changed = true; }
        if (string.IsNullOrWhiteSpace(TimeFormat)) { TimeFormat = "HH:mm"; changed = true; }
        if (string.IsNullOrWhiteSpace(GitHubRepo)) { GitHubRepo = "Crucialmer/Date"; changed = true; }

        if (!ThemeMode.Equals("Auto", StringComparison.OrdinalIgnoreCase) &&
            !ThemeMode.Equals("Dark", StringComparison.OrdinalIgnoreCase) &&
            !ThemeMode.Equals("Light", StringComparison.OrdinalIgnoreCase))
        {
            ThemeMode = "Auto";
            changed = true;
        }

        // 将旧版未手动改动过的默认 OffsetLeft (8 或 12) 一次性平滑迁移至新默认 0 DIP
        if (ConfigVersion < LatestConfigVersion)
        {
            if (OffsetLeft is 8 or 12)
            {
                OffsetLeft = 0;
            }
            ConfigVersion = LatestConfigVersion;
            changed = true;
        }

        if (OffsetLeft < 0 || OffsetLeft > 200)
        {
            OffsetLeft = 0;
            changed = true;
        }

        return changed;
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
