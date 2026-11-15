using System.Diagnostics;
using System.Runtime.InteropServices;
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

    private const double SlidingWindowMs = 650.0;
    private const double MinFrameDeltaMs = 0.5;
    private const double StaleResetMs = 1000.0;
    private const double EmaAlpha = 0.45;
    private const long D3D9SwapChainSentinel = 1L;
    private const int SwapChainSlotCount = 8;

    private struct SwapChainSlot
    {
        public long Pointer;
        public int RecentCount;
        public long LastSeenTick;
    }

    private readonly object _framesLock = new();
    private readonly Queue<double> _frameTimesMs = new(512);
    private readonly SwapChainSlot[] _swapChainSlots = new SwapChainSlot[SwapChainSlotCount];
    private readonly CancellationTokenSource _cts = new();
    private readonly int _currentAppPid = Environment.ProcessId;
    private readonly long _staleResetTicks = Stopwatch.Frequency;
    private readonly long _swapChainStaleTicks = Stopwatch.Frequency / 2;

    private TraceEventSession? _etwSession;
    private Task? _etwTask;

    // PID em foco cacheado (atualizado apenas 1x por segundo no loop principal para zero P/Invoke no callback ETW)
    private volatile int _activeForegroundPid = -1;
    private int _lastEvaluatedRawPid = -1;

    private long _primarySwapChain;
    private long _lastSwapChainDecayTick;
    private double _lastAcceptedEventTimeMs;
    private double _latestEventTimeMs;
    private double _lastSmoothedEventTimeMs;
    private double _smoothedFps;
    private long _lastFrameArrivalTick;
    private bool _hasAcceptedFrame;

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

    public int CurrentFps => CalculateCurrentFps();

    public void Start()
    {
        if (_isDisposed || _etwTask is not null)
            return;

        RefreshActiveForegroundProcess();
        _etwTask = Task.Run(() => RunEtwSession(_cts.Token));
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
        // Limpa sessões órfãs anteriores caso existam
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
            // Caso o usuário não tenha privilégios de sessão ETW ou ocorra parada abrupta, mantém o app estável
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
        _frameTimesMs.Clear();
        Array.Clear(_swapChainSlots, 0, _swapChainSlots.Length);
        _primarySwapChain = 0;
        _lastSwapChainDecayTick = 0;
        _lastAcceptedEventTimeMs = 0;
        _latestEventTimeMs = 0;
        _lastSmoothedEventTimeMs = 0;
        _smoothedFps = 0;
        _lastFrameArrivalTick = 0;
        _hasAcceptedFrame = false;
    }

    private void OnEtwEvent(TraceEvent data)
    {
        // 1ª linha obrigatória: comparação inteira direta O(1) sem P/Invoke e sem alocações
        if (data.ProcessID != _activeForegroundPid) return;

        if (_isDisposed || _isSuspended)
            return;

        int eventId = (int)data.ID;
        bool isDxgi = eventId == DxgiPresentStartEventId && data.ProviderGuid == DxgiProviderGuid;
        bool isD3D9 = !isDxgi && eventId == D3D9PresentStartEventId && data.ProviderGuid == D3D9ProviderGuid;

        if (!isDxgi && !isD3D9)
            return;

        long swapChainPtr = isDxgi ? ExtractDxgiSwapChainPointer(data) : D3D9SwapChainSentinel;
        double eventTimeMs = data.TimeStampRelativeMSec;
        long arrivalTick = Stopwatch.GetTimestamp();

        lock (_framesLock)
        {
            if (!TryAcceptPrimarySwapChainNoLock(swapChainPtr, arrivalTick))
                return;

            // Ignora eventos duplicados/micro-bursts (< 0.5 ms) usando timestamp ETW real (QPC)
            if (_hasAcceptedFrame && (eventTimeMs - _lastAcceptedEventTimeMs) < MinFrameDeltaMs)
                return;

            _hasAcceptedFrame = true;
            _lastAcceptedEventTimeMs = eventTimeMs;
            _latestEventTimeMs = eventTimeMs;
            _lastFrameArrivalTick = arrivalTick;

            _frameTimesMs.Enqueue(eventTimeMs);

            double cutoffMs = eventTimeMs - SlidingWindowMs;
            while (_frameTimesMs.Count > 2 && _frameTimesMs.Peek() < cutoffMs)
            {
                _frameTimesMs.Dequeue();
            }
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

    private bool TryAcceptPrimarySwapChainNoLock(long swapChainPtr, long nowTick)
    {
        if (_lastSwapChainDecayTick == 0 || (nowTick - _lastSwapChainDecayTick) >= _staleResetTicks)
        {
            _lastSwapChainDecayTick = nowTick;
            for (int i = 0; i < _swapChainSlots.Length; i++)
            {
                if (_swapChainSlots[i].Pointer == 0)
                    continue;

                if ((nowTick - _swapChainSlots[i].LastSeenTick) > _staleResetTicks)
                {
                    _swapChainSlots[i] = default;
                }
                else
                {
                    _swapChainSlots[i].RecentCount >>= 1;
                }
            }
        }

        int targetIndex = -1;
        int freeIndex = -1;
        int lowestIndex = 0;
        int lowestCount = int.MaxValue;

        for (int i = 0; i < _swapChainSlots.Length; i++)
        {
            if (_swapChainSlots[i].Pointer == swapChainPtr)
            {
                targetIndex = i;
                break;
            }

            if (_swapChainSlots[i].Pointer == 0 || (nowTick - _swapChainSlots[i].LastSeenTick) > _staleResetTicks)
            {
                if (freeIndex == -1)
                    freeIndex = i;
            }
            else if (_swapChainSlots[i].RecentCount < lowestCount)
            {
                lowestCount = _swapChainSlots[i].RecentCount;
                lowestIndex = i;
            }
        }

        if (targetIndex == -1)
        {
            targetIndex = freeIndex != -1 ? freeIndex : lowestIndex;
            _swapChainSlots[targetIndex] = new SwapChainSlot
            {
                Pointer = swapChainPtr,
                RecentCount = 0,
                LastSeenTick = nowTick
            };
        }

        _swapChainSlots[targetIndex].RecentCount++;
        _swapChainSlots[targetIndex].LastSeenTick = nowTick;

        if (_primarySwapChain == 0)
        {
            _primarySwapChain = swapChainPtr;
            return true;
        }

        if (swapChainPtr == _primarySwapChain)
            return true;

        int primaryIndex = -1;
        for (int i = 0; i < _swapChainSlots.Length; i++)
        {
            if (_swapChainSlots[i].Pointer == _primarySwapChain)
            {
                primaryIndex = i;
                break;
            }
        }

        bool primaryStale = primaryIndex == -1 ||
                            (nowTick - _swapChainSlots[primaryIndex].LastSeenTick) > _swapChainStaleTicks;

        int primaryCount = primaryIndex != -1 ? _swapChainSlots[primaryIndex].RecentCount : 0;
        int candidateCount = _swapChainSlots[targetIndex].RecentCount;

        if (primaryStale || (candidateCount >= primaryCount + 4 && candidateCount > (primaryCount * 1.25)))
        {
            _primarySwapChain = swapChainPtr;
            _frameTimesMs.Clear();
            _hasAcceptedFrame = false;
            _smoothedFps = 0;
            return true;
        }

        return false;
    }

    private int CalculateCurrentFps()
    {
        if (_isDisposed || _isSuspended || _activeForegroundPid <= 4)
            return 0;

        long nowTick = Stopwatch.GetTimestamp();

        lock (_framesLock)
        {
            if (!_hasAcceptedFrame || _lastFrameArrivalTick == 0 || (nowTick - _lastFrameArrivalTick) > _staleResetTicks)
            {
                if (_frameTimesMs.Count > 0 || _smoothedFps > 0)
                {
                    _frameTimesMs.Clear();
                    _hasAcceptedFrame = false;
                    _smoothedFps = 0;
                    _lastSmoothedEventTimeMs = 0;
                }
                return 0;
            }

            if (_frameTimesMs.Count < 2)
                return _smoothedFps > 0 ? (int)Math.Round(_smoothedFps) : 0;

            double firstMs = _frameTimesMs.Peek();
            double elapsedMs = _latestEventTimeMs - firstMs;

            if (elapsedMs > StaleResetMs * 1.5 || elapsedMs <= 0.1)
                return _smoothedFps > 0 ? (int)Math.Round(_smoothedFps) : 0;

            double rawFps = ((_frameTimesMs.Count - 1) * 1000.0) / elapsedMs;
            if (double.IsNaN(rawFps) || double.IsInfinity(rawFps) || rawFps <= 0)
                return 0;

            if (rawFps > 2000.0)
                rawFps = 2000.0;

            if (_smoothedFps <= 0.0)
            {
                _smoothedFps = rawFps;
                _lastSmoothedEventTimeMs = _latestEventTimeMs;
            }
            else if (Math.Abs(_latestEventTimeMs - _lastSmoothedEventTimeMs) > 0.01)
            {
                _smoothedFps = (_smoothedFps * (1.0 - EmaAlpha)) + (rawFps * EmaAlpha);
                _lastSmoothedEventTimeMs = _latestEventTimeMs;
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
