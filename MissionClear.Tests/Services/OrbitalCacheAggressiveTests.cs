using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

/// <summary>
/// Adversarial tests for OrbitalCache.
/// Targets: deduplication with real source strings, boundary altitudes, stale age,
/// thread safety, empty updates, and invariant consistency.
///
/// Tests marked BUG expose real defects — fail RED until fixed.
/// </summary>
public sealed class OrbitalCacheAggressiveTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static OrbitalObject Obj(string id, double altKm = 400, string source = "celestrak-stations",
        DateTime? updatedAt = null, string? name = null) =>
        new(id, name ?? $"OBJ-{id}", "debris", 0, 0, altKm, 7.5, source,
            updatedAt ?? DateTime.UtcNow,
            TleLine1: $"1 {id.PadLeft(5, '0')}U stub                    00000-0 0  9999",
            TleLine2: $"2 {id.PadLeft(5, '0')} 51.0000 000.0000 0001000 000.0000 000.0000 15.50000000    0");

    private static OrbitalObject ObjNoTle(string id, double altKm = 400, string source = "celestrak-stations",
        DateTime? updatedAt = null) =>
        new(id, $"OBJ-{id}", "debris", 0, 0, altKm, 7.5, source,
            updatedAt ?? DateTime.UtcNow, null, null);

    // ── BUG-2: CelesTrak-wins dedup with real source strings ─────────────────
    // DataAggregatorService sets source = "celestrak-{label}" (e.g. "celestrak-stations").
    // OrbitalCache.Update() checks Equals(source, "celestrak") — never matches.
    // So KeepTrack can WIN over CelesTrak if it appears first in the list.

    [Fact]
    public void BUG2_CelesTrakWithLabelSource_WinsOverKeepTrack_SameBatch()
    {
        var cache     = new OrbitalCache();
        var celestrak = Obj("42", source: "celestrak-stations", name: "FROM_CELESTRAK");
        var keeptrack = Obj("42", source: "keeptrack",          name: "FROM_KEEPTRACK");

        // KeepTrack listed first — buggy code keeps KeepTrack because it never recognises
        // "celestrak-stations" as a CelesTrak source.
        cache.Update([keeptrack, celestrak]);

        cache.GetById("42")!.Source.Should().StartWith("celestrak",
            "CelesTrak must win over KeepTrack regardless of list order");
        cache.GetById("42")!.Name.Should().Be("FROM_CELESTRAK");
    }

    [Fact]
    public void BUG2_CelesTrakActive_WinsOverKeepTrack_SameBatch()
    {
        var cache     = new OrbitalCache();
        var celestrak = Obj("99", source: "celestrak-active",   name: "FROM_CELESTRAK");
        var keeptrack = Obj("99", source: "keeptrack",          name: "FROM_KEEPTRACK");

        cache.Update([keeptrack, celestrak]);

        cache.GetById("99")!.Source.Should().StartWith("celestrak");
    }

    [Fact]
    public void BUG2_CelesTrakDebris_WinsOverKeepTrack_ReverseOrder()
    {
        var cache     = new OrbitalCache();
        var celestrak = Obj("77", source: "celestrak-cosmos-1408-debris", name: "REAL");
        var keeptrack = Obj("77", source: "keeptrack",                    name: "FAKE");

        // Reverse order: CelesTrak last — with correct logic it should still win
        cache.Update([keeptrack, celestrak]);

        cache.GetById("77")!.Name.Should().Be("REAL");
    }

    // ── Altitude boundary exactness ───────────────────────────────────────────

    [Theory]
    [InlineData(200.0,  true,  "exact LEO minimum must be kept")]
    [InlineData(199.99, false, "below 200 km must be filtered")]
    [InlineData(2000.0, true,  "exact LEO maximum must be kept")]
    [InlineData(2000.01,false, "above 2000 km must be filtered")]
    [InlineData(400.0,  true,  "nominal LEO must be kept")]
    [InlineData(0.0,    false, "zero altitude must be filtered")]
    [InlineData(-100.0, false, "negative altitude must be filtered")]
    public void Update_LeoAltitudeBoundary(double altKm, bool expected, string reason)
    {
        var cache = new OrbitalCache();
        cache.Update([Obj("1", altKm: altKm)]);
        var inCache = cache.GetById("1") is not null;
        inCache.Should().Be(expected, reason);
    }

    // ── Stale age boundary exactness ──────────────────────────────────────────

    // Exact 7-day boundary test is inherently racy (two DateTime.UtcNow calls).
    // Test clear values inside and outside the window instead.
    [Theory]
    [InlineData( 1, true,  "1 day old — fresh, kept")]
    [InlineData( 6, true,  "6 days old — within 7-day window, kept")]
    [InlineData( 8, false, "8 days old — past 7-day cutoff, filtered")]
    [InlineData(30, false, "30 days old — stale, filtered")]
    public void Update_StaleAgeBoundary(int daysOld, bool expectedInCache, string reason)
    {
        var cache     = new OrbitalCache();
        var updatedAt = DateTime.UtcNow.AddDays(-daysOld);
        cache.Update([Obj("1", updatedAt: updatedAt)]);
        (cache.GetById("1") is not null).Should().Be(expectedInCache, reason);
    }

    // ── Empty update ──────────────────────────────────────────────────────────

    [Fact]
    public void Update_EmptyList_ClearsCacheAndSetsTimestamp()
    {
        var cache = new OrbitalCache();
        cache.Update([Obj("1")]);
        cache.Count.Should().Be(1);

        cache.Update([], isFetch: true);

        cache.Count.Should().Be(0, "empty update must clear the cache");
        cache.IsReady.Should().BeFalse();
        cache.LastFetch.Should().NotBeNull("timestamp must still be set for empty fetch");
    }

    [Fact]
    public void Update_EmptyList_GetById_ReturnsNull()
    {
        var cache = new OrbitalCache();
        cache.Update([Obj("1")]);
        cache.Update([]);

        cache.GetById("1").Should().BeNull("object must be gone after empty update");
    }

    // ── Duplicate IDs in same batch ───────────────────────────────────────────

    [Fact]
    public void Update_DuplicateIdSameSource_KeepsOne()
    {
        var cache  = new OrbitalCache();
        var first  = Obj("42", name: "FIRST");
        var second = Obj("42", name: "SECOND");

        cache.Update([first, second]);

        cache.Count.Should().Be(1, "duplicate IDs must be deduplicated");
    }

    [Fact]
    public void Update_DuplicateIdSameSource_KeptObjectIsConsistent()
    {
        var cache = new OrbitalCache();
        cache.Update([Obj("42", name: "A"), Obj("42", name: "B"), Obj("42", name: "C")]);

        var obj = cache.GetById("42")!;
        cache.GetAll().Should().HaveCount(1, "only one entry per ID");
        obj.Should().NotBeNull();
    }

    // ── LastFetch/LastPropagation isFetch flag ────────────────────────────────

    [Fact]
    public void Update_IsFetchTrue_SetsLastFetch_NotPropagation()
    {
        var cache = new OrbitalCache();
        cache.Update([ObjNoTle("1")], isFetch: true);

        cache.LastFetch.Should().NotBeNull();
        cache.LastPropagation.Should().BeNull();
    }

    [Fact]
    public void Update_IsFetchFalse_SetsLastPropagation_NotFetch()
    {
        var cache = new OrbitalCache();
        cache.Update([ObjNoTle("1")], isFetch: false);

        cache.LastPropagation.Should().NotBeNull();
        cache.LastFetch.Should().BeNull();
    }

    // ── GetAll snapshot consistency ───────────────────────────────────────────

    [Fact]
    public void GetAll_ReturnsSameSnapshotEvenAfterUpdate()
    {
        var cache = new OrbitalCache();
        cache.Update([Obj("1"), Obj("2")]);

        var snapshot1 = cache.GetAll();

        // Replace with different data
        cache.Update([Obj("3"), Obj("4"), Obj("5")]);

        // Old snapshot must not be mutated
        snapshot1.Should().HaveCount(2, "old snapshot must not change after new Update()");
        snapshot1.Select(o => o.Id).Should().BeEquivalentTo(["1", "2"]);
    }

    // ── Index consistency: GetAll() and GetById() agree ───────────────────────

    [Fact]
    public void GetAll_AndGetById_AreAlwaysConsistent()
    {
        var cache = new OrbitalCache();
        cache.Update([Obj("1"), Obj("2"), Obj("3")]);

        var all = cache.GetAll();
        foreach (var obj in all)
        {
            cache.GetById(obj.Id).Should().NotBeNull(
                $"GetById({obj.Id}) must return same object that GetAll() returns");
            cache.GetById(obj.Id)!.Id.Should().Be(obj.Id);
        }

        cache.Count.Should().Be(all.Count);
    }

    // ── Multiple sequential updates ───────────────────────────────────────────

    [Fact]
    public void Update_CalledMultipleTimes_AlwaysReplacesNotAppends()
    {
        var cache = new OrbitalCache();

        cache.Update([Obj("1"), Obj("2"), Obj("3")]);
        cache.Count.Should().Be(3);

        cache.Update([Obj("4"), Obj("5")]);
        cache.Count.Should().Be(2, "each Update() replaces the entire cache, not appends");
    }

    // ── Thread safety: reads during writes ───────────────────────────────────

    [Fact]
    public void Update_ConcurrentReadsAndWrites_NeverThrow()
    {
        var cache = new OrbitalCache();
        cache.Update(Enumerable.Range(0, 100).Select(i => Obj(i.ToString())).ToList());

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var writers = Enumerable.Range(0, 5).Select(_ => Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var batch = Enumerable.Range(0, 50)
                    .Select(j => Obj(j.ToString()))
                    .ToList();
                cache.Update(batch);
            }
        }));

        var readers = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var c = cache.Count;
                var r = cache.IsReady;
                var a = cache.GetAll();
                var b = cache.GetById("42");
                var f = cache.LastFetch;
                _ = (c, r, a, b, f); // suppress unused warnings
            }
        }));

        var act = () =>
        {
            cts.CancelAfter(500);
            Task.WhenAll(writers.Concat(readers))
                .GetAwaiter().GetResult();
        };

        act.Should().NotThrow("concurrent reads and writes must never corrupt or throw");
    }

    // ── Large batch ───────────────────────────────────────────────────────────

    [Fact]
    public void Update_LargeBatch_18kObjects_HandledWithinTolerance()
    {
        var cache = new OrbitalCache();
        var large = Enumerable.Range(1, 18_000)
            .Select(i => Obj(i.ToString()))
            .ToList();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        cache.Update(large);
        sw.Stop();

        cache.Count.Should().Be(18_000);
        sw.ElapsedMilliseconds.Should().BeLessThan(2_000,
            "ingesting 18k objects must complete in under 2 seconds");
    }
}
