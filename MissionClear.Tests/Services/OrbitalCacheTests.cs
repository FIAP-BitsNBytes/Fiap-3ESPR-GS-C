using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class OrbitalCacheTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static OrbitalObject TleObj(string id, double altKm = 400, string source = "celestrak",
        DateTime? updatedAt = null) =>
        new(id, $"OBJ-{id}", "debris", 0, 0, altKm, 7.5, source,
            updatedAt ?? DateTime.UtcNow,
            TleLine1: $"1 {id}U stub",
            TleLine2: $"2 {id} stub");

    private static OrbitalObject PropagatedObj(string id, double altKm = 400, string source = "celestrak",
        DateTime? updatedAt = null) =>
        new(id, $"OBJ-{id}", "debris", 0, 0, altKm, 7.5, source,
            updatedAt ?? DateTime.UtcNow,
            TleLine1: null,
            TleLine2: null);

    // ── IsReady ───────────────────────────────────────────────────────────────

    [Fact]
    public void IsReady_IsFalse_BeforeFirstUpdate()
    {
        var cache = new OrbitalCache();
        cache.IsReady.Should().BeFalse();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void IsReady_IsTrue_AfterFirstUpdate()
    {
        var cache = new OrbitalCache();
        cache.Update([TleObj("1")]);
        cache.IsReady.Should().BeTrue();
    }

    // ── LEO filter ────────────────────────────────────────────────────────────

    [Fact]
    public void Update_FiltersOutNonLEO_Altitudes()
    {
        var cache = new OrbitalCache();
        cache.Update([
            TleObj("low",  altKm: 100),   // below LEO — filtered
            TleObj("ok",   altKm: 400),   // in LEO — kept
            TleObj("high", altKm: 3000),  // above LEO — filtered
        ]);

        cache.Count.Should().Be(1);
        cache.GetById("ok").Should().NotBeNull();
        cache.GetById("low").Should().BeNull();
        cache.GetById("high").Should().BeNull();
    }

    // ── Stale filter ──────────────────────────────────────────────────────────

    [Fact]
    public void Update_FiltersStaleObjects_OlderThan7Days()
    {
        var cache = new OrbitalCache();
        var stale = TleObj("stale", updatedAt: DateTime.UtcNow.AddDays(-8));
        var fresh = TleObj("fresh");
        cache.Update([stale, fresh]);

        cache.Count.Should().Be(1);
        cache.GetById("stale").Should().BeNull();
        cache.GetById("fresh").Should().NotBeNull();
    }

    // ── CelesTrak wins ────────────────────────────────────────────────────────

    [Fact]
    public void Update_CelesTrakWins_WhenConflictWithKeepTrack()
    {
        var cache = new OrbitalCache();
        var celestrak = TleObj("42", source: "celestrak") with { Name = "FROM_CELESTRAK" };
        var keeptrack = TleObj("42", source: "keeptrack") with { Name = "FROM_KEEPTRACK" };

        // Both arrive in the same batch (simulating merge output)
        cache.Update([celestrak, keeptrack]);

        cache.GetById("42")!.Source.Should().Be("celestrak");
        cache.GetById("42")!.Name.Should().Be("FROM_CELESTRAK");
    }

    [Fact]
    public void Update_CelesTrakOverwritesExistingKeepTrack_OnSubsequentCall()
    {
        var cache = new OrbitalCache();
        cache.Update([TleObj("42", source: "keeptrack") with { Name = "OLD" }]);
        cache.Update([TleObj("42", source: "celestrak") with { Name = "NEW" }]);

        cache.GetById("42")!.Source.Should().Be("celestrak");
        cache.GetById("42")!.Name.Should().Be("NEW");
    }

    // ── LastFetch / LastPropagation ───────────────────────────────────────────

    [Fact]
    public void Update_SetsLastFetch_WhenObjectsHaveTleLines()
    {
        var cache = new OrbitalCache();
        cache.Update([TleObj("1")]);   // TleLine1 != null

        cache.LastFetch.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        cache.LastPropagation.Should().BeNull();
    }

    [Fact]
    public void Update_SetsLastPropagation_WhenObjectsHaveNoTleLines()
    {
        var cache = new OrbitalCache();
        cache.Update([PropagatedObj("1")]);   // TleLine1 == null

        cache.LastPropagation.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        cache.LastFetch.Should().BeNull();
    }

    // ── Thread safety (smoke) ─────────────────────────────────────────────────

    [Fact]
    public void Update_ThreadSafe_ConcurrentWrites_DoNotThrow()
    {
        var cache = new OrbitalCache();
        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            var batch = Enumerable.Range(i * 10, 10)
                .Select(j => TleObj(j.ToString()))
                .ToList();
            cache.Update(batch);
        }));

        var act = () => Task.WhenAll(tasks).GetAwaiter().GetResult();
        act.Should().NotThrow();
    }
}
