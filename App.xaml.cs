using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Date.Models;
using Date.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Date;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static NotifyIcon? _trayIcon;
    private static MainWindow? _mainWindow;
    private static AppConfig? _config;
    private static volatile bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. 全局异常兜底保护，杜绝任何未捕获异常导致的“闪退”
        SetupGlobalExceptionHandling();

        // 2. 单实例检查（纯绿色软件，避免多开重复悬浮）
        const string mutexName = "Date_App_SingleInstance_Mutex_Crucialmer";
        try
        {
            _mutex = new Mutex(true, mutexName, out bool createdNew);
            if (!createdNew)
            {
                _mutex.Dispose();
                _mutex = null;
                MessageBox.Show("Date 已经在运行中！可在屏幕右下角系统托盘找到它。", "提示 - Date", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }
        catch (AbandonedMutexException)
        {
            // 上一个实例非正常退出留下的孤立互斥体，继续运行
        }

        // 3. 加载本地便携配置
        _config = AppConfig.Load();

        // 4. 初始化深浅色主题监听服务
        ThemeService.Initialize(_config.ThemeMode);

        // 5. 同步开机自启动设置（若移动了程序目录自动纠正路径）
        if (_config.AutoStart && !AutoStartService.IsAutoStartEnabled())
        {
            AutoStartService.SetAutoStart(true);
        }

        // 6. 创建并呈现任务栏日期悬浮胶囊
        _mainWindow = new MainWindow(_config);
        _mainWindow.Show();

        // 7. 初始化系统托盘图标
        InitTrayIcon();

        // 8. 每次打开软件时后台自动异步检查更新（静默检测，发现新版本立即在右下角弹出提示）
        if (_config.CheckUpdatesOnStartup)
        {
            var cfg = _config;
            _ = Task.Run(async () =>
            {
                await Task.Delay(2500);
                if (!_isExiting)
                {
                    await UpdateService.CheckForUpdatesAsync(cfg, isManual: false);
                }
            });
        }
    }

    private void SetupGlobalExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            LogLocalError("AppDomain", e.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (_, e) =>
        {
            LogLocalError("Dispatcher", e.Exception);
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogLocalError("TaskScheduler", e.Exception);
            e.SetObserved();
        };
    }

    private static void LogLocalError(string source, Exception? ex)
    {
        try
        {
            if (ex == null) return;
            string logPath = Path.Combine(AppContext.BaseDirectory, "error.log");
            string content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n\n";
            File.AppendAllText(logPath, content);
        }
        catch { }
    }

    private void InitTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Text = $"Date v{UpdateService.CurrentVersion.ToString(3)} - 任务栏日期与星期",
            Visible = true
        };

        LoadAppIconForTray();

        _trayIcon.MouseClick += (_, _) =>
        {
            if (_isExiting) return;
            _mainWindow?.Dispatcher.BeginInvoke(() =>
            {
                if (!_isExiting)
                {
                    _mainWindow.OpenMacContextMenu();
                }
            });
        };
    }

    private static void LoadAppIconForTray()
    {
        if (_trayIcon == null) return;
        try
        {
            // 优先支持用户目录自定义覆盖，否则直接读取单文件内嵌高质感图标
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                _trayIcon.Icon = new Icon(iconPath);
                return;
            }

            using var resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Date.Assets.app.ico");
            if (resStream != null)
            {
                _trayIcon.Icon = new Icon(resStream);
                return;
            }

            _trayIcon.Icon = SystemIcons.Application;
        }
        catch
        {
            _trayIcon.Icon = SystemIcons.Application;
        }
    }

    private static void CleanupResources()
    {
        ThemeService.Shutdown();

        if (_trayIcon != null)
        {
            var icon = _trayIcon;
            _trayIcon = null;
            try
            {
                icon.Visible = false;
                icon.Icon?.Dispose();
                icon.Dispose();
            }
            catch { }
        }

        if (_mutex != null)
        {
            var mutex = _mutex;
            _mutex = null;
            try
            {
                mutex.ReleaseMutex();
            }
            catch { }
            finally
            {
                mutex.Dispose();
            }
        }
    }

    public static void ExitApp()
    {
        _isExiting = true;
        _mainWindow?.PrepareClose();
        _mainWindow?.Close();
        CleanupResources();
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isExiting = true;
        CleanupResources();
        base.OnExit(e);
    }
}
