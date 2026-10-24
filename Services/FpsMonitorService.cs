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

    private readonly object _framesLock = new();
    private readonly Queue<long> _frameTimestamps = new(512);
    private readonly CancellationTokenSource _cts = new();
    private readonly int _currentAppPid = Environment.ProcessId;

    private TraceEventSession? _etwSession;
    private Task? _etwTask;

    // PID em foco cacheado (atualizado apenas 1x por segundo no loop principal para zero P/Invoke no callback ETW)
    private volatile int _activeForegroundPid = -1;
    private int _lastEvaluatedRawPid = -1;

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
                    _frameTimestamps.Clear();
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
                    _frameTimestamps.Clear();
                }
                return;
            }

            lock (_framesLock)
            {
                _frameTimestamps.Clear();
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

    private void OnEtwEvent(TraceEvent data)
    {
        // 1ª linha obrigatória: comparação inteira direta O(1) sem P/Invoke e sem alocações
        if (data.ProcessID != _activeForegroundPid) return;

        if (_isDisposed || _isSuspended)
            return;

        int eventId = (int)data.ID;
        if (!((eventId == DxgiPresentStartEventId && data.ProviderGuid == DxgiProviderGuid) ||
              (eventId == D3D9PresentStartEventId && data.ProviderGuid == D3D9ProviderGuid)))
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        long cutoff = now - Stopwatch.Frequency;

        lock (_framesLock)
        {
            _frameTimestamps.Enqueue(now);

            while (_frameTimestamps.Count > 0 && _frameTimestamps.Peek() < cutoff)
            {
                _frameTimestamps.Dequeue();
            }
        }
    }

    private int CalculateCurrentFps()
    {
        if (_isDisposed || _isSuspended || _activeForegroundPid <= 4)
            return 0;

        long cutoff = Stopwatch.GetTimestamp() - Stopwatch.Frequency;

        lock (_framesLock)
        {
            while (_frameTimestamps.Count > 0 && _frameTimestamps.Peek() < cutoff)
            {
                _frameTimestamps.Dequeue();
            }

            return _frameTimestamps.Count;
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
