namespace HardwareOverlayApp.Models;

public class HardwareMetrics
{
    // FPS
    public int CurrentFps { get; set; }

    // CPU (Overlay + Global)
    public float CpuTemperature { get; set; }
    public float CpuLoad { get; set; }
    public float CpuClock { get; set; }
    public float CpuMaxClock { get; set; }
    public float CpuPower { get; set; }
    public float? CpuVoltageVcore { get; set; }

    // CPU Híbrida Intel (P-Cores vs E-Cores)
    public bool IsHybridCpu { get; set; }
    public float CpuPCoreLoad { get; set; }
    public float CpuECoreLoad { get; set; }
    public float CpuPCoreClock { get; set; }
    public float CpuECoreClock { get; set; }

    // GPU Primária (para o Overlay flutuante)
    public float GpuTemperature { get; set; }
    public float GpuLoad { get; set; }
    public float GpuClock { get; set; }
    public float GpuPower { get; set; }
    public float GpuVramUsedGb { get; set; }
    public float? GpuVramTotalGb { get; set; }

    // RAM
    public float RamUsagePercent { get; set; }
    public float RamUsedGb { get; set; }
    public float RamTotalGb { get; set; }
    public float RamFreeGb { get; set; }

    // Indica se este ciclo incluiu a telemetria detalhada para a janela principal
    public bool IsDetailedSnapshot { get; set; }

    // Snapshots value-type alocados apenas para as coleções visíveis quando a janela principal está aberta
    public List<CpuCoreSnapshot>? CpuCores { get; set; }
    public List<GpuDetailedSnapshot>? Gpus { get; set; }
    public List<StorageDriveSnapshot>? StorageDrives { get; set; }
    public List<PhysicalStorageSnapshot>? PhysicalDisks { get; set; }
}
