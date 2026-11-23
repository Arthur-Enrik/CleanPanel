using System.Diagnostics;

namespace HardwareOverlayApp.Services;

public class StartupService
{
    private const string TaskName = "CleanPanelAutoStart";
    private const string LegacyTaskName = "HardwareOverlayMonitorApp";

    private readonly object _syncLock = new();
    private bool? _pendingEnable;
    private bool _workerRunning;

    public void SetStartup(bool enable)
    {
        lock (_syncLock)
        {
            _pendingEnable = enable;
            if (_workerRunning)
                return;

            _workerRunning = true;
        }

        _ = Task.Run(ProcessPendingUpdates);
    }

    private void ProcessPendingUpdates()
    {
        while (true)
        {
            bool targetEnable;
            lock (_syncLock)
            {
                if (!_pendingEnable.HasValue)
                {
                    _workerRunning = false;
                    return;
                }

                targetEnable = _pendingEnable.Value;
                _pendingEnable = null;
            }

            ApplyStartupTask(targetEnable);
        }
    }

    private static void ApplyStartupTask(bool enable)
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                return;

            // Remove a tarefa antiga caso exista de versões anteriores para não deixar lixo no sistema
            RunSchtasksCommand($"/Delete /TN \"{LegacyTaskName}\" /F");

            string arguments = enable
                ? $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --minimized\" /SC ONLOGON /RL HIGHEST /F"
                : $"/Delete /TN \"{TaskName}\" /F";

            RunSchtasksCommand(arguments);
        }
        catch
        {
            // Ignora falhas caso o agendador de tarefas seja bloqueado por política local
        }
    }

    private static void RunSchtasksCommand(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using Process? process = Process.Start(startInfo);
            process?.WaitForExit(3000);
        }
        catch
        {
            // Ignora erros caso a tarefa não exista durante exclusão
        }
    }
}
