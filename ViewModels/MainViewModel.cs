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
    public const string DefaultBackgroundColorHex = "#141418";
    public const string DefaultBorderColorHex = "#33FFFFFF";
    public const string DefaultHeaderColorHex = "#00E5FF";
    public const string DefaultValueTextColorHex = "#FFFFFF";

    private static readonly string[] DefaultSectionKeys = ["FPS", "CPU", "GPU", "RAM"];

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

    // ==================== CORES PERSONALIZADAS DO OVERLAY (COM BRUSHES CONGELADOS) ====================
    [ObservableProperty]
    private string _overlayBackgroundColorHex = DefaultBackgroundColorHex;

    [ObservableProperty]
    private string _overlayBorderColorHex = DefaultBorderColorHex;

    [ObservableProperty]
    private string _overlayHeaderColorHex = DefaultHeaderColorHex;

    [ObservableProperty]
    private string _overlayValueTextColorHex = DefaultValueTextColorHex;

    [ObservableProperty]
    private SolidColorBrush _overlayBackgroundBrush = CreateFrozenBrush("#D9141418", Color.FromArgb(216, 20, 20, 24));

    [ObservableProperty]
    private SolidColorBrush _overlayBorderBrush = CreateFrozenBrush(DefaultBorderColorHex, Color.FromArgb(51, 255, 255, 255));

    [ObservableProperty]
    private SolidColorBrush _overlayHeaderBrush = CreateFrozenBrush(DefaultHeaderColorHex, Color.FromRgb(0, 229, 255));

    [ObservableProperty]
    private SolidColorBrush _overlayValueTextBrush = CreateFrozenBrush(DefaultValueTextColorHex, Colors.White);

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
    private bool _showCpuLoad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuClock))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow2))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow2Pipe))]
    private bool _showCpuClock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveShowCpuPower))]
    [NotifyPropertyChangedFor(nameof(ShowCpuSection))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow2))]
    [NotifyPropertyChangedFor(nameof(ShowCpuRow2Pipe))]
    private bool _showCpuPower;

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

        _isInitializing = true;
        AppSettings settings = _settingsService.Load();
        ApplySettings(settings);
        _isInitializing = false;

        ApplyApplicationTheme(AppTheme);
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
                ApplyApplicationTheme("Auto");
            }, DispatcherPriority.Background);
        }
    }

    public static void ApplyApplicationTheme(string? themeSetting)
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

            ApplicationThemeManager.Apply(targetTheme);
        }
        catch
        {
            // Fallback seguro caso a troca de tema ocorra antes da janela estar pronta
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

    public void UpdateOverlayBrushes()
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(UpdateOverlayBrushes);
            return;
        }

        if (double.IsNaN(OverlayOpacity) || OverlayOpacity <= 0.05 || OverlayOpacity > 1.0)
        {
            OverlayOpacity = 0.80;
        }

        Color baseColor = ParseHexColor(OverlayBackgroundColorHex, Color.FromRgb(20, 20, 24));
        byte alpha = (byte)Math.Clamp(OverlayOpacity * 255.0, 20, 255);
        Color finalBgColor = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);

        var bgBrush = new SolidColorBrush(finalBgColor);
        bgBrush.Freeze();
        OverlayBackgroundBrush = bgBrush;

        Color baseBorderColor = ParseHexColor(OverlayBorderColorHex, Color.FromArgb(51, 255, 255, 255));
        double borderRatio = baseBorderColor.A >= 250 ? 0.35 : (baseBorderColor.A / 255.0);
        byte borderAlpha = (byte)Math.Clamp(alpha * Math.Max(0.18, borderRatio), 15, 255);
        Color finalBorderColor = Color.FromArgb(borderAlpha, baseBorderColor.R, baseBorderColor.G, baseBorderColor.B);

        var borderBrush = new SolidColorBrush(finalBorderColor);
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

        OverlayBackgroundColorHex = string.IsNullOrWhiteSpace(settings.OverlayBackgroundColorHex)
            ? DefaultBackgroundColorHex
            : settings.OverlayBackgroundColorHex;
        OverlayBorderColorHex = string.IsNullOrWhiteSpace(settings.OverlayBorderColorHex)
            ? DefaultBorderColorHex
            : settings.OverlayBorderColorHex;
        OverlayHeaderColorHex = string.IsNullOrWhiteSpace(settings.OverlayHeaderColorHex)
            ? DefaultHeaderColorHex
            : settings.OverlayHeaderColorHex;
        OverlayValueTextColorHex = string.IsNullOrWhiteSpace(settings.OverlayValueTextColorHex)
            ? DefaultValueTextColorHex
            : settings.OverlayValueTextColorHex;

        RefreshFrozenColorBrushes();
        InitializeOverlaySectionsOrder(settings.OverlaySectionOrder);

        ShowFps = settings.ShowFps;

        ShowCpuTemp = settings.ShowCpuTemp;
        ShowCpuLoad = settings.ShowCpuLoad;
        ShowCpuClock = settings.ShowCpuClock;
        ShowCpuPower = settings.ShowCpuPower;

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
        OverlayHeaderBrush = CreateFrozenBrush(OverlayHeaderColorHex, Color.FromRgb(0, 229, 255));
        OverlayValueTextBrush = CreateFrozenBrush(OverlayValueTextColorHex, Colors.White);
    }

    private void InitializeOverlaySectionsOrder(List<string>? savedOrder)
    {
        OverlaySections.Clear();

        var orderedKeys = new List<string>();
        if (savedOrder is not null)
        {
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

            // CPU
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
            OverlayOpacity = Math.Clamp(Math.Round(OverlayOpacity, 2), 0.1, 1.0),
            OverlayScale = Math.Clamp(Math.Round(OverlayScale, 2), 0.70, 2.00),
            OverlayLeft = OverlayLeft,
            OverlayTop = OverlayTop,
            StartWithWindows = StartWithWindows,
            IsCompactMode = IsCompactMode,
            SmartFullscreenOnly = SmartFullscreenOnly,
            IsHorizontalLayout = IsHorizontalLayout,
            AppTheme = AppTheme,
            OverlayBackgroundColorHex = OverlayBackgroundColorHex,
            OverlayBorderColorHex = OverlayBorderColorHex,
            OverlayHeaderColorHex = OverlayHeaderColorHex,
            OverlayValueTextColorHex = OverlayValueTextColorHex,
            OverlaySectionOrder = OverlaySections.Select(s => s.Key).ToList(),
            ShowFps = ShowFps,
            ShowCpuTemp = ShowCpuTemp,
            ShowCpuLoad = ShowCpuLoad,
            ShowCpuClock = ShowCpuClock,
            ShowCpuPower = ShowCpuPower,
            ShowGpuTemp = ShowGpuTemp,
            ShowGpuLoad = ShowGpuLoad,
            ShowGpuVram = ShowGpuVram,
            ShowGpuClock = ShowGpuClock,
            ShowGpuPower = ShowGpuPower,
            ShowRam = ShowRam
        };

        _settingsService.Save(settings);
    }

    partial void OnAppThemeChanged(string value)
    {
        if (!_isInitializing)
        {
            ApplyApplicationTheme(value);
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
        OverlayHeaderBrush = CreateFrozenBrush(value, Color.FromRgb(0, 229, 255));
        SaveSettings();
    }

    partial void OnOverlayValueTextColorHexChanged(string value)
    {
        OverlayValueTextBrush = CreateFrozenBrush(value, Colors.White);
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
        if (double.IsNaN(value) || value <= 0.05 || value > 1.0)
        {
            OverlayOpacity = 0.80;
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
        if (value)
        {
            IsHorizontalLayout = true;
            OnPropertyChanged(nameof(IsHorizontalLayout));
        }

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
