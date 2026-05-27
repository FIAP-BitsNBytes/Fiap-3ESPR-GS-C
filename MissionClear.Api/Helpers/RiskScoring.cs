using MissionClear.Api.Models;

namespace MissionClear.Api.Helpers;

public static class RiskScoring
{
    public const double CriticalKm  = 1.0;
    public const double HighKm      = 5.0;
    public const double MediumKm    = 10.0;
    public const double SafeKm      = 25.0;
    public const double MaxRadiusKm = 200.0;

    public static RiskLevel Classify(double km) => km switch
    {
        < CriticalKm => RiskLevel.Critical,
        < HighKm     => RiskLevel.High,
        < MediumKm   => RiskLevel.Medium,
        _            => RiskLevel.Low
    };

    public static double ComputeScore(IEnumerable<double> closestApproachesKm)
    {
        double total = 0.0;
        foreach (var d in closestApproachesKm)
        {
            if (d >= MaxRadiusKm) continue;
            total += Math.Max(0.0, 1.0 - (d - SafeKm) / (MaxRadiusKm - SafeKm));
        }
        return Math.Min(1.0, total);
    }
}
