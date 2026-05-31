using MissionClear.Api.Dtos.Mission;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class MissionSimulationService(
    IConjunctionDetector detector,
    IOrbitalCache cache,
    ISessionStore sessions,
    IMissionHistoryService history) : IMissionSimulationService
{
    // ── SimulateAsync (Static) ────────────────────────────────────────────────

    public async Task<SimulateResponse> SimulateAsync(SimulateRequest request, CancellationToken ct)
    {
        var dest = KnownDestinations.FindById(request.Destination)
            ?? throw new DomainException("INVALID_DESTINATION", $"Destination '{request.Destination}' not supported.", 400);

        var debris = cache.GetAll();
        var conjunctions = detector.Detect(dest, request.DepartureTime, debris);
        
        var riskScore = RiskScoring.ComputeScore(conjunctions.Select(c => c.ClosestApproachKm));
        var (efficiency, safety, total) = MissionScoring.Compute(dest.DeltaVKmS, riskScore);

        var obstacles = conjunctions.Select(c => new ObstacleDto(
            c.DebrisId,
            c.DebrisName,
            c.ClosestApproachKm,
            c.TimeOfClosestApproach.ToString("O"),
            c.RiskLevel.ToString().ToLowerInvariant())).ToList();

        return new SimulateResponse(
            dest.Id,
            request.DepartureTime,
            request.ArrivalTime,
            [], // Trajectory empty in MVP
            obstacles,
            total,
            Math.Round(riskScore, 4),
            dest.DeltaVKmS);
    }

    // ── CreateSessionAsync (SSE Initializer) ──────────────────────────────────

    public Task<SessionResponse> CreateSessionAsync(SessionRequest request, CancellationToken ct)
    {
        var dest = KnownDestinations.FindById(request.Destination)
            ?? throw new DomainException("INVALID_DESTINATION", $"Destination '{request.Destination}' not supported.", 400);

        if (!DateTime.TryParse(request.DepartureTime, out var departure) ||
            !DateTime.TryParse(request.ArrivalTime, out var arrival))
        {
            throw new DomainException("INVALID_DATE_FORMAT", "Dates must be in ISO 8601 format.", 400);
        }

        var session = new MissionSession
        {
            Destination   = dest.Id,
            DepartureTime = departure.ToUniversalTime(),
            ArrivalTime   = arrival.ToUniversalTime(),
            UserId        = Guid.Empty // Will be updated if user is authenticated
        };

        sessions.Set(session);

        return Task.FromResult(new SessionResponse(
            session.SessionId,
            session.Destination,
            session.DepartureTime.ToString("O"),
            session.ArrivalTime.ToString("O"),
            $"/api/mission/session/{session.SessionId}/stream",
            session.ExpiresAt.ToString("O")));
    }

    // ── CompleteSessionAsync (Finalizer) ──────────────────────────────────────

    public async Task<CompleteSessionResponse> CompleteSessionAsync(
        string sessionId,
        CompleteSessionRequest request,
        Guid? userId,
        CancellationToken ct)
    {
        var session = sessions.Get(sessionId)
            ?? throw new DomainException("SESSION_NOT_FOUND", "Simulation session not found or expired.", 404);

        // Calculate final scores based on session state (populated by SSE stream)
        var (efficiency, safety, total) = MissionScoring.Compute(session.DeltaVKmS, session.RiskScore);
        session.MissionScore = total;
        session.Status       = request.Status.ToLowerInvariant() switch {
            "success" => SessionStatus.Completed,
            "aborted" => SessionStatus.Completed,
            _         => SessionStatus.Completed
        };

        string? msnId = null;
        if (request.SaveToHistory && userId.HasValue)
        {
            // Map ConjunctionResult list to generic object list for SaveMissionAsync
            var obstaclesData = session.Conjunctions.Select(c => new {
                debris_id                 = c.DebrisId,
                debris_name               = c.DebrisName,
                closest_approach_km       = c.ClosestApproachKm,
                time_of_closest_approach  = c.TimeOfClosestApproach.ToString("O"),
                risk_level                = c.RiskLevel.ToString().ToLowerInvariant()
            }).ToList();

            var summary = await history.SaveMissionAsync(
                userId.Value,
                session.SessionId,
                request.Status,
                session.RiskScore,
                session.DeltaVKmS,
                total,
                session.ObstaclesEncountered,
                session.DepartureTime,
                session.ArrivalTime,
                session.Destination,
                obstaclesData,
                ct);
            
            msnId = summary.Id;
        }

        sessions.Remove(sessionId);

        var durationSec = (session.ArrivalTime - session.DepartureTime).TotalSeconds;

        return new CompleteSessionResponse(
            session.SessionId,
            request.Status,
            total,
            session.RiskScore,
            session.DeltaVKmS,
            session.ObstaclesEncountered,
            durationSec,
            request.SaveToHistory && userId.HasValue,
            msnId);
    }
}
