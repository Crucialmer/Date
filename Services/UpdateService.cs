using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using Date.Models;
using WpfApplication = System.Windows.Application;

namespace Date.Services;

public class UpdateService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public class ReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }

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

        [JsonPropertyName("assets")]
        public List<ReleaseAsset> Assets { get; set; } = new();
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
        UpdateWindow? updateWindow = null;

        await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
        {
            if (isManual)
            {
                updateWindow = new UpdateWindow(ThemeService.IsDarkMode());
                updateWindow.Show();
                updateWindow.ShowChecking();
            }
        });

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{config.GitHubRepo}/releases/latest");
            request.Headers.Add("User-Agent", $"Date-App/{CurrentVersion}");
            request.Headers.Add("Accept", "application/vnd.github.v3+json");

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                if (isManual && updateWindow != null)
                {
                    await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                    {
                        updateWindow.ShowError($"无法连接到服务器 (HTTP {(int)response.StatusCode})");
                    });
                }
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                if (isManual && updateWindow != null)
                {
                    await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                    {
                        updateWindow.ShowUpToDate(CurrentVersion.ToString(3));
                    });
                }
                return;
            }

            config.LastUpdateCheck = DateTime.Now;
            config.Save();

            string tag = release.TagName.TrimStart('v', 'V').Trim();
            if (Version.TryParse(tag, out var latestVersion))
            {
                if (latestVersion > CurrentVersion)
                {
                    // 找到 zip 下载包
                    string downloadUrl = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl ?? string.Empty;
                    string notes = string.IsNullOrWhiteSpace(release.Body) ? "暂无详细说明" : release.Body.Trim();

                    await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (updateWindow == null)
                        {
                            updateWindow = new UpdateWindow(ThemeService.IsDarkMode());
                            updateWindow.Show();
                        }
                        updateWindow.ShowUpdateAvailable(latestVersion.ToString(), CurrentVersion.ToString(3), notes, downloadUrl, release.HtmlUrl);
                    });
                }
                else if (isManual && updateWindow != null)
                {
                    await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                    {
                        updateWindow.ShowUpToDate(CurrentVersion.ToString(3));
                    });
                }
            }
            else if (isManual && updateWindow != null)
            {
                await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                {
                    updateWindow.ShowUpToDate(CurrentVersion.ToString(3));
                });
            }
        }
        catch (Exception ex)
        {
            if (isManual && updateWindow != null)
            {
                await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                {
                    updateWindow.ShowError(ex.Message);
                });
            }
        }
    }
}
