using MissionClear.Api.Dtos.Mission;

namespace MissionClear.Api.Services.Interfaces;

public interface IMissionSimulationService
{
    Task<SimulateResponse> SimulateAsync(SimulateRequest request, CancellationToken ct = default);
    Task<SessionResponse> CreateSessionAsync(SessionRequest request, CancellationToken ct = default);
    Task<CompleteSessionResponse> CompleteSessionAsync(
        string sessionId,
        CompleteSessionRequest request,
        Guid? userId,
        CancellationToken ct = default);
}
