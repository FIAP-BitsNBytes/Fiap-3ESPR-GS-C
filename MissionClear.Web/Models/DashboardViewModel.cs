namespace MissionClear.Web.Models;

public sealed class DashboardViewModel
{
    public int TotalTrackedObjects { get; set; }
    public int Debris { get; set; }
    public int Satellites { get; set; }
    public int RocketBodies { get; set; }
    public int ActiveAlerts { get; set; }
    public string? LastUpdated { get; set; }

    // Null quando não autenticado
    public string? UserDisplayName { get; set; }
    public int? UserTotalMissions { get; set; }
    public int? UserBestScore { get; set; }
}
