# Plan 04 — Authentication (JWT + BCrypt + UserService)

**Execution order:** After plan-00 + plan-01 + plan-02. Parallel with plan-03.
**Estimated time:** 75 minutes.
**Goal:** Implementar autenticação JWT Bearer completa (register/login/refresh/logout) com hash BCrypt e endpoints de perfil de usuário com agregação de estatísticas de missão.
**Dependencies:** plan-00 (scaffolding + settings), plan-01 (UserEntity, RefreshTokenEntity, MissionEntity, AppDbContext), plan-02 (DTOs de Auth/User, ApiErrorDto).
**Unlocks:** plan-07-controllers.md

---

## Visão Geral

Três serviços coordenados:

1. **JwtService** — geração e validação de access tokens HMAC-SHA256, geração de refresh tokens opacos (GUID).
2. **AuthService** — orquestra registro, login, refresh e logout. Persiste refresh tokens no SQLite. Hasheia senhas com BCrypt (workFactor 12).
3. **UserService** — perfil autenticado: GET retorna usuário + estatísticas agregadas de missões, PUT atualiza display_name e/ou senha.

Regras invioláveis:
- Senha em plaintext **nunca** é persistida — só o hash BCrypt.
- Access token JWT é stateless. Refresh token é opaco e armazenado no banco.
- Logout marca `IsRevoked = true` (não deleta — preserva trilha de auditoria).
- Toda exceção de domínio é lançada com código que casa com `ApiErrorDto`.

---

## Task 4.0 — DomainException

Criar `MissionClear.Api/Exceptions/DomainException.cs`:

```csharp
namespace MissionClear.Api.Exceptions;

/// <summary>
/// Exceção de domínio com código de erro e HTTP status code.
/// Nunca exponha mensagem interna em produção — apenas o código.
/// </summary>
public sealed class DomainException : Exception
{
    public string ErrorCode { get; }
    public int StatusCode { get; }

    public DomainException(string errorCode, string message, int statusCode = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}
```

---

## Task 4.1 — JwtService

**Files:**
- Create: `MissionClear.Api/Services/JwtService.cs`
- Create: `MissionClear.Tests/Services/JwtServiceTests.cs`

### Step 1: Testes (RED)

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Entities;
using MissionClear.Api.Services;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace MissionClear.Tests.Services;

public class JwtServiceTests
{
    private readonly JwtService _sut;
    private readonly JwtSettings _settings = new()
    {
        Secret = "test-secret-key-with-at-least-32-characters-long-for-hmac",
        Issuer = "mission-clear-api-test",
        Audience = "mission-clear-mobile-test",
        AccessTokenExpirationHours = 1,
        RefreshTokenExpirationDays = 7
    };

    public JwtServiceTests() => _sut = new JwtService(Options.Create(_settings));

    private static UserEntity SampleUser() => new()
    {
        Id = "usr_abc123",
        Email = "piloto@missionclear.app",
        DisplayName = "Piloto Guss",
        PasswordHash = "hash",
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public void GenerateAccessToken_returns_parseable_jwt_with_expected_claims()
    {
        var user = SampleUser();

        var token = _sut.GenerateAccessToken(user);

        token.Should().NotBeNullOrWhiteSpace();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Issuer.Should().Be(_settings.Issuer);
        jwt.Audiences.Should().Contain(_settings.Audience);
        jwt.Claims.Should().Contain(c => c.Type == "sub" && c.Value == user.Id);
        jwt.Claims.Should().Contain(c => c.Type == "email" && c.Value == user.Email);
        jwt.Claims.Should().Contain(c => c.Type == "name" && c.Value == user.DisplayName);
        jwt.Claims.Should().Contain(c => c.Type == "jti");
    }

    [Fact]
    public void ValidateAccessToken_returns_principal_for_valid_token()
    {
        var user = SampleUser();
        var token = _sut.GenerateAccessToken(user);

        var principal = _sut.ValidateAccessToken(token);

        principal.Should().NotBeNull();
        _sut.GetUserIdFromToken(principal!).Should().Be(user.Id);
    }

    [Fact]
    public void ValidateAccessToken_returns_null_for_tampered_token()
    {
        var token = _sut.GenerateAccessToken(SampleUser()) + "xxx";
        _sut.ValidateAccessToken(token).Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_returns_null_for_expired_token()
    {
        var shortLived = new JwtSettings
        {
            Secret = _settings.Secret,
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            AccessTokenExpirationHours = 0,
            RefreshTokenExpirationDays = 7
        };
        var sut = new JwtService(Options.Create(shortLived));
        var token = sut.GenerateAccessToken(SampleUser());

        Thread.Sleep(50);

        sut.ValidateAccessToken(token).Should().BeNull();
    }

    [Fact]
    public void GenerateRefreshToken_returns_unique_non_empty_string()
    {
        var a = _sut.GenerateRefreshToken();
        var b = _sut.GenerateRefreshToken();

        a.Should().NotBeNullOrWhiteSpace();
        a.Should().NotBe(b);
    }
}
```

### Step 2: Implementação (GREEN)

Criar `MissionClear.Api/Services/JwtService.cs`:

```csharp
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MissionClear.Api.Configuration;
using MissionClear.Api.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace MissionClear.Api.Services;

public class JwtService
{
    private readonly JwtSettings _settings;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
        if (string.IsNullOrWhiteSpace(_settings.Secret) || _settings.Secret.Length < 32)
            throw new InvalidOperationException("Jwt.Secret deve ter pelo menos 32 caracteres.");
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
    }

    public string GenerateAccessToken(UserEntity user)
    {
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim("email", user.Email),
            new Claim("name", user.DisplayName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddHours(_settings.AccessTokenExpirationHours),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return _handler.WriteToken(token);
    }

    public string GenerateRefreshToken() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ClockSkew = TimeSpan.Zero
        };

        try { return _handler.ValidateToken(token, parameters, out _); }
        catch { return null; }
    }

