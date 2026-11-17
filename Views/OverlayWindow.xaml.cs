using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.ViewModels;

namespace HardwareOverlayApp.Views;

public partial class OverlayWindow : Window
{
    private readonly MainViewModel _viewModel;
    private HwndSource? _hwndSource;

    public OverlayWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.OverlaySizeProvider = () =>
        {
            UpdateLayout();
            return (ActualWidth, ActualHeight);
        };
        _viewModel.ReloadHotkeysProvider = ReloadGlobalHotkeys;
        _viewModel.SuspendHotkeysAction = SuspendGlobalHotkeys;
        _viewModel.EnsureOverlayTransparentAction = EnsureTransparentWindow;

        EnsureTransparentWindow();

        Left = _viewModel.OverlayLeft;
        Top = _viewModel.OverlayTop;
        UpdateCursor();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Deactivated += OnWindowDeactivated;
        Loaded += OnWindowLoaded;
    }

    public void EnsureTransparentWindow()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(EnsureTransparentWindow);
            return;
        }

        ClearValue(BackgroundProperty);
        Background = Brushes.Transparent;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            WindowWin32Helper.DisableDwmBackdrop(hwnd);
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } compositionTarget)
            {
                compositionTarget.BackgroundColor = Colors.Transparent;
            }

            WindowWin32Helper.SetClickThrough(this, _viewModel.IsOverlayLocked);
        }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        EnsureTransparentWindow();
        _viewModel.UpdateOverlayBrushes();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        EnsureTransparentWindow();
        _viewModel.UpdateOverlayBrushes();

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        WindowWin32Helper.SetClickThrough(this, _viewModel.IsOverlayLocked);
        _viewModel.ReloadGlobalHotkeys();
        WindowWin32Helper.ForceTopMost(this);
    }

    public (bool OverlayRegistered, bool CompactRegistered) ReloadGlobalHotkeys()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return (true, true);

        WindowWin32Helper.UnregisterAllHotKeys(hwnd);

        bool overlayOk = WindowWin32Helper.RegisterCustomHotKey(
            hwnd,
            WindowWin32Helper.HOTKEY_ID_TOGGLE_OVERLAY,
            _viewModel.ToggleOverlayModifiers,
            _viewModel.ToggleOverlayKey);

        bool compactOk = WindowWin32Helper.RegisterCustomHotKey(
            hwnd,
            WindowWin32Helper.HOTKEY_ID_TOGGLE_COMPACT,
            _viewModel.ToggleCompactModifiers,
            _viewModel.ToggleCompactKey);

        return (overlayOk, compactOk);
    }

    public void SuspendGlobalHotkeys()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            WindowWin32Helper.UnregisterAllHotKeys(hwnd);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WindowWin32Helper.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == WindowWin32Helper.HOTKEY_ID_TOGGLE_OVERLAY)
            {
                _viewModel.IsOverlayVisible = !_viewModel.IsOverlayVisible;
                handled = true;
            }
            else if (id == WindowWin32Helper.HOTKEY_ID_TOGGLE_COMPACT)
            {
                _viewModel.IsCompactMode = !_viewModel.IsCompactMode;
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsOverlayLocked):
                WindowWin32Helper.SetClickThrough(this, _viewModel.IsOverlayLocked);
                UpdateCursor();
                break;

            case nameof(MainViewModel.IsOverlayVisible):
            case nameof(MainViewModel.EffectiveOverlayVisible):
                if (_viewModel.EffectiveOverlayVisible)
                {
                    WindowWin32Helper.ForceTopMost(this);
                }
                break;

            case nameof(MainViewModel.OverlayLeft):
                if (Math.Abs(Left - _viewModel.OverlayLeft) > 0.5)
                    Left = _viewModel.OverlayLeft;
                break;

            case nameof(MainViewModel.OverlayTop):
                if (Math.Abs(Top - _viewModel.OverlayTop) > 0.5)
                    Top = _viewModel.OverlayTop;
                break;

            case nameof(MainViewModel.FpsText):
                if (_viewModel.EffectiveOverlayVisible)
                {
                    WindowWin32Helper.ForceTopMost(this);
                }
                break;
        }
    }

    private void UpdateCursor()
    {
        Cursor = _viewModel.IsOverlayLocked ? Cursors.Arrow : Cursors.SizeAll;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsOverlayLocked || e.ButtonState != MouseButtonState.Pressed)
            return;

        try
        {
            DragMove();
            _viewModel.SetOverlayPosition(Left, Top);
        }
        catch
        {
            // Ignora interrupções do clique pelo sistema operacional
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        Topmost = true;
        if (_viewModel.EffectiveOverlayVisible)
        {
            WindowWin32Helper.ForceTopMost(this);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            WindowWin32Helper.UnregisterAllHotKeys(hwnd);
        }

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        _viewModel.OverlaySizeProvider = null;
        _viewModel.ReloadHotkeysProvider = null;
        _viewModel.SuspendHotkeysAction = null;
        _viewModel.EnsureOverlayTransparentAction = null;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Deactivated -= OnWindowDeactivated;
        Loaded -= OnWindowLoaded;
        base.OnClosed(e);
    }
}
