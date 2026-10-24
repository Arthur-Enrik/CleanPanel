using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HardwareOverlayApp.Helpers;

public static class IconGeneratorHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Gera e garante que o arquivo físico Assets/app.ico contenha a nova logo oficial do CleanPanel em alta definição (múltiplas resoluções).
    /// </summary>
    public static void EnsureIconFileExists(string? filePath = null, bool forceOverwrite = true)
    {
        try
        {
            string targetPath = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (!forceOverwrite && File.Exists(targetPath))
                return;

            string? directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            SaveValidSingle256PngIco(targetPath);
        }
        catch
        {
            // Evita falha na inicialização caso o diretório de instalação seja somente leitura
        }
    }

    /// <summary>
    /// Cria um System.Drawing.Icon em alta definição para uso no NotifyIcon da bandeja do sistema (System Tray).
    /// </summary>
    public static System.Drawing.Icon CreateTrayIcon()
    {
        using System.Drawing.Bitmap bitmap = RenderCleanPanelLogoBitmap(64);
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

    /// <summary>
    /// Cria um ImageSource do WPF em alta definição com a nova logo do CleanPanel para a janela principal e TitleBar.
    /// </summary>
    public static ImageSource? CreateWindowIconSource(int size = 128)
    {
        try
        {
            using System.Drawing.Bitmap bitmap = RenderCleanPanelLogoBitmap(size);
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = ms;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Renderiza a logo minimalista do CleanPanel em estilo Fluent Design / Glassmorphism:
    /// - Base squircle em gradiente escuro profundo (#0F172A -> #1E293B) com borda iluminada.
    /// - 3 camadas translúcidas sobrepostas representando painéis limpos.
    /// - Barras de telemetria em gradiente Azul Celeste (#38BDF8) para Ciano/Verde (#2DD4BF).
    /// </summary>
    public static System.Drawing.Bitmap RenderCleanPanelLogoBitmap(int size)
    {
        var bitmap = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bitmap);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        g.Clear(System.Drawing.Color.Transparent);

        float s = size / 256f;

        // 1. Base Squircle (#0F172A -> #1E293B)
        float baseMargin = 12f * s;
        float baseSize = size - (baseMargin * 2f);
        float baseRadius = 54f * s;

        using (System.Drawing.Drawing2D.GraphicsPath squirclePath = CreateRoundedRectangle(baseMargin, baseMargin, baseSize, baseSize, baseRadius))
        {
            using var baseGradBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.PointF(baseMargin, baseMargin),
                new System.Drawing.PointF(size - baseMargin, size - baseMargin),
                System.Drawing.Color.FromArgb(255, 15, 23, 42),   // #0F172A
                System.Drawing.Color.FromArgb(255, 30, 41, 59));  // #1E293B

            g.FillPath(baseGradBrush, squirclePath);

            // Brilho sutil de iluminação no topo do squircle
            using var borderGradBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.PointF(baseMargin, baseMargin),
                new System.Drawing.PointF(size - baseMargin, size - baseMargin),
                System.Drawing.Color.FromArgb(180, 56, 189, 248), // #38BDF8
                System.Drawing.Color.FromArgb(90, 45, 212, 191)); // #2DD4BF

            using var baseBorderPen = new System.Drawing.Pen(borderGradBrush, Math.Max(1.5f, 4.5f * s));
            g.DrawPath(baseBorderPen, squirclePath);
        }

        // 2. Camada Traseira (Painel Translúcido 1 - Topo Direito)
        float backX = 68f * s;
        float backY = 40f * s;
        float backW = 142f * s;
        float backH = 112f * s;
        float cardRadius = 24f * s;

        using (System.Drawing.Drawing2D.GraphicsPath backCardPath = CreateRoundedRectangle(backX, backY, backW, backH, cardRadius))
        {
            using var backFill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(42, 56, 189, 248));
            using var backPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(85, 56, 189, 248), Math.Max(1f, 2.5f * s));
            g.FillPath(backFill, backCardPath);
            g.DrawPath(backPen, backCardPath);
        }

        // 3. Camada Intermediária (Painel Translúcido 2 - Centro Glassmorphism)
        float midX = 54f * s;
        float midY = 58f * s;
        float midW = 146f * s;
        float midH = 122f * s;

        using (System.Drawing.Drawing2D.GraphicsPath midCardPath = CreateRoundedRectangle(midX, midY, midW, midH, cardRadius))
        {
            using var midFill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(55, 30, 41, 59));
            using var midPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(105, 148, 163, 184), Math.Max(1f, 2.5f * s));
            g.FillPath(midFill, midCardPath);
            g.DrawPath(midPen, midCardPath);
        }

        // 4. Camada Principal Frontal (Painel Limpo Principal)
        float frontX = 40f * s;
        float frontY = 78f * s;
        float frontW = 156f * s;
        float frontH = 134f * s;

        using (System.Drawing.Drawing2D.GraphicsPath frontCardPath = CreateRoundedRectangle(frontX, frontY, frontW, frontH, cardRadius))
        {
            using var frontGradFill = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.PointF(frontX, frontY),
                new System.Drawing.PointF(frontX + frontW, frontY + frontH),
                System.Drawing.Color.FromArgb(238, 17, 27, 48),
                System.Drawing.Color.FromArgb(245, 15, 23, 42));

            g.FillPath(frontGradFill, frontCardPath);

            using var frontBorderGrad = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.PointF(frontX, frontY),
                new System.Drawing.PointF(frontX + frontW, frontY + frontH),
                System.Drawing.Color.FromArgb(210, 56, 189, 248), // #38BDF8
                System.Drawing.Color.FromArgb(180, 45, 212, 191)); // #2DD4BF

            using var frontPen = new System.Drawing.Pen(frontBorderGrad, Math.Max(1.2f, 3.2f * s));
            g.DrawPath(frontPen, frontCardPath);
        }

        // 5. Cabeçalho minimalista dentro do Painel Frontal (ponto neon + pílula de título)
        float dotSize = 11f * s;
        float headerY = 98f * s;
        using (var cyanBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 45, 212, 191)))
        {
            g.FillEllipse(cyanBrush, 60f * s, headerY, dotSize, dotSize);
        }

        using (var titlePillBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(130, 148, 163, 184)))
        using (System.Drawing.Drawing2D.GraphicsPath pillPath = CreateRoundedRectangle(79f * s, headerY + (2f * s), 58f * s, 7f * s, 3.5f * s))
        {
            g.FillPath(titlePillBrush, pillPath);
        }

        // 6. Três Barras de Telemetria Minimalistas em Gradiente (#38BDF8 -> #2DD4BF)
        float barStartX = 60f * s;
        float barMaxWidth = 116f * s;
        float barHeight = Math.Max(2f, 13f * s);
        float barRadius = barHeight / 2f;

        float[] barYPositions = [124f * s, 151f * s, 178f * s];
        float[] barFillRatios = [0.78f, 0.94f, 0.60f];

        using var trackBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(55, 255, 255, 255));
        using var telemetryGradBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new System.Drawing.PointF(barStartX, 0f),
            new System.Drawing.PointF(barStartX + barMaxWidth, 0f),
            System.Drawing.Color.FromArgb(255, 56, 189, 248),  // #38BDF8 (Azul Celeste)
            System.Drawing.Color.FromArgb(255, 45, 212, 191)); // #2DD4BF (Ciano / Verde Água)

        for (int i = 0; i < barYPositions.Length; i++)
        {
            float y = barYPositions[i];
            float fillW = Math.Max(barHeight, barMaxWidth * barFillRatios[i]);

            // Trilha de fundo
            using (System.Drawing.Drawing2D.GraphicsPath trackPath = CreateRoundedRectangle(barStartX, y, barMaxWidth, barHeight, barRadius))
            {
                g.FillPath(trackBrush, trackPath);
            }

            // Barra ativa em gradiente
            using (System.Drawing.Drawing2D.GraphicsPath activePath = CreateRoundedRectangle(barStartX, y, fillW, barHeight, barRadius))
            {
                g.FillPath(telemetryGradBrush, activePath);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Escreve um arquivo .ico binário padrão do Windows (cabeçalho ICONDIR de 6 bytes + ICONDIRENTRY de 16 bytes + payload PNG 256x256 no offset 22).
    /// Evita corrupção do cabeçalho PE Win32 durante o dotnet publish e compilação do Inno Setup (erro 0x800700C1).
    /// </summary>
    public static void SaveValidSingle256PngIco(string filePath)
    {
        using System.Drawing.Bitmap bmp = RenderCleanPanelLogoBitmap(256);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        byte[] pngBytes = ms.ToArray();

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        // Cabeçalho ICONDIR (6 bytes)
        bw.Write((short)0); // Reserved = 0
        bw.Write((short)1); // Type = 1 (Icon)
        bw.Write((short)1); // Count = 1 imagem

        // Entrada ICONDIRENTRY (16 bytes)
        bw.Write((byte)0);          // Width = 0 (256px)
        bw.Write((byte)0);          // Height = 0 (256px)
        bw.Write((byte)0);          // ColorCount = 0
        bw.Write((byte)0);          // Reserved = 0
        bw.Write((short)1);         // Planes = 1
        bw.Write((short)32);        // BitCount = 32 bpp
        bw.Write(pngBytes.Length);  // BytesInRes = tamanho exato do array PNG
        bw.Write(22);               // ImageOffset = 22 (6 + 16 bytes)

        // Payload PNG iniciando exatamente no byte 22
        bw.Write(pngBytes);
        bw.Flush();
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectangle(
        float x, float y, float width, float height, float radius)
    {
        float diameter = Math.Min(radius * 2f, Math.Min(width, height));
        var path = new System.Drawing.Drawing2D.GraphicsPath();

        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
