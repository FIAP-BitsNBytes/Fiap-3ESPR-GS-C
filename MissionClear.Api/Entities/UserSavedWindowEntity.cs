using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MissionClear.Api.Entities;

[Table("user_saved_windows")]
public sealed class UserSavedWindowEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>Matches SavedWindow.id from mobile — composite key like "ISS_2026-06-01T08:00:00Z".</summary>
    [Column("window_id")]
    [MaxLength(200)]
    public required string WindowId { get; set; }

    [Column("destination")]
    [MaxLength(50)]
    public required string Destination { get; set; }

    /// <summary>Full SavedWindow object serialised as JSON for round-trip fidelity.</summary>
    [Column("window_json", TypeName = "longtext")]
    public required string WindowJson { get; set; }

    [Column("label")]
    [MaxLength(100)]
    public string? Label { get; set; }

    [Column("saved_at")]
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    public UserEntity User { get; set; } = null!;
}
