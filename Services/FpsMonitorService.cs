using System.Diagnostics;
using HardwareOverlayApp.Helpers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace HardwareOverlayApp.Services;

public class FpsMonitorService : IDisposable
{
    private const string SessionName = "CleanPanelFpsSession";
    private const string LegacySessionName = "HardwareOverlayFpsSession";

    // Microsoft-Windows-DXGI (Present_Start = EventID 42)
    private static readonly Guid DxgiProviderGuid = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private const int DxgiPresentStartEventId = 42;

    // Microsoft-Windows-D3D9 (Present_Start = EventID 1)
    private static readonly Guid D3D9ProviderGuid = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int D3D9PresentStartEventId = 1;

    // Microsoft-Windows-Dwm-Core (Schedule_Present_Start = EventID 15, Commit/FlipChain = EventID 46)
    private static readonly Guid DwmProviderGuid = new("9E9BBA3C-2E38-40CB-99F4-9E8281425158");
    private const int DwmSchedulePresentStartEventId = 15;
    private const int DwmFlipChainEventId = 46;

    // Chave reservada no dicionário para armazenar quadros capturados via fallback DWM
    private const ulong DwmSwapChainKey = ulong.MaxValue;

    // Parâmetros de janela deslizante de frametime, suavização e fallbacks
    private const double SlidingWindowSeconds = 0.6; // 600 ms
    private const double DwmFallbackThresholdSeconds = 0.5; // 500 ms
    private const double MinValidDeltaMs = 0.5; // Descarta micro-deltas > 2000 FPS (artefatos/duplicatas)
    private const double EmaSignificantDeltaRatio = 0.20; // > 20% atualiza instantaneamente
    private const int EtWBufferSizeMb = 4;

    private readonly object _framesLock = new();
    private readonly object _sessionLock = new();
    private readonly Dictionary<ulong, List<long>> _swapChainTimestamps = new();
    private readonly Dictionary<ulong, long> _swapChainLastTicks = new();
    private readonly List<ulong> _staleKeysBuffer = new(16);
    private readonly int _currentAppPid = Environment.ProcessId;

    private TraceEventSession? _session;
    private Thread? _etwThread;

    // PID em foco cacheado (atualizado 1x por segundo fora do hot-path ETW)
    private volatile int _activeForegroundPid = -1;
    private int _lastEvaluatedRawPid = -1;

    private long _lastDirectGraphicsEventTick;
    private long _lastDwmEvent15Tick;
    private double _smoothedFps;

    private volatile bool _isDisposed;
    private volatile bool _isSuspended;

    public bool IsSuspended
    {
        get => _isSuspended;
        set
        {
            _isSuspended = value;
            if (value)
            {
                _activeForegroundPid = -1;
                _lastEvaluatedRawPid = -1;
                lock (_framesLock)
                {
                    ClearFrameHistoryLocked();
                }
            }
        }
    }

    public int CurrentFps
    {
        get
        {
            lock (_framesLock)
            {
                UpdateSmoothedFpsLocked();
                return (int)Math.Round(_smoothedFps);
            }
        }
    }

