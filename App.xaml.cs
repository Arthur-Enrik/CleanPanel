using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.Services;
using HardwareOverlayApp.ViewModels;
using HardwareOverlayApp.Views;

namespace HardwareOverlayApp;

public partial class App : Application
{
    private static readonly object CrashLogLock = new();

    private SettingsService? _settingsService;
    private HardwareMonitorService? _hardwareMonitorService;
    private FpsMonitorService? _fpsMonitorService;
    private TrayIconService? _trayIconService;
    private MainViewModel? _mainViewModel;
    private OverlayWindow? _overlayWindow;
    private SettingsWindow? _settingsWindow;
    private bool _isShuttingDown;
    private bool _exceptionHandlersRegistered;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Garante que o diretório de trabalho seja sempre a pasta do executável,
        // mesmo ao iniciar pelo Agendador de Tarefas do Windows (schtasks no logon aponta para System32).
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

        // PASSO 1: Registra handlers globais de exceção ANTES de qualquer janela ou serviço
        RegisterGlobalExceptionHandlers();

        if (e.Args.Any(arg => arg.Equals("--generate-icon", StringComparison.OrdinalIgnoreCase)))
        {
            IconGeneratorHelper.EnsureIconFileExists();
            string repoAssetIcon = Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Assets", "app.ico"));
            IconGeneratorHelper.EnsureIconFileExists(repoAssetIcon, forceOverwrite: true);
            Shutdown(0);
            return;
        }

        // Garante que o executável só possa ser aberto como Administrador (exceto em verificação diagnóstica --no-elevate / --self-test)
        bool isSelfTest = e.Args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase));
        bool skipElevation = isSelfTest || e.Args.Any(arg => arg.Equals("--no-elevate", StringComparison.OrdinalIgnoreCase));
        if (!skipElevation && !IsRunningAsAdministrator())
        {
            RelaunchAsAdministrator(e.Args);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        try
        {
            IconGeneratorHelper.EnsureIconFileExists();

            _settingsService = new SettingsService();
            var startupService = new StartupService();
            _hardwareMonitorService = new HardwareMonitorService();
            _fpsMonitorService = new FpsMonitorService();

            _mainViewModel = new MainViewModel(
                _settingsService,
                _hardwareMonitorService,
                _fpsMonitorService,
                startupService,
                ExitApplicationCompletely);

            _overlayWindow = new OverlayWindow(_mainViewModel);
            _settingsWindow = new SettingsWindow(_mainViewModel);

            _trayIconService = new TrayIconService(
                _mainViewModel,
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

            if (isSelfTest)
            {
                StartAutomatedSelfTestTimer();
            }
        }
        catch (Exception ex)
        {
            LogCrash("App.OnStartup", ex);
            throw;
        }
    }

    private void StartAutomatedSelfTestTimer()
    {
        int elapsedSeconds = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        timer.Tick += (_, _) =>
        {
            try
            {
                elapsedSeconds++;
                if (_mainViewModel is not null)
                {
                    _mainViewModel.SelectedNavIndex = elapsedSeconds % 6;
                }

                Console.WriteLine($"[SELF-TEST] Tick {elapsedSeconds}/10s OK | NavTab={_mainViewModel?.SelectedNavIndex} | CPU={_mainViewModel?.CpuLoadText} | GPU={_mainViewModel?.GpuLoadText} | RAM={_mainViewModel?.RamPercentText} | FPS={_mainViewModel?.FpsText}");

                if (elapsedSeconds >= 10)
                {
                    timer.Stop();
                    Console.WriteLine("[SELF-TEST] PASSED 10s stability verification with 0 unhandled exceptions.");
                    ExitApplicationCompletely();
                }
            }
            catch (Exception ex)
            {
                LogCrash("App.SelfTestTimer", ex);
                timer.Stop();
                ExitApplicationCompletely();
            }
        };

        timer.Start();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        if (_exceptionHandlersRegistered)
            return;

        _exceptionHandlersRegistered = true;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("Application.DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    public static void LogCrash(string source, Exception? exception)
    {
        try
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Timestamp : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Source    : {source}");
            if (exception is not null)
            {
                sb.AppendLine($"Exception : {exception.GetType().FullName}");
                sb.AppendLine($"Message   : {exception.Message}");
                sb.AppendLine("StackTrace:");
                sb.AppendLine(exception.ToString());
            }
            else
            {
                sb.AppendLine("Exception : (null exception object)");
            }
            sb.AppendLine();

            lock (CrashLogLock)
            {
                File.AppendAllText(logPath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Ignora falha de escrita no log de diagnóstico
        }
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
                "O CleanPanel exige privilégios de Administrador para acessar os sensores físicos de hardware e o monitor de FPS.",
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

            DisposeAllResources();
            Shutdown();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeAllResources();
        base.OnExit(e);
    }

    private void DisposeAllResources()
    {
        _mainViewModel?.Dispose();
        _settingsService?.Dispose();
        _trayIconService?.Dispose();
        _fpsMonitorService?.Dispose();
        _hardwareMonitorService?.Dispose();
    }
}
