using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Dtos.Dashboard;
using MissionClear.Api.Dtos.History;
using MissionClear.Api.Dtos.Mission;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;
using Xunit;

namespace MissionClear.Tests.Integration;

public sealed class SimulationFlowTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private async Task<string> RegisterAndGetTokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@flow.com";
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Test@Pass1",
            display_name = "Flow User",
            role = "Researcher"
        }, _jsonOptions);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return auth!.AccessToken;
    }

    private void SeedOrbitalCache()
    {
        var cache = factory.Services.GetRequiredService<IOrbitalCache>();
        var fakeDebris = new List<OrbitalObject>
        {
            new("NORAD_1", "DEB 1", "debris", 0, 0, 400, 7.5, "celestrak", DateTime.UtcNow),
            new("NORAD_2", "DEB 2", "debris", 10, 10, 400, 7.5, "celestrak", DateTime.UtcNow)
        };
        cache.Update(fakeDebris);
    }

    [Fact]
    public async Task FullSimulationLifecycle_WithHistoryAndDashboard()
    {
        // 1. Arrange & Seed
        SeedOrbitalCache();
        var token = await RegisterAndGetTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var destination = "iss";
        var departureTime = DateTime.UtcNow.AddHours(1).ToString("O");
        var arrivalTime = DateTime.UtcNow.AddHours(7).ToString("O"); // 6 hours

        // 2. Create Session
        var sessionReq = new SessionRequest(destination, departureTime, arrivalTime);
        var sessionResp = await _client.PostAsJsonAsync("/api/mission/session", sessionReq, _jsonOptions);
        sessionResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await sessionResp.Content.ReadFromJsonAsync<SessionResponse>(_jsonOptions);
        session!.SessionId.Should().StartWith("sess_");

        // 3. Simulate (Stateless prediction)
        var simulateReq = new SimulateRequest(destination, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(7));
        var simResp = await _client.PostAsJsonAsync("/api/mission/simulate", simulateReq, _jsonOptions);
        simResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var simResult = await simResp.Content.ReadFromJsonAsync<SimulateResponse>(_jsonOptions);
        simResult!.RiskScore.Should().BeInRange(0, 1);

        // 4. Complete Session (Save to History)
        var completeReq = new CompleteSessionRequest("success", SaveToHistory: true);
        var completeResp = await _client.PostAsJsonAsync($"/api/mission/session/{session.SessionId}/complete", completeReq, _jsonOptions);
        completeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var completeResult = await completeResp.Content.ReadFromJsonAsync<CompleteSessionResponse>(_jsonOptions);
        completeResult!.MissionId.Should().StartWith("msn_");

        // 5. Verify History
        var historyResp = await _client.GetAsync("/api/missions");
        historyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await historyResp.Content.ReadFromJsonAsync<MissionClear.Api.Dtos.Common.PagedResponse<MissionSummaryDto>>(_jsonOptions);
        history!.Data.Should().Contain(m => m.Id == completeResult.MissionId);

        // 6. Verify Dashboard Summary
        var dashResp = await _client.GetAsync("/api/dashboard/summary");
        dashResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dash = await dashResp.Content.ReadFromJsonAsync<DashboardSummaryResponse>(_jsonOptions);
        dash!.User!.TotalMissions.Should().BeGreaterThan(0);
        dash.User.BestScore.Should().BeGreaterThan(0);
    }
}