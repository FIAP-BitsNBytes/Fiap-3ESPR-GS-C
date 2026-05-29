using System.Net;
using FluentAssertions;
using Xunit;

namespace MissionClear.Tests.Integration;

public sealed class StatusEndpointTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetStatus_Returns200_Always()
    {
        var response = await _client.GetAsync("/api/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDestinations_Returns200_WithList()
    {
        var response = await _client.GetAsync("/api/destinations");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDebris_WhenCacheNotReady_Returns503()
    {
        // Factory starts with empty cache → IsReady = false → CACHE_NOT_READY
        var response = await _client.GetAsync("/api/debris");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}