namespace HardwareOverlayApp.Models;

/// <summary>
/// Documento JSON raiz do histórico de performance de jogos (persistido separadamente de settings.json).
/// </summary>
public sealed class GameHistoryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<GameHistoryRecord> Games { get; set; } = [];
}

/// <summary>
/// Registro agrupado por jogo contendo suas sessões registradas.
/// </summary>
public sealed class GameHistoryRecord
{
    public string GameKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public DateTime LastPlayedUtc { get; set; }
    public List<GameSessionRecord> Sessions { get; set; } = [];
}

/// <summary>
/// Métricas consolidadas de uma sessão individual com duração efetiva superior a 5 minutos.
/// </summary>
public sealed class GameSessionRecord
{
    public string Id { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public double DurationSeconds { get; set; }
    public double AverageFps { get; set; }
    public double OnePercentLowFps { get; set; }
}

/// <summary>
/// Resumo consolidado calculado para exibição agregada por jogo ou visão global.
/// </summary>
public readonly record struct GameConsolidatedMetrics(
    int SessionCount,
    double TotalDurationSeconds,
    double AverageFps,
    double OnePercentLowFps,
    DateTime LastPlayedUtc);

/// <summary>
/// Cálculos matemáticos ponderados por duração efetiva (média harmônica de FPS / frametime).
/// </summary>
public static class GameHistoryCalculator
{
    public static GameConsolidatedMetrics ComputeConsolidated(IReadOnlyList<GameSessionRecord> sessions)
    {
        if (sessions.Count == 0)
            return new GameConsolidatedMetrics(0, 0.0, 0.0, 0.0, DateTime.MinValue);

        int count = 0;
        double totalDuration = 0.0;
        double avgDenominator = 0.0;
        double lowDenominator = 0.0;
        double lowWeightedDuration = 0.0;
        DateTime lastPlayedUtc = DateTime.MinValue;

        for (int i = 0; i < sessions.Count; i++)
        {
            GameSessionRecord s = sessions[i];
            if (s.DurationSeconds <= 0.0)
                continue;

            count++;
            totalDuration += s.DurationSeconds;

            if (s.AverageFps > 0.0)
            {
                avgDenominator += s.DurationSeconds / s.AverageFps;
            }

            if (s.OnePercentLowFps > 0.0)
            {
                lowDenominator += s.DurationSeconds / s.OnePercentLowFps;
                lowWeightedDuration += s.DurationSeconds;
            }

            if (s.EndedAtUtc > lastPlayedUtc)
            {
                lastPlayedUtc = s.EndedAtUtc;
            }
        }

        if (totalDuration <= 0.0 || count == 0)
            return new GameConsolidatedMetrics(0, 0.0, 0.0, 0.0, DateTime.MinValue);

        double averageFps = avgDenominator > 0.0 ? totalDuration / avgDenominator : 0.0;
        double onePercentLowFps = lowDenominator > 0.0 ? lowWeightedDuration / lowDenominator : 0.0;

        if (averageFps > 0.0 && onePercentLowFps > averageFps)
        {
            onePercentLowFps = averageFps;
        }

        return new GameConsolidatedMetrics(
            count,
            totalDuration,
            Math.Round(averageFps, 1),
            Math.Round(onePercentLowFps, 1),
            lastPlayedUtc);
    }
}
