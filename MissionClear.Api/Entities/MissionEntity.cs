using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MissionClear.Api.Entities;

[Table("missions")]
public sealed class MissionEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>ISS | LEO_GENERIC | SSO</summary>
    [Column("destination")]
    [MaxLength(50)]
    public required string Destination { get; set; }

    /// <summary>success | failure | aborted</summary>
    [Column("status")]
    [MaxLength(20)]
    public required string Status { get; set; }

    [Column("mission_score")]
    public int MissionScore { get; set; }

    [Column("risk_score")]
    public double RiskScore { get; set; }

    [Column("delta_v_km_s")]
    public double DeltaVKmS { get; set; }

    [Column("obstacles_encountered")]
    public int ObstaclesEncountered { get; set; }

    [Column("departure_time")]
    public DateTime DepartureTime { get; set; }

    [Column("arrival_time")]
    public DateTime ArrivalTime { get; set; }

    /// <summary>JSON array of obstacle objects — serialized at Service layer.</summary>
    [Column("obstacles_json")]
    public string ObstaclesJson { get; set; } = "[]";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;
}
