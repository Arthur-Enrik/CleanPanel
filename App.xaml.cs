using System.Windows;
using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.Services;
using HardwareOverlayApp.ViewModels;
using HardwareOverlayApp.Views;

namespace HardwareOverlayApp;

public partial class App : Application
{
    private HardwareMonitorService? _hardwareMonitorService;
    private FpsMonitorService? _fpsMonitorService;
    private TrayIconService? _trayIconService;
    private OverlayWindow? _overlayWindow;
    private SettingsWindow? _settingsWindow;
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IconGeneratorHelper.EnsureIconFileExists();

        var settingsService = new SettingsService();
        var startupService = new StartupService();
        _hardwareMonitorService = new HardwareMonitorService();
        _fpsMonitorService = new FpsMonitorService();

        var mainViewModel = new MainViewModel(
            settingsService,
            _hardwareMonitorService,
            _fpsMonitorService,
            startupService,
            ExitApplicationCompletely);

        _overlayWindow = new OverlayWindow(mainViewModel);
        _settingsWindow = new SettingsWindow(mainViewModel);

        _trayIconService = new TrayIconService(
            mainViewModel,
            OpenSettingsWindow,
            ExitApplicationCompletely);

        _overlayWindow.Show();

        bool startMinimized = e.Args.Any(arg =>
            arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        if (!startMinimized)
        {
            _settingsWindow.Show();
        }

        _fpsMonitorService.Start();
        _hardwareMonitorService.Start();
    }

    private void OpenSettingsWindow()
    {
        Dispatcher.Invoke(() =>
        {
            _settingsWindow?.ShowAndActivate();
        });
    }

    private void ExitApplicationCompletely()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;

        Dispatcher.Invoke(() =>
        {
            _settingsWindow?.PrepareForExit();
            _settingsWindow?.Close();
            _overlayWindow?.Close();

            _trayIconService?.Dispose();
            _fpsMonitorService?.Dispose();
            _hardwareMonitorService?.Dispose();

            Shutdown();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        _fpsMonitorService?.Dispose();
        _hardwareMonitorService?.Dispose();
        base.OnExit(e);
    }
}
