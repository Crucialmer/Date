using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Date.Models;
using WpfApplication = System.Windows.Application;

namespace Date.Services;

public static class UpdateService
{
    internal static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static int _isChecking;
    private static UpdateWindow? _activeWindow;

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
            return ver != null
                ? new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build))
                : new Version(1, 0, 0);
        }
    }

    private static Version NormalizeVersion(Version v) =>
        new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static async Task CheckForUpdatesAsync(AppConfig config, bool isManual)
    {
        // 若已有更新卡片正在展示且是手动触发，直接激活已有窗口避免重复弹窗
        if (Interlocked.CompareExchange(ref _isChecking, 1, 0) != 0)
        {
            if (isManual)
            {
                await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_activeWindow is { IsVisible: true })
                    {
                        _activeWindow.Activate();
                    }
                });
            }
            return;
        }

        UpdateWindow? updateWindow = null;

        try
        {
            await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
            {
                if (isManual)
                {
                    updateWindow = GetOrCreateUpdateWindow();
                    updateWindow.ShowChecking();
                }
            });

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{config.GitHubRepo}/releases/latest");
            request.Headers.Add("User-Agent", $"Date-App/{CurrentVersion.ToString(3)}");
            request.Headers.Add("Accept", "application/vnd.github.v3+json");

            using var response = await SharedHttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                if (isManual && updateWindow != null)
                {
                    await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (updateWindow.IsLoaded)
                        {
                            updateWindow.ShowError($"无法连接到服务器 (HTTP {(int)response.StatusCode})");
                        }
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
                        if (updateWindow.IsLoaded)
                        {
                            updateWindow.ShowUpToDate(CurrentVersion.ToString(3));
                        }
                    });
                }
                return;
            }

            config.LastUpdateCheck = DateTime.Now;
            config.Save();

            string tag = release.TagName.TrimStart('v', 'V').Trim();
            if (Version.TryParse(tag, out var parsedVersion))
            {
                var latestVersion = NormalizeVersion(parsedVersion);
                if (latestVersion > CurrentVersion)
                {
                    string downloadUrl = release.Assets
                        .FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        ?.BrowserDownloadUrl ?? string.Empty;
                    string notes = string.IsNullOrWhiteSpace(release.Body)
                        ? "暂无详细更新说明"
                        : release.Body.Trim();

                    await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                    {
                        updateWindow ??= GetOrCreateUpdateWindow();
                        updateWindow.ShowUpdateAvailable(
                            latestVersion.ToString(3),
                            CurrentVersion.ToString(3),
                            notes,
                            downloadUrl,
                            release.HtmlUrl);
                    });
                }
                else if (isManual && updateWindow != null)
                {
                    await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (updateWindow.IsLoaded)
                        {
                            updateWindow.ShowUpToDate(CurrentVersion.ToString(3));
                        }
                    });
                }
            }
            else if (isManual && updateWindow != null)
            {
                await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (updateWindow.IsLoaded)
                    {
                        updateWindow.ShowUpToDate(CurrentVersion.ToString(3));
                    }
                });
            }
        }
        catch (Exception ex)
        {
            if (isManual && updateWindow != null)
            {
                await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (updateWindow.IsLoaded)
                    {
                        updateWindow.ShowError(ex.Message);
                    }
                });
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    private static UpdateWindow GetOrCreateUpdateWindow()
    {
        if (_activeWindow is { IsVisible: true })
        {
            _activeWindow.Activate();
            return _activeWindow;
        }

        var window = new UpdateWindow(ThemeService.IsDarkMode());
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeWindow, window))
            {
                _activeWindow = null;
            }
        };
        _activeWindow = window;
        window.Show();
        return window;
    }
}
