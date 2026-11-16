using System.IO;
using System.Text.Json;
using HardwareOverlayApp.Models;

namespace HardwareOverlayApp.Services;

public class SettingsService : IDisposable
{
    private const int DebounceDelayMs = 300;

    private readonly string _settingsFilePath;
    private readonly object _debounceLock = new();
    private CancellationTokenSource? _debounceCts;
    private AppSettings? _pendingSettings;
    private bool _isDisposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsService()
    {
        _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                var defaultSettings = SanitizeSettings(new AppSettings());
                WriteToDiskImmediate(defaultSettings);
                return defaultSettings;
            }

            string json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return SanitizeSettings(settings);
        }
        catch
        {
            return SanitizeSettings(new AppSettings());
        }
    }

    public static AppSettings SanitizeSettings(AppSettings? settings)
    {
        settings ??= new AppSettings();

        // Normaliza opacidade caso tenha sido salva em 0..100 ou esteja fora do intervalo
        if (double.IsNaN(settings.OverlayOpacity) || double.IsInfinity(settings.OverlayOpacity))
        {
            settings.OverlayOpacity = 0.85;
        }
        else
        {
            if (settings.OverlayOpacity > 1.0 && settings.OverlayOpacity <= 100.0)
            {
                settings.OverlayOpacity /= 100.0;
            }

            if (settings.OverlayOpacity <= 0.05 || settings.OverlayOpacity > 1.0)
            {
                settings.OverlayOpacity = 0.85;
            }
            else
            {
                settings.OverlayOpacity = Math.Clamp(Math.Round(settings.OverlayOpacity, 2), 0.05, 1.0);
            }
        }

        // Garante que OverlayScale seja sempre um double válido > 0 para nunca quebrar o ScaleTransform do WPF
        if (double.IsNaN(settings.OverlayScale) || double.IsInfinity(settings.OverlayScale) || settings.OverlayScale <= 0.0)
        {
            settings.OverlayScale = 1.0;
        }
        else
        {
            settings.OverlayScale = Math.Clamp(Math.Round(settings.OverlayScale, 2), 0.70, 2.00);
        }

        if (double.IsNaN(settings.OverlayLeft) || double.IsInfinity(settings.OverlayLeft))
        {
            settings.OverlayLeft = 50.0;
        }

        if (double.IsNaN(settings.OverlayTop) || double.IsInfinity(settings.OverlayTop))
        {
            settings.OverlayTop = 50.0;
        }

        if (string.IsNullOrWhiteSpace(settings.AppTheme))
        {
            settings.AppTheme = "Dark";
        }

        if (string.IsNullOrWhiteSpace(settings.AccentColorHex))
        {
            settings.AccentColorHex = "#FAFAFA";
        }

        if (string.IsNullOrWhiteSpace(settings.OverlayBackgroundColorHex))
        {
            settings.OverlayBackgroundColorHex = "#09090B";
        }

        if (string.IsNullOrWhiteSpace(settings.OverlayBorderColorHex))
        {
            settings.OverlayBorderColorHex = "#27272A";
        }

        if (string.IsNullOrWhiteSpace(settings.OverlayHeaderColorHex))
        {
            settings.OverlayHeaderColorHex = "#FAFAFA";
        }

        if (string.IsNullOrWhiteSpace(settings.OverlayValueTextColorHex))
        {
            settings.OverlayValueTextColorHex = "#FAFAFA";
        }

        if (double.IsNaN(settings.TempWarningThreshold) || double.IsInfinity(settings.TempWarningThreshold) || settings.TempWarningThreshold <= 0.0)
        {
            settings.TempWarningThreshold = 70.0;
        }
        else
        {
            settings.TempWarningThreshold = Math.Clamp(Math.Round(settings.TempWarningThreshold), 30.0, 110.0);
        }

        if (double.IsNaN(settings.TempCriticalThreshold) || double.IsInfinity(settings.TempCriticalThreshold) || settings.TempCriticalThreshold <= 0.0)
        {
            settings.TempCriticalThreshold = 85.0;
        }
        else
        {
            settings.TempCriticalThreshold = Math.Clamp(Math.Round(settings.TempCriticalThreshold), 35.0, 120.0);
        }

        if (settings.TempCriticalThreshold <= settings.TempWarningThreshold)
        {
            settings.TempCriticalThreshold = Math.Clamp(settings.TempWarningThreshold + 5.0, 35.0, 120.0);
        }

        if (settings.OverlaySectionOrder is null || settings.OverlaySectionOrder.Count == 0)
        {
            settings.OverlaySectionOrder = ["CPU", "GPU", "RAM", "FPS"];
        }
        else
        {
            settings.OverlaySectionOrder = settings.OverlaySectionOrder
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (settings.OverlaySectionOrder.Count == 0)
            {
                settings.OverlaySectionOrder = ["CPU", "GPU", "RAM", "FPS"];
            }
        }

        return settings;
    }

    /// <summary>
    /// Agenda a gravação de settings.json com Debounce assíncrono de 300ms, evitando escritas excessivas
    /// no disco ao arrastar o Slider de Opacidade ou mover o Overlay.
    /// </summary>
    public void Save(AppSettings settings)
    {
        if (_isDisposed)
            return;

        CancellationToken token;

        lock (_debounceLock)
        {
            _pendingSettings = settings;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            token = _debounceCts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelayMs, token).ConfigureAwait(false);

                AppSettings? toWrite;
                lock (_debounceLock)
                {
                    if (token.IsCancellationRequested)
                        return;

                    toWrite = _pendingSettings;
                    _pendingSettings = null;
                }

                if (toWrite is not null)
                {
                    WriteToDiskImmediate(toWrite);
                }
            }
            catch (OperationCanceledException)
            {
                // Novo evento de alteração substituiu este ciclo de debounce
            }
            catch
            {
                // Ignora falhas transitórias de I/O
            }
        }, token);
    }

    public void FlushImmediate()
    {
        AppSettings? pending;
        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
            pending = _pendingSettings;
            _pendingSettings = null;
        }

        if (pending is not null)
        {
            WriteToDiskImmediate(pending);
        }
    }

    private void WriteToDiskImmediate(AppSettings settings)
    {
        try
        {
            SanitizeSettings(settings);
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Ignora falhas transitórias de escrita em disco
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        FlushImmediate();
    }
}
