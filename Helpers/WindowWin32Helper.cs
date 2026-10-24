using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HardwareOverlayApp.Helpers;

public static class WindowWin32Helper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;

    public const int WM_HOTKEY = 0x0312;
    public const int HOTKEY_ID_TOGGLE_OVERLAY = 9001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint VK_O = 0x4F;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

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

    /// <summary>
    /// Configura o estilo estendido da janela para ocultar do Alt+Tab (WS_EX_TOOLWINDOW)
    /// e ativar/desativar o modo Click-Through (WS_EX_TRANSPARENT).
    /// </summary>
    public static void SetClickThrough(Window window, bool locked)
    {
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
    public static void ForceTopMost(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
