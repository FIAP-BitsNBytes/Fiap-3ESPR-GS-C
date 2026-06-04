using MissionClear.Api.Models;

namespace MissionClear.Api.Helpers;

public static class RiskScoring
{
    public const double CriticalKm  = 20.0;
    public const double HighKm      = 80.0;
    public const double MediumKm    = 150.0;
    public const double SafeKm      = 200.0;
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
            // Linear 1.0 → 0.0 from 0 km to MaxRadiusKm; never exceeds 1.0 per object
            total += Math.Max(0.0, 1.0 - d / MaxRadiusKm);
        }
        return Math.Min(1.0, total);
    }
}
