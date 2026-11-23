using System.Diagnostics;
using System.IO;
using System.Text.Json;
using HardwareOverlayApp.Helpers;
using HardwareOverlayApp.Models;

namespace HardwareOverlayApp.Services;

/// <summary>
/// Serviço opcional e preguiçoso (lazy-loading) de rastreamento de sessões de jogos em Tela Cheia Real.
/// Utiliza histograma de memória fixa (~24 KB) para cálculo exato de FPS Médio e 1% Low sem alocações no hot-path ETW.
/// </summary>
public sealed class GameHistoryService : IFrameSampleSink, IDisposable
{
    private const double MinValidSessionDurationSeconds = 300.0; // Estritamente superior a 5 minutos
    private const long MinSessionValidFrames = 100;
    private const int MinActiveFramesPerTick = 5;
    private const int StartupWarmupTicksRequired = 3;
    private const int RefocusStabilizationTicksRequired = 2;
    private const int IdleFreezeTicksBeforePauseSampling = 2;
    private const double AltTabGraceTimeoutSeconds = 180.0;
    private const double MaxSingleTickCreditSeconds = 2.0;
    private const double SystemSleepJumpThresholdSeconds = 10.0;
    private const double MinSampleFrametimeMs = 0.5;
    private const double MaxSampleFrametimeMs = 500.0;
    private const int MaxIdentityCacheEntries = 128;
    private const int DisposeFlushTimeoutMs = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Aplicativo atual
        "Cleanpanel",
        "HardwareOverlayApp",

        // Shell e utilitários do Windows
        "explorer",
        "dwm",
        "SearchHost",
        "SearchApp",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "ShellHost",
        "LockApp",
        "ApplicationFrameHost",
        "SystemSettings",
        "Taskmgr",
        "mmc",
        "mstsc",
        "ScreenClippingHost",
        "SnippingTool",
        "TextInputHost",
        "Widgets",
        "WindowsTerminal",
        "cmd",
        "powershell",
        "pwsh",
        "conhost",
        "WerFault",
        "msiexec",

        // Navegadores
        "chrome",
        "msedge",
        "firefox",
        "brave",
        "opera",
        "opera_gx",
        "vivaldi",
        "arc",
        "waterfox",
        "librewolf",
        "zen",
        "iexplore",
        "chromium",
        "thorium",
        "floorp",
        "maxthon",
        "yandex",

        // Players de vídeo, áudio, visualizadores e captura
        "vlc",
        "mpv",
        "mpc-hc",
        "mpc-hc64",
        "mpc-be",
        "mpc-be64",
        "potplayermini",
        "potplayermini64",
        "potplayer",
        "potplayer64",
        "wmplayer",
        "Video.UI",
        "Microsoft.Media.Player",
        "MediaPlayer",
        "kmplayer",
        "gom",
        "kodi",
        "plex",
        "stremio",
        "netflix",
        "primevideo",
        "spotify",
        "itunes",
        "foobar2000",
        "musicbee",
        "Photos",
        "Microsoft.Photos",
        "PhotosApp",
        "irfanview",
        "i_view64",
        "nomacs",
        "honeyview",
        "obs64",
        "obs32",
        "streamlabs obs",
        "xsplit.core",

        // Escritório, PDFs, IDEs, editores, comunicação e criação
        "winword",
        "excel",
        "powerpnt",
        "outlook",
        "olk",
        "onenote",
        "msaccess",
        "mspub",
        "visio",
        "soffice",
        "soffice.bin",
        "swriter",
        "scalc",
        "simpress",
        "acrobat",
        "acrord32",
        "foxitpdfreader",
        "sumatrapdf",
        "code",
        "devenv",
        "rider64",
        "idea64",
        "pycharm64",
        "webstorm64",
        "clion64",
        "goland64",
        "datagrip64",
        "studio64",
        "eclipse",
        "sublime_text",
        "notepad",
        "notepad++",
        "cursor",
        "windsurf",
        "antigravity",
        "discord",
        "discordptb",
        "discordcanary",
        "teams",
        "ms-teams",
        "slack",
        "zoom",
        "skype",
        "telegram",
        "whatsapp",
        "signal",
        "blender",
        "photoshop",
        "illustrator",
        "premiere",
        "afterfx",
        "resolve",
        "figma",
        "gimp-2.10",
        "krita",
        "paintdotnet",
        "mspaint",
        "audacity",
        "fl64",
        "ableton",

