using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace HardwareOverlayApp.Views;

public partial class SettingsWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private bool _allowRealClose;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        try
        {
            Icon = IconGeneratorHelper.CreateWindowIconSource(128);
        }
        catch
        {
            // Fallback seguro caso o ícone dinâmico não possa ser criado
        }

        _viewModel = viewModel;
        DataContext = _viewModel;

        IsVisibleChanged += OnWindowVisibilityChanged;
        StateChanged += OnWindowStateChanged;
        Loaded += OnWindowLoaded;
    }

    private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncVisibilityWithMonitorService();

    private void OnWindowStateChanged(object? sender, EventArgs e) => SyncVisibilityWithMonitorService();

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_viewModel.AppTheme, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            SystemThemeWatcher.Watch(this, WindowBackdropType.None, false);
        }

        MainViewModel.ApplyApplicationTheme(_viewModel.AppTheme, _viewModel.AccentColorHex);
        _viewModel.UpdateOverlayBrushes();
        SyncVisibilityWithMonitorService();
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
            return;

        _viewModel.PauseHotkeysForCapture();
        textBox.Text = "Pressione a combinação (ex: Ctrl + Shift + O)...";
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            textBox.Text = string.Equals(textBox.Tag as string, "ToggleCompact", StringComparison.Ordinal)
                ? _viewModel.ToggleCompactModeHotkeyDisplay
                : _viewModel.ToggleOverlayHotkeyDisplay;
        }

        _viewModel.ResumeHotkeysAfterCapture();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (sender is not System.Windows.Controls.TextBox textBox)
            return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            textBox.Text = string.Equals(textBox.Tag as string, "ToggleCompact", StringComparison.Ordinal)
                ? _viewModel.ToggleCompactModeHotkeyDisplay
                : _viewModel.ToggleOverlayHotkeyDisplay;
            Keyboard.ClearFocus();
            Focus();
            return;
        }

        if (IsModifierOnlyKey(key) || key == Key.None || key == Key.DeadCharProcessed || key == Key.ImeProcessed)
            return;

        ModifierKeys modifiers = Keyboard.Modifiers;
        uint win32Mods = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) win32Mods |= WindowWin32Helper.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) win32Mods |= WindowWin32Helper.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Alt)) win32Mods |= WindowWin32Helper.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) win32Mods |= WindowWin32Helper.MOD_WIN;

        if (win32Mods == 0)
        {
            _viewModel.HotkeyStatusMessage = "Inclua uma tecla modificadora (Ctrl, Shift ou Alt) junto com a tecla.";
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0)
            return;

        string display = FormatHotkeyDisplay(win32Mods, key);

        if (string.Equals(textBox.Tag as string, "ToggleCompact", StringComparison.Ordinal))
        {
            _viewModel.SetToggleCompactModeHotkey(win32Mods, (uint)vk, display);
            textBox.Text = _viewModel.ToggleCompactModeHotkeyDisplay;
        }
        else
        {
            _viewModel.SetToggleOverlayHotkey(win32Mods, (uint)vk, display);
            textBox.Text = _viewModel.ToggleOverlayHotkeyDisplay;
        }

        Keyboard.ClearFocus();
        Focus();
    }

    private static bool IsModifierOnlyKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin;

    private static string FormatHotkeyDisplay(uint modifiers, Key key)
    {
        var sb = new StringBuilder();
        if ((modifiers & WindowWin32Helper.MOD_CONTROL) != 0) sb.Append("Ctrl + ");
        if ((modifiers & WindowWin32Helper.MOD_ALT) != 0) sb.Append("Alt + ");
        if ((modifiers & WindowWin32Helper.MOD_SHIFT) != 0) sb.Append("Shift + ");
        if ((modifiers & WindowWin32Helper.MOD_WIN) != 0) sb.Append("Win + ");

        string keyName = key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.Space => "Space",
            _ => key.ToString()
        };

        sb.Append(keyName);
        return sb.ToString();
    }

    private void SyncVisibilityWithMonitorService()
    {
        bool isActivelyVisible = IsVisible && WindowState != WindowState.Minimized;
        _viewModel.SetMainWindowVisible(isActivelyVisible);
    }

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Focus();
        SyncVisibilityWithMonitorService();
    }

    public void PrepareForExit()
    {
        _allowRealClose = true;
        _viewModel.SetMainWindowVisible(false);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowRealClose)
        {
            // Mantém o programa rodando na bandeja do sistema e o Overlay ativo, suspendendo sensores pesados da janela principal
            e.Cancel = true;
            Hide();
            SyncVisibilityWithMonitorService();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        IsVisibleChanged -= OnWindowVisibilityChanged;
        StateChanged -= OnWindowStateChanged;
        Loaded -= OnWindowLoaded;
        base.OnClosed(e);
    }
}
