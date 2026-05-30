using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class LaunchWindowCalculator : ILaunchWindowCalculator
{
    private const int SlotMinutes = 15;

    private readonly IConjunctionDetector _detector;

    // Default constructor used in tests (no DI needed for a pure service)
    public LaunchWindowCalculator() : this(new ConjunctionDetector()) { }

    public LaunchWindowCalculator(IConjunctionDetector detector)
    {
        _detector = detector;
    }

    public IReadOnlyList<LaunchWindow> Calculate(
        MissionDestination destination,
        DateTime from,
        DateTime to,
        IReadOnlyList<OrbitalObject> debris)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(debris);

        var windows = new List<LaunchWindow>();
        var current = from;

        while (current < to)
        {
            var slotEnd     = current.AddMinutes(SlotMinutes);
            var conjunctions = _detector.Detect(destination, current, debris);
            var riskScore    = RiskScoring.ComputeScore(
                conjunctions.Select(c => c.ClosestApproachKm));

            windows.Add(new LaunchWindow(
                Start:         current,
                End:           slotEnd,
                RiskScore:     Math.Round(riskScore, 4),
                DeltaVKmS:     destination.DeltaVKmS,
                DurationHours: destination.MissionDurationHours,
                IsRecommended: riskScore < 0.1,
                Conjunctions:  conjunctions));

            current = slotEnd;
        }

        return windows.AsReadOnly();
    }
}
