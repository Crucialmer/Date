using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Date.Services;
using MediaColor = System.Windows.Media.Color;
using MediaPoint = System.Windows.Point;

namespace Date;

public partial class UpdateWindow : Window
{
    private string _downloadUrl = string.Empty;
    private string _releasePageUrl = string.Empty;
    private bool _isDarkMode;
    private readonly DispatcherTimer _autoCloseTimer;
    private CancellationTokenSource? _downloadCts;

    // ─── macOS 26 Liquid Glass 更新卡片材质（双环折射，冻结复用） ───
    private static readonly LinearGradientBrush DarkCardBg;
    private static readonly LinearGradientBrush DarkCardBorder;
    private static readonly LinearGradientBrush DarkCardInnerRim;
    private static readonly LinearGradientBrush DarkCardSheen;
    private static readonly SolidColorBrush DarkNotesBg;
    private static readonly SolidColorBrush DarkNotesBorder;
    private static readonly SolidColorBrush DarkFg;
    private static readonly SolidColorBrush DarkSecondaryFg;
    private static readonly SolidColorBrush DarkCancelFg;
    private static readonly SolidColorBrush DarkAccent;
    private static readonly SolidColorBrush DarkAccentHover;
    private static readonly SolidColorBrush DarkAccentPressed;
    private static readonly SolidColorBrush DarkAccentPillBg;
    private static readonly SolidColorBrush DarkCloseHover;
    private static readonly SolidColorBrush DarkSecondaryBtnBg;
    private static readonly SolidColorBrush DarkSecondaryBtnHover;
    private static readonly SolidColorBrush DarkProgressTrack;
    private static readonly SolidColorBrush DarkScrollThumb;

    private static readonly LinearGradientBrush LightCardBg;
    private static readonly LinearGradientBrush LightCardBorder;
    private static readonly LinearGradientBrush LightCardInnerRim;
    private static readonly LinearGradientBrush LightCardSheen;
    private static readonly SolidColorBrush LightNotesBg;
    private static readonly SolidColorBrush LightNotesBorder;
    private static readonly SolidColorBrush LightFg;
    private static readonly SolidColorBrush LightSecondaryFg;
    private static readonly SolidColorBrush LightCancelFg;
    private static readonly SolidColorBrush LightAccent;
    private static readonly SolidColorBrush LightAccentHover;
    private static readonly SolidColorBrush LightAccentPressed;
    private static readonly SolidColorBrush LightAccentPillBg;
    private static readonly SolidColorBrush LightCloseHover;
    private static readonly SolidColorBrush LightSecondaryBtnBg;
    private static readonly SolidColorBrush LightSecondaryBtnHover;
    private static readonly SolidColorBrush LightProgressTrack;
    private static readonly SolidColorBrush LightScrollThumb;

