using MissionClear.Api.Models;

namespace MissionClear.Api.Models;

/// <summary>
/// Aproximação detectada entre um objeto orbital e uma trajetória de missão.
/// RiskLevel calculado pelo RiskScoring helper.
/// </summary>
public sealed record ConjunctionResult(
    string DebrisId,
    string DebrisName,
    double ClosestApproachKm,
    DateTime TimeOfClosestApproach,
    RiskLevel RiskLevel);
