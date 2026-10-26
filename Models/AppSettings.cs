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

    // Personalização de Cores do Overlay (Hex)
    public string OverlayBackgroundColorHex { get; set; } = "#141418";
    public string OverlayBorderColorHex { get; set; } = "#33FFFFFF";
    public string OverlayHeaderColorHex { get; set; } = "#00E5FF";
    public string OverlayValueTextColorHex { get; set; } = "#FFFFFF";

    // Ordem personalizável dos blocos de sensores no Overlay (padrão: CPU -> GPU -> RAM -> FPS)
    public List<string> OverlaySectionOrder { get; set; } = ["CPU", "GPU", "RAM", "FPS"];

    // FPS
    public bool ShowFps { get; set; } = true;

    // CPU
    public bool ShowCpuTemp { get; set; } = true;
    public bool ShowCpuLoad { get; set; } = true;
    public bool ShowCpuClock { get; set; } = true;
    public bool ShowCpuPower { get; set; } = true;

    // GPU
    public bool ShowGpuTemp { get; set; } = true;
    public bool ShowGpuLoad { get; set; } = true;
    public bool ShowGpuVram { get; set; } = true;
    public bool ShowGpuClock { get; set; } = true;
    public bool ShowGpuPower { get; set; } = true;

    // RAM
    public bool ShowRam { get; set; } = true;
}
