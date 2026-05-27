namespace MissionClear.Api.Helpers;

public static class MissionScoring
{
    public const double MaxDeltaVKmS     = 12.0;
    public const double EfficiencyWeight = 50.0;
    public const double SafetyWeight     = 50.0;

    public static (double Efficiency, double Safety, int Total) Compute(double deltaVKmS, double riskScore)
    {
        var efficiency = Math.Max(0.0, 1.0 - deltaVKmS / MaxDeltaVKmS) * EfficiencyWeight;
        var safety     = (1.0 - Math.Clamp(riskScore, 0.0, 1.0)) * SafetyWeight;
        var total      = (int)Math.Clamp(Math.Round(efficiency + safety), 0, 100);
        return (efficiency, safety, total);
    }
}
