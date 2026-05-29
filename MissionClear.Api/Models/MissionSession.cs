namespace MissionClear.Api.Models;

public enum SessionStatus { Active, Completed, Expired }

/// <summary>
/// Sessão de simulação ao vivo (SSE). Mutável: Status muda quando a simulação termina.
/// Mantida em memória pelo SessionStore. Não persiste a menos que o usuário peça save_to_history.
/// TTL padrão: 30 minutos a partir da criação.
/// </summary>
public sealed class MissionSession
{
    public string SessionId { get; init; } = $"sess_{Guid.NewGuid():N}";
    public required string Destination { get; init; }
    public required DateTime DepartureTime { get; init; }
    public required DateTime ArrivalTime { get; init; }
    public DateTime ExpiresAt { get; init; } = DateTime.UtcNow.AddMinutes(30);
    public required Guid UserId { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public double RiskScore { get; set; }
    public double DeltaVKmS { get; set; }
    public int MissionScore { get; set; }
    public int ObstaclesEncountered { get; set; }
    public List<ConjunctionResult> Conjunctions { get; } = [];

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
}
