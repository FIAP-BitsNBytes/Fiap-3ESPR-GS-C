using MissionClear.Api.Models;

namespace MissionClear.Api.Services.Interfaces;

public interface IConjunctionDetector
{
    IReadOnlyList<ConjunctionResult> Detect(
        MissionDestination destination,
        DateTime at,
        IReadOnlyList<OrbitalObject> debris);
}
