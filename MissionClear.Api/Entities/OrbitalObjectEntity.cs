using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MissionClear.Api.Entities;

[Table("orbital_objects")]
public sealed class OrbitalObjectEntity
{
    [Key]
    [Column("id")]
    [MaxLength(50)]
    public string Id { get; set; } = string.Empty;

    [Column("name")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Column("type")]
    [MaxLength(50)]
    public string Type { get; set; } = "debris";

    [Column("latitude")]
    public double Latitude { get; set; }

    [Column("longitude")]
    public double Longitude { get; set; }

    [Column("altitude_km")]
    public double AltitudeKm { get; set; }

    [Column("velocity_km_s")]
    public double VelocityKmS { get; set; }

    [Column("source")]
    [MaxLength(50)]
    public string Source { get; set; } = "local_seed";

    [Column("tle_line1")]
    [MaxLength(100)]
    public string? TleLine1 { get; set; }

    [Column("tle_line2")]
    [MaxLength(100)]
    public string? TleLine2 { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}