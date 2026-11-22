using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using Date.Models;
using WpfMessageBox = System.Windows.MessageBox;

namespace Date.Services;

public class UpdateService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }
    }

    public static Version CurrentVersion
    {
        get
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            return ver != null ? new Version(ver.Major, ver.Minor, ver.Build >= 0 ? ver.Build : 0) : new Version(1, 0, 0);
        }
    }

    public static async Task CheckForUpdatesAsync(AppConfig config, bool isManual)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{config.GitHubRepo}/releases/latest");
            request.Headers.Add("User-Agent", $"Date-App/{CurrentVersion}");
            request.Headers.Add("Accept", "application/vnd.github.v3+json");

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                if (isManual)
                {
                    WpfMessageBox.Show(
                        $"检查更新失败 (HTTP {(int)response.StatusCode})\n请检查网络连接或稍后重试。",
                        "检查更新 - Date",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                if (isManual)
                {
                    WpfMessageBox.Show(
                        $"当前已是最新版本 (v{CurrentVersion.ToString(3)})！\n暂无可用更新。",
                        "检查更新 - Date",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }

            config.LastUpdateCheck = DateTime.Now;
            config.Save();

            // 解析版本号（剥除前缀 'v' 或 'V'）
            string tag = release.TagName.TrimStart('v', 'V').Trim();
            if (Version.TryParse(tag, out var latestVersion))
            {
                if (latestVersion > CurrentVersion)
                {
                    string notes = string.IsNullOrWhiteSpace(release.Body) ? "无更新说明" : release.Body.Trim();
                    if (notes.Length > 200) notes = notes[..200] + "...";

                    var result = WpfMessageBox.Show(
                        $"发现新版本: v{latestVersion} (当前版本: v{CurrentVersion.ToString(3)})\n\n更新说明:\n{notes}\n\n是否前往 GitHub 下载最新绿色版？",
                        "发现新版本 - Date",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(release.HtmlUrl))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = release.HtmlUrl,
                            UseShellExecute = true
                        });
                    }
                }
                else if (isManual)
                {
                    WpfMessageBox.Show(
                        $"当前已是最新版本 (v{CurrentVersion.ToString(3)})！",
                        "检查更新 - Date",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            else if (isManual)
            {
                WpfMessageBox.Show(
                    $"当前已是最新版本 (v{CurrentVersion.ToString(3)})！",
                    "检查更新 - Date",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            if (isManual)
            {
                WpfMessageBox.Show(
                    $"检查更新过程中发生异常：\n{ex.Message}",
                    "检查更新 - Date",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
