using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Dtos.History;

namespace MissionClear.Api.Services.Interfaces;

public interface IMissionHistoryService
{
    /// <summary>
    /// Lista missões do usuário com paginação, filtros e sort.
    /// Mapeado para GET /api/missions
    /// </summary>
    Task<PagedResponse<MissionSummaryDto>> GetMissionsAsync(
        Guid userId,
        int page,
        int limit,
        string? status,
        string? destination,
        string sort,
        CancellationToken ct = default);

    /// <summary>
    /// Detalhe de uma missão. Lança 404 se não encontrada, 403 se não pertence ao userId.
    /// Mapeado para GET /api/missions/{id}
    /// </summary>
    Task<MissionDetailResponse> GetMissionDetailAsync(
        Guid id,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Detalhe de qualquer missão, sem verificação de ownership. Uso exclusivo admin.
    /// </summary>
    Task<MissionDetailResponse> GetMissionDetailAdminAsync(
        Guid id,
        CancellationToken ct = default);

    /// <summary>
    /// Estatísticas agregadas do usuário.
    /// Mapeado para GET /api/missions/stats
    /// </summary>
    Task<MissionStatsResponse> GetStatsAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Remove missão. Lança 404 se não encontrada, 403 se não pertence ao userId.
    /// Mapeado para DELETE /api/missions/{id}
    /// </summary>
    Task DeleteMissionAsync(
        Guid id,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Persiste missão finalizada e retorna o DTO de sumário.
    /// Chamado internamente por MissionSimulationService ao completar sessão com save_to_history = true.
    /// </summary>
    Task<MissionSummaryDto> SaveMissionAsync(
        Guid userId,
        string sessionId,
        string status,
        double riskScore,
        double deltaV,
        int score,
        int obstacles,
        DateTime departure,
        DateTime arrival,
        string destination,
        IReadOnlyList<object> obstaclesData,
        CancellationToken ct = default);
}
