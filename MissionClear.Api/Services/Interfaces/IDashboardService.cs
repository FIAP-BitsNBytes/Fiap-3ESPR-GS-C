using MissionClear.Api.Dtos.Dashboard;

namespace MissionClear.Api.Services.Interfaces;

public interface IDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync(
        Guid? userId,
        string? displayName,
        CancellationToken ct = default);

    Task<AlertsResponse> GetAlertsAsync(
        int windowHours,
        string minRisk,
        CancellationToken ct = default);
}
