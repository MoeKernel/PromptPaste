using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using PromptPaste.Services;

namespace PromptPaste;

public partial class App : Application
{
    private TaskbarIcon? _tray;
    private MainWindow? _mainWindow;
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogService.Error("Unhandled UI exception", args.Exception);
            MessageBox.Show($"应用发生错误：{args.Exception.Message}", "PromptPaste", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogService.Error("Unhandled application exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogService.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        LogService.Info("Application starting");

        _mainWindow = new MainWindow();

        // Set window icon (taskbar)
        _mainWindow.Icon = IconHelper.CreateWindowIcon(32);

        // Tray icon — Material Design clipboard icon
        _tray = new TaskbarIcon
        {
            ToolTipText = "PromptPaste",
            Icon = IconHelper.CreateTrayIcon(),
        };

        // Tray menu
        var showItem = new MenuItem { Header = "显示/隐藏" };
        showItem.Click += (_, _) => ToggleWindow();

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => ShutdownApp();

        _tray.ContextMenu = new ContextMenu();
        _tray.ContextMenu.Items.Add(showItem);
        _tray.ContextMenu.Items.Add(exitItem);
        _tray.TrayMouseDoubleClick += (_, _) => ToggleWindow();

        // Close → hide to tray; real exit only via tray menu or File→退出
        _mainWindow.Closing += (_, args) =>
        {
            if (_isShuttingDown) return;
            if (!_mainWindow.CloseToTray)
            {
                _isShuttingDown = true;
                _tray?.Dispose();
                Shutdown();
                return;
            }

            args.Cancel = true;
            _mainWindow.Hide();
        };

        _mainWindow.Show();
        if (_mainWindow.StartMinimizedToTray)
            _mainWindow.Hide();
    }

    public void ShutdownApp()
    {
        LogService.Info("Application shutting down");
        _isShuttingDown = true;
        _mainWindow?.Close();
        _tray?.Dispose();
        Shutdown();
    }

    private void ToggleWindow()
    {
        if (_mainWindow == null) return;
        if (_mainWindow.IsVisible)
        {
            _mainWindow.Hide();
        }
        else
        {
            VirtualDesktopService.MoveWindowToCurrentDesktop(_mainWindow);
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
    }
}
