using System.Diagnostics;
using System.Security.Principal;
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
        // Garante que o executável só possa ser aberto como Administrador
        if (!IsRunningAsAdministrator())
        {
            RelaunchAsAdministrator(e.Args);
            Shutdown();
            return;
        }

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

    private static bool IsRunningAsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchAsAdministrator(string[] args)
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                return;

            string arguments = string.Join(" ", args.Select(a => $"\"{a}\""));

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            Process.Start(startInfo);
        }
        catch
        {
            MessageBox.Show(
                "O Hardware Overlay Monitor exige privilégios de Administrador para acessar os sensores físicos de hardware e o monitor de FPS.",
                "Execução como Administrador Obrigatória",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
