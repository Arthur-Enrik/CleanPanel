namespace HardwareOverlayApp.Models;

public class AppSettings
{
    public bool IsOverlayVisible { get; set; } = true;
    public bool IsOverlayLocked { get; set; } = false;
    public double OverlayOpacity { get; set; } = 0.85;
    public double OverlayLeft { get; set; } = 50.0;
    public double OverlayTop { get; set; } = 50.0;
    public bool StartWithWindows { get; set; } = false;

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
    public bool ShowGpuClock { get; set; } = true;
    public bool ShowGpuPower { get; set; } = true;

    // RAM
    public bool ShowRam { get; set; } = true;
}
