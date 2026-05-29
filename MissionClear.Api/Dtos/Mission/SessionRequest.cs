using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Mission;

public sealed record SessionRequest(
    [Required] string Destination,
    [Required] string DepartureTime,
    [Required] string ArrivalTime);
