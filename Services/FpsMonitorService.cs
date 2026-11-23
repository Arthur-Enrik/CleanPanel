using System.Diagnostics;
using System.Runtime.InteropServices;
using HardwareOverlayApp.Helpers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace HardwareOverlayApp.Services;

/// <summary>
/// Receptor leve de intervalos de frametime da SwapChain dominante em foco.
/// </summary>
public interface IFrameSampleSink
{
    void OnFrameInterval(int processId, double frametimeMs);
}

/// <summary>
/// Motor de captura e cálculo de FPS via ETW (Event Tracing for Windows) sem injeção de DLLs.
/// Suporta DX9, DX10/11/12 (DXGI), Vulkan/OpenGL (DXGI e DxgKrnl) e fallback via compositor DWM.
/// </summary>
public class FpsMonitorService : IDisposable
{
    private const string SessionName = "CleanPanelFpsSession";
    private const string LegacySessionName = "HardwareOverlayFpsSession";

    // Provedores e Event IDs verificados nos manifestos ETW do Windows
    private static readonly Guid DxgiProviderGuid = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private const int DxgiPresentStartEventId = 42;
    private const int DxgiPresentMpoStartEventId = 55;
    private const ulong DxgiEventsKeyword = 0x2UL; // Microsoft-Windows-DXGI: Events

    private static readonly Guid D3D9ProviderGuid = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int D3D9PresentStartEventId = 1;
    private const ulong D3D9EventsKeyword = 0x2UL; // Microsoft-Windows-D3D9: Events

    private static readonly Guid DxgKrnlProviderGuid = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    private const int DxgKrnlPresentEventId = 184;             // Task 107 (Present)
    private const int DxgKrnlPresentHistoryStartEventId = 171; // Task 6 (PresentHistory), Opcode 1 (Start)
    private const ulong DxgKrnlPresentKeyword = 0x08000000UL;  // Present

    private static readonly Guid DwmCoreProviderGuid = new("9E9BBA3C-2E38-40CB-99F4-9E8281425158");
    private const int DwmSchedulePresentStartEventId = 15;     // SCHEDULE_PRESENT (Start)
    private const int DwmUcePresentEventStartEventId = 61;     // MILEVENT_MEDIA_UCE_PRESENTEVENT (Start)
    private const ulong DwmCoreCompositionAndSchedulingKeyword = 0x81UL; // Composition (0x01) | Scheduling (0x80)

    // Chaves fallback para fluxos sem ponteiro explícito
    private const ulong DxgiDefaultStreamKey = 0x1UL;
    private const ulong D3D9DefaultStreamKey = 0x2UL;
    private const ulong DxgKrnlDefaultStreamKey = 0x3UL;
    private const ulong DxgKrnlHistoryStreamKey = 0x4UL;
    private const ulong DwmStreamKey = 0x5UL;

    // Parâmetros ETW e cálculo de frametime em janela deslizante
    private const int EtwBufferSizeMb = 16;
    private const int EtwBufferQuantumKb = 16;
    private const int SessionRestartDelayMs = 1000;
    private const int ThreadJoinTimeoutMs = 500;
    private const int MaxTrackedStreams = 64;

    private const double SlidingWindowMs = 1000.0;
    private const double MinFrametimeMs = 0.35;       // Filtra bursts duplicados (> 2857 FPS)
    private const double MaxFrametimeMs = 500.0;      // Ignora pausas longas acima de 500 ms (2 FPS)
    private const double TierFallbackTimeoutSeconds = 0.5;
    private const double IdleResetSeconds = 1.35;     // Superior ao FlushTimer de 1s do kernel ETW
    private const double EmaStableWeight = 0.70;
    private const double EmaImmediateJumpRatio = 0.18;
    private const double OutputHysteresisFps = 0.65;

    private enum FrameTier : byte
    {
        Tier1Direct = 1,      // DXGI (42/55) e D3D9 (1) por PID
        Tier2KernelWddm = 2,  // DxgKrnl (184/171) por PID (Vulkan/OpenGL nativos)
        Tier3Dwm = 3          // Dwm-Core (15/61) fallback fullscreen
    }

    private enum FrameEventKind : byte
    {
        DxgiPresent = 1,
        DxgiPresentMpo = 2,
        D3D9Present = 3,
        DxgKrnlPresent = 4,
        DxgKrnlPresentHistory = 5,
        DwmSchedulePresent = 6,
        DwmUcePresent = 7
    }

