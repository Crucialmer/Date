using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
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
    private static ContextMenuStrip? _trayMenu;
    private static MainWindow? _mainWindow;
    private static AppConfig? _config;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. 单实例检查（纯绿色软件，避免多开重叠）
        const string mutexName = "Date_App_SingleInstance_Mutex_Crucialmer";
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Date 已经在运行中！可在屏幕右下角系统托盘找到它。", "提示 - Date", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 2. 加载本地便携配置
        _config = AppConfig.Load();

        // 3. 初始化深浅色主题监听服务
        ThemeService.Initialize(_config.ThemeMode);

        // 4. 创建并呈现任务栏日期悬浮窗
        _mainWindow = new MainWindow(_config);
        _mainWindow.Show();

        // 5. 初始化系统托盘图标
        InitTrayIcon();

        // 6. 启动时自动检查更新（如果开启）
        if (_config.CheckUpdatesOnStartup)
        {
            bool shouldCheck = !_config.LastUpdateCheck.HasValue ||
                               (DateTime.Now - _config.LastUpdateCheck.Value).TotalHours >= 24;
            if (shouldCheck)
            {
                _ = UpdateService.CheckForUpdatesAsync(_config, isManual: false);
            }
        }
    }

    private void InitTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();
        BuildTrayMenu();

        _trayIcon = new NotifyIcon
        {
            Text = "Date - 任务栏日期与星期",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };

        // 加载托盘图标（支持外部文件或内嵌资源，纯绿色便携）
        string trayIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico");
        if (File.Exists(trayIconPath))
        {
            _trayIcon.Icon = new Icon(trayIconPath);
        }
        else
        {
            var resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Date.Assets.tray.ico");
            if (resStream != null)
            {
                _trayIcon.Icon = new Icon(resStream);
            }
            else
            {
                _trayIcon.Icon = SystemIcons.Application;
            }
        }

        _trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                // 左键单击也可以唤起菜单
                ShowContextMenu();
            }
        };
    }

    private static void BuildTrayMenu()
    {
        if (_trayMenu == null || _config == null) return;
        _trayMenu.Items.Clear();

        // 标题项
        var titleItem = new ToolStripMenuItem($"Date (v{UpdateService.CurrentVersion.ToString(3)})")
        {
            Enabled = false
        };
        _trayMenu.Items.Add(titleItem);
        _trayMenu.Items.Add(new ToolStripSeparator());

        // 主题菜单
        var themeMenu = new ToolStripMenuItem("外观主题");
        var autoItem = new ToolStripMenuItem("跟随系统 (Auto)", null, (s, e) => SetTheme("Auto"))
        {
            Checked = _config.ThemeMode.Equals("Auto", StringComparison.OrdinalIgnoreCase)
        };
        var darkItem = new ToolStripMenuItem("深色模式 (Dark)", null, (s, e) => SetTheme("Dark"))
        {
            Checked = _config.ThemeMode.Equals("Dark", StringComparison.OrdinalIgnoreCase)
        };
        var lightItem = new ToolStripMenuItem("浅色模式 (Light)", null, (s, e) => SetTheme("Light"))
        {
            Checked = _config.ThemeMode.Equals("Light", StringComparison.OrdinalIgnoreCase)
        };
        themeMenu.DropDownItems.AddRange(new ToolStripItem[] { autoItem, darkItem, lightItem });
        _trayMenu.Items.Add(themeMenu);

        // 检查更新
        var updateItem = new ToolStripMenuItem("检查更新...", null, async (s, e) =>
        {
            if (_config != null)
            {
                await UpdateService.CheckForUpdatesAsync(_config, isManual: true);
            }
        });
        _trayMenu.Items.Add(updateItem);

        // 打开程序目录
        var openFolderItem = new ToolStripMenuItem("打开程序目录", null, (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppContext.BaseDirectory,
                    UseShellExecute = true
                });
            }
            catch { }
        });
        _trayMenu.Items.Add(openFolderItem);

        _trayMenu.Items.Add(new ToolStripSeparator());

        // 退出
        var exitItem = new ToolStripMenuItem("退出", null, (s, e) =>
        {
            ExitApp();
        });
        _trayMenu.Items.Add(exitItem);
    }

    private static void SetTheme(string mode)
    {
        if (_config == null) return;
        _config.ThemeMode = mode;
        _config.Save();
        ThemeService.SetMode(mode);
        BuildTrayMenu();
    }

    public static void ShowContextMenu()
    {
        if (_trayMenu != null)
        {
            BuildTrayMenu();
            var pt = Control.MousePosition;
            _trayMenu.Show(pt.X, pt.Y);
        }
    }

    private static void ExitApp()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _mutex?.ReleaseMutex();
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _mutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
