using System.Diagnostics;
using HardwareOverlayApp.Helpers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace HardwareOverlayApp.Services;

public class FpsMonitorService : IDisposable
{
    private const string SessionName = "HardwareOverlayFpsSession";

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
    private int _trackedPid;
    private bool _trackedPidIsIgnored;
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

        _etwTask = Task.Run(() => RunEtwSession(_cts.Token));
    }

    private void RunEtwSession(CancellationToken cancellationToken)
    {
        try
        {
            using (_ = new TraceEventSession(SessionName) { StopOnDispose = true })
            {
            }
        }
        catch
        {
            // Ignora caso não exista sessão anterior
        }

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

    private void OnEtwEvent(TraceEvent data)
    {
        if (_isDisposed || _isSuspended || _cts.IsCancellationRequested)
            return;

        try
        {
            int eventId = (int)data.ID;

            bool isDxgiPresent = data.ProviderGuid == DxgiProviderGuid && eventId == DxgiPresentStartEventId;
            bool isD3D9Present = data.ProviderGuid == D3D9ProviderGuid && eventId == D3D9PresentStartEventId;

            if (!isDxgiPresent && !isD3D9Present)
                return;

            int eventPid = data.ProcessID;
            if (eventPid <= 4 || eventPid == _currentAppPid)
                return;

            int foregroundPid = WindowWin32Helper.GetForegroundProcessId();
            if (foregroundPid <= 0 || eventPid != foregroundPid)
                return;

            long now = Stopwatch.GetTimestamp();

            lock (_framesLock)
            {
                if (foregroundPid != _trackedPid)
                {
                    _trackedPid = foregroundPid;
                    _frameTimestamps.Clear();
                    _trackedPidIsIgnored = IsSystemShellOrDwmProcess(foregroundPid);
                }

                if (_trackedPidIsIgnored)
                    return;

                _frameTimestamps.Enqueue(now);

                long cutoff = now - Stopwatch.Frequency;
                while (_frameTimestamps.Count > 0 && _frameTimestamps.Peek() < cutoff)
                {
                    _frameTimestamps.Dequeue();
                }
            }
        }
        catch
        {
            // Protege o callback ETW contra encerramento abrupto do processo do jogo ou falha de leitura de evento
        }
    }

    private int CalculateCurrentFps()
    {
        if (_isDisposed || _isSuspended)
            return 0;

        try
        {
            int foregroundPid = WindowWin32Helper.GetForegroundProcessId();
            if (foregroundPid <= 4 || foregroundPid == _currentAppPid)
                return 0;

            long cutoff = Stopwatch.GetTimestamp() - Stopwatch.Frequency;

            lock (_framesLock)
            {
                if (foregroundPid != _trackedPid || _trackedPidIsIgnored)
                    return 0;

                while (_frameTimestamps.Count > 0 && _frameTimestamps.Peek() < cutoff)
                {
                    _frameTimestamps.Dequeue();
                }

                return _frameTimestamps.Count;
            }
        }
        catch
        {
            return 0;
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
        _cts.Cancel();

        try
        {
            _etwSession?.Stop();
            _etwSession?.Dispose();
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
