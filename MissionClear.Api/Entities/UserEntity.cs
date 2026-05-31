using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MissionClear.Api.Entities;

[Table("users")]
public sealed class UserEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("email")]
    [MaxLength(256)]
    public required string Email { get; set; }

    [Column("display_name")]
    [MaxLength(100)]
    public required string DisplayName { get; set; }

    /// <summary>BCrypt hash — NEVER store plaintext password.</summary>
    [Column("password_hash")]
    [MaxLength(256)]
    public required string PasswordHash { get; set; }

    /// <summary>Researcher | Administrator</summary>
    [Column("role")]
    [MaxLength(50)]
    public string Role { get; set; } = "Researcher";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RefreshTokenEntity> RefreshTokens { get; set; } = [];
    public ICollection<MissionEntity> Missions { get; set; } = [];
    public ICollection<UserFavoriteDebrisEntity> FavoriteDebris { get; set; } = [];
    public ICollection<UserSavedWindowEntity> SavedWindows { get; set; } = [];
}
