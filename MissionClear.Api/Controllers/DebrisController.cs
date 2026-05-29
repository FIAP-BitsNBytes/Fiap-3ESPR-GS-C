using MissionClear.Api.Dtos.Orbital;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class DebrisController(IOrbitalCache cache) : BaseApiController
{
    // GET api/debris
    [HttpGet]
    public IActionResult GetAll(
        [FromQuery] double altitudeMinKm = 200,
        [FromQuery] double altitudeMaxKm = 2000,
        [FromQuery] string? type         = null,
        [FromQuery] int    limit         = 500)
    {
        if (!cache.IsReady)
            throw new DomainException("CACHE_NOT_READY", "Orbital data is still loading.", 503);

        var query = cache.GetAll()
            .Where(o => o.AltitudeKm >= altitudeMinKm && o.AltitudeKm <= altitudeMaxKm);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(o => o.Type == type);

        var result = query
            .Take(Math.Min(limit, 2000))
            .Select(o => new DebrisDto(
                o.Id, o.Name, o.Type,
                o.Latitude, o.Longitude,
                o.AltitudeKm, o.VelocityKmS,
                o.Source, o.UpdatedAt.ToString("O")))
            .ToList();

        Response.Headers.CacheControl = "max-age=60";
        return Ok(result);
    }

    // GET api/debris/stats  ← MUST be before {id}
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        if (!cache.IsReady)
            throw new DomainException("CACHE_NOT_READY", "Orbital data is still loading.", 503);

        var all = cache.GetAll();
        int debris = 0, satellite = 0, rocket = 0, low = 0, mid = 0, high = 0;

        foreach (var o in all)
        {
            switch (o.Type)
            {
                case "debris":      debris++;  break;
                case "satellite":   satellite++; break;
                case "rocket_body": rocket++;  break;
            }
            if      (o.AltitudeKm < 500)  low++;
            else if (o.AltitudeKm < 1000) mid++;
            else                          high++;
        }

        return Ok(new DebrisStatsDto(
            TotalTracked: all.Count,
            ByType:       new ByTypeDto(debris, satellite, rocket),
            ByAltitudeBand: new ByAltitudeBandDto(low, mid, high),
            Sources:      new SourcesDto(
                              all.Count(o => o.Source == "celestrak"),
                              all.Count(o => o.Source == "keeptrack")),
            LastUpdated:  (cache.LastPropagation ?? DateTime.UtcNow).ToString("O")));
    }

    // GET api/debris/{id}  ← AFTER stats
    [HttpGet("{id}")]
    public IActionResult GetById(string id)
    {
        if (!cache.IsReady)
            throw new DomainException("CACHE_NOT_READY", "Orbital data is still loading.", 503);

        var obj = cache.GetById(id)
            ?? throw new DomainException("DEBRIS_NOT_FOUND", $"Object {id} not found.", 404);

        TleDto? tle = null;
        if (obj.TleLine1 != null && obj.TleLine2 != null)
            tle = new TleDto(obj.TleEpoch ?? "", obj.TleLine1, obj.TleLine2);

        OrbitParamsDto? orbit = null;
        if (obj.InclinationDeg.HasValue)
            orbit = new OrbitParamsDto(
                obj.InclinationDeg.Value,
                obj.Eccentricity    ?? 0,
                obj.PeriodMinutes   ?? 0,
                obj.ApogeeKm        ?? 0,
                obj.PerigeeKm       ?? 0);

        return Ok(new DebrisDetailDto(
            obj.Id, obj.Name, obj.Type,
            obj.Latitude, obj.Longitude,
            obj.AltitudeKm, obj.VelocityKmS,
            obj.Source, obj.UpdatedAt.ToString("O"),
            tle, orbit));
    }
}