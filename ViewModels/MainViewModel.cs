using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareOverlayApp.Models;
using HardwareOverlayApp.Services;

namespace HardwareOverlayApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly HardwareMonitorService _hardwareMonitorService;
    private readonly FpsMonitorService _fpsMonitorService;
    private readonly StartupService _startupService;
    private readonly Action _exitApplicationAction;
    private readonly bool _isInitializing;

    // Textos formatados para o Overlay
    [ObservableProperty]
    private string _fpsText = "0 FPS";

    [ObservableProperty]
    private string _cpuTemperatureText = "0 °C";

    [ObservableProperty]
    private string _cpuLoadText = "0 %";

    [ObservableProperty]
    private string _cpuClockText = "0 MHz";

    [ObservableProperty]
    private string _cpuPowerText = "0.0 W";

    [ObservableProperty]
    private string _gpuTemperatureText = "0 °C";

    [ObservableProperty]
    private string _gpuLoadText = "0 %";

    [ObservableProperty]
    private string _gpuClockText = "0 MHz";

    [ObservableProperty]
    private string _gpuPowerText = "0.0 W";

    [ObservableProperty]
    private string _ramUsageText = "0.0 GB (0%)";

    // Controles gerais
    [ObservableProperty]
    private bool _isOverlayVisible;

    [ObservableProperty]
    private bool _isOverlayLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayOpacityPercentText))]
    private double _overlayOpacity;

    [ObservableProperty]
    private double _overlayLeft;

    [ObservableProperty]
    private double _overlayTop;

    [ObservableProperty]
    private bool _startWithWindows;

    // Visibilidade individual de métricas
    [ObservableProperty]
    private bool _showFps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    private bool _showCpuTemp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    private bool _showCpuLoad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    private bool _showCpuClock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    private bool _showCpuPower;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    private bool _showGpuTemp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    private bool _showGpuLoad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    private bool _showGpuClock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    private bool _showGpuPower;

    [ObservableProperty]
    private bool _showRam;

    public string OverlayOpacityPercentText => $"{OverlayOpacity * 100.0:0}%";

    public bool ShowCpuSection => ShowCpuTemp || ShowCpuLoad || ShowCpuClock || ShowCpuPower;

    public bool ShowGpuSection => ShowGpuTemp || ShowGpuLoad || ShowGpuClock || ShowGpuPower;

    public MainViewModel(
        SettingsService settingsService,
        HardwareMonitorService hardwareMonitorService,
        FpsMonitorService fpsMonitorService,
        StartupService startupService,
        Action exitApplicationAction)
    {
        _settingsService = settingsService;
        _hardwareMonitorService = hardwareMonitorService;
        _fpsMonitorService = fpsMonitorService;
        _startupService = startupService;
        _exitApplicationAction = exitApplicationAction;

        _isInitializing = true;
        AppSettings settings = _settingsService.Load();
        ApplySettings(settings);
        _isInitializing = false;

        _hardwareMonitorService.MetricsUpdated += OnHardwareMetricsUpdated;
    }

    private void ApplySettings(AppSettings settings)
    {
        IsOverlayVisible = settings.IsOverlayVisible;
        IsOverlayLocked = settings.IsOverlayLocked;
        OverlayOpacity = Math.Clamp(settings.OverlayOpacity, 0.1, 1.0);
        OverlayLeft = settings.OverlayLeft;
        OverlayTop = settings.OverlayTop;
        StartWithWindows = settings.StartWithWindows;

        ShowFps = settings.ShowFps;

        ShowCpuTemp = settings.ShowCpuTemp;
        ShowCpuLoad = settings.ShowCpuLoad;
        ShowCpuClock = settings.ShowCpuClock;
        ShowCpuPower = settings.ShowCpuPower;

        ShowGpuTemp = settings.ShowGpuTemp;
        ShowGpuLoad = settings.ShowGpuLoad;
        ShowGpuClock = settings.ShowGpuClock;
        ShowGpuPower = settings.ShowGpuPower;

        ShowRam = settings.ShowRam;
    }

    private void OnHardwareMetricsUpdated(HardwareMetrics metrics)
    {
        int currentFps = _fpsMonitorService.CurrentFps;
        metrics.CurrentFps = currentFps;

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            FpsText = $"{metrics.CurrentFps} FPS";

            CpuTemperatureText = $"{metrics.CpuTemperature:0} °C";
            CpuLoadText = $"{metrics.CpuLoad:0} %";
            CpuClockText = $"{metrics.CpuClock:0} MHz";
            CpuPowerText = string.Create(CultureInfo.InvariantCulture, $"{metrics.CpuPower:0.0} W");

            GpuTemperatureText = $"{metrics.GpuTemperature:0} °C";
            GpuLoadText = $"{metrics.GpuLoad:0} %";
            GpuClockText = $"{metrics.GpuClock:0} MHz";
            GpuPowerText = string.Create(CultureInfo.InvariantCulture, $"{metrics.GpuPower:0.0} W");

            RamUsageText = string.Create(
                CultureInfo.InvariantCulture,
                $"{metrics.RamUsedGb:0.0} GB ({metrics.RamUsagePercent:0}%)");
        });
    }

    [RelayCommand]
    private void ResetPosition()
    {
        SetOverlayPosition(50.0, 50.0);
    }

    [RelayCommand]
    private void ExitApplication()
    {
        _exitApplicationAction();
    }

    public void SetOverlayPosition(double left, double top)
    {
        OverlayLeft = Math.Round(left);
        OverlayTop = Math.Round(top);
        SaveSettings();
    }

    private void SaveSettings()
    {
        if (_isInitializing)
            return;

        var settings = new AppSettings
        {
            IsOverlayVisible = IsOverlayVisible,
            IsOverlayLocked = IsOverlayLocked,
            OverlayOpacity = Math.Clamp(Math.Round(OverlayOpacity, 2), 0.1, 1.0),
            OverlayLeft = OverlayLeft,
            OverlayTop = OverlayTop,
            StartWithWindows = StartWithWindows,
            ShowFps = ShowFps,
            ShowCpuTemp = ShowCpuTemp,
            ShowCpuLoad = ShowCpuLoad,
            ShowCpuClock = ShowCpuClock,
            ShowCpuPower = ShowCpuPower,
            ShowGpuTemp = ShowGpuTemp,
            ShowGpuLoad = ShowGpuLoad,
            ShowGpuClock = ShowGpuClock,
            ShowGpuPower = ShowGpuPower,
            ShowRam = ShowRam
        };

        _settingsService.Save(settings);
    }

    partial void OnIsOverlayVisibleChanged(bool value) => SaveSettings();
    partial void OnIsOverlayLockedChanged(bool value) => SaveSettings();
    partial void OnOverlayOpacityChanged(double value) => SaveSettings();

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_isInitializing)
        {
            _startupService.SetStartup(value);
        }
        SaveSettings();
    }

    partial void OnShowFpsChanged(bool value) => SaveSettings();
    partial void OnShowCpuTempChanged(bool value) => SaveSettings();
    partial void OnShowCpuLoadChanged(bool value) => SaveSettings();
    partial void OnShowCpuClockChanged(bool value) => SaveSettings();
    partial void OnShowCpuPowerChanged(bool value) => SaveSettings();
    partial void OnShowGpuTempChanged(bool value) => SaveSettings();
    partial void OnShowGpuLoadChanged(bool value) => SaveSettings();
    partial void OnShowGpuClockChanged(bool value) => SaveSettings();
    partial void OnShowGpuPowerChanged(bool value) => SaveSettings();
    partial void OnShowRamChanged(bool value) => SaveSettings();
}
