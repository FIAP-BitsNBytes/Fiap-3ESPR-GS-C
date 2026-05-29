using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MissionClear.Api.Dtos.Auth;
using Xunit;

namespace MissionClear.Tests.Integration;

public sealed class MissionsAuthorizationTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private async Task<string> RegisterAndGetTokenAsync(string role = "Researcher")
    {
        var email    = $"{Guid.NewGuid():N}@test.com";
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password     = "Test@Pass1",
            display_name = "Test User",
            role          // service must accept role hint; otherwise all users are Researcher
        });
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return auth!.AccessToken;
    }

    [Fact]
    public async Task GetMissions_Returns401_WithoutToken()
    {
        var response = await _client.GetAsync("/api/missions");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMissions_Returns200_WithValidToken()
    {
        var token = await RegisterAndGetTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/missions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMissionStats_Returns200_WithValidToken()
    {
        var token = await RegisterAndGetTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/missions/stats");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteMission_Returns403_ForResearcher()
    {
        // Researcher role does not have Administrator — must receive 403
        var token = await RegisterAndGetTokenAsync("Researcher");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // Non-existent mission ID is fine — auth check happens before DB lookup
        var response = await _client.DeleteAsync(
            "/api/missions/msn_00000000000000000000000000000001");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}