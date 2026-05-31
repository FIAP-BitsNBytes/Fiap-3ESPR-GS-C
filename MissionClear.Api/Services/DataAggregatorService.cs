using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Data;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

/// <summary>
/// Fetches TLE data from all configured CelesTrak catalogs using FORMAT=tle (3-line text).
/// Uses ETag-based conditional GET to skip unchanged catalogs.
/// Adds a polite delay between fetches to respect CelesTrak rate limits.
/// Falls back to local database seed when CelesTrak is unreachable.
/// </summary>
public sealed class DataAggregatorService : IDataAggregatorService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IOrbitalCache _cache;
    private readonly ExternalApiSettings _settings;
    private readonly ILogger<DataAggregatorService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly Dictionary<string, string> _etags = new(StringComparer.Ordinal);

    internal List<IReadOnlyList<OrbitalObject>>? _capturedUpdates;

    public DataAggregatorService(
        IHttpClientFactory httpFactory,
        IOrbitalCache cache,
        IOptions<ExternalApiSettings> settings,
        ILogger<DataAggregatorService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _httpFactory = httpFactory;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task FetchAndMergeAsync(CancellationToken ct = default)
    {
        var allObjects = new Dictionary<string, OrbitalObject>(StringComparer.Ordinal);
        var anySucceeded = false;
        var isFirst = true;
        var networkBlocked = false;

        foreach (var catalog in _settings.CelesTrakCatalogs)
        {
            if (networkBlocked)
            {
                _logger.LogDebug(
                    "CelesTrak [{Label}]: skipping — network unreachable", catalog.Label);
                continue;
            }

            if (!isFirst && _settings.CelesTrakRequestDelaySeconds > 0)
            {
                _logger.LogDebug(
                    "DataAggregator: waiting {Delay}s before next CelesTrak request",
                    _settings.CelesTrakRequestDelaySeconds);
                await Task.Delay(
                    TimeSpan.FromSeconds(_settings.CelesTrakRequestDelaySeconds), ct);
            }
            isFirst = false;

            try
            {
                var (objects, fromCache) = await FetchCelesTrakTleAsync(catalog, ct);
                if (fromCache)
                    _logger.LogInformation(
                        "CelesTrak [{Label}]: 304 Not Modified — reusing {Count} cached objects",
                        catalog.Label, objects.Count);
                else
                    _logger.LogInformation(
                        "CelesTrak [{Label}]: fetched {Count} TLE records",
                        catalog.Label, objects.Count);

                foreach (var obj in objects)
                    allObjects[obj.Id] = obj;

                anySucceeded = true;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                _logger.LogWarning(ex,
                    "CelesTrak [{Label}] fetch failed — skipping catalog", catalog.Label);

                if (IsNetworkUnreachable(ex))
                {
                    networkBlocked = true;
                    _logger.LogWarning(
                        "CelesTrak host unreachable — skipping remaining catalogs and using fallback");
                }
            }
        }

        if (!anySucceeded)
        {
            _logger.LogWarning("All CelesTrak catalogs failed. Falling back to database seed.");
            var seed = await FetchFromDatabaseAsync(ct);
            foreach (var obj in seed)
                allObjects[obj.Id] = obj;
            _logger.LogInformation("Database fallback: loaded {Count} records", allObjects.Count);

            if (allObjects.Count == 0)
            {
                _logger.LogWarning("Database empty — loading embedded TLE seed for offline operation");
                var embedded = LoadEmbeddedSeed();
                foreach (var obj in embedded)
                    allObjects[obj.Id] = obj;
                _logger.LogInformation("Embedded TLE seed: {Count} objects loaded", allObjects.Count);
            }
        }

        var keeptrack = await TryFetchKeepTrackAsync(ct);
        if (keeptrack.Count > 0)
        {
            foreach (var obj in keeptrack)
                allObjects.TryAdd(obj.Id, obj);
            _logger.LogInformation(
                "KeepTrack: merged {Count} records (gaps only)", keeptrack.Count);
        }

        var result = allObjects.Values.ToList().AsReadOnly();
        _cache.Update(result, isFetch: true);
        _capturedUpdates?.Add(result);

        _logger.LogInformation(
            "OrbitalCache updated: {Total} objects from all sources", result.Count);
    }

    // ── CelesTrak TLE fetch ───────────────────────────────────────────────────

    private async Task<(IReadOnlyList<OrbitalObject> Objects, bool FromCache)>
        FetchCelesTrakTleAsync(CelesTrakCatalog catalog, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("celestrak");
        using var request = new HttpRequestMessage(HttpMethod.Get, catalog.Url);

        if (_etags.TryGetValue(catalog.Url, out var storedEtag))
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{storedEtag}\""));

        _logger.LogDebug("CelesTrak [{Label}]: GET {Url}", catalog.Label, catalog.Url);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout fired — not a genuine cancellation. Re-throw as
            // HttpRequestException so the outer per-catalog catch can skip it.
            throw new HttpRequestException(
                $"CelesTrak [{catalog.Label}]: request timed out", ex);
        }

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            var cached = _cache.GetAll()
                .Where(o => o.Source == $"celestrak-{catalog.Label}")
                .ToList()
                .AsReadOnly();
            return (cached, true);
        }

        response.EnsureSuccessStatusCode();

        var etag = response.Headers.ETag?.Tag?.Trim('"');
        if (!string.IsNullOrEmpty(etag))
            _etags[catalog.Url] = etag;

        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body)) return ([], false);

        var objects = ParseTleText(body, $"celestrak-{catalog.Label}");
        return (objects, false);
    }

    // ── TLE text parser ───────────────────────────────────────────────────────

    /// <summary>
    /// Parses CelesTrak 3-line TLE text format:
    ///   Line 0: object name
    ///   Line 1: "1 NNNNN..."
    ///   Line 2: "2 NNNNN..."
    /// Also handles 2-line format (no name line).
    /// </summary>
    private IReadOnlyList<OrbitalObject> ParseTleText(string body, string source)
    {
        var lines = body
            .Split('\n', StringSplitOptions.None)
            .Select(l => l.TrimEnd('\r').Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        var objects = new List<OrbitalObject>();
        var now = DateTime.UtcNow;
        int i = 0;

        while (i < lines.Length)
        {
            string name, tle1, tle2;

            if (i + 2 < lines.Length
                && !lines[i].StartsWith("1 ") && !lines[i].StartsWith("2 ")
                && lines[i + 1].StartsWith("1 ")
                && lines[i + 2].StartsWith("2 "))
            {
                name = lines[i];
                tle1 = lines[i + 1];
                tle2 = lines[i + 2];
                i += 3;
            }
            else if (i + 1 < lines.Length
                && lines[i].StartsWith("1 ")
                && lines[i + 1].StartsWith("2 "))
            {
                name = string.Empty;
                tle1 = lines[i];
                tle2 = lines[i + 1];
                i += 2;
            }
            else
            {
                i++;
                continue;
            }

            if (tle1.Length < 8 || tle2.Length < 8) continue;

            var noradId = tle1.Length >= 7 ? tle1[2..7].Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(noradId)) continue;

            var displayName = string.IsNullOrWhiteSpace(name)
                ? $"OBJECT-{noradId}"
                : name.Trim();

            var orbit = ParseTleOrbitParams(tle1, tle2);

            objects.Add(new OrbitalObject(
                Id:             noradId,
                Name:           displayName,
                Type:           ClassifyType(displayName, string.Empty),
                Latitude:       0.0,
                Longitude:      0.0,
                AltitudeKm:     orbit.MeanAltitudeKm,
                VelocityKmS:    orbit.VelocityKmS,
                Source:         source,
                UpdatedAt:      now,
                TleLine1:       tle1,
                TleLine2:       tle2,
                TleEpoch:       orbit.Epoch,
                InclinationDeg: orbit.InclinationDeg,
                Eccentricity:   orbit.Eccentricity,
                PeriodMinutes:  orbit.PeriodMinutes,
                ApogeeKm:       orbit.ApogeeKm,
                PerigeeKm:      orbit.PerigeeKm));
        }

        return objects;
    }

    // ── Network diagnostics ───────────────────────────────────────────────────

    private static bool IsNetworkUnreachable(Exception ex)
    {
        if (ex is HttpRequestException { InnerException: System.Net.Sockets.SocketException })
            return true;
        if (ex is HttpRequestException { InnerException: TaskCanceledException { InnerException: TimeoutException } })
            return true;
        if (ex is HttpRequestException httpEx &&
            (httpEx.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
             httpEx.Message.Contains("TimedOut", StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }

    // ── Embedded TLE seed (offline fallback) ─────────────────────────────────

    private IReadOnlyList<OrbitalObject> LoadEmbeddedSeed()
    {
        const string resourceName = "MissionClear.Api.Data.Seeds.debris_seed.tle";
        var asm = typeof(DataAggregatorService).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            _logger.LogError("Embedded seed resource '{Resource}' not found in assembly", resourceName);
            return [];
        }
        using var reader = new StreamReader(stream);
        return ParseTleText(reader.ReadToEnd(), "celestrak-debris");
    }

    // ── Database fallback ─────────────────────────────────────────────────────

    private async Task<IReadOnlyList<OrbitalObject>> FetchFromDatabaseAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entities = await db.OrbitalObjects.AsNoTracking().ToListAsync(ct);
        return entities.Select(e => new OrbitalObject(
            e.Id, e.Name, e.Type, e.Latitude, e.Longitude, e.AltitudeKm, e.VelocityKmS,
            e.Source, e.UpdatedAt, e.TleLine1, e.TleLine2)).ToList();
    }

    // ── KeepTrack ─────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<OrbitalObject>> TryFetchKeepTrackAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.KeepTrackBaseUrl)
            || string.IsNullOrWhiteSpace(_settings.KeepTrackApiKey))
        {
            _logger.LogDebug("KeepTrack: not configured — skipping");
            return [];
        }

        try
        {
            var client = _httpFactory.CreateClient("keeptrack");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_settings.KeepTrackTimeoutSeconds));

            var url = $"{_settings.KeepTrackBaseUrl.TrimEnd('/')}/tle?apiKey={_settings.KeepTrackApiKey}";
            using var response = await client.GetAsync(url, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("KeepTrack returned {Status}", response.StatusCode);
                return [];
            }

            var body = await response.Content.ReadAsStringAsync(cts.Token);
            return ParseTleText(body, "keeptrack");
        }
        catch (Exception ex) when (ex is not OperationCanceledException { CancellationToken.IsCancellationRequested: true })
        {
            _logger.LogWarning(ex, "KeepTrack fetch failed — continuing without it");
            return [];
        }
    }

    // ── TLE orbital parameter parser ──────────────────────────────────────────

    private sealed record TleOrbitParams(
        string Epoch,
        double InclinationDeg,
        double Eccentricity,
        double PeriodMinutes,
        double ApogeeKm,
        double PerigeeKm,
        double MeanAltitudeKm,
        double VelocityKmS);

    private static TleOrbitParams ParseTleOrbitParams(string tle1, string tle2)
    {
        const double Mu    = 398600.4418; // km³/s²
        const double Re    = 6371.0;      // km
        const double TwoPi = 2.0 * Math.PI;

        var epoch = tle1.Length >= 32 ? tle1[18..32].Trim() : string.Empty;

        double inclDeg = 0, ecc = 0, meanMotionRevDay = 0;

        if (tle2.Length >= 16)
            double.TryParse(
                tle2[8..16].Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out inclDeg);

        if (tle2.Length >= 33)
        {
            var eccStr = "0." + tle2[26..33].Trim();
            double.TryParse(
                eccStr,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out ecc);
        }

        if (tle2.Length >= 63)
            double.TryParse(
                tle2[52..63].Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out meanMotionRevDay);

        if (meanMotionRevDay <= 0)
            return new TleOrbitParams(epoch, inclDeg, ecc, 0, 0, 0, 400.0, 7.5);

        var periodMin = 1440.0 / meanMotionRevDay;
        var nRadS     = meanMotionRevDay * TwoPi / 86400.0;
        var a         = Math.Pow(Mu / (nRadS * nRadS), 1.0 / 3.0);
        var apogeeKm  = a * (1.0 + ecc) - Re;
        var perigeeKm = a * (1.0 - ecc) - Re;
        var meanAlt   = Math.Clamp((apogeeKm + perigeeKm) / 2.0, 200.0, 2000.0);
        var velocity  = Math.Sqrt(Mu / a);

        return new TleOrbitParams(
            epoch, inclDeg, ecc, periodMin,
            Math.Round(apogeeKm,  2),
            Math.Round(perigeeKm, 2),
            Math.Round(meanAlt,   2),
            Math.Round(velocity,  4));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string ClassifyType(string name, string objectType)
    {
        var upper = name.ToUpperInvariant();
        if (upper.Contains("DEB") || upper.Contains("DEBRIS")) return "debris";
        if (upper.Contains("R/B") || upper.Contains("ROCKET")) return "rocket_body";
        return "satellite";
    }
}