    public string? GetUserIdFromToken(ClaimsPrincipal principal) =>
        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public int AccessTokenExpirationSeconds => _settings.AccessTokenExpirationHours * 3600;
    public int RefreshTokenExpirationDays => _settings.RefreshTokenExpirationDays;
}
```

### Step 3: Commit

```bash
git add MissionClear.Api/Services/JwtService.cs MissionClear.Tests/Services/JwtServiceTests.cs
git commit -m "feat(auth): JwtService com geração/validação HMAC-SHA256 e refresh token GUID"
```

---

## Task 4.2 — AuthService (Register + Login + Refresh + Logout)

**Files:**
- Create: `MissionClear.Api/Services/AuthService.cs`
- Create: `MissionClear.Tests/Services/AuthServiceTests.cs`

### Step 1: Testes (RED)

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Data;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Models.Dtos.Auth;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public class AuthServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuthService _sut;
    private readonly JwtSettings _settings = new()
    {
        Secret = "test-secret-key-with-at-least-32-characters-long-for-hmac",
        Issuer = "mission-clear-api-test",
        Audience = "mission-clear-mobile-test",
        AccessTokenExpirationHours = 1,
        RefreshTokenExpirationDays = 7
    };

    public AuthServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(opts);
        var jwt = new JwtService(Options.Create(_settings));
        _sut = new AuthService(_db, jwt, Options.Create(_settings), NullLogger<AuthService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static AuthRegisterRequest ValidRegister() => new()
    {
        Email = "piloto@missionclear.app",
        Password = "MinhaSenh@123",
        DisplayName = "Piloto Guss"
    };

    [Fact]
    public async Task RegisterAsync_creates_user_and_returns_tokens()
    {
        var result = await _sut.RegisterAsync(ValidRegister());

        result.User.Id.Should().StartWith("usr_");
        result.User.Email.Should().Be("piloto@missionclear.app");
        result.User.TotalMissions.Should().Be(0);
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.ExpiresIn.Should().Be(3600);

        var inDb = await _db.Users.FirstAsync();
        inDb.PasswordHash.Should().NotBe("MinhaSenh@123");
        BCrypt.Net.BCrypt.Verify("MinhaSenh@123", inDb.PasswordHash).Should().BeTrue();

        (await _db.RefreshTokens.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RegisterAsync_throws_when_email_already_exists()
    {
        await _sut.RegisterAsync(ValidRegister());

        var act = () => _sut.RegisterAsync(ValidRegister());

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "EMAIL_ALREADY_EXISTS" && e.StatusCode == 409);
    }

    [Theory]
    [InlineData("short1A")]
    [InlineData("nouppercase123")]
    [InlineData("NoDigitsHere")]
    public async Task RegisterAsync_throws_when_password_invalid(string weak)
    {
        var dto = new AuthRegisterRequest { Email = "x@y.com", Password = weak, DisplayName = "X" };

        await ((Func<Task>)(() => _sut.RegisterAsync(dto)))
            .Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_PASSWORD_FORMAT");
    }

    [Fact]
    public async Task LoginAsync_with_correct_credentials_returns_tokens()
    {
        await _sut.RegisterAsync(ValidRegister());

        var result = await _sut.LoginAsync(new AuthLoginRequest
        {
            Email = "piloto@missionclear.app",
            Password = "MinhaSenh@123"
        });

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        (await _db.RefreshTokens.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task LoginAsync_throws_when_email_not_found()
    {
        var act = () => _sut.LoginAsync(new AuthLoginRequest { Email = "ghost@x.com", Password = "Whatever1A" });

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CREDENTIALS" && e.StatusCode == 401);
    }

    [Fact]
    public async Task LoginAsync_throws_when_password_wrong()
    {
        await _sut.RegisterAsync(ValidRegister());

        var act = () => _sut.LoginAsync(new AuthLoginRequest
        {
            Email = "piloto@missionclear.app",
            Password = "WrongPass1A"
        });

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task RefreshAsync_with_valid_token_returns_new_access_token()
    {
        var reg = await _sut.RegisterAsync(ValidRegister());

        var result = await _sut.RefreshAsync(reg.RefreshToken);

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.ExpiresIn.Should().Be(3600);
    }

    [Fact]
    public async Task RefreshAsync_throws_when_token_revoked()
    {
        var reg = await _sut.RegisterAsync(ValidRegister());
        await _sut.LogoutAsync(reg.RefreshToken, reg.User.Id);

        var act = () => _sut.RefreshAsync(reg.RefreshToken);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_REFRESH_TOKEN" && e.StatusCode == 401);
    }

    [Fact]
    public async Task LogoutAsync_marks_token_as_revoked()
    {
        var reg = await _sut.RegisterAsync(ValidRegister());

        await _sut.LogoutAsync(reg.RefreshToken, reg.User.Id);

        var stored = await _db.RefreshTokens.FirstAsync();
        stored.IsRevoked.Should().BeTrue();
    }
}
```

