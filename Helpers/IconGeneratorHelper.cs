using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources;

namespace HardwareOverlayApp.Helpers;

/// <summary>
/// Carrega os ícones oficiais do aplicativo a partir do recurso embutido Assets/app.ico.
/// </summary>
public static class IconGeneratorHelper
{
    private const string AppIconPackUri = "pack://application:,,,/Assets/app.ico";

    /// <summary>
    /// Carrega o System.Drawing.Icon oficial a partir do recurso embutido Assets/app.ico para o NotifyIcon da bandeja.
    /// </summary>
    public static System.Drawing.Icon CreateTrayIcon()
    {
        try
        {
            StreamResourceInfo? resourceInfo = Application.GetResourceStream(new Uri(AppIconPackUri, UriKind.Absolute));
            if (resourceInfo?.Stream is not null)
            {
                using Stream stream = resourceInfo.Stream;
                return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
            }
        }
        catch
        {
            // Fallback para caminho em disco ou ícone associado do executável
        }

        string diskIconPath = ResolveDiskIconPath();
        if (File.Exists(diskIconPath))
        {
            try
            {
                return new System.Drawing.Icon(diskIconPath, System.Windows.Forms.SystemInformation.SmallIconSize);
            }
            catch
            {
                // Ignora e tenta extrair do executável
            }
        }

        string? exePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
        {
            System.Drawing.Icon? exeIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (exeIcon is not null)
            {
                return exeIcon;
            }
        }

        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    /// <summary>
    /// Carrega o ImageSource oficial do Assets/app.ico para as janelas WPF e TitleBar.
    /// </summary>
    public static ImageSource? CreateWindowIconSource(int size = 128)
    {
        _ = size;

        try
        {
            BitmapFrame frame = BitmapFrame.Create(
                new Uri(AppIconPackUri, UriKind.Absolute),
                BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }
        catch
        {
            try
            {
                string diskIconPath = ResolveDiskIconPath();
                if (File.Exists(diskIconPath))
                {
                    BitmapFrame frame = BitmapFrame.Create(
                        new Uri(diskIconPath, UriKind.Absolute),
                        BitmapCreateOptions.None,
                        BitmapCacheOption.OnLoad);
                    frame.Freeze();
                    return frame;
                }
            }
            catch
            {
                // Fallback seguro caso o ícone não possa ser decodificado
            }

            return null;
        }
    }

    private static string ResolveDiskIconPath()
    {
        string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(assetsPath))
            return assetsPath;

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
    }
}