    public void Start()
    {
        if (_isDisposed || _etwThread is not null)
            return;

        RefreshActiveForegroundProcess();

        _etwThread = new Thread(RunEtwSession)
        {
            Name = "CleanPanelEtwFpsThread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _etwThread.Start();
    }

    /// <summary>
    /// Atualiza o PID do processo em primeiro plano apenas 1 vez por segundo (fora do callback de alta frequência do ETW).
    /// </summary>
    public void RefreshActiveForegroundProcess()
    {
        if (_isDisposed || _isSuspended)
        {
            _activeForegroundPid = -1;
            return;
        }

        try
        {
            int rawPid = WindowWin32Helper.GetForegroundProcessId();
            if (rawPid == _lastEvaluatedRawPid)
                return;

            _lastEvaluatedRawPid = rawPid;

            if (rawPid <= 4 || rawPid == _currentAppPid || IsSystemShellOrDwmProcess(rawPid))
            {
                _activeForegroundPid = -1;
                lock (_framesLock)
                {
                    ClearFrameHistoryLocked();
                }
                return;
            }

            lock (_framesLock)
            {
                ClearFrameHistoryLocked();
            }

            _activeForegroundPid = rawPid;
        }
        catch
        {
            _activeForegroundPid = -1;
        }
    }

    private void RunEtwSession()
    {
        TraceEventSession? session = null;

        try
        {
            lock (_sessionLock)
            {
                if (_isDisposed)
                    return;

                // Limpeza preventiva de sessões ETW órfãs deixadas por encerramentos abruptos anteriores
                CleanupOrphanSession(LegacySessionName);
                CleanupOrphanSession(SessionName);

                if (_isDisposed)
                    return;

                session = new TraceEventSession(SessionName)
                {
                    StopOnDispose = true,
                    BufferSizeMB = EtWBufferSizeMb
                };

                session.Source.AllEvents += OnEtwEvent;

                session.EnableProvider(DxgiProviderGuid, TraceEventLevel.Informational);
                session.EnableProvider(D3D9ProviderGuid, TraceEventLevel.Informational);
                session.EnableProvider(DwmProviderGuid, TraceEventLevel.Informational);

                _session = session;
            }

            // Loop bloqueante em thread dedicada para entrega em tempo real sem rajadas do ThreadPool
            session.Source.Process();
        }
        catch
        {
            // Mantém o aplicativo estável caso falte privilégio de administrador ou ocorra interrupção externa da sessão
        }
        finally
        {
            lock (_sessionLock)
            {
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                }
            }

            CloseSessionSafe(session);
        }
    }

    private static void CleanupOrphanSession(string sessionName)
    {
        try
        {
            using TraceEventSession? existingSession = TraceEventSession.GetActiveSession(sessionName);
            if (existingSession is not null)
            {
                existingSession.StopOnDispose = true;
                existingSession.Stop();
            }
        }
        catch
        {
            // Ignora caso não exista sessão órfã ativa
        }
    }

    private void CloseSessionSafe(TraceEventSession? session)
    {
        if (session is null)
            return;

        try
        {
            session.Source.AllEvents -= OnEtwEvent;
            session.Stop();
            session.Dispose();
        }
        catch
        {
            // Ignora erros no encerramento da sessão ETW
        }
    }

    private void OnEtwEvent(TraceEvent data)
    {
        if (_isDisposed || _isSuspended)
            return;

        Guid provider = data.ProviderGuid;
        TraceEventID eventId = data.ID;

        bool isDxgiPresent = provider == DxgiProviderGuid && eventId == (TraceEventID)DxgiPresentStartEventId;
        bool isD3D9Present = !isDxgiPresent && provider == D3D9ProviderGuid && eventId == (TraceEventID)D3D9PresentStartEventId;

        if (isDxgiPresent || isD3D9Present)
        {
            // 1ª linha obrigatória do callback DXGI/D3D9: filtro O(1) por PID ativo
            if (data.ProcessID != _activeForegroundPid || _activeForegroundPid <= 0)
                return;

            ulong swapChain = isD3D9Present ? 1UL : 0UL;
            try
            {
                object? rawSwapChain = data.PayloadByName("SwapChainAddress");
                if (rawSwapChain is ulong unsignedAddr)
                {
                    if (unsignedAddr != 0 || isDxgiPresent)
                    {
                        swapChain = unsignedAddr;
                    }
                }
                else if (rawSwapChain is long signedAddr)
                {
                    ulong converted = unchecked((ulong)signedAddr);
                    if (converted != 0 || isDxgiPresent)
                    {
                        swapChain = converted;
                    }
                }
                else if (rawSwapChain is not null)
                {
                    ulong converted = Convert.ToUInt64(rawSwapChain);
                    if (converted != 0 || isDxgiPresent)
                    {
                        swapChain = converted;
                    }
                }

                if (swapChain == DwmSwapChainKey)
                {
                    swapChain = isD3D9Present ? 1UL : 0UL;
                }
            }
            catch
            {
                // Mantém o endereço fallback caso o payload do evento ETW seja ausente ou malformado
            }

            long now = Stopwatch.GetTimestamp();
            lock (_framesLock)
            {
                _lastDirectGraphicsEventTick = now;
                RecordTimestampLocked(swapChain, now);
            }

            return;
        }

        if (provider == DwmProviderGuid &&
            (eventId == (TraceEventID)DwmSchedulePresentStartEventId || eventId == (TraceEventID)DwmFlipChainEventId))
        {
            if (_activeForegroundPid <= 0)
                return;

            try
            {
                long now = Stopwatch.GetTimestamp();
                long fallbackThresholdTicks = (long)(Stopwatch.Frequency * DwmFallbackThresholdSeconds);

                lock (_framesLock)
                {
                    // Se o processo ativo está emitindo DXGI/D3D9 diretamente, ignora o fallback do compositor DWM
                    if (_lastDirectGraphicsEventTick > 0 &&
                        (now - _lastDirectGraphicsEventTick) <= fallbackThresholdTicks)
                    {
                        return;
                    }

                    // Evita dupla contagem quando DWM emite ID 15 e ID 46 dentro do mesmo ciclo de composição
                    if (eventId == (TraceEventID)DwmSchedulePresentStartEventId)
                    {
                        _lastDwmEvent15Tick = now;
                    }
                    else if (_lastDwmEvent15Tick > 0 &&
                             (now - _lastDwmEvent15Tick) < fallbackThresholdTicks)
                    {
                        return;
                    }

                    RecordTimestampLocked(DwmSwapChainKey, now);
                }
            }
            catch
            {
                // Ignora silenciosamente falhas no parser de eventos DWM
            }
        }
    }

