using FluentAssertions;
using MissionClear.Api.Dtos.Mission;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using Moq;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class MissionSimulationServiceTests
{
    private static IMissionSimulationService BuildSut(
        IOrbitalCache? cache = null,
        ISessionStore? store = null,
        IMissionHistoryService? history = null)
    {
        cache   ??= Mock.Of<IOrbitalCache>(c => c.GetAll() == Array.Empty<OrbitalObject>());
        store   ??= new SessionStore();
        history ??= Mock.Of<IMissionHistoryService>();

        return new MissionSimulationService(
            new ConjunctionDetector(),
            new LaunchWindowCalculator(),
            cache,
            store,
            history);
    }

    [Fact]
    public async Task SimulateAsync_ReturnsValidResponse_ForKnownDestination()
    {
        var sut  = BuildSut();
        var req  = new SimulateRequest(
            "ISS",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 6, 0, 0, DateTimeKind.Utc));

        var result = await sut.SimulateAsync(req);

        result.Should().NotBeNull();
        result.MissionScore.Should().BeInRange(0, 100);
        result.RiskScore.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public async Task SimulateAsync_ReturnsMissionScore100_WhenNoDebris()
    {
        var sut    = BuildSut();
        var req    = new SimulateRequest(
            "ISS",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 6, 0, 0, DateTimeKind.Utc));
        var result = await sut.SimulateAsync(req);

        // No debris → risk_score = 0 → safety = 50; efficiency depends on deltaV
        result.RiskScore.Should().Be(0.0);
        result.MissionScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateSessionAsync_ReturnsSessionWithStreamUrl()
    {
        var sut = BuildSut();
        var req = new SessionRequest("ISS", DateTime.UtcNow.ToString("O"), DateTime.UtcNow.AddHours(6).ToString("O"));

        var result = await sut.CreateSessionAsync(req);

        result.Should().NotBeNull();
        result.SessionId.Should().NotBeNullOrEmpty();
        result.StreamUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CompleteSessionAsync_ReturnsScore_WhenSessionExists()
    {
        var store = new SessionStore();
        var sut   = BuildSut(store: store);

        var sessionResp = await sut.CreateSessionAsync(
            new SessionRequest("ISS", DateTime.UtcNow.ToString("O"), DateTime.UtcNow.AddHours(6).ToString("O")));

        var result = await sut.CompleteSessionAsync(
            sessionResp.SessionId,
            new CompleteSessionRequest(Status: "aborted", SaveToHistory: false),
            userId: null);

        result.Should().NotBeNull();
        result.MissionScore.Should().BeInRange(0, 100);
    }
}
