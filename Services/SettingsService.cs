using System.IO;
using System.Text.Json;
using HardwareOverlayApp.Models;

namespace HardwareOverlayApp.Services;

public class SettingsService : IDisposable
{
    private const int DebounceDelayMs = 300;
    private const double DefaultOverlayOpacity = 0.80;

    private readonly string _settingsFilePath;
    private readonly string _tempSettingsFilePath;
    private readonly object _debounceLock = new();
    private readonly object _fileIoLock = new();
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
        _tempSettingsFilePath = _settingsFilePath + ".tmp";
    }

    public AppSettings Load()
    {
        lock (_fileIoLock)
        {
            try
            {
                string? sourcePath = null;
                bool recoveredFromTemp = false;

                if (File.Exists(_settingsFilePath))
                {
                    sourcePath = _settingsFilePath;
                }
                else if (File.Exists(_tempSettingsFilePath))
                {
                    sourcePath = _tempSettingsFilePath;
                    recoveredFromTemp = true;
                }

                if (sourcePath is null)
                {
                    var defaultSettings = new AppSettings();
                    WriteToDiskImmediateLocked(defaultSettings);
                    return defaultSettings;
                }

                string json = File.ReadAllText(sourcePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                NormalizeOpacity(settings);

                if (recoveredFromTemp)
                {
                    WriteToDiskImmediateLocked(settings);
                }

                return settings;
            }
            catch
            {
                return new AppSettings();
            }
        }
    }

    /// <summary>
    /// Agenda a gravação de settings.json com Debounce assíncrono de 300ms, evitando escritas excessivas
    /// no disco ao arrastar o Slider de Opacidade ou mover o Overlay.
    /// </summary>
    public void Save(AppSettings settings)
    {
        CancellationToken token;

        lock (_debounceLock)
        {
            if (_isDisposed)
                return;

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
                    if (_isDisposed || token.IsCancellationRequested)
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
        lock (_fileIoLock)
        {
            WriteToDiskImmediateLocked(settings);
        }
    }

    private void WriteToDiskImmediateLocked(AppSettings settings)
    {
        try
        {
            NormalizeOpacity(settings);
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_tempSettingsFilePath, json);
            File.Move(_tempSettingsFilePath, _settingsFilePath, overwrite: true);
        }
        catch
        {
            // Ignora falhas transitórias de escrita em disco
        }
    }

    private static void NormalizeOpacity(AppSettings settings)
    {
        if (settings.OverlayOpacity > 1.0 && settings.OverlayOpacity <= 100.0)
        {
            settings.OverlayOpacity /= 100.0;
        }

        if (double.IsNaN(settings.OverlayOpacity) || settings.OverlayOpacity <= 0.05 || settings.OverlayOpacity > 1.0)
        {
            settings.OverlayOpacity = DefaultOverlayOpacity;
        }
        else
        {
            settings.OverlayOpacity = Math.Clamp(Math.Round(settings.OverlayOpacity, 2), 0.1, 1.0);
        }
    }

    public void Dispose()
    {
        AppSettings? pending;
        lock (_debounceLock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
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
}
