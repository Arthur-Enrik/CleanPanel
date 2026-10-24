using System.ComponentModel;
using System.Windows;
using HardwareOverlayApp.ViewModels;
using Wpf.Ui.Controls;

namespace HardwareOverlayApp.Views;

public partial class SettingsWindow : FluentWindow
{
    private bool _allowRealClose;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
    }

    public void PrepareForExit()
    {
        _allowRealClose = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowRealClose)
        {
            // Mantém o programa rodando na bandeja do sistema e o Overlay ativo
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
