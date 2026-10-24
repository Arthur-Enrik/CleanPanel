using System.ComponentModel;
using System.Windows;
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

        _viewModel = viewModel;
        DataContext = _viewModel;

        IsVisibleChanged += (_, _) => SyncVisibilityWithMonitorService();
        StateChanged += (_, _) => SyncVisibilityWithMonitorService();
        Loaded += (_, _) => SyncVisibilityWithMonitorService();
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
}
