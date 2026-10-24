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
    private readonly int _currentAppPid = Environment.ProcessId;

    private TraceEventSession? _etwSession;
    private Task? _etwTask;
    private int _trackedPid;
    private bool _trackedPidIsIgnored;
    private bool _isDisposed;

    public int CurrentFps => CalculateCurrentFps();

    public void Start()
    {
        if (_etwTask != null)
            return;

        _etwTask = Task.Run(RunEtwSession);
    }

    private void RunEtwSession()
    {
        try
        {
            // Se houver uma sessão órfã anterior com o mesmo nome, encerra antes de recriar
            using (_ = new TraceEventSession(SessionName) { StopOnDispose = true })
            {
            }
        }
        catch
        {
            // Ignora caso não exista sessão anterior
        }

        try
        {
            _etwSession = new TraceEventSession(SessionName)
            {
                StopOnDispose = true
            };

            _etwSession.Source.AllEvents += OnEtwEvent;

            _etwSession.EnableProvider(DxgiProviderGuid, TraceEventLevel.Informational);
            _etwSession.EnableProvider(D3D9ProviderGuid, TraceEventLevel.Informational);

            // Process() é bloqueante até que _etwSession.Stop() ou Dispose() seja chamado
            _etwSession.Source.Process();
        }
        catch
        {
            // Caso o usuário não esteja com privilégios suficientes para sessão ETW, mantém FPS em 0 sem travar o app
        }
    }

    private void OnEtwEvent(TraceEvent data)
    {
        if (_isDisposed)
            return;

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

    private int CalculateCurrentFps()
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

        try
        {
            _etwSession?.Stop();
            _etwSession?.Dispose();
        }
        catch
        {
            // Ignora erros no encerramento da sessão ETW
        }
    }
}
