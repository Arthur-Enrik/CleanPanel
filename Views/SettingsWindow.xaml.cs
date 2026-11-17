using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.ViewModels;
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
        PreviewKeyDown += OnWindowPreviewKeyDown;
        Deactivated += OnWindowDeactivated;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.IsRecordingOverlayHotkey && !_viewModel.IsRecordingCompactHotkey)
            return;

        e.Handled = true;
        Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;

        if (MainViewModel.IsModifierKeyOnly(actualKey))
            return;

        if (actualKey == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            _viewModel.CancelHotkeyRecording();
            return;
        }

        _viewModel.TryApplyRecordedHotkey(Keyboard.Modifiers, actualKey);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _viewModel.CancelHotkeyRecording();
    }

    private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncVisibilityWithMonitorService();

    private void OnWindowStateChanged(object? sender, EventArgs e) => SyncVisibilityWithMonitorService();

    private void OnWindowLoaded(object sender, RoutedEventArgs e) => SyncVisibilityWithMonitorService();

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
        _viewModel.CancelHotkeyRecording();

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
        _viewModel.CancelHotkeyRecording();
        IsVisibleChanged -= OnWindowVisibilityChanged;
        StateChanged -= OnWindowStateChanged;
        Loaded -= OnWindowLoaded;
        PreviewKeyDown -= OnWindowPreviewKeyDown;
        Deactivated -= OnWindowDeactivated;
        base.OnClosed(e);
    }
}
