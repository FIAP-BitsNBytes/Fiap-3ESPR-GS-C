namespace MissionClear.Api.Models;

/// <summary>
/// Janela temporal de lançamento avaliada. RiskScore in [0,1]; menor é melhor.
/// IsRecommended = true quando RiskScore < 0.1.
/// </summary>
public sealed record LaunchWindow(
    DateTime Start,
    DateTime End,
    double RiskScore,
    double DeltaVKmS,
    double DurationHours,
    bool IsRecommended,
    IReadOnlyList<ConjunctionResult> Conjunctions);