### Step 2: Implementação (GREEN)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Data;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Models.Dtos.Auth;

namespace MissionClear.Api.Services;

public class AuthService
{
    private const int BcryptWorkFactor = 12;

    private readonly AppDbContext _db;
    private readonly JwtService _jwt;
    private readonly JwtSettings _settings;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AppDbContext db, JwtService jwt, IOptions<JwtSettings> settings, ILogger<AuthService> logger)
    {
        _db = db;
        _jwt = jwt;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(AuthRegisterRequest dto, CancellationToken ct = default)
    {
        if (!IsValidPassword(dto.Password))
            throw new DomainException("INVALID_PASSWORD_FORMAT",
                "Senha deve ter no mínimo 8 caracteres, 1 maiúscula e 1 dígito.", 400);

        var email = dto.Email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            throw new DomainException("EMAIL_ALREADY_EXISTS", "Email já está cadastrado.", 409);

        var user = new UserEntity
        {
            Id = "usr_" + Guid.NewGuid().ToString("N"),
            Email = email,
            DisplayName = dto.DisplayName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, BcryptWorkFactor),
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);

        var accessToken = _jwt.GenerateAccessToken(user);
        var refreshToken = _jwt.GenerateRefreshToken();
        _db.RefreshTokens.Add(NewRefreshToken(refreshToken, user.Id));

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Usuário registrado {UserId}", user.Id);

        return BuildAuthResponse(user, accessToken, refreshToken, totalMissions: 0, bestScore: 0);
    }

    public async Task<AuthResponse> LoginAsync(AuthLoginRequest dto, CancellationToken ct = default)
    {
        var email = dto.Email.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            throw new DomainException("INVALID_CREDENTIALS", "Email ou senha inválidos.", 401);

        var accessToken = _jwt.GenerateAccessToken(user);
        var refreshToken = _jwt.GenerateRefreshToken();
        _db.RefreshTokens.Add(NewRefreshToken(refreshToken, user.Id));

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Login bem-sucedido {UserId}", user.Id);

        var stats = await GetUserStatsAsync(user.Id, ct);
        return BuildAuthResponse(user, accessToken, refreshToken, stats.totalMissions, stats.bestScore);
    }

    public async Task<AuthRefreshResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new DomainException("INVALID_REFRESH_TOKEN", "Refresh token inválido.", 401);

        var now = DateTime.UtcNow;
        var stored = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken && !rt.IsRevoked && rt.ExpiresAt > now, ct);

        if (stored?.User is null)
            throw new DomainException("INVALID_REFRESH_TOKEN", "Refresh token inválido, expirado ou revogado.", 401);

        return new AuthRefreshResponse
        {
            AccessToken = _jwt.GenerateAccessToken(stored.User),
            RefreshToken = refreshToken,
            ExpiresIn = _jwt.AccessTokenExpirationSeconds
        };
    }

    public async Task LogoutAsync(string refreshToken, string userId, CancellationToken ct = default)
    {
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken && rt.UserId == userId, ct);

        if (stored is null) { _logger.LogWarning("Logout para token desconhecido {UserId}", userId); return; }

        stored.IsRevoked = true;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Logout: refresh token revogado {UserId}", userId);
    }

    public async Task<(int totalMissions, int bestScore)> GetUserStatsAsync(string userId, CancellationToken ct = default)
    {
        var stats = await _db.Missions
            .Where(m => m.UserId == userId)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Best = g.Max(m => (int?)m.MissionScore) ?? 0 })
            .FirstOrDefaultAsync(ct);

        return (stats?.Total ?? 0, stats?.Best ?? 0);
    }

    private RefreshTokenEntity NewRefreshToken(string token, string userId) => new()
    {
        Id = "rtk_" + Guid.NewGuid().ToString("N"),
        Token = token,
        UserId = userId,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpirationDays),
        IsRevoked = false
    };

    private AuthResponse BuildAuthResponse(UserEntity user, string accessToken, string refreshToken, int totalMissions, int bestScore) =>
        new()
        {
            User = new AuthUserSummaryDto
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                CreatedAt = user.CreatedAt,
                TotalMissions = totalMissions,
                BestScore = bestScore
            },
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _jwt.AccessTokenExpirationSeconds
        };

    private static bool IsValidPassword(string password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length >= 8 &&
        password.Any(char.IsUpper) &&
        password.Any(char.IsDigit);
}

