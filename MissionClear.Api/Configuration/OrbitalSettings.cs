namespace MissionClear.Api.Configuration;

public sealed class OrbitalSettings
{
    public const string SectionName = "Orbital";

    public int TleFetchIntervalMinutes { get; init; } = 60;
    public int PropagationIntervalSeconds { get; init; } = 60;
    public int MaxDebrisCount { get; init; } = 30000;
    public int TleMaxAgeDays { get; init; } = 7;
}
