using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace MissionClear.Tests.Integration;

/// <summary>
/// Contract tests: verify every response field name, error code, and behavior
/// that the mobile (React Native) client depends on.
///
/// Tests are deliberately order-independent and create isolated data via Guid emails.
/// </summary>
public sealed class MobileContractTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(string email, string password, JsonDocument doc)> RegisterUserAsync()
    {
        var email    = $"{Guid.NewGuid():N}@contract.io";
        const string password = "Contract@Pass1";

        var resp = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password,
            display_name = "Contract User",
        }, _json);

        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return (email, password, doc);
    }

    private async Task<(string accessToken, string refreshToken)> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { email, password }, _json);
        resp.EnsureSuccessStatusCode();
        var doc  = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        return (root.GetProperty("access_token").GetString()!,
                root.GetProperty("refresh_token").GetString()!);
    }

    private string GenerateExpiredJwt(Guid userId, string email)
    {
        const string secret   = "test-secret-key-with-at-least-32-characters-long!!";
        const string issuer   = "mission-clear-api-test";
        const string audience = "mission-clear-mobile-test";

        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("display_name", "Test"),
            new Claim(ClaimTypes.Role, "Researcher"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:            issuer,
            audience:          audience,
            claims:            claims,
            notBefore:         DateTime.UtcNow.AddHours(-2),
            expires:           DateTime.UtcNow.AddHours(-1), // expired 1 hour ago
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CONTRACT 1: Login response field names exactly match what mobile expects
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Login_ResponseShape_MatchesMobileContract()
    {
        var (email, password, _) = await RegisterUserAsync();
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { email, password }, _json);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var root = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;

        // Top-level fields mobile reads
        root.TryGetProperty("access_token",  out _).Should().BeTrue("mobile reads access_token");
        root.TryGetProperty("refresh_token", out _).Should().BeTrue("mobile reads refresh_token");
        root.TryGetProperty("expires_in",    out _).Should().BeTrue("mobile reads expires_in");
        root.TryGetProperty("user",          out var user).Should().BeTrue("mobile reads user object");

        // user object fields
        user.TryGetProperty("id",           out _).Should().BeTrue("mobile reads user.id");
        user.TryGetProperty("email",        out _).Should().BeTrue("mobile reads user.email");
        user.TryGetProperty("display_name", out _).Should().BeTrue("mobile reads user.display_name");
        user.TryGetProperty("role",         out _).Should().BeTrue("mobile reads user.role");
        user.TryGetProperty("created_at",   out _).Should().BeTrue("mobile reads user.created_at");

        // PascalCase must NOT exist
        root.TryGetProperty("AccessToken",  out _).Should().BeFalse("must be snake_case");
        root.TryGetProperty("RefreshToken", out _).Should().BeFalse("must be snake_case");
        root.TryGetProperty("ExpiresIn",    out _).Should().BeFalse("must be snake_case");
    }

    [Fact]
    public async Task Login_UserId_StartsWithUsr()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (at, _) = await LoginAsync(email, password);

        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { email, password }, _json);
        var root = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;
        root.GetProperty("user").GetProperty("id").GetString().Should().StartWith("usr_");
    }

    [Fact]
    public async Task Login_TotalMissionsAndBestScore_AbsentOrInteger_ForNewUser()
    {
        // BuildAuthResponseAsync does not query mission DB — values are null → omitted.
        // Mobile TypeScript types declare them optional; absent == undefined is handled gracefully.
        var (email, password, _) = await RegisterUserAsync();
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { email, password }, _json);
        var root = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;
        var user = root.GetProperty("user");

        if (user.TryGetProperty("total_missions", out var tm))
            tm.ValueKind.Should().BeOneOf(JsonValueKind.Number, JsonValueKind.Null);
        if (user.TryGetProperty("best_score", out var bs))
            bs.ValueKind.Should().BeOneOf(JsonValueKind.Number, JsonValueKind.Null);
        // If absent: acceptable — WhenWritingNull omits null ints; mobile handles undefined
    }

    [Fact]
    public async Task Register_TotalMissionsAndBestScore_AbsentOrNull()
    {
        // New account: total_missions / best_score are null (omitted by WhenWritingNull)
        var (_, _, doc) = await RegisterUserAsync();
        var user = doc.RootElement.GetProperty("user");

        // Either absent or null — both acceptable; mobile handles nulls
        if (user.TryGetProperty("total_missions", out var tm))
            tm.ValueKind.Should().Be(JsonValueKind.Null);
        if (user.TryGetProperty("best_score", out var bs))
            bs.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Login_ExpiresIn_IsSeconds_NotMilliseconds()
    {
        var (email, password, _) = await RegisterUserAsync();
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { email, password }, _json);
        var root = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;

        var expiresIn = root.GetProperty("expires_in").GetInt32();
        // Should be in seconds (e.g. 900 = 15 min). Mobile does: now + expires_in * 1000
        expiresIn.Should().BeGreaterThan(60).And.BeLessThan(86_400 * 365);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CONTRACT 2: Refresh response — access_token + expires_in, NO new refresh_token
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Refresh_ResponseShape_MatchesMobileContract()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (_, rt) = await LoginAsync(email, password);

        var resp = await _client.PostAsJsonAsync("/api/auth/refresh", new { refresh_token = rt }, _json);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var root = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;
        root.TryGetProperty("access_token", out _).Should().BeTrue("mobile reads access_token from refresh");
        root.TryGetProperty("expires_in",   out _).Should().BeTrue("mobile reads expires_in from refresh");
        root.TryGetProperty("refresh_token", out _).Should().BeFalse("no new refresh_token — mobile reuses original");
    }

    [Fact]
    public async Task Refresh_DoesNotRotate_SameTokenUsableMultipleTimes()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (_, rt) = await LoginAsync(email, password);

        // Use the same refresh_token three times — all must succeed
        for (var i = 0; i < 3; i++)
        {
            var resp = await _client.PostAsJsonAsync("/api/auth/refresh", new { refresh_token = rt }, _json);
            resp.StatusCode.Should().Be(HttpStatusCode.OK, $"refresh attempt {i + 1} must succeed");
        }
    }

    [Fact]
    public async Task Refresh_NewAccessToken_IsDifferentFromOriginal()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (at1, rt) = await LoginAsync(email, password);

        var resp = await _client.PostAsJsonAsync("/api/auth/refresh", new { refresh_token = rt }, _json);
        var root = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;
        var at2 = root.GetProperty("access_token").GetString();

        at2.Should().NotBeNullOrEmpty();
        at2.Should().NotBe(at1, "new access token must differ from original");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CONTRACT 3: Error response body — { error, message, timestamp }
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ErrorResponse_FieldNames_MatchMobileContract()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "bad@x.com", password = "Wrong1" }, _json);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var root = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;
        root.TryGetProperty("error",     out _).Should().BeTrue("mobile reads error code");
        root.TryGetProperty("message",   out _).Should().BeTrue("mobile reads message (dev only)");
        root.TryGetProperty("timestamp", out _).Should().BeTrue("mobile reads timestamp");

        // PascalCase must NOT exist
        root.TryGetProperty("Error",   out _).Should().BeFalse();
        root.TryGetProperty("Message", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ErrorResponse_InvalidCredentials_ReturnsCorrectCode()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "noone@x.com", password = "Wrong1" }, _json);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var root = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;
        root.GetProperty("error").GetString().Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task ErrorResponse_DuplicateEmail_ReturnsCorrectCode()
    {
        var (email, _, _) = await RegisterUserAsync();
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Test@Pass1", display_name = "Dup" }, _json);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var root = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;
        root.GetProperty("error").GetString().Should().Be("EMAIL_ALREADY_EXISTS");
    }

    [Fact]
    public async Task ErrorResponse_InvalidRefreshToken_ReturnsCorrectCode()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refresh_token = "totally-invalid-token" }, _json);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var root = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;
        root.GetProperty("error").GetString().Should().Be("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task ErrorResponse_WeakPassword_ReturnsCorrectCode()
    {
        // Password "nouppercase1" passes [MinLength(8)] but fails the regex (no uppercase).
        // display_name must be >= 2 chars to pass [StringLength(MinimumLength=2)].
        // Without these valid values, ASP.NET rejects at model binding (ProblemDetails format),
        // never reaching AuthService where DomainException is thrown.
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = "weak@x.com", password = "nouppercase1", display_name = "XY" }, _json);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(body).RootElement;
        root.GetProperty("error").GetString().Should().Be("INVALID_PASSWORD_FORMAT");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CONTRACT 4: 401 JWT errors — body must exist with correct error code
    // (CRITICAL: mobile interceptor reads error.response.data.error to decide refresh)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProtectedEndpoint_NoToken_Returns401WithUnauthorizedCode()
    {
        var resp = await _client.GetAsync("/api/users/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrEmpty("mobile interceptor needs JSON body to read error code");

        var root = JsonDocument.Parse(body).RootElement;
        root.GetProperty("error").GetString().Should().Be("UNAUTHORIZED",
            "mobile must see UNAUTHORIZED (not TOKEN_EXPIRED) to avoid spurious refresh");
    }

    [Fact]
    public async Task ProtectedEndpoint_ExpiredToken_Returns401WithTokenExpiredCode()
    {
        // Generate a token that is valid but expired (signed with test key)
        var expiredToken = GenerateExpiredJwt(Guid.NewGuid(), "test@contract.io");

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", expiredToken);

        var resp = await _client.GetAsync("/api/users/me");
        _client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrEmpty("mobile MUST receive JSON body for expired token");

        var root = JsonDocument.Parse(body).RootElement;
        root.GetProperty("error").GetString().Should().Be("TOKEN_EXPIRED",
            "CRITICAL: mobile interceptor triggers refresh ONLY when error == TOKEN_EXPIRED");
    }

    [Fact]
    public async Task ProtectedEndpoint_ValidToken_Returns200()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (at, _) = await LoginAsync(email, password);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", at);

        var resp = await _client.GetAsync("/api/users/me");
        _client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CONTRACT 5: Logout
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Logout_Returns204_WithValidToken()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (at, rt) = await LoginAsync(email, password);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", at);

        var resp = await _client.PostAsJsonAsync("/api/auth/logout",
            new { refresh_token = rt }, _json);
        _client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_Returns401_WithoutToken()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/logout",
            new { refresh_token = "irrelevant" }, _json);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ErrorBody_HasJsonFormat()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/logout",
            new { refresh_token = "irrelevant" }, _json);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrEmpty();
        var root = JsonDocument.Parse(body).RootElement;
        root.TryGetProperty("error", out _).Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CONTRACT 6: Full lifecycle — the exact sequence the mobile executes
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullAuthLifecycle_RegisterLoginUseTokenRefreshUseAgainLogout()
    {
        // ── STEP 1: Register ──────────────────────────────────────────────────
        var email    = $"{Guid.NewGuid():N}@lifecycle.io";
        const string password = "Lifecycle@1";

        var regResp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password, display_name = "Lifecycle" }, _json);
        regResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var regRoot = (await JsonDocument.ParseAsync(await regResp.Content.ReadAsStreamAsync())).RootElement;
        regRoot.GetProperty("access_token").GetString().Should().NotBeNullOrWhiteSpace();
        regRoot.GetProperty("refresh_token").GetString().Should().NotBeNullOrWhiteSpace();

        // ── STEP 2: Login ─────────────────────────────────────────────────────
        var (at1, rt) = await LoginAsync(email, password);
        at1.Should().NotBeNullOrWhiteSpace();
        rt.Should().NotBeNullOrWhiteSpace();

        // ── STEP 3: Use access token → GET /api/users/me ──────────────────────
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at1);
        var meResp = await _client.GetAsync("/api/users/me");
        meResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var meRoot = (await JsonDocument.ParseAsync(await meResp.Content.ReadAsStreamAsync())).RootElement;
        meRoot.GetProperty("email").GetString().Should().Be(email);

        // ── STEP 4: Refresh → new access token (same refresh token) ──────────
        _client.DefaultRequestHeaders.Authorization = null;
        var refreshResp = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refresh_token = rt }, _json);
        refreshResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshRoot = (await JsonDocument.ParseAsync(await refreshResp.Content.ReadAsStreamAsync())).RootElement;
        var at2 = refreshRoot.GetProperty("access_token").GetString()!;
        at2.Should().NotBe(at1, "access token must change after refresh");
        refreshRoot.TryGetProperty("refresh_token", out _).Should().BeFalse("no rotation — mobile keeps original rt");

        // ── STEP 5: Use new access token ──────────────────────────────────────
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at2);
        var me2Resp = await _client.GetAsync("/api/users/me");
        me2Resp.StatusCode.Should().Be(HttpStatusCode.OK, "new access token must work");

        // ── STEP 6: Refresh again with SAME rt (no rotation) ─────────────────
        _client.DefaultRequestHeaders.Authorization = null;
        var refresh2Resp = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refresh_token = rt }, _json);
        refresh2Resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "CRITICAL: same refresh_token must work multiple times (no rotation)");

        // ── STEP 7: Logout ────────────────────────────────────────────────────
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at2);
        var logoutResp = await _client.PostAsJsonAsync("/api/auth/logout",
            new { refresh_token = rt }, _json);
        logoutResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _client.DefaultRequestHeaders.Authorization = null;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CONTRACT 7: CORS — Development must allow any origin (web + mobile web)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Cors_DevelopmentMode_AllowsAnyOrigin()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/status");
        request.Headers.Add("Origin", "http://localhost:8081"); // Expo web default port
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type,Authorization");

        var resp = await _client.SendAsync(request);

        // Either 204 (preflight success) or the origin header is reflected
        var allowOrigin = resp.Headers.TryGetValues("Access-Control-Allow-Origin", out var vals)
            ? vals.FirstOrDefault()
            : null;

        (resp.StatusCode == HttpStatusCode.NoContent || allowOrigin is not null)
            .Should().BeTrue("CORS preflight must succeed for Expo web origin");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CONTRACT 8: Users/me response shape
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UsersMe_ResponseShape_MatchesMobileContract()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (at, _) = await LoginAsync(email, password);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at);
        var resp = await _client.GetAsync("/api/users/me");
        _client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;

        // Mobile AuthContext bootstrap reads these fields
        root.TryGetProperty("id",           out _).Should().BeTrue();
        root.TryGetProperty("email",        out _).Should().BeTrue();
        root.TryGetProperty("display_name", out _).Should().BeTrue();
        root.TryGetProperty("role",         out _).Should().BeTrue();
        root.TryGetProperty("created_at",   out _).Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CONTRACT 9: Status endpoint — mobile checks this before showing orbital screens
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Status_ResponseShape_MatchesMobileContract()
    {
        var resp = await _client.GetAsync("/api/status");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var root = (await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync())).RootElement;
        root.TryGetProperty("status",      out var status).Should().BeTrue("mobile reads status field");
        root.TryGetProperty("tle_count",   out _).Should().BeTrue("mobile reads tle_count");

        // Mobile awaits status === "ready" before displaying orbital data
        var statusValue = status.GetString();
        statusValue.Should().BeOneOf("loading", "ready", "error",
            "mobile handles exactly these three states");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CONTRACT 10: Favorites — GET/PUT /api/users/me/favorites
    // CRITICAL: mobile FavoritesContext syncs on auth, mutations push to server.
    // Shapes, error codes, and partial-update semantics must match exactly.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Favorites_GET_RequiresAuthentication()
    {
        var resp = await _client.GetAsync("/api/users/me/favorites");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        root.TryGetProperty("error", out _).Should().BeTrue("mobile interceptor reads error field");
    }

    [Fact]
    public async Task Favorites_PUT_RequiresAuthentication()
    {
        var resp = await _client.PutAsJsonAsync("/api/users/me/favorites",
            new { debris_ids = new[] { "25544" }, windows = Array.Empty<object>() }, _json);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Favorites_GET_NewUser_ReturnsEmptyArrays()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (at, _) = await LoginAsync(email, password);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at);
        var resp = await _client.GetAsync("/api/users/me/favorites");
        _client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        root.GetProperty("debris_ids").GetArrayLength().Should().Be(0, "new user has no debris favorites");
        root.GetProperty("windows").GetArrayLength().Should().Be(0, "new user has no window favorites");
    }

    [Fact]
    public async Task Favorites_GET_ResponseShape_MatchesMobileContract()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (at, _) = await LoginAsync(email, password);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at);
        var resp = await _client.GetAsync("/api/users/me/favorites");
        _client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

        // Mobile FavoritesPayload: { debris_ids, windows, updated_at }
        root.TryGetProperty("debris_ids", out _).Should().BeTrue("mobile reads debris_ids");
        root.TryGetProperty("windows",    out _).Should().BeTrue("mobile reads windows");
        root.TryGetProperty("updated_at", out _).Should().BeTrue("mobile reads updated_at");

        // PascalCase must NOT exist
        root.TryGetProperty("DebrisIds",  out _).Should().BeFalse("must be snake_case");
        root.TryGetProperty("Windows",    out _).Should().BeFalse("must be snake_case");
        root.TryGetProperty("UpdatedAt",  out _).Should().BeFalse("must be snake_case");
    }

    [Fact]
    public async Task Favorites_PUT_UpdatesDebrisIds_AndReturnsUpdatedShape()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (at, _) = await LoginAsync(email, password);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at);

        var putResp = await _client.PutAsJsonAsync("/api/users/me/favorites",
            new { debris_ids = new[] { "25544", "12345" }, windows = Array.Empty<object>() }, _json);

        _client.DefaultRequestHeaders.Authorization = null;

        putResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = JsonDocument.Parse(await putResp.Content.ReadAsStringAsync()).RootElement;
        var ids = root.GetProperty("debris_ids").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        ids.Should().BeEquivalentTo(["25544", "12345"]);
    }

    [Fact]
    public async Task Favorites_GET_AfterPUT_ReturnsPersisted_Ids()
    {
        // Critical lifecycle test: PUT then GET verifies server-side persistence
        var (email, password, _) = await RegisterUserAsync();
        var (at, _) = await LoginAsync(email, password);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at);

        // PUT
        await _client.PutAsJsonAsync("/api/users/me/favorites",
            new { debris_ids = new[] { "PERSIST-1", "PERSIST-2" }, windows = Array.Empty<object>() }, _json);

        // GET
        var getResp = await _client.GetAsync("/api/users/me/favorites");
        _client.DefaultRequestHeaders.Authorization = null;

        var root = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync()).RootElement;
        var ids = root.GetProperty("debris_ids").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        ids.Should().BeEquivalentTo(["PERSIST-1", "PERSIST-2"],
            "GET must reflect what was PUT — confirms DB persistence");
    }

    [Fact]
    public async Task Favorites_PUT_ServerDeduplicates_DebrisIds()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (at, _) = await LoginAsync(email, password);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at);

        var putResp = await _client.PutAsJsonAsync("/api/users/me/favorites",
            new { debris_ids = new[] { "DUP", "DUP", "DUP", "UNIQUE" } }, _json);

        _client.DefaultRequestHeaders.Authorization = null;

        var root = JsonDocument.Parse(await putResp.Content.ReadAsStringAsync()).RootElement;
        var ids = root.GetProperty("debris_ids").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        ids.Should().OnlyHaveUniqueItems("server deduplicates before storing");
        ids.Should().BeEquivalentTo(["DUP", "UNIQUE"]);
    }

    [Fact]
    public async Task Favorites_PUT_NullDebrisIds_PreservesExistingDebris()
    {
        // CRITICAL: mobile sends { windows: [...] } without debris_ids when only updating windows.
        // Server must treat null as "don't touch" — not as "clear".
        var (email, password, _) = await RegisterUserAsync();
        var (at, _) = await LoginAsync(email, password);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at);

        // First PUT: set debris IDs
        await _client.PutAsJsonAsync("/api/users/me/favorites",
            new { debris_ids = new[] { "KEEP-ME" } }, _json);

        // Second PUT: update only windows — debris_ids absent (null)
        await _client.PutAsJsonAsync("/api/users/me/favorites",
            new { windows = Array.Empty<object>() }, _json);

        // GET: debris IDs must still be there
        var getResp = await _client.GetAsync("/api/users/me/favorites");
        _client.DefaultRequestHeaders.Authorization = null;

        var root = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync()).RootElement;
        var ids = root.GetProperty("debris_ids").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        ids.Should().Contain("KEEP-ME",
            "null debris_ids in PUT body must not clear existing debris");
    }

    [Fact]
    public async Task Favorites_PUT_EmptyDebrisArray_ClearsDebris()
    {
        // [] (empty array) explicitly means "clear" — different from null (preserve)
        var (email, password, _) = await RegisterUserAsync();
        var (at, _) = await LoginAsync(email, password);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at);

        // Set some debris
        await _client.PutAsJsonAsync("/api/users/me/favorites",
            new { debris_ids = new[] { "WILL-BE-GONE" } }, _json);

        // Clear with empty array
        var clearResp = await _client.PutAsJsonAsync("/api/users/me/favorites",
            new { debris_ids = Array.Empty<string>() }, _json);
        _client.DefaultRequestHeaders.Authorization = null;

        var root = JsonDocument.Parse(await clearResp.Content.ReadAsStringAsync()).RootElement;
        root.GetProperty("debris_ids").GetArrayLength().Should().Be(0,
            "empty array explicitly clears debris favorites");
    }

    [Fact]
    public async Task Favorites_PUT_UpdatedAt_ChangesAfterUpdate()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (at, _) = await LoginAsync(email, password);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at);

        // First PUT
        var resp1 = await _client.PutAsJsonAsync("/api/users/me/favorites",
            new { debris_ids = new[] { "A" } }, _json);
        var at1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync())
            .RootElement.GetProperty("updated_at").GetString();

        // Brief delay so timestamps can differ
        await Task.Delay(10);

        // Second PUT
        var resp2 = await _client.PutAsJsonAsync("/api/users/me/favorites",
            new { debris_ids = new[] { "B" } }, _json);
        _client.DefaultRequestHeaders.Authorization = null;

        var at2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync())
            .RootElement.GetProperty("updated_at").GetString();

        at1.Should().NotBeNull();
        at2.Should().NotBeNull();
        // updated_at must be a valid ISO 8601 string
        DateTime.Parse(at2!, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind).Should()
            .BeOnOrAfter(DateTime.Parse(at1!, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public async Task Favorites_PUT_WithWindowsPayload_Roundtrips()
    {
        var (email, password, _) = await RegisterUserAsync();
        var (at, _) = await LoginAsync(email, password);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", at);

        var window = new
        {
            id          = "ISS_2026-06-01T08:00:00Z",
            destination = "ISS",
            window      = new { start = "2026-06-01T08:00:00Z", end = "2026-06-01T10:00:00Z",
                                risk_score = 0.12, delta_v_km_s = 3.2, duration_hours = 2 },
            saved_at    = "2026-05-30T12:00:00Z",
        };

        await _client.PutAsJsonAsync("/api/users/me/favorites",
            new { windows = new[] { window } }, _json);

        var getResp = await _client.GetAsync("/api/users/me/favorites");
        _client.DefaultRequestHeaders.Authorization = null;

        var root = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync()).RootElement;
        root.GetProperty("windows").GetArrayLength().Should().Be(1,
            "PUT window must be retrievable via GET");
    }
}
