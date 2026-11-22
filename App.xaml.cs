using System;
using System.Diagnostics;
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

        // 5. 创建并呈现任务栏日期悬浮胶囊
        _mainWindow = new MainWindow(_config);
        _mainWindow.Show();

        // 6. 初始化系统托盘图标
        InitTrayIcon();

        // 7. 启动时后台异步检查更新（静默不阻塞主线程）
        if (_config.CheckUpdatesOnStartup)
        {
            bool shouldCheck = !_config.LastUpdateCheck.HasValue ||
                               (DateTime.Now - _config.LastUpdateCheck.Value).TotalHours >= 24;
            if (shouldCheck)
            {
                Task.Run(() => UpdateService.CheckForUpdatesAsync(_config, isManual: false));
            }
        }
    }

    private void SetupGlobalExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogLocalError("AppDomain", e.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, e) =>
        {
            LogLocalError("Dispatcher", e.Exception);
            e.Handled = true; // 拦截异常，防止 WPF 崩溃退出
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
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
            Text = "Date - 任务栏日期与星期",
            Visible = true
        };

        // 托盘图标加载纯净透明的高质感日历图标
        LoadAppIconForTray();

        _trayIcon.MouseClick += (s, e) =>
        {
            // 无论是左键还是右键点击托盘，均唤起统一的 macOS 26 毛玻璃风格菜单
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.OpenMacContextMenu());
        };
    }

    private static void LoadAppIconForTray()
    {
        if (_trayIcon == null) return;
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                _trayIcon.Icon = new Icon(iconPath);
                return;
            }

            var resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Date.Assets.app.ico")
                         ?? Assembly.GetExecutingAssembly().GetManifestResourceStream("Date.Assets.tray.ico");
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

    public static void ExitApp()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
        }
        try { _mutex?.ReleaseMutex(); } catch { }
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
        }
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
