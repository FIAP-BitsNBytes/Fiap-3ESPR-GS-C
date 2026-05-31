using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MissionClear.Api.Entities;

[Table("user_favorite_debris")]
public sealed class UserFavoriteDebrisEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("debris_id")]
    [MaxLength(50)]
    public required string DebrisId { get; set; }

    [Column("saved_at")]
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    public UserEntity User { get; set; } = null!;
}
