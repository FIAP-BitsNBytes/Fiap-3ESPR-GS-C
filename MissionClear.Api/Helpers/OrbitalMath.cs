namespace MissionClear.Api.Helpers;

public static class OrbitalMath
{
    public const double EarthRadiusKm = 6371.0;
    public const double EarthFlattening = 1.0 / 298.257223563;
    public const double EarthEccentricitySq = 2 * EarthFlattening - EarthFlattening * EarthFlattening;

    public static double ToRad(double deg) => deg * Math.PI / 180.0;
    public static double ToDeg(double rad) => rad * 180.0 / Math.PI;

    public static double HaversineKm(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg, double radiusKm)
    {
        var dLat = ToRad(lat2Deg - lat1Deg);
        var dLon = ToRad(lon2Deg - lon1Deg);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1Deg)) * Math.Cos(ToRad(lat2Deg))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return radiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static double Gmst(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC DateTime required.", nameof(utc));

        var jd = 367.0 * utc.Year
            - (int)(7.0 * (utc.Year + (int)((utc.Month + 9.0) / 12.0)) / 4.0)
            + (int)(275.0 * utc.Month / 9.0)
            + utc.Day + 1721013.5
            + utc.TimeOfDay.TotalHours / 24.0;
        var t = (jd - 2451545.0) / 36525.0;
        var gmstDeg = 280.46061837 + 360.98564736629 * (jd - 2451545.0)
                    + 0.000387933 * t * t - t * t * t / 38710000.0;
        return ToRad(gmstDeg % 360.0);
    }

    public static (double LatDeg, double LonDeg, double AltKm) EciToGeodetic(
        double xKm, double yKm, double zKm, double gmstRad)
    {
        var lonRad = Math.Atan2(yKm, xKm) - gmstRad;
        var rKm = Math.Sqrt(xKm * xKm + yKm * yKm);
        var latRad = Math.Atan2(zKm, rKm * (1 - EarthEccentricitySq));

        for (var i = 0; i < 3; i++)
        {
            var sinLat = Math.Sin(latRad);
            var n = EarthRadiusKm / Math.Sqrt(1 - EarthEccentricitySq * sinLat * sinLat);
            latRad = Math.Atan2(zKm + EarthEccentricitySq * n * sinLat, rKm);
        }

        var sinLat2 = Math.Sin(latRad);
        var nFinal = EarthRadiusKm / Math.Sqrt(1 - EarthEccentricitySq * sinLat2 * sinLat2);
        var altKm = rKm / Math.Cos(latRad) - nFinal;

        var lonDeg = ToDeg(lonRad) % 360.0;
        if (lonDeg > 180) lonDeg -= 360;
        if (lonDeg < -180) lonDeg += 360;

        return (ToDeg(latRad), lonDeg, altKm);
    }
}
