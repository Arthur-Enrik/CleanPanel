using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareOverlayApp.Models;
using HardwareOverlayApp.Services;

namespace HardwareOverlayApp.ViewModels;

/// <summary>
/// ViewModel dedicada da aba "Histórico", carregada exclusivamente sob demanda quando habilitada e acessada.
/// </summary>
public partial class GameHistoryViewModel : ObservableObject, IDisposable
{
    private readonly GameHistoryService _historyService;
    private bool _hasLoaded;
    private bool _isDisposed;

    public ObservableCollection<GameHistoryGroupItemViewModel> Games { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    private bool _hasGames;

    public bool IsEmptyStateVisible => !IsLoading && !HasGames;

    [ObservableProperty]
    private string _totalGamesText = "0";

    [ObservableProperty]
    private string _totalSessionsSummaryText = "0 sessões registradas";

    [ObservableProperty]
    private string _totalPlaytimeText = "0h 00m";

    [ObservableProperty]
    private string _overallAverageFpsText = "—";

    [ObservableProperty]
    private string _overallOnePercentLowText = "Sem sessões";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveSession))]
    private string _activeSessionStatusText = string.Empty;

    public bool HasActiveSession => !string.IsNullOrWhiteSpace(ActiveSessionStatusText);

    public GameHistoryViewModel(GameHistoryService historyService)
    {
        _historyService = historyService;
        _activeSessionStatusText = historyService.ActiveSessionStatusText ?? string.Empty;

        _historyService.HistoryChanged += OnHistoryChanged;
        _historyService.ActiveSessionStatusChanged += OnActiveSessionStatusChanged;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_hasLoaded || _isDisposed)
            return;

        _hasLoaded = true;
        IsLoading = true;
        await ReloadFromServiceAsync().ConfigureAwait(false);
    }

    private void OnHistoryChanged()
    {
        if (!_hasLoaded || _isDisposed)
            return;

        _ = ReloadFromServiceAsync();
    }

    private void OnActiveSessionStatusChanged(string? status)
    {
        if (_isDisposed)
            return;

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (!_isDisposed)
            {
                ActiveSessionStatusText = status ?? string.Empty;
            }
        }, DispatcherPriority.Background);
    }

    private async Task ReloadFromServiceAsync()
    {
        try
        {
            GameHistoryDocument snapshot = await _historyService.GetHistorySnapshotAsync().ConfigureAwait(false);
            if (_isDisposed)
                return;

            var gameEntries = new List<(GameHistoryRecord Record, GameConsolidatedMetrics Metrics)>(snapshot.Games.Count);
            var allSessions = new List<GameSessionRecord>();

            for (int i = 0; i < snapshot.Games.Count; i++)
            {
                GameHistoryRecord game = snapshot.Games[i];
                if (game.Sessions.Count == 0)
                    continue;

                GameConsolidatedMetrics metrics = GameHistoryCalculator.ComputeConsolidated(game.Sessions);
                if (metrics.SessionCount == 0)
                    continue;

                gameEntries.Add((game, metrics));
                allSessions.AddRange(game.Sessions);
            }

            gameEntries.Sort(static (a, b) => b.Metrics.LastPlayedUtc.CompareTo(a.Metrics.LastPlayedUtc));

            GameConsolidatedMetrics overall = GameHistoryCalculator.ComputeConsolidated(allSessions);

            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
                return;

            await dispatcher.InvokeAsync(() =>
            {
                if (_isDisposed)
                    return;

                var expandedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < Games.Count; i++)
                {
                    if (Games[i].IsExpanded)
                    {
                        expandedKeys.Add(Games[i].GameKey);
                    }
                }

                Games.Clear();

                for (int i = 0; i < gameEntries.Count; i++)
                {
                    (GameHistoryRecord record, GameConsolidatedMetrics metrics) = gameEntries[i];
                    var groupVm = new GameHistoryGroupItemViewModel(
                        gameKey: record.GameKey,
                        displayName: record.DisplayName,
                        processName: record.ProcessName,
                        sessionCountText: FormatSessionCount(metrics.SessionCount),
                        lastPlayedText: FormatLastPlayed(metrics.LastPlayedUtc),
                        totalPlaytimeText: FormatConsolidatedPlaytime(metrics.TotalDurationSeconds),
                        averageFpsText: FormatFps(metrics.AverageFps),
                        onePercentLowText: FormatFps(metrics.OnePercentLowFps),
                        parent: this,
                        isExpanded: expandedKeys.Contains(record.GameKey));

                    for (int j = 0; j < record.Sessions.Count; j++)
                    {
                        GameSessionRecord session = record.Sessions[j];
                        groupVm.Sessions.Add(new GameSessionItemViewModel(
                            gameKey: record.GameKey,
                            sessionId: session.Id,
                            dateRangeText: FormatDateRange(session.StartedAtUtc, session.EndedAtUtc),
                            durationText: FormatSessionDuration(session.DurationSeconds),
                            averageFpsText: FormatFps(session.AverageFps),
                            onePercentLowText: FormatFps(session.OnePercentLowFps),
                            parentGame: groupVm));
                    }

                    Games.Add(groupVm);
                }

                int totalGames = gameEntries.Count;
                HasGames = totalGames > 0;
                TotalGamesText = totalGames.ToString(CultureInfo.InvariantCulture);
                TotalSessionsSummaryText = FormatTotalSessionsSummary(overall.SessionCount);
                TotalPlaytimeText = FormatConsolidatedPlaytime(overall.TotalDurationSeconds);
                OverallAverageFpsText = overall.SessionCount > 0 ? FormatFps(overall.AverageFps) : "—";
                OverallOnePercentLowText = overall.SessionCount > 0
                    ? $"1% Low: {FormatFps(overall.OnePercentLowFps)}"
                    : "Sem sessões";
                IsLoading = false;
            }, DispatcherPriority.Background);
        }
        catch
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (!_isDisposed)
                {
                    IsLoading = false;
                }
            }, DispatcherPriority.Background);
        }
    }

    [RelayCommand]
    private async Task DeleteSessionAsync(GameSessionItemViewModel? session)
    {
        if (_isDisposed || session is null)
            return;

        await _historyService.DeleteSessionAsync(session.GameKey, session.SessionId);
    }

    [RelayCommand]
    private async Task DeleteGameAsync(GameHistoryGroupItemViewModel? game)
    {
        if (_isDisposed || game is null)
            return;

        await _historyService.DeleteGameAsync(game.GameKey);
    }

    [RelayCommand]
    private async Task ClearAllHistoryAsync()
    {
        if (_isDisposed)
            return;

        MessageBoxResult confirmation = MessageBox.Show(
            "Deseja remover permanentemente todo o histórico de jogos e sessões registradas?",
            "Limpar Histórico",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation == MessageBoxResult.Yes)
        {
            await _historyService.ClearAllAsync();
        }
    }

    private static string FormatConsolidatedPlaytime(double totalSeconds)
    {
        int safeSeconds = Math.Max(0, (int)Math.Floor(totalSeconds));
        int hours = safeSeconds / 3600;
        int minutes = (safeSeconds % 3600) / 60;
        return string.Create(CultureInfo.InvariantCulture, $"{hours}h {minutes:00}m");
    }

    private static string FormatSessionDuration(double totalSeconds)
    {
        int safeSeconds = Math.Max(0, (int)Math.Floor(totalSeconds));
        if (safeSeconds >= 3600)
        {
            int hours = safeSeconds / 3600;
            int minutes = (safeSeconds % 3600) / 60;
            return string.Create(CultureInfo.InvariantCulture, $"{hours}h {minutes:00}m");
        }

        int mins = safeSeconds / 60;
        int secs = safeSeconds % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{mins}m {secs:00}s");
    }

    private static string FormatFps(double fps) =>
        fps > 0.0
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(fps):0} FPS")
            : "0 FPS";

    private static string FormatSessionCount(int count) =>
        count == 1 ? "1 sessão" : $"{count} sessões";

    private static string FormatTotalSessionsSummary(int totalSessions) =>
        totalSessions == 1 ? "1 sessão registrada" : $"{totalSessions} sessões registradas";

    private static string FormatLastPlayed(DateTime lastPlayedUtc) =>
        lastPlayedUtc == DateTime.MinValue
            ? "—"
            : $"Última sessão: {lastPlayedUtc.ToLocalTime():dd/MM/yyyy HH:mm}";

    private static string FormatDateRange(DateTime startedUtc, DateTime endedUtc)
    {
        DateTime localStart = startedUtc.ToLocalTime();
        DateTime localEnd = endedUtc.ToLocalTime();

        return localStart.Date == localEnd.Date
            ? $"{localStart:dd/MM/yyyy} • {localStart:HH:mm} – {localEnd:HH:mm}"
            : $"{localStart:dd/MM HH:mm} – {localEnd:dd/MM HH:mm}";
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _historyService.HistoryChanged -= OnHistoryChanged;
        _historyService.ActiveSessionStatusChanged -= OnActiveSessionStatusChanged;
        Games.Clear();
    }
}

