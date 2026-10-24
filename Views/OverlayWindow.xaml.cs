using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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

        Left = _viewModel.OverlayLeft;
        Top = _viewModel.OverlayTop;
        UpdateCursor();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Deactivated += OnWindowDeactivated;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        WindowWin32Helper.SetClickThrough(this, _viewModel.IsOverlayLocked);
        WindowWin32Helper.RegisterToggleOverlayHotKey(hwnd);
        WindowWin32Helper.ForceTopMost(this);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WindowWin32Helper.WM_HOTKEY &&
            wParam.ToInt32() == WindowWin32Helper.HOTKEY_ID_TOGGLE_OVERLAY)
        {
            _viewModel.IsOverlayVisible = !_viewModel.IsOverlayVisible;
            handled = true;
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
                if (_viewModel.IsOverlayVisible)
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
                if (_viewModel.IsOverlayVisible)
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
        WindowWin32Helper.ForceTopMost(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        WindowWin32Helper.UnregisterToggleOverlayHotKey(hwnd);

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Deactivated -= OnWindowDeactivated;
        base.OnClosed(e);
    }
}
