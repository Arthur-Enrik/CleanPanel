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
                var defaultSettings = new AppSettings();
                WriteToDiskImmediate(defaultSettings);
                return defaultSettings;
            }

            string json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            // Normaliza caso um arquivo antigo tenha salvo em escala 10..100
            if (settings.OverlayOpacity > 1.0 && settings.OverlayOpacity <= 100.0)
            {
                settings.OverlayOpacity /= 100.0;
            }

            // Fallback seguro de 0.80 se o valor for inválido, corrompido ou <= 0.05
            if (double.IsNaN(settings.OverlayOpacity) || settings.OverlayOpacity <= 0.05 || settings.OverlayOpacity > 1.0)
            {
                settings.OverlayOpacity = 0.80;
            }
            else
            {
                settings.OverlayOpacity = Math.Clamp(settings.OverlayOpacity, 0.1, 1.0);
            }
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
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
            if (double.IsNaN(settings.OverlayOpacity) || settings.OverlayOpacity <= 0.05 || settings.OverlayOpacity > 1.0)
            {
                settings.OverlayOpacity = 0.80;
            }
            else
            {
                settings.OverlayOpacity = Math.Clamp(Math.Round(settings.OverlayOpacity, 2), 0.1, 1.0);
            }

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
