using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Mission;

public sealed record SimulateRequest(
    [Required] string Destination,
    DateTime DepartureTime,
    DateTime ArrivalTime);
