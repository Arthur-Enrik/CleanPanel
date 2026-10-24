using System.Collections.ObjectModel;
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

    // ==================== NAVEGAÇÃO LATERAL (TASK MANAGER STYLE) ====================
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsCpuTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsGpuTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsStorageTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsTabSelected))]
    private int _selectedNavIndex;

    public bool IsOverviewTabSelected
    {
        get => SelectedNavIndex == 0;
        set { if (value) SelectedNavIndex = 0; }
    }

    public bool IsCpuTabSelected
    {
        get => SelectedNavIndex == 1;
        set { if (value) SelectedNavIndex = 1; }
    }

    public bool IsGpuTabSelected
    {
        get => SelectedNavIndex == 2;
        set { if (value) SelectedNavIndex = 2; }
    }

    public bool IsStorageTabSelected
    {
        get => SelectedNavIndex == 3;
        set { if (value) SelectedNavIndex = 3; }
    }

    public bool IsSettingsTabSelected
    {
        get => SelectedNavIndex == 4;
        set { if (value) SelectedNavIndex = 4; }
    }

    // ==================== ESPECIFICAÇÕES ESTÁTICAS (CACHEADAS) ====================
    [ObservableProperty]
    private string _osName = "Microsoft Windows";

    [ObservableProperty]
    private string _osEdition = "N/D";

    [ObservableProperty]
    private string _osBuild = "N/D";

    [ObservableProperty]
    private string _cpuName = "Detectando processador...";

    [ObservableProperty]
    private int _cpuPhysicalCores = 1;

    [ObservableProperty]
    private int _cpuLogicalThreads = 1;

    [ObservableProperty]
    private string _cpuCoresThreadsSummary = "Detectando núcleos...";

    // ==================== TEXTOS FORMATADOS (OVERLAY + DASHBOARD) ====================
    [ObservableProperty]
    private string _fpsText = "0 FPS";

    [ObservableProperty]
    private string _cpuTemperatureText = "N/D";

    [ObservableProperty]
    private string _cpuLoadText = "0 %";

    [ObservableProperty]
    private double _cpuLoadBarValue;

    [ObservableProperty]
    private string _cpuClockText = "N/D";

    [ObservableProperty]
    private string _cpuMaxClockText = "N/D";

    [ObservableProperty]
    private string _cpuClockAvgMaxText = "N/D";

    [ObservableProperty]
    private string _cpuPowerText = "N/D";

    [ObservableProperty]
    private string _cpuVoltageText = "N/D";

    [ObservableProperty]
    private string _gpuTemperatureText = "N/D";

    [ObservableProperty]
    private string _gpuLoadText = "0 %";

    [ObservableProperty]
    private string _gpuClockText = "N/D";

    [ObservableProperty]
    private string _gpuPowerText = "N/D";

    [ObservableProperty]
    private string _ramUsageText = "0.0 GB (0%)";

    [ObservableProperty]
    private string _ramTotalText = "N/D";

    [ObservableProperty]
    private string _ramUsedText = "0.0 GB";

    [ObservableProperty]
    private string _ramFreeText = "N/D";

    [ObservableProperty]
    private string _ramPercentText = "0 %";

    [ObservableProperty]
    private double _ramUsageBarValue;

    [ObservableProperty]
    private string _ramDetailedSummaryText = "Calculando uso de memória física...";

    // ==================== COLEÇÕES DE TELEMETRIA AVANÇADA ====================
    public ObservableCollection<CpuCoreMetric> CpuCores { get; } = [];
    public ObservableCollection<GpuDetailedMetrics> Gpus { get; } = [];
    public ObservableCollection<StorageDriveMetrics> StorageDrives { get; } = [];
    public ObservableCollection<PhysicalStorageMetrics> PhysicalDisks { get; } = [];

    [ObservableProperty]
    private GpuDetailedMetrics? _selectedGpu;

    [ObservableProperty]
    private string _gpuCountBadgeText = "1 GPU Detectada";

    // ==================== CONTROLES DO OVERLAY & CONFIGURAÇÕES ====================
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

    // Visibilidade individual de métricas no Overlay
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

        _hardwareMonitorService.SpecsInitialized += OnHardwareSpecsInitialized;
        _hardwareMonitorService.HeartbeatTick += OnHeartbeatTick;
        _hardwareMonitorService.MetricsUpdated += OnHardwareMetricsUpdated;
    }

    public void SetMainWindowVisible(bool isVisible)
    {
        _hardwareMonitorService.IsMainWindowVisible = isVisible;
        UpdatePollingAndSmartVisibilityState();
    }

    private void OnHardwareSpecsInitialized(HardwareSpecs specs)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            OsName = specs.OsName;
            OsEdition = specs.OsEdition;
            OsBuild = specs.OsBuild;

            CpuName = specs.CpuName;
            CpuPhysicalCores = specs.CpuPhysicalCores;
            CpuLogicalThreads = specs.CpuLogicalThreads;
            CpuCoresThreadsSummary = specs.CpuCoresThreadsSummary;

            if (specs.TotalRamGb > 0f)
            {
                RamTotalText = string.Create(CultureInfo.InvariantCulture, $"{specs.TotalRamGb:0.0} GB");
            }

            if (Gpus.Count == 0 && specs.Gpus.Count > 0)
            {
                foreach (GpuSpecInfo gpuSpec in specs.Gpus)
                {
                    Gpus.Add(new GpuDetailedMetrics
                    {
                        Id = gpuSpec.Id,
                        Name = gpuSpec.Name,
                        Vendor = gpuSpec.Vendor,
                        TotalVramGb = gpuSpec.TotalVramGb,
                        IsIntegratedOrSharedVram = gpuSpec.IsIntegratedOrSharedVram,
                        TotalVramDisplay = gpuSpec.TotalVramDisplay
                    });
                }

                SelectedGpu ??= Gpus.FirstOrDefault();
                UpdateGpuCountBadge();
            }
        });
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
        bool shouldOverlayBeVisible = IsOverlayVisible && isFullscreen;
        bool isMainWindowOpen = _hardwareMonitorService.IsMainWindowVisible;

        _hardwareMonitorService.IsPollingSuspended = !shouldOverlayBeVisible && !isMainWindowOpen;
        _fpsMonitorService.IsSuspended = (!shouldOverlayBeVisible || !EffectiveShowFps) && !isMainWindowOpen;

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
            // FPS
            FpsText = $"{metrics.CurrentFps} FPS";

            // CPU (Com blindagem contra zeros falsos -> "N/D")
            CpuTemperatureText = metrics.CpuTemperature > 0f
                ? $"{metrics.CpuTemperature:0} °C"
                : "N/D";

            CpuLoadText = $"{metrics.CpuLoad:0} %";
            CpuLoadBarValue = Math.Clamp(metrics.CpuLoad, 0f, 100f);

            CpuClockText = metrics.CpuClock > 0f
                ? $"{metrics.CpuClock:0} MHz"
                : "N/D";

            CpuMaxClockText = metrics.CpuMaxClock > 0f
                ? $"{metrics.CpuMaxClock:0} MHz"
                : "N/D";

            CpuClockAvgMaxText = metrics.CpuClock > 0f
                ? (metrics.CpuMaxClock > 0f
                    ? $"{metrics.CpuClock:0} MHz (Máx: {metrics.CpuMaxClock:0} MHz)"
                    : $"{metrics.CpuClock:0} MHz")
                : "N/D";

            CpuPowerText = metrics.CpuPower > 0f
                ? string.Create(CultureInfo.InvariantCulture, $"{metrics.CpuPower:0.0} W")
                : "N/D";

            CpuVoltageText = metrics.CpuVoltageVcore is > 0f
                ? string.Create(CultureInfo.InvariantCulture, $"{metrics.CpuVoltageVcore.Value:0.000} V")
                : "N/D";

            // GPU Primária do Overlay (Com blindagem contra zeros falsos -> "N/D")
            GpuTemperatureText = metrics.GpuTemperature > 0f
                ? $"{metrics.GpuTemperature:0} °C"
                : "N/D";

            GpuLoadText = $"{metrics.GpuLoad:0} %";

            GpuClockText = metrics.GpuClock > 0f
                ? $"{metrics.GpuClock:0} MHz"
                : "N/D";

            GpuPowerText = metrics.GpuPower > 0f
                ? string.Create(CultureInfo.InvariantCulture, $"{metrics.GpuPower:0.0} W")
                : "N/D";

            // Memória RAM
            RamUsageText = string.Create(
                CultureInfo.InvariantCulture,
                $"{metrics.RamUsedGb:0.0} GB ({metrics.RamUsagePercent:0}%)");

            RamUsedText = string.Create(CultureInfo.InvariantCulture, $"{metrics.RamUsedGb:0.0} GB");
            RamPercentText = $"{metrics.RamUsagePercent:0} %";
            RamUsageBarValue = Math.Clamp(metrics.RamUsagePercent, 0f, 100f);

            if (metrics.RamTotalGb > 0f)
            {
                RamTotalText = string.Create(CultureInfo.InvariantCulture, $"{metrics.RamTotalGb:0.0} GB");
                RamFreeText = string.Create(CultureInfo.InvariantCulture, $"{metrics.RamFreeGb:0.0} GB");
                RamDetailedSummaryText = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{metrics.RamUsedGb:0.0} GB usados de {metrics.RamTotalGb:0.0} GB ({metrics.RamFreeGb:0.0} GB livres)");
            }

            // Telemetria detalhada (só atualiza as coleções da UI quando a janela principal está aberta)
            if (metrics.IsDetailedSnapshot)
            {
                SyncCpuCoresCollection(metrics.CpuCores);
                SyncGpusCollection(metrics.Gpus);
                SyncStorageCollections(metrics.StorageDrives, metrics.PhysicalDisks);
            }
        });
    }

    private void SyncCpuCoresCollection(List<CpuCoreMetric> incoming)
    {
        if (CpuCores.Count == incoming.Count)
        {
            for (int i = 0; i < incoming.Count; i++)
            {
                CpuCores[i].UpdateFrom(incoming[i]);
            }
            return;
        }

        CpuCores.Clear();
        foreach (CpuCoreMetric item in incoming)
        {
            CpuCores.Add(item);
        }
    }

    private void SyncGpusCollection(List<GpuDetailedMetrics> incoming)
    {
        if (incoming.Count == 0)
            return;

        if (Gpus.Count == incoming.Count)
        {
            for (int i = 0; i < incoming.Count; i++)
            {
                Gpus[i].UpdateFrom(incoming[i]);
            }

            SelectedGpu ??= Gpus.FirstOrDefault();
            return;
        }

        string? previousSelectedId = SelectedGpu?.Id;
        Gpus.Clear();
        foreach (GpuDetailedMetrics gpu in incoming)
        {
            Gpus.Add(gpu);
        }

        SelectedGpu = Gpus.FirstOrDefault(g => g.Id == previousSelectedId) ?? Gpus.FirstOrDefault();
        UpdateGpuCountBadge();
    }

    private void UpdateGpuCountBadge()
    {
        GpuCountBadgeText = Gpus.Count == 1
            ? "1 GPU Detectada"
            : $"{Gpus.Count} GPUs Detectadas";
    }

    private void SyncStorageCollections(List<StorageDriveMetrics> drives, List<PhysicalStorageMetrics> physicalDisks)
    {
        if (StorageDrives.Count == drives.Count)
        {
            for (int i = 0; i < drives.Count; i++)
            {
                StorageDrives[i].UpdateFrom(drives[i]);
            }
        }
        else
        {
            StorageDrives.Clear();
            foreach (StorageDriveMetrics d in drives)
            {
                StorageDrives.Add(d);
            }
        }

        if (PhysicalDisks.Count == physicalDisks.Count)
        {
            for (int i = 0; i < physicalDisks.Count; i++)
            {
                PhysicalDisks[i].UpdateFrom(physicalDisks[i]);
            }
        }
        else
        {
            PhysicalDisks.Clear();
            foreach (PhysicalStorageMetrics p in physicalDisks)
            {
                PhysicalDisks.Add(p);
            }
        }
    }

    [RelayCommand]
    private void SelectTab(string? indexParameter)
    {
        if (int.TryParse(indexParameter, out int idx) && idx is >= 0 and <= 4)
        {
            SelectedNavIndex = idx;
        }
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
