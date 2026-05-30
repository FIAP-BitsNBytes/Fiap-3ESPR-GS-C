using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class MissionSseService(
    ISessionStore sessions,
    IOrbitalCache cache,
    IConjunctionDetector detector) : IMissionSseService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task StreamAsync(string sessionId, HttpResponse response, CancellationToken ct)
    {
        var session = sessions.Get(sessionId);
        if (session == null) return;

        response.Headers.Append("Content-Type", "text/event-stream");
        response.Headers.Append("Cache-Control", "no-cache");
        response.Headers.Append("Connection", "keep-alive");

        var dest = KnownDestinations.FindById(session.Destination);
        if (dest == null) return;

        var debris = cache.GetAll();
        var random = new Random();

        // Simulation loop: 10 steps (0 to 100%)
        for (int i = 0; i <= 100; i += 10)
        {
            if (ct.IsCancellationRequested) break;

            // 1. Broadcast Progress
            await SendEvent(response, "progress", new { percentage = i }, ct);

            // 2. Proximity check at current simulation point
            var simTime = session.DepartureTime.AddSeconds((session.ArrivalTime - session.DepartureTime).TotalSeconds * (i / 100.0));
            var conjunctions = detector.Detect(dest, simTime, debris);

            foreach (var c in conjunctions)
            {
                if (c.RiskLevel >= RiskLevel.High)
                {
                    session.ObstaclesEncountered++;
                    session.Conjunctions.Add(c);
                    
                    await SendEvent(response, "obstacle", new
                    {
                        debris_id                = c.DebrisId,
                        debris_name              = c.DebrisName,
                        closest_approach_km      = c.ClosestApproachKm,
                        time_of_closest_approach = c.TimeOfClosestApproach.ToString("O"),
                        risk_level               = c.RiskLevel.ToString().ToLowerInvariant()
                    }, ct);
                }
            }

            // 3. Update session safety score based on cumulative conjunctions
            session.RiskScore = RiskScoring.ComputeScore(session.Conjunctions.Select(c => c.ClosestApproachKm));
            session.DeltaVKmS = dest.DeltaVKmS;

            await Task.Delay(500, ct); // Pace the simulation
        }

        // Final event
        var (eff, saf, total) = MissionScoring.Compute(session.DeltaVKmS, session.RiskScore);
        await SendEvent(response, "result", new
        {
            status        = "completed",
            mission_score = total,
            risk_score    = Math.Round(session.RiskScore, 4),
            delta_v_km_s  = session.DeltaVKmS
        }, ct);
    }

    private static async Task SendEvent(HttpResponse response, string eventName, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var payload = $"event: {eventName}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await response.Body.WriteAsync(bytes, ct);
        await response.Body.FlushAsync(ct);
    }
}
