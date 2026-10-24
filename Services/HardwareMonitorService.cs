using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.Models;
using LibreHardwareMonitor.Hardware;

namespace HardwareOverlayApp.Services;

public class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _updateVisitor;
    private readonly CancellationTokenSource _cts = new();
    private Task? _monitoringTask;
    private bool _isDisposed;

    public event Action<HardwareMetrics>? MetricsUpdated;

    public HardwareMonitorService()
    {
        _updateVisitor = new UpdateVisitor();

        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true
        };
    }

    public void Start()
    {
        try
        {
            _computer.Open();
        }
        catch
        {
            // Mantém o serviço ativo mesmo se um sensor específico falhar na abertura
        }

        _monitoringTask = Task.Run(() => RunMonitoringLoopAsync(_cts.Token));
    }

    private async Task RunMonitoringLoopAsync(CancellationToken cancellationToken)
    {
        // Leitura imediata inicial antes do primeiro ciclo de 1s
        ReadAndPublishMetrics();

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                ReadAndPublishMetrics();
            }
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal solicitado pelo CancellationToken
        }
    }

    private void ReadAndPublishMetrics()
    {
        if (_isDisposed)
            return;

        try
        {
            _computer.Accept(_updateVisitor);

            var metrics = new HardwareMetrics();

            foreach (IHardware hardware in _computer.Hardware)
            {
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        ReadCpuMetrics(hardware, metrics);
                        break;

                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        ReadGpuMetrics(hardware, metrics);
                        break;

                    case HardwareType.Memory:
                        ReadRamMetrics(hardware, metrics);
                        break;
                }
            }

            // Fallback de temperatura da CPU na placa-mãe caso o processador não exponha sensor próprio
            if (metrics.CpuTemperature <= 0f)
            {
                foreach (IHardware hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.Motherboard)
                    {
                        ReadMotherboardCpuTempFallback(hardware, metrics);
                    }
                }
            }

            MetricsUpdated?.Invoke(metrics);
        }
        catch
        {
            // Ignora falhas transitórias na leitura dos sensores
        }
    }

    private static void ReadCpuMetrics(IHardware cpu, HardwareMetrics metrics)
    {
        float? preferredTemp = null;
        float firstTemp = 0f;
        float clockSum = 0f;
        int clockCount = 0;

        foreach (ISensor sensor in cpu.Sensors)
        {
            float value = sensor.Value ?? 0f;
            if (value <= 0f)
                continue;

            switch (sensor.SensorType)
            {
                case SensorType.Temperature:
                    if (firstTemp <= 0f)
                    {
                        firstTemp = value;
                    }

                    if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                        sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                        sensor.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
                        sensor.Name.Contains("Core Average", StringComparison.OrdinalIgnoreCase))
                    {
                        preferredTemp = value;
                    }
                    break;

                case SensorType.Load:
                    if (sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                    {
                        metrics.CpuLoad = value;
                    }
                    break;

                case SensorType.Clock:
                    if (!sensor.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase))
                    {
                        clockSum += value;
                        clockCount++;
                    }
                    break;

                case SensorType.Power:
                    if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                        metrics.CpuPower <= 0f)
                    {
                        metrics.CpuPower = value;
                    }
                    break;
            }
        }

        metrics.CpuTemperature = preferredTemp ?? firstTemp;

        if (clockCount > 0)
        {
            metrics.CpuClock = clockSum / clockCount;
        }
    }

    private static void ReadGpuMetrics(IHardware gpu, HardwareMetrics metrics)
    {
        foreach (ISensor sensor in gpu.Sensors)
        {
            float value = sensor.Value ?? 0f;
            if (value <= 0f)
                continue;

            switch (sensor.SensorType)
            {
                case SensorType.Temperature:
                    if (sensor.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) ||
                        sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                        metrics.GpuTemperature <= 0f)
                    {
                        metrics.GpuTemperature = value;
                    }
                    break;

                case SensorType.Load:
                    if (sensor.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) ||
                        (sensor.Name.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase) && metrics.GpuLoad <= 0f) ||
                        metrics.GpuLoad <= 0f)
                    {
                        metrics.GpuLoad = value;
                    }
                    break;

                case SensorType.Clock:
                    if (sensor.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) ||
                        sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                        metrics.GpuClock <= 0f)
                    {
                        metrics.GpuClock = value;
                    }
                    break;

                case SensorType.Power:
                    if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                        sensor.Name.Contains("Power", StringComparison.OrdinalIgnoreCase) ||
                        sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                        metrics.GpuPower <= 0f)
                    {
                        metrics.GpuPower = value;
                    }
                    break;
            }
        }
    }

    private static void ReadRamMetrics(IHardware memory, HardwareMetrics metrics)
    {
        if (memory.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
            return;

        foreach (ISensor sensor in memory.Sensors)
        {
            float value = sensor.Value ?? 0f;

            if (sensor.SensorType == SensorType.Load &&
                sensor.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase))
            {
                metrics.RamUsagePercent = value;
            }
            else if (sensor.SensorType == SensorType.Data &&
                     sensor.Name.Equals("Memory Used", StringComparison.OrdinalIgnoreCase))
            {
                metrics.RamUsedGb = value;
            }
        }
    }

    private static void ReadMotherboardCpuTempFallback(IHardware motherboard, HardwareMetrics metrics)
    {
        foreach (ISensor sensor in motherboard.Sensors)
        {
            float value = sensor.Value ?? 0f;
            if (sensor.SensorType == SensorType.Temperature &&
                value > 0f &&
                sensor.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            {
                metrics.CpuTemperature = value;
                return;
            }
        }

        foreach (IHardware subHardware in motherboard.SubHardware)
        {
            ReadMotherboardCpuTempFallback(subHardware, metrics);
            if (metrics.CpuTemperature > 0f)
                return;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _cts.Cancel();

        try
        {
            _monitoringTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // Ignora cancelamento no encerramento
        }

        try
        {
            _computer.Close();
        }
        catch
        {
            // Ignora erros de fechamento de driver
        }

        _cts.Dispose();
    }
}
