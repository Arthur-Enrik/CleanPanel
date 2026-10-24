using System.Globalization;
using System.IO;
using System.Management;
using CommunityToolkit.Mvvm.ComponentModel;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace HardwareOverlayApp.Models;

/// <summary>
/// Especificações estáticas e híbridas do sistema (lidas uma única vez na inicialização e cacheadas em memória).
/// Combina Registro do Windows, WMI (System.Management), System.IO.DriveInfo e LibreHardwareMonitor.
/// </summary>
public class HardwareSpecs
{
    // Sistema Operacional
    public string OsName { get; set; } = "Windows";
    public string OsEdition { get; set; } = "N/D";
    public string OsDisplayVersion { get; set; } = "N/D";
    public string OsBuild { get; set; } = "N/D";
    public string OsFullSummary => $"{OsName} ({OsDisplayVersion} - Build {OsBuild})";

    // Processador (CPU)
    public string CpuName { get; set; } = "Processador Desconhecido";
    public int CpuPhysicalCores { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public int CpuLogicalThreads { get; set; } = Environment.ProcessorCount;
    public string CpuCoresThreadsSummary => $"{CpuPhysicalCores} Núcleos Físicos / {CpuLogicalThreads} Threads Lógicos";

    // Memória RAM Total
    public float TotalRamGb { get; set; }

    // GPUs detectadas (estático + híbrido)
    public List<GpuSpecInfo> Gpus { get; set; } = [];

    // Mapeamento estático de partições lógicas (ex: "C:") para modelo do disco físico e capacidade
    public Dictionary<string, string> LogicalDriveToPhysicalModel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> PartitionStaticTotalGb { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Carrega todas as especificações estáticas de forma blindada (try/catch isolado por categoria).
    /// </summary>
    public static HardwareSpecs LoadStaticSpecs(IComputer? computer)
    {
        var specs = new HardwareSpecs();

        LoadOsSpecsFromRegistry(specs);
        LoadCpuSpecsFromWmiAndLhm(specs, computer);
        LoadTotalRamSpecs(specs, computer);
        LoadStorageStaticMappings(specs);
        LoadGpuStaticSpecs(specs, computer);

        return specs;
    }

    private static void LoadOsSpecsFromRegistry(HardwareSpecs specs)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is null)
                return;

            string productName = key.GetValue("ProductName")?.ToString()?.Trim() ?? "Microsoft Windows";
            string displayVersion = key.GetValue("DisplayVersion")?.ToString()?.Trim()
                                    ?? key.GetValue("ReleaseId")?.ToString()?.Trim()
                                    ?? "N/D";
            string currentBuild = key.GetValue("CurrentBuild")?.ToString()?.Trim()
                                  ?? key.GetValue("CurrentBuildNumber")?.ToString()?.Trim()
                                  ?? "0";
            string ubr = key.GetValue("UBR")?.ToString()?.Trim() ?? string.Empty;
            string editionId = key.GetValue("EditionID")?.ToString()?.Trim() ?? "Standard";

            // Corrige a detecção do Windows 11 (Build >= 22000 onde o ProductName no registro ainda reporta Windows 10)
            if (int.TryParse(currentBuild, out int buildNumber) && buildNumber >= 22000)
            {
                if (productName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
                {
                    productName = productName.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
                }
                else if (!productName.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
                {
                    productName = $"Windows 11 ({productName})";
                }
            }

            specs.OsName = productName;
            specs.OsEdition = !string.IsNullOrWhiteSpace(displayVersion) && displayVersion != "N/D"
                ? $"{editionId} ({displayVersion})"
                : editionId;
            specs.OsDisplayVersion = displayVersion;
            specs.OsBuild = !string.IsNullOrWhiteSpace(ubr) ? $"{currentBuild}.{ubr}" : currentBuild;
        }
        catch
        {
            specs.OsName = RuntimeInformationFallbackOsName();
            specs.OsEdition = "Windows NT";
            specs.OsBuild = Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string RuntimeInformationFallbackOsName()
    {
        int build = Environment.OSVersion.Version.Build;
        return build >= 22000 ? "Microsoft Windows 11" : "Microsoft Windows 10";
    }

    private static void LoadCpuSpecsFromWmiAndLhm(HardwareSpecs specs, IComputer? computer)
    {
        string? cpuName = null;
        int physicalCores = 0;
        int logicalProcessors = 0;

        // 1. Tenta obter via WMI (Win32_Processor)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementBaseObject obj in results)
            {
                if (string.IsNullOrWhiteSpace(cpuName))
                {
                    cpuName = obj["Name"]?.ToString()?.Trim();
                }

                if (obj["NumberOfCores"] is not null &&
                    int.TryParse(obj["NumberOfCores"].ToString(), out int cores) &&
                    cores > 0)
                {
                    physicalCores += cores;
                }

                if (obj["NumberOfLogicalProcessors"] is not null &&
                    int.TryParse(obj["NumberOfLogicalProcessors"].ToString(), out int logical) &&
                    logical > 0)
                {
                    logicalProcessors += logical;
                }
            }
        }
        catch
        {
            // Fallback seguro abaixo caso o serviço WMI falhe
        }

        // 2. Complementa o nome com o LibreHardwareMonitor caso o WMI não tenha retornado
        if (string.IsNullOrWhiteSpace(cpuName) && computer is not null)
        {
            try
            {
                IHardware? cpuHw = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                if (!string.IsNullOrWhiteSpace(cpuHw?.Name))
                {
                    cpuName = cpuHw.Name.Trim();
                }
            }
            catch
            {
                // Ignora falha de leitura secundária
            }
        }

        // 3. Fallbacks seguros para Environment.ProcessorCount
        if (logicalProcessors <= 0)
        {
            logicalProcessors = Math.Max(1, Environment.ProcessorCount);
        }

        if (physicalCores <= 0)
        {
            physicalCores = Math.Max(1, logicalProcessors / 2);
        }

        specs.CpuName = !string.IsNullOrWhiteSpace(cpuName) ? cpuName : "Processador x64";
        specs.CpuPhysicalCores = physicalCores;
        specs.CpuLogicalThreads = logicalProcessors;
    }

    private static void LoadTotalRamSpecs(HardwareSpecs specs, IComputer? computer)
    {
        try
        {
            // Tenta obter via GC MemoryInfo (.NET 8 nativo)
            long totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (totalBytes > 0)
            {
                specs.TotalRamGb = (float)(totalBytes / (1024.0 * 1024.0 * 1024.0));
                return;
            }
        }
        catch
        {
            //Continua para fallback WMI
        }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            using ManagementObjectCollection results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            {
                if (obj["TotalPhysicalMemory"] is not null &&
                    ulong.TryParse(obj["TotalPhysicalMemory"].ToString(), out ulong totalBytes) &&
                    totalBytes > 0)
                {
                    specs.TotalRamGb = (float)(totalBytes / (1024.0 * 1024.0 * 1024.0));
                    return;
                }
            }
        }
        catch
        {
            // Fallback posterior pelos sensores Memory Used + Memory Available do LHM
        }
    }

    private static void LoadStorageStaticMappings(HardwareSpecs specs)
    {
        // 1. Capacidade total estática das partições fixas
        try
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                        continue;

                    string key = drive.Name.TrimEnd('\\');
                    double totalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    specs.PartitionStaticTotalGb[key] = totalGb;
                }
                catch
                {
                    // Ignora unidade inacessível individualmente
                }
            }
        }
        catch
        {
            // Ignora falha na enumeração de unidades
        }

        // 2. Associação WMI entre Partições Lógicas (C:, D:) e Modelo do Disco Físico (Win32_DiskDrive)
        try
        {
            using var diskSearcher = new ManagementObjectSearcher("SELECT DeviceID, Model FROM Win32_DiskDrive");
            using ManagementObjectCollection disks = diskSearcher.Get();

            foreach (ManagementBaseObject disk in disks)
            {
                try
                {
                    string? deviceId = disk["DeviceID"]?.ToString();
                    string? model = disk["Model"]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(model))
                        continue;

                    string escapedDeviceId = deviceId.Replace("\\", "\\\\");
                    using var partitionSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{escapedDeviceId}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition");
                    using ManagementObjectCollection partitions = partitionSearcher.Get();

                    foreach (ManagementBaseObject partition in partitions)
                    {
                        string? partId = partition["DeviceID"]?.ToString();
                        if (string.IsNullOrWhiteSpace(partId))
                            continue;

                        using var logicalSearcher = new ManagementObjectSearcher(
                            $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partId}'}} WHERE AssocClass = Win32_LogicalDiskToPartition");
                        using ManagementObjectCollection logicalDisks = logicalSearcher.Get();

                        foreach (ManagementBaseObject logical in logicalDisks)
                        {
                            string? driveLetter = logical["DeviceID"]?.ToString()?.Trim()?.TrimEnd('\\');
                            if (!string.IsNullOrWhiteSpace(driveLetter))
                            {
                                specs.LogicalDriveToPhysicalModel[driveLetter] = model;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignora falha em um disco específico e prossegue para os demais
                }
            }
        }
        catch
        {
            // Caso o WMI de associação de disco falhe, o fallback por HardwareType.Storage será usado
        }
    }

    private static void LoadGpuStaticSpecs(HardwareSpecs specs, IComputer? computer)
    {
        var registryVramMap = ReadGpuVramFromRegistry();
        var wmiVramMap = ReadGpuVramFromWmi();

        if (computer is not null)
        {
            try
            {
                int index = 0;
                foreach (IHardware hw in computer.Hardware)
                {
                    if (hw.HardwareType is not (HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
                        continue;

                    try
                    {
                        hw.Update();
                    }
                    catch
                    {
                        // Continua mesmo se a primeira atualização da GPU falhar
                    }

                    string gpuName = !string.IsNullOrWhiteSpace(hw.Name) ? hw.Name.Trim() : $"GPU #{index + 1}";
                    string vendor = hw.HardwareType switch
                    {
                        HardwareType.GpuNvidia => "NVIDIA",
                        HardwareType.GpuAmd => "AMD",
                        HardwareType.GpuIntel => "Intel",
                        _ => "GPU"
                    };

                    var (vramGb, isShared) = ResolveTotalVramForGpu(hw, gpuName, registryVramMap, wmiVramMap);

                    specs.Gpus.Add(new GpuSpecInfo
                    {
                        Id = hw.Identifier?.ToString() ?? $"gpu_{index}",
                        Name = gpuName,
                        Vendor = vendor,
                        HardwareType = hw.HardwareType,
                        TotalVramGb = vramGb,
                        IsIntegratedOrSharedVram = isShared,
                        TotalVramDisplay = vramGb is > 0f && !isShared
                            ? string.Create(CultureInfo.InvariantCulture, $"{vramGb.Value:0.#} GB")
                            : "Compartilhada / Integrada"
                    });

                    index++;
                }
            }
            catch
            {
                // Fallback abaixo caso nenhuma GPU tenha sido lida do LHM
            }
        }

        // Se o LHM não reportou GPUs, tenta listar via WMI como fallback
        if (specs.Gpus.Count == 0 && wmiVramMap.Count > 0)
        {
            int idx = 0;
            foreach (var kvp in wmiVramMap)
            {
                float? gb = kvp.Value > 0.5f ? kvp.Value : null;
                bool isShared = gb is null;
                specs.Gpus.Add(new GpuSpecInfo
                {
                    Id = $"wmi_gpu_{idx++}",
                    Name = kvp.Key,
                    Vendor = kvp.Key.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                           : kvp.Key.Contains("AMD", StringComparison.OrdinalIgnoreCase) || kvp.Key.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD"
                           : "Intel",
                    HardwareType = HardwareType.GpuIntel,
                    TotalVramGb = gb,
                    IsIntegratedOrSharedVram = isShared,
                    TotalVramDisplay = gb is > 0f && !isShared
                        ? string.Create(CultureInfo.InvariantCulture, $"{gb.Value:0.#} GB")
                        : "Compartilhada / Integrada"
                });
            }
        }
    }

    public static (float? VramGb, bool IsShared) ResolveTotalVramForGpu(
        IHardware gpuHardware,
        string gpuName,
        Dictionary<string, float> registryVramMap,
        Dictionary<string, float> wmiVramMap)
    {
        bool looksIntegrated = LooksLikeIntegratedGpu(gpuHardware.HardwareType, gpuName);

        // 1. Busca primeiro o sensor do LHM: SensorType.SmallData com nome "GPU Memory Total" ou "D3D Dedicated Memory Total" (em MB -> GB)
        try
        {
            ISensor? lhmTotalSensor = gpuHardware.Sensors.FirstOrDefault(s =>
                s.SensorType == SensorType.SmallData &&
                s.Name.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase));

            lhmTotalSensor ??= gpuHardware.Sensors.FirstOrDefault(s =>
                s.SensorType == SensorType.SmallData &&
                s.Name.Equals("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase));

            lhmTotalSensor ??= gpuHardware.Sensors.FirstOrDefault(s =>
                s.SensorType == SensorType.SmallData &&
                (s.Name.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase) ||
                 s.Name.Contains("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase)));

            if (lhmTotalSensor?.Value is float mbVal &&
                !float.IsNaN(mbVal) &&
                !float.IsInfinity(mbVal) &&
                mbVal > 0f)
            {
                float gb = mbVal / 1024f;
                // GPUs integradas costumam alocar apenas 128MB ~ 512MB fixos de BIOS para D3D Dedicated e o resto compartilhado
                if (looksIntegrated && gb <= 0.6f)
                {
                    return (null, true);
                }

                return (gb, false);
            }
        }
        catch
        {
            // Prossegue para o fallback de Registro / WMI
        }

        // 2. Fallback no Registro do Windows (Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318})
        foreach (var kvp in registryVramMap)
        {
            if (NamesMatchGpu(gpuName, kvp.Key) && kvp.Value > 0f)
            {
                if (looksIntegrated && kvp.Value <= 0.6f)
                    return (null, true);

                return (kvp.Value, false);
            }
        }

        // 3. Fallback no WMI (Win32_VideoController)
        foreach (var kvp in wmiVramMap)
        {
            if (NamesMatchGpu(gpuName, kvp.Key) && kvp.Value > 0f)
            {
                if (looksIntegrated && kvp.Value <= 0.6f)
                    return (null, true);

                return (kvp.Value, false);
            }
        }

        // 4. Se não existir sensor dedicado ou for memória dinâmica compartilhada
        return (null, true);
    }

    private static bool LooksLikeIntegratedGpu(HardwareType type, string name)
    {
        if (type == HardwareType.GpuIntel && !name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AMD Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AMD Radeon Graphics", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Vega", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool NamesMatchGpu(string lhmName, string otherName)
    {
        if (string.IsNullOrWhiteSpace(lhmName) || string.IsNullOrWhiteSpace(otherName))
            return false;

        return lhmName.Contains(otherName, StringComparison.OrdinalIgnoreCase) ||
               otherName.Contains(lhmName, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, float> ReadGpuVramFromRegistry()
    {
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using RegistryKey? baseKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (baseKey is null)
                return result;

            foreach (string subKeyName in baseKey.GetSubKeyNames())
            {
                if (!subKeyName.StartsWith('0'))
                    continue;

                try
                {
                    using RegistryKey? subKey = baseKey.OpenSubKey(subKeyName);
                    if (subKey is null)
                        continue;

                    string? desc = subKey.GetValue("DriverDesc")?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(desc))
                        continue;

                    long bytes = 0;
                    object? qwMem = subKey.GetValue("HardwareInformation.qwMemorySize");
                    if (qwMem is long qword && qword > 0)
                    {
                        bytes = qword;
                    }
                    else if (qwMem is byte[] qwBytes && qwBytes.Length >= 8)
                    {
                        bytes = BitConverter.ToInt64(qwBytes, 0);
                    }
                    else
                    {
                        object? memSize = subKey.GetValue("HardwareInformation.MemorySize");
                        if (memSize is int dword && dword > 0)
                        {
                            bytes = (uint)dword;
                        }
                        else if (memSize is byte[] dwBytes && dwBytes.Length >= 4)
                        {
                            bytes = BitConverter.ToUInt32(dwBytes, 0);
                        }
                    }

                    if (bytes > 0)
                    {
                        result[desc] = (float)(bytes / (1024.0 * 1024.0 * 1024.0));
                    }
                }
                catch
                {
                    // Ignora subchave individual inacessível
                }
            }
        }
        catch
        {
            // Ignora falha geral de leitura do registro de vídeo
        }

        return result;
    }

    private static Dictionary<string, float> ReadGpuVramFromWmi()
    {
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            using ManagementObjectCollection items = searcher.Get();
            foreach (ManagementBaseObject obj in items)
            {
                try
                {
                    string? name = obj["Name"]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (obj["AdapterRAM"] is not null &&
                        ulong.TryParse(obj["AdapterRAM"].ToString(), out ulong bytes) &&
                        bytes > 0)
                    {
                        result[name] = (float)(bytes / (1024.0 * 1024.0 * 1024.0));
                    }
                    else if (!result.ContainsKey(name))
                    {
                        result[name] = 0f;
                    }
                }
                catch
                {
                    // Ignora adaptador individual
                }
            }
        }
        catch
        {
            // Ignora falha WMI de vídeo
        }

        return result;
    }
}

public class GpuSpecInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "GPU Desconhecida";
    public string Vendor { get; set; } = "GPU";
    public HardwareType HardwareType { get; set; }
    public float? TotalVramGb { get; set; }
    public bool IsIntegratedOrSharedVram { get; set; }
    public string TotalVramDisplay { get; set; } = "Compartilhada / Integrada";
}

/// <summary>
/// Telemetria individual por núcleo/thread da CPU (Per-Core Telemetry).
/// </summary>
public class CpuCoreMetric : ObservableObject
{
    public int CoreIndex { get; set; }
    public string CoreName { get; set; } = string.Empty;
    public float? LoadPercent { get; set; }
    public float? ClockMhz { get; set; }

    public double LoadBarValue => LoadPercent is > 0f ? Math.Clamp(LoadPercent.Value, 0f, 100f) : 0.0;

    public string LoadText => LoadPercent is not null && !float.IsNaN(LoadPercent.Value) && LoadPercent.Value >= 0f
        ? $"{LoadPercent.Value:0} %"
        : "N/D";

    public string ClockText => ClockMhz is > 0f
        ? $"{ClockMhz.Value:0} MHz"
        : "N/D";

    public void UpdateFrom(CpuCoreMetric other)
    {
        CoreName = other.CoreName;
        LoadPercent = other.LoadPercent;
        ClockMhz = other.ClockMhz;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>
/// Telemetria completa de uma Placa de Vídeo (suporte Multi-GPU: Integrada + Dedicada).
/// </summary>
public class GpuDetailedMetrics : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Placa de Vídeo";
    public string Vendor { get; set; } = "GPU";
    public float? TotalVramGb { get; set; }
    public bool IsIntegratedOrSharedVram { get; set; }
    public string TotalVramDisplay { get; set; } = "Compartilhada / Integrada";

    public float? CoreLoadPercent { get; set; }
    public float? CoreTemperatureC { get; set; }
    public float? HotSpotTemperatureC { get; set; }
    public float? CoreClockMhz { get; set; }
    public float? MemoryClockMhz { get; set; }
    public float? VramUsedGb { get; set; }
    public float? PowerWatts { get; set; }
    public float? FanRpm { get; set; }
    public float? FanPercent { get; set; }

    public string DisplaySelectorTitle => $"{Name} ({TotalVramDisplay})";

    public double CoreLoadBarValue => CoreLoadPercent is > 0f ? Math.Clamp(CoreLoadPercent.Value, 0f, 100f) : 0.0;

    public string CoreLoadText => CoreLoadPercent is not null && !float.IsNaN(CoreLoadPercent.Value) && CoreLoadPercent.Value >= 0f
        ? $"{CoreLoadPercent.Value:0} %"
        : "N/D";

    public string CoreTemperatureText => CoreTemperatureC is > 0f
        ? $"{CoreTemperatureC.Value:0} °C"
        : "N/D";

    public string HotSpotTemperatureText => HotSpotTemperatureC is > 0f
        ? $"{HotSpotTemperatureC.Value:0} °C"
        : "N/D";

    public string CoreClockText => CoreClockMhz is > 0f
        ? $"{CoreClockMhz.Value:0} MHz"
        : "N/D";

    public string MemoryClockText => MemoryClockMhz is > 0f
        ? $"{MemoryClockMhz.Value:0} MHz"
        : "N/D";

    public string PowerText => PowerWatts is > 0f
        ? string.Create(CultureInfo.InvariantCulture, $"{PowerWatts.Value:0.0} W")
        : "N/D";

    public string VramUsageText
    {
        get
        {
            if (VramUsedGb is > 0f)
            {
                string usedStr = string.Create(CultureInfo.InvariantCulture, $"{VramUsedGb.Value:0.00} GB");
                if (TotalVramGb is > 0f && !IsIntegratedOrSharedVram)
                {
                    string totalStr = string.Create(CultureInfo.InvariantCulture, $"{TotalVramGb.Value:0.#} GB");
                    double pct = Math.Clamp((VramUsedGb.Value / TotalVramGb.Value) * 100.0, 0.0, 100.0);
                    return $"{usedStr} / {totalStr} ({pct:0}%)";
                }

                return $"{usedStr} ({TotalVramDisplay})";
            }

            return TotalVramGb is > 0f && !IsIntegratedOrSharedVram
                ? $"N/D (Total: {TotalVramDisplay})"
                : TotalVramDisplay;
        }
    }

    public double VramUsagePercent =>
        VramUsedGb is > 0f && TotalVramGb is > 0f && !IsIntegratedOrSharedVram
            ? Math.Clamp((VramUsedGb.Value / TotalVramGb.Value) * 100.0, 0.0, 100.0)
            : 0.0;

    public string FanText
    {
        get
        {
            bool hasRpm = FanRpm is > 0f;
            bool hasPct = FanPercent is > 0f;

            if (hasRpm && hasPct)
                return $"{FanRpm!.Value:0} RPM ({FanPercent!.Value:0}%)";
            if (hasRpm)
                return $"{FanRpm!.Value:0} RPM";
            if (hasPct)
                return $"{FanPercent!.Value:0} %";

            return "N/D";
        }
    }

    public void UpdateFrom(GpuDetailedMetrics other)
    {
        Name = other.Name;
        Vendor = other.Vendor;
        TotalVramGb = other.TotalVramGb;
        IsIntegratedOrSharedVram = other.IsIntegratedOrSharedVram;
        TotalVramDisplay = other.TotalVramDisplay;
        CoreLoadPercent = other.CoreLoadPercent;
        CoreTemperatureC = other.CoreTemperatureC;
        HotSpotTemperatureC = other.HotSpotTemperatureC;
        CoreClockMhz = other.CoreClockMhz;
        MemoryClockMhz = other.MemoryClockMhz;
        VramUsedGb = other.VramUsedGb;
        PowerWatts = other.PowerWatts;
        FanRpm = other.FanRpm;
        FanPercent = other.FanPercent;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>
/// Telemetria de Armazenamento combinando Partições Lógicas (DriveInfo) + Sensores Físicos (HardwareType.Storage).
/// </summary>
public class StorageDriveMetrics : ObservableObject
{
    public string DriveName { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public string FileSystem { get; set; } = "NTFS";
    public string PhysicalDiskModel { get; set; } = "Unidade de Armazenamento";
    public double TotalSizeGb { get; set; }
    public double UsedSizeGb { get; set; }
    public double FreeSizeGb { get; set; }
    public double UsagePercent { get; set; }
    public float? TemperatureC { get; set; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(VolumeLabel)
        ? $"Unidade {DriveName}"
        : $"Unidade {DriveName} ({VolumeLabel})";

    public string CapacitySummaryText => string.Create(
        CultureInfo.InvariantCulture,
        $"{UsedSizeGb:0.0} GB usados de {TotalSizeGb:0.0} GB");

    public string FreeSpaceSummaryText => string.Create(
        CultureInfo.InvariantCulture,
        $"{FreeSizeGb:0.0} GB livres ({UsagePercent:0}% ocupado)");

    public string OverviewLineText => string.Create(
        CultureInfo.InvariantCulture,
        $"Total: {TotalSizeGb:0.0} GB  •  Usado: {UsedSizeGb:0.0} GB  •  Livre: {FreeSizeGb:0.0} GB ({UsagePercent:0}%)");

    public string TemperatureText => TemperatureC is > 0f
        ? $"{TemperatureC.Value:0} °C"
        : "Sem sensor";

    public bool HasTemperatureSensor => TemperatureC is > 0f;

    public void UpdateFrom(StorageDriveMetrics other)
    {
        VolumeLabel = other.VolumeLabel;
        FileSystem = other.FileSystem;
        PhysicalDiskModel = other.PhysicalDiskModel;
        TotalSizeGb = other.TotalSizeGb;
        UsedSizeGb = other.UsedSizeGb;
        FreeSizeGb = other.FreeSizeGb;
        UsagePercent = other.UsagePercent;
        TemperatureC = other.TemperatureC;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>
/// Representa um disco físico detectado diretamente pelo LibreHardwareMonitor (HardwareType.Storage).
/// </summary>
public class PhysicalStorageMetrics : ObservableObject
{
    public string ModelName { get; set; } = "Disco Físico";
    public float? TemperatureC { get; set; }
    public float? UsedSpacePercent { get; set; }

    public string TemperatureText => TemperatureC is > 0f
        ? $"{TemperatureC.Value:0} °C"
        : "Sem sensor";

    public bool HasTemperatureSensor => TemperatureC is > 0f;

    public void UpdateFrom(PhysicalStorageMetrics other)
    {
        ModelName = other.ModelName;
        TemperatureC = other.TemperatureC;
        UsedSpacePercent = other.UsedSpacePercent;
        OnPropertyChanged(string.Empty);
    }
}
