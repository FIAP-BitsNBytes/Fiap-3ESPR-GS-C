namespace MissionClear.Web.Models;

public sealed class DashboardViewModel
{
    // Summary
    public int TotalTrackedObjects { get; set; }
    public int Debris { get; set; }
    public int Satellites { get; set; }
    public int RocketBodies { get; set; }
    public int ActiveAlerts { get; set; }
    public string? LastUpdated { get; set; }
    public int LowAlt { get; set; }
    public int MidAlt { get; set; }
    public int HighAlt { get; set; }

    // User
    public string? UserDisplayName { get; set; }
    public int? UserTotalMissions { get; set; }
    public int? UserBestScore { get; set; }

    // Orbital detail
    public List<SourceCount> BySource { get; set; } = [];
    public List<InclinationBin> InclinationBins { get; set; } = [];
    public List<InclinationAltitudeCell> InclinationGrid { get; set; } = [];
    public int TotalWithTle { get; set; }
    public int TotalWithoutTle { get; set; }
}

public sealed class SourceCount { public string Source { get; set; } = ""; public int Count { get; set; } }
public sealed class InclinationBin { public string Band { get; set; } = ""; public int Count { get; set; } }
public sealed class InclinationAltitudeCell { public string InclinationBand { get; set; } = ""; public string AltitudeBand { get; set; } = ""; public int Count { get; set; } }
