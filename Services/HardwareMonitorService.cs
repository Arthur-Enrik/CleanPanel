using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.Models;
using LibreHardwareMonitor.Hardware;

namespace HardwareOverlayApp.Services;

public class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _hardwareLock = new();
    private Task? _monitoringTask;
    private volatile bool _isDisposed;
    private volatile bool _isComputerOpened;
    private volatile bool _isPollingSuspended;
    private volatile bool _isMainWindowVisible;
    private HardwareSpecs _staticSpecs = new();

    // ==================== CACHE DE HARDWARE E REFERÊNCIAS DIRETAS DE SENSORES (ZERO LINQ NO LOOP DE 1S) ====================
    private IHardware? _cpuHardware;
    private readonly List<IHardware> _gpuHardwares = [];
    private readonly List<IHardware> _memoryHardwares = [];
    private readonly List<IHardware> _motherboardHardwares = [];
    private readonly List<IHardware> _storageHardwares = [];

    // Referências diretas de sensores de CPU
    private ISensor? _cpuTempSensor;
    private ISensor? _cpuMotherboardTempFallbackSensor;
    private ISensor? _cpuLoadSensor;
    private ISensor? _cpuPowerSensor;
    private ISensor? _cpuVoltageSensor;
    private ISensor? _cpuMotherboardVcoreFallbackSensor;
    private readonly List<ISensor> _cpuCoreClockSensors = [];
    private readonly List<CachedCpuCoreSensors> _cachedCpuCores = [];

    // Referências diretas da GPU Primária (Overlay) e lista Multi-GPU cacheada
    private ISensor? _gpuTempSensor;
    private ISensor? _gpuLoadSensor;
    private ISensor? _gpuClockSensor;
    private ISensor? _gpuPowerSensor;
    private readonly List<CachedGpuSensors> _cachedGpus = [];

    // Referências diretas de sensores de RAM
    private ISensor? _ramLoadSensor;
    private ISensor? _ramUsedSensor;
    private ISensor? _ramAvailableSensor;

    // Referências diretas de sensores de Armazenamento Físico (SSD/HDD)
    private readonly List<CachedStorageSensors> _cachedStorages = [];

    public event Action<HardwareMetrics>? MetricsUpdated;
    public event Action<HardwareSpecs>? SpecsInitialized;
    public event Action? HeartbeatTick;

    public HardwareSpecs StaticSpecs => _staticSpecs;

    public bool IsPollingSuspended
    {
        get => _isPollingSuspended;
        set
        {
            bool wasSuspended = _isPollingSuspended;
            _isPollingSuspended = value;

            if (wasSuspended && !value && !_isDisposed && _isComputerOpened)
            {
                _ = Task.Run(ReadAndPublishMetrics);
            }
        }
    }

    public bool IsMainWindowVisible
    {
        get => _isMainWindowVisible;
        set
        {
            bool wasVisible = _isMainWindowVisible;
            _isMainWindowVisible = value;

            if (!wasVisible && value && !_isDisposed && _isComputerOpened)
            {
                _ = Task.Run(ReadAndPublishMetrics);
            }
        }
    }

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsMotherboardEnabled = true
        };
    }

    public void Start()
    {
        if (_isDisposed || _monitoringTask is not null)
            return;

        _monitoringTask = Task.Run(() => RunMonitoringLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Método auxiliar de leitura segura contra valores nulos, NaN, Infinito ou zerados.
    /// </summary>
    public static bool IsValidSensorValue(ISensor? sensor, out float value)
    {
        if (sensor?.Value != null &&
            !float.IsNaN(sensor.Value.Value) &&
            !float.IsInfinity(sensor.Value.Value) &&
            sensor.Value.Value > 0)
        {
            value = sensor.Value.Value;
            return true;
        }

        value = 0f;
        return false;
    }

    private static bool IsValidLoadSensorValue(ISensor? sensor, out float value)
    {
        if (IsValidSensorValue(sensor, out value))
            return true;

        if (sensor?.Value != null &&
            !float.IsNaN(sensor.Value.Value) &&
            !float.IsInfinity(sensor.Value.Value) &&
            sensor.Value.Value >= 0f)
        {
            value = sensor.Value.Value;
            return true;
        }

        value = 0f;
        return false;
    }

    private async Task RunMonitoringLoopAsync(CancellationToken cancellationToken)
    {
        lock (_hardwareLock)
        {
            if (!_isDisposed && !_isComputerOpened)
            {
                try
                {
                    _computer.Open();
                }
                catch
                {
                    // Mantém o serviço ativo mesmo se um driver específico falhar na abertura
                }

                // Atualiza os componentes uma única vez para popular a árvore de sensores e mapear referências diretas
                try
                {
                    foreach (IHardware hw in _computer.Hardware)
                    {
                        UpdateHardwareTree(hw);
                    }
                }
                catch
                {
                    // Ignora falhas individuais na primeira varredura
                }

                // Carrega especificações estáticas apenas uma vez
                try
                {
                    _staticSpecs = HardwareSpecs.LoadStaticSpecs(_computer);
                }
                catch
                {
                    // Fallback seguro
                }

                // Mapeia todas as referências diretas de ISensor para eliminar LINQ no loop de 1s
                BuildDirectSensorReferencesCache();

                _isComputerOpened = true;
                SpecsInitialized?.Invoke(_staticSpecs);
            }
        }

        ReadAndPublishMetrics();

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                HeartbeatTick?.Invoke();

                if (_isPollingSuspended && !_isMainWindowVisible)
                    continue;

                ReadAndPublishMetrics();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelamento limpo via CancellationTokenSource
        }
    }

    /// <summary>
    /// Executa apenas na inicialização para mapear e guardar as referências diretas dos objetos ISensor.
    /// </summary>
    private void BuildDirectSensorReferencesCache()
    {
        _cpuHardware = null;
        _gpuHardwares.Clear();
        _memoryHardwares.Clear();
        _motherboardHardwares.Clear();
        _storageHardwares.Clear();

        foreach (IHardware hw in _computer.Hardware)
        {
            switch (hw.HardwareType)
            {
                case HardwareType.Cpu:
                    _cpuHardware ??= hw;
                    break;
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    _gpuHardwares.Add(hw);
                    break;
                case HardwareType.Memory:
                    if (!hw.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                    {
                        _memoryHardwares.Add(hw);
                    }
                    break;
                case HardwareType.Motherboard:
                    _motherboardHardwares.Add(hw);
                    break;
                case HardwareType.Storage:
                    _storageHardwares.Add(hw);
                    break;
            }
        }

        // Ordena GPUs priorizando dedicadas (NVIDIA / AMD) antes de integradas
        _gpuHardwares.Sort( static (a, b) => GetGpuPriority(a.HardwareType).CompareTo(GetGpuPriority(b.HardwareType)));

        CacheCpuSensorReferences();
        CacheMotherboardFallbackSensorReferences();
        CacheGpuSensorReferences();
        CacheRamSensorReferences();
        CacheStorageSensorReferences();
    }

    private static int GetGpuPriority(HardwareType type) => type switch
    {
        HardwareType.GpuNvidia => 0,
        HardwareType.GpuAmd => 1,
        HardwareType.GpuIntel => 2,
        _ => 3
    };

    private void CacheCpuSensorReferences()
    {
        _cpuCoreClockSensors.Clear();
        _cachedCpuCores.Clear();

        if (_cpuHardware is null)
            return;

        IReadOnlyList<ISensor> sensors = _cpuHardware.Sensors;

        // Temperatura de CPU: "Core (Tctl/Tdie)", "CPU Package", "Package", "Core Average", "Core Max" ou 1º disponível
        _cpuTempSensor = ResolveSensorReferenceByAliases(
            sensors,
            SensorType.Temperature,
            "Core (Tctl/Tdie)",
            "CPU Package",
            "Package",
            "Core Average",
            "Core Max")
            ?? sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);

        // Uso Total da CPU
        _cpuLoadSensor = ResolveSensorReferenceByAliases(
            sensors,
            SensorType.Load,
            "CPU Total",
            "Total");

        // Consumo de CPU (Watts): "Package", "CPU Package", "Cores (SMU)", "CPU Power"
        _cpuPowerSensor = ResolveSensorReferenceByAliases(
            sensors,
            SensorType.Power,
            "Package",
            "CPU Package",
            "Cores (SMU)",
            "CPU Power");

        // Tensão de CPU (VCore): "Core (SVI2 TFN)", "Vcore", "CPU Core", "VID"
        _cpuVoltageSensor = ResolveSensorReferenceByAliases(
            sensors,
            SensorType.Voltage,
            "Core (SVI2 TFN)",
            "Vcore",
            "CPU Core",
            "VID")
            ?? sensors.FirstOrDefault(s => s.SensorType == SensorType.Voltage);

        // Clocks dos núcleos (excluindo Bus Clock)
        var nonEffectiveClocks = new List<ISensor>();
        var allNonBusClocks = new List<ISensor>();
        var clockByCoreNumber = new Dictionary<int, ISensor>();

        foreach (ISensor s in sensors)
        {
            if (s.SensorType != SensorType.Clock ||
                s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            allNonBusClocks.Add(s);

            if (!s.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase))
            {
                nonEffectiveClocks.Add(s);
                int cNum = ExtractCoreNumber(s.Name);
                if (cNum > 0 && !clockByCoreNumber.ContainsKey(cNum))
                {
                    clockByCoreNumber[cNum] = s;
                }
            }
        }

        List<ISensor> chosenClocks = nonEffectiveClocks.Count > 0 ? nonEffectiveClocks : allNonBusClocks;
        foreach (ISensor clkSensor in chosenClocks)
        {
            _cpuCoreClockSensors.Add(clkSensor);
            int cNum = ExtractCoreNumber(clkSensor.Name);
            if (cNum > 0 && !clockByCoreNumber.ContainsKey(cNum))
            {
                clockByCoreNumber[cNum] = clkSensor;
            }
        }

        // Mapeia os pares de sensores (Load + Clock) de cada núcleo individualmente
        var loadSensors = sensors
            .Where(s => s.SensorType == SensorType.Load &&
                        !s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) &&
                        !s.Name.Contains("Max", StringComparison.OrdinalIgnoreCase))
            .ToList();

        for (int i = 0; i < loadSensors.Count; i++)
        {
            ISensor loadSensor = loadSensors[i];
            int coreNum = ExtractCoreNumber(loadSensor.Name);

            ISensor? matchedClock = null;
            if (coreNum > 0 && clockByCoreNumber.TryGetValue(coreNum, out ISensor? byCoreNum))
            {
                matchedClock = byCoreNum;
            }
            else if (i < _cpuCoreClockSensors.Count)
            {
                matchedClock = _cpuCoreClockSensors[i];
            }

            _cachedCpuCores.Add(new CachedCpuCoreSensors
            {
                CoreIndex = i + 1,
                DisplayName = FormatCoreDisplayName(loadSensor.Name, i + 1),
                LoadSensor = loadSensor,
                ClockSensor = matchedClock
            });
        }

        if (_cachedCpuCores.Count == 0 && _cpuCoreClockSensors.Count > 0)
        {
            for (int i = 0; i < _cpuCoreClockSensors.Count; i++)
            {
                _cachedCpuCores.Add(new CachedCpuCoreSensors
                {
                    CoreIndex = i + 1,
                    DisplayName = $"Núcleo #{i + 1}",
                    LoadSensor = _cpuLoadSensor,
                    ClockSensor = _cpuCoreClockSensors[i]
                });
            }
        }
    }

    private void CacheMotherboardFallbackSensorReferences()
    {
        _cpuMotherboardTempFallbackSensor = null;
        _cpuMotherboardVcoreFallbackSensor = null;

        foreach (IHardware mb in _motherboardHardwares)
        {
            _cpuMotherboardTempFallbackSensor ??= FindMotherboardTempSensorRecursive(mb);
            _cpuMotherboardVcoreFallbackSensor ??= FindMotherboardVcoreSensorRecursive(mb);
        }
    }

    private static ISensor? FindMotherboardTempSensorRecursive(IHardware hw)
    {
        foreach (ISensor s in hw.Sensors)
        {
            if (s.SensorType == SensorType.Temperature &&
                s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }
        }

        foreach (IHardware sub in hw.SubHardware)
        {
            ISensor? found = FindMotherboardTempSensorRecursive(sub);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static ISensor? FindMotherboardVcoreSensorRecursive(IHardware hw)
    {
        ISensor? direct = ResolveSensorReferenceByAliases(
            hw.Sensors,
            SensorType.Voltage,
            "Vcore",
            "CPU VCore",
            "CPU Core",
            "VDDCR CPU");

        if (direct is not null)
            return direct;

        foreach (IHardware sub in hw.SubHardware)
        {
            ISensor? subFound = FindMotherboardVcoreSensorRecursive(sub);
            if (subFound is not null)
                return subFound;
        }

        return null;
    }

    private void CacheGpuSensorReferences()
    {
        _cachedGpus.Clear();
        _gpuTempSensor = null;
        _gpuLoadSensor = null;
        _gpuClockSensor = null;
        _gpuPowerSensor = null;

        for (int i = 0; i < _gpuHardwares.Count; i++)
        {
            IHardware gpu = _gpuHardwares[i];
            IReadOnlyList<ISensor> sensors = gpu.Sensors;

            string gpuId = gpu.Identifier?.ToString() ?? $"gpu_{i}";
            GpuSpecInfo? spec = _staticSpecs.Gpus.FirstOrDefault(g =>
                g.Id.Equals(gpuId, StringComparison.OrdinalIgnoreCase) ||
                g.Name.Equals(gpu.Name, StringComparison.OrdinalIgnoreCase));

            string vendor = gpu.HardwareType switch
            {
                HardwareType.GpuNvidia => "NVIDIA",
                HardwareType.GpuAmd => "AMD",
                HardwareType.GpuIntel => "Intel",
                _ => spec?.Vendor ?? "GPU"
            };

            ISensor? coreTemp = ResolveSensorReferenceByAliases(
                sensors,
                SensorType.Temperature,
                "GPU Core",
                "GPU",
                "Hot Spot")
                ?? sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);

            ISensor? hotSpotTemp = ResolveSensorReferenceByAliases(
                sensors,
                SensorType.Temperature,
                "Hot Spot",
                "GPU Hot Spot",
                "Memory Junction");

            ISensor? coreLoad = ResolveSensorReferenceByAliases(
                sensors,
                SensorType.Load,
                "GPU Core",
                "D3D 3D",
                "GPU");

            ISensor? coreClock = ResolveSensorReferenceByAliases(
                sensors,
                SensorType.Clock,
                "GPU Core",
                "Core",
                "Graphics");

            ISensor? memClock = ResolveSensorReferenceByAliases(
                sensors,
                SensorType.Clock,
                "GPU Memory",
                "Memory",
                "VRAM");

            ISensor? power = ResolveSensorReferenceByAliases(
                sensors,
                SensorType.Power,
                "GPU Package",
                "GPU Power",
                "GPU Core",
                "Board Power",
                "Total Board Power");

            ISensor? vramTotalSmall = ResolveSensorReferenceByAliases(
                sensors,
                SensorType.SmallData,
                "GPU Memory Total",
                "D3D Dedicated Memory Total");

            ISensor? vramUsedSmall = ResolveSensorReferenceByAliases(
                sensors,
                SensorType.SmallData,
                "GPU Memory Used",
                "D3D Dedicated Memory Used");

            bool vramUsedIsMb = true;
            ISensor? vramUsedSensor = vramUsedSmall;
            if (vramUsedSensor is null)
            {
                vramUsedSensor = ResolveSensorReferenceByAliases(
                    sensors,
                    SensorType.Data,
                    "GPU Memory Used",
                    "D3D Dedicated Memory Used");
                vramUsedIsMb = false;
            }

            ISensor? fanRpm = ResolveSensorReferenceByAliases(sensors, SensorType.Fan, "GPU Fan", "Fan")
                              ?? sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan);

            ISensor? fanPct = ResolveSensorReferenceByAliases(sensors, SensorType.Control, "GPU Fan", "Fan");

            var cachedGpu = new CachedGpuSensors
            {
                Hardware = gpu,
                Id = gpuId,
                Name = !string.IsNullOrWhiteSpace(gpu.Name) ? gpu.Name.Trim() : spec?.Name ?? $"GPU #{i + 1}",
                Vendor = vendor,
                TotalVramGb = spec?.TotalVramGb,
                IsIntegratedOrSharedVram = spec?.IsIntegratedOrSharedVram ?? true,
                TotalVramDisplay = spec?.TotalVramDisplay ?? "Compartilhada / Integrada",
                CoreTempSensor = coreTemp,
                HotSpotTempSensor = hotSpotTemp,
                CoreLoadSensor = coreLoad,
                CoreClockSensor = coreClock,
                MemoryClockSensor = memClock,
                PowerSensor = power,
                VramTotalSmallDataSensor = vramTotalSmall,
                VramUsedSensor = vramUsedSensor,
                VramUsedIsSmallDataMb = vramUsedIsMb,
                FanRpmSensor = fanRpm,
                FanPercentSensor = fanPct
            };

            _cachedGpus.Add(cachedGpu);

            // Define referências diretas da GPU Primária para leitura ultrarrápida do Overlay
            if (i == 0)
            {
                _gpuTempSensor = coreTemp;
                _gpuLoadSensor = coreLoad;
                _gpuClockSensor = coreClock;
                _gpuPowerSensor = power;
            }
        }
    }

    private void CacheRamSensorReferences()
    {
        _ramLoadSensor = null;
        _ramUsedSensor = null;
        _ramAvailableSensor = null;

        foreach (IHardware mem in _memoryHardwares)
        {
            foreach (ISensor s in mem.Sensors)
            {
                if (s.SensorType == SensorType.Load &&
                    s.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase))
                {
                    _ramLoadSensor ??= s;
                }
                else if (s.SensorType == SensorType.Data &&
                         s.Name.Equals("Memory Used", StringComparison.OrdinalIgnoreCase))
                {
                    _ramUsedSensor ??= s;
                }
                else if (s.SensorType == SensorType.Data &&
                         s.Name.Equals("Memory Available", StringComparison.OrdinalIgnoreCase))
                {
                    _ramAvailableSensor ??= s;
                }
            }
        }
    }

    private void CacheStorageSensorReferences()
    {
        _cachedStorages.Clear();

        foreach (IHardware storage in _storageHardwares)
        {
            ISensor? tempSensor = ResolveSensorReferenceByAliases(
                storage.Sensors,
                SensorType.Temperature,
                "Temperature",
                "Composite",
                "Drive Temperature",
                "Airflow Temperature")
                ?? storage.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);

            ISensor? usedSpaceSensor = ResolveSensorReferenceByAliases(
                storage.Sensors,
                SensorType.Load,
                "Used Space");

            _cachedStorages.Add(new CachedStorageSensors
            {
                Hardware = storage,
                ModelName = !string.IsNullOrWhiteSpace(storage.Name) ? storage.Name.Trim() : "Unidade Física SSD/HDD",
                TempSensor = tempSensor,
                UsedSpaceSensor = usedSpaceSensor
            });
        }
    }

    public void RequestImmediateRefresh()
    {
        if (!_isDisposed && _isComputerOpened)
        {
            _ = Task.Run(ReadAndPublishMetrics);
        }
    }

    private void ReadAndPublishMetrics()
    {
        if (_isDisposed || !_isComputerOpened || !Monitor.TryEnter(_hardwareLock))
            return;

        try
        {
            bool readDetailed = _isMainWindowVisible;
            var metrics = new HardwareMetrics
            {
                IsDetailedSnapshot = readDetailed,
                RamTotalGb = _staticSpecs.TotalRamGb
            };

            // 1. Atualiza apenas os hardwares necessários neste ciclo
            UpdateRequiredHardwares(readDetailed);

            // 2. Leitura direta de CPU via referências cacheadas (Try/Catch isolado)
            try
            {
                ReadCpuMetricsFromCache(metrics, readDetailed);
            }
            catch
            {
                // Isola falha de leitura da CPU
            }

            // 3. Leitura direta de GPU(s) via referências cacheadas (Try/Catch isolado)
            try
            {
                ReadGpuMetricsFromCache(metrics, readDetailed);
            }
            catch
            {
                // Isola falha de leitura da GPU
            }

            // 4. Leitura direta de Memória RAM via referências cacheadas (Try/Catch isolado)
            try
            {
                ReadRamMetricsFromCache(metrics);
            }
            catch
            {
                // Isola falha de leitura da RAM
            }

            // 5. Leitura de Armazenamento apenas quando a janela principal está aberta
            if (readDetailed)
            {
                try
                {
                    ReadStorageMetricsFromCache(metrics);
                }
                catch
                {
                    // Isola falha de leitura de discos
                }
            }

            MetricsUpdated?.Invoke(metrics);
        }
        catch
        {
            // Ignora falhas transitórias gerais
        }
        finally
        {
            Monitor.Exit(_hardwareLock);
        }
    }

    private void UpdateRequiredHardwares(bool readDetailed)
    {
        if (_cpuHardware is not null)
        {
            UpdateHardwareTree(_cpuHardware);
        }

        if (readDetailed)
        {
            for (int i = 0; i < _gpuHardwares.Count; i++)
            {
                UpdateHardwareTree(_gpuHardwares[i]);
            }

            for (int i = 0; i < _motherboardHardwares.Count; i++)
            {
                UpdateHardwareTree(_motherboardHardwares[i]);
            }

            for (int i = 0; i < _storageHardwares.Count; i++)
            {
                UpdateHardwareTree(_storageHardwares[i]);
            }
        }
        else if (_gpuHardwares.Count > 0)
        {
            // Em modo Overlay leve, atualiza apenas a GPU primária
            UpdateHardwareTree(_gpuHardwares[0]);
        }

        for (int i = 0; i < _memoryHardwares.Count; i++)
        {
            UpdateHardwareTree(_memoryHardwares[i]);
        }
    }

    private static void UpdateHardwareTree(IHardware hardware)
    {
        try
        {
            hardware.Update();
        }
        catch
        {
            // Ignora erro no componente pai
        }

        IReadOnlyList<IHardware> subHardwares = hardware.SubHardware;
        for (int i = 0; i < subHardwares.Count; i++)
        {
            try
            {
                subHardwares[i].Update();
            }
            catch
            {
                // Ignora falha em sub-hardware específico
            }
        }
    }

    private void ReadCpuMetricsFromCache(HardwareMetrics metrics, bool readDetailed)
    {
        if (IsValidSensorValue(_cpuTempSensor, out float cpuTemp))
        {
            metrics.CpuTemperature = cpuTemp;
        }
        else if (_cpuMotherboardTempFallbackSensor is not null)
        {
            if (!readDetailed)
            {
                for (int i = 0; i < _motherboardHardwares.Count; i++)
                {
                    UpdateHardwareTree(_motherboardHardwares[i]);
                }
            }

            if (IsValidSensorValue(_cpuMotherboardTempFallbackSensor, out float mbTemp))
            {
                metrics.CpuTemperature = mbTemp;
            }
        }

        if (IsValidLoadSensorValue(_cpuLoadSensor, out float cpuLoad))
        {
            metrics.CpuLoad = cpuLoad;
        }

        if (IsValidSensorValue(_cpuPowerSensor, out float cpuPower))
        {
            metrics.CpuPower = cpuPower;
        }

        float clockSum = 0f;
        float maxClock = 0f;
        int clockCount = 0;

        for (int i = 0; i < _cpuCoreClockSensors.Count; i++)
        {
            if (IsValidSensorValue(_cpuCoreClockSensors[i], out float clk))
            {
                clockSum += clk;
                clockCount++;
                if (clk > maxClock)
                {
                    maxClock = clk;
                }
            }
        }

        if (clockCount > 0)
        {
            metrics.CpuClock = clockSum / clockCount;
            metrics.CpuMaxClock = maxClock;
        }

        if (!readDetailed)
            return;

        // VCore direto do sensor cacheado (CPU ou fallback SuperIO da placa-mãe)
        if (IsValidSensorValue(_cpuVoltageSensor, out float vcore) && vcore is > 0.15f and < 2.6f)
        {
            metrics.CpuVoltageVcore = vcore;
        }
        else if (IsValidSensorValue(_cpuMotherboardVcoreFallbackSensor, out float mbVcore) && mbVcore is > 0.15f and < 2.6f)
        {
            metrics.CpuVoltageVcore = mbVcore;
        }

        // Telemetria por núcleo direta sem LINQ e sem Regex
        for (int i = 0; i < _cachedCpuCores.Count; i++)
        {
            CachedCpuCoreSensors core = _cachedCpuCores[i];
            float? coreLoadVal = IsValidLoadSensorValue(core.LoadSensor, out float cl) ? cl : null;
            float? coreClockVal = IsValidSensorValue(core.ClockSensor, out float cc)
                ? cc
                : (metrics.CpuClock > 0f ? metrics.CpuClock : null);

            metrics.CpuCores.Add(new CpuCoreMetric
            {
                CoreIndex = core.CoreIndex,
                CoreName = core.DisplayName,
                LoadPercent = coreLoadVal,
                ClockMhz = coreClockVal
            });
        }
    }

    private void ReadGpuMetricsFromCache(HardwareMetrics metrics, bool readDetailed)
    {
        if (IsValidSensorValue(_gpuTempSensor, out float primaryTemp))
            metrics.GpuTemperature = primaryTemp;

        if (IsValidLoadSensorValue(_gpuLoadSensor, out float primaryLoad))
            metrics.GpuLoad = primaryLoad;

        if (IsValidSensorValue(_gpuClockSensor, out float primaryClock))
            metrics.GpuClock = primaryClock;

        if (IsValidSensorValue(_gpuPowerSensor, out float primaryPower))
            metrics.GpuPower = primaryPower;

        if (!readDetailed)
            return;

        for (int i = 0; i < _cachedGpus.Count; i++)
        {
            CachedGpuSensors g = _cachedGpus[i];

            float? coreTemp = IsValidSensorValue(g.CoreTempSensor, out float ct) ? ct : null;
            float? hotSpotTemp = IsValidSensorValue(g.HotSpotTempSensor, out float hst) ? hst : null;
            float? coreLoad = IsValidLoadSensorValue(g.CoreLoadSensor, out float cl) ? cl : null;
            float? coreClock = IsValidSensorValue(g.CoreClockSensor, out float cc) ? cc : null;
            float? memClock = IsValidSensorValue(g.MemoryClockSensor, out float mc) ? mc : null;
            float? power = IsValidSensorValue(g.PowerSensor, out float pw) ? pw : null;
            float? fanRpm = IsValidSensorValue(g.FanRpmSensor, out float rpm) ? rpm : null;
            float? fanPct = IsValidSensorValue(g.FanPercentSensor, out float fp) ? fp : null;

            if (IsValidSensorValue(g.VramTotalSmallDataSensor, out float totalVramMb) && totalVramMb > 640f)
            {
                g.TotalVramGb = totalVramMb / 1024f;
                g.IsIntegratedOrSharedVram = false;
                g.TotalVramDisplay = string.Create(CultureInfo.InvariantCulture, $"{g.TotalVramGb.Value:0.#} GB");
            }

            float? vramUsedGb = null;
            if (IsValidSensorValue(g.VramUsedSensor, out float rawVramUsed))
            {
                vramUsedGb = g.VramUsedIsSmallDataMb || rawVramUsed > 128f
                    ? rawVramUsed / 1024f
                    : rawVramUsed;
            }

            metrics.Gpus.Add(new GpuDetailedMetrics
            {
                Id = g.Id,
                Name = g.Name,
                Vendor = g.Vendor,
                TotalVramGb = g.TotalVramGb,
                IsIntegratedOrSharedVram = g.IsIntegratedOrSharedVram,
                TotalVramDisplay = g.TotalVramDisplay,
                CoreLoadPercent = coreLoad,
                CoreTemperatureC = coreTemp,
                HotSpotTemperatureC = hotSpotTemp,
                CoreClockMhz = coreClock,
                MemoryClockMhz = memClock,
                VramUsedGb = vramUsedGb,
                PowerWatts = power,
                FanRpm = fanRpm,
                FanPercent = fanPct
            });
        }

        if (metrics.Gpus.Count == 0 && _staticSpecs.Gpus.Count > 0)
        {
            for (int i = 0; i < _staticSpecs.Gpus.Count; i++)
            {
                GpuSpecInfo spec = _staticSpecs.Gpus[i];
                metrics.Gpus.Add(new GpuDetailedMetrics
                {
                    Id = spec.Id,
                    Name = spec.Name,
                    Vendor = spec.Vendor,
                    TotalVramGb = spec.TotalVramGb,
                    IsIntegratedOrSharedVram = spec.IsIntegratedOrSharedVram,
                    TotalVramDisplay = spec.TotalVramDisplay
                });
            }
        }
    }

    private void ReadRamMetricsFromCache(HardwareMetrics metrics)
    {
        float usedGb = IsValidSensorValue(_ramUsedSensor, out float u) ? u : 0f;
        float freeGb = IsValidSensorValue(_ramAvailableSensor, out float a) ? a : 0f;
        float loadPct = IsValidSensorValue(_ramLoadSensor, out float l) ? l : 0f;

        if (usedGb > 0f)
            metrics.RamUsedGb = usedGb;

        if (loadPct > 0f)
            metrics.RamUsagePercent = loadPct;

        float lhmTotal = usedGb + freeGb;
        if (lhmTotal > 0f)
        {
            metrics.RamTotalGb = lhmTotal;
            metrics.RamFreeGb = freeGb;
            if (_staticSpecs.TotalRamGb <= 0f)
            {
                _staticSpecs.TotalRamGb = lhmTotal;
            }
        }
        else if (_staticSpecs.TotalRamGb > 0f)
        {
            metrics.RamTotalGb = _staticSpecs.TotalRamGb;
            metrics.RamFreeGb = Math.Max(0f, _staticSpecs.TotalRamGb - metrics.RamUsedGb);
        }
    }

    private void ReadStorageMetricsFromCache(HardwareMetrics metrics)
    {
        var physicalDisks = new List<PhysicalStorageMetrics>(_cachedStorages.Count);

        for (int i = 0; i < _cachedStorages.Count; i++)
        {
            CachedStorageSensors cs = _cachedStorages[i];
            float? temp = IsValidSensorValue(cs.TempSensor, out float tVal) ? tVal : null;
            float? usedPct = IsValidSensorValue(cs.UsedSpaceSensor, out float uVal) ? uVal : null;

            physicalDisks.Add(new PhysicalStorageMetrics
            {
                ModelName = cs.ModelName,
                TemperatureC = temp,
                UsedSpacePercent = usedPct
            });
        }

        metrics.PhysicalDisks = physicalDisks;

        DriveInfo[] drives = DriveInfo.GetDrives();
        int driveIndex = 0;

        for (int i = 0; i < drives.Length; i++)
        {
            DriveInfo drive = drives[i];
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    continue;

                string driveKey = drive.Name.TrimEnd('\\');
                double totalGb = _staticSpecs.PartitionStaticTotalGb.TryGetValue(driveKey, out double cachedTotal) && cachedTotal > 0
                    ? cachedTotal
                    : drive.TotalSize / (1024.0 * 1024.0 * 1024.0);

                double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                double usedGb = Math.Max(0.0, totalGb - freeGb);
                double usagePct = totalGb > 0.0 ? Math.Clamp((usedGb / totalGb) * 100.0, 0.0, 100.0) : 0.0;

                string volumeLabel = string.Empty;
                string fileSystem = "NTFS";
                try
                {
                    volumeLabel = drive.VolumeLabel?.Trim() ?? string.Empty;
                    fileSystem = drive.DriveFormat?.Trim() ?? "NTFS";
                }
                catch
                {
                    // Ignora falha de leitura de rótulo secundário
                }

                var (physicalModel, tempC) = ResolvePhysicalDiskForDrive(driveKey, driveIndex, physicalDisks);

                metrics.StorageDrives.Add(new StorageDriveMetrics
                {
                    DriveName = driveKey,
                    VolumeLabel = volumeLabel,
                    FileSystem = fileSystem,
                    PhysicalDiskModel = physicalModel,
                    TotalSizeGb = totalGb,
                    UsedSizeGb = usedGb,
                    FreeSizeGb = freeGb,
                    UsagePercent = usagePct,
                    TemperatureC = tempC
                });

                driveIndex++;
            }
            catch
            {
                // Isola falha em partição individual
            }
        }
    }

    private (string ModelName, float? TemperatureC) ResolvePhysicalDiskForDrive(
        string driveKey,
        int driveIndex,
        List<PhysicalStorageMetrics> physicalDisks)
    {
        if (_staticSpecs.LogicalDriveToPhysicalModel.TryGetValue(driveKey, out string? mappedModel) &&
            !string.IsNullOrWhiteSpace(mappedModel))
        {
            for (int i = 0; i < physicalDisks.Count; i++)
            {
                PhysicalStorageMetrics p = physicalDisks[i];
                if (p.ModelName.Contains(mappedModel, StringComparison.OrdinalIgnoreCase) ||
                    mappedModel.Contains(p.ModelName, StringComparison.OrdinalIgnoreCase))
                {
                    return (p.ModelName, p.TemperatureC);
                }
            }

            if (physicalDisks.Count == 1)
            {
                return (mappedModel, physicalDisks[0].TemperatureC);
            }

            return (mappedModel, null);
        }

        if (physicalDisks.Count == 1)
        {
            return (physicalDisks[0].ModelName, physicalDisks[0].TemperatureC);
        }

        if (driveIndex >= 0 && driveIndex < physicalDisks.Count)
        {
            return (physicalDisks[driveIndex].ModelName, physicalDisks[driveIndex].TemperatureC);
        }

        return ("Disco Local Fixo", null);
    }

    private static ISensor? ResolveSensorReferenceByAliases(
        IReadOnlyList<ISensor> sensors,
        SensorType sensorType,
        params string[] aliases)
    {
        for (int a = 0; a < aliases.Length; a++)
        {
            string alias = aliases[a];
            for (int s = 0; s < sensors.Count; s++)
            {
                ISensor sensor = sensors[s];
                if (sensor.SensorType == sensorType &&
                    sensor.Name.Equals(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return sensor;
                }
            }
        }

        for (int a = 0; a < aliases.Length; a++)
        {
            string alias = aliases[a];
            for (int s = 0; s < sensors.Count; s++)
            {
                ISensor sensor = sensors[s];
                if (sensor.SensorType == sensorType &&
                    sensor.Name.Contains(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return sensor;
                }
            }
        }

        return null;
    }

    private static int ExtractCoreNumber(string sensorName)
    {
        Match match = Regex.Match(sensorName, @"Core\s*#?(\d+)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
            return num;

        return -1;
    }

    private static string FormatCoreDisplayName(string rawName, int fallbackIndex)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return $"Núcleo #{fallbackIndex}";

        return rawName
            .Replace("CPU Core", "Núcleo", StringComparison.OrdinalIgnoreCase)
            .Replace("Core", "Núcleo", StringComparison.OrdinalIgnoreCase)
            .Trim();
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

        lock (_hardwareLock)
        {
            try
            {
                _computer.Close();
            }
            catch
            {
                // Ignora erros de fechamento de driver
            }
        }

        _cts.Dispose();
    }

    private sealed class CachedCpuCoreSensors
    {
        public int CoreIndex { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public ISensor? LoadSensor { get; init; }
        public ISensor? ClockSensor { get; init; }
    }

    private sealed class CachedGpuSensors
    {
        public required IHardware Hardware { get; init; }
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Vendor { get; init; } = string.Empty;
        public float? TotalVramGb { get; set; }
        public bool IsIntegratedOrSharedVram { get; set; }
        public string TotalVramDisplay { get; set; } = "Compartilhada / Integrada";
        public ISensor? CoreTempSensor { get; init; }
        public ISensor? HotSpotTempSensor { get; init; }
        public ISensor? CoreLoadSensor { get; init; }
        public ISensor? CoreClockSensor { get; init; }
        public ISensor? MemoryClockSensor { get; init; }
        public ISensor? PowerSensor { get; init; }
        public ISensor? VramTotalSmallDataSensor { get; init; }
        public ISensor? VramUsedSensor { get; init; }
        public bool VramUsedIsSmallDataMb { get; init; }
        public ISensor? FanRpmSensor { get; init; }
        public ISensor? FanPercentSensor { get; init; }
    }

    private sealed class CachedStorageSensors
    {
        public required IHardware Hardware { get; init; }
        public string ModelName { get; init; } = string.Empty;
        public ISensor? TempSensor { get; init; }
        public ISensor? UsedSpaceSensor { get; init; }
    }
}