    private readonly struct FrameStreamId(FrameTier tier, ulong streamKey) : IEquatable<FrameStreamId>
    {
        public FrameTier Tier { get; } = tier;
        public ulong StreamKey { get; } = streamKey;

        public bool Equals(FrameStreamId other) => Tier == other.Tier && StreamKey == other.StreamKey;
        public override bool Equals(object? obj) => obj is FrameStreamId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((byte)Tier, StreamKey);
    }

    private sealed class SwapChainTrack
    {
        public readonly List<double> EventTimestampsMs = new(256);
        public int HeadIndex;
        public long LastArrivalWallTicks;
        public double LatestEventTimeMs;
    }

    private static readonly long TierFallbackTimeoutTicks = (long)(Stopwatch.Frequency * TierFallbackTimeoutSeconds);
    private static readonly long IdleResetTicks = (long)(Stopwatch.Frequency * IdleResetSeconds);

    private readonly object _framesLock = new();
    private readonly object _sessionLock = new();
    private readonly Dictionary<FrameStreamId, SwapChainTrack> _tracks = new();
    private readonly List<FrameStreamId> _staleKeysBuffer = new(16);
    private readonly ManualResetEventSlim _disposeSignal = new(false);
    private readonly ManualResetEventSlim _activeSignal = new(true);
    private readonly int _currentAppPid = Environment.ProcessId;

    private TraceEventSession? _session;
    private Thread? _etwThread;
    private volatile bool _isStarted;
    private volatile IFrameSampleSink? _frameSampleSink;

    // Identificação da SwapChain dominante para encaminhamento sem alocações ao sink
    private FrameStreamId _dominantStreamId;
    private bool _hasDominantStream;

    // Estado de janela em primeiro plano atualizado fora do hot-path ETW
    private volatile int _activeForegroundPid = -1;
    private volatile bool _isForegroundFullscreen;
    private int _lastEvaluatedRawPid = -1;
    private bool _lastEvaluatedPidIgnored;

    // Controle de precedência entre provedores/tiers (ticks do Stopwatch em tempo de parede)
    private long _lastDxgi42WallTicks;
    private long _lastTier1WallTicks;
    private long _lastDxgKrnl184WallTicks;
    private long _lastTier2WallTicks;
    private long _lastDwm15WallTicks;

    // Suavização e histerese
    private double _smoothedFps;
    private int _lastDisplayedFps;

    private volatile bool _isDisposed;
    private volatile bool _isSuspended;

    public void SetFrameSampleSink(IFrameSampleSink? sink) => _frameSampleSink = sink;

    public bool IsSuspended
    {
        get => _isSuspended;
        set
        {
            bool hasActiveSession = false;
            bool needStartThread = false;

            lock (_sessionLock)
            {
                if (_isDisposed || _isSuspended == value)
                    return;

                if (value)
                {
                    _isSuspended = true;
                    _activeSignal.Reset();
                    hasActiveSession = _session is not null;
                }
                else
                {
                    _isSuspended = false;
                    _activeSignal.Set();
                    needStartThread = _isStarted && _etwThread is null;
                }
            }

            if (value)
            {
                _activeForegroundPid = -1;
                _isForegroundFullscreen = false;
                _lastEvaluatedRawPid = -1;
                _lastEvaluatedPidIgnored = false;

                lock (_framesLock)
                {
                    ClearFrameHistoryLocked();
                }

                if (hasActiveSession)
                {
                    _ = Task.Run(StopSessionIfSuspended);
                }
            }
            else if (needStartThread)
            {
                RefreshActiveForegroundProcess();
                EnsureEtwThreadStarted();
            }
        }
    }

    public int CurrentFps
    {
        get
        {
            lock (_framesLock)
            {
                return ComputeCurrentFpsLocked();
            }
        }
    }

    public void Start()
    {
        bool shouldStart;
        lock (_sessionLock)
        {
            if (_isDisposed || _isStarted)
                return;

            _isStarted = true;
            shouldStart = !_isSuspended && _etwThread is null;
        }

        if (shouldStart)
        {
            RefreshActiveForegroundProcess();
            EnsureEtwThreadStarted();
        }
    }

