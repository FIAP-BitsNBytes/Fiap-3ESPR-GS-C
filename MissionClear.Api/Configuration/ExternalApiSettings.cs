namespace MissionClear.Api.Configuration;

public sealed class ExternalApiSettings
{
    public const string SectionName = "ExternalApi";

    /// <summary>
    /// List of CelesTrak GP catalog URLs to fetch sequentially.
    /// Each entry is fetched with a polite delay between requests.
    /// </summary>
    public IReadOnlyList<CelesTrakCatalog> CelesTrakCatalogs { get; init; } =
    [
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=stations&FORMAT=tle",           "stations"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=last-30-days&FORMAT=tle",       "recent"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=fengyun-1c-debris&FORMAT=tle",  "fengyun-debris"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=cosmos-2251-debris&FORMAT=tle", "cosmos-debris"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=iridium-33-debris&FORMAT=tle",  "iridium-debris"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=active&FORMAT=tle",             "active"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=cosmos-1408-debris&FORMAT=tle", "cosmos-1408-debris"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=starlink&FORMAT=tle",           "starlink"),
    ];

    /// <summary>Seconds to wait between consecutive CelesTrak catalog fetches.</summary>
    public int CelesTrakRequestDelaySeconds { get; init; } = 3;

    public string KeepTrackBaseUrl { get; init; } = "https://keeptrack.space/api";
    public string KeepTrackApiKey { get; init; } = string.Empty;
    public int KeepTrackTimeoutSeconds { get; init; } = 5;
}

public sealed record CelesTrakCatalog(string Url, string Label);
