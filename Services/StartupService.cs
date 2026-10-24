using System.Diagnostics;

namespace HardwareOverlayApp.Services;

public class StartupService
{
    private const string TaskName = "HardwareOverlayMonitorApp";

    public void SetStartup(bool enable)
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                return;

            string arguments = enable
                ? $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --minimized\" /SC ONLOGON /RL HIGHEST /F"
                : $"/Delete /TN \"{TaskName}\" /F";

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
            // Ignora falhas caso o agendador de tarefas seja bloqueado por política local
        }
    }
}
