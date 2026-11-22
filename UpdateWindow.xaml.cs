using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Date.Services;
using MediaColor = System.Windows.Media.Color;

namespace Date;

public partial class UpdateWindow : Window
{
    private string _downloadUrl = string.Empty;
    private string _releasePageUrl = string.Empty;
    private readonly bool _isDarkMode;
    private readonly DispatcherTimer _autoCloseTimer;

    public UpdateWindow(bool isDarkMode)
    {
        _isDarkMode = isDarkMode;
        InitializeComponent();

        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3.5)
        };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            Close();
        };

        ApplyTheme();
        LoadAppLogo();

        Loaded += UpdateWindow_Loaded;
    }

    private void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 定位至屏幕右下角（任务栏工作区边缘，留出 16px 边距）
        UpdateLayout();
        this.Left = SystemParameters.WorkArea.Right - this.ActualWidth - 16;
        this.Top = SystemParameters.WorkArea.Bottom - this.ActualHeight - 16;
    }

    private void ApplyTheme()
    {
        if (_isDarkMode)
        {
            CardContainer.Background = new SolidColorBrush(MediaColor.FromArgb(0xF2, 0x1E, 0x1E, 0x22));
            CardContainer.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            NotesContainer.Background = new SolidColorBrush(MediaColor.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
            CardShadow.Color = MediaColor.FromRgb(0x00, 0x00, 0x00);
            CardShadow.Opacity = 0.45;

            var fg = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0xFF, 0xFF));
            TxtTitle.Foreground = fg;
            TxtStatus.Foreground = fg;
            TxtReleaseNotes.Foreground = fg;
            TxtProgressDesc.Foreground = fg;
            TxtPercent.Foreground = fg;
            BtnCancel.Foreground = new SolidColorBrush(MediaColor.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        }
        else
        {
            CardContainer.Background = new SolidColorBrush(MediaColor.FromArgb(0xF6, 0xF6, 0xF6, 0xF9));
            CardContainer.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x35, 0x00, 0x00, 0x00));
            NotesContainer.Background = new SolidColorBrush(MediaColor.FromArgb(0x12, 0x00, 0x00, 0x00));
            CardShadow.Color = MediaColor.FromRgb(0x50, 0x50, 0x50);
            CardShadow.Opacity = 0.20;

            var fg = new SolidColorBrush(MediaColor.FromRgb(0x11, 0x11, 0x13));
            TxtTitle.Foreground = fg;
            TxtStatus.Foreground = fg;
            TxtReleaseNotes.Foreground = fg;
            TxtProgressDesc.Foreground = fg;
            TxtPercent.Foreground = fg;
            BtnCancel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x3A, 0x3A, 0x3C));
        }
    }

    private void LoadAppLogo()
    {
        try
        {
            string pngPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.png");
            if (File.Exists(pngPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(pngPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                AppLogo.Source = bitmap;
            }
        }
        catch { }
    }

    public void ShowChecking()
    {
        PanelChecking.Visibility = Visibility.Visible;
        PanelUpdateDetails.Visibility = Visibility.Collapsed;
        PanelProgress.Visibility = Visibility.Collapsed;
        BtnAction.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Collapsed;

        TxtStatus.Text = "正在连接 GitHub 检查最新版本...";
    }

    public void ShowUpToDate(string currentVersion)
    {
        PanelChecking.Visibility = Visibility.Visible;
        PanelUpdateDetails.Visibility = Visibility.Collapsed;
        PanelProgress.Visibility = Visibility.Collapsed;
        BtnAction.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Collapsed;

        TxtStatus.Text = $"🎉 当前已是最新版本 (v{currentVersion})，无需更新。";
        _autoCloseTimer.Start();
    }

    public void ShowError(string message)
    {
        PanelChecking.Visibility = Visibility.Visible;
        PanelUpdateDetails.Visibility = Visibility.Collapsed;
        PanelProgress.Visibility = Visibility.Collapsed;
        BtnAction.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Collapsed;

        TxtStatus.Text = $"检查更新遇到问题：\n{message}";
        _autoCloseTimer.Start();
    }

    public void ShowUpdateAvailable(string newVersion, string currentVersion, string notes, string downloadUrl, string releasePageUrl)
    {
        _autoCloseTimer.Stop();
        _downloadUrl = downloadUrl;
        _releasePageUrl = releasePageUrl;

        PanelChecking.Visibility = Visibility.Collapsed;
        PanelUpdateDetails.Visibility = Visibility.Visible;
        PanelProgress.Visibility = Visibility.Collapsed;

        TxtNewVersion.Text = $"新版本: v{newVersion}";
        TxtCurrentVersion.Text = $"当前: v{currentVersion}";
        TxtReleaseNotes.Text = string.IsNullOrWhiteSpace(notes) ? "无详细更新说明" : notes;

        BtnCancel.Visibility = Visibility.Visible;
        BtnAction.Visibility = Visibility.Visible;
        BtnAction.Content = "立即更新";

        // 重新调整位置以匹配内容高度
        UpdateLayout();
        this.Left = SystemParameters.WorkArea.Right - this.ActualWidth - 16;
        this.Top = SystemParameters.WorkArea.Bottom - this.ActualHeight - 16;
    }

    private async void BtnAction_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_downloadUrl))
        {
            if (!string.IsNullOrWhiteSpace(_releasePageUrl))
            {
                Process.Start(new ProcessStartInfo { FileName = _releasePageUrl, UseShellExecute = true });
            }
            Close();
            return;
        }

        // 切换到下载进度面板
        PanelUpdateDetails.Visibility = Visibility.Collapsed;
        PanelProgress.Visibility = Visibility.Visible;
        BtnAction.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Collapsed;

        await StartDownloadAndUpdateAsync(_downloadUrl);
    }

    private async Task StartDownloadAndUpdateAsync(string url)
    {
        try
        {
            TxtProgressDesc.Text = "正在下载更新包...";
            ProgressBarUpdate.Value = 0;
            TxtPercent.Text = "0%";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Date-Updater");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            string zipPath = Path.Combine(AppContext.BaseDirectory, "update_download.zip");

            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double progress = (double)totalRead / totalBytes.Value * 100.0;
                        ProgressBarUpdate.Value = progress;
                        TxtPercent.Text = $"{(int)progress}%";
                        TxtProgressDesc.Text = $"正在下载 ({totalRead / 1024 / 1024:F1} MB / {totalBytes.Value / 1024 / 1024:F1} MB)";
                    }
                }
            }

            TxtProgressDesc.Text = "下载完成，正在自动应用更新...";
            ProgressBarUpdate.Value = 100;
            TxtPercent.Text = "100%";

            // 解压到本地缓存
            string extractPath = Path.Combine(AppContext.BaseDirectory, "update_temp");
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            ZipFile.ExtractToDirectory(zipPath, extractPath, true);

            // 启动独立脚本替换 Date.exe 并重启软件
            string appDir = AppContext.BaseDirectory.TrimEnd('\\');
            string script = $"/c timeout /t 1 /nobreak & copy /y \"{extractPath}\\Date.exe\" \"{appDir}\\Date.exe\" & rmdir /s /q \"{extractPath}\" & del /f /q \"{zipPath}\" & start \"\" \"{appDir}\\Date.exe\"";

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = script,
                CreateNoWindow = true,
                UseShellExecute = false
            });

            // 优雅退出当前进程
            App.ExitApp();
        }
        catch
        {
            TxtProgressDesc.Text = "自动更新失败，请前往网页手动下载";
            TxtPercent.Text = "";
            BtnAction.Visibility = Visibility.Visible;
            BtnAction.Content = "打开网页下载";
            _downloadUrl = string.Empty; // 点击后直接打开网页
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