    private void EnsureEtwThreadStarted()
    {
        lock (_sessionLock)
        {
            if (_isDisposed || _isSuspended || !_isStarted || _etwThread is not null)
                return;

            _etwThread = new Thread(RunEtwSessionLoop)
            {
                Name = "CleanPanelEtwFpsThread",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _etwThread.Start();
        }
    }

    private void StopSessionIfSuspended()
    {
        TraceEventSession? sessionToClose;
        lock (_sessionLock)
        {
            if (!_isSuspended && !_isDisposed)
                return;

            sessionToClose = _session;
            _session = null;
        }

        CloseSessionSafe(sessionToClose);
    }

    /// <summary>
    /// Atualiza o PID do processo em primeiro plano e o estado de tela cheia fora do callback ETW.
    /// </summary>
    public void RefreshActiveForegroundProcess()
    {
        if (_isDisposed || _isSuspended)
        {
            _activeForegroundPid = -1;
            _isForegroundFullscreen = false;
            return;
        }

        try
        {
            int rawPid = WindowWin32Helper.GetForegroundProcessId();
            bool samePidAsLast = rawPid == _lastEvaluatedRawPid;

            if (rawPid <= 4 || rawPid == _currentAppPid || (samePidAsLast ? _lastEvaluatedPidIgnored : IsIgnoredSystemProcess(rawPid)))
            {
                if (_activeForegroundPid != -1 || !samePidAsLast)
                {
                    _activeForegroundPid = -1;
                    _isForegroundFullscreen = false;
                    _lastEvaluatedRawPid = rawPid;
                    _lastEvaluatedPidIgnored = rawPid > 4 && rawPid != _currentAppPid;
                    lock (_framesLock)
                    {
                        ClearFrameHistoryLocked();
                    }
                }
                return;
            }

            if (!samePidAsLast)
            {
                _lastEvaluatedRawPid = rawPid;
                _lastEvaluatedPidIgnored = false;

                lock (_framesLock)
                {
                    ClearFrameHistoryLocked();
                }

                _activeForegroundPid = rawPid;
            }

            // Só consulta IsForegroundWindowFullScreen quando Tiers 1 e 2 estão inativos (fallback DWM)
            long nowWallTicks = Stopwatch.GetTimestamp();
            long tier1Ticks = Volatile.Read(ref _lastTier1WallTicks);
            long tier2Ticks = Volatile.Read(ref _lastTier2WallTicks);
            bool directOrKernelActive =
                (tier1Ticks > 0 && (nowWallTicks - tier1Ticks) <= TierFallbackTimeoutTicks) ||
                (tier2Ticks > 0 && (nowWallTicks - tier2Ticks) <= TierFallbackTimeoutTicks);

            _isForegroundFullscreen = !directOrKernelActive && WindowWin32Helper.IsForegroundWindowFullScreen();
        }
        catch
        {
            _activeForegroundPid = -1;
            _isForegroundFullscreen = false;
        }
    }

    private void RunEtwSessionLoop()
    {
        while (!_isDisposed)
        {
            if (_isSuspended)
            {
                try
                {
                    _activeSignal.Wait();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (_isDisposed)
                    return;

                if (_isSuspended)
                    continue;
            }

            TraceEventSession? session = null;

            try
            {
                lock (_sessionLock)
                {
                    if (_isDisposed)
                        return;

                    if (_isSuspended)
                        continue;

                    StopOrphanSession(LegacySessionName);
                    StopOrphanSession(SessionName);

                    if (_isDisposed)
                        return;

                    if (_isSuspended)
                        continue;

                    session = new TraceEventSession(SessionName, TraceEventSessionOptions.Create)
                    {
                        StopOnDispose = true,
                        BufferSizeMB = EtwBufferSizeMb
                    };

                    try
                    {
                        session.BufferQuantumKB = EtwBufferQuantumKb;
                    }
                    catch
                    {
                        // Ignora caso o kernel não permita alterar o tamanho do quantum
                    }

                    session.Source.AllEvents += OnEtwEvent;

                    bool anyEnabled =
                        TryEnableProvider(session, DxgiProviderGuid, TraceEventLevel.Informational, DxgiEventsKeyword) |
                        TryEnableProvider(session, D3D9ProviderGuid, TraceEventLevel.Informational, D3D9EventsKeyword) |
                        TryEnableProvider(session, DxgKrnlProviderGuid, TraceEventLevel.Informational, DxgKrnlPresentKeyword) |
                        TryEnableProvider(session, DwmCoreProviderGuid, TraceEventLevel.Informational, DwmCoreCompositionAndSchedulingKeyword);

                    if (!anyEnabled)
                    {
                        CloseSessionSafe(session);
                        session = null;
                        break;
                    }

                    _session = session;
                }

                // Loop bloqueante em tempo real na thread dedicada
                session.Source.Process();
            }
            catch
            {
                // Mantém estabilidade caso falte privilégio de administrador ou ocorra falha transitória na sessão
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

            if (!_isDisposed && !_isSuspended)
            {
                _disposeSignal.Wait(SessionRestartDelayMs);
            }
        }
    }

    private static bool TryEnableProvider(
        TraceEventSession session,
        Guid providerGuid,
        TraceEventLevel level,
        ulong matchAnyKeywords)
    {
        try
        {
            session.EnableProvider(providerGuid, level, matchAnyKeywords);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void StopOrphanSession(string sessionName)
    {
        try
        {
            using TraceEventSession? existing = TraceEventSession.GetActiveSession(sessionName);
            if (existing is not null)
            {
                existing.StopOnDispose = true;
                existing.Stop(noThrow: true);
            }
        }
        catch
        {
            // Ignora caso não haja sessão vinculada no processo atual
        }

        try
        {
            using var attached = new TraceEventSession(sessionName, TraceEventSessionOptions.Attach)
            {
                StopOnDispose = true
            };
            attached.Stop(noThrow: true);
        }
        catch
        {
            // Ignora caso não exista sessão órfã ativa no kernel
        }
    }

    private void CloseSessionSafe(TraceEventSession? session)
    {
        if (session is null)
            return;

        try
        {
            session.Source.AllEvents -= OnEtwEvent;
        }
        catch
        {
            // Ignora se a fonte já foi encerrada
        }

        try
        {
            session.Stop(noThrow: true);
        }
        catch
        {
            // Ignora erros ao parar a sessão
        }

        try
        {
            session.Dispose();
        }
        catch
        {
            // Ignora erros ao liberar a sessão
        }
    }

    private void OnEtwEvent(TraceEvent data)
    {
        if (_isDisposed || _isSuspended)
            return;

        int activePid = _activeForegroundPid;
        if (activePid <= 4)
            return;

        if (!TryClassifyEvent(data, activePid, out FrameTier tier, out FrameEventKind kind))
            return;

        long nowWallTicks = Stopwatch.GetTimestamp();

        // Supressão lock-free antes de ExtractStreamKey ou lock(_framesLock)
        if (tier == FrameTier.Tier2KernelWddm)
        {
            if (IsWithinTierTimeout(Volatile.Read(ref _lastTier1WallTicks), nowWallTicks))
                return;
        }
        else if (tier == FrameTier.Tier3Dwm)
        {
            if (IsWithinTierTimeout(Volatile.Read(ref _lastTier1WallTicks), nowWallTicks) ||
                IsWithinTierTimeout(Volatile.Read(ref _lastTier2WallTicks), nowWallTicks))
            {
                return;
            }
        }

        double eventTimeMs = data.TimeStampRelativeMSec;
        if (double.IsNaN(eventTimeMs) || double.IsInfinity(eventTimeMs))
            return;

        ulong streamKey = ExtractStreamKey(data, kind);

        lock (_framesLock)
        {
            // Revalida após adquirir o lock para evitar gravar estado obsoleto após suspensão/troca de foco
            if (_isDisposed || _isSuspended || _activeForegroundPid != activePid)
                return;

            if (!AcceptByTierAndPrecedenceLocked(tier, kind, nowWallTicks))
                return;

            RecordFrameSampleLocked(new FrameStreamId(tier, streamKey), eventTimeMs, nowWallTicks);
        }
    }

    private bool TryClassifyEvent(TraceEvent data, int activePid, out FrameTier tier, out FrameEventKind kind)
    {
        Guid provider = data.ProviderGuid;
        int eventId = (int)data.ID;

        if (provider == DxgiProviderGuid)
        {
            if (data.ProcessID == activePid)
            {
                if (eventId == DxgiPresentStartEventId)
                {
                    tier = FrameTier.Tier1Direct;
                    kind = FrameEventKind.DxgiPresent;
                    return true;
                }

                if (eventId == DxgiPresentMpoStartEventId)
                {
                    tier = FrameTier.Tier1Direct;
                    kind = FrameEventKind.DxgiPresentMpo;
                    return true;
                }
            }
        }
        else if (provider == D3D9ProviderGuid)
        {
            if (eventId == D3D9PresentStartEventId && data.ProcessID == activePid)
            {
                tier = FrameTier.Tier1Direct;
                kind = FrameEventKind.D3D9Present;
                return true;
            }
        }
        else if (provider == DxgKrnlProviderGuid)
        {
            if (data.ProcessID == activePid)
            {
                if (eventId == DxgKrnlPresentEventId)
                {
                    tier = FrameTier.Tier2KernelWddm;
                    kind = FrameEventKind.DxgKrnlPresent;
                    return true;
                }

                if (eventId == DxgKrnlPresentHistoryStartEventId && (byte)data.Opcode == 1)
                {
                    tier = FrameTier.Tier2KernelWddm;
                    kind = FrameEventKind.DxgKrnlPresentHistory;
                    return true;
                }
            }
        }
        else if (provider == DwmCoreProviderGuid && _isForegroundFullscreen)
        {
            if (eventId == DwmSchedulePresentStartEventId)
            {
                tier = FrameTier.Tier3Dwm;
                kind = FrameEventKind.DwmSchedulePresent;
                return true;
            }

            if (eventId == DwmUcePresentEventStartEventId)
            {
                tier = FrameTier.Tier3Dwm;
                kind = FrameEventKind.DwmUcePresent;
                return true;
            }
        }

        tier = default;
        kind = default;
        return false;
    }

    private bool AcceptByTierAndPrecedenceLocked(FrameTier tier, FrameEventKind kind, long nowWallTicks)
    {
        switch (tier)
        {
            case FrameTier.Tier1Direct:
                if (kind == FrameEventKind.DxgiPresent)
                {
                    _lastDxgi42WallTicks = nowWallTicks;
                    Volatile.Write(ref _lastTier1WallTicks, nowWallTicks);
                    return true;
                }

                if (kind == FrameEventKind.DxgiPresentMpo)
                {
                    if (IsWithinTierTimeout(_lastDxgi42WallTicks, nowWallTicks))
                        return false;

                    Volatile.Write(ref _lastTier1WallTicks, nowWallTicks);
                    return true;
                }

                Volatile.Write(ref _lastTier1WallTicks, nowWallTicks);
                return true;

            case FrameTier.Tier2KernelWddm:
                if (IsWithinTierTimeout(Volatile.Read(ref _lastTier1WallTicks), nowWallTicks))
                    return false;

                if (kind == FrameEventKind.DxgKrnlPresent)
                {
                    _lastDxgKrnl184WallTicks = nowWallTicks;
                    Volatile.Write(ref _lastTier2WallTicks, nowWallTicks);
                    return true;
                }

                if (IsWithinTierTimeout(_lastDxgKrnl184WallTicks, nowWallTicks))
                    return false;

                Volatile.Write(ref _lastTier2WallTicks, nowWallTicks);
                return true;

            case FrameTier.Tier3Dwm:
                if (IsWithinTierTimeout(Volatile.Read(ref _lastTier1WallTicks), nowWallTicks) ||
                    IsWithinTierTimeout(Volatile.Read(ref _lastTier2WallTicks), nowWallTicks))
                {
                    return false;
                }

                if (kind == FrameEventKind.DwmSchedulePresent)
                {
                    _lastDwm15WallTicks = nowWallTicks;
                    return true;
                }

                return !IsWithinTierTimeout(_lastDwm15WallTicks, nowWallTicks);

            default:
                return false;
        }
    }

    private void RecordFrameSampleLocked(FrameStreamId streamId, double eventTimeMs, long nowWallTicks)
    {
        if (!_tracks.TryGetValue(streamId, out SwapChainTrack? track))
        {
            if (_tracks.Count >= MaxTrackedStreams)
            {
                EvictOldestTrackLocked();
            }

            track = new SwapChainTrack();
            _tracks[streamId] = track;
        }

        track.LastArrivalWallTicks = nowWallTicks;

        List<double> timestamps = track.EventTimestampsMs;
        int count = timestamps.Count;
        int head = track.HeadIndex;

        if (head >= count)
        {
            if (count > 0)
            {
                timestamps.Clear();
            }
            track.HeadIndex = 0;
            timestamps.Add(eventTimeMs);
            track.LatestEventTimeMs = eventTimeMs;
            return;
        }

        double lastTimeMs = timestamps[count - 1];
        double deltaMs = eventTimeMs - lastTimeMs;

        if (deltaMs >= 0.0)
        {
            if (deltaMs < MinFrametimeMs)
                return;

            timestamps.Add(eventTimeMs);
            track.LatestEventTimeMs = eventTimeMs;

            if (deltaMs <= MaxFrametimeMs)
            {
                IFrameSampleSink? sink = _frameSampleSink;
                if (sink is not null)
                {
                    if (!_hasDominantStream)
                    {
                        _dominantStreamId = streamId;
                        _hasDominantStream = true;
                    }
                    else if (!streamId.Equals(_dominantStreamId))
                    {
                        int currentStreamSamples = timestamps.Count - track.HeadIndex;
                        if (!_tracks.TryGetValue(_dominantStreamId, out SwapChainTrack? domTrack) ||
                            domTrack.LastArrivalWallTicks < nowWallTicks - IdleResetTicks ||
                            streamId.Tier < _dominantStreamId.Tier ||
                            (streamId.Tier == _dominantStreamId.Tier &&
                             currentStreamSamples > (domTrack.EventTimestampsMs.Count - domTrack.HeadIndex)))
                        {
                            _dominantStreamId = streamId;
                        }
                    }

                    if (streamId.Equals(_dominantStreamId))
                    {
                        sink.OnFrameInterval(_activeForegroundPid, deltaMs);
                    }
                }
            }
        }
        else if (deltaMs > -50.0)
        {
            // Trata eventual entrega fora de ordem entre buffers por núcleo
            int insertIndex = count;
            while (insertIndex > head && timestamps[insertIndex - 1] > eventTimeMs)
            {
                insertIndex--;
            }

            if ((insertIndex > head && Math.Abs(eventTimeMs - timestamps[insertIndex - 1]) < MinFrametimeMs) ||
                (insertIndex < count && Math.Abs(timestamps[insertIndex] - eventTimeMs) < MinFrametimeMs))
            {
                return;
            }

            timestamps.Insert(insertIndex, eventTimeMs);
            if (eventTimeMs > track.LatestEventTimeMs)
            {
                track.LatestEventTimeMs = eventTimeMs;
            }
        }
        else
        {
            // Reinício de sessão ETW (relógio relativo reiniciado)
            timestamps.Clear();
            track.HeadIndex = 0;
            timestamps.Add(eventTimeMs);
            track.LatestEventTimeMs = eventTimeMs;
        }

        // Poda amortizada O(1) via HeadIndex preservando janela deslizante enxuta
        double pruneCutoffMs = track.LatestEventTimeMs - SlidingWindowMs;
        if ((timestamps.Count - track.HeadIndex) > 2 && timestamps[track.HeadIndex] < pruneCutoffMs)
        {
            PruneOlderThan(track, pruneCutoffMs);
        }
    }

    private int ComputeCurrentFpsLocked()
    {
        if (_isDisposed || _isSuspended || _activeForegroundPid <= 4)
        {
            _smoothedFps = 0.0;
            _lastDisplayedFps = 0;
            return 0;
        }

        long nowWallTicks = Stopwatch.GetTimestamp();
        long idleCutoffWallTicks = nowWallTicks - IdleResetTicks;

        FrameTier activePriorityTier = DetermineActiveTierLocked(nowWallTicks);

        // Remove fluxos ociosos e seleciona a SwapChain dominante no tier prioritário
        _staleKeysBuffer.Clear();
        int bestValidIntervals = 0;
        double bestSumDeltaMs = 0.0;
        FrameStreamId bestStreamId = default;
        bool hasAnyActiveTrack = false;

        foreach (KeyValuePair<FrameStreamId, SwapChainTrack> kvp in _tracks)
        {
            SwapChainTrack track = kvp.Value;
            if (track.LastArrivalWallTicks < idleCutoffWallTicks)
            {
                _staleKeysBuffer.Add(kvp.Key);
                continue;
            }

            hasAnyActiveTrack = true;

            double cutoffEventTimeMs = track.LatestEventTimeMs - SlidingWindowMs;
            PruneOlderThan(track, cutoffEventTimeMs);

            if (kvp.Key.Tier != activePriorityTier)
                continue;

            List<double> timestamps = track.EventTimestampsMs;
            int head = track.HeadIndex;
            int count = timestamps.Count;
            if (count - head < 2)
                continue;

            int validIntervals = 0;
            double sumDeltaMs = 0.0;

            for (int i = head + 1; i < count; i++)
            {
                double deltaMs = timestamps[i] - timestamps[i - 1];
                if (deltaMs < MinFrametimeMs || deltaMs > MaxFrametimeMs)
                    continue;

                sumDeltaMs += deltaMs;
                validIntervals++;
            }

            if (validIntervals > bestValidIntervals)
            {
                bestValidIntervals = validIntervals;
                bestSumDeltaMs = sumDeltaMs;
                bestStreamId = kvp.Key;
            }
        }

        if (bestValidIntervals > 0)
        {
            _dominantStreamId = bestStreamId;
            _hasDominantStream = true;
        }

        for (int i = 0; i < _staleKeysBuffer.Count; i++)
        {
            _tracks.Remove(_staleKeysBuffer[i]);
        }

        if (!hasAnyActiveTrack)
        {
            ClearFrameHistoryLocked();
            return 0;
        }

        if (bestValidIntervals <= 0 || bestSumDeltaMs <= 0.0)
            return _lastDisplayedFps;

        double avgFrametimeMs = bestSumDeltaMs / bestValidIntervals;
        double rawFps = 1000.0 / avgFrametimeMs;

        if (double.IsNaN(rawFps) || double.IsInfinity(rawFps) || rawFps <= 0.0)
            return _lastDisplayedFps;

        if (_smoothedFps <= 0.0 ||
            (Math.Abs(rawFps - _smoothedFps) / _smoothedFps) > EmaImmediateJumpRatio)
        {
            _smoothedFps = rawFps;
        }
        else
        {
            _smoothedFps = (_smoothedFps * EmaStableWeight) + (rawFps * (1.0 - EmaStableWeight));
        }

        if (_lastDisplayedFps <= 0 || Math.Abs(_smoothedFps - _lastDisplayedFps) >= OutputHysteresisFps)
        {
            _lastDisplayedFps = Math.Max(1, (int)Math.Round(_smoothedFps));
        }

        return _lastDisplayedFps;
    }

    private FrameTier DetermineActiveTierLocked(long nowWallTicks)
    {
        if (IsWithinTierTimeout(Volatile.Read(ref _lastTier1WallTicks), nowWallTicks))
            return FrameTier.Tier1Direct;

        if (IsWithinTierTimeout(Volatile.Read(ref _lastTier2WallTicks), nowWallTicks))
            return FrameTier.Tier2KernelWddm;

        return FrameTier.Tier3Dwm;
    }

    private static bool IsWithinTierTimeout(long lastWallTicks, long nowWallTicks) =>
        lastWallTicks > 0 && (nowWallTicks - lastWallTicks) <= TierFallbackTimeoutTicks;

    private void EvictOldestTrackLocked()
    {
        FrameStreamId oldestKey = default;
        long oldestTicks = long.MaxValue;
        bool found = false;

        foreach (KeyValuePair<FrameStreamId, SwapChainTrack> kvp in _tracks)
        {
            if (kvp.Value.LastArrivalWallTicks < oldestTicks)
            {
                oldestTicks = kvp.Value.LastArrivalWallTicks;
                oldestKey = kvp.Key;
                found = true;
            }
        }

        if (found)
        {
            _tracks.Remove(oldestKey);
        }
    }

    private void ClearFrameHistoryLocked()
    {
        _tracks.Clear();
        _dominantStreamId = default;
        _hasDominantStream = false;
        _lastDxgi42WallTicks = 0;
        Volatile.Write(ref _lastTier1WallTicks, 0);
        _lastDxgKrnl184WallTicks = 0;
        Volatile.Write(ref _lastTier2WallTicks, 0);
        _lastDwm15WallTicks = 0;
        _smoothedFps = 0.0;
        _lastDisplayedFps = 0;
    }

    private static ulong ExtractStreamKey(TraceEvent data, FrameEventKind kind)
    {
        try
        {
            if (kind is FrameEventKind.DwmSchedulePresent or FrameEventKind.DwmUcePresent)
                return DwmStreamKey;

            IntPtr dataStart = data.DataStart;
            int dataLength = data.EventDataLength;

            if (kind == FrameEventKind.DxgKrnlPresent)
            {
                // Microsoft-Windows-DxgKrnl v1 ID 184: hContext (UInt32 @ 0) + hWindow (Pointer @ 4)
                if (dataStart != IntPtr.Zero && dataLength >= sizeof(uint))
                {
                    uint hContext = unchecked((uint)Marshal.ReadInt32(dataStart, 0));
                    int ptrSize = data.PointerSize == 4 ? 4 : 8;
                    ulong hWindow = 0;

                    if (dataLength >= sizeof(uint) + ptrSize)
                    {
                        hWindow = ptrSize == 4
                            ? unchecked((uint)Marshal.ReadInt32(dataStart, sizeof(uint)))
                            : unchecked((ulong)Marshal.ReadInt64(dataStart, sizeof(uint)));
                    }

                    ulong key = unchecked(hWindow * 0x9E3779B97F4A7C15UL) ^ hContext;
                    return key != 0 ? key : DxgKrnlDefaultStreamKey;
                }

                return DxgKrnlDefaultStreamKey;
            }

            if (kind == FrameEventKind.DxgKrnlPresentHistory)
            {
                // Microsoft-Windows-DxgKrnl v2 ID 171: hAdapter (Pointer @ 0)
                if (TryReadPointerAtOffsetZero(data, out ulong hAdapter) && hAdapter != 0)
                    return hAdapter;

                return DxgKrnlHistoryStreamKey;
            }

            // Tier 1 (DXGI 42 / DXGI 55 / D3D9 1): ponteiro SwapChain no offset 0
            if (TryReadPointerAtOffsetZero(data, out ulong ptr) && ptr != 0)
                return ptr;
        }
        catch
        {
            // Fallback seguro caso o layout binário varie
        }

        return kind switch
        {
            FrameEventKind.D3D9Present => D3D9DefaultStreamKey,
            FrameEventKind.DxgiPresent or FrameEventKind.DxgiPresentMpo => DxgiDefaultStreamKey,
            FrameEventKind.DxgKrnlPresent => DxgKrnlDefaultStreamKey,
            FrameEventKind.DxgKrnlPresentHistory => DxgKrnlHistoryStreamKey,
            _ => DwmStreamKey
        };
    }

    private static bool TryReadPointerAtOffsetZero(TraceEvent data, out ulong pointer)
    {
        pointer = 0;
        IntPtr dataStart = data.DataStart;
        if (dataStart == IntPtr.Zero)
            return false;

        int ptrSize = data.PointerSize == 4 ? 4 : 8;
        if (data.EventDataLength < ptrSize)
            return false;

        pointer = ptrSize == 4
            ? unchecked((uint)Marshal.ReadInt32(dataStart, 0))
            : unchecked((ulong)Marshal.ReadInt64(dataStart, 0));

        return true;
    }

    private static void PruneOlderThan(SwapChainTrack track, double cutoffMs)
    {
        List<double> timestamps = track.EventTimestampsMs;
        int count = timestamps.Count;
        int head = track.HeadIndex;

        if (head >= count || timestamps[head] >= cutoffMs)
            return;

        while (head < count && timestamps[head] < cutoffMs)
        {
            head++;
        }

        if (head >= count)
        {
            timestamps.Clear();
            track.HeadIndex = 0;
        }
        else
        {
            track.HeadIndex = head;
            if (head >= 64 && head * 2 >= count)
            {
                timestamps.RemoveRange(0, head);
                track.HeadIndex = 0;
            }
        }
    }

    private static bool IsIgnoredSystemProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            string name = proc.ProcessName;
            return name.Equals("dwm", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("ShellHost", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("LockApp", StringComparison.OrdinalIgnoreCase);
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
        _frameSampleSink = null;
        _activeForegroundPid = -1;
        _isForegroundFullscreen = false;

        try
        {
            _activeSignal.Set();
            _disposeSignal.Set();
        }
        catch
        {
            // Ignora caso os sinais já tenham sido liberados
        }

        TraceEventSession? sessionToClose;
        Thread? etwThread;
        lock (_sessionLock)
        {
            sessionToClose = _session;
            _session = null;
            etwThread = _etwThread;
            _etwThread = null;
        }

        CloseSessionSafe(sessionToClose);

        try
        {
            if (etwThread is not null && etwThread.IsAlive)
            {
                etwThread.Join(TimeSpan.FromMilliseconds(ThreadJoinTimeoutMs));
            }
        }
        catch
        {
            // Ignora exceções ao aguardar o encerramento da thread ETW
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
            // Ignora falhas na limpeza final dos fluxos
        }

        try
        {
            _disposeSignal.Dispose();
        }
        catch
        {
            // Ignora falhas na liberação do evento de encerramento
        }

        try
        {
            _activeSignal.Dispose();
        }
        catch
        {
            // Ignora falhas na liberação do evento de suspensão
        }
    }
}