    private void RecordTimestampLocked(ulong swapChainKey, long now)
    {
        if (!_swapChainTimestamps.TryGetValue(swapChainKey, out List<long>? timestamps))
        {
            timestamps = new List<long>(128);
            _swapChainTimestamps[swapChainKey] = timestamps;
        }

        timestamps.Add(now);
        _swapChainLastTicks[swapChainKey] = now;

        // Evita acúmulo excessivo em memória se CurrentFps ficar temporariamente sem leitura
        if (timestamps.Count > 2048)
        {
            PruneOlderThan(timestamps, now - Stopwatch.Frequency);
        }
    }

    private void UpdateSmoothedFpsLocked()
    {
        if (_isDisposed || _isSuspended || _activeForegroundPid <= 0)
        {
            _smoothedFps = 0.0;
            return;
        }

        long now = Stopwatch.GetTimestamp();
        long windowCutoff = now - (long)(Stopwatch.Frequency * SlidingWindowSeconds);
        long idleCutoff = now - Stopwatch.Frequency; // 1000 ms
        long dwmFallbackThresholdTicks = (long)(Stopwatch.Frequency * DwmFallbackThresholdSeconds);

        bool hasRecentDirectEvents = _lastDirectGraphicsEventTick > 0 &&
                                     (now - _lastDirectGraphicsEventTick) <= dwmFallbackThresholdTicks;

        // 1. Atualiza janela deslizante (600ms) e identifica SwapChains ociosas (> 1000ms)
        _staleKeysBuffer.Clear();
        long mostRecentCandidateTick = 0;
        long mostRecentAnyTick = 0;

        foreach (KeyValuePair<ulong, List<long>> kvp in _swapChainTimestamps)
        {
            ulong key = kvp.Key;
            PruneOlderThan(kvp.Value, windowCutoff);

            long lastTick = _swapChainLastTicks.GetValueOrDefault(key);
            if (lastTick < idleCutoff)
            {
                _staleKeysBuffer.Add(key);
                continue;
            }

            if (lastTick > mostRecentAnyTick)
            {
                mostRecentAnyTick = lastTick;
            }

            bool isCandidate = hasRecentDirectEvents ? key != DwmSwapChainKey : key == DwmSwapChainKey;
            if (isCandidate && lastTick > mostRecentCandidateTick)
            {
                mostRecentCandidateTick = lastTick;
            }
        }

        for (int i = 0; i < _staleKeysBuffer.Count; i++)
        {
            ulong staleKey = _staleKeysBuffer[i];
            _swapChainTimestamps.Remove(staleKey);
            _swapChainLastTicks.Remove(staleKey);
        }

        // 2. Timeout de ociosidade: se nenhuma SwapChain renderizou nos últimos 1000ms, zera histórico e retorna 0 FPS
        if (mostRecentAnyTick == 0 || mostRecentAnyTick < idleCutoff)
        {
            ClearFrameHistoryLocked();
            return;
        }

        // Se nenhum evento direto chegou nos últimos 500ms e DWM também não tem amostras recentes, aguarda até o timeout de 1s
        if (mostRecentCandidateTick == 0)
            return;

        // 3. Seleciona a SwapChain dominante com maior número de intervalos válidos (delta >= 0.5ms)
        int bestValidIntervals = 0;
        double bestSumDeltaMs = 0.0;
        double ticksToMs = 1000.0 / Stopwatch.Frequency;

        foreach (KeyValuePair<ulong, List<long>> kvp in _swapChainTimestamps)
        {
            bool isCandidate = hasRecentDirectEvents ? kvp.Key != DwmSwapChainKey : kvp.Key == DwmSwapChainKey;
            if (!isCandidate)
                continue;

            List<long> timestamps = kvp.Value;
            int count = timestamps.Count;
            if (count < 2)
                continue;

            int validCount = 0;
            double sumDeltaMs = 0.0;

            for (int i = 1; i < count; i++)
            {
                double deltaMs = (timestamps[i] - timestamps[i - 1]) * ticksToMs;
                if (deltaMs < MinValidDeltaMs)
                    continue;

                sumDeltaMs += deltaMs;
                validCount++;
            }

            if (validCount > bestValidIntervals)
            {
                bestValidIntervals = validCount;
                bestSumDeltaMs = sumDeltaMs;
            }
        }

        if (bestValidIntervals <= 0 || bestSumDeltaMs <= 0.0)
            return;

        // 4. Converte frametime médio em FPS e aplica suavização adaptativa (EMA)
        double avgFrametimeMs = bestSumDeltaMs / bestValidIntervals;
        double rawFps = 1000.0 / avgFrametimeMs;

        if (_smoothedFps <= 0.0 || (Math.Abs(rawFps - _smoothedFps) / _smoothedFps) > EmaSignificantDeltaRatio)
        {
            _smoothedFps = rawFps;
        }
        else
        {
            _smoothedFps = (_smoothedFps * 0.65) + (rawFps * 0.35);
        }
    }

