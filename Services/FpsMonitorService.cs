using System.Diagnostics;
using System.Runtime.InteropServices;
using HardwareOverlayApp.Helpers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace HardwareOverlayApp.Services;

/// <summary>
/// Motor único de captura de FPS via ETW (Microsoft-Windows-DXGI / D3D9) com Ring Buffer lock-bounded
/// de alta precisão (Stopwatch) e suavização EMA responsiva.
/// </summary>
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

    private const int RingBufferCapacity = 256;
    private const int RingBufferMask = RingBufferCapacity - 1;
    private const int SwapChainSlotCount = 8;
    private const long D3D9SwapChainSentinel = 1L;
    private const double MinFrameDeltaSeconds = 0.0004; // 0.4 ms filtro contra burst duplicado
    private const double MaxSaneFps = 2500.0;

    private struct SwapChainSlot
    {
        public long Pointer;
        public int WindowCount;
        public long LastSeenTicks;
    }

    private readonly object _framesLock = new();
    private readonly long[] _ringBuffer = new long[RingBufferCapacity];
    private readonly SwapChainSlot[] _swapChainSlots = new SwapChainSlot[SwapChainSlotCount];
    private readonly CancellationTokenSource _cts = new();
    private readonly int _currentAppPid = Environment.ProcessId;
    private readonly long _windowTicks = Stopwatch.Frequency; // 1000 ms
    private readonly long _swapChainStaleTicks = Stopwatch.Frequency / 2; // 500 ms
    private readonly long _minFrameDeltaTicks = Math.Max(1L, (long)(Stopwatch.Frequency * MinFrameDeltaSeconds));

    private TraceEventSession? _etwSession;
    private Task? _etwTask;

    // PID em foco cacheado em O(1) para zero P/Invoke no hot path do ETW
    private volatile int _activeForegroundPid = -1;
    private int _lastEvaluatedRawPid = -1;

    private int _writeIndex;
    private int _frameCount;
    private long _lastFrameArrivalTicks;
    private long _lastSmoothedFrameTicks;
    private long _activeSwapChainPtr;
    private long _lastSwapChainEvalTicks;
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
                    ResetFrameStateNoLock();
                }
            }
        }
    }

    public int CurrentFps => GetCurrentFps();

    public void Start()
    {
        if (_isDisposed || _etwTask is not null)
            return;

        RefreshActiveForegroundProcess();
        _etwTask = Task.Run(() => RunEtwSession(_cts.Token));
    }

    /// <summary>
    /// Atualiza o PID do processo em primeiro plano fora do callback de alta frequência do ETW.
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
                    ResetFrameStateNoLock();
                }
                return;
            }

            lock (_framesLock)
            {
                ResetFrameStateNoLock();
            }

            _activeForegroundPid = rawPid;
        }
        catch
        {
            _activeForegroundPid = -1;
        }
    }

    private void RunEtwSession(CancellationToken cancellationToken)
    {
        CloseExistingSessionSafe(LegacySessionName);
        CloseExistingSessionSafe(SessionName);

        if (cancellationToken.IsCancellationRequested)
            return;

        try
        {
            _etwSession = new TraceEventSession(SessionName)
            {
                StopOnDispose = true
            };

            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                try
                {
                    _etwSession?.Stop();
                }
                catch
                {
                    // Ignora falhas de interrupção durante o cancelamento
                }
            });

            _etwSession.Source.AllEvents += OnEtwEvent;
            _etwSession.EnableProvider(DxgiProviderGuid, TraceEventLevel.Informational);
            _etwSession.EnableProvider(D3D9ProviderGuid, TraceEventLevel.Informational);
            _etwSession.Source.Process();
        }
        catch
        {
            // Mantém o aplicativo estável caso não haja privilégio administrativo para sessão ETW
        }
    }

    private static void CloseExistingSessionSafe(string sessionName)
    {
        try
        {
            using var existing = new TraceEventSession(sessionName) { StopOnDispose = true };
        }
        catch
        {
            // Ignora caso não exista sessão anterior
        }
    }

    private void ResetFrameStateNoLock()
    {
        Array.Clear(_ringBuffer, 0, _ringBuffer.Length);
        Array.Clear(_swapChainSlots, 0, _swapChainSlots.Length);
        _writeIndex = 0;
        _frameCount = 0;
        _lastFrameArrivalTicks = 0;
        _lastSmoothedFrameTicks = 0;
        _activeSwapChainPtr = 0;
        _lastSwapChainEvalTicks = 0;
        _smoothedFps = 0.0;
    }

    private void OnEtwEvent(TraceEvent data)
    {
        if (data.ProcessID != _activeForegroundPid) return;

        if (_isDisposed || _isSuspended)
            return;

        int eventId = (int)data.ID;
        bool isDxgi = eventId == DxgiPresentStartEventId && data.ProviderGuid == DxgiProviderGuid;
        bool isD3D9 = !isDxgi && eventId == D3D9PresentStartEventId && data.ProviderGuid == D3D9ProviderGuid;

        if (!isDxgi && !isD3D9)
            return;

        long swapChainPtr = isDxgi ? ExtractDxgiSwapChainPointer(data) : D3D9SwapChainSentinel;
        long nowTicks = Stopwatch.GetTimestamp();

        lock (_framesLock)
        {
            if (!TryAcceptPrimarySwapChainNoLock(swapChainPtr, nowTicks))
                return;

            // Filtro anti-duplicação (< 0.4ms)
            if (_frameCount > 0)
            {
                long newestTick = _ringBuffer[(_writeIndex - 1 + RingBufferCapacity) & RingBufferMask];
                if ((nowTicks - newestTick) < _minFrameDeltaTicks)
                    return;
            }

            _ringBuffer[_writeIndex] = nowTicks;
            _writeIndex = (_writeIndex + 1) & RingBufferMask;
            if (_frameCount < RingBufferCapacity)
            {
                _frameCount++;
            }

            _lastFrameArrivalTicks = nowTicks;
        }
    }

    private static long ExtractDxgiSwapChainPointer(TraceEvent data)
    {
        if (data.DataStart != IntPtr.Zero && data.EventDataLength >= IntPtr.Size)
        {
            long ptr = Marshal.ReadIntPtr(data.DataStart).ToInt64();
            if (ptr != 0)
                return ptr;
        }

        return D3D9SwapChainSentinel;
    }

    private bool TryAcceptPrimarySwapChainNoLock(long swapChainPtr, long nowTicks)
    {
        int slotIndex = -1;
        int freeIndex = -1;
        int lowestIndex = 0;
        int lowestCount = int.MaxValue;

        for (int i = 0; i < _swapChainSlots.Length; i++)
        {
            if (_swapChainSlots[i].Pointer == swapChainPtr)
            {
                slotIndex = i;
                break;
            }

            if (_swapChainSlots[i].Pointer == 0 || (nowTicks - _swapChainSlots[i].LastSeenTicks) > _windowTicks)
            {
                if (freeIndex == -1)
                {
                    freeIndex = i;
                }
            }
            else if (_swapChainSlots[i].WindowCount < lowestCount)
            {
                lowestCount = _swapChainSlots[i].WindowCount;
                lowestIndex = i;
            }
        }

        if (slotIndex == -1)
        {
            slotIndex = freeIndex != -1 ? freeIndex : lowestIndex;
            _swapChainSlots[slotIndex] = new SwapChainSlot
            {
                Pointer = swapChainPtr,
                WindowCount = 0,
                LastSeenTicks = nowTicks
            };
        }

        _swapChainSlots[slotIndex].WindowCount++;
        _swapChainSlots[slotIndex].LastSeenTicks = nowTicks;

        if (_activeSwapChainPtr == 0)
        {
            _activeSwapChainPtr = swapChainPtr;
            _lastSwapChainEvalTicks = nowTicks;
            return true;
        }

        // Avaliação periódica por segundo para travar no SwapChain de maior taxa de quadros
        if (_lastSwapChainEvalTicks == 0)
        {
            _lastSwapChainEvalTicks = nowTicks;
        }
        else if ((nowTicks - _lastSwapChainEvalTicks) >= _windowTicks)
        {
            int activeCount = 0;
            long activeLastSeen = 0;
            int topIndex = -1;
            int topCount = 0;

            for (int i = 0; i < _swapChainSlots.Length; i++)
            {
                if (_swapChainSlots[i].Pointer == _activeSwapChainPtr)
                {
                    activeCount = _swapChainSlots[i].WindowCount;
                    activeLastSeen = _swapChainSlots[i].LastSeenTicks;
                }

                if (_swapChainSlots[i].Pointer != 0 &&
                    (nowTicks - _swapChainSlots[i].LastSeenTicks) <= _windowTicks &&
                    _swapChainSlots[i].WindowCount > topCount)
                {
                    topCount = _swapChainSlots[i].WindowCount;
                    topIndex = i;
                }
            }

            if (topIndex != -1)
            {
                long candidatePtr = _swapChainSlots[topIndex].Pointer;
                bool activeStale = activeLastSeen == 0 || (nowTicks - activeLastSeen) > _swapChainStaleTicks;
                if (candidatePtr != _activeSwapChainPtr && (activeStale || topCount >= activeCount + 4))
                {
                    SwitchActiveSwapChainNoLock(candidatePtr);
                }
            }

            // Reinicia contagem por janela de 1 segundo
            for (int i = 0; i < _swapChainSlots.Length; i++)
            {
                if ((nowTicks - _swapChainSlots[i].LastSeenTicks) > _windowTicks)
                {
                    _swapChainSlots[i] = default;
                }
                else
                {
                    _swapChainSlots[i].WindowCount = 0;
                }
            }

            _lastSwapChainEvalTicks = nowTicks;
        }

        if (swapChainPtr == _activeSwapChainPtr)
            return true;

        // Se a SwapChain ativa ficou silenciosa por > 500ms no meio da janela, promove imediatamente a nova candidata
        long currentActiveSeen = 0;
        for (int i = 0; i < _swapChainSlots.Length; i++)
        {
            if (_swapChainSlots[i].Pointer == _activeSwapChainPtr)
            {
                currentActiveSeen = _swapChainSlots[i].LastSeenTicks;
                break;
            }
        }

        if (currentActiveSeen == 0 || (nowTicks - currentActiveSeen) > _swapChainStaleTicks)
        {
            SwitchActiveSwapChainNoLock(swapChainPtr);
            return true;
        }

        return false;
    }

    private void SwitchActiveSwapChainNoLock(long newSwapChainPtr)
    {
        _activeSwapChainPtr = newSwapChainPtr;
        _writeIndex = 0;
        _frameCount = 0;
        _lastFrameArrivalTicks = 0;
        _lastSmoothedFrameTicks = 0;
        _smoothedFps = 0.0;
    }

    public int GetCurrentFps()
    {
        if (_isDisposed || _isSuspended || _activeForegroundPid <= 4)
            return 0;

        long nowTicks = Stopwatch.GetTimestamp();

        lock (_framesLock)
        {
            // Reset automático ao ultrapassar 1000ms sem novos quadros
            if (_lastFrameArrivalTicks == 0 || (nowTicks - _lastFrameArrivalTicks) > _windowTicks)
            {
                if (_frameCount > 0 || _smoothedFps > 0.0)
                {
                    _writeIndex = 0;
                    _frameCount = 0;
                    _lastFrameArrivalTicks = 0;
                    _lastSmoothedFrameTicks = 0;
                    _smoothedFps = 0.0;
                }
                return 0;
            }

            if (_frameCount < 2)
                return _smoothedFps > 0.0 ? (int)Math.Round(_smoothedFps) : 0;

            long cutoffTicks = nowTicks - _windowTicks;
            int newestIndex = (_writeIndex - 1 + RingBufferCapacity) & RingBufferMask;
            long newestTick = _ringBuffer[newestIndex];
            long oldestTickInWindow = newestTick;
            int framesInWindow = 0;

            for (int i = 0; i < _frameCount; i++)
            {
                int idx = (_writeIndex - 1 - i + RingBufferCapacity) & RingBufferMask;
                long tick = _ringBuffer[idx];
                if (tick < cutoffTicks)
                    break;

                oldestTickInWindow = tick;
                framesInWindow++;
            }

            if (framesInWindow < 2)
                return _smoothedFps > 0.0 ? (int)Math.Round(_smoothedFps) : 0;

            long tickSpan = newestTick - oldestTickInWindow;
            if (tickSpan <= _minFrameDeltaTicks)
                return _smoothedFps > 0.0 ? (int)Math.Round(_smoothedFps) : 0;

            double exactFps = (framesInWindow - 1) * (double)Stopwatch.Frequency / tickSpan;
            if (double.IsNaN(exactFps) || double.IsInfinity(exactFps) || exactFps <= 0.0)
                return 0;

            if (exactFps > MaxSaneFps)
            {
                exactFps = MaxSaneFps;
            }

            if (_smoothedFps <= 0.0)
            {
                _smoothedFps = exactFps;
                _lastSmoothedFrameTicks = newestTick;
            }
            else if (newestTick != _lastSmoothedFrameTicks)
            {
                _smoothedFps = (_smoothedFps * 0.35) + (exactFps * 0.65);
                _lastSmoothedFrameTicks = newestTick;
            }

            return (int)Math.Round(_smoothedFps);
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
        _cts.Cancel();

        try
        {
            if (_etwSession is not null)
            {
                _etwSession.Source.AllEvents -= OnEtwEvent;
                _etwSession.Stop();
                _etwSession.Dispose();
                _etwSession = null;
            }
        }
        catch
        {
            // Ignora erros no encerramento da sessão ETW
        }

        try
        {
            _etwTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // Ignora exceções de cancelamento da Task
        }

        _cts.Dispose();
    }
}
