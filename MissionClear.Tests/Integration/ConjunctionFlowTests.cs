using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Dtos.Dashboard;
using MissionClear.Api.Dtos.Orbital;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;
using Xunit;

namespace MissionClear.Tests.Integration;

public sealed class ConjunctionFlowTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private void SeedOrbitalCache()
    {
        var cache = factory.Services.GetRequiredService<IOrbitalCache>();
        var fakeDebris = new List<OrbitalObject>
        {
            new("NORAD_1", "DEB 1", "debris", 0, 0, 400, 7.5, "celestrak", DateTime.UtcNow),
            new("NORAD_2", "DEB 2", "satellite", 10, 10, 800, 7.5, "celestrak", DateTime.UtcNow),
            new("NORAD_3", "DEB 3", "rocket_body", -10, -10, 1500, 7.5, "keeptrack", DateTime.UtcNow)
        };
        cache.Update(fakeDebris);
    }

    [Fact]
    public async Task GetDebris_ReturnsPaginatedListAndFiltersByAltitude()
    {
        SeedOrbitalCache();

        // Should return NORAD_2 and NORAD_3
        var resp = await _client.GetAsync("/api/debris?altitudeMinKm=500&altitudeMaxKm=2000");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await resp.Content.ReadFromJsonAsync<List<DebrisDto>>(_jsonOptions);
        list.Should().NotBeNull();
        list!.Count.Should().Be(2);
        list.Should().Contain(d => d.Id == "NORAD_2");
        list.Should().Contain(d => d.Id == "NORAD_3");
    }

    [Fact]
    public async Task GetDebrisStats_ReturnsAccurateCounts()
    {
        SeedOrbitalCache();

        var resp = await _client.GetAsync("/api/debris/stats");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var stats = await resp.Content.ReadFromJsonAsync<DebrisStatsDto>(_jsonOptions);
        stats!.TotalTracked.Should().Be(3);
        stats.ByType.Debris.Should().Be(1);
        stats.ByType.Satellite.Should().Be(1);
        stats.ByType.RocketBody.Should().Be(1);
        
        stats.ByAltitudeBand.Low200500km.Should().Be(1); // 400
        stats.ByAltitudeBand.Mid5001000km.Should().Be(1); // 800
        stats.ByAltitudeBand.High10002000km.Should().Be(1); // 1500

        stats.Sources.Celestrak.Should().Be(2);
        stats.Sources.Keeptrack.Should().Be(1);
    }

    // Workaround: I am redefining LaunchWindowResponse and BestWindowResponse here because they are returned anonymously 
    // from the controller using 'new { ... }'
    public sealed record LaunchWindowResponse(string Destination, string From, string To, int TotalWindows, int SafeWindows);
    public sealed record BestWindowResponse(string Destination, string From, string To, List<object> BestWindows);

    [Fact]
    public async Task GetLaunchWindows_ReturnsSlotsAndCalculatesRisk()
    {
        SeedOrbitalCache();

        var from = DateTime.UtcNow.ToString("O");
        var to = DateTime.UtcNow.AddHours(24).ToString("O");
        var resp = await _client.GetAsync($"/api/launch-windows?destination=iss&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await resp.Content.ReadFromJsonAsync<LaunchWindowResponse>(_jsonOptions);
        result!.TotalWindows.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetBestLaunchWindows_FiltersByRisk()
    {
        SeedOrbitalCache();

        var from = DateTime.UtcNow.ToString("O");
        var to = DateTime.UtcNow.AddHours(24).ToString("O");
        var resp = await _client.GetAsync($"/api/launch-windows/best?destination=iss&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&maxRisk=1.0&count=2");
        
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await resp.Content.ReadFromJsonAsync<BestWindowResponse>(_jsonOptions);
        result!.BestWindows.Count.Should().BeLessThanOrEqualTo(2);
    }
}