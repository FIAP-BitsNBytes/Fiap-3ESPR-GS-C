using MissionClear.Api.Dtos.Destination;
using MissionClear.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class DestinationsController : BaseApiController
{
    // GET api/destinations
    [HttpGet]
    public IActionResult Get()
    {
        var dtos = KnownDestinations.All
            .Select(d => new DestinationDto(
                d.Id,
                d.DisplayName,
                d.AltitudeKm,
                d.InclinationDeg,
                d.Description,
                d.DeltaVKmS,
                d.MissionDurationHours,
                d.Icon))
            .ToList();

        return Ok(new DestinationsResponse(dtos));
    }
}