using System.IO;
using System.Text.RegularExpressions;
using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.Models;
using LibreHardwareMonitor.Hardware;

namespace HardwareOverlayApp.Services;

public class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _updateVisitor;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _hardwareLock = new();
    private Task? _monitoringTask;
    private volatile bool _isDisposed;
    private volatile bool _isComputerOpened;
    private volatile bool _isPollingSuspended;
    private volatile bool _isMainWindowVisible;
    private HardwareSpecs _staticSpecs = new();

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

            // Ao retomar do modo suspenso, realiza uma leitura imediata em background
            if (wasSuspended && !value && !_isDisposed)
            {
                _ = Task.Run(ReadAndPublishMetrics);
            }
        }
    }

    /// <summary>
    /// Indica se a janela principal estilo Task Manager está aberta e visível na tela.
    /// Quando false (fechada/minimizada na bandeja), sensores pesados (núcleos individuais, tensões SuperIO, SMART/discos) são poupados.
    /// </summary>
    public bool IsMainWindowVisible
    {
        get => _isMainWindowVisible;
        set
        {
            bool wasVisible = _isMainWindowVisible;
            _isMainWindowVisible = value;

            if (!wasVisible && value && !_isDisposed)
            {
                _ = Task.Run(ReadAndPublishMetrics);
            }
        }
    }

    public HardwareMonitorService()
    {
        _updateVisitor = new UpdateVisitor();

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
    /// Método auxiliar de leitura segura exigido pela blindagem contra sensores nulos, NaN, Infinito ou zerados.
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

    /// <summary>
    /// Leitura segura para sensores de carga (Load %) onde 0% em repouso também é um valor numérico válido se o sensor existir.
    /// </summary>
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

                // Carrega especificações estáticas APENAS UMA VEZ na inicialização e faz cache em memória
                try
                {
                    _staticSpecs = HardwareSpecs.LoadStaticSpecs(_computer);
                    SpecsInitialized?.Invoke(_staticSpecs);
                }
                catch
                {
                    // Fallback seguro caso ocorra erro inesperado
                }

                _isComputerOpened = true;
            }
        }

        ReadAndPublishMetrics();

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                HeartbeatTick?.Invoke();

                // Se a janela principal não estiver visível e o Overlay estiver suspenso, poupa 100% de CPU
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

            // Atualização seletiva de hardware (Zero desperdício de CPU quando na bandeja)
            UpdateHardwareSelectively(readDetailed);

            // 1. Leitura de CPU (Try/Catch isolado)
            try
            {
                IHardware? cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                if (cpu is not null)
                {
                    ReadCpuMetrics(cpu, metrics, readDetailed);
                }
            }
            catch
            {
                // Isola falhas de leitura da CPU
            }

            // 2. Fallback de Temperatura e VCore na Placa-Mãe / SuperIO (Try/Catch isolado)
            try
            {
                bool needsTempFallback = metrics.CpuTemperature <= 0f;
                bool needsVcoreFallback = readDetailed && (metrics.CpuVoltageVcore is null || metrics.CpuVoltageVcore <= 0f);

                if (needsTempFallback || needsVcoreFallback)
                {
                    foreach (IHardware motherboard in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Motherboard))
                    {
                        if (!readDetailed)
                        {
                            // Em modo leve, atualiza a placa-mãe apenas se precisar de fallback de temperatura da CPU
                            UpdateHardwareTree(motherboard);
                        }

                        if (needsTempFallback && metrics.CpuTemperature <= 0f)
                        {
                            ReadMotherboardCpuTempFallback(motherboard, metrics);
                        }

                        if (needsVcoreFallback && (metrics.CpuVoltageVcore is null || metrics.CpuVoltageVcore <= 0f))
                        {
                            metrics.CpuVoltageVcore = ReadMotherboardVcoreFallback(motherboard);
                        }
                    }
                }
            }
            catch
            {
                // Isola falhas de leitura da Placa-Mãe / SuperIO
            }

            // 3. Leitura de GPUs - Multi-GPU e GPU Primária do Overlay (Try/Catch isolado)
            try
            {
                ReadAllGpusMetrics(metrics, readDetailed);
            }
            catch
            {
                // Isola falhas de leitura de GPU
            }

            // 4. Leitura de Memória RAM (Try/Catch isolado)
            try
            {
                foreach (IHardware memory in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Memory))
                {
                    ReadRamMetrics(memory, metrics);
                }
            }
            catch
            {
                // Isola falhas de leitura de RAM
            }

            // 5. Leitura de Armazenamento: Partições DriveInfo + Temperatura Física SSD/HDD (Apenas com janela principal visível)
            if (readDetailed)
            {
                try
                {
                    ReadStorageMetrics(metrics);
                }
                catch
                {
                    // Isola falhas de leitura de discos
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

    private void UpdateHardwareSelectively(bool readDetailed)
    {
        foreach (IHardware hardware in _computer.Hardware)
        {
            try
            {
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                    case HardwareType.Memory:
                        UpdateHardwareTree(hardware);
                        break;

                    case HardwareType.Motherboard:
                    case HardwareType.Storage:
                        // Sensores pesados (SuperIO da placa-mãe e SMART/temperatura de discos) só são atualizados se a janela principal estiver aberta
                        if (readDetailed)
                        {
                            UpdateHardwareTree(hardware);
                        }
                        break;
                }
            }
            catch
            {
                // Continua atualizando os demais componentes se um driver falhar
            }
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
            // Ignora erro no componente pai e tenta sub-hardwares
        }

        foreach (IHardware sub in hardware.SubHardware)
        {
            try
            {
                sub.Update();
            }
            catch
            {
                // Ignora falha em sub-hardware específico (ex: SuperIO)
            }
        }
    }

    private void ReadCpuMetrics(IHardware cpu, HardwareMetrics metrics, bool readDetailed)
    {
        IReadOnlyList<ISensor> sensors = cpu.Sensors;

        // 1. Temperatura de CPU na ordem estrita de prioridade:
        // "Core (Tctl/Tdie)" -> "CPU Package" -> "Package" -> "Core Average" -> "Core Max" -> primeiro SensorType.Temperature disponível
        float? cpuTemp = FindSensorByAliases(
            sensors,
            SensorType.Temperature,
            "Core (Tctl/Tdie)",
            "CPU Package",
            "Package",
            "Core Average",
            "Core Max");

        if (cpuTemp is null)
        {
            foreach (ISensor s in sensors)
            {
                if (s.SensorType == SensorType.Temperature && IsValidSensorValue(s, out float firstTemp))
                {
                    cpuTemp = firstTemp;
                    break;
                }
            }
        }

        metrics.CpuTemperature = cpuTemp ?? 0f;

        // 2. Uso Total da CPU (%)
        float? totalLoad = FindLoadSensorByAliases(sensors, "CPU Total", "Total");
        metrics.CpuLoad = totalLoad ?? 0f;

        // 3. Consumo de CPU (Watts) na ordem: "Package", "CPU Package", "Cores (SMU)", "CPU Power"
        float? cpuPower = FindSensorByAliases(
            sensors,
            SensorType.Power,
            "Package",
            "CPU Package",
            "Cores (SMU)",
            "CPU Power");
        metrics.CpuPower = cpuPower ?? 0f;

        // 4. Clocks da CPU (Médio e Máximo)
        float clockSum = 0f;
        float maxClock = 0f;
        int clockCount = 0;

        foreach (ISensor sensor in sensors)
        {
            if (sensor.SensorType == SensorType.Clock &&
                !sensor.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase) &&
                IsValidSensorValue(sensor, out float clockVal))
            {
                clockSum += clockVal;
                clockCount++;
                if (clockVal > maxClock)
                {
                    maxClock = clockVal;
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

        // 5. Tensão VCore (Apenas com janela principal aberta):
        // Tenta na CPU: "Core (SVI2 TFN)", "Vcore", "CPU Core", "VID"
        float? vcore = FindSensorByAliases(
            sensors,
            SensorType.Voltage,
            "Core (SVI2 TFN)",
            "Vcore",
            "CPU Core",
            "VID");

        if (vcore is null)
        {
            foreach (ISensor s in sensors)
            {
                if (s.SensorType == SensorType.Voltage &&
                    IsValidSensorValue(s, out float v) &&
                    v is > 0.2f and < 2.5f)
                {
                    vcore = v;
                    break;
                }
            }
        }

        metrics.CpuVoltageVcore = vcore;

        // 6. Grade de Núcleos (Per-Core Telemetry)
        ReadCpuPerCoreTelemetry(sensors, metrics);
    }

    private void ReadCpuPerCoreTelemetry(IReadOnlyList<ISensor> sensors, HardwareMetrics metrics)
    {
        var clockByCoreNumber = new Dictionary<int, float>();
        var allCoreClocks = new List<float>();

        foreach (ISensor sensor in sensors)
        {
            if (sensor.SensorType == SensorType.Clock &&
                !sensor.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase) &&
                !sensor.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase) &&
                IsValidSensorValue(sensor, out float clk))
            {
                allCoreClocks.Add(clk);
                int coreNum = ExtractCoreNumber(sensor.Name);
                if (coreNum > 0 && !clockByCoreNumber.ContainsKey(coreNum))
                {
                    clockByCoreNumber[coreNum] = clk;
                }
            }
        }

        // Fallback para clocks "Effective" caso o processador exponha apenas clocks efetivos
        if (allCoreClocks.Count == 0)
        {
            foreach (ISensor sensor in sensors)
            {
                if (sensor.SensorType == SensorType.Clock &&
                    !sensor.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase) &&
                    IsValidSensorValue(sensor, out float clk))
                {
                    allCoreClocks.Add(clk);
                    int coreNum = ExtractCoreNumber(sensor.Name);
                    if (coreNum > 0 && !clockByCoreNumber.ContainsKey(coreNum))
                    {
                        clockByCoreNumber[coreNum] = clk;
                    }
                }
            }
        }

        var loadSensors = sensors
            .Where(s => s.SensorType == SensorType.Load &&
                        !s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) &&
                        !s.Name.Contains("Max", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int idx = 0;
        foreach (ISensor loadSensor in loadSensors)
        {
            float? loadVal = IsValidLoadSensorValue(loadSensor, out float lv) ? lv : null;
            int coreNum = ExtractCoreNumber(loadSensor.Name);

            float? coreClock = null;
            if (coreNum > 0 && clockByCoreNumber.TryGetValue(coreNum, out float matchedClock))
            {
                coreClock = matchedClock;
            }
            else if (idx < allCoreClocks.Count)
            {
                coreClock = allCoreClocks[idx];
            }
            else if (metrics.CpuClock > 0f)
            {
                coreClock = metrics.CpuClock;
            }

            string cleanName = FormatCoreDisplayName(loadSensor.Name, idx + 1);

            metrics.CpuCores.Add(new CpuCoreMetric
            {
                CoreIndex = idx + 1,
                CoreName = cleanName,
                LoadPercent = loadVal,
                ClockMhz = coreClock
            });

            idx++;
        }

        // Caso a CPU exponha sensores de Clock por núcleo mas não exponha Load por núcleo
        if (metrics.CpuCores.Count == 0 && allCoreClocks.Count > 0)
        {
            for (int i = 0; i < allCoreClocks.Count; i++)
            {
                metrics.CpuCores.Add(new CpuCoreMetric
                {
                    CoreIndex = i + 1,
                    CoreName = $"Núcleo #{i + 1}",
                    LoadPercent = metrics.CpuLoad,
                    ClockMhz = allCoreClocks[i]
                });
            }
        }
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

        string formatted = rawName
            .Replace("CPU Core", "Núcleo", StringComparison.OrdinalIgnoreCase)
            .Replace("Core", "Núcleo", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return formatted;
    }

    private static void ReadMotherboardCpuTempFallback(IHardware hardware, HardwareMetrics metrics)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Temperature &&
                sensor.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) &&
                IsValidSensorValue(sensor, out float value))
            {
                metrics.CpuTemperature = value;
                return;
            }
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            ReadMotherboardCpuTempFallback(subHardware, metrics);
            if (metrics.CpuTemperature > 0f)
                return;
        }
    }

    private static float? ReadMotherboardVcoreFallback(IHardware hardware)
    {
        float? vcore = FindSensorByAliases(
            hardware.Sensors,
            SensorType.Voltage,
            "Vcore",
            "CPU VCore",
            "CPU Core",
            "VDDCR CPU");

        if (vcore is > 0f)
            return vcore;

        foreach (IHardware sub in hardware.SubHardware)
        {
            float? subVcore = ReadMotherboardVcoreFallback(sub);
            if (subVcore is > 0f)
                return subVcore;
        }

        return null;
    }

    private void ReadAllGpusMetrics(HardwareMetrics metrics, bool readDetailed)
    {
        var gpuHardwares = _computer.Hardware
            .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .ToList();

        // Ordena priorizando GPUs dedicadas (NVIDIA / AMD) antes de integradas para alimentar o Overlay primário
        var orderedGpus = gpuHardwares
            .OrderBy(h => h.HardwareType switch
            {
                HardwareType.GpuNvidia => 0,
                HardwareType.GpuAmd => 1,
                HardwareType.GpuIntel => 2,
                _ => 3
            })
            .ToList();

        bool primaryOverlayGpuSet = false;
        int index = 0;

        foreach (IHardware gpu in orderedGpus)
        {
            IReadOnlyList<ISensor> sensors = gpu.Sensors;

            // Temperatura Core: "GPU Core", "GPU", "Hot Spot"
            float? coreTemp = FindSensorByAliases(
                sensors,
                SensorType.Temperature,
                "GPU Core",
                "GPU",
                "Hot Spot");

            if (coreTemp is null)
            {
                foreach (ISensor s in sensors)
                {
                    if (s.SensorType == SensorType.Temperature && IsValidSensorValue(s, out float ft))
                    {
                        coreTemp = ft;
                        break;
                    }
                }
            }

            // Uso do Core (%)
            float? coreLoad = FindLoadSensorByAliases(sensors, "GPU Core", "D3D 3D", "GPU");

            // Clock do Core (MHz)
            float? coreClock = FindSensorByAliases(
                sensors,
                SensorType.Clock,
                "GPU Core",
                "Core",
                "Graphics");

            // Consumo da GPU (Watts): "GPU Package", "GPU Power", "GPU Core", "Board Power"
            float? gpuPower = FindSensorByAliases(
                sensors,
                SensorType.Power,
                "GPU Package",
                "GPU Power",
                "GPU Core",
                "Board Power",
                "Total Board Power");

            // Define as métricas da GPU principal para o Overlay flutuante
            if (!primaryOverlayGpuSet || (metrics.GpuTemperature <= 0f && coreTemp is > 0f))
            {
                metrics.GpuTemperature = coreTemp ?? 0f;
                metrics.GpuLoad = coreLoad ?? 0f;
                metrics.GpuClock = coreClock ?? 0f;
                metrics.GpuPower = gpuPower ?? 0f;
                primaryOverlayGpuSet = true;
            }

            if (!readDetailed)
            {
                index++;
                continue;
            }

            // Telemetria avançada da GPU para as abas Visão Geral e Placa de Vídeo (GPU)
            string gpuId = gpu.Identifier?.ToString() ?? $"gpu_{index}";
            GpuSpecInfo? cachedSpec = _staticSpecs.Gpus.FirstOrDefault(g =>
                g.Id.Equals(gpuId, StringComparison.OrdinalIgnoreCase) ||
                g.Name.Equals(gpu.Name, StringComparison.OrdinalIgnoreCase));

            // Verifica se o sensor de VRAM Total do LHM ficou disponível após a atualização
            float? totalVramGb = cachedSpec?.TotalVramGb;
            bool isSharedVram = cachedSpec?.IsIntegratedOrSharedVram ?? true;
            string totalVramDisplay = cachedSpec?.TotalVramDisplay ?? "Compartilhada / Integrada";

            float? lhmTotalVramMb = FindSensorByAliases(
                sensors,
                SensorType.SmallData,
                "GPU Memory Total",
                "D3D Dedicated Memory Total");

            if (lhmTotalVramMb is > 640f) // > 0.625 GB dedicado
            {
                totalVramGb = lhmTotalVramMb.Value / 1024f;
                isSharedVram = false;
                totalVramDisplay = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{totalVramGb.Value:0.#} GB");
            }

            // Temperatura Hot Spot (°C)
            float? hotSpotTemp = FindSensorByAliases(
                sensors,
                SensorType.Temperature,
                "Hot Spot",
                "GPU Hot Spot",
                "Memory Junction");

            // Clock da Memória (MHz)
            float? memClock = FindSensorByAliases(
                sensors,
                SensorType.Clock,
                "GPU Memory",
                "Memory",
                "VRAM");

            // VRAM Usada: SensorType.SmallData com "GPU Memory Used" ou "D3D Dedicated Memory Used" (divide por 1024 para converter MB em GB)
            float? vramUsedMb = FindSensorByAliases(
                sensors,
                SensorType.SmallData,
                "GPU Memory Used",
                "D3D Dedicated Memory Used");

            float? vramUsedGb = vramUsedMb is > 0f ? vramUsedMb.Value / 1024f : null;
            if (vramUsedGb is null)
            {
                float? vramDataGb = FindSensorByAliases(
                    sensors,
                    SensorType.Data,
                    "GPU Memory Used",
                    "D3D Dedicated Memory Used");
                if (vramDataGb is > 0f)
                {
                    vramUsedGb = vramDataGb.Value > 128f ? vramDataGb.Value / 1024f : vramDataGb.Value;
                }
            }

            // Velocidade dos Fans (RPM e %)
            float? fanRpm = FindSensorByAliases(sensors, SensorType.Fan, "GPU Fan", "Fan");
            if (fanRpm is null)
            {
                foreach (ISensor s in sensors)
                {
                    if (s.SensorType == SensorType.Fan && IsValidSensorValue(s, out float rpmVal))
                    {
                        fanRpm = rpmVal;
                        break;
                    }
                }
            }

            float? fanPercent = FindSensorByAliases(sensors, SensorType.Control, "GPU Fan", "Fan");

            string vendor = gpu.HardwareType switch
            {
                HardwareType.GpuNvidia => "NVIDIA",
                HardwareType.GpuAmd => "AMD",
                HardwareType.GpuIntel => "Intel",
                _ => cachedSpec?.Vendor ?? "GPU"
            };

            metrics.Gpus.Add(new GpuDetailedMetrics
            {
                Id = gpuId,
                Name = !string.IsNullOrWhiteSpace(gpu.Name) ? gpu.Name.Trim() : cachedSpec?.Name ?? $"GPU #{index + 1}",
                Vendor = vendor,
                TotalVramGb = totalVramGb,
                IsIntegratedOrSharedVram = isSharedVram,
                TotalVramDisplay = totalVramDisplay,
                CoreLoadPercent = coreLoad,
                CoreTemperatureC = coreTemp,
                HotSpotTemperatureC = hotSpotTemp,
                CoreClockMhz = coreClock,
                MemoryClockMhz = memClock,
                VramUsedGb = vramUsedGb,
                PowerWatts = gpuPower,
                FanRpm = fanRpm,
                FanPercent = fanPercent
            });

            index++;
        }

        // Se nenhuma GPU foi exposta pelos sensores do LHM, mas há GPUs cacheadas no WMI/Registro
        if (readDetailed && metrics.Gpus.Count == 0 && _staticSpecs.Gpus.Count > 0)
        {
            foreach (GpuSpecInfo spec in _staticSpecs.Gpus)
            {
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

    private void ReadRamMetrics(IHardware memory, HardwareMetrics metrics)
    {
        if (memory.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
            return;

        float usedGb = 0f;
        float freeGb = 0f;
        float loadPct = 0f;

        foreach (ISensor sensor in memory.Sensors)
        {
            if (sensor.SensorType == SensorType.Load &&
                sensor.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase) &&
                IsValidSensorValue(sensor, out float loadVal))
            {
                loadPct = loadVal;
            }
            else if (sensor.SensorType == SensorType.Data &&
                     sensor.Name.Equals("Memory Used", StringComparison.OrdinalIgnoreCase) &&
                     IsValidSensorValue(sensor, out float usedVal))
            {
                usedGb = usedVal;
            }
            else if (sensor.SensorType == SensorType.Data &&
                     sensor.Name.Equals("Memory Available", StringComparison.OrdinalIgnoreCase) &&
                     IsValidSensorValue(sensor, out float availVal))
            {
                freeGb = availVal;
            }
        }

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

    private void ReadStorageMetrics(HardwareMetrics metrics)
    {
        // 1. Lê discos físicos e temperaturas via LibreHardwareMonitor (HardwareType.Storage)
        var physicalDisks = new List<PhysicalStorageMetrics>();

        foreach (IHardware storage in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage))
        {
            try
            {
                float? temp = FindSensorByAliases(
                    storage.Sensors,
                    SensorType.Temperature,
                    "Temperature",
                    "Composite",
                    "Drive Temperature",
                    "Airflow Temperature");

                if (temp is null)
                {
                    foreach (ISensor s in storage.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature && IsValidSensorValue(s, out float tVal))
                        {
                            temp = tVal;
                            break;
                        }
                    }
                }

                float? usedPct = FindSensorByAliases(storage.Sensors, SensorType.Load, "Used Space");

                physicalDisks.Add(new PhysicalStorageMetrics
                {
                    ModelName = !string.IsNullOrWhiteSpace(storage.Name) ? storage.Name.Trim() : "Unidade Física SSD/HDD",
                    TemperatureC = temp,
                    UsedSpacePercent = usedPct
                });
            }
            catch
            {
                // Ignora falha em um disco físico específico
            }
        }

        metrics.PhysicalDisks = physicalDisks;

        // 2. Lê partições lógicas fixas via System.IO.DriveInfo.GetDrives() (IsReady == true && DriveType.Fixed)
        int driveIndex = 0;
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    continue;

                string driveKey = drive.Name.TrimEnd('\\'); // Ex: "C:"
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
                    // Ignora se metadados secundários de volume falharem
                }

                // Associa o disco físico e sua temperatura atual (com fallback para "Sem sensor")
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
                // Isola erro em partição específica
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
            PhysicalStorageMetrics? matched = physicalDisks.FirstOrDefault(p =>
                p.ModelName.Contains(mappedModel, StringComparison.OrdinalIgnoreCase) ||
                mappedModel.Contains(p.ModelName, StringComparison.OrdinalIgnoreCase));

            if (matched is not null)
            {
                return (matched.ModelName, matched.TemperatureC);
            }

            // Se o WMI encontrou o modelo mas o LHM tem apenas 1 disco físico, usa a temperatura dele
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

    private static float? FindSensorByAliases(
        IEnumerable<ISensor> sensors,
        SensorType sensorType,
        params string[] aliases)
    {
        var candidates = sensors.Where(s => s.SensorType == sensorType).ToList();
        if (candidates.Count == 0)
            return null;

        // 1. Prioriza correspondência exata na ordem dos aliases
        foreach (string alias in aliases)
        {
            foreach (ISensor sensor in candidates)
            {
                if (sensor.Name.Equals(alias, StringComparison.OrdinalIgnoreCase) &&
                    IsValidSensorValue(sensor, out float exactValue))
                {
                    return exactValue;
                }
            }
        }

        // 2. Correspondência parcial (Contains) na ordem dos aliases
        foreach (string alias in aliases)
        {
            foreach (ISensor sensor in candidates)
            {
                if (sensor.Name.Contains(alias, StringComparison.OrdinalIgnoreCase) &&
                    IsValidSensorValue(sensor, out float partialValue))
                {
                    return partialValue;
                }
            }
        }

        return null;
    }

    private static float? FindLoadSensorByAliases(
        IEnumerable<ISensor> sensors,
        params string[] aliases)
    {
        var candidates = sensors.Where(s => s.SensorType == SensorType.Load).ToList();
        if (candidates.Count == 0)
            return null;

        foreach (string alias in aliases)
        {
            foreach (ISensor sensor in candidates)
            {
                if (sensor.Name.Equals(alias, StringComparison.OrdinalIgnoreCase) &&
                    IsValidLoadSensorValue(sensor, out float exactValue))
                {
                    return exactValue;
                }
            }
        }

        foreach (string alias in aliases)
        {
            foreach (ISensor sensor in candidates)
            {
                if (sensor.Name.Contains(alias, StringComparison.OrdinalIgnoreCase) &&
                    IsValidLoadSensorValue(sensor, out float partialValue))
                {
                    return partialValue;
                }
            }
        }

        return null;
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
}
