using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.Models;
using HardwareOverlayApp.Services;
using HardwareOverlayApp.Views;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace HardwareOverlayApp.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public const string DefaultBackgroundColorHex = "#09090B";
    public const string DefaultBorderColorHex = "#27272A";
    public const string DefaultHeaderColorHex = "#FAFAFA";
    public const string DefaultValueTextColorHex = "#FAFAFA";

    private static readonly string[] DefaultSectionKeys = ["CPU", "GPU", "RAM", "FPS"];

    private readonly SettingsService _settingsService;
    private readonly HardwareMonitorService _hardwareMonitorService;
    private readonly FpsMonitorService _fpsMonitorService;
    private readonly StartupService _startupService;
    private readonly Action _exitApplicationAction;
    private readonly bool _isInitializing;
    private GameHistoryService? _gameHistoryService;
    private bool _isDisposed;

    public Func<(double Width, double Height)>? OverlaySizeProvider { get; set; }
    public Func<(bool OverlayRegistered, bool CompactRegistered)>? ReloadHotkeysProvider { get; set; }
    public Action? SuspendHotkeysAction { get; set; }
    public Action? EnsureOverlayTransparentAction { get; set; }

    // ==================== ATALHOS GLOBAIS PERSONALIZÁVEIS ====================
    public const ModifierKeys DefaultHotkeyModifiers = ModifierKeys.Control | ModifierKeys.Shift;
    public const Key DefaultToggleOverlayKey = Key.O;
    public const Key DefaultToggleCompactKey = Key.C;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayHotkeyDisplayText))]
    private ModifierKeys _toggleOverlayModifiers = DefaultHotkeyModifiers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayHotkeyDisplayText))]
    private Key _toggleOverlayKey = DefaultToggleOverlayKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompactHotkeyDisplayText))]
    private ModifierKeys _toggleCompactModifiers = DefaultHotkeyModifiers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompactHotkeyDisplayText))]
    private Key _toggleCompactKey = DefaultToggleCompactKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayHotkeyDisplayText))]
    private bool _isRecordingOverlayHotkey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompactHotkeyDisplayText))]
    private bool _isRecordingCompactHotkey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlayHotkeyError))]
    private string _overlayHotkeyErrorText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompactHotkeyError))]
    private string _compactHotkeyErrorText = string.Empty;

    public bool HasOverlayHotkeyError => !string.IsNullOrWhiteSpace(OverlayHotkeyErrorText);
    public bool HasCompactHotkeyError => !string.IsNullOrWhiteSpace(CompactHotkeyErrorText);

    public string OverlayHotkeyDisplayText => IsRecordingOverlayHotkey
        ? "Pressione o atalho..."
        : FormatHotkeyText(ToggleOverlayModifiers, ToggleOverlayKey);

    public string CompactHotkeyDisplayText => IsRecordingCompactHotkey
        ? "Pressione o atalho..."
        : FormatHotkeyText(ToggleCompactModifiers, ToggleCompactKey);

    // ==================== NAVEGAÇÃO LATERAL MINIMALISTA ====================
    // 0 = Visão Geral, 1 = Processador, 2 = Placa de Vídeo, 3 = Sistema & Discos, 4 = Configurações, 5 = Histórico
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsCpuTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsGpuTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsStorageTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsHistoryTabSelected))]
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

    public bool IsHistoryTabSelected
    {
        get => SelectedNavIndex == 5;
        set { if (value) SelectedNavIndex = 5; }
    }

    // ==================== HISTÓRICO OPCIONAL DE JOGOS (SOB DEMANDA) ====================
    [ObservableProperty]
    private bool _enableGameHistory;

    [ObservableProperty]
    private GameHistoryViewModel? _gameHistory;

    // ==================== SUB-ABAS DE CONFIGURAÇÕES ====================
    // 0 = Geral & Sistema, 1 = Overlay & Layout, 2 = Sensores & Ordem
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsGeneralSubTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsOverlaySubTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSensorsSubTabSelected))]
    private int _selectedSettingsSubTabIndex;

    public bool IsSettingsGeneralSubTabSelected
    {
        get => SelectedSettingsSubTabIndex == 0;
        set { if (value) SelectedSettingsSubTabIndex = 0; }
    }

    public bool IsSettingsOverlaySubTabSelected
    {
        get => SelectedSettingsSubTabIndex == 1;
        set { if (value) SelectedSettingsSubTabIndex = 1; }
    }

    public bool IsSettingsSensorsSubTabSelected
    {
        get => SelectedSettingsSubTabIndex == 2;
        set { if (value) SelectedSettingsSubTabIndex = 2; }
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

    // ==================== BRUSHES MONOCROMÁTICOS DO OVERLAY E TEMA (CONGELADOS) ====================
    private static readonly SolidColorBrush FrozenOverlayHeaderBrush = CreateFrozenRgbBrush(250, 250, 250);
    private static readonly SolidColorBrush FrozenOverlayValueTextBrush = CreateFrozenRgbBrush(250, 250, 250);
    private static readonly SolidColorBrush FrozenOverlayLabelTextBrush = CreateFrozenRgbBrush(161, 161, 170);

    private static readonly SolidColorBrush LightWindowBackgroundBrush = CreateFrozenRgbBrush(250, 250, 250);
    private static readonly SolidColorBrush LightSidebarBackgroundBrush = CreateFrozenRgbBrush(244, 244, 245);
    private static readonly SolidColorBrush LightCardBackgroundBrush = CreateFrozenRgbBrush(255, 255, 255);
    private static readonly SolidColorBrush LightSurfaceHoverBrush = CreateFrozenRgbBrush(244, 244, 245);
    private static readonly SolidColorBrush LightBorderSubtleBrush = CreateFrozenRgbBrush(228, 228, 231);
    private static readonly SolidColorBrush LightBorderActiveBrush = CreateFrozenRgbBrush(161, 161, 170);
    private static readonly SolidColorBrush LightTextPrimaryBrush = CreateFrozenRgbBrush(9, 9, 11);
    private static readonly SolidColorBrush LightTextSecondaryBrush = CreateFrozenRgbBrush(82, 82, 91);
    private static readonly SolidColorBrush LightTextMutedBrush = CreateFrozenRgbBrush(113, 113, 122);
    private static readonly SolidColorBrush LightAccentPrimaryBrush = CreateFrozenRgbBrush(9, 9, 11);
    private static readonly SolidColorBrush LightAccentForegroundBrush = CreateFrozenRgbBrush(250, 250, 250);

    private static readonly SolidColorBrush DarkWindowBackgroundBrush = CreateFrozenRgbBrush(9, 9, 11);
    private static readonly SolidColorBrush DarkSidebarBackgroundBrush = CreateFrozenRgbBrush(12, 12, 14);
    private static readonly SolidColorBrush DarkCardBackgroundBrush = CreateFrozenRgbBrush(17, 17, 20);
    private static readonly SolidColorBrush DarkSurfaceHoverBrush = CreateFrozenRgbBrush(24, 24, 27);
    private static readonly SolidColorBrush DarkBorderSubtleBrush = CreateFrozenRgbBrush(39, 39, 42);
    private static readonly SolidColorBrush DarkBorderActiveBrush = CreateFrozenRgbBrush(82, 82, 91);
    private static readonly SolidColorBrush DarkTextPrimaryBrush = CreateFrozenRgbBrush(250, 250, 250);
    private static readonly SolidColorBrush DarkTextSecondaryBrush = CreateFrozenRgbBrush(161, 161, 170);
    private static readonly SolidColorBrush DarkTextMutedBrush = CreateFrozenRgbBrush(113, 113, 122);
    private static readonly SolidColorBrush DarkAccentPrimaryBrush = CreateFrozenRgbBrush(250, 250, 250);
    private static readonly SolidColorBrush DarkAccentForegroundBrush = CreateFrozenRgbBrush(9, 9, 11);

    private int _lastAppliedBgAlpha = -1;
    private int _lastAppliedBorderAlpha = -1;
    private volatile bool _lastDispatchedFullscreen = true;
    private volatile int _activeNavTabIndex;

    // Cache do último ciclo aplicado na UI para evitar alocações de string e notificações redundantes
    private int _prevFps = int.MinValue;
    private int _prevCpuTemp = int.MinValue;
    private double _prevCpuLoad = double.NaN;
    private int _prevCpuClock = int.MinValue;
    private int _prevCpuMaxClock = int.MinValue;
    private double _prevCpuPower = double.NaN;
    private double _prevCpuVcore = double.NaN;
    private double _prevPCoreLoad = double.NaN;
    private double _prevECoreLoad = double.NaN;
    private int _prevPCoreClock = int.MinValue;
    private int _prevECoreClock = int.MinValue;
    private int _prevGpuTemp = int.MinValue;
    private int _prevGpuLoad = int.MinValue;
    private int _prevGpuClock = int.MinValue;
    private double _prevGpuPower = double.NaN;
    private double _prevGpuVramUsed = double.NaN;
    private double _prevGpuVramTotal = double.NaN;
    private double _prevRamUsed = double.NaN;
    private double _prevRamTotal = double.NaN;
    private double _prevRamFree = double.NaN;
    private double _prevRamPct = double.NaN;

    [ObservableProperty]
    private SolidColorBrush _overlayBackgroundBrush = CreateFrozenRgbBrush(9, 9, 11, 217);

    [ObservableProperty]
    private SolidColorBrush _overlayBorderBrush = CreateFrozenRgbBrush(39, 39, 42);

    [ObservableProperty]
    private SolidColorBrush _overlayHeaderBrush = FrozenOverlayHeaderBrush;

    [ObservableProperty]
    private SolidColorBrush _overlayValueTextBrush = FrozenOverlayValueTextBrush;

    [ObservableProperty]
    private SolidColorBrush _overlayLabelTextBrush = FrozenOverlayLabelTextBrush;

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
    private string _cpuClockAvgMaxText = "N/D";

    [ObservableProperty]
    private string _cpuPowerText = "N/D";

    [ObservableProperty]
    private string _cpuVoltageText = "N/D";

    // Métricas dedicadas para CPU Híbrida Intel (P-Cores e E-Cores)
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
    [NotifyPropertyChangedFor(nameof(OverlayStatusText))]
    private bool _isOverlayVisible;

    [ObservableProperty]
    private bool _isOverlayLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayOpacityPercentText))]
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
    [NotifyPropertyChangedFor(nameof(ShowNormalCpuLoad))]
    [NotifyPropertyChangedFor(nameof(ShowHybridCpuLoad))]
    private bool _showCpuLoad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuClock))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowNormalCpuClock))]
    [NotifyPropertyChangedFor(nameof(ShowHybridCpuClock))]
    private bool _showCpuClock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuPower))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
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
    private bool _showGpuTemp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuLoad))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    private bool _showGpuLoad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowGpuVram))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSection))]
    private bool _showGpuVram;

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

    public string OverlayOpacityPercentText => $"{Math.Round(OverlayOpacity * 100.0):0}%";

    public string OverlayScalePercentText => $"{Math.Round(OverlayScale * 100.0):0}%";

    public string OverlayStatusText => IsOverlayVisible ? "Overlay Ativo" : "Oculto";

    public bool EffectiveOverlayVisible => IsOverlayVisible && (!SmartFullscreenOnly || IsForegroundFullScreen);

    public bool IsVerticalLayout
    {
        get => !IsHorizontalLayout;
        set
        {
            if (value && IsHorizontalLayout)
            {
                IsHorizontalLayout = false;
            }
        }
    }

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

        if (EnableGameHistory)
        {
            ActivateGameHistory();
        }

        ApplyApplicationTheme(AppTheme);
        SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;
        ApplicationThemeManager.Changed += OnWpfUiThemeChanged;

        UpdatePollingAndSmartVisibilityState();

        _hardwareMonitorService.SpecsInitialized += OnHardwareSpecsInitialized;
        _hardwareMonitorService.HeartbeatTick += OnHeartbeatTick;
        _hardwareMonitorService.MetricsUpdated += OnHardwareMetricsUpdated;

        // Chamada explícita para garantir brushes de opacidade e cores 100% atualizados no final da inicialização
        UpdateOverlayBrushes();
    }

    public void SetMainWindowVisible(bool isVisible)
    {
        UpdatePollingAndSmartVisibilityState(mainWindowVisibleOverride: isVisible);
    }

    private void OnSystemUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General &&
            IsAutoThemeSetting(AppTheme))
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                ApplyApplicationTheme("Auto");
                UpdateOverlayBrushes();
                EnsureOverlayTransparentAction?.Invoke();
            }, DispatcherPriority.Background);
        }
    }

    private void OnWpfUiThemeChanged(ApplicationTheme currentApplicationTheme, Color systemAccent)
    {
        if (_isDisposed)
            return;

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_isDisposed)
                return;

            UpdateMonochromeThemeResources(currentApplicationTheme == ApplicationTheme.Light);
            RestoreWindowMonochromeAndTransparency();
            UpdateOverlayBrushes();
            EnsureOverlayTransparentAction?.Invoke();
        }, DispatcherPriority.Send);
    }

    private static bool IsAutoThemeSetting(string? themeSetting) =>
        string.Equals(themeSetting, "Auto", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(themeSetting, "Automático", StringComparison.OrdinalIgnoreCase);

    public static void ApplyApplicationTheme(string? themeSetting)
    {
        try
        {
            bool isAuto = IsAutoThemeSetting(themeSetting);
            ApplicationTheme targetTheme = ApplicationTheme.Dark;

            if (string.Equals(themeSetting, "Light", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(themeSetting, "Claro", StringComparison.OrdinalIgnoreCase))
            {
                targetTheme = ApplicationTheme.Light;
            }
            else if (isAuto)
            {
                targetTheme = IsWindowsInLightTheme() ? ApplicationTheme.Light : ApplicationTheme.Dark;
            }

            // WindowBackdropType.None evita que o WPF-UI aplique Mica/Acrylic e quebre o Alpha do OverlayWindow
            ApplicationThemeManager.Apply(targetTheme, WindowBackdropType.None, false);
            UpdateMonochromeThemeResources(targetTheme == ApplicationTheme.Light);

            if (Application.Current?.MainWindow is SettingsWindow mainSettingsWindow)
            {
                try
                {
                    SystemThemeWatcher.UnWatch(mainSettingsWindow);
                    if (isAuto)
                    {
                        SystemThemeWatcher.Watch(mainSettingsWindow, WindowBackdropType.None, false);
                    }
                }
                catch
                {
                    // Ignora caso o handle da MainWindow ainda não tenha sido criado
                }
            }

            RestoreWindowMonochromeAndTransparency();
        }
        catch
        {
            // Fallback seguro caso a troca de tema ocorra antes da janela estar pronta
        }
    }

    private static void RestoreWindowMonochromeAndTransparency()
    {
        if (Application.Current is null)
            return;

        foreach (Window window in Application.Current.Windows)
        {
            if (window is SettingsWindow settingsWin)
            {
                settingsWin.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "WindowBackgroundBrush");
            }
            else if (window is OverlayWindow overlayWin)
            {
                overlayWin.EnsureTransparentWindow();
            }
        }
    }

    private static void UpdateMonochromeThemeResources(bool isLight)
    {
        ResourceDictionary? resources = Application.Current?.Resources;
        if (resources is null)
            return;

        if (isLight)
        {
            resources["WindowBackgroundBrush"] = LightWindowBackgroundBrush;
            resources["SidebarBackgroundBrush"] = LightSidebarBackgroundBrush;
            resources["CardBackgroundBrush"] = LightCardBackgroundBrush;
            resources["SurfaceHoverBrush"] = LightSurfaceHoverBrush;
            resources["BorderSubtleBrush"] = LightBorderSubtleBrush;
            resources["BorderActiveBrush"] = LightBorderActiveBrush;
            resources["TextPrimaryBrush"] = LightTextPrimaryBrush;
            resources["TextSecondaryBrush"] = LightTextSecondaryBrush;
            resources["TextMutedBrush"] = LightTextMutedBrush;
            resources["AccentPrimaryBrush"] = LightAccentPrimaryBrush;
            resources["AccentForegroundBrush"] = LightAccentForegroundBrush;
        }
        else
        {
            resources["WindowBackgroundBrush"] = DarkWindowBackgroundBrush;
            resources["SidebarBackgroundBrush"] = DarkSidebarBackgroundBrush;
            resources["CardBackgroundBrush"] = DarkCardBackgroundBrush;
            resources["SurfaceHoverBrush"] = DarkSurfaceHoverBrush;
            resources["BorderSubtleBrush"] = DarkBorderSubtleBrush;
            resources["BorderActiveBrush"] = DarkBorderActiveBrush;
            resources["TextPrimaryBrush"] = DarkTextPrimaryBrush;
            resources["TextSecondaryBrush"] = DarkTextSecondaryBrush;
            resources["TextMutedBrush"] = DarkTextMutedBrush;
            resources["AccentPrimaryBrush"] = DarkAccentPrimaryBrush;
            resources["AccentForegroundBrush"] = DarkAccentForegroundBrush;
        }
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

    private static SolidColorBrush CreateFrozenRgbBrush(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public void UpdateOverlayBrushes(bool ensureTransparency = true)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => UpdateOverlayBrushes(ensureTransparency));
            return;
        }

        if (double.IsNaN(OverlayOpacity) || OverlayOpacity <= 0.05 || OverlayOpacity > 1.0)
        {
            OverlayOpacity = 0.80;
        }

        // Mantém o padrão monocromático fosco profundo (#09090B) com canal Alpha guiado pela Opacidade
        byte alpha = (byte)Math.Clamp(OverlayOpacity * 255.0, 20, 255);
        byte borderAlpha = (byte)Math.Clamp((OverlayOpacity * 0.80 + 0.20) * 255.0, 50, 255);

        if (alpha != _lastAppliedBgAlpha || borderAlpha != _lastAppliedBorderAlpha)
        {
            _lastAppliedBgAlpha = alpha;
            _lastAppliedBorderAlpha = borderAlpha;

            OverlayBackgroundBrush = CreateFrozenRgbBrush(9, 9, 11, alpha);
            OverlayBorderBrush = CreateFrozenRgbBrush(39, 39, 42, borderAlpha);
        }

        if (ensureTransparency)
        {
            EnsureOverlayTransparentAction?.Invoke();
        }
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

        double rawOpacity = settings.OverlayOpacity;
        if (double.IsNaN(rawOpacity) || rawOpacity <= 0.05 || rawOpacity > 1.0)
        {
            if (rawOpacity > 1.0 && rawOpacity <= 100.0)
            {
                rawOpacity /= 100.0;
            }
            else
            {
                rawOpacity = 0.80;
            }
        }
        OverlayOpacity = Math.Clamp(Math.Round(rawOpacity, 2), 0.10, 1.0);
        OverlayScale = Math.Clamp(Math.Round(settings.OverlayScale <= 0 ? 1.0 : settings.OverlayScale, 2), 0.70, 2.00);
        OverlayLeft = Math.Round(settings.OverlayLeft);
        OverlayTop = Math.Round(settings.OverlayTop);
        StartWithWindows = settings.StartWithWindows;

        IsCompactMode = settings.IsCompactMode;
        SmartFullscreenOnly = settings.SmartFullscreenOnly;
        IsHorizontalLayout = settings.IsCompactMode || settings.IsHorizontalLayout;

        AppTheme = string.IsNullOrWhiteSpace(settings.AppTheme) ? "Dark" : settings.AppTheme;

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
        EnableGameHistory = settings.EnableGameHistory;

        ModifierKeys overlayMods = settings.ToggleOverlayModifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
        Key overlayKey = settings.ToggleOverlayKey;
        if (overlayMods == ModifierKeys.None || !IsValidHotkeyMainKey(overlayKey))
        {
            overlayMods = DefaultHotkeyModifiers;
            overlayKey = DefaultToggleOverlayKey;
        }

        ModifierKeys compactMods = settings.ToggleCompactModifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
        Key compactKey = settings.ToggleCompactKey;
        if (compactMods == ModifierKeys.None || !IsValidHotkeyMainKey(compactKey) ||
            (compactMods == overlayMods && compactKey == overlayKey))
        {
            compactMods = DefaultHotkeyModifiers;
            compactKey = DefaultToggleCompactKey;
        }

        ToggleOverlayModifiers = overlayMods;
        ToggleOverlayKey = overlayKey;
        ToggleCompactModifiers = compactMods;
        ToggleCompactKey = compactKey;
    }

    private void RefreshFrozenColorBrushes()
    {
        UpdateOverlayBrushes();
        OverlayHeaderBrush = FrozenOverlayHeaderBrush;
        OverlayValueTextBrush = FrozenOverlayValueTextBrush;
        OverlayLabelTextBrush = FrozenOverlayLabelTextBrush;
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
        "FPS" => new OverlaySectionItemViewModel("FPS", "Contador de FPS (Quadros por Segundo)", this),
        "CPU" => new OverlaySectionItemViewModel("CPU", "Processador (Modelo, Uso, Temp, Clock, Watts)", this),
        "GPU" => new OverlaySectionItemViewModel("GPU", "Placa de Vídeo (Modelo, Uso, Temp, VRAM, Clock, Watts)", this),
        "RAM" => new OverlaySectionItemViewModel("RAM", "Memória RAM Física (GB e %)", this),
        _ => new OverlaySectionItemViewModel(key, key, this)
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

    private void OnHeartbeatTick()
    {
        UpdatePollingAndSmartVisibilityState(isHeartbeatTick: true);
    }

    private void UpdatePollingAndSmartVisibilityState(
        bool? mainWindowVisibleOverride = null,
        bool forceImmediateRefresh = false,
        bool isHeartbeatTick = false)
    {
        if (_isDisposed)
            return;

        bool historyEnabled = EnableGameHistory;
        bool hasFullscreenWindow = false;
        ForegroundFullscreenInfo fullscreenInfo = default;

        if (SmartFullscreenOnly || historyEnabled)
        {
            hasFullscreenWindow = WindowWin32Helper.TryGetForegroundFullscreenWindow(out fullscreenInfo);
        }

        bool isFullscreen = !SmartFullscreenOnly || hasFullscreenWindow;
        bool shouldOverlayBeVisible = IsOverlayVisible && isFullscreen;
        bool isMainWindowOpen = mainWindowVisibleOverride ?? _hardwareMonitorService.IsMainWindowVisible;

        _hardwareMonitorService.UpdateMonitoringState(
            isPollingSuspended: !shouldOverlayBeVisible && !isMainWindowOpen,
            isMainWindowVisible: isMainWindowOpen,
            activeDashboardTab: _activeNavTabIndex,
            needCpu: ShowCpuSection,
            needGpu: ShowGpuSection,
            needRam: EffectiveShowRam,
            forceImmediateRefresh: forceImmediateRefresh);

        bool needFpsForHistory = false;
        GameHistoryService? historyService = _gameHistoryService;
        if (historyEnabled && historyService is not null)
        {
            needFpsForHistory = isHeartbeatTick
                ? historyService.ProcessHeartbeatTick(hasFullscreenWindow, fullscreenInfo)
                : historyService.RequiresFpsCapture;
        }

        bool shouldSuspendFps = (!shouldOverlayBeVisible || !EffectiveShowFps) && !isMainWindowOpen && !needFpsForHistory;
        _fpsMonitorService.IsSuspended = shouldSuspendFps;

        if (!shouldSuspendFps)
        {
            _fpsMonitorService.RefreshActiveForegroundProcess();
        }

        if (_lastDispatchedFullscreen != isFullscreen)
        {
            _lastDispatchedFullscreen = isFullscreen;
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (!_isDisposed)
                {
                    IsForegroundFullScreen = isFullscreen;
                }
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

        double cpuVcoreKey = roundedCpuVcore ?? -1.0;
        double gpuVramTotalKey = roundedGpuVramTotal ?? -1.0;

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (_isDisposed)
                return;

            // FPS
            if (currentFps != _prevFps)
            {
                _prevFps = currentFps;
                FpsValueText = currentFps.ToString(CultureInfo.InvariantCulture);
                FpsText = $"{FpsValueText} FPS";
            }

            // CPU
            if (roundedCpuTemp != _prevCpuTemp)
            {
                _prevCpuTemp = roundedCpuTemp;
                CpuTemperatureText = roundedCpuTemp > 0 ? $"{roundedCpuTemp} °C" : "N/D";
            }

            if (roundedCpuLoad != _prevCpuLoad)
            {
                _prevCpuLoad = roundedCpuLoad;
                CpuLoadText = $"{roundedCpuLoad:0}%";
                CpuLoadBarValue = roundedCpuLoad;
            }

            bool cpuClockChanged = roundedCpuClock != _prevCpuClock;
            bool cpuMaxClockChanged = roundedCpuMaxClock != _prevCpuMaxClock;
            if (cpuClockChanged || cpuMaxClockChanged)
            {
                _prevCpuClock = roundedCpuClock;
                _prevCpuMaxClock = roundedCpuMaxClock;

                if (cpuClockChanged)
                {
                    CpuClockText = roundedCpuClock > 0 ? $"{roundedCpuClock} MHz" : "N/D";
                }

                CpuClockAvgMaxText = roundedCpuClock > 0
                    ? (roundedCpuMaxClock > 0
                        ? $"{roundedCpuClock} MHz (Máx: {roundedCpuMaxClock} MHz)"
                        : $"{roundedCpuClock} MHz")
                    : "N/D";
            }

            if (roundedCpuPower != _prevCpuPower)
            {
                _prevCpuPower = roundedCpuPower;
                CpuPowerText = roundedCpuPower > 0.0
                    ? string.Create(CultureInfo.InvariantCulture, $"{roundedCpuPower:0.0} W")
                    : "N/D";
            }

            if (cpuVcoreKey != _prevCpuVcore)
            {
                _prevCpuVcore = cpuVcoreKey;
                CpuVoltageText = cpuVcoreKey > 0.0
                    ? string.Create(CultureInfo.InvariantCulture, $"{cpuVcoreKey:0.000} V")
                    : "N/D";
            }

            if (metrics.IsHybridCpu)
            {
                bool pLoadChanged = roundedPCoreLoad != _prevPCoreLoad;
                bool eLoadChanged = roundedECoreLoad != _prevECoreLoad;

                if (pLoadChanged)
                {
                    _prevPCoreLoad = roundedPCoreLoad;
                    CpuPCoreLoadText = $"{roundedPCoreLoad:0}%";
                }

                if (eLoadChanged)
                {
                    _prevECoreLoad = roundedECoreLoad;
                    CpuECoreLoadText = $"{roundedECoreLoad:0}%";
                }

                if (pLoadChanged || eLoadChanged)
                {
                    CpuHybridCompactLoadText = $"P: {roundedPCoreLoad:0}% | E: {roundedECoreLoad:0}%";
                }

                if (roundedPCoreClock != _prevPCoreClock)
                {
                    _prevPCoreClock = roundedPCoreClock;
                    CpuPCoreClockText = roundedPCoreClock > 0 ? $"{roundedPCoreClock} MHz" : "N/D";
                }

                if (roundedECoreClock != _prevECoreClock)
                {
                    _prevECoreClock = roundedECoreClock;
                    CpuECoreClockText = roundedECoreClock > 0 ? $"{roundedECoreClock} MHz" : "N/D";
                }
            }

            // GPU Primária do Overlay
            if (roundedGpuTemp != _prevGpuTemp)
            {
                _prevGpuTemp = roundedGpuTemp;
                GpuTemperatureText = roundedGpuTemp > 0 ? $"{roundedGpuTemp} °C" : "N/D";
            }

            if (roundedGpuLoad != _prevGpuLoad)
            {
                _prevGpuLoad = roundedGpuLoad;
                GpuLoadText = $"{roundedGpuLoad}%";
            }

            if (roundedGpuClock != _prevGpuClock)
            {
                _prevGpuClock = roundedGpuClock;
                GpuClockText = roundedGpuClock > 0 ? $"{roundedGpuClock} MHz" : "N/D";
            }

            if (roundedGpuPower != _prevGpuPower)
            {
                _prevGpuPower = roundedGpuPower;
                GpuPowerText = roundedGpuPower > 0.0
                    ? string.Create(CultureInfo.InvariantCulture, $"{roundedGpuPower:0.0} W")
                    : "N/D";
            }

            if (roundedGpuVramUsed != _prevGpuVramUsed || gpuVramTotalKey != _prevGpuVramTotal)
            {
                _prevGpuVramUsed = roundedGpuVramUsed;
                _prevGpuVramTotal = gpuVramTotalKey;

                if (roundedGpuVramUsed > 0.0)
                {
                    if (gpuVramTotalKey > 0.0)
                    {
                        int vramPct = (int)Math.Clamp(Math.Round((roundedGpuVramUsed / gpuVramTotalKey) * 100.0), 0.0, 100.0);
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
            }

            // Memória RAM
            bool ramUsedChanged = roundedRamUsed != _prevRamUsed;
            bool ramTotalChanged = roundedRamTotal != _prevRamTotal;
            bool ramFreeChanged = roundedRamFree != _prevRamFree;
            bool ramPctChanged = roundedRamPct != _prevRamPct;

            if (ramUsedChanged || ramPctChanged)
            {
                RamUsageText = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{roundedRamUsed:0.0} GB ({roundedRamPct:0}%)");
            }

            if (ramUsedChanged)
            {
                _prevRamUsed = roundedRamUsed;
                RamUsedText = string.Create(CultureInfo.InvariantCulture, $"{roundedRamUsed:0.0} GB");
            }

            if (ramPctChanged)
            {
                _prevRamPct = roundedRamPct;
                RamPercentText = $"{roundedRamPct:0}%";
                RamUsageBarValue = roundedRamPct;
            }

            if (ramTotalChanged)
            {
                _prevRamTotal = roundedRamTotal;
                if (roundedRamTotal > 0.0)
                {
                    RamTotalText = string.Create(CultureInfo.InvariantCulture, $"{roundedRamTotal:0.0} GB");
                }
            }

            if (ramFreeChanged)
            {
                _prevRamFree = roundedRamFree;
                if (roundedRamTotal > 0.0)
                {
                    RamFreeText = string.Create(CultureInfo.InvariantCulture, $"{roundedRamFree:0.0} GB");
                }
            }

            if (roundedRamTotal > 0.0 && (ramUsedChanged || ramTotalChanged || ramFreeChanged))
            {
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

    private void SyncCpuCoresCollection(List<CpuCoreSnapshot>? incoming)
    {
        if (incoming is null)
            return;

        if (CpuCores.Count == incoming.Count)
        {
            for (int i = 0; i < incoming.Count; i++)
            {
                CpuCores[i].UpdateFrom(incoming[i]);
            }
            return;
        }

        CpuCores.Clear();
        for (int i = 0; i < incoming.Count; i++)
        {
            CpuCores.Add(CpuCoreMetric.FromSnapshot(incoming[i]));
        }
    }

    private void SyncGpusCollection(List<GpuDetailedSnapshot>? incoming)
    {
        if (incoming is null || incoming.Count == 0)
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
        for (int i = 0; i < incoming.Count; i++)
        {
            Gpus.Add(GpuDetailedMetrics.FromSnapshot(incoming[i]));
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

    private void SyncStorageCollections(List<StorageDriveSnapshot>? drives, List<PhysicalStorageSnapshot>? physicalDisks)
    {
        if (drives is not null)
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
                for (int i = 0; i < drives.Count; i++)
                {
                    StorageDrives.Add(StorageDriveMetrics.FromSnapshot(drives[i]));
                }
            }
        }

        if (physicalDisks is not null)
        {
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
                for (int i = 0; i < physicalDisks.Count; i++)
                {
                    PhysicalDisks.Add(PhysicalStorageMetrics.FromSnapshot(physicalDisks[i]));
                }
            }
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

    [RelayCommand]
    private void StartRecordingOverlayHotkey()
    {
        if (IsRecordingOverlayHotkey)
        {
            CancelHotkeyRecording();
            return;
        }

        IsRecordingCompactHotkey = false;
        IsRecordingOverlayHotkey = true;
        OverlayHotkeyErrorText = string.Empty;
        SuspendHotkeysAction?.Invoke();
    }

    [RelayCommand]
    private void StartRecordingCompactHotkey()
    {
        if (IsRecordingCompactHotkey)
        {
            CancelHotkeyRecording();
            return;
        }

        IsRecordingOverlayHotkey = false;
        IsRecordingCompactHotkey = true;
        CompactHotkeyErrorText = string.Empty;
        SuspendHotkeysAction?.Invoke();
    }

    [RelayCommand]
    private void ResetOverlayHotkey()
    {
        IsRecordingOverlayHotkey = false;
        IsRecordingCompactHotkey = false;

        if (ToggleCompactModifiers == DefaultHotkeyModifiers && ToggleCompactKey == DefaultToggleOverlayKey)
        {
            OverlayHotkeyErrorText = "Conflito: Ctrl + Shift + O já está em uso no Modo Compacto.";
            ReloadHotkeysProvider?.Invoke();
            return;
        }

        ToggleOverlayModifiers = DefaultHotkeyModifiers;
        ToggleOverlayKey = DefaultToggleOverlayKey;
        OverlayHotkeyErrorText = string.Empty;
        SaveSettings();
        ReloadGlobalHotkeys();
    }

    [RelayCommand]
    private void ResetCompactHotkey()
    {
        IsRecordingOverlayHotkey = false;
        IsRecordingCompactHotkey = false;

        if (ToggleOverlayModifiers == DefaultHotkeyModifiers && ToggleOverlayKey == DefaultToggleCompactKey)
        {
            CompactHotkeyErrorText = "Conflito: Ctrl + Shift + C já está em uso no Overlay.";
            ReloadHotkeysProvider?.Invoke();
            return;
        }

        ToggleCompactModifiers = DefaultHotkeyModifiers;
        ToggleCompactKey = DefaultToggleCompactKey;
        CompactHotkeyErrorText = string.Empty;
        SaveSettings();
        ReloadGlobalHotkeys();
    }

    public void ReloadGlobalHotkeys()
    {
        if (ReloadHotkeysProvider is null)
            return;

        (bool overlayOk, bool compactOk) = ReloadHotkeysProvider.Invoke();
        OverlayHotkeyErrorText = overlayOk
            ? string.Empty
            : "Atalho indisponível (em uso por outro programa ou pelo sistema).";
        CompactHotkeyErrorText = compactOk
            ? string.Empty
            : "Atalho indisponível (em uso por outro programa ou pelo sistema).";
    }

    public void CancelHotkeyRecording()
    {
        if (!IsRecordingOverlayHotkey && !IsRecordingCompactHotkey)
            return;

        IsRecordingOverlayHotkey = false;
        IsRecordingCompactHotkey = false;
        ReloadGlobalHotkeys();
    }

    public void TryApplyRecordedHotkey(ModifierKeys rawModifiers, Key key)
    {
        bool recordingOverlay = IsRecordingOverlayHotkey;
        bool recordingCompact = IsRecordingCompactHotkey;
        if (!recordingOverlay && !recordingCompact)
            return;

        ModifierKeys cleanModifiers = rawModifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
        if (cleanModifiers == ModifierKeys.None || !IsValidHotkeyMainKey(key))
        {
            if (recordingOverlay)
            {
                OverlayHotkeyErrorText = "Combine pelo menos Ctrl, Alt ou Shift com uma tecla válida.";
            }
            else
            {
                CompactHotkeyErrorText = "Combine pelo menos Ctrl, Alt ou Shift com uma tecla válida.";
            }

            IsRecordingOverlayHotkey = false;
            IsRecordingCompactHotkey = false;
            ReloadHotkeysProvider?.Invoke();
            return;
        }

        if (recordingOverlay && cleanModifiers == ToggleCompactModifiers && key == ToggleCompactKey)
        {
            OverlayHotkeyErrorText = "Este atalho já está atribuído ao Modo Compacto.";
            IsRecordingOverlayHotkey = false;
            ReloadHotkeysProvider?.Invoke();
            return;
        }

        if (recordingCompact && cleanModifiers == ToggleOverlayModifiers && key == ToggleOverlayKey)
        {
            CompactHotkeyErrorText = "Este atalho já está atribuído ao Overlay.";
            IsRecordingCompactHotkey = false;
            ReloadHotkeysProvider?.Invoke();
            return;
        }

        if (recordingOverlay)
        {
            ToggleOverlayModifiers = cleanModifiers;
            ToggleOverlayKey = key;
            OverlayHotkeyErrorText = string.Empty;
        }
        else
        {
            ToggleCompactModifiers = cleanModifiers;
            ToggleCompactKey = key;
            CompactHotkeyErrorText = string.Empty;
        }

        IsRecordingOverlayHotkey = false;
        IsRecordingCompactHotkey = false;
        SaveSettings();
        ReloadGlobalHotkeys();
    }

    public static bool IsModifierKeyOnly(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin;

    public static bool IsValidHotkeyMainKey(Key key)
    {
        if (IsModifierKeyOnly(key) ||
            key is Key.None or Key.Escape or Key.DeadCharProcessed or Key.ImeProcessed or Key.System)
        {
            return false;
        }

        return KeyInterop.VirtualKeyFromKey(key) != 0;
    }

    public static string FormatHotkeyText(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");

        parts.Add(FormatKeyName(key));
        return string.Join(" + ", parts);
    }

    private static string FormatKeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num{key - Key.NumPad0}",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        _ => key.ToString()
    };

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
            OverlayOpacity = Math.Clamp(Math.Round(OverlayOpacity, 2), 0.1, 1.0),
            OverlayScale = Math.Clamp(Math.Round(OverlayScale, 2), 0.70, 2.00),
            OverlayLeft = OverlayLeft,
            OverlayTop = OverlayTop,
            StartWithWindows = StartWithWindows,
            ToggleOverlayModifiers = ToggleOverlayModifiers,
            ToggleOverlayKey = ToggleOverlayKey,
            ToggleCompactModifiers = ToggleCompactModifiers,
            ToggleCompactKey = ToggleCompactKey,
            IsCompactMode = IsCompactMode,
            SmartFullscreenOnly = SmartFullscreenOnly,
            IsHorizontalLayout = IsHorizontalLayout,
            AppTheme = AppTheme,
            OverlayBackgroundColorHex = DefaultBackgroundColorHex,
            OverlayBorderColorHex = DefaultBorderColorHex,
            OverlayHeaderColorHex = DefaultHeaderColorHex,
            OverlayValueTextColorHex = DefaultValueTextColorHex,
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
            ShowRam = ShowRam,
            EnableGameHistory = EnableGameHistory
        };

        _settingsService.Save(settings);
    }

    private void ActivateGameHistory()
    {
        if (_isDisposed || _gameHistoryService is not null)
            return;

        _gameHistoryService = new GameHistoryService(_fpsMonitorService);
        GameHistory = new GameHistoryViewModel(_gameHistoryService);
    }

    private void DeactivateGameHistory()
    {
        if (GameHistory is not null)
        {
            GameHistory.Dispose();
            GameHistory = null;
        }

        if (_gameHistoryService is not null)
        {
            _gameHistoryService.Dispose();
            _gameHistoryService = null;
        }
    }

    partial void OnEnableGameHistoryChanged(bool value)
    {
        if (_isInitializing)
            return;

        if (value)
        {
            ActivateGameHistory();
        }
        else
        {
            if (SelectedNavIndex == 5)
            {
                SelectedNavIndex = 0;
            }

            DeactivateGameHistory();
        }

        UpdatePollingAndSmartVisibilityState();
        SaveSettings();
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        _activeNavTabIndex = value;
        CancelHotkeyRecording();

        if (!_isInitializing)
        {
            UpdatePollingAndSmartVisibilityState(forceImmediateRefresh: value is >= 0 and <= 3 && _hardwareMonitorService.IsMainWindowVisible);

            if (value == 5 && GameHistory is not null)
            {
                _ = GameHistory.EnsureLoadedAsync();
            }
        }
    }

    partial void OnSelectedSettingsSubTabIndexChanged(int value) => CancelHotkeyRecording();

    partial void OnAppThemeChanged(string value)
    {
        if (!_isInitializing)
        {
            ApplyApplicationTheme(value);
            UpdateOverlayBrushes();
            EnsureOverlayTransparentAction?.Invoke();
        }
        SaveSettings();
    }

    partial void OnIsOverlayVisibleChanged(bool value)
    {
        if (!_isInitializing)
        {
            UpdatePollingAndSmartVisibilityState();
        }
        SaveSettings();
    }

    partial void OnIsOverlayLockedChanged(bool value) => SaveSettings();

    partial void OnOverlayOpacityChanged(double value)
    {
        if (double.IsNaN(value) || value <= 0.05 || value > 1.0)
        {
            OverlayOpacity = 0.80;
            return;
        }

        UpdateOverlayBrushes(ensureTransparency: false);
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

        if (!_isInitializing)
        {
            UpdatePollingAndSmartVisibilityState();
        }
        SaveSettings();
    }

    partial void OnSmartFullscreenOnlyChanged(bool value)
    {
        if (!_isInitializing)
        {
            UpdatePollingAndSmartVisibilityState();
        }
        SaveSettings();
    }

    partial void OnIsHorizontalLayoutChanged(bool value) => SaveSettings();

    partial void OnShowFpsChanged(bool value) => OnSensorVisibilityToggled();
    partial void OnShowCpuTempChanged(bool value) => OnSensorVisibilityToggled();
    partial void OnShowCpuLoadChanged(bool value) => OnSensorVisibilityToggled();
    partial void OnShowCpuClockChanged(bool value) => OnSensorVisibilityToggled();
    partial void OnShowCpuPowerChanged(bool value) => OnSensorVisibilityToggled();
    partial void OnSplitIntelHybridCoresChanged(bool value)
    {
        _hardwareMonitorService.SplitIntelHybridCores = value;
        SaveSettings();
    }
    partial void OnShowGpuTempChanged(bool value) => OnSensorVisibilityToggled();
    partial void OnShowGpuLoadChanged(bool value) => OnSensorVisibilityToggled();
    partial void OnShowGpuVramChanged(bool value) => OnSensorVisibilityToggled();
    partial void OnShowGpuClockChanged(bool value) => OnSensorVisibilityToggled();
    partial void OnShowGpuPowerChanged(bool value) => OnSensorVisibilityToggled();
    partial void OnShowRamChanged(bool value) => OnSensorVisibilityToggled();

    private void OnSensorVisibilityToggled()
    {
        if (!_isInitializing)
        {
            UpdatePollingAndSmartVisibilityState();
        }
        SaveSettings();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        SystemEvents.UserPreferenceChanged -= OnSystemUserPreferenceChanged;
        ApplicationThemeManager.Changed -= OnWpfUiThemeChanged;

        try
        {
            if (Application.Current?.MainWindow is SettingsWindow mainSettingsWindow)
            {
                SystemThemeWatcher.UnWatch(mainSettingsWindow);
            }
        }
        catch
        {
            // Ignora falhas de limpeza do watcher ao encerrar
        }

        _hardwareMonitorService.SpecsInitialized -= OnHardwareSpecsInitialized;
        _hardwareMonitorService.HeartbeatTick -= OnHeartbeatTick;
        _hardwareMonitorService.MetricsUpdated -= OnHardwareMetricsUpdated;

        DeactivateGameHistory();
    }
}

/// <summary>
/// Representa um bloco reordenável do Overlay (FPS, CPU, GPU ou RAM).
/// </summary>
public partial class OverlaySectionItemViewModel : ObservableObject
{
    public string Key { get; }
    public string DisplayTitle { get; }
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

    public OverlaySectionItemViewModel(string key, string displayTitle, MainViewModel parent)
    {
        Key = key;
        DisplayTitle = displayTitle;
        Parent = parent;
    }
}
