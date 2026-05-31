using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services.Interfaces;
using Moq;
using Xunit;

namespace MissionClear.Tests.Integration;

/// <summary>
/// Adversarial tests for POST /api/admin/refresh.
/// Target: error propagation, response contract, and auth edge cases.
/// Tests marked BUG expose real defects — they should fail RED, then pass GREEN after fixes.
/// </summary>
public sealed class AdminRefreshAggressiveTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private const string Secret   = "test-secret-key-with-at-least-32-characters-long!!";
    private const string Issuer   = "mission-clear-api-test";
    private const string Audience = "mission-clear-mobile-test";

    private string MakeToken(string role, bool expired = false, bool notYetValid = false,
        string? wrongSecret = null, bool omitRole = false)
    {
        var keyBytes = Encoding.UTF8.GetBytes(wrongSecret ?? Secret);
        var key      = new SymmetricSecurityKey(keyBytes);
        var creds    = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, $"{role.ToLower()}@test.com"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (!omitRole)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var expires   = expired     ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1);
        var notBefore = notYetValid ? DateTime.UtcNow.AddHours(1)
                      : expired     ? expires.AddHours(-1)         // must be before expires
                      :               DateTime.UtcNow.AddSeconds(-1);

        var token = new JwtSecurityToken(Issuer, Audience, claims, notBefore, expires, creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private HttpClient ClientWithAggregator(Action<Mock<IDataAggregatorService>> setup)
    {
        var mock = new Mock<IDataAggregatorService>();
        setup(mock);

        return factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
                s.AddScoped<IDataAggregatorService>(_ => mock.Object)))
            .CreateClient();
    }

    private static void AuthAsAdmin(HttpClient client, string secret = Secret) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer",
                new AdminRefreshAggressiveTests(null!).MakeToken("Administrator", wrongSecret: secret == Secret ? null : secret));

    // ── BUG-1: HttpRequestException should map to 503 ─────────────────────────
    // Currently FAILS: middleware catches as generic Exception → returns 500 INTERNAL_ERROR.
    // Fix: AdminController must catch non-DomainException and re-throw as DomainException(503).

    [Fact]
    public async Task BUG1_Refresh_AggregatorThrowsHttpRequestException_Returns503()
    {
        var client = ClientWithAggregator(m =>
            m.Setup(s => s.FetchAndMergeAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new HttpRequestException("CelesTrak unreachable")));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MakeToken("Administrator"));

        var resp = await client.PostAsync("/api/admin/refresh", null);

        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "CelesTrak failure must surface as 503 per API contract §13");
    }

    [Fact]
    public async Task BUG1_Refresh_AggregatorThrowsHttpRequestException_ErrorIsCacheNotReady()
    {
        var client = ClientWithAggregator(m =>
            m.Setup(s => s.FetchAndMergeAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new HttpRequestException("CelesTrak down")));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MakeToken("Administrator"));

        var resp = await client.PostAsync("/api/admin/refresh", null);
        var body = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var root = JsonDocument.Parse(body).RootElement;
        root.TryGetProperty("error", out var errorProp).Should().BeTrue("response must have 'error' field");
        errorProp.GetString().Should().Be("CACHE_NOT_READY",
            "error code must match API contract §13 table");
    }

    [Fact]
    public async Task BUG1_Refresh_AggregatorThrowsDbException_Returns503NotInternalError()
    {
        var client = ClientWithAggregator(m =>
            m.Setup(s => s.FetchAndMergeAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("DB connection failed — no fallback")));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MakeToken("Administrator"));

        var resp = await client.PostAsync("/api/admin/refresh", null);
        var body = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "DB failure during fallback should also be 503, not 500");
        var root = JsonDocument.Parse(body).RootElement;
        root.TryGetProperty("error", out var ep).Should().BeTrue();
        ep.GetString().Should().Be("CACHE_NOT_READY");
    }

    // ── DomainException from aggregator is handled correctly ─────────────────

    [Fact]
    public async Task Refresh_AggregatorThrowsDomainException503_Returns503()
    {
        var client = ClientWithAggregator(m =>
            m.Setup(s => s.FetchAndMergeAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new DomainException("CACHE_NOT_READY", "already a domain error", 503)));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MakeToken("Administrator"));

        var resp = await client.PostAsync("/api/admin/refresh", null);
        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // ── Successful refresh shape ──────────────────────────────────────────────

    [Fact]
    public async Task Refresh_AggregatorSucceeds_Returns200WithCorrectShape()
    {
        var client = ClientWithAggregator(m =>
            m.Setup(s => s.FetchAndMergeAsync(It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MakeToken("Administrator"));

        var resp = await client.PostAsync("/api/admin/refresh", null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        root.TryGetProperty("objects_in_cache", out _).Should().BeTrue();
        root.TryGetProperty("last_fetch",       out _).Should().BeTrue();
        root.TryGetProperty("message",          out _).Should().BeTrue();
        root.TryGetProperty("ObjectsInCache",   out _).Should().BeFalse("must be snake_case");
    }

    [Fact]
    public async Task Refresh_AggregatorSucceeds_MessageContainsObjectCount()
    {
        var client = ClientWithAggregator(m =>
            m.Setup(s => s.FetchAndMergeAsync(It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MakeToken("Administrator"));

        var resp = await client.PostAsync("/api/admin/refresh", null);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

        var message = root.GetProperty("message").GetString();
        message.Should().NotBeNullOrWhiteSpace();
        message.Should().Contain("Refresh complete");
    }

    // ── Auth edge cases ───────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MakeToken("Administrator", expired: true));

        var resp = await client.PostAsync("/api/admin/refresh", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "expired tokens must be rejected");
    }

    [Fact]
    public async Task Refresh_TokenSignedWithWrongKey_Returns401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer",
                MakeToken("Administrator", wrongSecret: "totally-different-wrong-secret-key-123456!!"));

        var resp = await client.PostAsync("/api/admin/refresh", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "tokens signed with wrong key must be rejected");
    }

    [Fact]
    public async Task Refresh_TokenWithNoRoleClaim_Returns403()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer",
                MakeToken("Administrator", omitRole: true));

        var resp = await client.PostAsync("/api/admin/refresh", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "valid token without Administrator role must be 403, not 401");
    }

    [Fact]
    public async Task Refresh_BearerLiteralWithoutValue_Returns401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer ");

        var resp = await client.PostAsync("/api/admin/refresh", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_GarbageAuthHeader_Returns401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "garbage-not-a-jwt");

        var resp = await client.PostAsync("/api/admin/refresh", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Response contract ─────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_AnyFailure_ResponseIsAlwaysJson()
    {
        var client = ClientWithAggregator(m =>
            m.Setup(s => s.FetchAndMergeAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new Exception("unexpected failure")));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MakeToken("Administrator"));

        var resp = await client.PostAsync("/api/admin/refresh", null);
        var body = await resp.Content.ReadAsStringAsync();

        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/json",
            "all error responses must be JSON");
        var act = () => JsonDocument.Parse(body);
        act.Should().NotThrow("response body must be valid JSON regardless of error type");
    }

    [Fact]
    public async Task Refresh_401Response_HasJsonErrorEnvelope()
    {
        var client = factory.CreateClient();

        var resp = await client.PostAsync("/api/admin/refresh", null);
        var body = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var act = () => JsonDocument.Parse(body);
        act.Should().NotThrow("401 must return JSON, not HTML or empty");
        JsonDocument.Parse(body).RootElement
            .TryGetProperty("error", out _).Should().BeTrue();
    }

    // ── Idempotency under mocked aggregator ───────────────────────────────────

    [Fact]
    public async Task Refresh_CalledConcurrently_NoCrash()
    {
        var client = ClientWithAggregator(m =>
            m.Setup(s => s.FetchAndMergeAsync(It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask));

        var token = MakeToken("Administrator");

        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            using var c = factory.WithWebHostBuilder(b =>
                b.ConfigureServices(s => {
                    var mk = new Mock<IDataAggregatorService>();
                    mk.Setup(x => x.FetchAndMergeAsync(It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask);
                    s.AddScoped<IDataAggregatorService>(_ => mk.Object);
                })).CreateClient();
            c.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            return await c.PostAsync("/api/admin/refresh", null);
        });

        var responses = await Task.WhenAll(tasks);
        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().Be(HttpStatusCode.OK, "concurrent refreshes must not crash"));
    }
}