public sealed record AuthRefreshResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }
}
```

### Step 3: Commit

```bash
git add .
git commit -m "feat(auth): AuthService com register/login/refresh/logout + BCrypt e refresh tokens persistidos"
```

---

## Task 4.3 — UserService (perfil + estatísticas)

### Step 1: Testes (RED)

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MissionClear.Api.Data;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Models.Dtos.User;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public class UserServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly UserService _sut;

    public UserServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(opts);
        _sut = new UserService(_db, NullLogger<UserService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<UserEntity> SeedUserAsync(string password = "MinhaSenh@123")
    {
        var user = new UserEntity
        {
            Id = "usr_test01",
            Email = "piloto@missionclear.app",
            DisplayName = "Piloto Guss",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 12),
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task SeedMissionsAsync(string userId)
    {
        _db.Missions.AddRange(
            new MissionEntity { Id = "msn_1", UserId = userId, Destination = "ISS", Status = "success", MissionScore = 90, DeltaVKmS = 9.4, CreatedAt = DateTime.UtcNow, DepartureTime = DateTime.UtcNow, ArrivalTime = DateTime.UtcNow.AddHours(6), ObstaclesJson = "[]", ScoreBreakdownJson = "{}" },
            new MissionEntity { Id = "msn_2", UserId = userId, Destination = "ISS", Status = "success", MissionScore = 97, DeltaVKmS = 9.4, CreatedAt = DateTime.UtcNow, DepartureTime = DateTime.UtcNow, ArrivalTime = DateTime.UtcNow.AddHours(6), ObstaclesJson = "[]", ScoreBreakdownJson = "{}" },
            new MissionEntity { Id = "msn_3", UserId = userId, Destination = "SSO", Status = "failure", MissionScore = 40, DeltaVKmS = 10.1, CreatedAt = DateTime.UtcNow, DepartureTime = DateTime.UtcNow, ArrivalTime = DateTime.UtcNow.AddHours(6), ObstaclesJson = "[]", ScoreBreakdownJson = "{}" },
            new MissionEntity { Id = "msn_4", UserId = userId, Destination = "ISS", Status = "aborted", MissionScore = 0, DeltaVKmS = 0.0, CreatedAt = DateTime.UtcNow, DepartureTime = DateTime.UtcNow, ArrivalTime = DateTime.UtcNow.AddHours(6), ObstaclesJson = "[]", ScoreBreakdownJson = "{}" }
        );
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetMeAsync_returns_user_with_aggregated_stats()
    {
        var user = await SeedUserAsync();
        await SeedMissionsAsync(user.Id);

        var result = await _sut.GetMeAsync(user.Id);

        result.Stats.TotalMissions.Should().Be(4);
        result.Stats.SuccessfulMissions.Should().Be(2);
        result.Stats.FailedMissions.Should().Be(1);
        result.Stats.AbortedMissions.Should().Be(1);
        result.Stats.SuccessRate.Should().BeApproximately(0.5, 0.001);
        result.Stats.BestScore.Should().Be(97);
        result.Stats.FavoriteDestination.Should().Be("ISS");
        result.Stats.TotalDeltaVKmS.Should().BeApproximately(28.9, 0.001);
    }

    [Fact]
    public async Task GetMeAsync_returns_zero_stats_when_no_missions()
    {
        var user = await SeedUserAsync();

        var result = await _sut.GetMeAsync(user.Id);

        result.Stats.TotalMissions.Should().Be(0);
        result.Stats.FavoriteDestination.Should().BeNull();
    }

    [Fact]
    public async Task GetMeAsync_throws_when_user_not_found()
    {
        await ((Func<Task>)(() => _sut.GetMeAsync("usr_ghost")))
            .Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "USER_NOT_FOUND" && e.StatusCode == 404);
    }

    [Fact]
    public async Task UpdateMeAsync_updates_display_name()
    {
        var user = await SeedUserAsync();

        var result = await _sut.UpdateMeAsync(user.Id, new UpdateUserRequest { DisplayName = "Novo Nome" });

        result.DisplayName.Should().Be("Novo Nome");
    }

    [Fact]
    public async Task UpdateMeAsync_updates_password_when_current_valid()
    {
        var user = await SeedUserAsync("MinhaSenh@123");

        await _sut.UpdateMeAsync(user.Id, new UpdateUserRequest
        {
            DisplayName = "unchanged",
            CurrentPassword = "MinhaSenh@123",
            NewPassword = "NovaSenha@456"
        });

        var stored = await _db.Users.FirstAsync();
        BCrypt.Net.BCrypt.Verify("NovaSenha@456", stored.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateMeAsync_throws_when_current_password_wrong()
    {
        var user = await SeedUserAsync();

        await ((Func<Task>)(() => _sut.UpdateMeAsync(user.Id, new UpdateUserRequest
        {
            DisplayName = "unchanged",
            CurrentPassword = "WrongOne1A",
            NewPassword = "NovaSenha@456"
        }))).Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CURRENT_PASSWORD");
    }
}
```

