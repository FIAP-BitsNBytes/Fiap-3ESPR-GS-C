using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MissionClear.Api.Dtos.Auth;
using Xunit;

namespace MissionClear.Tests.Integration;

public sealed class AuthEndpointTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public async Task Register_Returns201_WithResearcherRole()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email        = $"{Guid.NewGuid():N}@test.com",
            password     = "Test@Pass1",
            display_name = "New User"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        body!.User.Role.Should().Be("Researcher");
        body.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Register_Returns409_WhenEmailDuplicate()
    {
        var email = $"{Guid.NewGuid():N}@test.com";
        var payload = new { email, password = "Test@Pass1", display_name = "Dup" };

        await _client.PostAsJsonAsync("/api/auth/register", payload);
        var second = await _client.PostAsJsonAsync("/api/auth/register", payload);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_Returns401_WithWrongPassword()
    {
        var email = $"{Guid.NewGuid():N}@test.com";
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Test@Pass1", display_name = "User" });

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Wrong@Pass1" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_Returns200_WithCorrectCredentials()
    {
        var email = $"{Guid.NewGuid():N}@test.com";
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Test@Pass1", display_name = "User" });

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Test@Pass1" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}