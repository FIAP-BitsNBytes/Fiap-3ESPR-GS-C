using MissionClear.Api.Models;

namespace MissionClear.Api.Services.Interfaces;

public interface ILaunchWindowCalculator
{
    IReadOnlyList<LaunchWindow> Calculate(
        MissionDestination destination,
        DateTime from,
        DateTime to,
        IReadOnlyList<OrbitalObject> debris);
}
