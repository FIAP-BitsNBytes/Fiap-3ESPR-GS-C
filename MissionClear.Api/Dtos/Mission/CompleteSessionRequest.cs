using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Mission;

public sealed record CompleteSessionRequest(
    [Required] string Status,
    bool SaveToHistory = false);