    static UpdateWindow()
    {
        DarkCardBg = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0xF0, 0x26, 0x26, 0x2E), 0.0),
            (MediaColor.FromArgb(0xF4, 0x1A, 0x1A, 0x20), 1.0));
        DarkCardBorder = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x52, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.5),
            (MediaColor.FromArgb(0x32, 0xFF, 0xFF, 0xFF), 1.0));
        DarkCardInnerRim = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x20, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.5),
            (MediaColor.FromArgb(0x0E, 0xFF, 0xFF, 0xFF), 1.0));
        DarkCardSheen = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
        DarkNotesBg = FreezeBrush(MediaColor.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
        DarkNotesBorder = FreezeBrush(MediaColor.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
        DarkFg = FreezeBrush(MediaColor.FromRgb(0xF5, 0xF5, 0xF7));
        DarkSecondaryFg = FreezeBrush(MediaColor.FromArgb(0xA6, 0xEB, 0xEB, 0xF5));
        DarkCancelFg = FreezeBrush(MediaColor.FromArgb(0xDE, 0xFF, 0xFF, 0xFF));
        DarkAccent = FreezeBrush(MediaColor.FromRgb(0x0A, 0x84, 0xFF));
        DarkAccentHover = FreezeBrush(MediaColor.FromRgb(0x29, 0x97, 0xFF));
        DarkAccentPressed = FreezeBrush(MediaColor.FromRgb(0x00, 0x6A, 0xE0));
        DarkAccentPillBg = FreezeBrush(MediaColor.FromArgb(0x28, 0x0A, 0x84, 0xFF));
        DarkCloseHover = FreezeBrush(MediaColor.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        DarkSecondaryBtnBg = FreezeBrush(MediaColor.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        DarkSecondaryBtnHover = FreezeBrush(MediaColor.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        DarkProgressTrack = FreezeBrush(MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        DarkScrollThumb = FreezeBrush(MediaColor.FromArgb(0x46, 0xFF, 0xFF, 0xFF));

        LightCardBg = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0xF8, 0xFD, 0xFD, 0xFF), 0.0),
            (MediaColor.FromArgb(0xF2, 0xF1, 0xF3, 0xF8), 1.0));
        LightCardBorder = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x20, 0x00, 0x00, 0x00), 0.0),
            (MediaColor.FromArgb(0x26, 0x00, 0x00, 0x00), 0.5),
            (MediaColor.FromArgb(0x34, 0x00, 0x00, 0x00), 1.0));
        LightCardInnerRim = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0xEE, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 0.5),
            (MediaColor.FromArgb(0x88, 0xFF, 0xFF, 0xFF), 1.0));
        LightCardSheen = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
        LightNotesBg = FreezeBrush(MediaColor.FromArgb(0x0A, 0x00, 0x00, 0x00));
        LightNotesBorder = FreezeBrush(MediaColor.FromArgb(0x12, 0x00, 0x00, 0x00));
        LightFg = FreezeBrush(MediaColor.FromRgb(0x14, 0x14, 0x18));
        LightSecondaryFg = FreezeBrush(MediaColor.FromArgb(0x9E, 0x3C, 0x3C, 0x43));
        LightCancelFg = FreezeBrush(MediaColor.FromRgb(0x2C, 0x2C, 0x30));
        LightAccent = FreezeBrush(MediaColor.FromRgb(0x00, 0x7A, 0xFF));
        LightAccentHover = FreezeBrush(MediaColor.FromRgb(0x00, 0x68, 0xDC));
        LightAccentPressed = FreezeBrush(MediaColor.FromRgb(0x00, 0x55, 0xBA));
        LightAccentPillBg = FreezeBrush(MediaColor.FromArgb(0x18, 0x00, 0x7A, 0xFF));
        LightCloseHover = FreezeBrush(MediaColor.FromArgb(0x14, 0x00, 0x00, 0x00));
        LightSecondaryBtnBg = FreezeBrush(MediaColor.FromArgb(0x0E, 0x00, 0x00, 0x00));
        LightSecondaryBtnHover = FreezeBrush(MediaColor.FromArgb(0x18, 0x00, 0x00, 0x00));
        LightProgressTrack = FreezeBrush(MediaColor.FromArgb(0x18, 0x00, 0x00, 0x00));
        LightScrollThumb = FreezeBrush(MediaColor.FromArgb(0x38, 0x00, 0x00, 0x00));
    }

    private static SolidColorBrush FreezeBrush(MediaColor color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush FreezeGradient(
        MediaPoint start,
        MediaPoint end,
        params (MediaColor Color, double Offset)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop(c, o));
        b.Freeze();
        return b;
    }

    public UpdateWindow(bool isDarkMode)
    {
        _isDarkMode = isDarkMode;
        InitializeComponent();

        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3.5)
        };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            Close();
        };

        ApplyTheme(_isDarkMode);
        LoadAppLogo();

        ThemeService.ThemeChanged += OnThemeChanged;
        Loaded += UpdateWindow_Loaded;
    }

    private void OnThemeChanged(bool isDarkMode)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded) ApplyTheme(isDarkMode);
        });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _autoCloseTimer.Stop();
        _downloadCts?.Cancel();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        _downloadCts?.Dispose();
        _downloadCts = null;
        base.OnClosed(e);
    }

    private void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RepositionBottomRight();

        // macOS 26 柔和上浮淡入动效
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
        var slideAnim = new DoubleAnimation(12.0, 0.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };

        BeginAnimation(OpacityProperty, fadeAnim);
        CardTranslate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
    }

    private void RepositionBottomRight()
    {
        UpdateLayout();
        Left = SystemParameters.WorkArea.Right - ActualWidth - 6;
        Top = SystemParameters.WorkArea.Bottom - ActualHeight - 6;
    }

    private void ApplyTheme(bool isDarkMode)
    {
        _isDarkMode = isDarkMode;

        if (_isDarkMode)
        {
            CardContainer.Background = DarkCardBg;
            CardContainer.BorderBrush = DarkCardBorder;
            CardInnerRim.BorderBrush = DarkCardInnerRim;
            CardSheen.Background = DarkCardSheen;
            NotesContainer.Background = DarkNotesBg;
            NotesContainer.BorderBrush = DarkNotesBorder;
            CardShadow.Color = MediaColor.FromRgb(0x00, 0x00, 0x00);
            CardShadow.Opacity = 0.44;

            TxtTitle.Foreground = DarkFg;
            TxtStatus.Foreground = DarkFg;
            TxtReleaseNotes.Foreground = DarkFg;
            TxtProgressDesc.Foreground = DarkFg;
            TxtPercent.Foreground = DarkFg;
            BtnCancel.Foreground = DarkCancelFg;

            Resources["AccentBrush"] = DarkAccent;
            Resources["AccentHoverBrush"] = DarkAccentHover;
            Resources["AccentPressedBrush"] = DarkAccentPressed;
            Resources["AccentPillBgBrush"] = DarkAccentPillBg;
            Resources["SecondaryFgBrush"] = DarkSecondaryFg;
            Resources["CloseHoverBrush"] = DarkCloseHover;
            Resources["SecondaryBtnBgBrush"] = DarkSecondaryBtnBg;
            Resources["SecondaryBtnHoverBrush"] = DarkSecondaryBtnHover;
            Resources["ProgressTrackBrush"] = DarkProgressTrack;
            Resources["ScrollThumbBrush"] = DarkScrollThumb;
        }
        else
        {
            CardContainer.Background = LightCardBg;
            CardContainer.BorderBrush = LightCardBorder;
            CardInnerRim.BorderBrush = LightCardInnerRim;
            CardSheen.Background = LightCardSheen;
            NotesContainer.Background = LightNotesBg;
            NotesContainer.BorderBrush = LightNotesBorder;
            CardShadow.Color = MediaColor.FromRgb(0x38, 0x40, 0x52);
            CardShadow.Opacity = 0.18;

            TxtTitle.Foreground = LightFg;
            TxtStatus.Foreground = LightFg;
            TxtReleaseNotes.Foreground = LightFg;
            TxtProgressDesc.Foreground = LightFg;
            TxtPercent.Foreground = LightFg;
            BtnCancel.Foreground = LightCancelFg;

            Resources["AccentBrush"] = LightAccent;
            Resources["AccentHoverBrush"] = LightAccentHover;
            Resources["AccentPressedBrush"] = LightAccentPressed;
            Resources["AccentPillBgBrush"] = LightAccentPillBg;
            Resources["SecondaryFgBrush"] = LightSecondaryFg;
            Resources["CloseHoverBrush"] = LightCloseHover;
            Resources["SecondaryBtnBgBrush"] = LightSecondaryBtnBg;
            Resources["SecondaryBtnHoverBrush"] = LightSecondaryBtnHover;
            Resources["ProgressTrackBrush"] = LightProgressTrack;
            Resources["ScrollThumbBrush"] = LightScrollThumb;
        }
    }

    private void LoadAppLogo()
    {
        try
        {
            string pngPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.png");
            Uri sourceUri = File.Exists(pngPath)
                ? new Uri(pngPath, UriKind.Absolute)
                : new Uri("pack://application:,,,/Assets/app.png", UriKind.Absolute);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = sourceUri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 64;
            bitmap.EndInit();
            bitmap.Freeze();
            AppLogo.Source = bitmap;
        }
        catch { }
    }

    public void ShowChecking()
    {
        _autoCloseTimer.Stop();
        PanelChecking.Visibility = Visibility.Visible;
        PanelUpdateDetails.Visibility = Visibility.Collapsed;
        PanelProgress.Visibility = Visibility.Collapsed;
        BtnAction.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Collapsed;

        TxtStatus.Text = "正在连接 GitHub 检查最新版本...";
        RepositionBottomRight();
    }

    public void ShowUpToDate(string currentVersion)
    {
        PanelChecking.Visibility = Visibility.Visible;
        PanelUpdateDetails.Visibility = Visibility.Collapsed;
        PanelProgress.Visibility = Visibility.Collapsed;
        BtnAction.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Collapsed;

        TxtStatus.Text = $"当前已是最新版本 (v{currentVersion})，无需更新。";
        RepositionBottomRight();
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
        RepositionBottomRight();
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

        TxtNewVersion.Text = $"新版本 v{newVersion}";
        TxtCurrentVersion.Text = $"当前 v{currentVersion}";
        TxtReleaseNotes.Text = string.IsNullOrWhiteSpace(notes) ? "无详细更新说明" : notes;

        BtnCancel.Visibility = Visibility.Visible;
        BtnAction.Visibility = Visibility.Visible;
        BtnAction.Content = "立即更新";

        RepositionBottomRight();
    }

    private async void BtnAction_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_downloadUrl))
        {
            if (!string.IsNullOrWhiteSpace(_releasePageUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = _releasePageUrl, UseShellExecute = true });
                }
                catch { }
            }
            Close();
            return;
        }

        PanelUpdateDetails.Visibility = Visibility.Collapsed;
        PanelProgress.Visibility = Visibility.Visible;
        BtnAction.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Collapsed;
        RepositionBottomRight();

        await StartDownloadAndUpdateAsync(_downloadUrl);
    }

    private async Task StartDownloadAndUpdateAsync(string url)
    {
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        CancellationToken ct = _downloadCts.Token;

        string zipPath = Path.Combine(AppContext.BaseDirectory, "update_download.zip");
        string extractPath = Path.Combine(AppContext.BaseDirectory, "update_temp");

        try
        {
            TxtProgressDesc.Text = "正在下载更新包...";
            ProgressBarUpdate.Value = 0;
            TxtPercent.Text = "0%";

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            client.DefaultRequestHeaders.Add("User-Agent", $"Date-Updater/{UpdateService.CurrentVersion.ToString(3)}");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 16384, true))
            {
                var buffer = new byte[16384];
                long totalRead = 0;
                int bytesRead;
                long lastUiUpdateTick = 0;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;

                    long nowTick = Environment.TickCount64;
                    if (nowTick - lastUiUpdateTick >= 60)
                    {
                        lastUiUpdateTick = nowTick;
                        if (totalBytes is > 0)
                        {
                            double progress = (double)totalRead / totalBytes.Value * 100.0;
                            ProgressBarUpdate.Value = progress;
                            TxtPercent.Text = $"{(int)progress}%";
                            TxtProgressDesc.Text = $"正在下载 ({totalRead / 1048576.0:F1} MB / {totalBytes.Value / 1048576.0:F1} MB)";
                        }
                        else
                        {
                            TxtProgressDesc.Text = $"正在下载 ({totalRead / 1048576.0:F1} MB)";
                        }
                    }
                }
            }

            ct.ThrowIfCancellationRequested();

            TxtProgressDesc.Text = "下载完成，正在自动应用更新...";
            ProgressBarUpdate.Value = 100;
            TxtPercent.Text = "100%";

            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            ZipFile.ExtractToDirectory(zipPath, extractPath, true);

            string extractedExe = Path.Combine(extractPath, "Date.exe");
            if (!File.Exists(extractedExe))
            {
                throw new FileNotFoundException("更新包内未找到 Date.exe");
            }

            // 带重试保护与 UTF-8 路径兼容的自更新批处理脚本：等待进程退出释放锁后安全替换并重启
            string appDir = AppContext.BaseDirectory.TrimEnd('\\');
            string targetExe = Path.Combine(appDir, "Date.exe");
            string batPath = Path.Combine(Path.GetTempPath(), $"Date_update_{Environment.ProcessId}.cmd");
            string batContent =
                "@echo off\r\n" +
                "chcp 65001 >nul\r\n" +
                "for /L %%i in (1,1,10) do (\r\n" +
                "  timeout /t 1 /nobreak >nul\r\n" +
                $"  copy /y \"{extractedExe}\" \"{targetExe}\" >nul 2>&1 && goto :done\r\n" +
                ")\r\n" +
                ":done\r\n" +
                $"rmdir /s /q \"{extractPath}\" >nul 2>&1\r\n" +
                $"del /f /q \"{zipPath}\" >nul 2>&1\r\n" +
                $"start \"\" \"{targetExe}\"\r\n" +
                "del /f /q \"%~f0\" >nul 2>&1\r\n";

            File.WriteAllText(batPath, batContent, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            App.ExitApp();
        }
        catch (OperationCanceledException)
        {
            CleanupTempFiles(zipPath, extractPath);
        }
        catch
        {
            CleanupTempFiles(zipPath, extractPath);
            if (!IsLoaded) return;

            TxtProgressDesc.Text = "自动更新失败，请前往网页手动下载";
            TxtPercent.Text = "";
            BtnAction.Visibility = Visibility.Visible;
            BtnAction.Content = "打开网页下载";
            _downloadUrl = string.Empty;
            RepositionBottomRight();
        }
    }

    private static void CleanupTempFiles(string zipPath, string extractPath)
    {
        try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
        try { if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true); } catch { }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
