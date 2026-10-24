using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.ViewModels;

namespace HardwareOverlayApp.Services;

public class TrayIconService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ContextMenuStrip _contextMenu;
    private readonly System.Drawing.Icon _trayIcon;
    private readonly MainViewModel _viewModel;
    private readonly Action _openSettingsAction;
    private readonly Action _exitApplicationAction;
    private bool _isDisposed;

    public TrayIconService(
        MainViewModel viewModel,
        Action openSettingsAction,
        Action exitApplicationAction)
    {
        _viewModel = viewModel;
        _openSettingsAction = openSettingsAction;
        _exitApplicationAction = exitApplicationAction;

        _trayIcon = IconGeneratorHelper.CreateTrayIcon();
        _contextMenu = new System.Windows.Forms.ContextMenuStrip();

        var openSettingsItem = new System.Windows.Forms.ToolStripMenuItem("Abrir Configurações");
        openSettingsItem.Click += (_, _) => _openSettingsAction();

        var toggleOverlayItem = new System.Windows.Forms.ToolStripMenuItem("Mostrar / Ocultar Overlay (Ctrl+Shift+O)");
        toggleOverlayItem.Click += (_, _) => _viewModel.IsOverlayVisible = !_viewModel.IsOverlayVisible;

        var toggleLockItem = new System.Windows.Forms.ToolStripMenuItem("Travar / Destravar Posição do Overlay");
        toggleLockItem.Click += (_, _) => _viewModel.IsOverlayLocked = !_viewModel.IsOverlayLocked;

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Sair Completamente");
        exitItem.Click += (_, _) => _exitApplicationAction();

        _contextMenu.Items.Add(openSettingsItem);
        _contextMenu.Items.Add(toggleOverlayItem);
        _contextMenu.Items.Add(toggleLockItem);
        _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Hardware Overlay Monitor",
            ContextMenuStrip = _contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => _openSettingsAction();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _trayIcon.Dispose();
    }
}
