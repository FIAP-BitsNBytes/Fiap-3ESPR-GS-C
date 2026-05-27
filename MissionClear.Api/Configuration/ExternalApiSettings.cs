namespace MissionClear.Api.Configuration;

public sealed class ExternalApiSettings
{
    public const string SectionName = "ExternalApi";

    public string CelesTrakBaseUrl { get; init; } =
        "https://celestrak.org/NORAD/elements/gp.php?GROUP=debris&FORMAT=json";
    public string KeepTrackBaseUrl { get; init; } = "https://keeptrack.space/api";
    public string KeepTrackApiKey { get; init; } = string.Empty;
    public int KeepTrackTimeoutSeconds { get; init; } = 5;
}
