using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace MissionClear.Tests.Integration;

/// <summary>
/// Testa POST /api/admin/refresh.
/// Usa tokens JWT gerados localmente para evitar dependência de fluxo de registro.
/// </summary>
public sealed class AdminRefreshTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private const string Secret   = "test-secret-key-with-at-least-32-characters-long!!";
    private const string Issuer   = "mission-clear-api-test";
    private const string Audience = "mission-clear-mobile-test";

    private string GenerateToken(string role)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Email, $"{role.ToLower()}@test.com"),
            new Claim("display_name", role),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:             Issuer,
            audience:           Audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task Refresh_NoToken_Returns401()
    {
        var resp = await _client.PostAsync("/api/admin/refresh", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadAsStringAsync();
        JsonDocument.Parse(body).RootElement
            .TryGetProperty("error", out _).Should().BeTrue("must return JSON error envelope");
    }

    [Fact]
    public async Task Refresh_ResearcherRole_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateToken("Researcher"));

        var resp = await _client.PostAsync("/api/admin/refresh", null);
        _client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // DESATIVADO: dispara DataAggregatorService.FetchAndMergeAsync() sem mock do aggregator.
    // Se ImmediateFailHandler no TestWebApplicationFactory não interceptar corretamente,
    // o teste faz chamadas HTTP reais ao celestrak.org e pode bloquear o IP.
    // Cobertura equivalente: AdminRefreshAggressiveTests.Refresh_AggregatorSucceeds_Returns200WithCorrectShape
    // [Fact]
    // public async Task Refresh_AdministratorRole_Returns200OrServiceUnavailable() { ... }

    // DESATIVADO: mesmo motivo — usa _client sem mock do aggregator, depende de rede real.
    // [Fact]
    // public async Task Refresh_ResponseShape_HasRequiredFields() { ... }

    // DESATIVADO: chama /api/admin/refresh duas vezes sem mock — risco de 2x chamadas reais.
    // [Fact]
    // public async Task Refresh_IsIdempotent_CanBeCalledMultipleTimes() { ... }
}
