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

    // Telemetria detalhada (atualizada apenas quando a janela principal está aberta e visível)
    public List<CpuCoreMetric> CpuCores { get; set; } = [];
    public List<GpuDetailedMetrics> Gpus { get; set; } = [];
    public List<StorageDriveMetrics> StorageDrives { get; set; } = [];
    public List<PhysicalStorageMetrics> PhysicalDisks { get; set; } = [];
}