        // Launchers e overlays
        "steam",
        "steamwebhelper",
        "epicgameslauncher",
        "epicwebhelper",
        "eadesktop",
        "eabackgroundservice",
        "upc",
        "ubisoftconnect",
        "battle.net",
        "galaxyclient",
        "playnite.desktopapp",
        "playnite.fullscreenapp",
        "xboxpcapp",
        "riotclientservices",
        "riotclientux",
        "riotclientuxrender",
        "rockstarlauncher",
        "launcherpatcher",
        "nvidia app",
        "nvcontainer",
        "geforceexperience",
        "radeonsoftware",
        "amdrsserv"
    };

    private static readonly string[] GenericMetadataPlaceholders =
    [
        "UnrealEngine",
        "Unreal Engine",
        "BootstrapPackagedGame",
        "UE4Game",
        "UE5Game",
        "UnityPlayer",
        "Unity",
        "Game",
        "Application",
        "Executable",
        "Windows Application"
    ];

    private static readonly string[] ProcessSuffixesToStrip =
    [
        "-Win64-Shipping",
        "-WinGDK-Shipping",
        "_Win64_Shipping",
        "_Shipping",
        "_DX12",
        "_DX11",
        "_Vulkan",
        "_x64",
        "-x64"
    ];

    private enum TrackingState : byte
    {
        Idle = 0,
        Warmup = 1,
        Active = 2,
        Paused = 3
    }

    private readonly record struct GameIdentity(string GameKey, string DisplayName, string CleanProcessName);

    private sealed class CompletedSessionData
    {
        public required string GameKey { get; init; }
        public required string DisplayName { get; init; }
        public required string ProcessName { get; init; }
        public required GameSessionRecord Session { get; init; }
    }

    /// <summary>
    /// Histograma fixo de 1.500 compartimentos (~24 KB constantes) para cálculo preciso de FPS Médio e 1% Low sem alocações por quadro.
    /// Faixa 0: [0,0 ; 40,0) ms @ 0,05 ms -> índices 0..799
    /// Faixa 1: [40,0 ; 120,0) ms @ 0,25 ms -> índices 800..1119
    /// Faixa 2: [120,0 ; 500,0] ms @ 1,00 ms -> índices 1120..1499
    /// </summary>
    private sealed class FrametimeHistogram
    {
        private const int BucketCount = 1500;
        private readonly long[] _counts = new long[BucketCount];
        private readonly double[] _sumMs = new double[BucketCount];
        private long _totalFrames;
        private double _totalFrametimeMs;

        public long TotalFrames => _totalFrames;

        public void Reset()
        {
            Array.Clear(_counts);
            Array.Clear(_sumMs);
            _totalFrames = 0;
            _totalFrametimeMs = 0.0;
        }

        public void Add(double frametimeMs)
        {
            if (double.IsNaN(frametimeMs) || double.IsInfinity(frametimeMs) ||
                frametimeMs < MinSampleFrametimeMs || frametimeMs > MaxSampleFrametimeMs)
            {
                return;
            }

            int bucket;
            if (frametimeMs < 40.0)
            {
                bucket = (int)(frametimeMs * 20.0);
            }
            else if (frametimeMs < 120.0)
            {
                bucket = 800 + (int)((frametimeMs - 40.0) * 4.0);
            }
            else
            {
                bucket = Math.Min(BucketCount - 1, 1120 + (int)(frametimeMs - 120.0));
            }

            _counts[bucket]++;
            _sumMs[bucket] += frametimeMs;
            _totalFrames++;
            _totalFrametimeMs += frametimeMs;
        }

        public bool TryComputeMetrics(out double averageFps, out double onePercentLowFps)
        {
            averageFps = 0.0;
            onePercentLowFps = 0.0;

            if (_totalFrames < MinSessionValidFrames || _totalFrametimeMs <= 0.0)
                return false;

            averageFps = (_totalFrames * 1000.0) / _totalFrametimeMs;

            long slowestTarget = Math.Max(1L, _totalFrames / 100L);
            long collected = 0;
            double slowestSumMs = 0.0;

            for (int i = BucketCount - 1; i >= 0 && collected < slowestTarget; i--)
            {
                long count = _counts[i];
                if (count <= 0)
                    continue;

                long needed = slowestTarget - collected;
                if (count <= needed)
                {
                    slowestSumMs += _sumMs[i];
                    collected += count;
                }
                else
                {
                    double meanBucketMs = _sumMs[i] / count;
                    slowestSumMs += meanBucketMs * needed;
                    collected += needed;
                }
            }

            if (collected <= 0 || slowestSumMs <= 0.0)
                return false;

            double avgSlowestMs = slowestSumMs / collected;
            onePercentLowFps = Math.Clamp(1000.0 / avgSlowestMs, 1.0, averageFps);
            return true;
        }
    }

    private readonly FpsMonitorService _fpsMonitorService;
    private readonly string _historyFilePath;
    private readonly string _tempHistoryFilePath;
    private readonly string _windowsDirectoryPrefix;

    private readonly object _stateLock = new();
    private readonly object _sampleLock = new();
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    private readonly FrametimeHistogram _histogram = new();
    private readonly Dictionary<string, GameIdentity> _identityCache = new(StringComparer.OrdinalIgnoreCase);

    private GameHistoryDocument? _loadedDocument;

    private TrackingState _trackingState = TrackingState.Idle;
    private volatile int _trackedPid = -1;
    private volatile bool _isRecordingSamples;
    private volatile bool _requiresFpsCapture;
    private volatile bool _isDisposed;
    private int _framesSinceLastTick;

    private string _activeGameKey = string.Empty;
    private string _activeDisplayName = string.Empty;
    private string _activeProcessName = string.Empty;
    private DateTime _sessionStartedAtUtc;
    private double _effectivePlaytimeSeconds;
    private int _warmupTicks;
    private int _refocusTicksRemaining;
    private int _consecutiveIdleTicks;
    private long _lastHeartbeatWallTicks;
    private long _pausedStartWallTicks;

    private string? _activeSessionStatusText;

    public bool RequiresFpsCapture => _requiresFpsCapture;

    public string? ActiveSessionStatusText
    {
        get
        {
            lock (_stateLock)
            {
                return _activeSessionStatusText;
            }
        }
    }

    public event Action<string?>? ActiveSessionStatusChanged;
    public event Action? HistoryChanged;

    public GameHistoryService(FpsMonitorService fpsMonitorService)
    {
        _fpsMonitorService = fpsMonitorService;
        _historyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game_history.json");
        _tempHistoryFilePath = _historyFilePath + ".tmp";

        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        _windowsDirectoryPrefix = string.IsNullOrWhiteSpace(windowsDir)
            ? string.Empty
            : windowsDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        _lastHeartbeatWallTicks = Stopwatch.GetTimestamp();
        _fpsMonitorService.SetFrameSampleSink(this);
    }

    /// <summary>
    /// Hot-path chamado diretamente pela thread ETW apenas para intervalos da SwapChain dominante.
    /// Hierarquia estrita de locks: nunca invoca FpsMonitorService a partir de _sampleLock.
    /// </summary>
    public void OnFrameInterval(int processId, double frametimeMs)
    {
        if (_isDisposed || processId <= 4 || processId != _trackedPid)
            return;

        Interlocked.Increment(ref _framesSinceLastTick);

        if (!_isRecordingSamples)
            return;

        lock (_sampleLock)
        {
            if (!_isDisposed && _isRecordingSamples && processId == _trackedPid)
            {
                _histogram.Add(frametimeMs);
            }
        }
    }

    /// <summary>
    /// Avança a máquina de estados da sessão no batimento de 1 segundo da aplicação.
    /// Retorna true caso o motor de FPS (ETW) precise permanecer ativo para o rastreamento em andamento.
    /// </summary>
    public bool ProcessHeartbeatTick(bool hasFullscreenWindow, ForegroundFullscreenInfo fullscreenInfo)
    {
        CompletedSessionData? completedToSave = null;
        string? newStatus = null;
        bool statusChanged = false;
        bool needFps;

        lock (_stateLock)
        {
            if (_isDisposed)
                return false;

            long nowTicks = Stopwatch.GetTimestamp();
            double rawDeltaSeconds = _lastHeartbeatWallTicks > 0
                ? (double)(nowTicks - _lastHeartbeatWallTicks) / Stopwatch.Frequency
                : 1.0;
            _lastHeartbeatWallTicks = nowTicks;

            if (rawDeltaSeconds < 0.0)
                rawDeltaSeconds = 0.0;

            bool sleepJumpDetected = rawDeltaSeconds > SystemSleepJumpThresholdSeconds;
            int framesInTick = Interlocked.Exchange(ref _framesSinceLastTick, 0);

            bool eligibleFullscreenGame =
                hasFullscreenWindow &&
                fullscreenInfo.ProcessId > 4 &&
                !IsExcludedNonGameProcess(fullscreenInfo.ProcessName, fullscreenInfo.ExecutablePath);

            switch (_trackingState)
            {
                case TrackingState.Idle:
                    if (eligibleFullscreenGame)
                    {
                        BeginWarmupLocked(fullscreenInfo);
                        needFps = true;
                    }
                    else
                    {
                        _requiresFpsCapture = false;
                        needFps = false;
                    }
                    break;

                case TrackingState.Warmup:
                    if (eligibleFullscreenGame && fullscreenInfo.ProcessId == _trackedPid)
                    {
                        if (sleepJumpDetected || framesInTick < MinActiveFramesPerTick)
                        {
                            _warmupTicks = 0;
                        }
                        else
                        {
                            _warmupTicks++;
                            if (_warmupTicks >= StartupWarmupTicksRequired)
                            {
                                TransitionWarmupToActiveLocked();
                                statusChanged = UpdateStatusLocked($"{_activeDisplayName} • Registrando", out newStatus);
                            }
                        }

                        _requiresFpsCapture = true;
                        needFps = true;
                    }
                    else if (eligibleFullscreenGame)
                    {
                        BeginWarmupLocked(fullscreenInfo);
                        needFps = true;
                    }
                    else
                    {
                        ResetToIdleLocked();
                        needFps = false;
                    }
                    break;

                case TrackingState.Active:
                    if (eligibleFullscreenGame && fullscreenInfo.ProcessId == _trackedPid)
                    {
                        if (sleepJumpDetected)
                        {
                            _isRecordingSamples = false;
                            _refocusTicksRemaining = RefocusStabilizationTicksRequired;
                            _consecutiveIdleTicks = 0;
                            statusChanged = UpdateStatusLocked($"{_activeDisplayName} • Retomando...", out newStatus);
                        }
                        else if (framesInTick >= MinActiveFramesPerTick)
                        {
                            _consecutiveIdleTicks = 0;
                            if (_refocusTicksRemaining > 0)
                            {
                                _refocusTicksRemaining--;
                                if (_refocusTicksRemaining == 0)
                                {
                                    _isRecordingSamples = true;
                                    statusChanged = UpdateStatusLocked($"{_activeDisplayName} • Registrando", out newStatus);
                                }
                            }
                            else
                            {
                                _effectivePlaytimeSeconds += Math.Min(rawDeltaSeconds, MaxSingleTickCreditSeconds);
                                _isRecordingSamples = true;
                            }
                        }
                        else
                        {
                            // Jogo ocioso/travado sem renderização ativa: não credita tempo nem polui histograma
                            _consecutiveIdleTicks++;
                            if (_consecutiveIdleTicks >= IdleFreezeTicksBeforePauseSampling)
                            {
                                _isRecordingSamples = false;
                                _refocusTicksRemaining = 1;
                            }
                        }

                        _requiresFpsCapture = true;
                        needFps = true;
                    }
                    else
                    {
                        _isRecordingSamples = false;

                        if (eligibleFullscreenGame)
                        {
                            completedToSave = FinalizeCurrentSessionLocked();
                            BeginWarmupLocked(fullscreenInfo);
                            statusChanged = UpdateStatusLocked(null, out newStatus);
                            needFps = true;
                        }
                        else if (!WindowWin32Helper.IsProcessAlive(_trackedPid))
                        {
                            completedToSave = FinalizeCurrentSessionLocked();
                            ResetToIdleLocked();
                            statusChanged = UpdateStatusLocked(null, out newStatus);
                            needFps = false;
                        }
                        else
                        {
                            _trackingState = TrackingState.Paused;
                            _pausedStartWallTicks = nowTicks;
                            _requiresFpsCapture = false;
                            statusChanged = UpdateStatusLocked($"{_activeDisplayName} • Pausado (Alt+Tab)", out newStatus);
                            needFps = false;
                        }
                    }
                    break;

                case TrackingState.Paused:
                    if (eligibleFullscreenGame && fullscreenInfo.ProcessId == _trackedPid)
                    {
                        _trackingState = TrackingState.Active;
                        _refocusTicksRemaining = RefocusStabilizationTicksRequired;
                        _consecutiveIdleTicks = 0;
                        _isRecordingSamples = false;
                        _requiresFpsCapture = true;
                        statusChanged = UpdateStatusLocked($"{_activeDisplayName} • Retomando...", out newStatus);
                        needFps = true;
                    }
                    else if (eligibleFullscreenGame)
                    {
                        completedToSave = FinalizeCurrentSessionLocked();
                        BeginWarmupLocked(fullscreenInfo);
                        statusChanged = UpdateStatusLocked(null, out newStatus);
                        needFps = true;
                    }
                    else
                    {
                        double pausedElapsedSeconds = _pausedStartWallTicks > 0
                            ? (double)(nowTicks - _pausedStartWallTicks) / Stopwatch.Frequency
                            : AltTabGraceTimeoutSeconds;

                        if (!WindowWin32Helper.IsProcessAlive(_trackedPid) ||
                            pausedElapsedSeconds >= AltTabGraceTimeoutSeconds)
                        {
                            completedToSave = FinalizeCurrentSessionLocked();
                            ResetToIdleLocked();
                            statusChanged = UpdateStatusLocked(null, out newStatus);
                        }

                        _requiresFpsCapture = false;
                        needFps = false;
                    }
                    break;

                default:
                    _requiresFpsCapture = false;
                    needFps = false;
                    break;
            }
        }

        if (statusChanged)
        {
            ActiveSessionStatusChanged?.Invoke(newStatus);
        }

        if (completedToSave is not null)
        {
            _ = PersistSessionSafeAsync(completedToSave);
        }

        return needFps;
    }

    /// <summary>
    /// Carrega sob demanda e retorna uma cópia isolada do histórico armazenado em disco.
    /// </summary>
    public async Task<GameHistoryDocument> GetHistorySnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isDisposed)
                return new GameHistoryDocument();

            GameHistoryDocument doc = await EnsureDocumentLoadedLockedAsync(cancellationToken).ConfigureAwait(false);
            return CloneDocument(doc);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task DeleteSessionAsync(string gameKey, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(gameKey) || string.IsNullOrWhiteSpace(sessionId))
            return;

        bool changed = false;

        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isDisposed)
                return;

            GameHistoryDocument doc = await EnsureDocumentLoadedLockedAsync(CancellationToken.None).ConfigureAwait(false);
            GameHistoryRecord? game = doc.Games.FirstOrDefault(g =>
                string.Equals(g.GameKey, gameKey, StringComparison.OrdinalIgnoreCase));

            if (game is not null)
            {
                int removed = game.Sessions.RemoveAll(s =>
                    string.Equals(s.Id, sessionId, StringComparison.Ordinal));

                if (removed > 0)
                {
                    if (game.Sessions.Count == 0)
                    {
                        doc.Games.Remove(game);
                    }
                    else
                    {
                        game.LastPlayedUtc = game.Sessions.Max(s => s.EndedAtUtc);
                    }

                    await WriteDocumentAtomicLockedAsync(doc).ConfigureAwait(false);
                    changed = true;
                }
            }
        }
        catch
        {
            // Isola falhas transitórias de I/O
        }
        finally
        {
            _ioLock.Release();
        }

        if (changed)
        {
            HistoryChanged?.Invoke();
        }
    }

    public async Task DeleteGameAsync(string gameKey)
    {
        if (string.IsNullOrWhiteSpace(gameKey))
            return;

        bool changed = false;

        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isDisposed)
                return;

            GameHistoryDocument doc = await EnsureDocumentLoadedLockedAsync(CancellationToken.None).ConfigureAwait(false);
            int removed = doc.Games.RemoveAll(g =>
                string.Equals(g.GameKey, gameKey, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                await WriteDocumentAtomicLockedAsync(doc).ConfigureAwait(false);
                changed = true;
            }
        }
        catch
        {
            // Isola falhas transitórias de I/O
        }
        finally
        {
            _ioLock.Release();
        }

        if (changed)
        {
            HistoryChanged?.Invoke();
        }
    }

    public async Task ClearAllAsync()
    {
        bool changed = false;

        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isDisposed)
                return;

            GameHistoryDocument doc = await EnsureDocumentLoadedLockedAsync(CancellationToken.None).ConfigureAwait(false);
            if (doc.Games.Count > 0 || File.Exists(_historyFilePath))
            {
                doc.Games.Clear();
                await WriteDocumentAtomicLockedAsync(doc).ConfigureAwait(false);
                changed = true;
            }
        }
        catch
        {
            // Isola falhas transitórias de I/O
        }
        finally
        {
            _ioLock.Release();
        }

        if (changed)
        {
            HistoryChanged?.Invoke();
        }
    }

    private void BeginWarmupLocked(ForegroundFullscreenInfo info)
    {
        GameIdentity identity = ResolveGameIdentity(info.ProcessName, info.ExecutablePath);

        _trackingState = TrackingState.Warmup;
        _trackedPid = info.ProcessId;
        _isRecordingSamples = false;
        _requiresFpsCapture = true;
        _activeGameKey = identity.GameKey;
        _activeDisplayName = identity.DisplayName;
        _activeProcessName = identity.CleanProcessName;
        _warmupTicks = 0;
        _refocusTicksRemaining = 0;
        _consecutiveIdleTicks = 0;
        _effectivePlaytimeSeconds = 0.0;
        Interlocked.Exchange(ref _framesSinceLastTick, 0);
    }

    private void TransitionWarmupToActiveLocked()
    {
        lock (_sampleLock)
        {
            _histogram.Reset();
        }

        _trackingState = TrackingState.Active;
        _sessionStartedAtUtc = DateTime.UtcNow;
        _effectivePlaytimeSeconds = 0.0;
        _consecutiveIdleTicks = 0;
        _refocusTicksRemaining = 0;
        _isRecordingSamples = true;
        _requiresFpsCapture = true;
    }

    private void ResetToIdleLocked()
    {
        _trackingState = TrackingState.Idle;
        _trackedPid = -1;
        _isRecordingSamples = false;
        _requiresFpsCapture = false;
        _activeGameKey = string.Empty;
        _activeDisplayName = string.Empty;
        _activeProcessName = string.Empty;
        _effectivePlaytimeSeconds = 0.0;
        _warmupTicks = 0;
        _refocusTicksRemaining = 0;
        _consecutiveIdleTicks = 0;
        _pausedStartWallTicks = 0;
        Interlocked.Exchange(ref _framesSinceLastTick, 0);

        lock (_sampleLock)
        {
            _histogram.Reset();
        }
    }

    private CompletedSessionData? FinalizeCurrentSessionLocked()
    {
        CompletedSessionData? result = null;
        double durationSeconds = _effectivePlaytimeSeconds;

        lock (_sampleLock)
        {
            if (durationSeconds > MinValidSessionDurationSeconds &&
                _histogram.TryComputeMetrics(out double avgFps, out double onePercentLowFps))
            {
                DateTime endedAtUtc = DateTime.UtcNow;
                result = new CompletedSessionData
                {
                    GameKey = _activeGameKey,
                    DisplayName = _activeDisplayName,
                    ProcessName = _activeProcessName,
                    Session = new GameSessionRecord
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        StartedAtUtc = _sessionStartedAtUtc == default
                            ? endedAtUtc.AddSeconds(-durationSeconds)
                            : _sessionStartedAtUtc,
                        EndedAtUtc = endedAtUtc,
                        DurationSeconds = Math.Round(durationSeconds, 1),
                        AverageFps = Math.Round(avgFps, 1),
                        OnePercentLowFps = Math.Round(onePercentLowFps, 1)
                    }
                };
            }

            _histogram.Reset();
        }

        _effectivePlaytimeSeconds = 0.0;
        _warmupTicks = 0;
        _refocusTicksRemaining = 0;
        _consecutiveIdleTicks = 0;
        _pausedStartWallTicks = 0;
        return result;
    }

    private bool UpdateStatusLocked(string? nextStatus, out string? publishedStatus)
    {
        if (string.Equals(_activeSessionStatusText, nextStatus, StringComparison.Ordinal))
        {
            publishedStatus = _activeSessionStatusText;
            return false;
        }

        _activeSessionStatusText = nextStatus;
        publishedStatus = nextStatus;
        return true;
    }

    private async Task PersistSessionSafeAsync(CompletedSessionData completed)
    {
        bool saved = false;

        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            saved = await PersistSessionLockedAsync(completed).ConfigureAwait(false);
        }
        catch
        {
            // Isola falhas transitórias de gravação
        }
        finally
        {
            _ioLock.Release();
        }

        if (saved && !_isDisposed)
        {
            HistoryChanged?.Invoke();
        }
    }

    private async Task<bool> PersistSessionLockedAsync(CompletedSessionData completed)
    {
        GameHistoryDocument doc = await EnsureDocumentLoadedLockedAsync(CancellationToken.None).ConfigureAwait(false);

        GameHistoryRecord? game = doc.Games.FirstOrDefault(g =>
            string.Equals(g.GameKey, completed.GameKey, StringComparison.OrdinalIgnoreCase));

        if (game is null)
        {
            game = new GameHistoryRecord
            {
                GameKey = completed.GameKey,
                DisplayName = completed.DisplayName,
                ProcessName = completed.ProcessName,
                LastPlayedUtc = completed.Session.EndedAtUtc
            };
            doc.Games.Add(game);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(completed.DisplayName))
            {
                game.DisplayName = completed.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(completed.ProcessName))
            {
                game.ProcessName = completed.ProcessName;
            }

            if (completed.Session.EndedAtUtc > game.LastPlayedUtc)
            {
                game.LastPlayedUtc = completed.Session.EndedAtUtc;
            }
        }

        game.Sessions.Add(completed.Session);
        game.Sessions.Sort(static (a, b) => b.EndedAtUtc.CompareTo(a.EndedAtUtc));

        await WriteDocumentAtomicLockedAsync(doc).ConfigureAwait(false);
        return true;
    }

    private async Task<GameHistoryDocument> EnsureDocumentLoadedLockedAsync(CancellationToken cancellationToken)
    {
        if (_loadedDocument is not null)
            return _loadedDocument;

        GameHistoryDocument? loaded = null;
        string? sourcePath = null;
        bool recoveredFromTemp = false;

        if (File.Exists(_historyFilePath))
        {
            sourcePath = _historyFilePath;
        }
        else if (File.Exists(_tempHistoryFilePath))
        {
            sourcePath = _tempHistoryFilePath;
            recoveredFromTemp = true;
        }

        if (sourcePath is not null)
        {
            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
                if (bytes.Length > 0)
                {
                    loaded = JsonSerializer.Deserialize<GameHistoryDocument>(bytes, JsonOptions);
                }
            }
            catch
            {
                loaded = null;
            }
        }

        loaded ??= new GameHistoryDocument();
        loaded.Games ??= [];

        foreach (GameHistoryRecord game in loaded.Games)
        {
            game.Sessions ??= [];
            game.Sessions.Sort(static (a, b) => b.EndedAtUtc.CompareTo(a.EndedAtUtc));
        }

        _loadedDocument = loaded;

        if (recoveredFromTemp)
        {
            await WriteDocumentAtomicLockedAsync(loaded).ConfigureAwait(false);
        }

        return loaded;
    }

    private async Task WriteDocumentAtomicLockedAsync(GameHistoryDocument doc)
    {
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(doc, JsonOptions);
        await File.WriteAllBytesAsync(_tempHistoryFilePath, jsonBytes).ConfigureAwait(false);
        File.Move(_tempHistoryFilePath, _historyFilePath, overwrite: true);
    }

    private static GameHistoryDocument CloneDocument(GameHistoryDocument source)
    {
        var clone = new GameHistoryDocument
        {
            SchemaVersion = source.SchemaVersion,
            Games = new List<GameHistoryRecord>(source.Games.Count)
        };

        for (int i = 0; i < source.Games.Count; i++)
        {
            GameHistoryRecord g = source.Games[i];
            var gameClone = new GameHistoryRecord
            {
                GameKey = g.GameKey,
                DisplayName = g.DisplayName,
                ProcessName = g.ProcessName,
                LastPlayedUtc = g.LastPlayedUtc,
                Sessions = new List<GameSessionRecord>(g.Sessions.Count)
            };

            for (int j = 0; j < g.Sessions.Count; j++)
            {
                GameSessionRecord s = g.Sessions[j];
                gameClone.Sessions.Add(new GameSessionRecord
                {
                    Id = s.Id,
                    StartedAtUtc = s.StartedAtUtc,
                    EndedAtUtc = s.EndedAtUtc,
                    DurationSeconds = s.DurationSeconds,
                    AverageFps = s.AverageFps,
                    OnePercentLowFps = s.OnePercentLowFps
                });
            }

            clone.Games.Add(gameClone);
        }

        return clone;
    }

    private bool IsExcludedNonGameProcess(string? processName, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return true;

        if (ExcludedProcessNames.Contains(processName))
            return true;

        if (!string.IsNullOrEmpty(_windowsDirectoryPrefix) &&
            !string.IsNullOrWhiteSpace(executablePath) &&
            executablePath.StartsWith(_windowsDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private GameIdentity ResolveGameIdentity(string processName, string? executablePath)
    {
        string normalizedProcess = processName.Trim();
        string cacheKey = string.IsNullOrWhiteSpace(executablePath)
            ? normalizedProcess
            : executablePath;

        if (_identityCache.TryGetValue(cacheKey, out GameIdentity cached))
            return cached;

        if (_identityCache.Count >= MaxIdentityCacheEntries)
        {
            _identityCache.Clear();
        }

        string? friendlyTitle = null;

        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            try
            {
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                if (IsUsefulGameTitle(versionInfo.ProductName))
                {
                    friendlyTitle = NormalizeTitleWhitespace(versionInfo.ProductName!);
                }
                else if (IsUsefulGameTitle(versionInfo.FileDescription))
                {
                    friendlyTitle = NormalizeTitleWhitespace(versionInfo.FileDescription!);
                }
            }
            catch
            {
                // Ignora caso o binário esteja bloqueado para leitura de recursos PE
            }
        }

        bool usedMetadataTitle = !string.IsNullOrWhiteSpace(friendlyTitle);
        string displayName = usedMetadataTitle
            ? friendlyTitle!
            : BeautifyProcessName(normalizedProcess);

        string gameKey = usedMetadataTitle
            ? displayName.ToLowerInvariant()
            : normalizedProcess.ToLowerInvariant();

        var identity = new GameIdentity(gameKey, displayName, normalizedProcess);
        _identityCache[cacheKey] = identity;
        return identity;
    }

    private static bool IsUsefulGameTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        string trimmed = title.Trim();
        if (trimmed.Length < 2 || trimmed.StartsWith("TODO:", StringComparison.OrdinalIgnoreCase))
            return false;

        for (int i = 0; i < GenericMetadataPlaceholders.Length; i++)
        {
            if (trimmed.Equals(GenericMetadataPlaceholders[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string BeautifyProcessName(string processName)
    {
        string trimmed = processName.Trim();

        for (int i = 0; i < ProcessSuffixesToStrip.Length; i++)
        {
            string suffix = ProcessSuffixesToStrip[i];
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && trimmed.Length > suffix.Length)
            {
                trimmed = trimmed[..^suffix.Length];
                break;
            }
        }

        trimmed = trimmed.Replace('_', ' ').Replace('-', ' ');
        return NormalizeTitleWhitespace(trimmed);
    }

    private static string NormalizeTitleWhitespace(string raw)
    {
        return string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        // Desacopla primeiro fora de locks para evitar inversão de ordem com FpsMonitorService
        _fpsMonitorService.SetFrameSampleSink(null);

        CompletedSessionData? sessionToFlush;
        bool notifyStatusClear = false;

        lock (_stateLock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _isRecordingSamples = false;
            _requiresFpsCapture = false;
            _trackedPid = -1;

            sessionToFlush = _trackingState is TrackingState.Active or TrackingState.Paused
                ? FinalizeCurrentSessionLocked()
                : null;

            ResetToIdleLocked();

            if (_activeSessionStatusText is not null)
            {
                _activeSessionStatusText = null;
                notifyStatusClear = true;
            }
        }

        if (notifyStatusClear)
        {
            ActiveSessionStatusChanged?.Invoke(null);
        }

        if (sessionToFlush is not null)
        {
            try
            {
                if (_ioLock.Wait(DisposeFlushTimeoutMs))
                {
                    try
                    {
                        PersistSessionLockedAsync(sessionToFlush).GetAwaiter().GetResult();
                    }
                    finally
                    {
                        _ioLock.Release();
                    }
                }
            }
            catch
            {
                // Ignora falhas de flush no encerramento para não travar saída da aplicação
            }
        }

        try
        {
            if (_ioLock.Wait(500))
            {
                try
                {
                    _loadedDocument = null;
                }
                finally
                {
                    _ioLock.Release();
                }
            }
            else
            {
                _loadedDocument = null;
            }
        }
        catch
        {
            _loadedDocument = null;
        }

        _identityCache.Clear();
    }
}
