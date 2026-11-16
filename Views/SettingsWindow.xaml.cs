using System.ComponentModel;
using System.Windows;
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
