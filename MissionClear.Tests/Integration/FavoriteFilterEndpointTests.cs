using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;
using Moq;
using Xunit;

namespace MissionClear.Tests.Integration;

/// <summary>
/// Integration tests for the favorites filter endpoints:
///   GET /api/users/me/favorites/debris?type=&amp;sort=
///   GET /api/users/me/favorites/windows?destination=&amp;sort=
///
/// Flow: register → login → PUT favorites → GET filtered → assert.
/// Debris tests override IOrbitalCache (singleton) to inject known objects.
/// </summary>
public sealed class FavoriteFilterEndpointTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<(HttpClient client, string token)> RegisterAndLoginAsync()
    {
        var client = factory.CreateClient();
        var email  = $"{Guid.NewGuid():N}@filter-test.com";

        var reg = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password     = "Filter@Pass1",
            display_name = "Filter Tester",
        });
        reg.EnsureSuccessStatusCode();

        var auth = await reg.Content.ReadFromJsonAsync<AuthResponse>(_json);
        return (client, auth!.AccessToken);
    }

    private HttpClient ClientWithCache(Action<Mock<IOrbitalCache>> setup)
    {
        var mock = new Mock<IOrbitalCache>();
        mock.SetupGet(c => c.IsReady).Returns(true);
        setup(mock);

        return factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                var existing = s.SingleOrDefault(d => d.ServiceType == typeof(IOrbitalCache));
                if (existing is not null) s.Remove(existing);
                s.AddSingleton<IOrbitalCache>(_ => mock.Object);
            })).CreateClient();
    }

    private static OrbitalObject MakeObj(string id, string type, double altKm, double velKmS = 7.5) =>
        new(id, $"Object {id}", type, 0, 0, altKm, velKmS, "celestrak", DateTime.UtcNow);

    private static object WindowPayload(string id, string destination) => new
    {
        id,
        destination,
        saved_at = DateTime.UtcNow.ToString("O"),
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // AUTH GUARD — both endpoints require Bearer
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetFavoriteDebris_NoToken_Returns401()
    {
        var client = factory.CreateClient();
        var resp   = await client.GetAsync("/api/users/me/favorites/debris");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFavoriteWindows_NoToken_Returns401()
    {
        var client = factory.CreateClient();
        var resp   = await client.GetAsync("/api/users/me/favorites/windows");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RESPONSE CONTRACT — shape and status with empty favorites
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetFavoriteDebris_NoFavoritesSaved_Returns200EmptyArray()
    {
        var (client, token) = await RegisterAndLoginAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/users/me/favorites/debris");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array,
            "response must be a JSON array");
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetFavoriteWindows_NoFavoritesSaved_Returns200EmptyArray()
    {
        var (client, token) = await RegisterAndLoginAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/users/me/favorites/windows");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DEBRIS FILTER — type filter end-to-end
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetFavoriteDebris_TypeFilter_ReturnsOnlyMatchingObjects()
    {
        // Step 1: Register + login a real user
        var (setupClient, setupToken) = await RegisterAndLoginAsync();
        setupClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setupToken);

        // Step 2: PUT 3 favorite debris IDs
        var putResp = await setupClient.PutAsJsonAsync("/api/users/me/favorites", new
        {
            debris_ids = new[] { "SAT-1", "DEB-1", "RKT-1" },
            windows    = Array.Empty<object>(),
        });
        putResp.EnsureSuccessStatusCode();

        // Step 3: Create a client with mocked cache containing those IDs
        var cacheObjects = new List<OrbitalObject>
        {
            MakeObj("SAT-1", "satellite",   500),
            MakeObj("DEB-1", "debris",      700),
            MakeObj("RKT-1", "rocket_body", 600),
            MakeObj("EXTRA", "satellite",   400), // not in favorites — must not appear
        };
        var cachedClient = ClientWithCache(m =>
            m.Setup(c => c.GetAll()).Returns(cacheObjects));
        cachedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setupToken);

        // Step 4: GET with type=satellite filter
        var resp = await cachedClient.GetAsync("/api/users/me/favorites/debris?type=satellite");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>(_json);
        items.Should().NotBeNull();
        items!.Should().HaveCount(1, "only SAT-1 is both favorited AND a satellite");
        items[0].GetProperty("id").GetString().Should().Be("SAT-1");
        items[0].GetProperty("type").GetString().Should().Be("satellite");
    }

    [Fact]
    public async Task GetFavoriteDebris_NoTypeFilter_ReturnsAllFavoritedObjects()
    {
        var (setupClient, setupToken) = await RegisterAndLoginAsync();
        setupClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setupToken);

        await setupClient.PutAsJsonAsync("/api/users/me/favorites", new
        {
            debris_ids = new[] { "SAT-2", "DEB-2" },
            windows    = Array.Empty<object>(),
        });

        var cacheObjects = new List<OrbitalObject>
        {
            MakeObj("SAT-2", "satellite", 500),
            MakeObj("DEB-2", "debris",    700),
        };
        var cachedClient = ClientWithCache(m =>
            m.Setup(c => c.GetAll()).Returns(cacheObjects));
        cachedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setupToken);

        var resp  = await cachedClient.GetAsync("/api/users/me/favorites/debris");
        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>(_json);

        items.Should().HaveCount(2, "no type filter — both favorited objects returned");
    }

    [Fact]
    public async Task GetFavoriteDebris_IdNotInCache_IsSkippedGracefully()
    {
        var (setupClient, setupToken) = await RegisterAndLoginAsync();
        setupClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setupToken);

        await setupClient.PutAsJsonAsync("/api/users/me/favorites", new
        {
            debris_ids = new[] { "KNOWN", "GHOST" }, // GHOST won't be in cache
            windows    = Array.Empty<object>(),
        });

        var cacheObjects = new List<OrbitalObject>
        {
            MakeObj("KNOWN", "debris", 500),
            // GHOST intentionally absent
        };
        var cachedClient = ClientWithCache(m =>
            m.Setup(c => c.GetAll()).Returns(cacheObjects));
        cachedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setupToken);

        var resp  = await cachedClient.GetAsync("/api/users/me/favorites/debris");
        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>(_json);

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "missing cache entries must not cause 500 — endpoint degrades gracefully");
        items.Should().HaveCount(1);
        items![0].GetProperty("id").GetString().Should().Be("KNOWN");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DEBRIS SORT — sort order end-to-end
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetFavoriteDebris_SortAltitudeDesc_ReturnsInDescendingOrder()
    {
        var (setupClient, setupToken) = await RegisterAndLoginAsync();
        setupClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setupToken);

        await setupClient.PutAsJsonAsync("/api/users/me/favorites", new
        {
            debris_ids = new[] { "D-LOW", "D-MID", "D-HIGH" },
            windows    = Array.Empty<object>(),
        });

        var cacheObjects = new List<OrbitalObject>
        {
            MakeObj("D-LOW",  "debris", 300),
            MakeObj("D-MID",  "debris", 600),
            MakeObj("D-HIGH", "debris", 900),
        };
        var cachedClient = ClientWithCache(m =>
            m.Setup(c => c.GetAll()).Returns(cacheObjects));
        cachedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setupToken);

        var resp  = await cachedClient.GetAsync("/api/users/me/favorites/debris?sort=altitude_desc");
        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>(_json);

        items.Should().HaveCount(3);
        var altitudes = items!.Select(i => i.GetProperty("altitude_km").GetDouble()).ToList();
        altitudes.Should().BeInDescendingOrder("sort=altitude_desc must order highest first");
    }

    [Fact]
    public async Task GetFavoriteDebris_SortNameAsc_ReturnsAlphabetically()
    {
        var (setupClient, setupToken) = await RegisterAndLoginAsync();
        setupClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setupToken);

        await setupClient.PutAsJsonAsync("/api/users/me/favorites", new
        {
            debris_ids = new[] { "N-C", "N-A", "N-B" },
            windows    = Array.Empty<object>(),
        });

        var cacheObjects = new List<OrbitalObject>
        {
            new("N-C", "Charlie Debris", "debris", 0, 0, 500, 7.5, "celestrak", DateTime.UtcNow),
            new("N-A", "Alpha Sat",      "debris", 0, 0, 500, 7.5, "celestrak", DateTime.UtcNow),
            new("N-B", "Bravo Rocket",   "debris", 0, 0, 500, 7.5, "celestrak", DateTime.UtcNow),
        };
        var cachedClient = ClientWithCache(m =>
            m.Setup(c => c.GetAll()).Returns(cacheObjects));
        cachedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setupToken);

        var resp  = await cachedClient.GetAsync("/api/users/me/favorites/debris?sort=name_asc");
        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>(_json);

        var names = items!.Select(i => i.GetProperty("name").GetString()!).ToList();
        names[0].Should().Be("Alpha Sat",    "A comes first alphabetically");
        names[1].Should().Be("Bravo Rocket", "B second");
        names[2].Should().Be("Charlie Debris", "C third");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WINDOWS FILTER — destination filter end-to-end
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetFavoriteWindows_DestinationFilter_ReturnsOnlyMatchingWindows()
    {
        var (client, token) = await RegisterAndLoginAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // Step 1: PUT 3 windows — 2 ISS, 1 LEO
        var putResp = await client.PutAsJsonAsync("/api/users/me/favorites", new
        {
            debris_ids = Array.Empty<string>(),
            windows    = new object[]
            {
                WindowPayload("ISS_WIN_1",  "ISS"),
                WindowPayload("ISS_WIN_2",  "ISS"),
                WindowPayload("LEO_WIN_1",  "LEO_GENERIC"),
            },
        });
        putResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 2: GET filtered by destination=ISS
        var resp = await client.GetAsync("/api/users/me/favorites/windows?destination=ISS");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc  = JsonDocument.Parse(body);
        var arr  = doc.RootElement;
        arr.GetArrayLength().Should().Be(2, "only the 2 ISS windows must be returned");

        foreach (var item in arr.EnumerateArray())
        {
            item.GetProperty("destination").GetString().Should().Be("ISS");
        }
    }

    [Fact]
    public async Task GetFavoriteWindows_NoDestinationFilter_ReturnsAllWindows()
    {
        var (client, token) = await RegisterAndLoginAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        await client.PutAsJsonAsync("/api/users/me/favorites", new
        {
            debris_ids = Array.Empty<string>(),
            windows    = new object[]
            {
                WindowPayload("W-ISS",  "ISS"),
                WindowPayload("W-LEO",  "LEO_GENERIC"),
                WindowPayload("W-SSO",  "SSO"),
            },
        });

        var resp = await client.GetAsync("/api/users/me/favorites/windows");
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.GetArrayLength().Should().Be(3,
            "no destination filter — all 3 windows returned");
    }

    [Fact]
    public async Task GetFavoriteWindows_UnknownDestination_ReturnsEmpty()
    {
        var (client, token) = await RegisterAndLoginAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        await client.PutAsJsonAsync("/api/users/me/favorites", new
        {
            debris_ids = Array.Empty<string>(),
            windows    = new object[] { WindowPayload("W-ISS", "ISS") },
        });

        var resp = await client.GetAsync("/api/users/me/favorites/windows?destination=MARS");
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.GetArrayLength().Should().Be(0,
            "no windows match MARS destination");
    }

    [Fact]
    public async Task GetFavoriteWindows_UserIsolation_CannotSeeAnotherUsersWindows()
    {
        // User A saves a window
        var (clientA, tokenA) = await RegisterAndLoginAsync();
        clientA.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenA);

        await clientA.PutAsJsonAsync("/api/users/me/favorites", new
        {
            debris_ids = Array.Empty<string>(),
            windows    = new object[] { WindowPayload("PRIVATE-WIN", "ISS") },
        });

        // User B registers separately — should not see User A's window
        var (clientB, tokenB) = await RegisterAndLoginAsync();
        clientB.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenB);

        var resp = await clientB.GetAsync("/api/users/me/favorites/windows");
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.GetArrayLength().Should().Be(0,
            "user B must not see user A's windows — tenant isolation");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FULL CYCLE — PUT then GET filtered, verify data survives the round-trip
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullCycle_PutThenGetFiltered_WindowDataPreservedAfterRoundTrip()
    {
        var (client, token) = await RegisterAndLoginAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var issWindow = new
        {
            id          = "ISS_ROUNDTRIP",
            destination = "ISS",
            saved_at    = "2026-06-01T10:00:00Z",
            label       = "Test window",
        };

        // PUT → round-trip
        await client.PutAsJsonAsync("/api/users/me/favorites", new
        {
            debris_ids = Array.Empty<string>(),
            windows    = new object[] { issWindow },
        });

        // GET with filter
        var resp = await client.GetAsync("/api/users/me/favorites/windows?destination=ISS");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var arr = doc.RootElement;

        arr.GetArrayLength().Should().Be(1);
        var item = arr[0];
        item.GetProperty("id").GetString().Should().Be("ISS_ROUNDTRIP",
            "window ID survives round-trip through DB → JSON → response");
        item.GetProperty("destination").GetString().Should().Be("ISS");
    }
}
