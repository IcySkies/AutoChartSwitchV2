using System.Windows;
using System.IO;
using AutoChartSwitch.Core;
using AutoChartSwitch.GameBridge;
using AutoChartSwitch.Obs;

namespace AutoChartSwitch.App;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private bool _isExiting;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private System.Drawing.Icon? _trayIcon;
    private MainViewModel? _viewModel;
    private TechStatsWindow? _techStatsWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        _instanceMutex = new Mutex(true, "SVC-AS.AutoChartSwitchV2", out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            MessageBox.Show("Auto Chart Switch V2 is already running.", "Auto Chart Switch V2", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var persistence = new AppPersistence();
        var settings = await persistence.LoadSettingsAsync();
        var fontCoverage = new SystemFontCoverage(ObsChartPublisher.DefaultTextFont);
        var publisher = new ObsChartPublisher(fontCoverage.Supports);
        var gameSource = new RelayGameEventSource(settings.GamePath);
        var coordinator = new LiveChartCoordinator(gameSource, publisher);
        _viewModel = new MainViewModel(gameSource, publisher, coordinator, persistence, settings);

        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        _techStatsWindow = new TechStatsWindow(_viewModel);
        _viewModel.ShowTechStatsRequested += (_, _) => ShowTechStatsWindow();
        ConfigureNotificationArea(window);
        window.Show();
        _techStatsWindow.Show();
        await _viewModel.InitializeAsync();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("Dispatcher exception", e.Exception);
        e.Handled = true;
        if (_viewModel is not null) _viewModel.StatusText = $"Controller error: {e.Exception.Message}";
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) LogException("Unhandled exception", exception);
    }

    private static void LogException(string context, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPersistence.RootPath);
            File.AppendAllText(Path.Combine(AppPersistence.RootPath, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {context}: {exception}\r\n");
        }
        catch { }
    }

    private void ConfigureNotificationArea(MainWindow window)
    {
        _trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
            ?? new System.Drawing.Icon(System.Drawing.SystemIcons.Application, System.Drawing.SystemIcons.Application.Size);
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RestoreMainWindow());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitApplicationAsync());
        _notifyIcon = new System.Windows.Forms.NotifyIcon { ContextMenuStrip = menu, Icon = _trayIcon, Text = "Auto Chart Switch V2", Visible = true };
        _notifyIcon.DoubleClick += (_, _) => RestoreMainWindow();
        window.Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting) return;
        e.Cancel = true;
        MainWindow?.Hide();
    }

    private void RestoreMainWindow()
    {
        if (MainWindow is null) return;
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    private void ShowTechStatsWindow()
    {
        if (_techStatsWindow is null) return;
        if (!_techStatsWindow.IsVisible) _techStatsWindow.Show();
        _techStatsWindow.Activate();
    }

    private async Task ExitApplicationAsync()
    {
        if (_isExiting) return;
        _isExiting = true;
        try
        {
            if (_viewModel is not null) await _viewModel.ShutdownAsync();
            _techStatsWindow?.AllowClose();
            _techStatsWindow?.Close();
        }
        finally { Shutdown(); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_notifyIcon is not null) { _notifyIcon.Visible = false; _notifyIcon.Dispose(); }
        _trayIcon?.Dispose();
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
