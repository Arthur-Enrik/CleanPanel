namespace HardwareOverlayApp.Models;

public class AppSettings
{
    public bool IsOverlayVisible { get; set; } = true;
    public bool IsOverlayLocked { get; set; } = false;
    public double OverlayOpacity { get; set; } = 0.85;
    public double OverlayScale { get; set; } = 1.0;
    public double OverlayLeft { get; set; } = 50.0;
    public double OverlayTop { get; set; } = 50.0;
    public bool StartWithWindows { get; set; } = false;

    // Personalização avançada e exibição inteligente
    public bool IsCompactMode { get; set; } = false;
    public bool SmartFullscreenOnly { get; set; } = false;
    public bool IsHorizontalLayout { get; set; } = false;

    // Tema do Aplicativo: "Dark", "Light" ou "Auto"
    public string AppTheme { get; set; } = "Dark";

    // Personalização de Cores do Overlay e Destaque (Hex)
    public string AccentColorHex { get; set; } = "#FAFAFA";
    public string OverlayBackgroundColorHex { get; set; } = "#09090B";
    public string OverlayBorderColorHex { get; set; } = "#2E2E32";
    public string OverlayHeaderColorHex { get; set; } = "#FAFAFA";
    public string OverlayValueTextColorHex { get; set; } = "#FAFAFA";

    // Alertas Térmicos de Temperatura (CPU / GPU)
    public bool EnableThermalColorAlerts { get; set; } = true;
    public double TempWarningThreshold { get; set; } = 70.0;
    public double TempCriticalThreshold { get; set; } = 85.0;

    // Atalhos Globais Configuráveis (Win32 RegisterHotKey: MOD_CONTROL=2, MOD_SHIFT=4, VK_O=0x4F, VK_C=0x43)
    public uint ToggleOverlayHotkeyModifiers { get; set; } = 0x0002 | 0x0004;
    public uint ToggleOverlayHotkeyKey { get; set; } = 0x4F;
    public string ToggleOverlayHotkeyDisplay { get; set; } = "Ctrl + Shift + O";

    public uint ToggleCompactModeHotkeyModifiers { get; set; } = 0x0002 | 0x0004;
    public uint ToggleCompactModeHotkeyKey { get; set; } = 0x43;
    public string ToggleCompactModeHotkeyDisplay { get; set; } = "Ctrl + Shift + C";

    // Ordem personalizável dos blocos de sensores no Overlay (padrão: CPU -> GPU -> RAM -> FPS)
    public List<string> OverlaySectionOrder { get; set; } = ["CPU", "GPU", "RAM", "FPS"];

    // FPS
    public bool ShowFps { get; set; } = true;

    // CPU
    public bool ShowCpuTemp { get; set; } = true;
    public bool ShowCpuLoad { get; set; } = true;
    public bool ShowCpuClock { get; set; } = true;
    public bool ShowCpuPower { get; set; } = true;
    public bool SplitIntelHybridCores { get; set; } = false;

    // GPU
    public bool ShowGpuTemp { get; set; } = true;
    public bool ShowGpuLoad { get; set; } = true;
    public bool ShowGpuVram { get; set; } = true;
    public bool ShowGpuClock { get; set; } = true;
    public bool ShowGpuPower { get; set; } = true;

    // RAM
    public bool ShowRam { get; set; } = true;
}