    private void ClearFrameHistoryLocked()
    {
        _swapChainTimestamps.Clear();
        _swapChainLastTicks.Clear();
        _lastDirectGraphicsEventTick = 0;
        _lastDwmEvent15Tick = 0;
        _smoothedFps = 0.0;
    }

    private static void PruneOlderThan(List<long> timestamps, long cutoff)
    {
        int count = timestamps.Count;
        if (count == 0 || timestamps[0] >= cutoff)
            return;

        int firstValidIndex = 0;
        while (firstValidIndex < count && timestamps[firstValidIndex] < cutoff)
        {
            firstValidIndex++;
        }

        if (firstValidIndex >= count)
        {
            timestamps.Clear();
        }
        else if (firstValidIndex > 0)
        {
            timestamps.RemoveRange(0, firstValidIndex);
        }
    }

    private static bool IsSystemShellOrDwmProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            string name = proc.ProcessName;
            return name.Equals("dwm", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _activeForegroundPid = -1;

        TraceEventSession? sessionToClose;
        lock (_sessionLock)
        {
            sessionToClose = _session;
            _session = null;
        }

        CloseSessionSafe(sessionToClose);

        try
        {
            if (_etwThread is not null && _etwThread.IsAlive)
            {
                _etwThread.Join(TimeSpan.FromMilliseconds(500));
            }
        }
        catch
        {
            // Ignora exceções ao aguardar término da thread dedicada ETW
        }

        try
        {
            lock (_framesLock)
            {
                ClearFrameHistoryLocked();
            }
        }
        catch
        {
            // Ignora exceções na limpeza final dos dicionários
        }
    }
}
