using System.IO;
using System.Runtime.InteropServices;

namespace HardwareOverlayApp.Helpers;

public static class IconGeneratorHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Garante que o arquivo físico Assets/app.ico exista no disco.
    /// </summary>
    public static void EnsureIconFileExists(string? filePath = null)
    {
        try
        {
            string targetPath = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(targetPath))
                return;

            string? directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using System.Drawing.Bitmap bitmap = RenderModernIconBitmap(64);
            SaveBitmapAsIco(bitmap, targetPath);
        }
        catch
        {
            // Evita falha na inicialização caso o diretório seja somente leitura
        }
    }

    /// <summary>
    /// Cria um System.Drawing.Icon em memória para uso no NotifyIcon da bandeja do sistema.
    /// </summary>
    public static System.Drawing.Icon CreateTrayIcon()
    {
        using System.Drawing.Bitmap bitmap = RenderModernIconBitmap(64);
        IntPtr hIcon = bitmap.GetHicon();

        try
        {
            using var tempIcon = System.Drawing.Icon.FromHandle(hIcon);
            return (System.Drawing.Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static System.Drawing.Bitmap RenderModernIconBitmap(int size)
    {
        var bitmap = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bitmap);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.Clear(System.Drawing.Color.Transparent);

        float margin = 3f;
        float rectSize = size - (margin * 2f);
        float radius = 14f;

        using (System.Drawing.Drawing2D.GraphicsPath bgPath = CreateRoundedRectangle(margin, margin, rectSize, rectSize, radius))
        {
            // Fundo escuro arredondado (#181824)
            using var bgBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 24, 24, 36));
            g.FillPath(bgBrush, bgPath);

            // Borda neon ciano (#00E5FF)
            using var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 0, 229, 255), 2.5f);
            g.DrawPath(borderPen, bgPath);
        }

        // Desenho central: moldura discreta de chip + gráfico de pulso em gradiente Ciano (#00E5FF) para Verde (#00FF87)
        using var gradientBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new System.Drawing.PointF(10f, 32f),
            new System.Drawing.PointF(54f, 32f),
            System.Drawing.Color.FromArgb(255, 0, 229, 255),
            System.Drawing.Color.FromArgb(255, 0, 255, 135));

        using var pulsePen = new System.Drawing.Pen(gradientBrush, 3.8f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };

        System.Drawing.PointF[] pulsePoints =
        [
            new(12f, 36f),
            new(21f, 36f),
            new(26f, 20f),
            new(33f, 46f),
            new(40f, 26f),
            new(45f, 36f),
            new(52f, 36f)
        ];

        g.DrawLines(pulsePen, pulsePoints);

        return bitmap;
    }

    private static void SaveBitmapAsIco(System.Drawing.Bitmap bitmap, string filePath)
    {
        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
        byte[] pngBytes = pngStream.ToArray();

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // Cabeçalho ICONDIR (6 bytes)
        bw.Write((short)0);      // Reservado (0)
        bw.Write((short)1);      // Tipo: 1 = Ícone (.ico)
        bw.Write((short)1);      // Quantidade de imagens: 1

        // Entrada ICONDIRENTRY (16 bytes)
        bw.Write((byte)bitmap.Width);   // Largura
        bw.Write((byte)bitmap.Height);  // Altura
        bw.Write((byte)0);              // Paleta de cores (0 = sem paleta)
        bw.Write((byte)0);              // Reservado
        bw.Write((short)1);             // Planos de cor
        bw.Write((short)32);            // Bits por pixel (32bpp ARGB)
        bw.Write(pngBytes.Length);      // Tamanho do payload PNG
        bw.Write(22);                   // Offset dos dados (6 + 16 = 22)

        // Dados da imagem PNG
        bw.Write(pngBytes);
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectangle(
        float x, float y, float width, float height, float radius)
    {
        float diameter = radius * 2f;
        var path = new System.Drawing.Drawing2D.GraphicsPath();

        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
