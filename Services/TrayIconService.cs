using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.ViewModels;

namespace HardwareOverlayApp.Services;

public class TrayIconService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ContextMenuStrip _contextMenu;
    private readonly System.Windows.Forms.ToolStripMenuItem _openSettingsItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _toggleOverlayItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _toggleLockItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _exitItem;
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

        _openSettingsItem = new System.Windows.Forms.ToolStripMenuItem("Abrir Cleanpanel");
        _openSettingsItem.Click += OnOpenSettingsClick;

        _toggleOverlayItem = new System.Windows.Forms.ToolStripMenuItem("Mostrar / Ocultar Overlay (Ctrl+Shift+O)");
        _toggleOverlayItem.Click += OnToggleOverlayClick;

        _toggleLockItem = new System.Windows.Forms.ToolStripMenuItem("Travar / Destravar Posição do Overlay");
        _toggleLockItem.Click += OnToggleLockClick;

        _exitItem = new System.Windows.Forms.ToolStripMenuItem("Sair Completamente");
        _exitItem.Click += OnExitClick;

        _contextMenu.Items.Add(_openSettingsItem);
        _contextMenu.Items.Add(_toggleOverlayItem);
        _contextMenu.Items.Add(_toggleLockItem);
        _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _contextMenu.Items.Add(_exitItem);

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Cleanpanel",
            ContextMenuStrip = _contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += OnNotifyIconDoubleClick;
    }

    private void OnOpenSettingsClick(object? sender, EventArgs e) => _openSettingsAction();

    private void OnToggleOverlayClick(object? sender, EventArgs e) =>
        _viewModel.IsOverlayVisible = !_viewModel.IsOverlayVisible;

    private void OnToggleLockClick(object? sender, EventArgs e) =>
        _viewModel.IsOverlayLocked = !_viewModel.IsOverlayLocked;

    private void OnExitClick(object? sender, EventArgs e) => _exitApplicationAction();

    private void OnNotifyIconDoubleClick(object? sender, EventArgs e) => _openSettingsAction();

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _notifyIcon.DoubleClick -= OnNotifyIconDoubleClick;
        _openSettingsItem.Click -= OnOpenSettingsClick;
        _toggleOverlayItem.Click -= OnToggleOverlayClick;
        _toggleLockItem.Click -= OnToggleLockClick;
        _exitItem.Click -= OnExitClick;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _trayIcon.Dispose();
    }
}
