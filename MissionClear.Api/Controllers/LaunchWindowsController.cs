using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[Route("api/launch-windows")]
public sealed class LaunchWindowsController(
    ILaunchWindowCalculator calculator,
    IOrbitalCache           cache) : BaseApiController
{
    // GET api/launch-windows
    [HttpGet]
    public IActionResult GetAll(
        [FromQuery] string? destination,
        [FromQuery] string? from,
        [FromQuery] string? to)
    {
        ParseAndValidate(destination, from, to, out var dest, out var fromDt, out var toDt);
        var debris  = RequireCache();
        var windows = calculator.Calculate(dest, fromDt, toDt, debris);

        var dtos = windows
            .Select(w => new LaunchWindowDto(
                w.Start.ToString("O"), w.End.ToString("O"),
                w.RiskScore, w.DeltaVKmS, w.DurationHours, w.IsRecommended,
                w.Conjunctions
                    .Select(c => new ConjunctionDto(
                        c.DebrisId, c.DebrisName,
                        c.ClosestApproachKm,
                        c.TimeOfClosestApproach.ToString("O"),
                        c.RiskLevel.ToString().ToLowerInvariant()))
                    .ToList()))
            .ToList();

        return Ok(new
        {
            destination  = dest.DisplayName,
            from         = fromDt.ToString("O"),
            to           = toDt.ToString("O"),
            total_windows = dtos.Count,
            safe_windows  = dtos.Count(w => w.IsRecommended),
            windows       = dtos
        });
    }

    // GET api/launch-windows/best
    [HttpGet("best")]
    public IActionResult GetBest(
        [FromQuery] string? destination,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int     count   = 5,
        [FromQuery] double  maxRisk = 0.8)
    {
        if (count is <= 0 or > 20)
            throw new DomainException("INVALID_PARAMETER", "'count' must be between 1 and 20.", 400);

        if (maxRisk is < 0.0 or > 1.0)
            throw new DomainException("INVALID_PARAMETER", "'max_risk' must be between 0.0 and 1.0.", 400);

        ParseAndValidate(destination, from, to, out var dest, out var fromDt, out var toDt);
        var debris  = RequireCache();
        var windows = calculator.Calculate(dest, fromDt, toDt, debris);

        // Always return top-N by risk. Apply maxRisk filter only when it yields results;
        // fall back to best available so the client is never left with an empty list.
        var filtered = windows.Where(w => w.RiskScore <= maxRisk).ToList();
        var source   = filtered.Count > 0 ? filtered : (IEnumerable<LaunchWindow>)windows;

        var best = source
            .OrderBy(w => w.RiskScore)
            .Take(count)
            .Select((w, i) => new BestWindowDto(
                i + 1,
                w.Start.ToString("O"), w.End.ToString("O"),
                w.RiskScore, w.DeltaVKmS, w.DurationHours,
                w.Conjunctions
                    .Select(c => new ConjunctionDto(
                        c.DebrisId, c.DebrisName,
                        c.ClosestApproachKm,
                        c.TimeOfClosestApproach.ToString("O"),
                        c.RiskLevel.ToString().ToLowerInvariant()))
                    .ToList()))
            .ToList();

        return Ok(new
        {
            destination  = dest.DisplayName,
            from         = fromDt.ToString("O"),
            to           = toDt.ToString("O"),
            best_windows = best
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IReadOnlyList<OrbitalObject> RequireCache()
    {
        if (!cache.IsReady)
            throw new DomainException("CACHE_NOT_READY", "Orbital data is still loading.", 503);
        return cache.GetAll();
    }

    private static void ParseAndValidate(
        string? dest, string? from, string? to,
        out MissionDestination destination,
        out DateTime fromDt, out DateTime toDt)
    {
        if (string.IsNullOrEmpty(dest))
            throw new DomainException("MISSING_PARAMETER", "'destination' is required.", 400);
        if (string.IsNullOrEmpty(from))
            throw new DomainException("MISSING_PARAMETER", "'from' is required.", 400);
        if (string.IsNullOrEmpty(to))
            throw new DomainException("MISSING_PARAMETER", "'to' is required.", 400);

        destination = KnownDestinations.FindById(dest)
            ?? throw new DomainException("INVALID_DESTINATION",
                $"Unknown destination: {dest}", 400);

        if (!DateTime.TryParse(from, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out fromDt))
            throw new DomainException("INVALID_DATE_FORMAT",
                "'from' is not a valid ISO 8601 date.", 400);

        if (!DateTime.TryParse(to, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out toDt))
            throw new DomainException("INVALID_DATE_FORMAT",
                "'to' is not a valid ISO 8601 date.", 400);

        fromDt = fromDt.ToUniversalTime();
        toDt   = toDt.ToUniversalTime();

        if (toDt <= fromDt)
            throw new DomainException("INVALID_DATE_RANGE",
                "'to' must be after 'from'.", 400);

        if ((toDt - fromDt).TotalHours > 48)
            throw new DomainException("TIME_RANGE_EXCEEDED",
                "Range cannot exceed 48 hours.", 400);
    }
}