### Step 2: Implementação (GREEN)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MissionClear.Api.Data;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Models.Dtos.User;

namespace MissionClear.Api.Services;

public class UserService
{
    private const int BcryptWorkFactor = 12;

    private readonly AppDbContext _db;
    private readonly ILogger<UserService> _logger;

    public UserService(AppDbContext db, ILogger<UserService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<UserResponse> GetMeAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new DomainException("USER_NOT_FOUND", "Usuário não encontrado.", 404);

        var stats = await ComputeStatsAsync(userId, ct);
        return MapToDto(user, stats);
    }

    public async Task<UserResponse> UpdateMeAsync(string userId, UpdateUserRequest dto, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new DomainException("USER_NOT_FOUND", "Usuário não encontrado.", 404);

        if (!string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
                throw new DomainException("MISSING_PARAMETER", "current_password obrigatório ao trocar senha.", 400);

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                throw new DomainException("INVALID_CURRENT_PASSWORD", "Senha atual incorreta.", 401);

            if (!IsValidPassword(dto.NewPassword))
                throw new DomainException("INVALID_PASSWORD_FORMAT", "Senha deve ter no mínimo 8 caracteres, 1 maiúscula e 1 dígito.", 400);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, BcryptWorkFactor);
        }

        if (!string.IsNullOrWhiteSpace(dto.DisplayName))
            user.DisplayName = dto.DisplayName.Trim();

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Perfil atualizado {UserId}", userId);

