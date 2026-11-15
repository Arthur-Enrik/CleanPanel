using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.Models;
using HardwareOverlayApp.Services;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace HardwareOverlayApp.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public const string DefaultAccentColorHex = "#FAFAFA";
    public const string DefaultBackgroundColorHex = "#09090B";
    public const string DefaultBorderColorHex = "#2E2E32";
    public const string DefaultHeaderColorHex = "#FAFAFA";
    public const string DefaultValueTextColorHex = "#FAFAFA";

    // Pincéis pré-alocados e congelados para alertas térmicos (zero alocação por tick)
    private static readonly SolidColorBrush ThermalNormalBrush = CreateFrozenBrush("#FFFFFF", Colors.White);
    private static readonly SolidColorBrush ThermalWarningBrush = CreateFrozenBrush("#FACC15", Color.FromRgb(250, 204, 21));
    private static readonly SolidColorBrush ThermalCriticalBrush = CreateFrozenBrush("#EF4444", Color.FromRgb(239, 68, 68));

    private int _lastCpuTemp;
    private int _lastGpuTemp;

    private static readonly string[] DefaultSectionKeys = ["CPU", "GPU", "RAM", "FPS"];

    private readonly SettingsService _settingsService;
    private readonly HardwareMonitorService _hardwareMonitorService;
    private readonly FpsMonitorService _fpsMonitorService;
    private readonly StartupService _startupService;
    private readonly Action _exitApplicationAction;
    private readonly bool _isInitializing;
    private bool _isDisposed;

    public Func<(double Width, double Height)>? OverlaySizeProvider { get; set; }

    // ==================== NAVEGAÇÃO LATERAL (TASK MANAGER STYLE) ====================
    // 0 = Visão Geral, 1 = CPU, 2 = GPU, 3 = Sistema & Discos, 4 = Personalização, 5 = Configurações
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsCpuTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsGpuTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsStorageTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsPersonalizationTabSelected))]
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

    public bool IsPersonalizationTabSelected
    {
        get => SelectedNavIndex == 4;
        set { if (value) SelectedNavIndex = 4; }
    }

    public bool IsSettingsTabSelected
    {
        get => SelectedNavIndex == 5;
        set { if (value) SelectedNavIndex = 5; }
    }

    // ==================== TEMA DO APLICATIVO (DARK, LIGHT, AUTO) ====================
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThemeDarkSelected))]
    [NotifyPropertyChangedFor(nameof(IsThemeLightSelected))]
    [NotifyPropertyChangedFor(nameof(IsThemeAutoSelected))]
    private string _appTheme = "Dark";

    public bool IsThemeDarkSelected
    {
        get => string.Equals(AppTheme, "Dark", StringComparison.OrdinalIgnoreCase);
        set { if (value) AppTheme = "Dark"; }
    }

    public bool IsThemeLightSelected
    {
        get => string.Equals(AppTheme, "Light", StringComparison.OrdinalIgnoreCase);
        set { if (value) AppTheme = "Light"; }
    }

    public bool IsThemeAutoSelected
    {
        get => string.Equals(AppTheme, "Auto", StringComparison.OrdinalIgnoreCase);
        set { if (value) AppTheme = "Auto"; }
    }

    // ==================== CORES MONOCROMÁTICAS, ACCENT, ALERTAS TÉRMICOS E ATALHOS ====================
    [ObservableProperty]
    private string _accentColorHex = DefaultAccentColorHex;

    [ObservableProperty]
    private string _overlayBackgroundColorHex = DefaultBackgroundColorHex;

    [ObservableProperty]
    private string _overlayBorderColorHex = DefaultBorderColorHex;

    [ObservableProperty]
    private string _overlayHeaderColorHex = DefaultHeaderColorHex;

    [ObservableProperty]
    private string _overlayValueTextColorHex = DefaultValueTextColorHex;

    [ObservableProperty]
    private SolidColorBrush _overlayBackgroundBrush = CreateFrozenBrush("#D909090B", Color.FromArgb(217, 9, 9, 11));

    [ObservableProperty]
    private SolidColorBrush _overlayBorderBrush = CreateFrozenBrush(DefaultBorderColorHex, Color.FromRgb(46, 46, 50));

    [ObservableProperty]
    private SolidColorBrush _overlayHeaderBrush = CreateFrozenBrush(DefaultHeaderColorHex, Color.FromRgb(250, 250, 250));

    [ObservableProperty]
    private SolidColorBrush _overlayValueTextBrush = CreateFrozenBrush(DefaultValueTextColorHex, Color.FromRgb(250, 250, 250));

    [ObservableProperty]
    private bool _enableThermalColorAlerts = true;

    [ObservableProperty]
    private double _tempWarningThreshold = 70.0;

    [ObservableProperty]
    private double _tempCriticalThreshold = 85.0;

    [ObservableProperty]
    private SolidColorBrush _cpuTempBrush = ThermalNormalBrush;

    [ObservableProperty]
    private SolidColorBrush _gpuTempBrush = ThermalNormalBrush;

    [ObservableProperty]
    private uint _toggleOverlayHotkeyModifiers = WindowWin32Helper.MOD_CONTROL | WindowWin32Helper.MOD_SHIFT;

    [ObservableProperty]
    private uint _toggleOverlayHotkeyKey = WindowWin32Helper.VK_O;

    [ObservableProperty]
    private string _toggleOverlayHotkeyDisplay = "Ctrl + Shift + O";

    [ObservableProperty]
    private uint _toggleCompactModeHotkeyModifiers = WindowWin32Helper.MOD_CONTROL | WindowWin32Helper.MOD_SHIFT;

    [ObservableProperty]
    private uint _toggleCompactModeHotkeyKey = WindowWin32Helper.VK_C;

    [ObservableProperty]
    private string _toggleCompactModeHotkeyDisplay = "Ctrl + Shift + C";

    [ObservableProperty]
    private string _hotkeyStatusMessage = string.Empty;

    // ==================== ORDEM PERSONALIZÁVEL DAS SEÇÕES DO OVERLAY ====================
    public ObservableCollection<OverlaySectionItemViewModel> OverlaySections { get; } = [];

    // ==================== ESPECIFICAÇÕES ESTÁTICAS E NOMES CURTOS (CACHEADOS 1X) ====================
    [ObservableProperty]
    private string _osName = "Microsoft Windows";

    [ObservableProperty]
    private string _osEdition = "N/D";

    [ObservableProperty]
    private string _osBuild = "N/D";

    [ObservableProperty]
    private string _cpuName = "Detectando processador...";

    [ObservableProperty]
    private string _cpuShortName = "CPU";

    [ObservableProperty]
    private string _gpuShortName = "GPU";

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
    private string _fpsValueText = "0";

    [ObservableProperty]
    private string _cpuTemperatureText = "N/D";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveCompactCpuLoadText))]
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

    // Métricas dedicadas para CPU Híbrida Intel (P-Cores e E-Cores)
    [ObservableProperty]
    private float _cpuPCoreLoad;

    [ObservableProperty]
    private float _cpuECoreLoad;

    [ObservableProperty]
    private float _cpuPCoreClock;

    [ObservableProperty]
    private float _cpuECoreClock;

    [ObservableProperty]
    private string _cpuPCoreLoadText = "N/D";

    [ObservableProperty]
    private string _cpuECoreLoadText = "N/D";

    [ObservableProperty]
    private string _cpuPCoreClockText = "N/D";

    [ObservableProperty]
    private string _cpuECoreClockText = "N/D";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveCompactCpuLoadText))]
    private string _cpuHybridCompactLoadText = "N/D";

    [ObservableProperty]
    private string _gpuTemperatureText = "N/D";

    [ObservableProperty]
    private string _gpuLoadText = "0 %";

    [ObservableProperty]
    private string _gpuVramText = "N/D";

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
    [NotifyPropertyChangedFor(nameof(OverlayOpacityDisplayText))]
    private double _overlayOpacity = 0.85;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayScalePercentText))]
    private double _overlayScale = 1.0;

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
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuVram))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuClock))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuPower))]
    [NotifyPropertyChangedFor(nameof(EffectiveShowRam))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow1))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow2))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow1Pipe))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow2Pipe))]
    [NotifyPropertyChangedFor(nameof(ShowGpuRow1))]
    [NotifyPropertyChangedFor(nameof(ShowGpuRow2))]
    [NotifyPropertyChangedFor(nameof(ShowGpuRow1Pipe))]
    [NotifyPropertyChangedFor(nameof(ShowGpuVramClockPipe))]
    [NotifyPropertyChangedFor(nameof(ShowGpuClockPowerPipe))]
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
    [NotifyPropertyChangedFor(nameof(ShowCpuRow1))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow1Pipe))]
    private bool _showCpuTemp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuLoad))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow1))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow1Pipe))]
    [NotifyPropertyChangedFor(nameof(ShowNormalCpuLoad))]
    [NotifyPropertyChangedFor(nameof(ShowHybridCpuLoad))]
    private bool _showCpuLoad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuClock))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow2))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow2Pipe))]
    [NotifyPropertyChangedFor(nameof(ShowNormalCpuClock))]
    [NotifyPropertyChangedFor(nameof(ShowHybridCpuClock))]
    private bool _showCpuClock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuPower))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow2))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow2Pipe))]
    private bool _showCpuPower;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHybridCpuMetrics))]
    [NotifyPropertyChangedFor(nameof(ShowNormalCpuLoad))]
    [NotifyPropertyChangedFor(nameof(ShowNormalCpuClock))]
    [NotifyPropertyChangedFor(nameof(ShowHybridCpuLoad))]
    [NotifyPropertyChangedFor(nameof(ShowHybridCpuClock))]
    [NotifyPropertyChangedFor(nameof(EffectiveCompactCpuLoadText))]
    [NotifyPropertyChangedFor(nameof(EffectiveCompactCpuHeader))]
    [NotifyPropertyChangedFor(nameof(EffectiveCompactCpuHeaderWithSpace))]
    private bool _splitIntelHybridCores;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHybridCpuMetrics))]
    [NotifyPropertyChangedFor(nameof(ShowNormalCpuLoad))]
    [NotifyPropertyChangedFor(nameof(ShowNormalCpuClock))]
    [NotifyPropertyChangedFor(nameof(ShowHybridCpuLoad))]
    [NotifyPropertyChangedFor(nameof(ShowHybridCpuClock))]
    [NotifyPropertyChangedFor(nameof(IsNonHybridCpu))]
    [NotifyPropertyChangedFor(nameof(EffectiveCompactCpuLoadText))]
    [NotifyPropertyChangedFor(nameof(EffectiveCompactCpuHeader))]
    [NotifyPropertyChangedFor(nameof(EffectiveCompactCpuHeaderWithSpace))]
    private bool _isIntelHybridCpu;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuTemp))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowGpuRow1))]
    [NotifyPropertyChangedFor(nameof(ShowGpuRow1Pipe))]
    private bool _showGpuTemp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuLoad))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowGpuRow1))]
    [NotifyPropertyChangedFor(nameof(ShowGpuRow1Pipe))]
    private bool _showGpuLoad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuVram))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowGpuRow2))]
    [NotifyPropertyChangedFor(nameof(ShowGpuVramClockPipe))]
    [NotifyPropertyChangedFor(nameof(ShowGpuClockPowerPipe))]
    private bool _showGpuVram;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuClock))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowGpuRow2))]
    [NotifyPropertyChangedFor(nameof(ShowGpuVramClockPipe))]
    [NotifyPropertyChangedFor(nameof(ShowGpuClockPowerPipe))]
    private bool _showGpuClock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuPower))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowGpuRow2))]
    [NotifyPropertyChangedFor(nameof(ShowGpuVramClockPipe))]
    [NotifyPropertyChangedFor(nameof(ShowGpuClockPowerPipe))]
    private bool _showGpuPower;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowRam))]
    private bool _showRam;

    public string OverlayOpacityPercentText => $"{Math.Round(OverlayOpacity * 100.0):0}%";

    public string OverlayOpacityDisplayText => $"Opacidade do Fundo: {Math.Round(OverlayOpacity * 100.0):0}%";

    public string OverlayScalePercentText => $"{Math.Round(OverlayScale * 100.0):0}%";

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
    public bool EffectiveShowGpuVram => !IsCompactMode && ShowGpuVram;
    public bool EffectiveShowGpuClock => !IsCompactMode && ShowGpuClock;
    public bool EffectiveShowGpuPower => !IsCompactMode && ShowGpuPower;
    public bool EffectiveShowRam => !IsCompactMode && ShowRam;

    public bool ShowCpuSection => EffectiveShowCpuTemp || EffectiveShowCpuLoad || EffectiveShowCpuClock || EffectiveShowCpuPower;

    public bool ShowGpuSection => EffectiveShowGpuTemp || EffectiveShowGpuLoad || EffectiveShowGpuVram || EffectiveShowGpuClock || EffectiveShowGpuPower;

    // Propriedades computadas para CPU Híbrida Intel (P-Cores vs E-Cores)
    public bool IsNonHybridCpu => !IsIntelHybridCpu;
    public bool ShowHybridCpuMetrics => SplitIntelHybridCores && IsIntelHybridCpu;
    public bool ShowNormalCpuLoad => !ShowHybridCpuMetrics && EffectiveShowCpuLoad;
    public bool ShowNormalCpuClock => !ShowHybridCpuMetrics && EffectiveShowCpuClock;
    public bool ShowHybridCpuLoad => ShowHybridCpuMetrics && EffectiveShowCpuLoad;
    public bool ShowHybridCpuClock => ShowHybridCpuMetrics && EffectiveShowCpuClock;
    public string EffectiveCompactCpuHeader => ShowHybridCpuMetrics ? "CPU" : "CPU:";
    public string EffectiveCompactCpuHeaderWithSpace => ShowHybridCpuMetrics ? "CPU " : "CPU: ";
    public string EffectiveCompactCpuLoadText => ShowHybridCpuMetrics ? CpuHybridCompactLoadText : CpuLoadText;

    // Visibilidades auxiliares para as linhas agrupadas do Overlay Vertical
    public bool ShowCpuRow1 => EffectiveShowCpuLoad || EffectiveShowCpuTemp;
    public bool ShowCpuRow1Pipe => EffectiveShowCpuLoad && EffectiveShowCpuTemp;
    public bool ShowCpuRow2 => EffectiveShowCpuClock || EffectiveShowCpuPower;
    public bool ShowCpuRow2Pipe => EffectiveShowCpuClock && EffectiveShowCpuPower;

    public bool ShowGpuRow1 => EffectiveShowGpuLoad || EffectiveShowGpuTemp;
    public bool ShowGpuRow1Pipe => EffectiveShowGpuLoad && EffectiveShowGpuTemp;
    public bool ShowGpuRow2 => EffectiveShowGpuVram || EffectiveShowGpuClock || EffectiveShowGpuPower;
    public bool ShowGpuVramClockPipe => EffectiveShowGpuVram && (EffectiveShowGpuClock || EffectiveShowGpuPower);
    public bool ShowGpuClockPowerPipe => EffectiveShowGpuClock && EffectiveShowGpuPower;

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

        IsIntelHybridCpu = _hardwareMonitorService.IsIntelHybridCpu;

        _isInitializing = true;
        AppSettings settings = _settingsService.Load();
        ApplySettings(settings);
        _isInitializing = false;

        _hardwareMonitorService.SplitIntelHybridCores = SplitIntelHybridCores;

        ApplyApplicationTheme(AppTheme, AccentColorHex);
        SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;

        UpdatePollingAndSmartVisibilityState();

        _hardwareMonitorService.SpecsInitialized += OnHardwareSpecsInitialized;
        _hardwareMonitorService.HeartbeatTick += OnHeartbeatTick;
        _hardwareMonitorService.MetricsUpdated += OnHardwareMetricsUpdated;

        // Chamada explícita para garantir brushes de opacidade e cores 100% atualizados no final da inicialização
        UpdateOverlayBrushes();
    }

    public void SetMainWindowVisible(bool isVisible)
    {
        _hardwareMonitorService.IsMainWindowVisible = isVisible;
        UpdatePollingAndSmartVisibilityState();
    }

    private void OnSystemUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General &&
            string.Equals(AppTheme, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                ApplyApplicationTheme("Auto", AccentColorHex);
            }, DispatcherPriority.Background);
        }
    }

    public static void ApplyApplicationTheme(string? themeSetting, string? accentHex = null)
    {
        try
        {
            ApplicationTheme targetTheme = ApplicationTheme.Dark;

            if (string.Equals(themeSetting, "Light", StringComparison.OrdinalIgnoreCase))
            {
                targetTheme = ApplicationTheme.Light;
            }
            else if (string.Equals(themeSetting, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                targetTheme = IsWindowsInLightTheme() ? ApplicationTheme.Light : ApplicationTheme.Dark;
            }

            bool isLight = targetTheme == ApplicationTheme.Light;
            Color accentColor = ResolveEffectiveAccentColor(accentHex, isLight);

            ApplicationThemeManager.Apply(targetTheme, Wpf.Ui.Controls.WindowBackdropType.None, false);

            try
            {
                ApplicationAccentColorManager.Apply(accentColor, targetTheme, false);
            }
            catch
            {
                // Ignora falhas de atualização de acento no runtime
            }

            ApplyMonochromeThemeResources(isLight, accentColor);

            if (Application.Current?.Windows is { } windows)
            {
                foreach (Window window in windows)
                {
                    if (window is Views.OverlayWindow overlayWindow)
                    {
                        overlayWindow.EnsurePerPixelTransparency();
                    }
                }
            }
        }
        catch
        {
            // Fallback seguro caso a troca de tema ocorra antes da janela estar pronta
        }
    }

    private static Color ResolveEffectiveAccentColor(string? accentHex, bool isLightTheme)
    {
        Color parsed = ParseHexColor(accentHex, Color.FromRgb(250, 250, 250));

        if (isLightTheme && parsed.R >= 235 && parsed.G >= 235 && parsed.B >= 235)
        {
            return Color.FromRgb(9, 9, 11);
        }

        if (!isLightTheme && parsed.R <= 25 && parsed.G <= 25 && parsed.B <= 25)
        {
            return Color.FromRgb(250, 250, 250);
        }

        return Color.FromRgb(parsed.R, parsed.G, parsed.B);
    }

    private static void ApplyMonochromeThemeResources(bool isLight, Color accentColor)
    {
        ResourceDictionary? resources = Application.Current?.Resources;
        if (resources is null)
            return;

        Color bgColor = isLight ? Color.FromRgb(250, 250, 250) : Color.FromRgb(9, 9, 11);
        Color cardColor = isLight ? Color.FromRgb(255, 255, 255) : Color.FromRgb(17, 17, 20);
        Color subCardColor = isLight ? Color.FromRgb(244, 244, 245) : Color.FromRgb(24, 24, 27);
        Color hoverColor = isLight ? Color.FromRgb(236, 236, 239) : Color.FromRgb(32, 32, 36);
        Color borderColor = isLight ? Color.FromRgb(228, 228, 231) : Color.FromRgb(39, 39, 42);
        Color primaryTextColor = isLight ? Color.FromRgb(9, 9, 11) : Color.FromRgb(250, 250, 250);
        Color mutedTextColor = isLight ? Color.FromRgb(113, 113, 122) : Color.FromRgb(161, 161, 170);

        double luminance = (0.299 * accentColor.R) + (0.587 * accentColor.G) + (0.114 * accentColor.B);
        Color accentForegroundColor = luminance > 155.0
            ? Color.FromRgb(9, 9, 11)
            : Color.FromRgb(250, 250, 250);

        Color accentMutedColor = Color.FromArgb(isLight ? (byte)24 : (byte)28, accentColor.R, accentColor.G, accentColor.B);
        Color accentBorderColor = Color.FromArgb(isLight ? (byte)115 : (byte)95, accentColor.R, accentColor.G, accentColor.B);

        var bgBrush = CreateFrozenBrushFromColor(bgColor);
        var cardBrush = CreateFrozenBrushFromColor(cardColor);
        var subCardBrush = CreateFrozenBrushFromColor(subCardColor);
        var hoverBrush = CreateFrozenBrushFromColor(hoverColor);
        var borderBrush = CreateFrozenBrushFromColor(borderColor);
        var textPrimaryBrush = CreateFrozenBrushFromColor(primaryTextColor);
        var textMutedBrush = CreateFrozenBrushFromColor(mutedTextColor);
        var accentBrush = CreateFrozenBrushFromColor(accentColor);
        var accentFgBrush = CreateFrozenBrushFromColor(accentForegroundColor);
        var accentMutedBrush = CreateFrozenBrushFromColor(accentMutedColor);
        var accentBorderBrush = CreateFrozenBrushFromColor(accentBorderColor);

        resources["AppBackgroundBrush"] = bgBrush;
        resources["AppCardBackgroundBrush"] = cardBrush;
        resources["AppSubCardBackgroundBrush"] = subCardBrush;
        resources["AppBorderBrush"] = borderBrush;
        resources["AppTextPrimaryBrush"] = textPrimaryBrush;
        resources["AppTextMutedBrush"] = textMutedBrush;
        resources["AppAccentBrush"] = accentBrush;
        resources["AppAccentForegroundBrush"] = accentFgBrush;
        resources["AppAccentMutedBackgroundBrush"] = accentMutedBrush;
        resources["AppAccentBorderBrush"] = accentBorderBrush;

        // Sobrescrita dos tokens do WPF-UI para manter estética Shadcn/Linear 100% consistente
        resources["ApplicationBackgroundBrush"] = bgBrush;
        resources["CardBackgroundFillColorDefaultBrush"] = cardBrush;
        resources["CardBackgroundFillColorSecondaryBrush"] = subCardBrush;
        resources["ControlFillColorDefaultBrush"] = subCardBrush;
        resources["ControlFillColorSecondaryBrush"] = hoverBrush;
        resources["ControlStrokeColorDefaultBrush"] = borderBrush;
        resources["CardStrokeColorDefaultBrush"] = borderBrush;
        resources["TextFillColorPrimaryBrush"] = textPrimaryBrush;
        resources["TextFillColorSecondaryBrush"] = textMutedBrush;
        resources["AccentFillColorDefaultBrush"] = accentBrush;
        resources["AccentTextFillColorPrimaryBrush"] = accentBrush;
    }

    private static SolidColorBrush CreateFrozenBrushFromColor(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static bool IsWindowsInLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            if (value is int intVal)
            {
                return intVal != 0;
            }
        }
        catch
        {
            // Fallback para escuro se a chave não puder ser lida
        }

        return false;
    }

    private static Color ParseHexColor(string? hexColor, Color fallbackColor)
    {
        if (!string.IsNullOrWhiteSpace(hexColor))
        {
            try
            {
                string normalized = hexColor.Trim();
                if (!normalized.StartsWith('#'))
                {
                    normalized = "#" + normalized;
                }

                if (ColorConverter.ConvertFromString(normalized) is Color c)
                {
                    return c;
                }
            }
            catch
            {
                // Mantém fallbackColor caso o código Hex digitado seja inválido
            }
        }

        return fallbackColor;
    }

    private static SolidColorBrush CreateFrozenBrush(string? hexColor, Color fallbackColor)
    {
        Color parsedColor = ParseHexColor(hexColor, fallbackColor);
        var brush = new SolidColorBrush(parsedColor);
        brush.Freeze();
        return brush;
    }

    public static double NormalizeOverlayOpacity(double rawOpacity)
    {
        if (double.IsNaN(rawOpacity) || double.IsInfinity(rawOpacity) || rawOpacity <= 0.0)
            return 0.85;

        double normalized = rawOpacity > 1.0 ? (rawOpacity / 100.0) : rawOpacity;
        return Math.Clamp(Math.Round(normalized, 2), 0.05, 1.0);
    }

    public void UpdateOverlayBrushes()
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(UpdateOverlayBrushes);
            return;
        }

        double normalizedOpacity = NormalizeOverlayOpacity(OverlayOpacity);
        if (Math.Abs(normalizedOpacity - OverlayOpacity) > 0.0001)
        {
            OverlayOpacity = normalizedOpacity;
        }

        byte alpha = (byte)Math.Clamp(normalizedOpacity * 255.0, 10, 255);

        Color baseColor = ParseHexColor(OverlayBackgroundColorHex, Color.FromRgb(9, 9, 11));
        var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        brush.Freeze();
        OverlayBackgroundBrush = brush;

        Color baseBorderColor = ParseHexColor(OverlayBorderColorHex, Color.FromRgb(46, 46, 50));
        byte borderAlpha = baseBorderColor.A >= 250
            ? alpha
            : (byte)Math.Clamp(alpha * Math.Max(0.18, baseBorderColor.A / 255.0), 10, 255);
        var borderBrush = new SolidColorBrush(Color.FromArgb(borderAlpha, baseBorderColor.R, baseBorderColor.G, baseBorderColor.B));
        borderBrush.Freeze();
        OverlayBorderBrush = borderBrush;
    }

    private void OnHardwareSpecsInitialized(HardwareSpecs specs)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            OsName = specs.OsName;
            OsEdition = specs.OsEdition;
            OsBuild = specs.OsBuild;

            CpuName = specs.CpuName;
            CpuShortName = specs.CpuShortName;
            GpuShortName = specs.PrimaryGpuShortName;
            CpuPhysicalCores = specs.CpuPhysicalCores;
            CpuLogicalThreads = specs.CpuLogicalThreads;
            CpuCoresThreadsSummary = specs.CpuCoresThreadsSummary;
            IsIntelHybridCpu = specs.IsIntelHybridCpu;

            if (specs.TotalRamGb > 0f)
            {
                double roundedTotalRam = Math.Round(specs.TotalRamGb, 1);
                RamTotalText = string.Create(CultureInfo.InvariantCulture, $"{roundedTotalRam:0.0} GB");
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
        }, DispatcherPriority.Background);
    }

    private void ApplySettings(AppSettings settings)
    {
        IsOverlayVisible = settings.IsOverlayVisible;
        IsOverlayLocked = settings.IsOverlayLocked;

        OverlayOpacity = NormalizeOverlayOpacity(settings.OverlayOpacity);
        OverlayScale = Math.Clamp(Math.Round(settings.OverlayScale <= 0 ? 1.0 : settings.OverlayScale, 2), 0.70, 2.00);
        OverlayLeft = Math.Round(settings.OverlayLeft);
        OverlayTop = Math.Round(settings.OverlayTop);
        StartWithWindows = settings.StartWithWindows;

        IsCompactMode = settings.IsCompactMode;
        SmartFullscreenOnly = settings.SmartFullscreenOnly;
        IsHorizontalLayout = settings.IsCompactMode || settings.IsHorizontalLayout;

        AppTheme = string.IsNullOrWhiteSpace(settings.AppTheme) ? "Dark" : settings.AppTheme;
        AccentColorHex = string.IsNullOrWhiteSpace(settings.AccentColorHex)
            ? DefaultAccentColorHex
            : settings.AccentColorHex;

        // Migra defaults legados para o novo esquema monocromático se o usuário usava os valores antigos padrão
        string rawBgHex = string.IsNullOrWhiteSpace(settings.OverlayBackgroundColorHex) ? DefaultBackgroundColorHex : settings.OverlayBackgroundColorHex;
        string rawBorderHex = string.IsNullOrWhiteSpace(settings.OverlayBorderColorHex) ? DefaultBorderColorHex : settings.OverlayBorderColorHex;
        string rawHeaderHex = string.IsNullOrWhiteSpace(settings.OverlayHeaderColorHex) ? DefaultHeaderColorHex : settings.OverlayHeaderColorHex;
        string rawValueHex = string.IsNullOrWhiteSpace(settings.OverlayValueTextColorHex) ? DefaultValueTextColorHex : settings.OverlayValueTextColorHex;

        OverlayBackgroundColorHex = string.Equals(rawBgHex, "#141418", StringComparison.OrdinalIgnoreCase)
            ? DefaultBackgroundColorHex
            : rawBgHex;
        OverlayBorderColorHex = string.Equals(rawBorderHex, "#33FFFFFF", StringComparison.OrdinalIgnoreCase)
            ? DefaultBorderColorHex
            : rawBorderHex;
        OverlayHeaderColorHex = string.Equals(rawHeaderHex, "#00E5FF", StringComparison.OrdinalIgnoreCase)
            ? DefaultHeaderColorHex
            : rawHeaderHex;
        OverlayValueTextColorHex = string.Equals(rawValueHex, "#FFFFFF", StringComparison.OrdinalIgnoreCase)
            ? DefaultValueTextColorHex
            : rawValueHex;

        double warnTemp = Math.Clamp(Math.Round(settings.TempWarningThreshold <= 0 ? 70.0 : settings.TempWarningThreshold), 30.0, 110.0);
        double critTemp = Math.Clamp(Math.Round(settings.TempCriticalThreshold <= 0 ? 85.0 : settings.TempCriticalThreshold), 35.0, 120.0);
        if (critTemp <= warnTemp)
        {
            critTemp = Math.Clamp(warnTemp + 5.0, 35.0, 120.0);
            if (critTemp <= warnTemp)
            {
                warnTemp = Math.Max(30.0, critTemp - 1.0);
            }
        }

        EnableThermalColorAlerts = settings.EnableThermalColorAlerts;
        TempWarningThreshold = warnTemp;
        TempCriticalThreshold = critTemp;

        if (settings.ToggleOverlayHotkeyModifiers != 0 && settings.ToggleOverlayHotkeyKey != 0)
        {
            ToggleOverlayHotkeyModifiers = settings.ToggleOverlayHotkeyModifiers;
            ToggleOverlayHotkeyKey = settings.ToggleOverlayHotkeyKey;
            ToggleOverlayHotkeyDisplay = string.IsNullOrWhiteSpace(settings.ToggleOverlayHotkeyDisplay)
                ? "Ctrl + Shift + O"
                : settings.ToggleOverlayHotkeyDisplay;
        }
        else
        {
            ToggleOverlayHotkeyModifiers = WindowWin32Helper.MOD_CONTROL | WindowWin32Helper.MOD_SHIFT;
            ToggleOverlayHotkeyKey = WindowWin32Helper.VK_O;
            ToggleOverlayHotkeyDisplay = "Ctrl + Shift + O";
        }

        if (settings.ToggleCompactModeHotkeyModifiers != 0 && settings.ToggleCompactModeHotkeyKey != 0)
        {
            ToggleCompactModeHotkeyModifiers = settings.ToggleCompactModeHotkeyModifiers;
            ToggleCompactModeHotkeyKey = settings.ToggleCompactModeHotkeyKey;
            ToggleCompactModeHotkeyDisplay = string.IsNullOrWhiteSpace(settings.ToggleCompactModeHotkeyDisplay)
                ? "Ctrl + Shift + C"
                : settings.ToggleCompactModeHotkeyDisplay;
        }
        else
        {
            ToggleCompactModeHotkeyModifiers = WindowWin32Helper.MOD_CONTROL | WindowWin32Helper.MOD_SHIFT;
            ToggleCompactModeHotkeyKey = WindowWin32Helper.VK_C;
            ToggleCompactModeHotkeyDisplay = "Ctrl + Shift + C";
        }

        RefreshFrozenColorBrushes();
        InitializeOverlaySectionsOrder(settings.OverlaySectionOrder);

        ShowFps = settings.ShowFps;

        ShowCpuTemp = settings.ShowCpuTemp;
        ShowCpuLoad = settings.ShowCpuLoad;
        ShowCpuClock = settings.ShowCpuClock;
        ShowCpuPower = settings.ShowCpuPower;
        SplitIntelHybridCores = settings.SplitIntelHybridCores;
        _hardwareMonitorService.SplitIntelHybridCores = settings.SplitIntelHybridCores;

        ShowGpuTemp = settings.ShowGpuTemp;
        ShowGpuLoad = settings.ShowGpuLoad;
        ShowGpuVram = settings.ShowGpuVram;
        ShowGpuClock = settings.ShowGpuClock;
        ShowGpuPower = settings.ShowGpuPower;

        ShowRam = settings.ShowRam;
    }

    private void RefreshFrozenColorBrushes()
    {
        UpdateOverlayBrushes();
        OverlayHeaderBrush = CreateFrozenBrush(OverlayHeaderColorHex, Color.FromRgb(250, 250, 250));
        OverlayValueTextBrush = CreateFrozenBrush(OverlayValueTextColorHex, Color.FromRgb(250, 250, 250));
        UpdateThermalAlertBrushes(_lastCpuTemp, _lastGpuTemp);
    }

    private void InitializeOverlaySectionsOrder(List<string>? savedOrder)
    {
        OverlaySections.Clear();

        var orderedKeys = new List<string>();
        if (savedOrder is not null)
        {
            // Migração da ordem legada: se o FPS estava na primeira posição (ordem antiga padrão), move-o para o final
            if (savedOrder.Count > 0 && string.Equals(savedOrder[0], "FPS", StringComparison.OrdinalIgnoreCase))
            {
                var migrated = new List<string>(savedOrder);
                migrated.RemoveAt(0);
                migrated.Add("FPS");
                savedOrder = migrated;
            }

            foreach (string key in savedOrder)
            {
                string upper = key.Trim().ToUpperInvariant();
                if (DefaultSectionKeys.Contains(upper) && !orderedKeys.Contains(upper))
                {
                    orderedKeys.Add(upper);
                }
            }
        }

        foreach (string defaultKey in DefaultSectionKeys)
        {
            if (!orderedKeys.Contains(defaultKey))
            {
                orderedKeys.Add(defaultKey);
            }
        }

        foreach (string key in orderedKeys)
        {
            OverlaySections.Add(CreateSectionViewModel(key));
        }

        UpdateSectionBoundaryFlags();
    }

    private OverlaySectionItemViewModel CreateSectionViewModel(string key) => key switch
    {
        "FPS" => new OverlaySectionItemViewModel("FPS", "Contador de FPS (Quadros por Segundo)", "TopSpeed24", this),
        "CPU" => new OverlaySectionItemViewModel("CPU", "Processador (Modelo, Uso, Temp, Clock, Watts)", "DeveloperBoard24", this),
        "GPU" => new OverlaySectionItemViewModel("GPU", "Placa de Vídeo (Modelo, Uso, Temp, VRAM, Clock, Watts)", "Video24", this),
        "RAM" => new OverlaySectionItemViewModel("RAM", "Memória RAM Física (GB e %)", "Ram16", this),
        _ => new OverlaySectionItemViewModel(key, key, "DataHistogram24", this)
    };

    private void UpdateSectionBoundaryFlags()
    {
        for (int i = 0; i < OverlaySections.Count; i++)
        {
            OverlaySections[i].PositionNumber = i + 1;
            OverlaySections[i].CanMoveUp = i > 0;
            OverlaySections[i].CanMoveDown = i < OverlaySections.Count - 1;
        }
    }

    [RelayCommand]
    private void MoveOverlaySectionUp(OverlaySectionItemViewModel? item)
    {
        if (item is null)
            return;

        int index = OverlaySections.IndexOf(item);
        if (index <= 0)
            return;

        OverlaySections.Move(index, index - 1);
        UpdateSectionBoundaryFlags();
        SaveSettings();
    }

    [RelayCommand]
    private void MoveOverlaySectionDown(OverlaySectionItemViewModel? item)
    {
        if (item is null)
            return;

        int index = OverlaySections.IndexOf(item);
        if (index < 0 || index >= OverlaySections.Count - 1)
            return;

        OverlaySections.Move(index, index + 1);
        UpdateSectionBoundaryFlags();
        SaveSettings();
    }

    [RelayCommand]
    private void ResetOverlayScale()
    {
        OverlayScale = 1.0;
    }

    [RelayCommand]
    private void ResetOverlayColors()
    {
        OverlayBackgroundColorHex = DefaultBackgroundColorHex;
        OverlayBorderColorHex = DefaultBorderColorHex;
        OverlayHeaderColorHex = DefaultHeaderColorHex;
        OverlayValueTextColorHex = DefaultValueTextColorHex;
        RefreshFrozenColorBrushes();
        SaveSettings();
    }

    [RelayCommand]
    private void ApplyColorPreset(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
            return;

        switch (preset)
        {
            case "Monochrome":
                OverlayBackgroundColorHex = DefaultBackgroundColorHex;
                OverlayBorderColorHex = DefaultBorderColorHex;
                OverlayHeaderColorHex = DefaultHeaderColorHex;
                OverlayValueTextColorHex = DefaultValueTextColorHex;
                break;

            case "CleanCyan":
                OverlayBackgroundColorHex = "#141418";
                OverlayBorderColorHex = "#3338BDF8";
                OverlayHeaderColorHex = "#00E5FF";
                OverlayValueTextColorHex = "#FFFFFF";
                break;

            case "EmeraldNeon":
                OverlayBackgroundColorHex = "#0F1715";
                OverlayBorderColorHex = "#382DD4BF";
                OverlayHeaderColorHex = "#2DD4BF";
                OverlayValueTextColorHex = "#F0FDFA";
                break;

            case "AmberGold":
                OverlayBackgroundColorHex = "#181410";
                OverlayBorderColorHex = "#40FFB900";
                OverlayHeaderColorHex = "#FFB900";
                OverlayValueTextColorHex = "#FFFBEB";
                break;

            case "CrimsonPulse":
                OverlayBackgroundColorHex = "#1A1014";
                OverlayBorderColorHex = "#40F43F5E";
                OverlayHeaderColorHex = "#FB7185";
                OverlayValueTextColorHex = "#FFF1F2";
                break;

            case "PurpleSynth":
                OverlayBackgroundColorHex = "#151022";
                OverlayBorderColorHex = "#40A855F7";
                OverlayHeaderColorHex = "#C084FC";
                OverlayValueTextColorHex = "#FAF5FF";
                break;

            case "MinimalLight":
                OverlayBackgroundColorHex = "#F2F8FAFC";
                OverlayBorderColorHex = "#440284C7";
                OverlayHeaderColorHex = "#0284C7";
                OverlayValueTextColorHex = "#0F172A";
                break;
        }
    }

    [RelayCommand]
    private void SetAccentQuickColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            AccentColorHex = hex;
        }
    }

    [RelayCommand]
    private void ResetAccentColor()
    {
        AccentColorHex = DefaultAccentColorHex;
    }

    [RelayCommand]
    private void ResetThermalThresholds()
    {
        EnableThermalColorAlerts = true;
        TempWarningThreshold = 70.0;
        TempCriticalThreshold = 85.0;
    }

    public void RegisterConfiguredHotkeys(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        WindowWin32Helper.SetHotkeyWindowHandle(hwnd);
        bool overlayOk = WindowWin32Helper.RegisterOrUpdateHotKey(
            WindowWin32Helper.HOTKEY_ID_TOGGLE_OVERLAY,
            ToggleOverlayHotkeyModifiers,
            ToggleOverlayHotkeyKey);

        bool compactOk = WindowWin32Helper.RegisterOrUpdateHotKey(
            WindowWin32Helper.HOTKEY_ID_TOGGLE_COMPACT,
            ToggleCompactModeHotkeyModifiers,
            ToggleCompactModeHotkeyKey);

        UpdateHotkeyStatusMessage(overlayOk, compactOk);
    }

    public void PauseHotkeysForCapture() => WindowWin32Helper.PauseAllHotkeys();

    public void ResumeHotkeysAfterCapture()
    {
        bool allOk = WindowWin32Helper.ResumeAllHotkeys();
        if (!allOk)
        {
            HotkeyStatusMessage = "Um ou mais atalhos estão em conflito com outro aplicativo em execução.";
        }
    }

    public bool SetToggleOverlayHotkey(uint modifiers, uint virtualKey, string displayText)
    {
        if (modifiers == ToggleCompactModeHotkeyModifiers && virtualKey == ToggleCompactModeHotkeyKey)
        {
            HotkeyStatusMessage = "Esse atalho já está atribuído ao Modo Compacto.";
            return false;
        }

        ToggleOverlayHotkeyModifiers = modifiers;
        ToggleOverlayHotkeyKey = virtualKey;
        ToggleOverlayHotkeyDisplay = displayText;

        bool ok = WindowWin32Helper.RegisterOrUpdateHotKey(
            WindowWin32Helper.HOTKEY_ID_TOGGLE_OVERLAY,
            modifiers,
            virtualKey);

        HotkeyStatusMessage = ok
            ? "Atalho de exibição do Overlay atualizado."
            : "Aviso: o atalho foi salvo, mas pode estar em conflito com outro aplicativo.";

        SaveSettings();
        return ok;
    }

    public bool SetToggleCompactModeHotkey(uint modifiers, uint virtualKey, string displayText)
    {
        if (modifiers == ToggleOverlayHotkeyModifiers && virtualKey == ToggleOverlayHotkeyKey)
        {
            HotkeyStatusMessage = "Esse atalho já está atribuído a Mostrar/Ocultar Overlay.";
            return false;
        }

        ToggleCompactModeHotkeyModifiers = modifiers;
        ToggleCompactModeHotkeyKey = virtualKey;
        ToggleCompactModeHotkeyDisplay = displayText;

        bool ok = WindowWin32Helper.RegisterOrUpdateHotKey(
            WindowWin32Helper.HOTKEY_ID_TOGGLE_COMPACT,
            modifiers,
            virtualKey);

        HotkeyStatusMessage = ok
            ? "Atalho de Modo Compacto atualizado."
            : "Aviso: o atalho foi salvo, mas pode estar em conflito com outro aplicativo.";

        SaveSettings();
        return ok;
    }

    [RelayCommand]
    private void ResetToggleOverlayHotkey()
    {
        SetToggleOverlayHotkey(
            WindowWin32Helper.MOD_CONTROL | WindowWin32Helper.MOD_SHIFT,
            WindowWin32Helper.VK_O,
            "Ctrl + Shift + O");
    }

    [RelayCommand]
    private void ResetToggleCompactModeHotkey()
    {
        SetToggleCompactModeHotkey(
            WindowWin32Helper.MOD_CONTROL | WindowWin32Helper.MOD_SHIFT,
            WindowWin32Helper.VK_C,
            "Ctrl + Shift + C");
    }

    private void UpdateHotkeyStatusMessage(bool overlayOk, bool compactOk)
    {
        if (!overlayOk && !compactOk)
        {
            HotkeyStatusMessage = "Conflito detectado: ambos os atalhos globais estão ocupados por outro software.";
        }
        else if (!overlayOk)
        {
            HotkeyStatusMessage = $"Conflito no atalho '{ToggleOverlayHotkeyDisplay}' — escolha outra combinação.";
        }
        else if (!compactOk)
        {
            HotkeyStatusMessage = $"Conflito no atalho '{ToggleCompactModeHotkeyDisplay}' — escolha outra combinação.";
        }
        else
        {
            HotkeyStatusMessage = "Atalhos globais registrados e prontos para uso em jogos.";
        }
    }

    [RelayCommand]
    private void SetHeaderQuickColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            OverlayHeaderColorHex = hex;
        }
    }

    [RelayCommand]
    private void SetValueQuickColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            OverlayValueTextColorHex = hex;
        }
    }

    [RelayCommand]
    private void SetBackgroundQuickColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            OverlayBackgroundColorHex = hex;
        }
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

        if (!_fpsMonitorService.IsSuspended)
        {
            _fpsMonitorService.RefreshActiveForegroundProcess();
        }

        if (IsForegroundFullScreen != isFullscreen)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                IsForegroundFullScreen = isFullscreen;
            }, DispatcherPriority.Background);
        }
    }

    private void OnHardwareMetricsUpdated(HardwareMetrics metrics)
    {
        int currentFps = _fpsMonitorService.CurrentFps;
        metrics.CurrentFps = currentFps;

        // Pré-arredondamento fora da UI Thread para evitar notificações inúteis de redesenho no WPF
        int roundedCpuTemp = (int)Math.Round(metrics.CpuTemperature);
        double roundedCpuLoad = Math.Clamp(Math.Round(metrics.CpuLoad), 0.0, 100.0);
        int roundedCpuClock = (int)Math.Round(metrics.CpuClock);
        int roundedCpuMaxClock = (int)Math.Round(metrics.CpuMaxClock);
        double roundedCpuPower = Math.Round(metrics.CpuPower, 1);
        double? roundedCpuVcore = metrics.CpuVoltageVcore is > 0f ? Math.Round(metrics.CpuVoltageVcore.Value, 3) : null;

        double roundedPCoreLoad = Math.Clamp(Math.Round(metrics.CpuPCoreLoad), 0.0, 100.0);
        double roundedECoreLoad = Math.Clamp(Math.Round(metrics.CpuECoreLoad), 0.0, 100.0);
        int roundedPCoreClock = (int)Math.Round(metrics.CpuPCoreClock);
        int roundedECoreClock = (int)Math.Round(metrics.CpuECoreClock);

        int roundedGpuTemp = (int)Math.Round(metrics.GpuTemperature);
        int roundedGpuLoad = (int)Math.Clamp(Math.Round(metrics.GpuLoad), 0.0, 100.0);
        int roundedGpuClock = (int)Math.Round(metrics.GpuClock);
        double roundedGpuPower = Math.Round(metrics.GpuPower, 1);
        double roundedGpuVramUsed = Math.Round(metrics.GpuVramUsedGb, 1);
        double? roundedGpuVramTotal = metrics.GpuVramTotalGb is > 0f ? Math.Round(metrics.GpuVramTotalGb.Value, 1) : null;

        double roundedRamUsed = Math.Round(metrics.RamUsedGb, 1);
        double roundedRamTotal = Math.Round(metrics.RamTotalGb, 1);
        double roundedRamFree = Math.Round(metrics.RamFreeGb, 1);
        double roundedRamPct = Math.Clamp(Math.Round(metrics.RamUsagePercent), 0.0, 100.0);

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (_isDisposed)
                return;

            // FPS
            FpsValueText = $"{metrics.CurrentFps}";
            FpsText = $"{metrics.CurrentFps} FPS";

            // CPU & Alertas Térmicos
            UpdateThermalAlertBrushes(roundedCpuTemp, roundedGpuTemp);
            CpuTemperatureText = roundedCpuTemp > 0 ? $"{roundedCpuTemp} °C" : "N/D";
            CpuLoadText = $"{roundedCpuLoad:0}%";
            CpuLoadBarValue = roundedCpuLoad;

            CpuClockText = roundedCpuClock > 0 ? $"{roundedCpuClock} MHz" : "N/D";
            CpuMaxClockText = roundedCpuMaxClock > 0 ? $"{roundedCpuMaxClock} MHz" : "N/D";

            CpuClockAvgMaxText = roundedCpuClock > 0
                ? (roundedCpuMaxClock > 0
                    ? $"{roundedCpuClock} MHz (Máx: {roundedCpuMaxClock} MHz)"
                    : $"{roundedCpuClock} MHz")
                : "N/D";

            CpuPowerText = roundedCpuPower > 0.0
                ? string.Create(CultureInfo.InvariantCulture, $"{roundedCpuPower:0.0} W")
                : "N/D";

            CpuVoltageText = roundedCpuVcore is > 0.0
                ? string.Create(CultureInfo.InvariantCulture, $"{roundedCpuVcore.Value:0.000} V")
                : "N/D";

            if (metrics.IsHybridCpu)
            {
                CpuPCoreLoad = (float)roundedPCoreLoad;
                CpuECoreLoad = (float)roundedECoreLoad;
                CpuPCoreClock = roundedPCoreClock;
                CpuECoreClock = roundedECoreClock;

                CpuPCoreLoadText = $"{roundedPCoreLoad:0}%";
                CpuECoreLoadText = $"{roundedECoreLoad:0}%";

                CpuPCoreClockText = roundedPCoreClock > 0 ? $"{roundedPCoreClock} MHz" : "N/D";
                CpuECoreClockText = roundedECoreClock > 0 ? $"{roundedECoreClock} MHz" : "N/D";

                CpuHybridCompactLoadText = $"P: {roundedPCoreLoad:0}% | E: {roundedECoreLoad:0}%";
            }

            // GPU Primária do Overlay
            GpuTemperatureText = roundedGpuTemp > 0 ? $"{roundedGpuTemp} °C" : "N/D";
            GpuLoadText = $"{roundedGpuLoad}%";
            GpuClockText = roundedGpuClock > 0 ? $"{roundedGpuClock} MHz" : "N/D";
            GpuPowerText = roundedGpuPower > 0.0
                ? string.Create(CultureInfo.InvariantCulture, $"{roundedGpuPower:0.0} W")
                : "N/D";

            if (roundedGpuVramUsed > 0.0)
            {
                if (roundedGpuVramTotal is > 0.0)
                {
                    int vramPct = (int)Math.Clamp(Math.Round((roundedGpuVramUsed / roundedGpuVramTotal.Value) * 100.0), 0.0, 100.0);
                    GpuVramText = string.Create(CultureInfo.InvariantCulture, $"{roundedGpuVramUsed:0.0} GB ({vramPct}%)");
                }
                else
                {
                    GpuVramText = string.Create(CultureInfo.InvariantCulture, $"{roundedGpuVramUsed:0.0} GB");
                }
            }
            else
            {
                GpuVramText = "N/D";
            }

            // Memória RAM
            RamUsageText = string.Create(
                CultureInfo.InvariantCulture,
                $"{roundedRamUsed:0.0} GB ({roundedRamPct:0}%)");

            RamUsedText = string.Create(CultureInfo.InvariantCulture, $"{roundedRamUsed:0.0} GB");
            RamPercentText = $"{roundedRamPct:0}%";
            RamUsageBarValue = roundedRamPct;

            if (roundedRamTotal > 0.0)
            {
                RamTotalText = string.Create(CultureInfo.InvariantCulture, $"{roundedRamTotal:0.0} GB");
                RamFreeText = string.Create(CultureInfo.InvariantCulture, $"{roundedRamFree:0.0} GB");
                RamDetailedSummaryText = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{roundedRamUsed:0.0} GB usados de {roundedRamTotal:0.0} GB ({roundedRamFree:0.0} GB livres)");
            }

            if (metrics.IsDetailedSnapshot)
            {
                SyncCpuCoresCollection(metrics.CpuCores);
                SyncGpusCollection(metrics.Gpus);
                SyncStorageCollections(metrics.StorageDrives, metrics.PhysicalDisks);
            }
        }, DispatcherPriority.Background);
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
        if (int.TryParse(indexParameter, out int idx) && idx is >= 0 and <= 5)
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
        if (_isInitializing || _isDisposed)
            return;

        var settings = new AppSettings
        {
            IsOverlayVisible = IsOverlayVisible,
            IsOverlayLocked = IsOverlayLocked,
            OverlayOpacity = NormalizeOverlayOpacity(OverlayOpacity),
            OverlayScale = Math.Clamp(Math.Round(OverlayScale, 2), 0.70, 2.00),
            OverlayLeft = OverlayLeft,
            OverlayTop = OverlayTop,
            StartWithWindows = StartWithWindows,
            IsCompactMode = IsCompactMode,
            SmartFullscreenOnly = SmartFullscreenOnly,
            IsHorizontalLayout = IsHorizontalLayout,
            AppTheme = AppTheme,
            AccentColorHex = AccentColorHex,
            OverlayBackgroundColorHex = OverlayBackgroundColorHex,
            OverlayBorderColorHex = OverlayBorderColorHex,
            OverlayHeaderColorHex = OverlayHeaderColorHex,
            OverlayValueTextColorHex = OverlayValueTextColorHex,
            EnableThermalColorAlerts = EnableThermalColorAlerts,
            TempWarningThreshold = TempWarningThreshold,
            TempCriticalThreshold = TempCriticalThreshold,
            ToggleOverlayHotkeyModifiers = ToggleOverlayHotkeyModifiers,
            ToggleOverlayHotkeyKey = ToggleOverlayHotkeyKey,
            ToggleOverlayHotkeyDisplay = ToggleOverlayHotkeyDisplay,
            ToggleCompactModeHotkeyModifiers = ToggleCompactModeHotkeyModifiers,
            ToggleCompactModeHotkeyKey = ToggleCompactModeHotkeyKey,
            ToggleCompactModeHotkeyDisplay = ToggleCompactModeHotkeyDisplay,
            OverlaySectionOrder = OverlaySections.Select(s => s.Key).ToList(),
            ShowFps = ShowFps,
            ShowCpuTemp = ShowCpuTemp,
            ShowCpuLoad = ShowCpuLoad,
            ShowCpuClock = ShowCpuClock,
            ShowCpuPower = ShowCpuPower,
            SplitIntelHybridCores = SplitIntelHybridCores,
            ShowGpuTemp = ShowGpuTemp,
            ShowGpuLoad = ShowGpuLoad,
            ShowGpuVram = ShowGpuVram,
            ShowGpuClock = ShowGpuClock,
            ShowGpuPower = ShowGpuPower,
            ShowRam = ShowRam
        };

        _settingsService.Save(settings);
    }

    private SolidColorBrush ResolveTempBrush(int tempC)
    {
        if (!EnableThermalColorAlerts)
            return OverlayValueTextBrush;

        if (tempC <= 0)
            return ThermalNormalBrush;

        if (tempC >= TempCriticalThreshold)
            return ThermalCriticalBrush;

        if (tempC >= TempWarningThreshold)
            return ThermalWarningBrush;

        return ThermalNormalBrush;
    }

    private void UpdateThermalAlertBrushes(int cpuTemp, int gpuTemp)
    {
        _lastCpuTemp = cpuTemp;
        _lastGpuTemp = gpuTemp;

        SolidColorBrush nextCpuBrush = ResolveTempBrush(cpuTemp);
        if (!ReferenceEquals(CpuTempBrush, nextCpuBrush))
        {
            CpuTempBrush = nextCpuBrush;
        }

        SolidColorBrush nextGpuBrush = ResolveTempBrush(gpuTemp);
        if (!ReferenceEquals(GpuTempBrush, nextGpuBrush))
        {
            GpuTempBrush = nextGpuBrush;
        }
    }

    partial void OnAppThemeChanged(string value)
    {
        if (!_isInitializing)
        {
            ApplyApplicationTheme(value, AccentColorHex);
        }
        SaveSettings();
    }

    partial void OnAccentColorHexChanged(string value)
    {
        if (!_isInitializing)
        {
            ApplyApplicationTheme(AppTheme, value);
        }
        SaveSettings();
    }

    partial void OnOverlayBackgroundColorHexChanged(string value)
    {
        UpdateOverlayBrushes();
        SaveSettings();
    }

    partial void OnOverlayBorderColorHexChanged(string value)
    {
        UpdateOverlayBrushes();
        SaveSettings();
    }

    partial void OnOverlayHeaderColorHexChanged(string value)
    {
        OverlayHeaderBrush = CreateFrozenBrush(value, Color.FromRgb(250, 250, 250));
        SaveSettings();
    }

    partial void OnOverlayValueTextColorHexChanged(string value)
    {
        OverlayValueTextBrush = CreateFrozenBrush(value, Color.FromRgb(250, 250, 250));
        if (!_isInitializing)
        {
            UpdateThermalAlertBrushes(_lastCpuTemp, _lastGpuTemp);
        }
        SaveSettings();
    }

    partial void OnEnableThermalColorAlertsChanged(bool value)
    {
        if (!_isInitializing)
        {
            UpdateThermalAlertBrushes(_lastCpuTemp, _lastGpuTemp);
        }
        SaveSettings();
    }

    partial void OnTempWarningThresholdChanged(double value)
    {
        if (_isInitializing)
            return;

        double clamped = double.IsNaN(value) ? 70.0 : Math.Clamp(Math.Round(value), 30.0, 110.0);
        if (Math.Abs(clamped - value) > 0.001)
        {
            TempWarningThreshold = clamped;
            return;
        }

        if (TempCriticalThreshold <= clamped)
        {
            double nextCritical = Math.Clamp(clamped + 5.0, 35.0, 120.0);
            if (nextCritical <= clamped)
            {
                nextCritical = Math.Min(120.0, clamped + 1.0);
            }
            TempCriticalThreshold = nextCritical;
        }

        UpdateThermalAlertBrushes(_lastCpuTemp, _lastGpuTemp);
        SaveSettings();
    }

    partial void OnTempCriticalThresholdChanged(double value)
    {
        if (_isInitializing)
            return;

        double clamped = double.IsNaN(value) ? 85.0 : Math.Clamp(Math.Round(value), 35.0, 120.0);
        if (Math.Abs(clamped - value) > 0.001)
        {
            TempCriticalThreshold = clamped;
            return;
        }

        if (clamped <= TempWarningThreshold)
        {
            TempWarningThreshold = Math.Max(30.0, clamped - 1.0);
        }

        UpdateThermalAlertBrushes(_lastCpuTemp, _lastGpuTemp);
        SaveSettings();
    }

    partial void OnIsOverlayVisibleChanged(bool value)
    {
        UpdatePollingAndSmartVisibilityState();
        SaveSettings();
    }

    partial void OnIsOverlayLockedChanged(bool value) => SaveSettings();

    partial void OnOverlayOpacityChanged(double value)
    {
        double normalized = NormalizeOverlayOpacity(value);
        if (Math.Abs(normalized - value) > 0.0001)
        {
            OverlayOpacity = normalized;
            return;
        }

        UpdateOverlayBrushes();
        SaveSettings();
    }

    partial void OnOverlayScaleChanged(double value) => SaveSettings();

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
        IsHorizontalLayout = value;
        OnPropertyChanged(nameof(IsHorizontalLayout));

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
    partial void OnSplitIntelHybridCoresChanged(bool value)
    {
        _hardwareMonitorService.SplitIntelHybridCores = value;
        SaveSettings();
    }
    partial void OnShowGpuTempChanged(bool value) => SaveSettings();
    partial void OnShowGpuLoadChanged(bool value) => SaveSettings();
    partial void OnShowGpuVramChanged(bool value) => SaveSettings();
    partial void OnShowGpuClockChanged(bool value) => SaveSettings();
    partial void OnShowGpuPowerChanged(bool value) => SaveSettings();
    partial void OnShowRamChanged(bool value) => SaveSettings();

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        SystemEvents.UserPreferenceChanged -= OnSystemUserPreferenceChanged;
        _hardwareMonitorService.SpecsInitialized -= OnHardwareSpecsInitialized;
        _hardwareMonitorService.HeartbeatTick -= OnHeartbeatTick;
        _hardwareMonitorService.MetricsUpdated -= OnHardwareMetricsUpdated;
    }
}

/// <summary>
/// Representa um bloco reordenável do Overlay (FPS, CPU, GPU ou RAM).
/// </summary>
public partial class OverlaySectionItemViewModel : ObservableObject
{
    public string Key { get; }
    public string DisplayTitle { get; }
    public string IconSymbol { get; }
    public MainViewModel Parent { get; }

    public bool IsFpsSection => Key == "FPS";
    public bool IsCpuSection => Key == "CPU";
    public bool IsGpuSection => Key == "GPU";
    public bool IsRamSection => Key == "RAM";

    [ObservableProperty]
    private int _positionNumber;

    [ObservableProperty]
    private bool _canMoveUp;

    [ObservableProperty]
    private bool _canMoveDown;

    public OverlaySectionItemViewModel(string key, string displayTitle, string iconSymbol, MainViewModel parent)
    {
        Key = key;
        DisplayTitle = displayTitle;
        IconSymbol = iconSymbol;
        Parent = parent;
    }
}
