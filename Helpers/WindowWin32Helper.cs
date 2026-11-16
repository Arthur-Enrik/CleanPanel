using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace HardwareOverlayApp.Helpers;

public static class WindowWin32Helper
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;

    private const int WS_CAPTION = 0x00C00000;
    private const int WS_MAXIMIZE = 0x01000000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_SYSMENU = 0x00080000;

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_NONE = 1;

    public const int WM_HOTKEY = 0x0312;
    public const int HOTKEY_ID_TOGGLE_OVERLAY = 9001;
    public const int HOTKEY_ID_TOGGLE_COMPACT = 9002;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint VK_O = 0x4F;
    public const uint VK_C = 0x43;

    private static readonly object _hotkeyLock = new();
    private static readonly Dictionary<int, (uint Modifiers, uint VirtualKey)> _registeredHotkeys = new();
    private static IntPtr _hotkeyHwnd = IntPtr.Zero;
    private static bool _hotkeysPaused;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private static readonly HashSet<string> KnownDesktopApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "firefox",
        "brave",
        "opera",
        "code",
        "devenv",
        "discord",
        "spotify",
        "notepad"
    };

    private enum LOGICAL_PROCESSOR_RELATIONSHIP
    {
        RelationProcessorCore = 0,
        RelationNumaNode = 1,
        RelationCache = 2,
        RelationProcessorPackage = 3,
        RelationGroup = 4,
        RelationAll = 0xffff
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(
        LOGICAL_PROCESSOR_RELATIONSHIP RelationshipType,
        IntPtr Buffer,
        ref int ReturnedLength);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>
    /// Desativa explicitamente qualquer backdrop DWM do Windows 11 (Mica/Acrylic) na janela informada
    /// para preservar 100% da transparência por pixel.
    /// </summary>
    public static void DisableDwmSystemBackdrop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        try
        {
            int backdropNone = DWMSBT_NONE;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropNone, sizeof(int));
        }
        catch
        {
            // Ignora em versões do Windows anteriores ao Win11 22H2 (sem DWMWA_SYSTEMBACKDROP_TYPE)
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    /// <summary>
    /// Configura o estilo estendido da janela para ocultar do Alt+Tab (WS_EX_TOOLWINDOW)
    /// e ativar/desativar o modo Click-Through (WS_EX_TRANSPARENT).
    /// </summary>
    public static void SetClickThrough(Window? window, bool locked)
    {
        if (window is null)
            return;

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        extendedStyle |= WS_EX_TOOLWINDOW | WS_EX_LAYERED;

        if (locked)
        {
            extendedStyle |= WS_EX_TRANSPARENT;
        }
        else
        {
            extendedStyle &= ~WS_EX_TRANSPARENT;
        }

        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle);
    }

    /// <summary>
    /// Define o handle da janela que receberá mensagens WM_HOTKEY.
    /// </summary>
    public static void SetHotkeyWindowHandle(IntPtr hwnd)
    {
        lock (_hotkeyLock)
        {
            _hotkeyHwnd = hwnd;
        }
    }

    /// <summary>
    /// Registra ou atualiza um atalho global sem lançar exceções em caso de conflito com outro programa.
    /// </summary>
    public static bool RegisterOrUpdateHotKey(int hotkeyId, uint modifiers, uint virtualKey)
    {
        lock (_hotkeyLock)
        {
            if (_hotkeyHwnd == IntPtr.Zero || virtualKey == 0)
                return false;

            uint cleanModifiers = modifiers & (MOD_ALT | MOD_CONTROL | MOD_SHIFT | MOD_WIN);
            if (cleanModifiers == 0)
                return false;

            try
            {
                UnregisterHotKey(_hotkeyHwnd, hotkeyId);
            }
            catch
            {
                // Ignora falhas de desregistro prévio
            }

            _registeredHotkeys[hotkeyId] = (cleanModifiers, virtualKey);

            if (_hotkeysPaused)
                return true;

            try
            {
                return RegisterHotKey(_hotkeyHwnd, hotkeyId, cleanModifiers | MOD_NOREPEAT, virtualKey);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Pausa temporariamente os atalhos globais registrados (útil durante a captura de um novo atalho na UI).
    /// </summary>
    public static void PauseAllHotkeys()
    {
        lock (_hotkeyLock)
        {
            _hotkeysPaused = true;
            if (_hotkeyHwnd == IntPtr.Zero)
                return;

            foreach (int hotkeyId in _registeredHotkeys.Keys)
            {
                try
                {
                    UnregisterHotKey(_hotkeyHwnd, hotkeyId);
                }
                catch
                {
                    // Ignora falhas ao pausar atalho
                }
            }
        }
    }

    /// <summary>
    /// Reativa todos os atalhos globais configurados após finalizar a gravação de teclas.
    /// </summary>
    public static bool ResumeAllHotkeys()
    {
        lock (_hotkeyLock)
        {
            _hotkeysPaused = false;
            if (_hotkeyHwnd == IntPtr.Zero)
                return false;

            bool allSucceeded = true;
            foreach (var kvp in _registeredHotkeys)
            {
                try
                {
                    UnregisterHotKey(_hotkeyHwnd, kvp.Key);
                    if (!RegisterHotKey(_hotkeyHwnd, kvp.Key, kvp.Value.Modifiers | MOD_NOREPEAT, kvp.Value.VirtualKey))
                    {
                        allSucceeded = false;
                    }
                }
                catch
                {
                    allSucceeded = false;
                }
            }

            return allSucceeded;
        }
    }

    /// <summary>
    /// Remove o registro de todos os atalhos globais ao encerrar a janela.
    /// </summary>
    public static void UnregisterAllHotkeys(IntPtr hwnd)
    {
        lock (_hotkeyLock)
        {
            IntPtr targetHwnd = hwnd != IntPtr.Zero ? hwnd : _hotkeyHwnd;
            if (targetHwnd != IntPtr.Zero)
            {
                foreach (int hotkeyId in _registeredHotkeys.Keys)
                {
                    try
                    {
                        UnregisterHotKey(targetHwnd, hotkeyId);
                    }
                    catch
                    {
                        // Ignora
                    }
                }

                try { UnregisterHotKey(targetHwnd, HOTKEY_ID_TOGGLE_OVERLAY); } catch { }
                try { UnregisterHotKey(targetHwnd, HOTKEY_ID_TOGGLE_COMPACT); } catch { }
            }

            _registeredHotkeys.Clear();
            if (hwnd == IntPtr.Zero || hwnd == _hotkeyHwnd)
            {
                _hotkeyHwnd = IntPtr.Zero;
            }
            _hotkeysPaused = false;
        }
    }

    /// <summary>
    /// Compatibilidade: registra o atalho global padrão de visibilidade do Overlay.
    /// </summary>
    public static bool RegisterToggleOverlayHotKey(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        SetHotkeyWindowHandle(hwnd);
        return RegisterOrUpdateHotKey(HOTKEY_ID_TOGGLE_OVERLAY, MOD_CONTROL | MOD_SHIFT, VK_O);
    }

    /// <summary>
    /// Compatibilidade: remove os atalhos globais registrados na janela.
    /// </summary>
    public static void UnregisterToggleOverlayHotKey(IntPtr hwnd)
    {
        UnregisterAllHotkeys(hwnd);
    }

    /// <summary>
    /// Obtém o Process ID (PID) da janela ou jogo atualmente em primeiro plano (foco).
    /// </summary>
    public static int GetForegroundProcessId()
    {
        IntPtr foregroundHwnd = GetForegroundWindow();
        if (foregroundHwnd == IntPtr.Zero)
            return 0;

        GetWindowThreadProcessId(foregroundHwnd, out uint pid);
        return (int)pid;
    }

    /// <summary>
    /// Força a janela do Overlay a permanecer sempre no topo (acima de jogos Borderless Windowed e DX12 Flip Model).
    /// </summary>
    public static void ForceTopMost(Window? window)
    {
        if (window is null)
            return;

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Verifica se a janela atualmente em primeiro plano está em modo Tela Cheia real (Exclusive Fullscreen ou Borderless Fullscreen).
    /// Ignora janelas meramente maximizadas na área de trabalho (navegadores, explorador, editores),
    /// a área de trabalho do Windows e a barra de tarefas.
    /// </summary>
    public static bool IsForegroundWindowFullScreen()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return false;

            if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
                return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId)
                return false;

            // 1. Ignora classes do shell e da área de trabalho do Windows
            var classNameBuilder = new StringBuilder(64);
            int charsCopied = GetClassName(hwnd, classNameBuilder, classNameBuilder.Capacity);
            if (charsCopied > 0)
            {
                string className = classNameBuilder.ToString();
                if (className.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                    className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
                    className.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                    className.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // 2. Obtém o nome do processo e valida exclusões de shell do Windows
            string? processName = null;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;
            }
            catch
            {
                // Ignora falhas caso o processo tenha sido finalizado ou negue acesso
            }

            if (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase))
                return false;

            // 3. Verifica estilos de janela Win32
            int windowStyle = GetWindowLong(hwnd, GWL_STYLE);
            bool isMaximized = (windowStyle & WS_MAXIMIZE) != 0;
            bool hasCaption = (windowStyle & WS_CAPTION) == WS_CAPTION;

            // Janelas maximizadas padrão com barra de título do sistema (navegadores, editores, etc.)
            // NÃO são consideradas Tela Cheia
            if (isMaximized && hasCaption)
                return false;

            // Para aplicativos de desktop padrão conhecidos (navegadores, IDEs, chat),
            // só considera tela cheia se não tiver estilo de janela padrão (ex: F11 sem barra de título/menu)
            if (!string.IsNullOrEmpty(processName) && KnownDesktopApps.Contains(processName))
            {
                if (isMaximized || (windowStyle & WS_CAPTION) != 0)
                    return false;
            }

            // 4. Obtém o monitor físico correspondente (NUNCA usar rcWork)
            IntPtr hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero)
                return false;

            var monitorInfo = new MONITORINFO
            {
                cbSize = Marshal.SizeOf<MONITORINFO>()
            };

            if (!GetMonitorInfo(hMonitor, ref monitorInfo))
                return false;

            RECT monitorRect = monitorInfo.rcMonitor;
            int monitorWidth = monitorRect.Right - monitorRect.Left;
            int monitorHeight = monitorRect.Bottom - monitorRect.Top;

            if (monitorWidth <= 0 || monitorHeight <= 0)
                return false;

            // 5. Obtém os limites visíveis reais via DwmGetWindowAttribute (sem as margens invisíveis de redimensionamento do Win10/11)
            RECT visibleRect;
            int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out visibleRect, Marshal.SizeOf<RECT>());
            if (hr != 0)
            {
                if (!GetWindowRect(hwnd, out visibleRect))
                    return false;
            }

            // A janela visível deve cobrir 100% da área física total do monitor (sem margens de barra de tarefas)
            bool visibleCoversMonitor =
                visibleRect.Left <= monitorRect.Left &&
                visibleRect.Top <= monitorRect.Top &&
                visibleRect.Right >= monitorRect.Right &&
                visibleRect.Bottom >= monitorRect.Bottom;

            if (!visibleCoversMonitor)
                return false;

            // 6. Validação estrita da área de cliente real (GetClientRect + ClientToScreen)
            // Em tela cheia real (Exclusive ou Borderless), a área de cliente cobre 100% do monitor físico
            if (!GetClientRect(hwnd, out RECT clientRect))
                return false;

            int clientWidth = clientRect.Right - clientRect.Left;
            int clientHeight = clientRect.Bottom - clientRect.Top;

            var clientOrigin = new POINT { X = clientRect.Left, Y = clientRect.Top };
            if (!ClientToScreen(hwnd, ref clientOrigin))
                return false;

            bool clientCoversMonitor =
                clientOrigin.X <= monitorRect.Left + 2 &&
                clientOrigin.Y <= monitorRect.Top + 2 &&
                clientWidth >= monitorWidth - 2 &&
                clientHeight >= monitorHeight - 2;

            return clientCoversMonitor;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Consulta a topologia física de núcleos da CPU via Win32 (GetLogicalProcessorInformationEx)
    /// identificando se existem núcleos híbridos (P-Cores com EfficiencyClass > 0 e E-Cores com EfficiencyClass == 0).
    /// </summary>
    public static (bool isHybrid, List<byte> efficiencyClasses) GetCpuCoreTopology()
    {
        var efficiencyClasses = new List<byte>();
        try
        {
            int length = 0;
            GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, IntPtr.Zero, ref length);
            if (length <= 0)
                return (false, efficiencyClasses);

            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, buffer, ref length))
                    return (false, efficiencyClasses);

                int offset = 0;
                while (offset < length)
                {
                    IntPtr currentPtr = IntPtr.Add(buffer, offset);
                    int relationship = Marshal.ReadInt32(currentPtr, 0);
                    int size = Marshal.ReadInt32(currentPtr, 4);
                    if (size <= 0)
                        break;

                    if (relationship == (int)LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                    {
                        // In PROCESSOR_RELATIONSHIP:
                        // Offset 8: BYTE Flags
                        // Offset 9: BYTE EfficiencyClass
                        byte efficiencyClass = Marshal.ReadByte(currentPtr, 9);
                        efficiencyClasses.Add(efficiencyClass);
                    }

                    offset += size;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            bool hasPCores = efficiencyClasses.Any(c => c > 0);
            bool hasECores = efficiencyClasses.Any(c => c == 0);
            bool isHybrid = hasPCores && hasECores;

            return (isHybrid, efficiencyClasses);
        }
        catch
        {
            return (false, efficiencyClasses);
        }
    }
}