        var stats = await ComputeStatsAsync(userId, ct);
        return MapToDto(user, stats);
    }

    private async Task<UserStatsDto> ComputeStatsAsync(string userId, CancellationToken ct)
    {
        var missions = await _db.Missions
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.Destination, m.Status, m.MissionScore, m.DeltaVKmS })
            .ToListAsync(ct);

        if (missions.Count == 0)
            return new UserStatsDto();

        var total = missions.Count;
        var successful = missions.Count(m => m.Status == "success");
        var failed = missions.Count(m => m.Status == "failure");
        var aborted = missions.Count(m => m.Status == "aborted");

        var favorite = missions
            .GroupBy(m => m.Destination)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First().Key;

        return new UserStatsDto
        {
            TotalMissions = total,
            SuccessfulMissions = successful,
            FailedMissions = failed,
            AbortedMissions = aborted,
            SuccessRate = (double)successful / total,
            BestScore = missions.Max(m => m.MissionScore),
            AverageScore = missions.Average(m => m.MissionScore),
            FavoriteDestination = favorite,
            TotalDeltaVKmS = Math.Round(missions.Sum(m => m.DeltaVKmS), 3)
        };
    }

    private static UserResponse MapToDto(UserEntity user, UserStatsDto stats) => new()
    {
        Id = user.Id,
        Email = user.Email,
        DisplayName = user.DisplayName,
        CreatedAt = user.CreatedAt,
        Stats = stats
    };

    private static bool IsValidPassword(string password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length >= 8 &&
        password.Any(char.IsUpper) &&
        password.Any(char.IsDigit);
}
```

**Nota:** `UpdateUserRequest` em plan-02 tem `DisplayName`, mas troca de senha precisa de `CurrentPassword` e `NewPassword`. Atualizar DTO com esses campos se necessário.

### Step 3: Commit

```bash
git add .
git commit -m "feat(user): UserService com perfil + estatísticas agregadas e troca de senha"
```

---

## Task 4.4 — DI + Middleware JWT Bearer no Program.cs

```csharp
// appsettings.Development.json — acrescentar bloco Jwt:
{
  "Jwt": {
    "Secret": "dev-only-secret-key-with-at-least-32-characters-long",
    "Issuer": "mission-clear-api",
    "Audience": "mission-clear-mobile",
    "AccessTokenExpirationHours": 1,
    "RefreshTokenExpirationDays": 7
  }
}
```

```csharp
// Program.cs — seção de DI:
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção Jwt ausente em appsettings.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret) || jwtSettings.Secret.Length < 32)
    throw new InvalidOperationException("Jwt.Secret deve ter pelo menos 32 caracteres. Use env var Jwt__Secret.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "name"
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserService>();

// Pipeline (antes de MapControllers):
app.UseAuthentication();
app.UseAuthorization();
```

### Step: Commit

```bash
git add .
git commit -m "feat(auth): registrar JwtService/AuthService/UserService e middleware JWT Bearer"
```

---

## Success Criteria

- [ ] `JwtService` gera, valida, expira e extrai `sub` corretamente.
- [ ] `AuthService.RegisterAsync` rejeita email duplicado (409) e senha fraca (400).
- [ ] `AuthService.LoginAsync` retorna 401 genérico para email OU senha errados (sem leak de existência).
- [ ] `AuthService.RefreshAsync` rejeita tokens revogados, expirados e desconhecidos.
- [ ] `AuthService.LogoutAsync` marca `IsRevoked = true` sem deletar.
- [ ] `UserService.GetMeAsync` retorna stats agregadas corretas.
- [ ] `UserService.UpdateMeAsync` exige `current_password` ao trocar senha.
- [ ] Senha plaintext nunca é armazenada — apenas hash BCrypt (workFactor 12).
- [ ] Middleware JWT Bearer ativo, `UseAuthentication()` antes de `UseAuthorization()`.
- [ ] `dotnet test` 100% verde nos três serviços.

## Risks & Mitigations

| Risco | Mitigação |
|---|---|
| `Jwt.Secret` fraco em produção | Validação no startup (≥ 32 chars); env var `Jwt__Secret` obrigatória |
| BCrypt workFactor 12 lento em CI | Aceitável (~250 ms por hash); reduzir para 10 só em testes se necessário |
| Refresh token vazado | Tokens têm `ExpiresAt` (7 dias) e podem ser revogados em logout |
| email case-sensitive duplicado | Normalizado para `ToLowerInvariant()` antes de gravar/buscar |
| Race em `RefreshAsync` | `ClockSkew = TimeSpan.Zero` + filtro `rt.ExpiresAt > now` na query |
