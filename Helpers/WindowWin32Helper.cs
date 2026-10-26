using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace HardwareOverlayApp.Helpers;

public static class WindowWin32Helper
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public const int WM_HOTKEY = 0x0312;
    public const int HOTKEY_ID_TOGGLE_OVERLAY = 9001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint VK_O = 0x4F;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

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
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

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
    /// Registra o atalho global Ctrl + Shift + O para mostrar/ocultar o Overlay de dentro de qualquer jogo.
    /// </summary>
    public static bool RegisterToggleOverlayHotKey(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        return RegisterHotKey(hwnd, HOTKEY_ID_TOGGLE_OVERLAY, MOD_CONTROL | MOD_SHIFT, VK_O);
    }

    /// <summary>
    /// Remove o registro do atalho global ao encerrar a janela.
    /// </summary>
    public static void UnregisterToggleOverlayHotKey(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        UnregisterHotKey(hwnd, HOTKEY_ID_TOGGLE_OVERLAY);
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
    /// Verifica se a janela atualmente em primeiro plano está estritamente em modo Tela Cheia Real (Exclusive / Borderless Fullscreen).
    /// Rejeita janelas comuns maximizadas (como Chrome, Edge, Explorador de Arquivos) e elementos do Shell do Windows.
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

            var classNameBuilder = new StringBuilder(128);
            int charsCopied = GetClassName(hwnd, classNameBuilder, classNameBuilder.Capacity);
            string className = charsCopied > 0 ? classNameBuilder.ToString() : string.Empty;

            // 1. Ignorar elementos do Shell do Windows e Área de Trabalho
            if (className.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("DV2ControlHost", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int style = GetWindowLong(hwnd, GWL_STYLE);

            // 2. Janelas com barra de título padrão (WS_CAPTION) NUNCA estão em tela cheia real (Exclusive ou Borderless)
            if ((style & WS_CAPTION) == WS_CAPTION)
            {
                return false;
            }

            // 3. Rejeitar janelas comuns de navegador e Explorador de Arquivos em estado maximizado comum (IsZoomed)
            if (IsZoomed(hwnd))
            {
                if (className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
                    className.Equals("ExploreWClass", StringComparison.OrdinalIgnoreCase) ||
                    className.Equals("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase) ||
                    className.Equals("MozillaWindowClass", StringComparison.OrdinalIgnoreCase) ||
                    className.Equals("ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // 4. Obter dimensões do monitor da janela
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

            // 5. Obter dimensões reais da janela (preferindo DwmGetWindowAttribute para limites visuais sem margens ocultas)
            RECT targetRect;
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frameRect, Marshal.SizeOf<RECT>()) == 0 &&
                frameRect.Right > frameRect.Left && frameRect.Bottom > frameRect.Top)
            {
                targetRect = frameRect;
            }
            else
            {
                if (!GetWindowRect(hwnd, out RECT winRect))
                    return false;
                targetRect = winRect;
            }

            int windowWidth = targetRect.Right - targetRect.Left;
            int windowHeight = targetRect.Bottom - targetRect.Top;
            int monitorWidth = monitorRect.Right - monitorRect.Left;
            int monitorHeight = monitorRect.Bottom - monitorRect.Top;

            if (windowWidth < monitorWidth || windowHeight < monitorHeight)
                return false;

            // 6. Critério estrito de Fullscreen: cobrir ou ultrapassar perfeitamente a área física do monitor
            return targetRect.Left <= monitorRect.Left &&
                   targetRect.Top <= monitorRect.Top &&
                   targetRect.Right >= monitorRect.Right &&
                   targetRect.Bottom >= monitorRect.Bottom;
        }
        catch
        {
            return false;
        }
    }
}