public partial class GameHistoryGroupItemViewModel : ObservableObject
{
    public string GameKey { get; }
    public string DisplayName { get; }
    public string ProcessName { get; }
    public string SessionCountText { get; }
    public string LastPlayedText { get; }
    public string TotalPlaytimeText { get; }
    public string AverageFpsText { get; }
    public string OnePercentLowText { get; }
    public GameHistoryViewModel Parent { get; }
    public ObservableCollection<GameSessionItemViewModel> Sessions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandButtonText))]
    private bool _isExpanded;

    public string ExpandButtonText => IsExpanded
        ? "Ocultar sessões"
        : $"Ver sessões ({Sessions.Count})";

    public GameHistoryGroupItemViewModel(
        string gameKey,
        string displayName,
        string processName,
        string sessionCountText,
        string lastPlayedText,
        string totalPlaytimeText,
        string averageFpsText,
        string onePercentLowText,
        GameHistoryViewModel parent,
        bool isExpanded = false)
    {
        GameKey = gameKey;
        DisplayName = displayName;
        ProcessName = processName;
        SessionCountText = sessionCountText;
        LastPlayedText = lastPlayedText;
        TotalPlaytimeText = totalPlaytimeText;
        AverageFpsText = averageFpsText;
        OnePercentLowText = onePercentLowText;
        Parent = parent;
        _isExpanded = isExpanded;
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}

public sealed class GameSessionItemViewModel
{
    public string GameKey { get; }
    public string SessionId { get; }
    public string DateRangeText { get; }
    public string DurationText { get; }
    public string AverageFpsText { get; }
    public string OnePercentLowText { get; }
    public GameHistoryGroupItemViewModel ParentGame { get; }

    public GameSessionItemViewModel(
        string gameKey,
        string sessionId,
        string dateRangeText,
        string durationText,
        string averageFpsText,
        string onePercentLowText,
        GameHistoryGroupItemViewModel parentGame)
    {
        GameKey = gameKey;
        SessionId = sessionId;
        DateRangeText = dateRangeText;
        DurationText = durationText;
        AverageFpsText = averageFpsText;
        OnePercentLowText = onePercentLowText;
        ParentGame = parentGame;
    }
}
