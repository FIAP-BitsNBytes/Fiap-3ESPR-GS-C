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

        var dest = KnownDestinations.FindById(session.Destination);
        if (dest == null) return;

        var debris    = cache.GetAll();
        var debrisMap = debris.ToDictionary(d => d.Id);
        var alerted   = new HashSet<string>();

        // Simulation always runs to completion (~2.5 min, 51 ticks × 3 s) regardless of client disconnect.
        // Task.Delay does NOT pass ct — a mid-loop disconnect must not abort the loop.
        // SendEvent is wrapped so write failures don't stop the loop either.
        for (int i = 0; i <= 100; i += 2)
        {
            // ── heartbeat ────────────────────────────────────────────────────
            await TrySendEvent(response, "heartbeat",
                new { timestamp = DateTime.UtcNow.ToString("O") }, ct);

            // ── detect conjunctions at this trajectory point ──────────────
            var elapsed      = (session.ArrivalTime - session.DepartureTime).TotalSeconds * (i / 100.0);
            var simTime      = session.DepartureTime.AddSeconds(elapsed);
            var conjunctions = detector.Detect(dest, simTime, debris);

            // ── debris_update ─────────────────────────────────────────────
            var objects = conjunctions.Select(c =>
            {
                debrisMap.TryGetValue(c.DebrisId, out var obj);
                return new
                {
                    id                          = c.DebrisId,
                    name                        = c.DebrisName,
                    latitude                    = obj?.Latitude  ?? 0.0,
                    longitude                   = obj?.Longitude ?? 0.0,
                    altitude_km                 = obj?.AltitudeKm  ?? 0.0,
                    velocity_km_s               = obj?.VelocityKmS ?? 0.0,
                    distance_from_trajectory_km = c.ClosestApproachKm
                };
            }).ToList();

            await TrySendEvent(response, "debris_update",
                new { timestamp = DateTime.UtcNow.ToString("O"), objects }, ct);

            // ── conjunction_alert — high-risk, once per debris id ─────────
            foreach (var c in conjunctions.Where(c => c.RiskLevel >= RiskLevel.High))
            {
                if (!alerted.Add(c.DebrisId)) continue;

                session.ObstaclesEncountered++;
                session.Conjunctions.Add(c);

                var secondsUntil = Math.Max(0, (int)(c.TimeOfClosestApproach - simTime).TotalSeconds);

                await TrySendEvent(response, "conjunction_alert", new
                {
                    debris_id                 = c.DebrisId,
                    debris_name               = c.DebrisName,
                    closest_approach_km       = c.ClosestApproachKm,
                    time_of_closest_approach  = c.TimeOfClosestApproach.ToString("O"),
                    risk_level                = c.RiskLevel.ToString().ToLowerInvariant(),
                    seconds_until_conjunction = secondsUntil
                }, ct);
            }

            // update running scores
            session.RiskScore = RiskScoring.ComputeScore(
                session.Conjunctions.Select(c => c.ClosestApproachKm));
            session.DeltaVKmS = dest.DeltaVKmS;

            // Delay without ct so a client disconnect doesn't abort the loop
            await Task.Delay(3_000);
        }

        // ── session_complete — always fires ───────────────────────────────
        var (_, _, total) = MissionScoring.Compute(session.DeltaVKmS, session.RiskScore);
        session.MissionScore = total;

        var finalStatus = session.RiskScore >= 0.7 ? "failure" : "success";

        await TrySendEvent(response, "session_complete", new
        {
            status                = finalStatus,
            mission_score         = total,
            risk_score            = Math.Round(session.RiskScore, 4),
            delta_v_km_s          = session.DeltaVKmS,
            obstacles_encountered = session.ObstaclesEncountered
        }, ct);
    }

    // SendEvent that swallows write errors (client may have disconnected).
    // The session state is already updated in-memory; the event is best-effort.
    private static async Task TrySendEvent(
        HttpResponse response,
        string eventName,
        object data,
        CancellationToken ct)
    {
        try
        {
            var json    = JsonSerializer.Serialize(data, JsonOptions);
            var payload = $"event: {eventName}\ndata: {json}\n\n";
            var bytes   = Encoding.UTF8.GetBytes(payload);
            await response.Body.WriteAsync(bytes, ct);
            await response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }
}
