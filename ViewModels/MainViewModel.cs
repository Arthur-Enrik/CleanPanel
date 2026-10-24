using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareOverlayApp.Helpers;
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

    public Func<(double Width, double Height)>? OverlaySizeProvider { get; set; }

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
    [NotifyPropertyChangedFor(nameof(EffectiveOverlayVisible))]
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

    // Novas funcionalidades: Modo Compacto, Exibição Inteligente e Orientação do Layout
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayCardPadding))]
    [NotifyPropertyChangedFor(nameof(ShowSectionHeaders))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowFps))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuTemp))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuLoad))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuClock))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuPower))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuTemp))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuLoad))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuClock))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuPower))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowRam))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    private bool _isCompactMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveOverlayVisible))]
    private bool _smartFullscreenOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVerticalLayout))]
    private bool _isHorizontalLayout;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveOverlayVisible))]
    private bool _isForegroundFullScreen = true;

    // Visibilidade individual de métricas
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowFps))]
    private bool _showFps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuTemp))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    private bool _showCpuTemp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuLoad))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    private bool _showCpuLoad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuClock))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    private bool _showCpuClock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuPower))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    private bool _showCpuPower;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuTemp))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    private bool _showGpuTemp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuLoad))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    private bool _showGpuLoad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuClock))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    private bool _showGpuClock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuPower))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    private bool _showGpuPower;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowRam))]
    private bool _showRam;

    public string OverlayOpacityPercentText => $"{OverlayOpacity * 100.0:0}%";

    public bool EffectiveOverlayVisible => IsOverlayVisible && (!SmartFullscreenOnly || IsForegroundFullScreen);

    public bool IsVerticalLayout => !IsHorizontalLayout;

    public Thickness OverlayCardPadding => IsCompactMode ? new Thickness(10, 5, 10, 5) : new Thickness(14, 10, 14, 10);

    public bool ShowSectionHeaders => !IsCompactMode;

    // No Modo Compacto, exibe exclusivamente FPS, CPU % e GPU %
    public bool EffectiveShowFps => IsCompactMode || ShowFps;
    public bool EffectiveShowCpuTemp => !IsCompactMode && ShowCpuTemp;
    public bool EffectiveShowCpuLoad => IsCompactMode || ShowCpuLoad;
    public bool EffectiveShowCpuClock => !IsCompactMode && ShowCpuClock;
    public bool EffectiveShowCpuPower => !IsCompactMode && ShowCpuPower;
    public bool EffectiveShowGpuTemp => !IsCompactMode && ShowGpuTemp;
    public bool EffectiveShowGpuLoad => IsCompactMode || ShowGpuLoad;
    public bool EffectiveShowGpuClock => !IsCompactMode && ShowGpuClock;
    public bool EffectiveShowGpuPower => !IsCompactMode && ShowGpuPower;
    public bool EffectiveShowRam => !IsCompactMode && ShowRam;

    public bool ShowCpuSection => EffectiveShowCpuTemp || EffectiveShowCpuLoad || EffectiveShowCpuClock || EffectiveShowCpuPower;

    public bool ShowGpuSection => EffectiveShowGpuTemp || EffectiveShowGpuLoad || EffectiveShowGpuClock || EffectiveShowGpuPower;

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

        UpdatePollingAndSmartVisibilityState();

        _hardwareMonitorService.HeartbeatTick += OnHeartbeatTick;
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

        IsCompactMode = settings.IsCompactMode;
        SmartFullscreenOnly = settings.SmartFullscreenOnly;
        IsHorizontalLayout = settings.IsHorizontalLayout;

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

    private void OnHeartbeatTick()
    {
        UpdatePollingAndSmartVisibilityState();
    }

    private void UpdatePollingAndSmartVisibilityState()
    {
        bool isFullscreen = !SmartFullscreenOnly || WindowWin32Helper.IsForegroundWindowFullScreen();
        bool shouldBeVisible = IsOverlayVisible && isFullscreen;

        _hardwareMonitorService.IsPollingSuspended = !shouldBeVisible;
        _fpsMonitorService.IsSuspended = !shouldBeVisible || !EffectiveShowFps;

        if (IsForegroundFullScreen != isFullscreen)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                IsForegroundFullScreen = isFullscreen;
            });
        }
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
    private void AlignOverlay(string? alignment)
    {
        if (string.IsNullOrWhiteSpace(alignment))
            return;

        Rect workArea = SystemParameters.WorkArea;
        (double overlayWidth, double overlayHeight) = OverlaySizeProvider?.Invoke() ?? (240.0, 160.0);

        if (overlayWidth <= 10)
            overlayWidth = IsHorizontalLayout ? 420.0 : 240.0;
        if (overlayHeight <= 10)
            overlayHeight = IsHorizontalLayout ? 56.0 : (IsCompactMode ? 90.0 : 220.0);

        const double screenMargin = 16.0;

        double targetLeft = OverlayLeft;
        double targetTop = OverlayTop;

        switch (alignment)
        {
            case "TopLeft":
                targetLeft = workArea.Left + screenMargin;
                targetTop = workArea.Top + screenMargin;
                break;

            case "TopCenter":
                targetLeft = workArea.Left + ((workArea.Width - overlayWidth) / 2.0);
                targetTop = workArea.Top + screenMargin;
                break;

            case "TopRight":
                targetLeft = workArea.Right - overlayWidth - screenMargin;
                targetTop = workArea.Top + screenMargin;
                break;

            case "BottomLeft":
                targetLeft = workArea.Left + screenMargin;
                targetTop = workArea.Bottom - overlayHeight - screenMargin;
                break;

            case "BottomCenter":
                targetLeft = workArea.Left + ((workArea.Width - overlayWidth) / 2.0);
                targetTop = workArea.Bottom - overlayHeight - screenMargin;
                break;

            case "BottomRight":
                targetLeft = workArea.Right - overlayWidth - screenMargin;
                targetTop = workArea.Bottom - overlayHeight - screenMargin;
                break;
        }

        SetOverlayPosition(Math.Max(workArea.Left, targetLeft), Math.Max(workArea.Top, targetTop));
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
            IsCompactMode = IsCompactMode,
            SmartFullscreenOnly = SmartFullscreenOnly,
            IsHorizontalLayout = IsHorizontalLayout,
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

    partial void OnIsOverlayVisibleChanged(bool value)
    {
        UpdatePollingAndSmartVisibilityState();
        SaveSettings();
    }

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

    partial void OnIsCompactModeChanged(bool value)
    {
        UpdatePollingAndSmartVisibilityState();
        SaveSettings();
    }

    partial void OnSmartFullscreenOnlyChanged(bool value)
    {
        UpdatePollingAndSmartVisibilityState();
        SaveSettings();
    }

    partial void OnIsHorizontalLayoutChanged(bool value) => SaveSettings();

    partial void OnShowFpsChanged(bool value)
    {
        UpdatePollingAndSmartVisibilityState();
        SaveSettings();
    }

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
