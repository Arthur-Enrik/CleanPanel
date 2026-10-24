namespace HardwareOverlayApp.Models;

public class HardwareMetrics
{
    // FPS
    public int CurrentFps { get; set; }

    // CPU
    public float CpuTemperature { get; set; }
    public float CpuLoad { get; set; }
    public float CpuClock { get; set; }
    public float CpuPower { get; set; }

    // GPU
    public float GpuTemperature { get; set; }
    public float GpuLoad { get; set; }
    public float GpuClock { get; set; }
    public float GpuPower { get; set; }

    // RAM
    public float RamUsagePercent { get; set; }
    public float RamUsedGb { get; set; }
}
